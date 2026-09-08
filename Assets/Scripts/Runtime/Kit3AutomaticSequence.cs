using UnityEngine;
using UnityEngine.InputSystem;
using Kit1DigitalTwin.Hmi;

namespace Kit1DigitalTwin
{
    public sealed class Kit3AutomaticSequence : MonoBehaviour
    {
        private enum SequenceState
        {
            Idle,
            Homing,
            Feeding,
            RetractingFeed,
            StampingDown,
            StampingUp,
            ExtendingPickup,
            Gripping,
            RetractingPickup,
            Routing,
            Releasing,
            ReturningHome,
            Settling,
            SafeStopping,
            Complete,
            Fault
        }

        private const float StateTimeoutSeconds = 8f;
        private const float ReleaseSettleSeconds = 0.65f;
        private const float InterCycleDelaySeconds = 0.25f;

        private Kit3ManualCommissioning handling;
        private Kit3RotaryCalibration rotary;
        private Kit3PlcIo plcIo;
        private ProductionMonitor production;
        private SequenceState state = SequenceState.Idle;
        private float stateTimer;
        private bool paused;
        private bool stepMode;
        private bool stepRequested;
        private bool routeIsBlue;
        private int blueCount;
        private int orangeCount;
        private int faultCount;
        private string lastFault = "--";

        public static bool IsRunning { get; private set; }
        public string CurrentState => state.ToString();
        public bool IsPaused => paused;
        public bool IsStepMode => stepMode;
        public bool RouteIsBlue => routeIsBlue;
        public string RouteText => routeIsBlue ? "BLUE → BIN 1" : "ORANGE → BIN 2";
        public int BlueCount => blueCount;
        public int OrangeCount => orangeCount;
        public int FaultCount => faultCount;
        public string LastFault => lastFault;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            IsRunning = false;
            if (GameObject.Find("Kit3_AssemblyStation") != null &&
                FindAnyObjectByType<Kit3AutomaticSequence>() == null)
            {
                new GameObject("Kit 3 Automatic Sequence")
                    .AddComponent<Kit3AutomaticSequence>();
            }
        }

        private void Start()
        {
            handling = FindFirstObjectByType<Kit3ManualCommissioning>();
            rotary = FindFirstObjectByType<Kit3RotaryCalibration>();
            plcIo = FindFirstObjectByType<Kit3PlcIo>();
            production = FindAnyObjectByType<ProductionMonitor>();
            if (handling == null || rotary == null || plcIo == null)
            {
                Debug.LogError("KIT3_AUTO_MISSING: handling, rotary, or PLC I/O unavailable.");
                enabled = false;
                return;
            }
            Debug.Log("KIT3_AUTO_READY: S batch, N step/advance, P pause, X safe stop.");
        }

        private void Update()
        {
            ReadOperatorControls();
            if (!IsRunning || paused)
            {
                return;
            }
            stateTimer += Time.deltaTime;
            if (!stepMode && state != SequenceState.Settling &&
                state != SequenceState.Releasing &&
                state != SequenceState.SafeStopping &&
                state != SequenceState.Complete &&
                stateTimer > StateTimeoutSeconds)
            {
                RaiseFault($"Timeout in {state}.");
            }
            RunStateMachine();
        }

        private void ReadOperatorControls()
        {
            if (Keyboard.current == null)
            {
                return;
            }
            if (Keyboard.current.sKey.wasPressedThisFrame && !IsRunning)
            {
                StartBatch(false);
            }
            if (Keyboard.current.nKey.wasPressedThisFrame)
            {
                RequestStep();
            }
            if (Keyboard.current.pKey.wasPressedThisFrame && IsRunning)
            {
                RequestPauseToggle();
            }
            if (Keyboard.current.xKey.wasPressedThisFrame && IsRunning)
            {
                RequestSafeStop();
            }
        }

        private void StartBatch(bool singleStep)
        {
            blueCount = 0;
            orangeCount = 0;
            faultCount = 0;
            lastFault = "--";
            paused = false;
            stepMode = singleStep;
            stepRequested = singleStep;
            handling.RequestReset();
            plcIo.EnableOutputControl(true);
            CommandSafeHome();
            IsRunning = true;
            SetState(SequenceState.Homing);
            Debug.Log(singleStep ? "KIT3_AUTO_STEP_STARTED" : "KIT3_AUTO_BATCH_STARTED");
        }

        public void RequestStartContinuous()
        {
            if (!IsRunning) StartBatch(false);
        }

        public void RequestStep()
        {
            if (!IsRunning)
            {
                StartBatch(true);
                return;
            }
            stepMode = true;
            stepRequested = true;
            paused = false;
        }

        public void RequestPauseToggle()
        {
            if (IsRunning) paused = !paused;
        }

        public void RequestSafeStop()
        {
            if (IsRunning) BeginSafeStop("Operator safe stop.");
        }

        public void RequestReset()
        {
            if (IsRunning) return;
            handling.RequestReset();
            plcIo.ForceSafeHome();
            blueCount = 0;
            orangeCount = 0;
            faultCount = 0;
            lastFault = "--";
            SetState(SequenceState.Idle);
        }

        private void RunStateMachine()
        {
            switch (state)
            {
                case SequenceState.Homing:
                    if (MechanismsSafeHome() && plcIo.DI_MagazineReady)
                        Advance(SequenceState.Feeding, () => plcIo.SetFeedExtended(true));
                    break;
                case SequenceState.Feeding:
                    if (plcIo.DI_FeedExtended && plcIo.DI_PartAtStamp)
                    {
                        routeIsBlue = plcIo.DI_PartBlue;
                        production?.BeginPart(routeIsBlue ? "BLUE" : "ORANGE", handling.RemainingCount);
                        production?.RecordDetection(routeIsBlue ? "BIN 1" : "BIN 2");
                        Advance(SequenceState.RetractingFeed, () => plcIo.SetFeedExtended(false));
                    }
                    break;
                case SequenceState.RetractingFeed:
                    if (plcIo.DI_FeedHome)
                        Advance(SequenceState.StampingDown, () => plcIo.SetStampDown(true));
                    break;
                case SequenceState.StampingDown:
                    if (plcIo.DI_StampDown)
                        Advance(SequenceState.StampingUp, () => plcIo.SetStampDown(false));
                    break;
                case SequenceState.StampingUp:
                    if (plcIo.DI_StampUp)
                    {
                        Advance(SequenceState.ExtendingPickup, () =>
                        {
                            plcIo.SetRouteBin2(false);
                            plcIo.SetLiftUp(false);
                            plcIo.SetSlideExtended(true);
                        });
                    }
                    break;
                case SequenceState.ExtendingPickup:
                    if (plcIo.DI_RotaryBin1 && plcIo.DI_LiftDown && plcIo.DI_SlideExtended)
                        Advance(SequenceState.Gripping, () => plcIo.SetVacuum(true));
                    break;
                case SequenceState.Gripping:
                    if (plcIo.DI_VacuumGrip)
                        Advance(SequenceState.RetractingPickup, () => plcIo.SetSlideExtended(false));
                    break;
                case SequenceState.RetractingPickup:
                    if (plcIo.DI_SlideHome)
                        Advance(SequenceState.Routing, () => plcIo.SetRouteBin2(!routeIsBlue));
                    break;
                case SequenceState.Routing:
                    if (routeIsBlue ? plcIo.DI_RotaryBin1 : plcIo.DI_RotaryBin2)
                        Advance(SequenceState.Releasing, () => plcIo.SetVacuum(false));
                    break;
                case SequenceState.Releasing:
                    if (!plcIo.DI_VacuumGrip && stateTimer >= ReleaseSettleSeconds)
                    {
                        if (routeIsBlue) blueCount++; else orangeCount++;
                        production?.CompletePart();
                        Advance(SequenceState.ReturningHome, () => plcIo.SetRouteBin2(false));
                    }
                    break;
                case SequenceState.ReturningHome:
                    if (plcIo.DI_RotaryBin1)
                        SetState(SequenceState.Settling);
                    break;
                case SequenceState.Settling:
                    if (stateTimer < InterCycleDelaySeconds || !handling.MagazineSettled)
                        break;
                    if (plcIo.DI_MagazineEmpty)
                    {
                        IsRunning = false;
                        plcIo.SetSafeHomeOutputs();
                        plcIo.EnableOutputControl(false);
                        SetState(SequenceState.Complete);
                        Debug.Log("KIT3_AUTO_BATCH_COMPLETE: magazine empty.");
                    }
                    else if (plcIo.DI_MagazineReady)
                        Advance(SequenceState.Feeding, () => plcIo.SetFeedExtended(true));
                    break;
                case SequenceState.SafeStopping:
                    if (MechanismsSafeHome())
                    {
                        IsRunning = false;
                        plcIo.EnableOutputControl(false);
                        SetState(SequenceState.Idle);
                        Debug.Log("KIT3_AUTO_SAFE_HOME");
                    }
                    break;
                case SequenceState.Fault:
                    BeginSafeStop(lastFault);
                    break;
            }
        }

        private bool MechanismsSafeHome() => plcIo.DI_FeedHome && plcIo.DI_StampUp &&
            plcIo.DI_SlideHome && plcIo.DI_LiftDown && plcIo.DI_RotaryBin1 &&
            !plcIo.DI_VacuumGrip;

        private void Advance(SequenceState next, System.Action command)
        {
            if (stepMode && !stepRequested) return;
            if (stepMode) stepRequested = false;
            command?.Invoke();
            SetState(next);
            Debug.Log($"KIT3_AUTO_STATE: {state}");
        }

        private void RaiseFault(string message)
        {
            faultCount++;
            lastFault = message;
            production?.RecordAlarm(message);
            SetState(SequenceState.Fault);
            Debug.LogError($"KIT3_AUTO_FAULT: {message}");
        }

        private void BeginSafeStop(string reason)
        {
            paused = false;
            stepMode = false;
            stepRequested = false;
            production?.AbortPart(reason);
            CommandSafeHome();
            SetState(SequenceState.SafeStopping);
            Debug.LogWarning($"KIT3_AUTO_STOP: {reason}");
        }

        private void CommandSafeHome()
        {
            plcIo.SetSafeHomeOutputs();
        }

        private void SetState(SequenceState next)
        {
            state = next;
            stateTimer = 0f;
        }
    }
}
