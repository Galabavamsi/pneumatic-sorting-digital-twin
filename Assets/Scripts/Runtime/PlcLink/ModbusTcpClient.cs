using System;
using System.IO;
using System.Net.Sockets;

namespace Kit1DigitalTwin.PlcLink
{
    public sealed class ModbusException : Exception
    {
        public ModbusException(string message) : base(message)
        {
        }
    }

    // Minimal, dependency-free Modbus TCP client. Read-only by design: it only
    // implements function 01 (read coils) and 03 (read holding registers), so the
    // twin cannot move the machine. Not thread-safe; use one instance per thread.
    public sealed class ModbusTcpClient : IDisposable
    {
        private const byte FunctionReadCoils = 0x01;
        private const byte FunctionReadHoldingRegisters = 0x03;

        private readonly string host;
        private readonly int port;
        private readonly byte unitId;
        private readonly int timeoutMs;
        private TcpClient client;
        private NetworkStream stream;
        private ushort transactionId;

        public ModbusTcpClient(string host, int port = 502, byte unitId = 1, int timeoutMs = 1000)
        {
            this.host = host;
            this.port = port;
            this.unitId = unitId;
            this.timeoutMs = timeoutMs;
        }

        public bool Connected => client != null && client.Connected && stream != null;

        public void Connect()
        {
            Close();
            client = new TcpClient { NoDelay = true, ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
            IAsyncResult pending = client.BeginConnect(host, port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                Close();
                throw new ModbusException($"Timed out connecting to {host}:{port}.");
            }

            client.EndConnect(pending);
            stream = client.GetStream();
        }

        public bool[] ReadCoils(ushort start, ushort count)
        {
            if (count < 1 || count > 2000)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            byte[] payload = Transact(FunctionReadCoils, start, count);
            int expectedBytes = (count + 7) / 8;
            if (payload.Length < 1 || payload[0] != expectedBytes || payload.Length < 1 + expectedBytes)
            {
                throw new ModbusException("Malformed read-coils response.");
            }

            bool[] values = new bool[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = (payload[1 + index / 8] & (1 << (index % 8))) != 0;
            }

            return values;
        }

        public ushort[] ReadHoldingRegisters(ushort start, ushort count)
        {
            if (count < 1 || count > 125)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            byte[] payload = Transact(FunctionReadHoldingRegisters, start, count);
            if (payload.Length < 1 || payload[0] != count * 2 || payload.Length < 1 + count * 2)
            {
                throw new ModbusException("Malformed read-holding-registers response.");
            }

            ushort[] values = new ushort[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = (ushort)((payload[1 + index * 2] << 8) | payload[2 + index * 2]);
            }

            return values;
        }

        private byte[] Transact(byte function, ushort start, ushort count)
        {
            if (!Connected)
            {
                throw new ModbusException("Not connected.");
            }

            transactionId++;
            byte[] request =
            {
                (byte)(transactionId >> 8), (byte)transactionId,
                0, 0,          // protocol id
                0, 6,          // length: unit + function + 4 data bytes
                unitId,
                function,
                (byte)(start >> 8), (byte)start,
                (byte)(count >> 8), (byte)count
            };
            stream.Write(request, 0, request.Length);

            byte[] header = ReadExactly(7);
            ushort responseTransaction = (ushort)((header[0] << 8) | header[1]);
            int length = (header[4] << 8) | header[5];
            if (responseTransaction != transactionId)
            {
                throw new ModbusException(
                    $"Transaction mismatch: sent {transactionId}, got {responseTransaction}.");
            }

            if (length < 2 || length > 260)
            {
                throw new ModbusException($"Invalid MBAP length {length}.");
            }

            byte[] body = ReadExactly(length - 1);
            byte responseFunction = body[0];
            if (responseFunction == (function | 0x80))
            {
                byte code = body.Length > 1 ? body[1] : (byte)0;
                throw new ModbusException($"PLC returned Modbus exception {code} for function {function}.");
            }

            if (responseFunction != function)
            {
                throw new ModbusException($"Unexpected function {responseFunction} in response.");
            }

            byte[] payload = new byte[body.Length - 1];
            Array.Copy(body, 1, payload, 0, payload.Length);
            return payload;
        }

        private byte[] ReadExactly(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new IOException("PLC closed the connection.");
                }

                offset += read;
            }

            return buffer;
        }

        public void Close()
        {
            stream?.Dispose();
            client?.Close();
            stream = null;
            client = null;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
