using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class Kit2SecondCylinderCalibration : MonoBehaviour
    {
        private const string RodComponentName = "Component_030";
        private const string PusherComponentName = "Component_031";
        private const float StageStrokeMetres = 0.056f;
        private const float EjectStrokeMetres = 0.09f;
        private const float TravelSpeedMetresPerSecond = 0.12f;

        private Transform rod;
        private Transform pusher;
        private Vector3 rodHomePosition;
        private Vector3 pusherHomePosition;
        private Vector3 extensionDirection;
        private Kit2ElectropneumaticUtilities utilities;
        private float position;
        private float targetPosition;
        private GUIStyle headingStyle;
        private GUIStyle detailStyle;

        public float NormalizedPosition => position / StageStrokeMetres;
        public float TravelMetres => position;
        public bool IsHome => position <= 0.0005f;
        public bool IsAtStage => Mathf.Abs(position - StageStrokeMetres) <= 0.0005f;
        public bool IsAtEject => position >= EjectStrokeMetres - 0.0005f;
        public bool EjectCommanded => targetPosition > StageStrokeMetres + 0.001f;
        public Vector3 CurrentTravelOffset =>
            extensionDirection * position;
        public Vector3 StageTravelOffset =>
            extensionDirection * StageStrokeMetres;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2SecondCylinderCalibration>() == null)
            {
                new GameObject("Kit 2 Second Cylinder Calibration")
                    .AddComponent<Kit2SecondCylinderCalibration>();
            }
        }

        private void Start()
        {
            rod = GameObject.Find(RodComponentName)?.transform;
            pusher = GameObject.Find(PusherComponentName)?.transform;
            if (rod == null || pusher == null)
            {
                Debug.LogError(
                    $"Kit 2 cylinder 2 requires {RodComponentName} and {PusherComponentName}.");
                enabled = false;
                return;
            }

            rodHomePosition = rod.position;
            pusherHomePosition = pusher.position;
            utilities = FindFirstObjectByType<Kit2ElectropneumaticUtilities>();

            // For this assembly, positive CAD X is the physical extension direction.
            extensionDirection = rod.TransformDirection(Vector3.right).normalized;
            Debug.Log(
                $"KIT2_CYLINDER_2_READY: {RodComponentName} + {PusherComponentName}, " +
                $"56 mm stage / 90 mm eject travel along {extensionDirection}.");
        }

        private void Update()
        {
            if (rod == null)
            {
                return;
            }

            if (Keyboard.current != null && !Kit2AutomaticSequence.IsRunning)
            {
                if (Keyboard.current.tKey.wasPressedThisFrame)
                {
                    bool ejectRequested = Keyboard.current.leftShiftKey.isPressed ||
                        Keyboard.current.rightShiftKey.isPressed;
                    targetPosition = ejectRequested
                        ? EjectStrokeMetres
                        : StageStrokeMetres;
                }

                if (Keyboard.current.gKey.wasPressedThisFrame)
                {
                    targetPosition = 0f;
                }

                if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    targetPosition = targetPosition > StageStrokeMetres * 0.5f
                        ? 0f
                        : StageStrokeMetres;
                }
            }

            position = Mathf.MoveTowards(
                position,
                targetPosition,
                TravelSpeedMetresPerSecond *
                (utilities != null
                    ? utilities.GetMotionFactor(Kit2ActuatorFault.TransferCylinder)
                    : 1f) * Time.deltaTime);
            Vector3 offset = extensionDirection * position;
            rod.position = rodHomePosition + offset;
            pusher.position = pusherHomePosition + offset;
        }

        public void CommandExtend()
        {
            targetPosition = StageStrokeMetres;
        }

        public void CommandEject()
        {
            targetPosition = EjectStrokeMetres;
        }

        public void CommandRetract()
        {
            targetPosition = 0f;
        }

        private void OnGUI()
        {
            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.66f, 0.18f) }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(Screen.width - 420f, 158f, 400f, 128f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;

            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 10f, 370f, 30f),
                "KIT 2 · CYLINDER 2 CALIBRATION",
                headingStyle);
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 44f, 370f, 72f),
                $"Rod {RodComponentName} + pusher {PusherComponentName}\n" +
                $"Stage/Eject: 56/90 mm   Position: {position * 1000f:0} mm\n" +
                "T: stage   Shift+T: eject   G: retract",
                detailStyle);
        }
    }
}
