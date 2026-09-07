using UnityEngine;

namespace Kit1DigitalTwin.Hmi
{
    public enum HmiOperatingMode
    {
        Automatic,
        Manual
    }

    public abstract class KitHmiAdapterBase : MonoBehaviour
    {
        public abstract string KitId { get; }
        public abstract string KitTitle { get; }
        public abstract string ConnectionStatus { get; }
        public abstract string StatusText { get; }
        public abstract string AlarmText { get; }
        public abstract int RemainingWorkpieces { get; }
        public abstract bool IsRunning { get; }
        public abstract HmiOperatingMode Mode { get; }

        public abstract int ManualOutputCount { get; }
        public abstract string GetManualOutputLabel(int index);
        public abstract bool GetManualOutput(int index);
        public abstract void SetManualOutput(int index, bool value);

        public abstract int InputCount { get; }
        public abstract string GetInputLabel(int index);
        public abstract bool GetInputState(int index);

        public abstract void SetMode(HmiOperatingMode mode);
        public abstract void StartAutomatic();
        public abstract void Stop();
        public abstract void ResetMachine();
    }
}
