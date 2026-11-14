using System;

namespace Kit1DigitalTwin.PlcLink
{
    // One way of fetching the Kit 1 image (32 coils, 10 registers, in Kit1ModbusMap
    // order) from a controller. Read-only. Not thread-safe; one instance per thread,
    // except Dispose(), which may be called from another thread to abort a poll.
    public interface IKit1PlcReader : IDisposable
    {
        bool Connected { get; }

        // Where the data comes from, for the log and the on-screen badge.
        string Endpoint { get; }

        void Connect();

        void Read(out bool[] coils, out ushort[] registers);
    }

    // Modbus TCP: needs the Modbus mapping table in the controller project. Also what
    // the soft PLC in Tools/Kit1PlcSimulator serves.
    public sealed class Kit1ModbusReader : IKit1PlcReader
    {
        private readonly ModbusTcpClient client;
        private readonly string endpoint;

        public Kit1ModbusReader(string host, int port, byte unitId, int timeoutMs)
        {
            client = new ModbusTcpClient(host, port, unitId, timeoutMs);
            endpoint = $"{host}:{port} (Modbus TCP)";
        }

        public bool Connected => client.Connected;

        public string Endpoint => endpoint;

        public void Connect()
        {
            client.Connect();
        }

        public void Read(out bool[] coils, out ushort[] registers)
        {
            coils = client.ReadCoils(Kit1ModbusMap.CoilStart, Kit1ModbusMap.CoilCount);
            registers = client.ReadHoldingRegisters(Kit1ModbusMap.RegisterStart, Kit1ModbusMap.RegisterCount);
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }

    // EtherNet/IP: reads the same variables by name from a Micro800. Needs nothing set
    // up in the controller project, and finds the controller by itself when the host
    // is "auto".
    public sealed class Kit1EipReader : IKit1PlcReader
    {
        private readonly Micro800EipClient client;
        private readonly string host;

        public Kit1EipReader(string host, int timeoutMs)
        {
            this.host = host;
            client = new Micro800EipClient(host, timeoutMs);
        }

        public bool Connected => client.Connected;

        public string Endpoint
        {
            get
            {
                string address = client.RemoteAddress ?? (Micro800EipClient.IsAuto(host) ? "searching" : host);
                return $"{address} (EtherNet/IP)";
            }
        }

        public void Connect()
        {
            client.Connect();
        }

        public void Read(out bool[] coils, out ushort[] registers)
        {
            coils = new bool[Kit1ModbusMap.CoilCount];
            for (int index = 0; index < coils.Length; index++)
            {
                coils[index] = client.ReadBool(Kit1ModbusMap.CoilVariables[index]);
            }

            registers = new ushort[Kit1ModbusMap.RegisterCount];
            for (int index = 0; index < registers.Length; index++)
            {
                registers[index] = unchecked((ushort)client.ReadInt(Kit1ModbusMap.RegisterVariables[index]));
            }
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
