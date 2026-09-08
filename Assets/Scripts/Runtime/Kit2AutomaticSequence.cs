using UnityEngine;
using UnityEngine.InputSystem;
using Kit1DigitalTwin.Hmi;

namespace Kit1DigitalTwin
{
    public sealed class Kit2AutomaticSequence : MonoBehaviour
    {
        private enum SequenceState
        {
            Idle,
            Homing,
            FeedingStampA,
            RetractingFeed,
            Detecting,
            StampingA,
            RaisingStampA,
            TransferringToB,
            RetractingTransfer,
            StampingB,
            RaisingStampB,
            EjectingA,
            RetractingAfterA,
            EjectingB,
            RetractingAfterB,
            Settling,
            SafeStopping,
            Complete,
            Fault
        }

        private const float StateTimeoutSeconds = 8f;
        private const float InterCycleDelaySeconds = 0.35f;

        private Kit2FeedCylinderCalibration feedCylinder;
        private Kit2SecondCylinderCalibration transferCylinder;
        private Kit2StampingCylinderCalibration stamps;
        private Kit2MagazineFeedCalibration flow;
        private Kit2PlcIo plcIo;
        private Kit2ElectropneumaticUtilities utilities;
        private ProductionMonitor production;
        private SequenceState state = SequenceState.Idle;
        private float stateTimer;
        private bool paused;
        private bool stepMode;
        private bool stepRequested;
        private bool routeIsMetal;
        private int metalCount;
        private int plasticCount;
        private int faultCount;
        private string lastFault = "--";
        private GUIStyle headingStyle;
        private GUIStyle detailStyle;

        public static bool IsRunning { get; private set; }
        public string CurrentState => state.ToString();
        public bool IsPaused => paused;
        public bool IsStepMode => stepMode;
        public int MetalCount => metalCount;
        public int PlasticCount => plasticCount;
        public int FaultCount => faultCount;
        public string LastFault => lastFault;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            IsRunning = false;
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2AutomaticSequence>() == null)
            {
                new GameObject("Kit 2 Automatic Sequence")
                    .AddComponent<Kit2AutomaticSequence>();
            }
        }

        private void Start()
        {
            feedCylinder = FindFirstObjectByType<Kit2FeedCylinderCalibration>();
            transferCylinder = FindFirstObjectByType<Kit2SecondCylinderCalibration>();
            stamps = FindFirstObjectByType<Kit2StampingCylinderCalibration>();
            flow = FindFirstObjectByType<Kit2MagazineFeedCalibration>();
            plcIo = FindFirstObjectByType<Kit2PlcIo>();
            utilities = FindFirstObjectByType<Kit2ElectropneumaticUtilities>();
            production = FindAnyObjectByType<ProductionMonitor>();
            if (feedCylinder == null || transferCylinder == null ||
                stamps == null || flow == null || plcIo == null || utilities == null)
            {
                Debug.LogError("Kit 2 automatic cycle requires all mechanism controllers.");
                enabled = false;
                return;
            }

            Debug.Log(
                "KIT2_AUTO_READY: S continuous batch, N step/advance, P pause, X safe stop.");
        }

        private void Update()
        {
            ReadOperatorControls();
            if (!IsRunning || paused)
            {
                return;
            }

            if (!utilities.OperationPermitted && state != SequenceState.SafeStopping)
            {
                RaiseFault("Electropneumatic utility interlock opened.");
            }

            stateTimer += Time.deltaTime;
            if (!stepMode && state != SequenceState.Settling &&
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
                if (!IsRunning)
                {
                    StartBatch(true);
                }
                else
                {
                    stepMode = true;
                    stepRequested = true;
                    paused = false;
                }
            }
            if (Keyboard.current.pKey.wasPressedThisFrame && IsRunning)
            {
                paused = !paused;
                Debug.Log(paused ? "KIT2_AUTO_PAUSED" : "KIT2_AUTO_RESUMED");
            }
            if (Keyboard.current.xKey.wasPressedThisFrame && IsRunning)
            {
                BeginSafeStop("Operator safe stop.");
            }
        }

        private void StartBatch(bool singleStep)
        {
            if (!utilities.OperationPermitted)
            {
                production?.RecordAlarm("Kit 2 start blocked by utility interlock.");
                Debug.LogWarning("KIT2_AUTO_START_BLOCKED: restore power, E-stop and pressure.");
                return;
            }
            metalCount = 0;
            plasticCount = 0;
            faultCount = 0;
            lastFault = "--";
            paused = false;
            stepMode = singleStep;
            stepRequested = singleStep;
            IsRunning = true;
            flow.RequestReset();
            plcIo.EnableOutputControl(true);
            CommandSafeHome();
            SetState(SequenceState.Homing);
            Debug.Log(singleStep
                ? "KIT2_AUTO_STEP_STARTED"
                : "KIT2_AUTO_BATCH_STARTED");
        }

        public void RequestStartContinuous()
        {
            if (!IsRunning)
            {
                StartBatch(false);
            }
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
            if (IsRunning)
            {
                paused = !paused;
            }
        }

        public void RequestSafeStop()
        {
            if (IsRunning)
            {
                BeginSafeStop("Operator safe stop.");
            }
        }

        private void RunStateMachine()
        {
            switch (state)
            {
                case SequenceState.Homing:
                    if (MechanismsHome() && plcIo.DI_MagazinePartPresent &&
                        !flow.MagazineAdvancing)
                    {
                        Advance(SequenceState.FeedingStampA,
                            () => plcIo.SetFeedCommand(Kit2CylinderCommand.Stage));
                    }
                    break;

                case SequenceState.FeedingStampA:
                    if (plcIo.DI_FeedStage && plcIo.DI_PartAtStampA)
                    {
                        Advance(SequenceState.RetractingFeed,
                            () => plcIo.SetFeedCommand(Kit2CylinderCommand.Home));
                    }
                    break;

                case SequenceState.RetractingFeed:
                    if (!plcIo.DI_FeedHome)
                    {
                        break;
                    }
                    Advance(SequenceState.Detecting, null);
                    break;

                case SequenceState.Detecting:
                    if (plcIo.DI_MetalDetected)
                    {
                        routeIsMetal = true;
                        production?.BeginPart("METAL", flow.RemainingCount);
                        production?.RecordDetection("STAMP A / BIN 1");
                        Advance(SequenceState.StampingA,
                            () => plcIo.SetStampADown(true));
                    }
                    else if (plcIo.DI_CapacitiveDetected)
                    {
                        routeIsMetal = false;
                        production?.BeginPart("PLASTIC", flow.RemainingCount);
                        production?.RecordDetection("STAMP B / BIN 2");
                        Advance(SequenceState.TransferringToB,
                            () => plcIo.SetTransferCommand(Kit2CylinderCommand.Stage));
                    }
                    break;

                case SequenceState.StampingA:
                    if (plcIo.DI_StampADown)
                    {
                        Advance(SequenceState.RaisingStampA,
                            () => plcIo.SetStampADown(false));
                    }
                    break;

                case SequenceState.RaisingStampA:
                    if (plcIo.DI_StampAUp)
                    {
                        Advance(SequenceState.EjectingA,
                            () => plcIo.SetFeedCommand(Kit2CylinderCommand.Eject));
                    }
                    break;

                case SequenceState.TransferringToB:
                    if (plcIo.DI_TransferStage && flow.ActiveAtStampB)
                    {
                        Advance(SequenceState.RetractingTransfer,
                            () => plcIo.SetTransferCommand(Kit2CylinderCommand.Home));
                    }
                    break;

                case SequenceState.RetractingTransfer:
                    if (plcIo.DI_TransferHome && plcIo.DI_PartAtStampB &&
                        !flow.ActiveAtStampB)
                    {
                        Advance(SequenceState.StampingB,
                            () => plcIo.SetStampBDown(true));
                    }
                    break;

                case SequenceState.StampingB:
                    if (plcIo.DI_StampBDown)
                    {
                        Advance(SequenceState.RaisingStampB,
                            () => plcIo.SetStampBDown(false));
                    }
                    break;

                case SequenceState.RaisingStampB:
                    if (plcIo.DI_StampBUp)
                    {
                        Advance(SequenceState.EjectingB,
                            () => plcIo.SetTransferCommand(Kit2CylinderCommand.Eject));
                    }
                    break;

                case SequenceState.EjectingA:
                    if (plcIo.DI_FeedEject && flow.IsDischarging)
                    {
                        Advance(SequenceState.RetractingAfterA,
                            () => plcIo.SetFeedCommand(Kit2CylinderCommand.Home));
                    }
                    break;

                case SequenceState.RetractingAfterA:
                    if (plcIo.DI_FeedHome && !flow.IsDischarging)
                    {
                        CompletePart();
                    }
                    break;

                case SequenceState.EjectingB:
                    if (plcIo.DI_TransferEject && flow.IsDischarging)
                    {
                        Advance(SequenceState.RetractingAfterB,
                            () => plcIo.SetTransferCommand(Kit2CylinderCommand.Home));
                    }
                    break;

                case SequenceState.RetractingAfterB:
                    if (plcIo.DI_TransferHome && !flow.IsDischarging &&
                        !flow.StampBOccupied)
                    {
                        CompletePart();
                    }
                    break;

                case SequenceState.Settling:
                    if (stateTimer >= InterCycleDelaySeconds && MechanismsHome())
                    {
                        if (flow.MagazineEmpty)
                        {
                            IsRunning = false;
                            plcIo.SetSafeHomeOutputs();
                            plcIo.EnableOutputControl(false);
                            SetState(SequenceState.Complete);
                            Debug.Log("KIT2_AUTO_BATCH_COMPLETE: magazine empty.");
                        }
                        else if (flow.HasActiveWorkpiece && !flow.MagazineAdvancing)
                        {
                            Advance(SequenceState.FeedingStampA,
                                () => plcIo.SetFeedCommand(Kit2CylinderCommand.Stage));
                        }
                    }
                    break;

                case SequenceState.SafeStopping:
                    if (MechanismsHome())
                    {
                        IsRunning = false;
                        plcIo.EnableOutputControl(false);
                        SetState(SequenceState.Idle);
                        Debug.Log("KIT2_AUTO_SAFE_HOME");
                    }
                    break;

                case SequenceState.Fault:
                    BeginSafeStop(lastFault);
                    break;
            }
        }

        private void CompletePart()
        {
            if (routeIsMetal)
            {
                metalCount++;
            }
            else
            {
                plasticCount++;
            }
            production?.CompletePart();
            SetState(SequenceState.Settling);
        }

        private void Advance(SequenceState nextState, System.Action command)
        {
            if (stepMode && !stepRequested)
            {
                return;
            }
            if (stepMode)
            {
                stepRequested = false;
            }
            command?.Invoke();
            SetState(nextState);
            Debug.Log($"KIT2_AUTO_STATE: {state}");
        }

        private void RaiseFault(string message)
        {
            faultCount++;
            lastFault = message;
            production?.RecordAlarm(message);
            SetState(SequenceState.Fault);
            Debug.LogError($"KIT2_AUTO_FAULT: {message}");
        }

        private void BeginSafeStop(string reason)
        {
            paused = false;
            stepMode = false;
            stepRequested = false;
            production?.AbortPart(reason);
            CommandSafeHome();
            SetState(SequenceState.SafeStopping);
            Debug.LogWarning($"KIT2_AUTO_STOP: {reason}");
        }

        private void CommandSafeHome()
        {
            plcIo.SetSafeHomeOutputs();
        }

        private bool MechanismsHome()
        {
            return feedCylinder.IsHome && transferCylinder.IsHome &&
                stamps.StampAHome && stamps.StampBHome;
        }

        private void SetState(SequenceState nextState)
        {
            state = nextState;
            stateTimer = 0f;
        }

        private void OnGUI()
        {
            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.84f, 0.22f) }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(18f, 18f, 520f, 126f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.Label(new Rect(34f, 25f, 485f, 28f),
                "KIT 2 · AUTOMATIC STAMPING", headingStyle);
            GUI.Label(new Rect(34f, 56f, 488f, 82f),
                $"State: {state}{(paused ? " · PAUSED" : "")}{(stepMode ? " · STEP" : "")}   " +
                $"Route: {(routeIsMetal ? "METAL / A" : "PLASTIC / B")}\n" +
                $"Metal: {metalCount}   Plastic: {plasticCount}   Remaining: {flow?.RemainingCount ?? 0}   Faults: {faultCount}\n" +
                "S: auto batch   N: step/advance   P: pause   X: safe stop   M: reset (manual)",
                detailStyle);
        }
    }
}
