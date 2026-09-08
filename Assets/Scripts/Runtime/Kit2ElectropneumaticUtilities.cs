using Kit1DigitalTwin.Hmi;
using UnityEngine;

namespace Kit1DigitalTwin
{
    public enum Kit2ActuatorFault
    {
        None,
        FeedCylinder,
        TransferCylinder,
        StampA,
        StampB
    }

    public enum Kit2SensorFault
    {
        None,
        MagazinePresence,
        MetalSensor,
        CapacitiveSensor,
        StampAPosition,
        StampBPosition
    }

    public sealed class Kit2ElectropneumaticUtilities : MonoBehaviour
    {
        public const float NominalPressureBar = 6f;
        public const float MinimumPressureBar = 3.5f;
        private const float LeakLossBar = 2f;

        private ProductionMonitor production;

        public bool ControlPowerAvailable { get; private set; } = true;
        public bool EmergencyStopActive { get; private set; }
        public bool AirLeakActive { get; private set; }
        public float SupplyPressureBar { get; private set; } = NominalPressureBar;
        public float EffectivePressureBar => Mathf.Max(
            0f, SupplyPressureBar - (AirLeakActive ? LeakLossBar : 0f));
        public bool PressureAvailable => EffectivePressureBar >= MinimumPressureBar;
        public bool OperationPermitted =>
            ControlPowerAvailable && !EmergencyStopActive && PressureAvailable;
        public Kit2ActuatorFault ActuatorFault { get; private set; }
        public Kit2SensorFault SensorFault { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2ElectropneumaticUtilities>() == null)
            {
                new GameObject("Kit 2 Electropneumatic Utilities")
                    .AddComponent<Kit2ElectropneumaticUtilities>();
            }
        }

        private void Start()
        {
            production = FindAnyObjectByType<ProductionMonitor>();
        }

        public float GetMotionFactor(Kit2ActuatorFault actuator)
        {
            if (!OperationPermitted || ActuatorFault == actuator)
            {
                return 0f;
            }
            float pressureFactor = Mathf.InverseLerp(
                MinimumPressureBar, NominalPressureBar, EffectivePressureBar);
            return Mathf.Lerp(0.25f, 1f, pressureFactor);
        }

        public void SetControlPower(bool available)
        {
            ControlPowerAvailable = available;
            Record(available ? "Kit 2 control power restored." : "Kit 2 control power lost.",
                available ? ProductionEventSeverity.Info : ProductionEventSeverity.Alarm);
        }

        public void SetEmergencyStop(bool active)
        {
            EmergencyStopActive = active;
            Record(active ? "Kit 2 emergency stop active." : "Kit 2 emergency stop reset.",
                active ? ProductionEventSeverity.Alarm : ProductionEventSeverity.Warning);
        }

        public void SetSupplyPressure(float pressureBar)
        {
            SupplyPressureBar = Mathf.Clamp(pressureBar, 0f, 8f);
        }

        public void SetAirLeak(bool active)
        {
            AirLeakActive = active;
            Record(active ? "Kit 2 simulated air leak enabled." : "Kit 2 air leak cleared.",
                active ? ProductionEventSeverity.Warning : ProductionEventSeverity.Info);
        }

        public void SelectNextActuatorFault()
        {
            ActuatorFault = (Kit2ActuatorFault)(((int)ActuatorFault + 1) % 5);
            Record($"Kit 2 actuator fault: {ActuatorFault}.",
                ActuatorFault == Kit2ActuatorFault.None
                    ? ProductionEventSeverity.Info
                    : ProductionEventSeverity.Warning);
        }

        public void SelectNextSensorFault()
        {
            SensorFault = (Kit2SensorFault)(((int)SensorFault + 1) % 6);
            Record($"Kit 2 sensor fault: {SensorFault}.",
                SensorFault == Kit2SensorFault.None
                    ? ProductionEventSeverity.Info
                    : ProductionEventSeverity.Warning);
        }

        public void ResetUtilities()
        {
            ControlPowerAvailable = true;
            EmergencyStopActive = false;
            AirLeakActive = false;
            SupplyPressureBar = NominalPressureBar;
            ActuatorFault = Kit2ActuatorFault.None;
            SensorFault = Kit2SensorFault.None;
            Record("Kit 2 utilities restored to nominal.", ProductionEventSeverity.Info);
        }

        private void Record(string message, ProductionEventSeverity severity)
        {
            production?.RecordEvent(message, severity);
        }
    }
}
