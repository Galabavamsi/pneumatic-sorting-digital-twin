using UnityEngine;

namespace Kit1DigitalTwin
{
    public sealed class Kit1PlcIo : MonoBehaviour
    {
        private Kit1SensorSimulation sensors;
        private PneumaticCylinderTest feedCylinder;
        private SecondCylinderTest lowerEjector;
        private ThirdCylinderTest upperEjector;
        private LiftCylinderTest lift;
        private ElectropneumaticUtilities utilities;
        private GUIStyle headingStyle;
        private GUIStyle tagStyle;

        // Logical digital inputs. Physical Rockwell addresses are intentionally kept
        // outside the simulation until the real controller and wiring are confirmed.
        public bool DI_MagazinePartPresent { get; private set; }
        public bool DI_MagazineEmpty { get; private set; }
        public bool DI_PartOnLift { get; private set; }
        public bool DI_MetalDetected { get; private set; }
        public bool DI_PlasticDetected { get; private set; }
        public bool DI_LiftUp { get; private set; }
        public bool DI_LiftDown { get; private set; }
        public bool DI_Cylinder1Extended { get; private set; }
        public bool DI_Cylinder1Retracted { get; private set; }
        public bool DI_Cylinder2Extended { get; private set; }
        public bool DI_Cylinder2Retracted { get; private set; }
        public bool DI_Cylinder3Extended { get; private set; }
        public bool DI_Cylinder3Retracted { get; private set; }
        public bool DI_WorkpieceReleased { get; private set; }

        // Logical digital outputs used by either the internal sequencer or, later,
        // an external PLC communications adapter.
        public bool DO_Cylinder1Extend { get; private set; }
        public bool DO_Cylinder2Extend { get; private set; }
        public bool DO_Cylinder3Extend { get; private set; }
        public bool DO_LiftUp { get; private set; } = true;
        public bool OutputControlEnabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<Kit1PlcIo>() == null)
            {
                new GameObject("Kit 1 PLC IO").AddComponent<Kit1PlcIo>();
            }
        }

        private void Start()
        {
            sensors = FindFirstObjectByType<Kit1SensorSimulation>();
            feedCylinder = FindFirstObjectByType<PneumaticCylinderTest>();
            lowerEjector = FindFirstObjectByType<SecondCylinderTest>();
            upperEjector = FindFirstObjectByType<ThirdCylinderTest>();
            lift = FindFirstObjectByType<LiftCylinderTest>();
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();

            if (sensors == null || feedCylinder == null || lowerEjector == null ||
                upperEjector == null || lift == null)
            {
                Debug.LogError("PLC I/O layer could not find every sensor and actuator controller.");
                enabled = false;
                return;
            }

            SampleInputs();
            Debug.Log("Kit 1 logical PLC I/O layer online.");
        }

        private void Update()
        {
            SampleInputs();
            if (utilities != null &&
                (!utilities.ControlPowerAvailable || utilities.EmergencyStopActive))
            {
                ForceSafeHome();
            }

            if (OutputControlEnabled)
            {
                ApplyOutputsToActuators();
            }
        }

        public void EnableOutputControl(bool enable)
        {
            OutputControlEnabled = enable;
        }

        public void SetCylinder1Extend(bool extend)
        {
            DO_Cylinder1Extend = extend;
        }

        public void SetCylinder2Extend(bool extend)
        {
            DO_Cylinder2Extend = extend;
        }

        public void SetCylinder3Extend(bool extend)
        {
            DO_Cylinder3Extend = extend;
        }

        public void SetLiftUp(bool up)
        {
            DO_LiftUp = up;
        }

        public void SetSafeHomeOutputs()
        {
            DO_Cylinder1Extend = false;
            DO_Cylinder2Extend = false;
            DO_Cylinder3Extend = false;
            DO_LiftUp = true;
        }

        public void ForceSafeHome()
        {
            SetSafeHomeOutputs();
            if (feedCylinder != null && lowerEjector != null && upperEjector != null && lift != null)
            {
                ApplyOutputsToActuators();
            }
            OutputControlEnabled = false;
        }

        private void SampleInputs()
        {
            if (sensors == null)
            {
                return;
            }

            DI_MagazinePartPresent = sensors.WorkpieceAtMagazine;
            DI_MagazineEmpty = sensors.MagazineEmpty;
            DI_PartOnLift = sensors.WorkpieceOnLift;
            DI_MetalDetected = sensors.MetalDetected;
            DI_PlasticDetected = sensors.PlasticDetected;
            DI_LiftUp = sensors.LiftUp;
            DI_LiftDown = sensors.LiftDown;
            DI_Cylinder1Extended = sensors.FeedExtended;
            DI_Cylinder1Retracted = sensors.FeedRetracted;
            DI_Cylinder2Extended = sensors.LowerEjectorExtended;
            DI_Cylinder2Retracted = sensors.LowerEjectorRetracted;
            DI_Cylinder3Extended = sensors.UpperEjectorExtended;
            DI_Cylinder3Retracted = sensors.UpperEjectorRetracted;
            DI_WorkpieceReleased = sensors.WorkpieceReleased;

            if (utilities == null)
            {
                return;
            }

            switch (utilities.SensorFault)
            {
                case SimulatedSensorFault.MagazinePresence: DI_MagazinePartPresent = false; break;
                case SimulatedSensorFault.MetalDetector: DI_MetalDetected = false; break;
                case SimulatedSensorFault.PlasticDetector: DI_PlasticDetected = false; break;
                case SimulatedSensorFault.LiftUpper: DI_LiftUp = false; break;
                case SimulatedSensorFault.LiftLower: DI_LiftDown = false; break;
            }
        }

        private void ApplyOutputsToActuators()
        {
            if (DO_Cylinder1Extend)
            {
                feedCylinder.CommandExtend();
            }
            else
            {
                feedCylinder.CommandRetract();
            }

            if (DO_Cylinder2Extend)
            {
                lowerEjector.CommandExtend();
            }
            else
            {
                lowerEjector.CommandRetract();
            }

            if (DO_Cylinder3Extend)
            {
                upperEjector.CommandExtend();
            }
            else
            {
                upperEjector.CommandRetract();
            }

            if (DO_LiftUp)
            {
                lift.CommandUp();
            }
            else
            {
                lift.CommandDown();
            }
        }

        private void OnGUI()
        {
            if (!Kit1DebugOverlay.Visible || !Kit1DebugOverlay.LegacyPanelsVisible)
            {
                return;
            }

            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.35f, 0.85f, 1f) }
            };
            tagStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(1494f, 445f, 510f, 390f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(1510f, 457f, 480f, 30f), "PLC LOGICAL TAGS", headingStyle);
            GUI.Label(new Rect(1510f, 490f, 235f, 24f), "DIGITAL INPUTS", tagStyle);
            GUI.Label(new Rect(1760f, 490f, 225f, 24f), "DIGITAL OUTPUTS", tagStyle);

            DrawTag(1510f, 518f, "DI_MagazinePart", DI_MagazinePartPresent);
            DrawTag(1510f, 541f, "DI_MagazineEmpty", DI_MagazineEmpty);
            DrawTag(1510f, 564f, "DI_PartOnLift", DI_PartOnLift);
            DrawTag(1510f, 587f, "DI_Metal", DI_MetalDetected);
            DrawTag(1510f, 610f, "DI_Plastic", DI_PlasticDetected);
            DrawTag(1510f, 633f, "DI_LiftUp", DI_LiftUp);
            DrawTag(1510f, 656f, "DI_LiftDown", DI_LiftDown);
            DrawTag(1510f, 679f, "DI_C1_Ext", DI_Cylinder1Extended);
            DrawTag(1510f, 702f, "DI_C1_Ret", DI_Cylinder1Retracted);
            DrawTag(1510f, 725f, "DI_C2_Ext", DI_Cylinder2Extended);
            DrawTag(1510f, 748f, "DI_C2_Ret", DI_Cylinder2Retracted);
            DrawTag(1510f, 771f, "DI_C3_Ext", DI_Cylinder3Extended);
            DrawTag(1510f, 794f, "DI_C3_Ret", DI_Cylinder3Retracted);

            DrawTag(1760f, 518f, "DO_C1_Extend", DO_Cylinder1Extend);
            DrawTag(1760f, 541f, "DO_C2_Extend", DO_Cylinder2Extend);
            DrawTag(1760f, 564f, "DO_C3_Extend", DO_Cylinder3Extend);
            DrawTag(1760f, 587f, "DO_LiftUp", DO_LiftUp);
            DrawTag(1760f, 625f, "Output control", OutputControlEnabled);
        }

        private void DrawTag(float x, float y, string label, bool value)
        {
            Color previousColor = GUI.color;
            GUI.color = value ? new Color(0.2f, 1f, 0.42f) : new Color(0.25f, 0.28f, 0.32f);
            GUI.DrawTexture(new Rect(x, y + 4f, 13f, 13f), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 20f, y, 220f, 22f), label, tagStyle);
        }
    }
}
