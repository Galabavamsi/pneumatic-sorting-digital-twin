using System;

namespace Kit1DigitalTwin.PlcLink
{
    // Contract between the Micro850 Kit 1 program and the Unity twin.
    // Keep in sync with PLC/Kit1/Kit1_Modbus_Map.csv and PLC/Kit1/Kit1_Sort.st.
    //
    // The PLC copies its *logical* tags into these Modbus addresses every scan.
    // Physical patch-panel addresses (DI-x / DO-x) live only in the PLC program,
    // so re-patching the rig never changes this map or the Unity code.
    //
    // Modbus numbering: coil 00001 is PDU address 0; holding register 40001 is
    // PDU address 0. The constants below are PDU (zero-based) offsets.
    public static class Kit1ModbusMap
    {
        public const ushort MapVersion = 1;

        public const ushort CoilStart = 0;
        public const ushort CoilCount = 32;
        public const ushort RegisterStart = 0;
        public const ushort RegisterCount = 10;

        // Coils 00001-00016: logical inputs as seen by the PLC.
        public const int InMagazinePartPresent = 0;
        public const int InMagazineEmpty = 1;
        public const int InPartOnLift = 2;
        public const int InInductive = 3;
        public const int InCapacitive = 4;
        public const int InLiftUp = 5;
        public const int InLiftDown = 6;
        public const int InFeedExtended = 7;
        public const int InFeedRetracted = 8;
        public const int InLowerEjectorExtended = 9;
        public const int InLowerEjectorRetracted = 10;
        public const int InUpperEjectorExtended = 11;
        public const int InUpperEjectorRetracted = 12;
        public const int InEStopOk = 13;
        public const int InStartButton = 14;
        public const int InStopButton = 15;

        // Coils 00017-00024: solenoid outputs (double-acting, one coil per direction).
        public const int OutFeedExtend = 16;
        public const int OutFeedRetract = 17;
        public const int OutLowerEjectorExtend = 18;
        public const int OutLowerEjectorRetract = 19;
        public const int OutUpperEjectorExtend = 20;
        public const int OutUpperEjectorRetract = 21;
        public const int OutLiftUp = 22;
        public const int OutLiftDown = 23;

        // Coils 00025-00027: status.
        public const int StatusAutoRunning = 24;
        public const int StatusFaulted = 25;
        public const int StatusHomed = 26;

        // Coils 00028-00029: further logical inputs.
        public const int InResetButton = 27;
        public const int InContinuousMode = 28;

        // Coils 00030-00032: HMI command coils. These are the only writable
        // addresses; the PLC clears them after one scan. Unity never writes them.
        public const int HmiStart = 29;
        public const int HmiStop = 30;
        public const int HmiReset = 31;

        // Holding registers 40001-40010.
        public const int RegHeartbeat = 0;
        public const int RegStep = 1;
        public const int RegMaterial = 2;
        public const int RegPartsCompleted = 3;
        public const int RegMetalCount = 4;
        public const int RegPlasticCount = 5;
        public const int RegFaultCode = 6;
        public const int RegLastCycleTimeMs = 7;
        public const int RegMapVersion = 8;
        public const int RegReserved = 9;
    }

    // Sequence step numbers written by the PLC to register 40002.
    public enum Kit1PlcStep
    {
        Idle = 0,
        Homing = 1,
        WaitPart = 2,
        Feeding = 3,
        FeedRetract = 4,
        Detect = 5,
        UpperEject = 6,
        UpperRetract = 7,
        LiftDown = 8,
        LowerEject = 9,
        LowerRetract = 10,
        LiftUp = 11,
        CycleDone = 12,
        Stopping = 13,
        Fault = 90
    }

    // Material classification written to register 40003.
    public enum Kit1PlcMaterial
    {
        None = 0,
        Metal = 1,
        Plastic = 2
    }

    // Fault codes written to register 40007.
    public enum Kit1PlcFault
    {
        None = 0,
        EmergencyStop = 1,
        FeedExtendTimeout = 2,
        FeedRetractTimeout = 3,
        NoPartDetected = 4,
        UpperEjectorTimeout = 5,
        LiftDownTimeout = 6,
        LowerEjectorTimeout = 7,
        LiftUpTimeout = 8,
        HomeTimeout = 9
    }

    // One decoded poll of the PLC. Immutable once built.
    public sealed class Kit1PlcSnapshot
    {
        public DateTime TimestampUtc { get; private set; }

        public bool MagazinePartPresent { get; private set; }
        public bool MagazineEmpty { get; private set; }
        public bool PartOnLift { get; private set; }
        public bool Inductive { get; private set; }
        public bool Capacitive { get; private set; }
        public bool LiftUp { get; private set; }
        public bool LiftDown { get; private set; }
        public bool FeedExtended { get; private set; }
        public bool FeedRetracted { get; private set; }
        public bool LowerEjectorExtended { get; private set; }
        public bool LowerEjectorRetracted { get; private set; }
        public bool UpperEjectorExtended { get; private set; }
        public bool UpperEjectorRetracted { get; private set; }
        public bool EStopOk { get; private set; }
        public bool StartButton { get; private set; }
        public bool StopButton { get; private set; }

        public bool SolFeedExtend { get; private set; }
        public bool SolFeedRetract { get; private set; }
        public bool SolLowerEjectorExtend { get; private set; }
        public bool SolLowerEjectorRetract { get; private set; }
        public bool SolUpperEjectorExtend { get; private set; }
        public bool SolUpperEjectorRetract { get; private set; }
        public bool SolLiftUp { get; private set; }
        public bool SolLiftDown { get; private set; }

        public bool AutoRunning { get; private set; }
        public bool Faulted { get; private set; }
        public bool Homed { get; private set; }
        public bool ResetButton { get; private set; }
        public bool ContinuousMode { get; private set; }

        public ushort Heartbeat { get; private set; }
        public Kit1PlcStep Step { get; private set; }
        public Kit1PlcMaterial Material { get; private set; }
        public int PartsCompleted { get; private set; }
        public int MetalCount { get; private set; }
        public int PlasticCount { get; private set; }
        public Kit1PlcFault FaultCode { get; private set; }
        public int LastCycleTimeMs { get; private set; }
        public int MapVersion { get; private set; }

        // Plastic is "capacitive sees a part, inductive does not".
        public bool MetalDetected => PartOnLift && Inductive;
        public bool PlasticDetected => PartOnLift && Capacitive && !Inductive;

        public static Kit1PlcSnapshot Decode(bool[] coils, ushort[] registers, DateTime timestampUtc)
        {
            if (coils == null || coils.Length < Kit1ModbusMap.CoilCount)
            {
                throw new ArgumentException($"Expected {Kit1ModbusMap.CoilCount} coils.", nameof(coils));
            }

            if (registers == null || registers.Length < Kit1ModbusMap.RegisterCount)
            {
                throw new ArgumentException(
                    $"Expected {Kit1ModbusMap.RegisterCount} holding registers.", nameof(registers));
            }

            return new Kit1PlcSnapshot
            {
                TimestampUtc = timestampUtc,
                MagazinePartPresent = coils[Kit1ModbusMap.InMagazinePartPresent],
                MagazineEmpty = coils[Kit1ModbusMap.InMagazineEmpty],
                PartOnLift = coils[Kit1ModbusMap.InPartOnLift],
                Inductive = coils[Kit1ModbusMap.InInductive],
                Capacitive = coils[Kit1ModbusMap.InCapacitive],
                LiftUp = coils[Kit1ModbusMap.InLiftUp],
                LiftDown = coils[Kit1ModbusMap.InLiftDown],
                FeedExtended = coils[Kit1ModbusMap.InFeedExtended],
                FeedRetracted = coils[Kit1ModbusMap.InFeedRetracted],
                LowerEjectorExtended = coils[Kit1ModbusMap.InLowerEjectorExtended],
                LowerEjectorRetracted = coils[Kit1ModbusMap.InLowerEjectorRetracted],
                UpperEjectorExtended = coils[Kit1ModbusMap.InUpperEjectorExtended],
                UpperEjectorRetracted = coils[Kit1ModbusMap.InUpperEjectorRetracted],
                EStopOk = coils[Kit1ModbusMap.InEStopOk],
                StartButton = coils[Kit1ModbusMap.InStartButton],
                StopButton = coils[Kit1ModbusMap.InStopButton],
                SolFeedExtend = coils[Kit1ModbusMap.OutFeedExtend],
                SolFeedRetract = coils[Kit1ModbusMap.OutFeedRetract],
                SolLowerEjectorExtend = coils[Kit1ModbusMap.OutLowerEjectorExtend],
                SolLowerEjectorRetract = coils[Kit1ModbusMap.OutLowerEjectorRetract],
                SolUpperEjectorExtend = coils[Kit1ModbusMap.OutUpperEjectorExtend],
                SolUpperEjectorRetract = coils[Kit1ModbusMap.OutUpperEjectorRetract],
                SolLiftUp = coils[Kit1ModbusMap.OutLiftUp],
                SolLiftDown = coils[Kit1ModbusMap.OutLiftDown],
                AutoRunning = coils[Kit1ModbusMap.StatusAutoRunning],
                Faulted = coils[Kit1ModbusMap.StatusFaulted],
                Homed = coils[Kit1ModbusMap.StatusHomed],
                ResetButton = coils[Kit1ModbusMap.InResetButton],
                ContinuousMode = coils[Kit1ModbusMap.InContinuousMode],
                Heartbeat = registers[Kit1ModbusMap.RegHeartbeat],
                Step = (Kit1PlcStep)registers[Kit1ModbusMap.RegStep],
                Material = (Kit1PlcMaterial)registers[Kit1ModbusMap.RegMaterial],
                PartsCompleted = registers[Kit1ModbusMap.RegPartsCompleted],
                MetalCount = registers[Kit1ModbusMap.RegMetalCount],
                PlasticCount = registers[Kit1ModbusMap.RegPlasticCount],
                FaultCode = (Kit1PlcFault)registers[Kit1ModbusMap.RegFaultCode],
                LastCycleTimeMs = registers[Kit1ModbusMap.RegLastCycleTimeMs],
                MapVersion = registers[Kit1ModbusMap.RegMapVersion]
            };
        }
    }
}
