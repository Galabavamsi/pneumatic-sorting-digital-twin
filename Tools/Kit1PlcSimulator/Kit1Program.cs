namespace Kit1PlcSimulator
{
    // IEC 61131-3 TON (on-delay timer), time in milliseconds.
    public sealed class Ton
    {
        public bool Q;
        public long ET;
        private bool previousIn;
        private long startMs;

        public void Call(bool input, long presetMs, long nowMs)
        {
            if (!input)
            {
                Q = false;
                ET = 0;
            }
            else
            {
                if (!previousIn)
                {
                    startMs = nowMs;
                }

                ET = System.Math.Min(nowMs - startMs, presetMs);
                Q = ET >= presetMs;
            }

            previousIn = input;
        }
    }

    public sealed class RTrig
    {
        public bool Q;
        private bool previous;

        public void Call(bool clk)
        {
            Q = clk && !previous;
            previous = clk;
        }
    }

    // Line-by-line C# port of PLC/Kit1/Kit1_Sort.st. Keep the two in sync.
    // Field names match the CCW variable names so a diff is easy to read.
    public sealed class Kit1Program
    {
        // Physical I/O image (_IO_EM_DI_00.._15, _IO_EM_DO_00.._07).
        public readonly bool[] DI = new bool[16];
        public readonly bool[] DO = new bool[20];

        // Config (CCW initial values).
        public bool CFG_UseReeds = true;
        public bool CFG_UseMagSensor = true;
        public bool CFG_MetalToUpper = true;
        public bool CFG_EStopFitted = true;
        public bool CFG_EStopNC = true;
        public long CFG_TravelTime = 1500;
        public long CFG_MoveTimeout = 3000;
        public long CFG_HomeTimeout = 6000;
        public long CFG_DetectSettle = 300;
        public long CFG_DetectWindow = 1500;
        public long CFG_EmptyDebounce = 2000;
        public long CFG_FeedDelay = 300;

        // Logical inputs.
        public bool IN_StartPB, IN_StopPB, IN_ResetPB, IN_EStopOk, IN_Inductive, IN_Capacitive;
        public bool IN_ContinuousMode, IN_PartOnLift, IN_MagazinePartPresent, IN_MagazineEmpty;
        public bool IN_FeedExtended, IN_FeedRetracted, IN_UpperEjExtended, IN_UpperEjRetracted;
        public bool IN_LowerEjExtended, IN_LowerEjRetracted, IN_LiftUp, IN_LiftDown;

        // Logical outputs and status.
        public bool OUT_FeedExtend, OUT_FeedRetract, OUT_UpperEjExtend, OUT_UpperEjRetract;
        public bool OUT_LowerEjExtend, OUT_LowerEjRetract, OUT_LiftUp, OUT_LiftDown;
        public bool ST_AutoRunning, ST_Faulted, ST_Homed;

        // HMI command coils (written over Modbus, cleared by the PLC).
        public bool HMI_Start, HMI_Stop, HMI_Reset;

        // Holding registers.
        public int HR_Heartbeat, HR_Step, HR_Material, HR_PartsDone, HR_MetalCount;
        public int HR_PlasticCount, HR_FaultCode, HR_LastCycleMs, HR_MapVersion;

        // Locals.
        private bool rawEStop, rawMagPart, rawFeedRet, rawFeedExt, rawUpperRet, rawUpperExt;
        private bool rawLowerRet, rawLowerExt, rawLiftDown, rawLiftUp;
        private bool outEnable, liftHold, cmdFeedExt, cmdUpperExt, cmdLowerExt, cmdLiftUp = true;
        private bool wantAuto, stopRequest, cycleActive, magEmptyLatched, routeUpper, detected;
        private bool hbPulse, stepChanged, allRetracted;
        private int stepPrev;
        private long cycleMs;
        private readonly Ton tvFeedExt = new Ton(), tvFeedRet = new Ton(), tvUpperExt = new Ton();
        private readonly Ton tvUpperRet = new Ton(), tvLowerExt = new Ton(), tvLowerRet = new Ton();
        private readonly Ton tvLiftUp = new Ton(), tvLiftDown = new Ton();
        private readonly Ton tEmpty = new Ton(), tHb = new Ton(), tStep = new Ton(), tCycle = new Ton();
        private readonly RTrig rtStart = new RTrig(), rtStop = new RTrig(), rtReset = new RTrig();

        private const long OneHour = 3600000;

        public void Scan(long now)
        {
            // 1. Physical -> logical inputs.
            IN_StartPB = DI[0];
            IN_StopPB = DI[1];
            IN_ResetPB = DI[2];
            rawEStop = DI[3];
            IN_Inductive = DI[4];
            IN_Capacitive = DI[5];
            rawMagPart = DI[6];
            IN_ContinuousMode = DI[7];
            rawFeedRet = DI[8];
            rawFeedExt = DI[9];
            rawUpperRet = DI[10];
            rawUpperExt = DI[11];
            rawLowerRet = DI[12];
            rawLowerExt = DI[13];
            rawLiftDown = DI[14];
            rawLiftUp = DI[15];

            if (!CFG_EStopFitted)
            {
                IN_EStopOk = true;
            }
            else if (CFG_EStopNC)
            {
                IN_EStopOk = rawEStop;
            }
            else
            {
                IN_EStopOk = !rawEStop;
            }

            tvFeedExt.Call(OUT_FeedExtend, CFG_TravelTime, now);
            tvFeedRet.Call(OUT_FeedRetract, CFG_TravelTime, now);
            tvUpperExt.Call(OUT_UpperEjExtend, CFG_TravelTime, now);
            tvUpperRet.Call(OUT_UpperEjRetract, CFG_TravelTime, now);
            tvLowerExt.Call(OUT_LowerEjExtend, CFG_TravelTime, now);
            tvLowerRet.Call(OUT_LowerEjRetract, CFG_TravelTime, now);
            tvLiftUp.Call(OUT_LiftUp, CFG_TravelTime, now);
            tvLiftDown.Call(OUT_LiftDown, CFG_TravelTime, now);

            if (CFG_UseReeds)
            {
                IN_FeedExtended = rawFeedExt;
                IN_FeedRetracted = rawFeedRet;
                IN_UpperEjExtended = rawUpperExt;
                IN_UpperEjRetracted = rawUpperRet;
                IN_LowerEjExtended = rawLowerExt;
                IN_LowerEjRetracted = rawLowerRet;
                IN_LiftUp = rawLiftUp;
                IN_LiftDown = rawLiftDown;
            }
            else
            {
                IN_FeedExtended = tvFeedExt.Q;
                IN_FeedRetracted = tvFeedRet.Q;
                IN_UpperEjExtended = tvUpperExt.Q;
                IN_UpperEjRetracted = tvUpperRet.Q;
                IN_LowerEjExtended = tvLowerExt.Q;
                IN_LowerEjRetracted = tvLowerRet.Q;
                IN_LiftUp = tvLiftUp.Q;
                IN_LiftDown = tvLiftDown.Q;
            }

            IN_PartOnLift = IN_Inductive || IN_Capacitive;

            if (CFG_UseMagSensor)
            {
                IN_MagazinePartPresent = rawMagPart;
            }
            else
            {
                IN_MagazinePartPresent = !magEmptyLatched;
            }

            tEmpty.Call(!IN_MagazinePartPresent, CFG_EmptyDebounce, now);
            IN_MagazineEmpty = tEmpty.Q;

            // 2. Housekeeping.
            rtStart.Call(IN_StartPB || HMI_Start);
            rtStop.Call(IN_StopPB || HMI_Stop);
            rtReset.Call(IN_ResetPB || HMI_Reset);
            HMI_Start = false;
            HMI_Stop = false;
            HMI_Reset = false;

            tHb.Call(!hbPulse, 100, now);
            hbPulse = tHb.Q;
            if (hbPulse)
            {
                HR_Heartbeat = HR_Heartbeat + 1;
                if (HR_Heartbeat > 30000)
                {
                    HR_Heartbeat = 0;
                }
            }

            stepChanged = HR_Step != stepPrev;
            stepPrev = HR_Step;
            tStep.Call(!stepChanged, OneHour, now);
            tCycle.Call(cycleActive, OneHour, now);

            HR_MapVersion = 1;

            if (!IN_EStopOk && HR_Step != 90)
            {
                HR_FaultCode = 1;
                HR_Step = 90;
            }

            if (rtStop.Q && (ST_AutoRunning || wantAuto))
            {
                stopRequest = true;
            }

            allRetracted = IN_FeedRetracted && IN_UpperEjRetracted && IN_LowerEjRetracted;

            // 3. Sequence.
            switch (HR_Step)
            {
                case 0:
                    outEnable = false;
                    liftHold = false;
                    ST_AutoRunning = false;
                    stopRequest = false;
                    cycleActive = false;
                    if (rtStart.Q)
                    {
                        magEmptyLatched = false;
                        wantAuto = true;
                        HR_Step = 1;
                    }
                    else if (rtReset.Q)
                    {
                        HR_PartsDone = 0;
                        HR_MetalCount = 0;
                        HR_PlasticCount = 0;
                        HR_LastCycleMs = 0;
                        magEmptyLatched = false;
                        wantAuto = false;
                        HR_Step = 1;
                    }

                    break;

                case 1:
                    outEnable = true;
                    cmdFeedExt = false;
                    cmdUpperExt = false;
                    cmdLowerExt = false;
                    cmdLiftUp = true;
                    liftHold = !allRetracted;
                    if (allRetracted && IN_LiftUp)
                    {
                        liftHold = false;
                        HR_Material = 0;
                        if (wantAuto && !stopRequest)
                        {
                            ST_AutoRunning = true;
                            HR_Step = 2;
                        }
                        else
                        {
                            wantAuto = false;
                            stopRequest = false;
                            HR_Step = 0;
                        }
                    }
                    else if (tStep.ET >= CFG_HomeTimeout)
                    {
                        HR_FaultCode = 9;
                        HR_Step = 90;
                    }

                    break;

                case 2:
                    if (stopRequest)
                    {
                        HR_Step = 13;
                    }
                    else if (IN_PartOnLift && allRetracted && IN_LiftUp && tStep.ET >= CFG_FeedDelay)
                    {
                        // A part is already on the platform: sort it before feeding another.
                        cycleActive = true;
                        HR_Step = 5;
                    }
                    else if (IN_MagazineEmpty)
                    {
                        HR_Step = 13;
                    }
                    else if (IN_MagazinePartPresent && !IN_PartOnLift && allRetracted && IN_LiftUp &&
                             tStep.ET >= CFG_FeedDelay)
                    {
                        cycleActive = true;
                        HR_Step = 3;
                    }

                    break;

                case 3:
                    cmdFeedExt = true;
                    if (IN_FeedExtended)
                    {
                        HR_Step = 4;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 2;
                        HR_Step = 90;
                    }

                    break;

                case 4:
                    cmdFeedExt = false;
                    if (IN_FeedRetracted)
                    {
                        HR_Step = 5;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 3;
                        HR_Step = 90;
                    }

                    break;

                case 5:
                    if (tStep.ET >= CFG_DetectSettle)
                    {
                        if (IN_Inductive)
                        {
                            HR_Material = 1;
                            routeUpper = CFG_MetalToUpper;
                            detected = true;
                        }
                        else if (IN_Capacitive)
                        {
                            HR_Material = 2;
                            routeUpper = !CFG_MetalToUpper;
                            detected = true;
                        }
                        else
                        {
                            detected = false;
                        }

                        if (detected)
                        {
                            HR_Step = routeUpper ? 6 : 8;
                        }
                        else if (tStep.ET >= CFG_DetectWindow)
                        {
                            cycleActive = false;
                            if (CFG_UseMagSensor)
                            {
                                HR_FaultCode = 4;
                                HR_Step = 90;
                            }
                            else
                            {
                                magEmptyLatched = true;
                                HR_Step = 13;
                            }
                        }
                    }

                    break;

                case 6:
                    cmdUpperExt = true;
                    if (IN_UpperEjExtended)
                    {
                        HR_Step = 7;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 5;
                        HR_Step = 90;
                    }

                    break;

                case 7:
                    cmdUpperExt = false;
                    if (IN_UpperEjRetracted)
                    {
                        HR_Step = 12;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 5;
                        HR_Step = 90;
                    }

                    break;

                case 8:
                    cmdLiftUp = false;
                    if (IN_LiftDown)
                    {
                        HR_Step = 9;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 6;
                        HR_Step = 90;
                    }

                    break;

                case 9:
                    cmdLowerExt = true;
                    if (IN_LowerEjExtended)
                    {
                        HR_Step = 10;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 7;
                        HR_Step = 90;
                    }

                    break;

                case 10:
                    cmdLowerExt = false;
                    if (IN_LowerEjRetracted)
                    {
                        HR_Step = 11;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 7;
                        HR_Step = 90;
                    }

                    break;

                case 11:
                    cmdLiftUp = true;
                    if (IN_LiftUp)
                    {
                        HR_Step = 12;
                    }
                    else if (tStep.ET >= CFG_MoveTimeout)
                    {
                        HR_FaultCode = 8;
                        HR_Step = 90;
                    }

                    break;

                case 12:
                    HR_PartsDone = HR_PartsDone + 1;
                    if (HR_Material == 1)
                    {
                        HR_MetalCount = HR_MetalCount + 1;
                    }
                    else
                    {
                        HR_PlasticCount = HR_PlasticCount + 1;
                    }

                    cycleMs = tCycle.ET;
                    if (cycleMs > 32767)
                    {
                        cycleMs = 32767;
                    }

                    HR_LastCycleMs = (int)cycleMs;
                    cycleActive = false;
                    HR_Material = 0;
                    if (stopRequest || !IN_ContinuousMode)
                    {
                        HR_Step = 13;
                    }
                    else
                    {
                        HR_Step = 2;
                    }

                    break;

                case 13:
                    wantAuto = false;
                    stopRequest = false;
                    ST_AutoRunning = false;
                    HR_Step = 1;
                    break;

                case 90:
                    outEnable = false;
                    liftHold = false;
                    ST_AutoRunning = false;
                    wantAuto = false;
                    stopRequest = false;
                    cycleActive = false;
                    if (rtReset.Q && IN_EStopOk)
                    {
                        HR_FaultCode = 0;
                        HR_Material = 0;
                        HR_Step = 1;
                    }

                    break;

                default:
                    HR_Step = 90;
                    break;
            }

            if (HR_Step == 90)
            {
                outEnable = false;
            }

            // 4. Logical outputs.
            OUT_FeedExtend = outEnable && cmdFeedExt;
            OUT_FeedRetract = outEnable && !cmdFeedExt;
            OUT_UpperEjExtend = outEnable && cmdUpperExt;
            OUT_UpperEjRetract = outEnable && !cmdUpperExt;
            OUT_LowerEjExtend = outEnable && cmdLowerExt;
            OUT_LowerEjRetract = outEnable && !cmdLowerExt;
            OUT_LiftUp = outEnable && !liftHold && cmdLiftUp;
            OUT_LiftDown = outEnable && !liftHold && !cmdLiftUp;

            ST_Faulted = HR_Step == 90;
            ST_Homed = allRetracted && IN_LiftUp;

            // 5. Logical -> physical outputs.
            DO[0] = OUT_FeedExtend;
            DO[1] = OUT_FeedRetract;
            DO[2] = OUT_UpperEjExtend;
            DO[3] = OUT_UpperEjRetract;
            DO[4] = OUT_LowerEjExtend;
            DO[5] = OUT_LowerEjRetract;
            DO[6] = OUT_LiftUp;
            DO[7] = OUT_LiftDown;
        }
    }
}
