using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class Kit2StampingCylinderCalibration : MonoBehaviour
    {
        private const float StrokeMetres = 0.03f;
        private const float TravelSpeedMetresPerSecond = 0.08f;

        private StampAxis stampA;
        private StampAxis stampB;
        private Kit2ElectropneumaticUtilities utilities;
        private GUIStyle headingStyle;
        private GUIStyle detailStyle;

        public float StampAPosition => stampA?.Position ?? 0f;
        public float StampBPosition => stampB?.Position ?? 0f;
        public bool StampAHome => StampAPosition <= 0.01f;
        public bool StampBHome => StampBPosition <= 0.01f;
        public bool StampADown => StampAPosition >= 0.99f;
        public bool StampBDown => StampBPosition >= 0.99f;

        private sealed class StampAxis
        {
            public readonly string RodName;
            public readonly string ToolName;
            public Transform Rod;
            public Transform Tool;
            public Vector3 RodHome;
            public Vector3 ToolHome;
            public Vector3 Direction;
            public float Position;
            public float Target;

            public StampAxis(string rodName, string toolName)
            {
                RodName = rodName;
                ToolName = toolName;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2StampingCylinderCalibration>() == null)
            {
                new GameObject("Kit 2 Stamping Cylinder Calibration")
                    .AddComponent<Kit2StampingCylinderCalibration>();
            }
        }

        private void Start()
        {
            stampA = CreateAxis("Component_032", "Component_034");
            stampB = CreateAxis("Component_033", "Component_035");
            utilities = FindFirstObjectByType<Kit2ElectropneumaticUtilities>();
            if (stampA == null || stampB == null)
            {
                enabled = false;
                return;
            }

            Debug.Log(
                "KIT2_STAMPS_READY: A Component_032 + Component_034; " +
                "B Component_033 + Component_035; 30 mm downward calibration stroke.");
        }

        private static StampAxis CreateAxis(string rodName, string toolName)
        {
            StampAxis axis = new StampAxis(rodName, toolName)
            {
                Rod = GameObject.Find(rodName)?.transform,
                Tool = GameObject.Find(toolName)?.transform
            };

            if (axis.Rod == null || axis.Tool == null)
            {
                Debug.LogError($"Kit 2 stamp requires {rodName} and {toolName}.");
                return null;
            }

            axis.RodHome = axis.Rod.position;
            axis.ToolHome = axis.Tool.position;
            axis.Direction = -axis.Rod.TransformDirection(Vector3.forward).normalized;
            return axis;
        }

        private void Update()
        {
            if (Keyboard.current != null && !Kit2AutomaticSequence.IsRunning)
            {
                if (Keyboard.current.uKey.wasPressedThisFrame)
                {
                    stampA.Target = 1f;
                }
                if (Keyboard.current.jKey.wasPressedThisFrame)
                {
                    stampA.Target = 0f;
                }
                if (Keyboard.current.iKey.wasPressedThisFrame)
                {
                    stampA.Target = stampA.Target > 0.5f ? 0f : 1f;
                }

                if (Keyboard.current.vKey.wasPressedThisFrame)
                {
                    stampB.Target = 1f;
                }
                if (Keyboard.current.cKey.wasPressedThisFrame)
                {
                    stampB.Target = 0f;
                }
                if (Keyboard.current.bKey.wasPressedThisFrame)
                {
                    stampB.Target = stampB.Target > 0.5f ? 0f : 1f;
                }
            }

            MoveAxis(stampA, utilities != null
                ? utilities.GetMotionFactor(Kit2ActuatorFault.StampA)
                : 1f);
            MoveAxis(stampB, utilities != null
                ? utilities.GetMotionFactor(Kit2ActuatorFault.StampB)
                : 1f);
        }

        private static void MoveAxis(StampAxis axis, float speedFactor)
        {
            axis.Position = Mathf.MoveTowards(
                axis.Position,
                axis.Target,
                TravelSpeedMetresPerSecond / StrokeMetres * speedFactor * Time.deltaTime);
            Vector3 offset = axis.Direction * (StrokeMetres * axis.Position);
            axis.Rod.position = axis.RodHome + offset;
            axis.Tool.position = axis.ToolHome + offset;
        }

        public void CommandStampADown() => stampA.Target = 1f;

        public void CommandStampAUp() => stampA.Target = 0f;

        public void CommandStampBDown() => stampB.Target = 1f;

        public void CommandStampBUp() => stampB.Target = 0f;

        public void CommandSafeHome()
        {
            stampA.Target = 0f;
            stampB.Target = 0f;
        }

        private void OnGUI()
        {
            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.65f, 0.95f, 0.32f) }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = Color.white }
            };

            DrawAxisPanel(
                stampA,
                Screen.width - 420f,
                298f,
                "STAMP A · VERTICAL CYLINDER",
                "U: down   J: up   I: toggle");
            DrawAxisPanel(
                stampB,
                Screen.width - 420f,
                426f,
                "STAMP B · VERTICAL CYLINDER",
                "V: down   C: up   B: toggle");
        }

        private void DrawAxisPanel(
            StampAxis axis,
            float x,
            float y,
            string title,
            string controls)
        {
            if (axis == null)
            {
                return;
            }

            Rect panel = new Rect(x, y, 400f, 116f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.Label(new Rect(x + 16f, y + 8f, 370f, 28f), title, headingStyle);
            GUI.Label(
                new Rect(x + 16f, y + 39f, 370f, 68f),
                $"{axis.RodName} + {axis.ToolName}   Stroke: {StrokeMetres * 1000f:0} mm\n" +
                $"Position: {axis.Position * 100f:0}%\n{controls}",
                detailStyle);
        }
    }
}
