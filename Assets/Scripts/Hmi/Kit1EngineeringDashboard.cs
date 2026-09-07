using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin.Hmi
{
    public sealed class Kit1EngineeringDashboard : MonoBehaviour
    {
        private enum DashboardTab
        {
            Overview,
            IoMonitor,
            Production,
            Utilities,
            Historian,
            Actuators,
            Diagnostics
        }

        private Kit1HmiAdapter adapter;
        private Kit1PlcIo plcIo;
        private Kit1AutomaticSequence sequence;
        private Kit1MagazineBatch magazine;
        private Kit1SensorSimulation sensors;
        private MagazineFeedTest workpieceFlow;
        private PneumaticCylinderTest cylinder1;
        private SecondCylinderTest cylinder2;
        private ThirdCylinderTest cylinder3;
        private LiftCylinderTest lift;
        private ProductionMonitor productionMonitor;
        private ElectropneumaticUtilities utilities;
        private DigitalTwinHistorian historian;
        private DashboardTab selectedTab;
        private GUIStyle headerStyle;
        private GUIStyle subheaderStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;
        private GUIStyle metricStyle;
        private GUIStyle statusStyle;
        private static Rect dashboardRect;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<Kit1EngineeringDashboard>() == null)
            {
                new GameObject("Kit 1 Engineering Dashboard").AddComponent<Kit1EngineeringDashboard>();
            }
        }

        public static bool ContainsScreenPoint(Vector2 inputSystemScreenPoint)
        {
            if (!Kit1DebugOverlay.Visible)
            {
                return false;
            }

            Vector2 guiPoint = new Vector2(
                inputSystemScreenPoint.x,
                Screen.height - inputSystemScreenPoint.y);
            return dashboardRect.Contains(guiPoint);
        }

        private void Start()
        {
            adapter = FindFirstObjectByType<Kit1HmiAdapter>();
            plcIo = FindFirstObjectByType<Kit1PlcIo>();
            sequence = FindFirstObjectByType<Kit1AutomaticSequence>();
            magazine = FindFirstObjectByType<Kit1MagazineBatch>();
            sensors = FindFirstObjectByType<Kit1SensorSimulation>();
            workpieceFlow = FindFirstObjectByType<MagazineFeedTest>();
            cylinder1 = FindFirstObjectByType<PneumaticCylinderTest>();
            cylinder2 = FindFirstObjectByType<SecondCylinderTest>();
            cylinder3 = FindFirstObjectByType<ThirdCylinderTest>();
            lift = FindFirstObjectByType<LiftCylinderTest>();
            productionMonitor = FindAnyObjectByType<ProductionMonitor>();
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();
            historian = FindAnyObjectByType<DigitalTwinHistorian>();
        }

        private void OnGUI()
        {
            if (!Kit1DebugOverlay.Visible)
            {
                dashboardRect = new Rect(Screen.width - 156f, 18f, 138f, 32f);
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.08f, 0.55f, 0.76f);
                if (GUI.Button(dashboardRect, "F1  ENGINEERING"))
                {
                    Kit1DebugOverlay.ToggleVisibility();
                }
                GUI.backgroundColor = previousBackground;
                return;
            }

            InitializeStyles();
            const float designHeight = 735f;
            float width = Mathf.Clamp(Screen.width * 0.34f, 460f, 610f);
            // IMGUI uses render-target pixels. Upscale the complete engineering
            // panel on QHD/4K targets so it remains readable when Unity fits the
            // Game view into a smaller editor pane.
            float highDpiScale = Mathf.Clamp(Screen.height / 900f, 1f, 1.6f);
            float fitScale = Mathf.Min(
                (Screen.width - 36f) / width,
                (Screen.height - 36f) / designHeight);
            float scale = Mathf.Max(0.45f, Mathf.Min(highDpiScale, fitScale));
            float physicalWidth = width * scale;
            float physicalHeight = designHeight * scale;
            dashboardRect = new Rect(
                Screen.width - physicalWidth - 18f,
                18f,
                physicalWidth,
                physicalHeight);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(dashboardRect.x, dashboardRect.y, 0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.BeginGroup(new Rect(0f, 0f, width, designHeight));
            DrawPanelBackground(width, designHeight);
            DrawHeader(width);
            DrawTabs(width);

            switch (selectedTab)
            {
                case DashboardTab.Overview: DrawOverview(width); break;
                case DashboardTab.IoMonitor: DrawIoMonitor(width); break;
                case DashboardTab.Production: DrawProduction(width); break;
                case DashboardTab.Utilities: DrawUtilities(width); break;
                case DashboardTab.Historian: DrawHistorian(width); break;
                case DashboardTab.Actuators: DrawActuators(width); break;
                case DashboardTab.Diagnostics: DrawDiagnostics(width); break;
            }

            GUI.Label(
                new Rect(20f, designHeight - 30f, width - 40f, 22f),
                "F1  Close engineering view     •     F2  Toggle operator HMI",
                smallStyle);
            GUI.EndGroup();
            GUI.matrix = previousMatrix;
        }

        private void DrawPanelBackground(float width, float height)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.018f, 0.028f, 0.045f, 0.98f);
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            GUI.color = new Color(0.1f, 0.72f, 0.95f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, 5f, height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawHeader(float width)
        {
            GUI.Label(new Rect(22f, 17f, width - 44f, 30f),
                "KIT-01  /  ENGINEERING MONITOR", headerStyle);
            GUI.Label(new Rect(22f, 49f, width - 44f, 23f),
                "SORTING MODULE    •    LOCAL SIMULATION    •    PLC TAG LAYER ONLINE",
                smallStyle);

            Color previousColor = GUI.color;
            GUI.color = new Color(0.11f, 0.17f, 0.24f, 1f);
            GUI.DrawTexture(new Rect(18f, 79f, width - 36f, 1f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawTabs(float width)
        {
            string[] labels =
            {
                "OVERVIEW", "I/O", "PRODUCTION", "UTILITIES", "HISTORY", "ACTUATORS", "DIAG"
            };
            float buttonWidth = (width - 44f) / labels.Length;
            for (int index = 0; index < labels.Length; index++)
            {
                Color previousColor = GUI.backgroundColor;
                GUI.backgroundColor = selectedTab == (DashboardTab)index
                    ? new Color(0.12f, 0.65f, 0.88f)
                    : new Color(0.13f, 0.18f, 0.24f);
                if (GUI.Button(
                    new Rect(18f + buttonWidth * index, 92f, buttonWidth - 4f, 32f),
                    labels[index]))
                {
                    selectedTab = (DashboardTab)index;
                }
                GUI.backgroundColor = previousColor;
            }
        }

        private void DrawOverview(float width)
        {
            float y = 145f;
            DrawSectionTitle("PROCESS STATUS", y, width);
            y += 38f;

            string state = sequence != null ? sequence.CurrentStateName : "INITIALIZING";
            string material = sensors != null
                ? sensors.SelectedMaterial.ToString().ToUpperInvariant()
                : "UNKNOWN";
            DrawMetricCard(20f, y, (width - 52f) / 3f, "STATE", state);
            DrawMetricCard(26f + (width - 52f) / 3f, y, (width - 52f) / 3f,
                "REMAINING", magazine != null ? magazine.RemainingCount.ToString() : "--");
            DrawMetricCard(32f + (width - 52f) * 2f / 3f, y, (width - 52f) / 3f,
                "MATERIAL", material);
            y += 86f;

            DrawSectionTitle("SEQUENCE FLOW", y, width);
            y += 38f;
            DrawProcessStep(24f, y, "1", "FEED", plcIo != null && plcIo.DO_Cylinder1Extend);
            DrawProcessStep(24f, y + 42f, "2", "DETECT", plcIo != null && plcIo.DI_PartOnLift);
            DrawProcessStep(24f, y + 84f, "3", "SELECT LEVEL",
                plcIo != null && (plcIo.DI_LiftUp || plcIo.DI_LiftDown));
            DrawProcessStep(24f, y + 126f, "4", "EJECT",
                plcIo != null && (plcIo.DO_Cylinder2Extend || plcIo.DO_Cylinder3Extend));
            DrawProcessStep(24f, y + 168f, "5", "GRAVITY RELEASE",
                plcIo != null && plcIo.DI_WorkpieceReleased);
            y += 226f;

            DrawSectionTitle("SYSTEM HEALTH", y, width);
            y += 36f;
            DrawStatusLine(24f, y, "PLC logical interface", plcIo != null, "ONLINE", "OFFLINE");
            DrawStatusLine(24f, y + 29f, "Magazine presence sensor",
                plcIo != null && plcIo.DI_MagazinePartPresent, "PART READY", "NO PART");
            DrawStatusLine(24f, y + 58f, "Safety / reset state",
                workpieceFlow != null && !workpieceFlow.ResetPending, "READY", "RESETTING");
        }

        private void DrawIoMonitor(float width)
        {
            DrawSectionTitle("DIGITAL INPUTS", 145f, width);
            float columnWidth = (width - 54f) / 2f;
            if (adapter != null)
            {
                for (int index = 0; index < adapter.InputCount; index++)
                {
                    int column = index / 7;
                    int row = index % 7;
                    DrawIoRow(
                        22f + column * (columnWidth + 10f),
                        184f + row * 34f,
                        columnWidth,
                        adapter.GetInputLabel(index),
                        adapter.GetInputState(index));
                }
            }

            DrawSectionTitle("DIGITAL OUTPUTS", 443f, width);
            if (adapter != null)
            {
                for (int index = 0; index < adapter.ManualOutputCount; index++)
                {
                    DrawIoRow(22f, 482f + index * 36f, width - 44f,
                        adapter.GetManualOutputLabel(index), adapter.GetManualOutput(index));
                }
            }

            DrawStatusLine(22f, 642f, "Output driver",
                plcIo != null && plcIo.OutputControlEnabled, "ENABLED", "MANUAL / IDLE");
        }

        private void DrawActuators(float width)
        {
            DrawSectionTitle("ACTUATOR POSITION / COMMAND", 145f, width);
            DrawActuatorCard(22f, 188f, width - 44f, "CYLINDER 1  •  MAGAZINE FEED",
                cylinder1 != null ? cylinder1.NormalizedPosition : 0f,
                plcIo != null && plcIo.DO_Cylinder1Extend,
                plcIo != null && plcIo.DI_Cylinder1Extended,
                plcIo != null && plcIo.DI_Cylinder1Retracted);
            DrawActuatorCard(22f, 292f, width - 44f, "CYLINDER 2  •  LOWER EJECTOR",
                cylinder2 != null ? cylinder2.NormalizedPosition : 0f,
                plcIo != null && plcIo.DO_Cylinder2Extend,
                plcIo != null && plcIo.DI_Cylinder2Extended,
                plcIo != null && plcIo.DI_Cylinder2Retracted);
            DrawActuatorCard(22f, 396f, width - 44f, "CYLINDER 3  •  UPPER EJECTOR",
                cylinder3 != null ? cylinder3.NormalizedPosition : 0f,
                plcIo != null && plcIo.DO_Cylinder3Extend,
                plcIo != null && plcIo.DI_Cylinder3Extended,
                plcIo != null && plcIo.DI_Cylinder3Retracted);
            DrawActuatorCard(22f, 500f, width - 44f, "VERTICAL LIFT  •  LEVEL SELECT",
                lift != null ? lift.NormalizedPosition : 0f,
                plcIo != null && plcIo.DO_LiftUp,
                plcIo != null && plcIo.DI_LiftUp,
                plcIo != null && plcIo.DI_LiftDown,
                "UP", "DOWN");
        }

        private void DrawProduction(float width)
        {
            DrawSectionTitle("PRODUCTION SUMMARY", 145f, width);
            float cardWidth = (width - 52f) / 3f;
            DrawMetricCard(20f, 184f, cardWidth, "COMPLETED",
                productionMonitor != null ? productionMonitor.CompletedCycles.ToString() : "--");
            DrawMetricCard(26f + cardWidth, 184f, cardWidth, "METAL",
                productionMonitor != null ? productionMonitor.MetalCount.ToString() : "--");
            DrawMetricCard(32f + cardWidth * 2f, 184f, cardWidth, "PLASTIC",
                productionMonitor != null ? productionMonitor.PlasticCount.ToString() : "--");

            DrawMetricCard(20f, 266f, cardWidth, "LAST CYCLE",
                productionMonitor != null ? $"{productionMonitor.LastCycleSeconds:0.00} s" : "--");
            DrawMetricCard(26f + cardWidth, 266f, cardWidth, "AVERAGE",
                productionMonitor != null ? $"{productionMonitor.AverageCycleSeconds:0.00} s" : "--");
            DrawMetricCard(32f + cardWidth * 2f, 266f, cardWidth, "ABORTED",
                productionMonitor != null ? productionMonitor.AbortedCycles.ToString() : "--");

            DrawSectionTitle("EVENT HISTORY", 355f, width);
            if (productionMonitor == null || productionMonitor.EventCount == 0)
            {
                GUI.Label(new Rect(24f, 397f, width - 48f, 26f), "No production events recorded.", bodyStyle);
            }
            else
            {
                int visibleEvents = Mathf.Min(8, productionMonitor.EventCount);
                for (int index = 0; index < visibleEvents; index++)
                {
                    DrawEventRow(22f, 394f + index * 32f, width - 44f,
                        productionMonitor.GetEvent(index));
                }
            }

            bool previousEnabled = GUI.enabled;
            GUI.enabled = productionMonitor != null && (sequence == null || !sequence.IsRunningInstance);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.14f, 0.34f, 0.45f);
            if (GUI.Button(new Rect(22f, 662f, 190f, 36f), "RESET COUNTERS"))
            {
                productionMonitor.ResetCounters();
            }
            GUI.backgroundColor = previousBackground;
            GUI.enabled = previousEnabled;
            GUI.Label(new Rect(225f, 668f, width - 247f, 25f),
                sequence != null && sequence.IsRunningInstance
                    ? "Counters locked while the machine is running."
                    : "Runtime values reset when Play mode ends.",
                smallStyle);
        }

        private void DrawUtilities(float width)
        {
            DrawSectionTitle("ELECTROPNEUMATIC SUPPLY", 145f, width);
            float cardWidth = (width - 52f) / 3f;
            DrawMetricCard(20f, 184f, cardWidth, "CONTROL POWER",
                utilities == null ? "--" : utilities.ControlPowerAvailable ? "24.0 VDC" : "0.0 VDC");
            DrawMetricCard(26f + cardWidth, 184f, cardWidth, "AIR PRESSURE",
                utilities == null ? "--" : $"{utilities.EffectivePressureBar:0.0} bar");
            DrawMetricCard(32f + cardWidth * 2f, 184f, cardWidth, "INTERLOCK",
                utilities != null && utilities.OperationPermitted ? "READY" : "BLOCKED");

            DrawSectionTitle("SUPPLY CONTROLS", 273f, width);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = utilities != null && utilities.ControlPowerAvailable
                ? new Color(0.08f, 0.62f, 0.34f)
                : new Color(0.55f, 0.18f, 0.12f);
            if (GUI.Button(new Rect(22f, 314f, 174f, 42f),
                utilities != null && utilities.ControlPowerAvailable ? "24 VDC  ON" : "24 VDC  OFF"))
            {
                utilities?.SetControlPower(!utilities.ControlPowerAvailable);
            }

            GUI.backgroundColor = utilities != null && utilities.EmergencyStopActive
                ? new Color(0.9f, 0.12f, 0.08f)
                : new Color(0.2f, 0.25f, 0.3f);
            if (GUI.Button(new Rect(207f, 314f, width - 229f, 42f),
                utilities != null && utilities.EmergencyStopActive ? "RESET EMERGENCY STOP" : "EMERGENCY STOP"))
            {
                utilities?.SetEmergencyStop(!utilities.EmergencyStopActive);
            }
            GUI.backgroundColor = previousBackground;

            GUI.Label(new Rect(24f, 373f, width - 48f, 24f),
                utilities == null
                    ? "Supply pressure unavailable"
                    : $"Regulated supply: {utilities.SupplyPressureBar:0.0} bar    " +
                      $"Minimum operating: {ElectropneumaticUtilities.MinimumOperatingPressureBar:0.0} bar",
                bodyStyle);
            if (utilities != null)
            {
                float requestedPressure = GUI.HorizontalSlider(
                    new Rect(24f, 407f, width - 48f, 22f),
                    utilities.SupplyPressureBar,
                    0f,
                    8f);
                if (!Mathf.Approximately(requestedPressure, utilities.SupplyPressureBar))
                {
                    utilities.SetSupplyPressure(requestedPressure);
                }
            }

            DrawSectionTitle("FAULT INJECTION", 451f, width);
            previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = utilities != null && utilities.AirLeakActive
                ? new Color(0.72f, 0.36f, 0.08f)
                : new Color(0.14f, 0.25f, 0.32f);
            if (GUI.Button(new Rect(22f, 493f, width - 44f, 38f),
                utilities != null && utilities.AirLeakActive
                    ? "AIR LEAK: ACTIVE  (-2.0 bar)"
                    : "AIR LEAK: CLEAR"))
            {
                utilities?.SetAirLeak(!utilities.AirLeakActive);
            }

            GUI.backgroundColor = new Color(0.14f, 0.25f, 0.32f);
            if (GUI.Button(new Rect(22f, 541f, width - 44f, 38f),
                $"STUCK ACTUATOR: {(utilities != null ? utilities.ActuatorFault : SimulatedActuatorFault.None)}"))
            {
                utilities?.SelectNextActuatorFault();
            }
            if (GUI.Button(new Rect(22f, 589f, width - 44f, 38f),
                $"FAILED SENSOR: {(utilities != null ? utilities.SensorFault : SimulatedSensorFault.None)}"))
            {
                utilities?.SelectNextSensorFault();
            }

            GUI.backgroundColor = new Color(0.08f, 0.55f, 0.76f);
            if (GUI.Button(new Rect(22f, 644f, 190f, 38f), "RESTORE NOMINAL"))
            {
                utilities?.ResetUtilities();
            }
            GUI.backgroundColor = previousBackground;
            GUI.Label(new Rect(225f, 648f, width - 247f, 42f),
                "Simulation defaults only. Confirm the real kit regulator rating before PLC commissioning.",
                smallStyle);
        }

        private void DrawHistorian(float width)
        {
            DrawSectionTitle("DIGITAL TWIN HISTORIAN", 145f, width);
            float cardWidth = (width - 52f) / 3f;
            DrawMetricCard(20f, 184f, cardWidth, "STATUS",
                historian != null ? historian.StatusText : "OFFLINE");
            DrawMetricCard(26f + cardWidth, 184f, cardWidth, "SAMPLES",
                historian != null ? historian.SampleCount.ToString() : "--");
            DrawMetricCard(32f + cardWidth * 2f, 184f, cardWidth, "DURATION",
                historian != null ? $"{historian.DurationSeconds:0.0} s" : "--");

            DrawSectionTitle("SESSION CONTROLS", 273f, width);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = historian != null && historian.IsRecording
                ? new Color(0.82f, 0.16f, 0.12f)
                : new Color(0.08f, 0.58f, 0.34f);
            if (GUI.Button(new Rect(22f, 314f, 174f, 42f),
                historian != null && historian.IsRecording ? "STOP RECORDING" : "START RECORDING"))
            {
                if (historian != null && historian.IsRecording)
                {
                    historian.StopRecording();
                }
                else
                {
                    historian?.StartRecording();
                }
            }

            GUI.backgroundColor = historian != null && historian.IsReplaying
                ? new Color(0.72f, 0.36f, 0.08f)
                : new Color(0.08f, 0.45f, 0.7f);
            if (GUI.Button(new Rect(207f, 314f, 174f, 42f),
                historian != null && historian.IsReplaying ? "STOP REPLAY" : "REPLAY"))
            {
                if (historian != null && historian.IsReplaying)
                {
                    historian.StopReplay();
                }
                else
                {
                    historian?.StartReplay();
                }
            }

            GUI.backgroundColor = new Color(0.14f, 0.3f, 0.4f);
            if (GUI.Button(new Rect(392f, 314f, width - 414f, 42f), "EXPORT CSV"))
            {
                historian?.ExportCsv();
            }
            GUI.backgroundColor = previousBackground;

            DrawSectionTitle("REPLAY TIMELINE", 378f, width);
            float replayProgress = historian != null ? historian.ReplayNormalized : 0f;
            if (historian != null && historian.IsReplaying)
            {
                float requestedProgress = GUI.HorizontalSlider(
                    new Rect(24f, 422f, width - 48f, 22f), replayProgress, 0f, 1f);
                if (!Mathf.Approximately(requestedProgress, replayProgress))
                {
                    historian.SetReplayNormalized(requestedProgress);
                }
            }
            else
            {
                DrawBar(24f, 422f, width - 48f, replayProgress);
            }
            GUI.Label(new Rect(24f, 448f, width - 48f, 24f),
                historian != null
                    ? $"{historian.ReplaySeconds:0.0} / {historian.DurationSeconds:0.0} s"
                    : "0.0 / 0.0 s",
                smallStyle);

            DrawSectionTitle("RECORDED TELEMETRY", 482f, width);
            GUI.Label(new Rect(24f, 522f, width - 48f, 104f),
                historian == null
                    ? "Historian unavailable."
                    : $"Sequence state     {historian.ReplayState}\n" +
                      $"Material           {historian.ReplayMaterial}\n" +
                      $"Pressure           {historian.ReplayPressureBar:0.0} bar\n" +
                      $"Digital inputs     {historian.ReplayInputs}\n" +
                      $"Digital outputs    {historian.ReplayOutputs}",
                bodyStyle);

            bool previousEnabled = GUI.enabled;
            GUI.enabled = historian != null && !historian.IsRecording && !historian.IsReplaying;
            if (GUI.Button(new Rect(22f, 636f, 150f, 36f), "CLEAR MEMORY"))
            {
                historian.ClearRecording();
            }
            GUI.enabled = previousEnabled;
            GUI.Label(new Rect(184f, 634f, width - 206f, 53f),
                historian == null || string.IsNullOrEmpty(historian.LastExportPath)
                    ? "CSV files are written to Unity's persistent application-data folder."
                    : $"Last export: {historian.LastExportPath}",
                smallStyle);
        }

        private void DrawDiagnostics(float width)
        {
            DrawSectionTitle("ACTIVE DIAGNOSTICS", 145f, width);
            string alarm = adapter != null ? adapter.AlarmText : "INITIALIZING";
            DrawBanner(22f, 184f, width - 44f,
                string.IsNullOrEmpty(alarm) ? "NO ACTIVE ALARMS" : alarm,
                string.IsNullOrEmpty(alarm));

            DrawSectionTitle("INTERLOCKS", 249f, width);
            DrawStatusLine(24f, 289f, "Feed permitted only at lift upper level",
                plcIo != null && plcIo.DI_LiftUp, "SATISFIED", "BLOCKED");
            DrawStatusLine(24f, 320f, "Upper ejector requires metal detection",
                plcIo != null && plcIo.DI_MetalDetected, "SATISFIED", "WAITING");
            DrawStatusLine(24f, 351f, "Lower ejector requires plastic + lift down",
                plcIo != null && plcIo.DI_PlasticDetected && plcIo.DI_LiftDown,
                "SATISFIED", "WAITING");
            DrawStatusLine(24f, 382f, "Next feed waits for ejectors retracted",
                plcIo != null && plcIo.DI_Cylinder2Retracted && plcIo.DI_Cylinder3Retracted,
                "SATISFIED", "BLOCKED");

            DrawSectionTitle("RUNTIME", 436f, width);
            GUI.Label(new Rect(24f, 476f, width - 48f, 104f),
                $"Control source       LOCAL PLC SIMULATOR\n" +
                $"Automatic sequence   {(sequence != null && sequence.IsRunningInstance ? "RUNNING" : "STOPPED")}\n" +
                $"Magazine stack       {(magazine != null && magazine.IsSettled ? "SETTLED" : "GRAVITY DROP")}\n" +
                $"Workpiece physics    {(workpieceFlow != null && workpieceFlow.ReleasedToGravity ? "DYNAMIC" : "KINEMATIC")}",
                bodyStyle);

            DrawSectionTitle("ENGINEERING CONTROLS", 600f, width);
            GUI.Label(new Rect(24f, 638f, width - 48f, 80f),
                "Right mouse drag  Orbit camera\nMiddle drag / Shift + right drag  Pan workspace\n" +
                "Mouse wheel  Zoom    •    Shift + wheel  Fast zoom\n" +
                "F1  Close monitor    •    F2  Operator HMI    •    Tab  Expand HMI",
                smallStyle);
        }

        private void DrawSectionTitle(string title, float y, float width)
        {
            GUI.Label(new Rect(22f, y, width - 44f, 27f), title, subheaderStyle);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.1f, 0.72f, 0.95f, 0.72f);
            GUI.DrawTexture(new Rect(22f, y + 29f, width - 44f, 2f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawMetricCard(float x, float y, float width, string label, string value)
        {
            DrawCard(x, y, width, 70f);
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 20f), label, smallStyle);
            GUI.Label(new Rect(x + 12f, y + 30f, width - 24f, 30f), value, metricStyle);
        }

        private void DrawProcessStep(float x, float y, string number, string label, bool active)
        {
            Color previousColor = GUI.color;
            GUI.color = active ? new Color(0.15f, 0.92f, 0.5f) : new Color(0.18f, 0.24f, 0.31f);
            GUI.DrawTexture(new Rect(x, y, 30f, 30f), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 10f, y + 4f, 18f, 22f), number, statusStyle);
            GUI.Label(new Rect(x + 45f, y + 4f, 320f, 24f), label, bodyStyle);
        }

        private void DrawIoRow(float x, float y, float width, string label, bool value)
        {
            DrawCard(x, y, width, 27f);
            DrawLed(x + 9f, y + 7f, value);
            GUI.Label(new Rect(x + 32f, y + 3f, width - 42f, 21f), label, smallStyle);
        }

        private void DrawActuatorCard(
            float x, float y, float width, string title, float position, bool command,
            bool positiveReed, bool negativeReed, string positiveLabel = "EXT", string negativeLabel = "RET")
        {
            DrawCard(x, y, width, 90f);
            GUI.Label(new Rect(x + 14f, y + 9f, width - 28f, 22f), title, statusStyle);
            DrawBar(x + 14f, y + 38f, width - 155f, position);
            GUI.Label(new Rect(x + width - 128f, y + 34f, 110f, 23f),
                $"{position * 100f:0}%", metricStyle);
            DrawLed(x + 15f, y + 68f, command);
            GUI.Label(new Rect(x + 36f, y + 63f, 105f, 22f), "OUTPUT", smallStyle);
            DrawLed(x + 142f, y + 68f, positiveReed);
            GUI.Label(new Rect(x + 163f, y + 63f, 90f, 22f), positiveLabel, smallStyle);
            DrawLed(x + 244f, y + 68f, negativeReed);
            GUI.Label(new Rect(x + 265f, y + 63f, 90f, 22f), negativeLabel, smallStyle);
        }

        private void DrawBar(float x, float y, float width, float value)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.12f, 0.17f, 0.22f);
            GUI.DrawTexture(new Rect(x, y, width, 14f), Texture2D.whiteTexture);
            GUI.color = new Color(0.1f, 0.75f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(value), 14f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawStatusLine(
            float x, float y, string label, bool okay, string okayText, string offText)
        {
            DrawLed(x, y + 5f, okay);
            GUI.Label(new Rect(x + 22f, y, 305f, 24f), label, bodyStyle);
            GUI.Label(new Rect(x + 330f, y, 160f, 24f), okay ? okayText : offText, smallStyle);
        }

        private void DrawBanner(float x, float y, float width, string text, bool okay)
        {
            Color previousColor = GUI.color;
            GUI.color = okay ? new Color(0.06f, 0.31f, 0.2f) : new Color(0.47f, 0.2f, 0.05f);
            GUI.DrawTexture(new Rect(x, y, width, 46f), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 15f, y + 11f, width - 30f, 25f), text, statusStyle);
        }

        private void DrawEventRow(float x, float y, float width, ProductionEvent productionEvent)
        {
            DrawCard(x, y, width, 27f);
            Color previousColor = GUI.color;
            GUI.color = productionEvent.Severity switch
            {
                ProductionEventSeverity.Success => new Color(0.15f, 1f, 0.48f),
                ProductionEventSeverity.Warning => new Color(1f, 0.72f, 0.15f),
                ProductionEventSeverity.Alarm => new Color(1f, 0.25f, 0.18f),
                _ => new Color(0.1f, 0.75f, 0.95f)
            };
            GUI.DrawTexture(new Rect(x, y, 4f, 27f), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 12f, y + 3f, 70f, 21f), productionEvent.Timestamp, smallStyle);
            GUI.Label(new Rect(x + 84f, y + 3f, width - 94f, 21f), productionEvent.Message, smallStyle);
        }

        private void DrawCard(float x, float y, float width, float height)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.055f, 0.083f, 0.12f, 1f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawLed(float x, float y, bool value)
        {
            Color previousColor = GUI.color;
            GUI.color = value ? new Color(0.15f, 1f, 0.48f) : new Color(0.25f, 0.3f, 0.36f);
            GUI.DrawTexture(new Rect(x, y, 12f, 12f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void InitializeStyles()
        {
            headerStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.28f, 0.86f, 1f) }
            };
            subheaderStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.72f, 0.84f, 0.94f) }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.84f, 0.9f, 0.95f) }
            };
            smallStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.61f, 0.7f, 0.78f) }
            };
            metricStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            statusStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
        }
    }
}
