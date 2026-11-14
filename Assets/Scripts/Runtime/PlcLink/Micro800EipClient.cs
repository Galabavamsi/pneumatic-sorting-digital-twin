using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Kit1DigitalTwin.PlcLink
{
    public sealed class EipException : Exception
    {
        public EipException(string message) : base(message)
        {
        }
    }

    // Minimal, dependency-free EtherNet/IP (CIP) client for Micro800 controllers.
    // Read-only by design: it only implements "Read Tag" (service 0x4C), so the
    // twin cannot move the machine. Global variables are read by name, which needs
    // no Modbus mapping or any other set-up in Connected Components Workbench.
    //
    // The controller is found with the standard List Identity broadcast, because a
    // Micro850 left on DHCP gives itself a new 169.254.x.x address from time to time.
    // Not thread-safe; use one instance per thread.
    public sealed class Micro800EipClient : IDisposable
    {
        public const int EipPort = 44818;
        public const string AutoHost = "auto";

        public struct Identity
        {
            public string Address;        // the controller
            public string LocalAddress;   // the PC address that heard it
            public string ProductName;
        }

        private readonly string host;
        private readonly int timeoutMs;
        // Dispose() may be called from another thread to abort a poll; the gate
        // keeps it from racing Connect(), and a disposed client never reconnects.
        private readonly object gate = new object();
        private TcpClient client;
        private NetworkStream stream;
        private bool disposed;
        private uint session;
        private uint connectionId;
        private ushort sequence;

        public Micro800EipClient(string host, int timeoutMs = 1000)
        {
            this.host = host;
            this.timeoutMs = timeoutMs;
        }

        // Filled in by Connect().
        public string RemoteAddress { get; private set; }
        public string ProductName { get; private set; }

        public static bool IsAuto(string host)
        {
            return string.IsNullOrWhiteSpace(host) ||
                string.Equals(host.Trim(), AutoHost, StringComparison.OrdinalIgnoreCase);
        }

        public bool Connected
        {
            get
            {
                lock (gate)
                {
                    return !disposed && client != null && client.Connected && stream != null && connectionId != 0;
                }
            }
        }

        public void Connect()
        {
            Close();
            ThrowIfDisposed();

            string target = host;
            string local = null;
            string product = null;
            if (IsAuto(host))
            {
                Identity? found = null;
                foreach (Identity identity in Discover(600))
                {
                    if (identity.ProductName.StartsWith("2080-", StringComparison.OrdinalIgnoreCase))
                    {
                        found = identity;
                        break;
                    }
                }

                if (!found.HasValue)
                {
                    throw new EipException(
                        "No Micro800 answered on the network. Is the panel on and the cable plugged in?");
                }

                target = found.Value.Address;
                local = found.Value.LocalAddress;
                product = found.Value.ProductName;
            }

            // Binding to the PC address that heard the controller makes Windows use
            // that adapter even when another one shares the 169.254.x.x range.
            TcpClient candidate = local != null
                ? new TcpClient(new IPEndPoint(IPAddress.Parse(local), 0))
                : new TcpClient();
            candidate.NoDelay = true;
            candidate.ReceiveTimeout = timeoutMs;
            candidate.SendTimeout = timeoutMs;
            try
            {
                ThrowIfDisposed();
                IAsyncResult pending = candidate.BeginConnect(target, EipPort, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    throw new EipException($"Timed out connecting to {target}:{EipPort}.");
                }

                candidate.EndConnect(pending);
                lock (gate)
                {
                    if (disposed)
                    {
                        throw new ObjectDisposedException(nameof(Micro800EipClient));
                    }

                    client = candidate;
                    stream = candidate.GetStream();
                    candidate = null;   // ownership passed to the fields
                }

                RegisterSession();
                ForwardOpen();
                RemoteAddress = target;
                ProductName = product;
            }
            catch
            {
                Close();
                throw;
            }
            finally
            {
                candidate?.Close();     // timed out, failed, or disposed meanwhile
            }
        }

        public bool ReadBool(string variable)
        {
            ushort type;
            int offset;
            byte[] reply = ReadTag(variable, out type, out offset);
            if (type != 0x00C1)
            {
                throw new EipException($"'{variable}' is not a BOOL (CIP type 0x{type:X4}).");
            }

            return reply[offset] != 0;
        }

        public int ReadInt(string variable)
        {
            ushort type;
            int offset;
            byte[] reply = ReadTag(variable, out type, out offset);
            switch (type)
            {
                case 0x00C1: return reply[offset] != 0 ? 1 : 0;
                case 0x00C2: return (sbyte)reply[offset];
                case 0x00C3: return BitConverter.ToInt16(reply, offset);
                case 0x00C4: return BitConverter.ToInt32(reply, offset);
                case 0x00C6: return reply[offset];
                case 0x00C7: return BitConverter.ToUInt16(reply, offset);
                case 0x00C8: return unchecked((int)BitConverter.ToUInt32(reply, offset));
                default:
                    throw new EipException($"'{variable}' is not an integer (CIP type 0x{type:X4}).");
            }
        }

        // Broadcasts List Identity on every active IPv4 adapter and returns whoever answers.
        public static List<Identity> Discover(int waitPerAdapterMs)
        {
            List<Identity> results = new List<Identity>();
            HashSet<string> seen = new HashSet<string>();
            byte[] request = new byte[24];
            request[0] = 0x63;

            NetworkInterface[] adapters;
            try
            {
                adapters = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                return results;
            }

            foreach (NetworkInterface adapter in adapters)
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    try
                    {
                        using (UdpClient udp = new UdpClient(new IPEndPoint(info.Address, 0)))
                        {
                            udp.EnableBroadcast = true;
                            udp.Client.ReceiveTimeout = 200;
                            udp.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, EipPort));
                            IPAddress directed = DirectedBroadcast(info);
                            if (directed != null)
                            {
                                udp.Send(request, request.Length, new IPEndPoint(directed, EipPort));
                            }

                            Stopwatch waited = Stopwatch.StartNew();
                            while (waited.ElapsedMilliseconds < waitPerAdapterMs)
                            {
                                try
                                {
                                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                                    byte[] reply = udp.Receive(ref from);
                                    if (reply.Length > 63 && seen.Add(from.Address.ToString()))
                                    {
                                        int nameLength = Math.Min(reply[62], reply.Length - 63);
                                        results.Add(new Identity
                                        {
                                            Address = from.Address.ToString(),
                                            LocalAddress = info.Address.ToString(),
                                            ProductName = Encoding.ASCII.GetString(reply, 63, nameLength)
                                        });
                                    }
                                }
                                catch (SocketException)
                                {
                                    // receive timeout: keep waiting until the window closes
                                }
                            }
                        }
                    }
                    catch (SocketException)
                    {
                        // this adapter cannot broadcast; try the next one
                    }
                }
            }

            return results;
        }

        private static IPAddress DirectedBroadcast(UnicastIPAddressInformation info)
        {
            try
            {
                IPAddress mask = info.IPv4Mask;
                if (mask == null)
                {
                    return null;
                }

                byte[] address = info.Address.GetAddressBytes();
                byte[] maskBytes = mask.GetAddressBytes();
                byte[] broadcast = new byte[4];
                for (int index = 0; index < 4; index++)
                {
                    broadcast[index] = (byte)(address[index] | ~maskBytes[index]);
                }

                return new IPAddress(broadcast);
            }
            catch (NotImplementedException)
            {
                return null;   // some runtimes do not expose the mask
            }
        }

        private void ThrowIfDisposed()
        {
            lock (gate)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(Micro800EipClient));
                }
            }
        }

        private void RegisterSession()
        {
            byte[] reply = Encapsulated(0x65, 0, new byte[] { 1, 0, 0, 0 });
            session = BitConverter.ToUInt32(reply, 4);
            if (session == 0)
            {
                throw new EipException("The controller refused the EtherNet/IP session.");
            }
        }

        // Class 3 connection to the message router. Micro800 answers tag reads on a
        // connection; no routing path is used.
        private void ForwardOpen()
        {
            Random random = new Random();
            MemoryStream buffer = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(buffer);
            writer.Write((byte)0x54);
            writer.Write((byte)2);
            writer.Write(new byte[] { 0x20, 0x06, 0x24, 0x01 });
            writer.Write((byte)0x0A);
            writer.Write((byte)0x0E);
            writer.Write((uint)0);                                  // O->T id: the controller picks it
            writer.Write((uint)random.Next(1, int.MaxValue));       // T->O id
            writer.Write((ushort)random.Next(1, 0xFFFF));           // connection serial
            writer.Write((ushort)0x1337);                           // originator vendor
            writer.Write((uint)0x4B495431);                         // originator serial
            writer.Write((byte)1);                                  // timeout multiplier
            writer.Write(new byte[3]);
            writer.Write((uint)2000000);
            writer.Write((ushort)0x43F4);
            writer.Write((uint)2000000);
            writer.Write((ushort)0x43F4);
            writer.Write((byte)0xA3);
            writer.Write((byte)2);
            writer.Write(new byte[] { 0x20, 0x02, 0x24, 0x01 });

            byte[] reply = SendUnconnected(buffer.ToArray());
            if (reply.Length < 8 || reply[2] != 0)
            {
                throw new EipException($"The controller refused the connection (CIP status 0x{StatusOf(reply):X2}).");
            }

            connectionId = BitConverter.ToUInt32(reply, 4 + 2 * reply[3]);
            sequence = 0;
        }

        private static int StatusOf(byte[] reply)
        {
            return reply.Length > 2 ? reply[2] : 0xFF;
        }

        private byte[] ReadTag(string variable, out ushort type, out int offset)
        {
            byte[] name = Encoding.ASCII.GetBytes(variable);
            int pad = name.Length % 2;
            MemoryStream buffer = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(buffer);
            writer.Write((byte)0x4C);
            writer.Write((byte)((2 + name.Length + pad) / 2));
            writer.Write((byte)0x91);
            writer.Write((byte)name.Length);
            writer.Write(name);
            if (pad == 1)
            {
                writer.Write((byte)0);
            }

            writer.Write((ushort)1);

            byte[] reply = SendConnected(buffer.ToArray());
            if (reply.Length < 4)
            {
                throw new EipException($"Malformed reply reading '{variable}'.");
            }

            if (reply[2] != 0)
            {
                throw new EipException(
                    $"The controller would not read '{variable}' (CIP status 0x{reply[2]:X2}). " +
                    "It must be a Global Variable in the controller project.");
            }

            offset = 4 + 2 * reply[3];
            if (reply.Length < offset + 3)
            {
                throw new EipException($"Malformed reply reading '{variable}'.");
            }

            type = BitConverter.ToUInt16(reply, offset);
            offset += 2;
            return reply;
        }

        private byte[] SendUnconnected(byte[] cip)
        {
            MemoryStream buffer = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(buffer);
            writer.Write((uint)0);
            writer.Write((ushort)10);
            writer.Write((ushort)2);
            writer.Write((ushort)0x0000);
            writer.Write((ushort)0);
            writer.Write((ushort)0x00B2);
            writer.Write((ushort)cip.Length);
            writer.Write(cip);
            return ExtractItem(Encapsulated(0x6F, session, buffer.ToArray()), 0x00B2, 0);
        }

        private byte[] SendConnected(byte[] cip)
        {
            sequence++;
            MemoryStream buffer = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(buffer);
            writer.Write((uint)0);
            writer.Write((ushort)0);
            writer.Write((ushort)2);
            writer.Write((ushort)0x00A1);
            writer.Write((ushort)4);
            writer.Write(connectionId);
            writer.Write((ushort)0x00B1);
            writer.Write((ushort)(cip.Length + 2));
            writer.Write(sequence);
            writer.Write(cip);
            return ExtractItem(Encapsulated(0x70, session, buffer.ToArray()), 0x00B1, 2);
        }

        private static byte[] ExtractItem(byte[] reply, ushort wantedType, int skip)
        {
            int position = 24 + 6;
            if (reply.Length < position + 2)
            {
                throw new EipException("Malformed EtherNet/IP reply.");
            }

            int count = BitConverter.ToUInt16(reply, position);
            position += 2;
            for (int index = 0; index < count && position + 4 <= reply.Length; index++)
            {
                ushort type = BitConverter.ToUInt16(reply, position);
                int length = BitConverter.ToUInt16(reply, position + 2);
                position += 4;
                if (type == wantedType)
                {
                    if (length < skip || position + length > reply.Length)
                    {
                        break;
                    }

                    byte[] data = new byte[length - skip];
                    Array.Copy(reply, position + skip, data, 0, data.Length);
                    return data;
                }

                position += length;
            }

            throw new EipException("Malformed EtherNet/IP reply.");
        }

        private byte[] Encapsulated(ushort command, uint sessionHandle, byte[] data)
        {
            NetworkStream current;
            lock (gate)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(Micro800EipClient));
                }

                current = stream;
            }

            if (current == null)
            {
                throw new EipException("Not connected.");
            }

            byte[] packet = new byte[24 + data.Length];
            BitConverter.GetBytes(command).CopyTo(packet, 0);
            BitConverter.GetBytes((ushort)data.Length).CopyTo(packet, 2);
            BitConverter.GetBytes(sessionHandle).CopyTo(packet, 4);
            data.CopyTo(packet, 24);
            current.Write(packet, 0, packet.Length);

            byte[] header = ReadExactly(current, 24);
            int length = BitConverter.ToUInt16(header, 2);
            uint status = BitConverter.ToUInt32(header, 8);
            byte[] body = ReadExactly(current, length);
            if (status != 0)
            {
                throw new EipException($"EtherNet/IP status 0x{status:X}.");
            }

            byte[] all = new byte[24 + length];
            header.CopyTo(all, 0);
            body.CopyTo(all, 24);
            return all;
        }

        private static byte[] ReadExactly(NetworkStream source, int count)
        {
            byte[] buffer = new byte[count];
            int received = 0;
            while (received < count)
            {
                int read = source.Read(buffer, received, count - received);
                if (read <= 0)
                {
                    throw new EipException("The controller closed the connection.");
                }

                received += read;
            }

            return buffer;
        }

        private void Close()
        {
            lock (gate)
            {
                try
                {
                    stream?.Close();
                }
                catch (IOException)
                {
                }

                client?.Close();
                stream = null;
                client = null;
                session = 0;
                connectionId = 0;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
            }

            Close();
        }
    }
}
