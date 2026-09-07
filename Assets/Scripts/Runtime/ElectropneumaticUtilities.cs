using Kit1DigitalTwin.Hmi;
using UnityEngine;

namespace Kit1DigitalTwin
{
    public enum SimulatedActuatorFault
    {
        None,
        Cylinder1,
        Cylinder2,
        Cylinder3,
        VerticalLift
    }

    public enum SimulatedSensorFault
    {
        None,
        MagazinePresence,
        MetalDetector,
        PlasticDetector,
        LiftUpper,
        LiftLower
    }

    public sealed class ElectropneumaticUtilities : MonoBehaviour
    {
        public const float NominalPressureBar = 6f;
        public const float MinimumOperatingPressureBar = 3.5f;
        private const float MaximumPressureBar = 8f;
        private const float LeakPressureLossBar = 2f;

        private ProductionMonitor productionMonitor;

        public bool ControlPowerAvailable { get; private set; } = true;
        public bool EmergencyStopActive { get; private set; }
        public bool AirLeakActive { get; private set; }
        public float SupplyPressureBar { get; private set; } = NominalPressureBar;
        public float EffectivePressureBar => Mathf.Max(
            0f, SupplyPressureBar - (AirLeakActive ? LeakPressureLossBar : 0f));
        public bool PressureAvailable => EffectivePressureBar >= MinimumOperatingPressureBar;
        public bool OperationPermitted => ControlPowerAvailable && !EmergencyStopActive && PressureAvailable;
        public SimulatedActuatorFault ActuatorFault { get; private set; }
        public SimulatedSensorFault SensorFault { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<ElectropneumaticUtilities>() == null)
            {
                new GameObject("Electropneumatic Utilities").AddComponent<ElectropneumaticUtilities>();
            }
        }

        private void Start()
        {
            productionMonitor = FindAnyObjectByType<ProductionMonitor>();
        }

        public float GetMotionSpeedFactor(SimulatedActuatorFault actuator)
        {
            if (!ControlPowerAvailable || EmergencyStopActive || !PressureAvailable || ActuatorFault == actuator)
            {
                return 0f;
            }

            float pressureFactor = Mathf.InverseLerp(
                MinimumOperatingPressureBar,
                NominalPressureBar,
                EffectivePressureBar);
            return Mathf.Lerp(0.25f, 1f, pressureFactor);
        }

        public void SetControlPower(bool available)
        {
            if (ControlPowerAvailable == available)
            {
                return;
            }

            ControlPowerAvailable = available;
            Record(
                available ? "24 VDC control power restored." : "24 VDC control power lost.",
                available ? ProductionEventSeverity.Info : ProductionEventSeverity.Alarm);
        }

        public void SetEmergencyStop(bool active)
        {
            if (EmergencyStopActive == active)
            {
                return;
            }

            EmergencyStopActive = active;
            Record(
                active ? "Emergency stop activated." : "Emergency stop reset.",
                active ? ProductionEventSeverity.Alarm : ProductionEventSeverity.Warning);
        }

        public void SetSupplyPressure(float pressureBar)
        {
            float nextPressure = Mathf.Clamp(pressureBar, 0f, MaximumPressureBar);
            bool wasAvailable = PressureAvailable;
            SupplyPressureBar = nextPressure;
            if (wasAvailable != PressureAvailable)
            {
                Record(
                    PressureAvailable ? "Pneumatic pressure restored." : "Pneumatic pressure below operating threshold.",
                    PressureAvailable ? ProductionEventSeverity.Info : ProductionEventSeverity.Alarm);
            }
        }

        public void SetAirLeak(bool active)
        {
            if (AirLeakActive == active)
            {
                return;
            }

            AirLeakActive = active;
            Record(
                active ? "Air-leak fault injected." : "Air-leak fault cleared.",
                active ? ProductionEventSeverity.Warning : ProductionEventSeverity.Info);
        }

        public void SelectNextActuatorFault()
        {
            ActuatorFault = (SimulatedActuatorFault)(((int)ActuatorFault + 1) % 5);
            Record($"Actuator fault: {ActuatorFault}.",
                ActuatorFault == SimulatedActuatorFault.None
                    ? ProductionEventSeverity.Info
                    : ProductionEventSeverity.Warning);
        }

        public void SelectNextSensorFault()
        {
            SensorFault = (SimulatedSensorFault)(((int)SensorFault + 1) % 6);
            Record($"Sensor fault: {SensorFault}.",
                SensorFault == SimulatedSensorFault.None
                    ? ProductionEventSeverity.Info
                    : ProductionEventSeverity.Warning);
        }

        public void ResetUtilities()
        {
            ControlPowerAvailable = true;
            EmergencyStopActive = false;
            AirLeakActive = false;
            SupplyPressureBar = NominalPressureBar;
            ActuatorFault = SimulatedActuatorFault.None;
            SensorFault = SimulatedSensorFault.None;
            Record("Utility simulation restored to nominal.", ProductionEventSeverity.Info);
        }

        private void Record(string message, ProductionEventSeverity severity)
        {
            productionMonitor?.RecordEvent(message, severity);
        }
    }
}
