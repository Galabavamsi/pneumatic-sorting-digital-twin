using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Kit1PlcSimulator
{
    // Small Modbus TCP server exposing a coil table and a holding-register table.
    // Supports FC01 read coils, FC03 read holding registers, FC05 write single
    // coil (only for coils marked writable, i.e. the HMI command coils).
    public sealed class ModbusTcpServer : IDisposable
    {
        // A client that sends nothing for this long is dropped, and at most
        // MaxClients are served at once, so idle or stuck sockets cannot pile up.
        private const int IdleTimeoutMs = 30000;
        private const int MaxClients = 8;

        private readonly object sync;
        private readonly bool[] coils;
        private readonly ushort[] registers;
        private readonly Func<int, bool> coilWritable;
        private readonly TcpListener listener;
        private readonly List<TcpClient> clients = new List<TcpClient>();
        private Thread acceptThread;
        private volatile bool running;

        public ModbusTcpServer(
            IPAddress address, int port, object sync, bool[] coils, ushort[] registers,
            Func<int, bool> coilWritable)
        {
            this.sync = sync;
            this.coils = coils;
            this.registers = registers;
            this.coilWritable = coilWritable;
            listener = new TcpListener(address, port);
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public int RequestsServed;

        public void Start()
        {
            listener.Start();
            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Modbus accept" };
            acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                lock (clients)
                {
                    if (clients.Count >= MaxClients)
                    {
                        client.Close();
                        continue;
                    }

                    clients.Add(client);
                }

                Thread worker = new Thread(() => ServeClient(client))
                {
                    IsBackground = true,
                    Name = "Modbus client"
                };
                worker.Start();
            }
        }

        private void ServeClient(TcpClient client)
        {
            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = IdleTimeoutMs;   // Read throws IOException when idle
                client.SendTimeout = IdleTimeoutMs;
                NetworkStream stream = client.GetStream();
                byte[] header = new byte[7];
                while (running)
                {
                    if (!ReadExactly(stream, header, 7))
                    {
                        return;
                    }

                    int length = (header[4] << 8) | header[5];
                    if (length < 2 || length > 260)
                    {
                        return;
                    }

                    byte[] pdu = new byte[length - 1];
                    if (!ReadExactly(stream, pdu, pdu.Length))
                    {
                        return;
                    }

                    byte[] responsePdu = Handle(pdu);
                    byte[] response = new byte[7 + responsePdu.Length];
                    response[0] = header[0];
                    response[1] = header[1];
                    response[2] = 0;
                    response[3] = 0;
                    response[4] = (byte)((responsePdu.Length + 1) >> 8);
                    response[5] = (byte)(responsePdu.Length + 1);
                    response[6] = header[6];
                    Array.Copy(responsePdu, 0, response, 7, responsePdu.Length);
                    stream.Write(response, 0, response.Length);
                    Interlocked.Increment(ref RequestsServed);
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                lock (clients)
                {
                    clients.Remove(client);
                }

                client.Close();
            }
        }

        private byte[] Handle(byte[] pdu)
        {
            byte function = pdu[0];
            if (pdu.Length < 5)
            {
                return Exception(function, 3);
            }

            int address = (pdu[1] << 8) | pdu[2];
            int value = (pdu[3] << 8) | pdu[4];

            lock (sync)
            {
                switch (function)
                {
                    case 0x01:
                    {
                        if (value < 1 || value > 2000 || address + value > coils.Length)
                        {
                            return Exception(function, 2);
                        }

                        int byteCount = (value + 7) / 8;
                        byte[] response = new byte[2 + byteCount];
                        response[0] = function;
                        response[1] = (byte)byteCount;
                        for (int index = 0; index < value; index++)
                        {
                            if (coils[address + index])
                            {
                                response[2 + index / 8] |= (byte)(1 << (index % 8));
                            }
                        }

                        return response;
                    }

                    case 0x03:
                    {
                        if (value < 1 || value > 125 || address + value > registers.Length)
                        {
                            return Exception(function, 2);
                        }

                        byte[] response = new byte[2 + value * 2];
                        response[0] = function;
                        response[1] = (byte)(value * 2);
                        for (int index = 0; index < value; index++)
                        {
                            response[2 + index * 2] = (byte)(registers[address + index] >> 8);
                            response[3 + index * 2] = (byte)registers[address + index];
                        }

                        return response;
                    }

                    case 0x05:
                    {
                        if (address >= coils.Length || !coilWritable(address))
                        {
                            return Exception(function, 2);
                        }

                        if (value != 0xFF00 && value != 0x0000)
                        {
                            return Exception(function, 3);
                        }

                        coils[address] = value == 0xFF00;
                        byte[] echo = new byte[5];
                        Array.Copy(pdu, echo, 5);
                        return echo;
                    }

                    default:
                        return Exception(function, 1);
                }
            }
        }

        private static byte[] Exception(byte function, byte code)
        {
            return new[] { (byte)(function | 0x80), code };
        }

        private static bool ReadExactly(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        public void Dispose()
        {
            running = false;
            listener.Stop();
            lock (clients)
            {
                foreach (TcpClient client in clients)
                {
                    client.Close();
                }

                clients.Clear();
            }
        }
    }
}
