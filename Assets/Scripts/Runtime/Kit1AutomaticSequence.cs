using UnityEngine;
using UnityEngine.InputSystem;
using Kit1DigitalTwin.Hmi;

namespace Kit1DigitalTwin
{
    public sealed class Kit1AutomaticSequence : MonoBehaviour
    {
        private enum SequenceState
        {
            Idle,
            WaitingForReset,
            Feeding,
            RetractingFeeder,
            Detecting,
            LoweringLift,
            UpperEjecting,
            UpperRetracting,
            LowerEjecting,
            LowerRetracting,
            RaisingLift,
            Settling,
            FinalReset,
            AbortReset,
            Complete
        }

        private const float GravitySettleSeconds = 1.6f;

        private MagazineFeedTest workpieceFlow;
        private Kit1SensorSimulation sensors;
        private Kit1MagazineBatch magazine;
        private Kit1PlcIo plcIo;
        private ProductionMonitor productionMonitor;
        private ElectropneumaticUtilities utilities;
        private SequenceState state = SequenceState.Idle;
        private float stateTimer;
        private GUIStyle headingStyle;
        private GUIStyle textStyle;

        public static bool IsRunning { get; private set; }
        public bool IsRunningInstance => IsRunning;
        public string CurrentStateName => state.ToString();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!KitSceneContext.IsKit1Scene)
            {
                return;
            }

            IsRunning = false;
            if (FindFirstObjectByType<Kit1AutomaticSequence>() == null)
            {
                new GameObject("Kit 1 Automatic Sequence").AddComponent<Kit1AutomaticSequence>();
            }
        }

        private void Start()
        {
            workpieceFlow = FindFirstObjectByType<MagazineFeedTest>();
            sensors = FindFirstObjectByType<Kit1SensorSimulation>();
            magazine = FindFirstObjectByType<Kit1MagazineBatch>();
            plcIo = FindFirstObjectByType<Kit1PlcIo>();
            productionMonitor = FindAnyObjectByType<ProductionMonitor>();
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();

            if (workpieceFlow == null || sensors == null || magazine == null || plcIo == null)
            {
                Debug.LogError("Automatic sequence could not find every Kit 1 controller.");
                enabled = false;
                return;
            }

            Debug.Log("Automatic sorting ready. Press S to run one cycle; X performs a safe stop.");
        }

        private void Update()
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.sKey.wasPressedThisFrame && !IsRunning)
                {
                    StartCycle();
                }

                if (Keyboard.current.xKey.wasPressedThisFrame && IsRunning)
                {
                    SafeStop();
                }
            }

            if (!IsRunning)
            {
                return;
            }

            if (utilities != null && !utilities.OperationPermitted)
            {
                productionMonitor?.AbortPart("Cycle aborted by electropneumatic utility interlock.");
                plcIo.ForceSafeHome();
                IsRunning = false;
                SetState(SequenceState.Idle);
                return;
            }

            if (workpieceFlow.ResetPending &&
                state != SequenceState.WaitingForReset &&
                state != SequenceState.FinalReset &&
                state != SequenceState.AbortReset)
            {
                productionMonitor?.AbortPart("Master reset interrupted the active cycle.");
                SetState(SequenceState.AbortReset);
            }

            RunStateMachine();
        }

        private void StartCycle()
        {
            if (utilities != null && !utilities.OperationPermitted)
            {
                productionMonitor?.RecordAlarm("Cycle start blocked by utility interlock.");
                Debug.LogWarning("Cannot start: control power, E-stop, or pneumatic pressure is not ready.");
                return;
            }

            if (magazine.Empty)
            {
                productionMonitor?.RecordAlarm("Cycle start blocked: magazine empty.");
                Debug.LogWarning("Cannot start: magazine empty sensor is ON.");
                return;
            }

            IsRunning = true;
            magazine.RandomizeRemainingMaterials();
            sensors.SetAutomaticMaterial(magazine.CurrentMaterial);
            plcIo.SetSafeHomeOutputs();
            plcIo.EnableOutputControl(true);
            workpieceFlow.RequestMasterReset();
            SetState(SequenceState.WaitingForReset);
            productionMonitor?.RecordEvent("Automatic batch started.", ProductionEventSeverity.Info);
            productionMonitor?.BeginPart(sensors.SelectedMaterial.ToString(), magazine.RemainingCount);
            Debug.Log($"Automatic cycle started for {sensors.SelectedMaterial.ToString().ToUpperInvariant()} workpiece.");
        }

        public void RequestStart()
        {
            if (!IsRunning)
            {
                StartCycle();
            }
        }

        private void SafeStop()
        {
            productionMonitor?.AbortPart("Operator safe stop requested.");
            plcIo.SetSafeHomeOutputs();
            workpieceFlow.RequestCycleHome();
            SetState(SequenceState.AbortReset);
            Debug.LogWarning("Automatic cycle safe-stop requested.");
        }

        public void RequestSafeStop()
        {
            if (IsRunning)
            {
                SafeStop();
            }
        }

        private void RunStateMachine()
        {
            switch (state)
            {
                case SequenceState.WaitingForReset:
                    if (workpieceFlow.ReadyAtMagazine && plcIo.DI_LiftUp &&
                        plcIo.DI_MagazinePartPresent)
                    {
                        plcIo.SetCylinder1Extend(true);
                        SetState(SequenceState.Feeding);
                    }
                    break;

                case SequenceState.Feeding:
                    if (plcIo.DI_Cylinder1Extended && plcIo.DI_PartOnLift)
                    {
                        plcIo.SetCylinder1Extend(false);
                        SetState(SequenceState.RetractingFeeder);
                    }
                    break;

                case SequenceState.RetractingFeeder:
                    if (plcIo.DI_Cylinder1Retracted)
                    {
                        magazine.NotifyCurrentWorkpieceFed();
                        SetState(SequenceState.Detecting);
                    }
                    break;

                case SequenceState.Detecting:
                    if (plcIo.DI_MetalDetected)
                    {
                        productionMonitor?.RecordDetection("UPPER / CYLINDER 3");
                        plcIo.SetCylinder3Extend(true);
                        SetState(SequenceState.UpperEjecting);
                    }
                    else if (plcIo.DI_PlasticDetected)
                    {
                        productionMonitor?.RecordDetection("LOWER / CYLINDER 2");
                        plcIo.SetLiftUp(false);
                        SetState(SequenceState.LoweringLift);
                    }
                    break;

                case SequenceState.LoweringLift:
                    if (plcIo.DI_LiftDown)
                    {
                        plcIo.SetCylinder2Extend(true);
                        SetState(SequenceState.LowerEjecting);
                    }
                    break;

                case SequenceState.UpperEjecting:
                    if (plcIo.DI_Cylinder3Extended && plcIo.DI_WorkpieceReleased)
                    {
                        plcIo.SetCylinder3Extend(false);
                        SetState(SequenceState.UpperRetracting);
                    }
                    break;

                case SequenceState.UpperRetracting:
                    if (plcIo.DI_Cylinder3Retracted)
                    {
                        SetState(SequenceState.Settling);
                    }
                    break;

                case SequenceState.LowerEjecting:
                    if (plcIo.DI_Cylinder2Extended && plcIo.DI_WorkpieceReleased)
                    {
                        plcIo.SetCylinder2Extend(false);
                        SetState(SequenceState.LowerRetracting);
                    }
                    break;

                case SequenceState.LowerRetracting:
                    if (plcIo.DI_Cylinder2Retracted)
                    {
                        plcIo.SetLiftUp(true);
                        SetState(SequenceState.RaisingLift);
                    }
                    break;

                case SequenceState.RaisingLift:
                    if (plcIo.DI_LiftUp)
                    {
                        SetState(SequenceState.Settling);
                    }
                    break;

                case SequenceState.Settling:
                    stateTimer += Time.deltaTime;
                    if (stateTimer >= GravitySettleSeconds)
                    {
                        productionMonitor?.CompletePart();
                        plcIo.SetSafeHomeOutputs();
                        workpieceFlow.RequestCycleHome();
                        SetState(SequenceState.FinalReset);
                    }
                    break;

                case SequenceState.FinalReset:
                    if (workpieceFlow.CycleHomeComplete && magazine.IsSettled)
                    {
                        if (magazine.PromotePreparedWorkpiece(
                            out string componentName,
                            out Kit1SensorSimulation.WorkpieceMaterial nextMaterial) &&
                            workpieceFlow.ActivateWorkpiece(componentName))
                        {
                            sensors.SetAutomaticMaterial(nextMaterial);
                            productionMonitor?.BeginPart(nextMaterial.ToString(), magazine.RemainingCount);
                            plcIo.SetCylinder1Extend(true);
                            SetState(SequenceState.Feeding);
                        }
                        else if (plcIo.DI_MagazineEmpty)
                        {
                            plcIo.SetSafeHomeOutputs();
                            plcIo.EnableOutputControl(false);
                            IsRunning = false;
                            SetState(SequenceState.Complete);
                            productionMonitor?.RecordEvent(
                                "Automatic batch complete: magazine empty.",
                                ProductionEventSeverity.Success);
                            Debug.Log("Automatic batch complete: magazine is empty.");
                        }
                    }
                    break;

                case SequenceState.AbortReset:
                    if (workpieceFlow.CycleHomeComplete)
                    {
                        plcIo.EnableOutputControl(false);
                        IsRunning = false;
                        SetState(SequenceState.Idle);
                        productionMonitor?.RecordEvent(
                            "Machine returned to safe home.",
                            ProductionEventSeverity.Info);
                        Debug.Log("Safe stop complete; machine is home.");
                    }
                    break;
            }
        }

        private void SetState(SequenceState nextState)
        {
            state = nextState;
            stateTimer = 0f;
        }

        private void OnGUI()
        {
            if (!Kit1DebugOverlay.Visible || !Kit1DebugOverlay.LegacyPanelsVisible)
            {
                return;
            }

            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.2f) }
            };
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(1494f, 280f, 420f, 150f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(1510f, 292f, 390f, 32f), "AUTOMATIC SORTING", headingStyle);
            GUI.Label(
                new Rect(1510f, 330f, 385f, 82f),
                $"State: {state}\nMaterial: {(sensors != null ? sensors.SelectedMaterial : Kit1SensorSimulation.WorkpieceMaterial.Metal)}\n" +
                $"Remaining: {(magazine != null ? magazine.RemainingCount : 0)}\nS: run batch    X: safe stop",
                textStyle);
        }
    }
}
