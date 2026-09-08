using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class Kit2FeedCylinderCalibration : MonoBehaviour
    {
        private const string RodComponentName = "Component_029";
        private const string PusherComponentName = "Component_018";
        private const float StrokeMetres = 0.05f;
        private const float TravelSpeedMetresPerSecond = 0.12f;

        private Transform rod;
        private Transform pusher;
        private Vector3 rodHomePosition;
        private Vector3 pusherHomePosition;
        private Vector3 extensionDirection;
        private float position;
        private float targetPosition;
        private GUIStyle headingStyle;
        private GUIStyle detailStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2FeedCylinderCalibration>() == null)
            {
                new GameObject("Kit 2 Feed Cylinder Calibration")
                    .AddComponent<Kit2FeedCylinderCalibration>();
            }
        }

        private void Start()
        {
            rod = GameObject.Find(RodComponentName)?.transform;
            pusher = GameObject.Find(PusherComponentName)?.transform;
            if (rod == null || pusher == null)
            {
                Debug.LogError(
                    $"Kit 2 feed cylinder requires {RodComponentName} and {PusherComponentName}.");
                enabled = false;
                return;
            }

            rodHomePosition = rod.position;
            pusherHomePosition = pusher.position;

            // Both parts are modelled along CAD Y. Negative CAD Y points from the
            // cylinder body toward the stamping area.
            extensionDirection = -rod.TransformDirection(Vector3.up).normalized;
            Debug.Log(
                $"KIT2_CYLINDER_1_READY: {RodComponentName} + {PusherComponentName}, " +
                $"{StrokeMetres * 1000f:0} mm calibration stroke along {extensionDirection}.");
        }

        private void Update()
        {
            if (rod == null)
            {
                return;
            }

            if (Keyboard.current != null)
            {
                if (Keyboard.current.eKey.wasPressedThisFrame)
                {
                    targetPosition = 1f;
                }

                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    targetPosition = 0f;
                }

                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    targetPosition = targetPosition > 0.5f ? 0f : 1f;
                }
            }

            position = Mathf.MoveTowards(
                position,
                targetPosition,
                TravelSpeedMetresPerSecond / StrokeMetres * Time.deltaTime);
            Vector3 offset = extensionDirection * (StrokeMetres * position);
            rod.position = rodHomePosition + offset;
            pusher.position = pusherHomePosition + offset;
        }

        private void OnGUI()
        {
            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 0.82f, 1f) }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(Screen.width - 420f, 18f, 400f, 128f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;

            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 10f, 370f, 30f),
                "KIT 2 · CYLINDER 1 CALIBRATION",
                headingStyle);
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 44f, 370f, 72f),
                $"Rod {RodComponentName} + pusher {PusherComponentName}\n" +
                $"Stroke: 50 mm   Position: {position * 100f:0}%\n" +
                "E: extend   R: retract   Space: toggle",
                detailStyle);
        }
    }
}
