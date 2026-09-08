using UnityEngine;

namespace Kit1DigitalTwin.Hmi
{
    public sealed class Kit1HmiAdapter : KitHmiAdapterBase
    {
        private static readonly string[] OutputLabels =
        {
            "Cylinder 1 / Feed", "Cylinder 2 / Lower", "Cylinder 3 / Upper", "Vertical Lift Up"
        };

        private static readonly string[] InputLabels =
        {
            "Magazine present", "Magazine empty", "Part on lift", "Metal detected",
            "Plastic detected", "Lift upper", "Lift lower", "C1 extended",
            "C1 retracted", "C2 extended", "C2 retracted", "C3 extended",
            "C3 retracted", "Workpiece released"
        };

        private Kit1PlcIo plcIo;
        private Kit1AutomaticSequence sequence;
        private MagazineFeedTest workpieceFlow;
        private Kit1MagazineBatch magazine;
        private ProductionMonitor productionMonitor;
        private ElectropneumaticUtilities utilities;
        private HmiOperatingMode mode = HmiOperatingMode.Automatic;

        public override string KitId => "KIT-01";
        public override string KitTitle => "Sorting Module";
        public override string ConnectionStatus => "LOCAL SIMULATION";
        public override string StatusText => sequence == null
            ? "INITIALIZING"
            : sequence.IsRunningInstance ? sequence.CurrentStateName : "READY";
        public override string AlarmText
        {
            get
            {
                if (magazine != null && magazine.Empty)
                {
                    return "MAGAZINE EMPTY";
                }

                if (utilities != null && utilities.EmergencyStopActive)
                {
                    return "EMERGENCY STOP ACTIVE";
                }

                if (utilities != null && !utilities.ControlPowerAvailable)
                {
                    return "24 VDC CONTROL POWER LOST";
                }

                if (utilities != null && !utilities.PressureAvailable)
                {
                    return "PNEUMATIC PRESSURE LOW";
                }

                if (utilities != null && utilities.ActuatorFault != SimulatedActuatorFault.None)
                {
                    return $"STUCK ACTUATOR: {utilities.ActuatorFault}";
                }

                if (utilities != null && utilities.SensorFault != SimulatedSensorFault.None)
                {
                    return $"FAILED SENSOR: {utilities.SensorFault}";
                }

                if (workpieceFlow != null && workpieceFlow.ResetPending)
                {
                    return "RESET IN PROGRESS";
                }

                return string.Empty;
            }
        }

        public override int RemainingWorkpieces => magazine != null ? magazine.RemainingCount : 0;
        public override bool IsRunning => sequence != null && sequence.IsRunningInstance;
        public override HmiOperatingMode Mode => mode;
        public override int ManualOutputCount => OutputLabels.Length;
        public override int InputCount => InputLabels.Length;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindFirstObjectByType<Kit1HmiAdapter>() == null)
            {
                new GameObject("Kit 1 HMI Adapter").AddComponent<Kit1HmiAdapter>();
            }
        }

        private void Start()
        {
            plcIo = FindFirstObjectByType<Kit1PlcIo>();
            sequence = FindFirstObjectByType<Kit1AutomaticSequence>();
            workpieceFlow = FindFirstObjectByType<MagazineFeedTest>();
            magazine = FindFirstObjectByType<Kit1MagazineBatch>();
            productionMonitor = FindAnyObjectByType<ProductionMonitor>();
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();
            if (plcIo == null || sequence == null || workpieceFlow == null || magazine == null)
            {
                Debug.LogError("Kit 1 HMI adapter could not find its configured runtime services.");
                enabled = false;
            }
        }

        public override string GetManualOutputLabel(int index)
        {
            return index >= 0 && index < OutputLabels.Length ? OutputLabels[index] : "Unknown";
        }

        public override bool GetManualOutput(int index)
        {
            if (plcIo == null)
            {
                return false;
            }

            return index switch
            {
                0 => plcIo.DO_Cylinder1Extend,
                1 => plcIo.DO_Cylinder2Extend,
                2 => plcIo.DO_Cylinder3Extend,
                3 => plcIo.DO_LiftUp,
                _ => false
            };
        }

        public override void SetManualOutput(int index, bool value)
        {
            if (mode != HmiOperatingMode.Manual || IsRunning || plcIo == null)
            {
                return;
            }

            plcIo.EnableOutputControl(true);
            switch (index)
            {
                case 0: plcIo.SetCylinder1Extend(value); break;
                case 1: plcIo.SetCylinder2Extend(value); break;
                case 2: plcIo.SetCylinder3Extend(value); break;
                case 3: plcIo.SetLiftUp(value); break;
            }
        }

        public override string GetInputLabel(int index)
        {
            return index >= 0 && index < InputLabels.Length ? InputLabels[index] : "Unknown";
        }

        public override bool GetInputState(int index)
        {
            if (plcIo == null)
            {
                return false;
            }

            return index switch
            {
                0 => plcIo.DI_MagazinePartPresent,
                1 => plcIo.DI_MagazineEmpty,
                2 => plcIo.DI_PartOnLift,
                3 => plcIo.DI_MetalDetected,
                4 => plcIo.DI_PlasticDetected,
                5 => plcIo.DI_LiftUp,
                6 => plcIo.DI_LiftDown,
                7 => plcIo.DI_Cylinder1Extended,
                8 => plcIo.DI_Cylinder1Retracted,
                9 => plcIo.DI_Cylinder2Extended,
                10 => plcIo.DI_Cylinder2Retracted,
                11 => plcIo.DI_Cylinder3Extended,
                12 => plcIo.DI_Cylinder3Retracted,
                13 => plcIo.DI_WorkpieceReleased,
                _ => false
            };
        }

        public override void SetMode(HmiOperatingMode requestedMode)
        {
            if (IsRunning || plcIo == null)
            {
                return;
            }

            mode = requestedMode;
            plcIo.SetSafeHomeOutputs();
            plcIo.EnableOutputControl(mode == HmiOperatingMode.Manual);
        }

        public override void StartAutomatic()
        {
            if (mode == HmiOperatingMode.Automatic && sequence != null)
            {
                sequence.RequestStart();
            }
        }

        public override void Stop()
        {
            if (sequence != null && sequence.IsRunningInstance)
            {
                sequence.RequestSafeStop();
            }
            else if (plcIo != null)
            {
                plcIo.SetSafeHomeOutputs();
            }
        }

        public override void ResetMachine()
        {
            if (IsRunning)
            {
                Stop();
                return;
            }

            plcIo?.SetSafeHomeOutputs();
            workpieceFlow?.RequestMasterReset();
            productionMonitor?.RecordEvent("Operator master reset requested.", ProductionEventSeverity.Warning);
        }
    }
}
