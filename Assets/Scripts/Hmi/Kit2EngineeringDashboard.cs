using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin.Hmi
{
    public sealed class Kit2EngineeringDashboard : MonoBehaviour
    {
        private enum Tab { Overview, Io, Utilities, Diagnostics }

        private static Rect screenRect;
        private static bool visible;
        private Kit2AutomaticSequence sequence;
        private Kit2MagazineFeedCalibration flow;
        private Kit2PlcIo io;
        private Kit2ElectropneumaticUtilities utilities;
        private Tab selectedTab;
        private GUIStyle titleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            visible = false;
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2EngineeringDashboard>() == null)
            {
                new GameObject("Kit 2 Engineering Dashboard")
                    .AddComponent<Kit2EngineeringDashboard>();
            }
        }

        public static bool ContainsScreenPoint(Vector2 inputPoint)
        {
            Vector2 guiPoint = new Vector2(inputPoint.x, Screen.height - inputPoint.y);
            return screenRect.Contains(guiPoint);
        }

        private void Start()
        {
            sequence = FindFirstObjectByType<Kit2AutomaticSequence>();
            flow = FindFirstObjectByType<Kit2MagazineFeedCalibration>();
            io = FindFirstObjectByType<Kit2PlcIo>();
            utilities = FindFirstObjectByType<Kit2ElectropneumaticUtilities>();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            InitializeStyles();
            if (!visible)
            {
                screenRect = new Rect(Screen.width - 166f, 18f, 148f, 34f);
                if (GUI.Button(screenRect, "F1  ENGINEERING")) visible = true;
                return;
            }

            const float designWidth = 560f;
            const float designHeight = 690f;
            float scale = Mathf.Min(1.35f, Mathf.Min(
                (Screen.width - 36f) / designWidth,
                (Screen.height - 36f) / designHeight));
            scale = Mathf.Max(0.48f, scale);
            screenRect = new Rect(
                Screen.width - designWidth * scale - 18f,
                18f,
                designWidth * scale,
                designHeight * scale);

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(screenRect.x, screenRect.y, 0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.BeginGroup(new Rect(0f, 0f, designWidth, designHeight));
            DrawBackground(designWidth, designHeight);
            GUI.Label(new Rect(22f, 16f, 510f, 30f),
                "KIT-02  /  ENGINEERING MONITOR", titleStyle);
            GUI.Label(new Rect(22f, 49f, 510f, 22f),
                "STAMPING MODULE · LOCAL DIGITAL TWIN · LOGICAL PLC TAGS", smallStyle);
            DrawTabs();

            switch (selectedTab)
            {
                case Tab.Overview: DrawOverview(); break;
                case Tab.Io: DrawIo(); break;
                case Tab.Utilities: DrawUtilities(); break;
                case Tab.Diagnostics: DrawDiagnostics(); break;
            }

            GUI.Label(new Rect(22f, 656f, 510f, 22f),
                "F1 close · S auto · N step · P pause · X safe stop", smallStyle);
            GUI.EndGroup();
            GUI.matrix = oldMatrix;
        }

        private void DrawBackground(float width, float height)
        {
            Color old = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.04f, 0.98f);
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            GUI.color = new Color(0.08f, 0.78f, 0.98f);
            GUI.DrawTexture(new Rect(0f, 0f, 5f, height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawTabs()
        {
            string[] labels = { "OVERVIEW", "I/O", "UTILITIES", "DIAGNOSTICS" };
            for (int index = 0; index < labels.Length; index++)
            {
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = selectedTab == (Tab)index
                    ? new Color(0.08f, 0.62f, 0.82f)
                    : new Color(0.12f, 0.18f, 0.24f);
                if (GUI.Button(new Rect(18f + index * 131f, 86f, 125f, 34f), labels[index]))
                {
                    selectedTab = (Tab)index;
                }
                GUI.backgroundColor = old;
            }
        }

        private void DrawOverview()
        {
            Section("PROCESS STATUS", 142f);
            Metric(22f, 180f, 160f, "STATE", sequence?.CurrentState ?? "--");
            Metric(198f, 180f, 160f, "REMAINING", flow?.RemainingCount.ToString() ?? "--");
            Metric(374f, 180f, 160f, "COMPLETED", flow?.CompletedCount.ToString() ?? "--");

            Section("DETECTION / ROUTE", 276f);
            Status(22f, 316f, "Inductive metal sensor", io != null && io.DI_MetalDetected);
            Status(22f, 349f, "Capacitive workpiece sensor", io != null && io.DI_CapacitiveDetected);
            Status(22f, 382f, "Workpiece at Stamp A", io != null && io.DI_PartAtStampA);
            Status(22f, 415f, "Workpiece at Stamp B", io != null && io.DI_PartAtStampB);

            Section("OPERATOR CONTROL", 466f);
            if (GUI.Button(new Rect(22f, 505f, 118f, 40f), "START"))
                sequence?.RequestStartContinuous();
            if (GUI.Button(new Rect(151f, 505f, 118f, 40f), "STEP"))
                sequence?.RequestStep();
            if (GUI.Button(new Rect(280f, 505f, 118f, 40f), "PAUSE"))
                sequence?.RequestPauseToggle();
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.78f, 0.16f, 0.1f);
            if (GUI.Button(new Rect(409f, 505f, 125f, 40f), "SAFE STOP"))
                sequence?.RequestSafeStop();
            GUI.backgroundColor = old;
            GUI.Label(new Rect(22f, 567f, 510f, 55f),
                $"Metal: {sequence?.MetalCount ?? 0}     Plastic: {sequence?.PlasticCount ?? 0}     " +
                $"Faults: {sequence?.FaultCount ?? 0}\n" +
                "Recipe: METAL → Stamp A / Bin 1   ·   PLASTIC → Stamp B / Bin 2",
                bodyStyle);
        }

        private void DrawIo()
        {
            Section("DIGITAL INPUTS", 142f);
            string[] names =
            {
                "Magazine part", "Magazine empty", "Part at Stamp A", "Part at Stamp B",
                "Inductive / metal", "Capacitive / plastic", "Feed home", "Feed stage",
                "Feed eject", "Transfer home", "Transfer stage", "Transfer eject",
                "Stamp A up", "Stamp A down", "Stamp B up", "Stamp B down"
            };
            bool[] values = io == null ? new bool[16] : new[]
            {
                io.DI_MagazinePartPresent, io.DI_MagazineEmpty, io.DI_PartAtStampA,
                io.DI_PartAtStampB, io.DI_MetalDetected, io.DI_CapacitiveDetected,
                io.DI_FeedHome, io.DI_FeedStage, io.DI_FeedEject, io.DI_TransferHome,
                io.DI_TransferStage, io.DI_TransferEject, io.DI_StampAUp,
                io.DI_StampADown, io.DI_StampBUp, io.DI_StampBDown
            };
            for (int index = 0; index < names.Length; index++)
            {
                int column = index / 8;
                int row = index % 8;
                Status(22f + column * 265f, 180f + row * 31f, names[index], values[index], 245f);
            }

            Section("DIGITAL OUTPUT COMMANDS", 456f);
            GUI.Label(new Rect(24f, 496f, 510f, 95f), io == null ? "Offline" :
                $"Feed cylinder: {io.DO_FeedCommand}\n" +
                $"Transfer cylinder: {io.DO_TransferCommand}\n" +
                $"Stamp A: {(io.DO_StampADown ? "DOWN" : "UP")}     " +
                $"Stamp B: {(io.DO_StampBDown ? "DOWN" : "UP")}\n" +
                $"Output driver: {(io.OutputControlEnabled ? "ENABLED" : "MANUAL / IDLE")}", bodyStyle);
        }

        private void DrawUtilities()
        {
            Section("ELECTROPNEUMATIC SUPPLY", 142f);
            Metric(22f, 180f, 160f, "CONTROL", utilities != null && utilities.ControlPowerAvailable ? "24.0 VDC" : "0 VDC");
            Metric(198f, 180f, 160f, "PRESSURE", utilities != null ? $"{utilities.EffectivePressureBar:0.0} bar" : "--");
            Metric(374f, 180f, 160f, "INTERLOCK", utilities != null && utilities.OperationPermitted ? "READY" : "BLOCKED");

            Section("SUPPLY / FAULT CONTROLS", 276f);
            if (GUI.Button(new Rect(22f, 317f, 245f, 42f),
                utilities != null && utilities.ControlPowerAvailable ? "24 VDC ON" : "24 VDC OFF"))
                utilities?.SetControlPower(!utilities.ControlPowerAvailable);
            if (GUI.Button(new Rect(289f, 317f, 245f, 42f),
                utilities != null && utilities.EmergencyStopActive ? "RESET E-STOP" : "EMERGENCY STOP"))
                utilities?.SetEmergencyStop(!utilities.EmergencyStopActive);
            GUI.Label(new Rect(24f, 382f, 500f, 24f),
                utilities != null ? $"Regulated pressure: {utilities.SupplyPressureBar:0.0} bar" : "--", bodyStyle);
            if (utilities != null)
            {
                float pressure = GUI.HorizontalSlider(new Rect(24f, 419f, 500f, 20f),
                    utilities.SupplyPressureBar, 0f, 8f);
                if (!Mathf.Approximately(pressure, utilities.SupplyPressureBar))
                    utilities.SetSupplyPressure(pressure);
            }
            if (GUI.Button(new Rect(22f, 470f, 512f, 38f),
                utilities != null && utilities.AirLeakActive ? "AIR LEAK ACTIVE (-2 bar)" : "AIR LEAK CLEAR"))
                utilities?.SetAirLeak(!utilities.AirLeakActive);
            if (GUI.Button(new Rect(22f, 519f, 512f, 38f),
                $"STUCK ACTUATOR: {utilities?.ActuatorFault}"))
                utilities?.SelectNextActuatorFault();
            if (GUI.Button(new Rect(22f, 568f, 512f, 38f),
                $"FAILED SENSOR: {utilities?.SensorFault}"))
                utilities?.SelectNextSensorFault();
            if (GUI.Button(new Rect(364f, 616f, 170f, 30f), "RESTORE NOMINAL"))
                utilities?.ResetUtilities();
        }

        private void DrawDiagnostics()
        {
            Section("INTERLOCKS / DIAGNOSTICS", 142f);
            Status(22f, 184f, "Logical PLC interface", io != null);
            Status(22f, 219f, "Electropneumatic supply", io != null && io.DI_UtilityReady);
            Status(22f, 254f, "Magazine geometry stable", flow != null);
            Status(22f, 289f, "Automatic controller", sequence != null);
            GUI.Label(new Rect(22f, 350f, 512f, 110f),
                $"Last fault: {sequence?.LastFault ?? "--"}\n\n" +
                "State timeout: 8 seconds\n" +
                "Physical PLC addresses intentionally unassigned until controller and wiring are confirmed.",
                bodyStyle);
        }

        private void Section(string text, float y)
        {
            GUI.Label(new Rect(22f, y, 510f, 28f), text, sectionStyle);
        }

        private void Metric(float x, float y, float width, string label, string value)
        {
            Color old = GUI.color;
            GUI.color = new Color(0.07f, 0.11f, 0.16f, 1f);
            GUI.DrawTexture(new Rect(x, y, width, 68f), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(x + 10f, y + 7f, width - 20f, 20f), label, smallStyle);
            GUI.Label(new Rect(x + 10f, y + 31f, width - 20f, 27f), value, sectionStyle);
        }

        private void Status(float x, float y, string label, bool state, float width = 500f)
        {
            Color old = GUI.color;
            GUI.color = state ? new Color(0.18f, 1f, 0.43f) : new Color(0.25f, 0.29f, 0.34f);
            GUI.DrawTexture(new Rect(x, y + 5f, 13f, 13f), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(x + 21f, y, width - 21f, 24f), label, bodyStyle);
        }

        private void InitializeStyles()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.25f, 0.85f, 1f) } };
            sectionStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white } };
            bodyStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 15,
                normal = { textColor = new Color(0.87f, 0.92f, 0.96f) } };
            smallStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12,
                normal = { textColor = new Color(0.55f, 0.68f, 0.78f) } };
        }
    }
}
