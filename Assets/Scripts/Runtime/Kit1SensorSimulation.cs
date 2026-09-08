using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class Kit1SensorSimulation : MonoBehaviour
    {
        public enum WorkpieceMaterial
        {
            Metal,
            Plastic
        }

        private PneumaticCylinderTest feedCylinder;
        private SecondCylinderTest lowerEjector;
        private ThirdCylinderTest upperEjector;
        private LiftCylinderTest lift;
        private MagazineFeedTest workpieceFlow;
        private Kit1MagazineBatch magazine;
        private WorkpieceMaterial material = WorkpieceMaterial.Metal;
        private GUIStyle headingStyle;
        private GUIStyle textStyle;
        private GUIStyle smallStyle;

        public WorkpieceMaterial SelectedMaterial => material;
        public bool WorkpieceAtMagazine => magazine == null
            ? workpieceFlow != null && !workpieceFlow.IsFed
            : magazine.WorkpieceAtFeedSlot;
        public bool WorkpieceOnLift => workpieceFlow != null && workpieceFlow.IsFed && !workpieceFlow.IsRouted;
        public bool MetalDetected => WorkpieceOnLift && material == WorkpieceMaterial.Metal;
        public bool PlasticDetected => WorkpieceOnLift && material == WorkpieceMaterial.Plastic;
        public bool MagazineEmpty => magazine != null && magazine.Empty;
        public bool WorkpieceReleased => workpieceFlow != null && workpieceFlow.ReleasedToGravity;
        public bool LiftUp => lift != null && lift.NormalizedPosition >= 0.98f;
        public bool LiftDown => lift != null && lift.NormalizedPosition <= 0.02f;
        public bool FeedExtended => feedCylinder != null && feedCylinder.NormalizedPosition >= 0.98f;
        public bool FeedRetracted => feedCylinder != null && feedCylinder.NormalizedPosition <= 0.02f;
        public bool LowerEjectorExtended => lowerEjector != null && lowerEjector.NormalizedPosition >= 0.98f;
        public bool LowerEjectorRetracted => lowerEjector != null && lowerEjector.NormalizedPosition <= 0.02f;
        public bool UpperEjectorExtended => upperEjector != null && upperEjector.NormalizedPosition >= 0.98f;
        public bool UpperEjectorRetracted => upperEjector != null && upperEjector.NormalizedPosition <= 0.02f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindFirstObjectByType<Kit1SensorSimulation>() == null)
            {
                new GameObject("Kit 1 Sensor Simulation").AddComponent<Kit1SensorSimulation>();
            }
        }

        private void Start()
        {
            feedCylinder = FindFirstObjectByType<PneumaticCylinderTest>();
            lowerEjector = FindFirstObjectByType<SecondCylinderTest>();
            upperEjector = FindFirstObjectByType<ThirdCylinderTest>();
            lift = FindFirstObjectByType<LiftCylinderTest>();
            workpieceFlow = FindFirstObjectByType<MagazineFeedTest>();
            magazine = FindFirstObjectByType<Kit1MagazineBatch>();

            if (feedCylinder == null || lowerEjector == null || upperEjector == null ||
                lift == null || workpieceFlow == null)
            {
                Debug.LogError("Sensor simulation could not find every Kit 1 mechanism controller.");
                enabled = false;
                return;
            }

            ApplyMaterialAppearance();
            Debug.Log("Kit 1 simulated sensors online. Workpiece material defaults to METAL.");
        }

        private void Update()
        {
            if (Keyboard.current == null || Kit1AutomaticSequence.IsRunning)
            {
                return;
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                SelectMaterial(WorkpieceMaterial.Metal);
            }

            if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                SelectMaterial(WorkpieceMaterial.Plastic);
            }
        }

        private void SelectMaterial(WorkpieceMaterial selectedMaterial)
        {
            if (workpieceFlow.IsFed)
            {
                Debug.LogWarning("Reset the workpiece before changing its material type.");
                return;
            }

            material = selectedMaterial;
            ApplyMaterialAppearance();
            Debug.Log($"Workpiece material selected: {material}.");
        }

        public void SetAutomaticMaterial(WorkpieceMaterial selectedMaterial)
        {
            material = selectedMaterial;
            ApplyMaterialAppearance();
        }

        private void ApplyMaterialAppearance()
        {
            Color color = material == WorkpieceMaterial.Metal
                ? new Color(0.15f, 0.72f, 1f, 1f)
                : new Color(1f, 0.58f, 0.12f, 1f);
            workpieceFlow.SetWorkpieceColor(color);
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
                normal = { textColor = new Color(0.35f, 1f, 0.65f) }
            };
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };
            smallStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.82f, 0.86f, 0.9f) }
            };

            Rect panel = new Rect(1002f, 490f, 470f, 340f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(1018f, 502f, 430f, 32f), "SIMULATED PLC INPUTS", headingStyle);
            GUI.Label(
                new Rect(1018f, 538f, 430f, 28f),
                $"Workpiece: {material.ToString().ToUpperInvariant()}",
                textStyle);

            if (GUI.Button(new Rect(1018f, 570f, 150f, 36f), "1 - METAL"))
            {
                SelectMaterial(WorkpieceMaterial.Metal);
            }

            if (GUI.Button(new Rect(1180f, 570f, 150f, 36f), "2 - PLASTIC"))
            {
                SelectMaterial(WorkpieceMaterial.Plastic);
            }

            DrawSignal(1018f, 620f, "Magazine part present", WorkpieceAtMagazine);
            DrawSignal(1018f, 646f, "Part on lift", WorkpieceOnLift);
            DrawSignal(1018f, 672f, "Inductive / metal", MetalDetected);
            DrawSignal(1018f, 698f, "Capacitive / plastic", PlasticDetected);
            DrawSignal(1240f, 620f, "Lift upper reed", LiftUp);
            DrawSignal(1240f, 646f, "Lift lower reed", LiftDown);
            DrawSignal(1240f, 672f, "Cyl. 1 extended", FeedExtended);
            DrawSignal(1240f, 698f, "Cyl. 1 retracted", FeedRetracted);
            DrawSignal(1018f, 736f, "Cyl. 2 extended", LowerEjectorExtended);
            DrawSignal(1018f, 762f, "Cyl. 2 retracted", LowerEjectorRetracted);
            DrawSignal(1240f, 736f, "Cyl. 3 extended", UpperEjectorExtended);
            DrawSignal(1240f, 762f, "Cyl. 3 retracted", UpperEjectorRetracted);
            DrawSignal(1018f, 788f, "Workpiece released", workpieceFlow.ReleasedToGravity);
            DrawSignal(1018f, 814f, "Magazine empty", MagazineEmpty);

            GUI.Label(
                new Rect(1240f, 801f, 210f, 24f),
                "Green = input ON    Grey = input OFF",
                smallStyle);
        }

        private void DrawSignal(float x, float y, string label, bool value)
        {
            Color previousColor = GUI.color;
            GUI.color = value ? new Color(0.2f, 1f, 0.42f) : new Color(0.25f, 0.28f, 0.32f);
            GUI.DrawTexture(new Rect(x, y + 5f, 14f, 14f), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 21f, y, 200f, 24f), label, smallStyle);
        }
    }
}
