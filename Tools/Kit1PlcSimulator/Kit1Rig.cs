using Kit1DigitalTwin.PlcLink;

namespace Kit1PlcSimulator
{
    // Plant + PLC program + the Modbus image the real Micro850 would expose.
    public sealed class Kit1Rig
    {
        public readonly Kit1Plant Plant;
        public readonly Kit1Program Plc = new Kit1Program();
        public readonly object Sync = new object();
        public readonly bool[] Coils = new bool[Kit1ModbusMap.CoilCount];
        public readonly ushort[] Registers = new ushort[Kit1ModbusMap.RegisterCount];
        public long NowMs { get; private set; }

        public Kit1Rig(double travelMs = 400)
        {
            Plant = new Kit1Plant(travelMs);
        }

        public static bool CoilWritable(int address)
        {
            return address == Kit1ModbusMap.HmiStart ||
                address == Kit1ModbusMap.HmiStop ||
                address == Kit1ModbusMap.HmiReset;
        }

        // One PLC scan: plant inputs -> program -> plant outputs -> Modbus image.
        public void Step(double dtMs)
        {
            lock (Sync)
            {
                NowMs += (long)dtMs;
                Plc.HMI_Start |= Coils[Kit1ModbusMap.HmiStart];
                Plc.HMI_Stop |= Coils[Kit1ModbusMap.HmiStop];
                Plc.HMI_Reset |= Coils[Kit1ModbusMap.HmiReset];

                Plant.WriteInputs(Plc.DI);
                Plc.Scan(NowMs);
                Plant.Update(Plc.DO, dtMs);
                PublishImage();
            }
        }

        private void PublishImage()
        {
            Kit1Program p = Plc;
            Coils[Kit1ModbusMap.InMagazinePartPresent] = p.IN_MagazinePartPresent;
            Coils[Kit1ModbusMap.InMagazineEmpty] = p.IN_MagazineEmpty;
            Coils[Kit1ModbusMap.InPartOnLift] = p.IN_PartOnLift;
            Coils[Kit1ModbusMap.InInductive] = p.IN_Inductive;
            Coils[Kit1ModbusMap.InCapacitive] = p.IN_Capacitive;
            Coils[Kit1ModbusMap.InLiftUp] = p.IN_LiftUp;
            Coils[Kit1ModbusMap.InLiftDown] = p.IN_LiftDown;
            Coils[Kit1ModbusMap.InFeedExtended] = p.IN_FeedExtended;
            Coils[Kit1ModbusMap.InFeedRetracted] = p.IN_FeedRetracted;
            Coils[Kit1ModbusMap.InLowerEjectorExtended] = p.IN_LowerEjExtended;
            Coils[Kit1ModbusMap.InLowerEjectorRetracted] = p.IN_LowerEjRetracted;
            Coils[Kit1ModbusMap.InUpperEjectorExtended] = p.IN_UpperEjExtended;
            Coils[Kit1ModbusMap.InUpperEjectorRetracted] = p.IN_UpperEjRetracted;
            Coils[Kit1ModbusMap.InEStopOk] = p.IN_EStopOk;
            Coils[Kit1ModbusMap.InStartButton] = p.IN_StartPB;
            Coils[Kit1ModbusMap.InStopButton] = p.IN_StopPB;
            Coils[Kit1ModbusMap.OutFeedExtend] = p.OUT_FeedExtend;
            Coils[Kit1ModbusMap.OutFeedRetract] = p.OUT_FeedRetract;
            Coils[Kit1ModbusMap.OutLowerEjectorExtend] = p.OUT_LowerEjExtend;
            Coils[Kit1ModbusMap.OutLowerEjectorRetract] = p.OUT_LowerEjRetract;
            Coils[Kit1ModbusMap.OutUpperEjectorExtend] = p.OUT_UpperEjExtend;
            Coils[Kit1ModbusMap.OutUpperEjectorRetract] = p.OUT_UpperEjRetract;
            Coils[Kit1ModbusMap.OutLiftUp] = p.OUT_LiftUp;
            Coils[Kit1ModbusMap.OutLiftDown] = p.OUT_LiftDown;
            Coils[Kit1ModbusMap.StatusAutoRunning] = p.ST_AutoRunning;
            Coils[Kit1ModbusMap.StatusFaulted] = p.ST_Faulted;
            Coils[Kit1ModbusMap.StatusHomed] = p.ST_Homed;
            Coils[Kit1ModbusMap.InResetButton] = p.IN_ResetPB;
            Coils[Kit1ModbusMap.InContinuousMode] = p.IN_ContinuousMode;
            Coils[Kit1ModbusMap.HmiStart] = p.HMI_Start;
            Coils[Kit1ModbusMap.HmiStop] = p.HMI_Stop;
            Coils[Kit1ModbusMap.HmiReset] = p.HMI_Reset;

            Registers[Kit1ModbusMap.RegHeartbeat] = (ushort)p.HR_Heartbeat;
            Registers[Kit1ModbusMap.RegStep] = (ushort)p.HR_Step;
            Registers[Kit1ModbusMap.RegMaterial] = (ushort)p.HR_Material;
            Registers[Kit1ModbusMap.RegPartsCompleted] = (ushort)p.HR_PartsDone;
            Registers[Kit1ModbusMap.RegMetalCount] = (ushort)p.HR_MetalCount;
            Registers[Kit1ModbusMap.RegPlasticCount] = (ushort)p.HR_PlasticCount;
            Registers[Kit1ModbusMap.RegFaultCode] = (ushort)p.HR_FaultCode;
            Registers[Kit1ModbusMap.RegLastCycleTimeMs] = (ushort)p.HR_LastCycleMs;
            Registers[Kit1ModbusMap.RegMapVersion] = (ushort)p.HR_MapVersion;
        }
    }
}
