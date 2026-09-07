using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class SecondCylinderTest : MonoBehaviour
    {
        private const string RodComponentName = "Component_026";
        private const string PusherComponentName = "Component_055";
        private const float StrokeMetres = 0.05f;
        private const float TravelSpeedMetresPerSecond = 0.12f;
        private const float AutoCyclePauseSeconds = 0.75f;

        private Transform rod;
        private Transform pusher;
        private Vector3 rodHomeWorldPosition;
        private Vector3 pusherHomeWorldPosition;
        private Vector3 travelWorldDirection;
        private float position;
        private float targetPosition;
        private float autoCyclePause;
        private bool autoCycle;
        private ElectropneumaticUtilities utilities;
        private GUIStyle headingStyle;
        private GUIStyle textStyle;

        public float TravelDistanceMetres => position * StrokeMetres;
        public Vector3 TravelWorldDirection => travelWorldDirection;
        public float NormalizedPosition => position;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SecondCylinderTest>() == null)
            {
                new GameObject("Second Cylinder Motion Test").AddComponent<SecondCylinderTest>();
            }
        }

        private void Start()
        {
            GameObject rodObject = GameObject.Find(RodComponentName);
            GameObject pusherObject = GameObject.Find(PusherComponentName);
            if (rodObject == null || pusherObject == null)
            {
                Debug.LogError(
                    $"Cylinder 2 requires {RodComponentName} and {PusherComponentName}.");
                enabled = false;
                return;
            }

            rod = rodObject.transform;
            pusher = pusherObject.transform;
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();
            rodHomeWorldPosition = rod.position;
            pusherHomeWorldPosition = pusher.position;

            // The cylinder body lies behind the rod toward positive source Y.
            // Extension therefore travels toward negative source Y.
            travelWorldDirection = -rod.TransformDirection(Vector3.up).normalized;
            Debug.Log(
                $"Cylinder 2 attached to {RodComponentName} + {PusherComponentName}. " +
                $"Auto-cycling over {StrokeMetres * 1000f:0} mm along {travelWorldDirection}.");
        }

        private void Update()
        {
            if (rod == null || pusher == null)
            {
                return;
            }

            if (!Kit1AutomaticSequence.IsRunning && Keyboard.current != null)
            {
                if (Keyboard.current.tKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = 1f;
                }

                if (Keyboard.current.gKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = 0f;
                }

                if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = targetPosition > 0.5f ? 0f : 1f;
                }
            }

            float speedFactor = utilities != null
                ? utilities.GetMotionSpeedFactor(SimulatedActuatorFault.Cylinder2)
                : 1f;
            float normalizedSpeed = TravelSpeedMetresPerSecond / StrokeMetres * speedFactor;
            position = Mathf.MoveTowards(position, targetPosition, normalizedSpeed * Time.deltaTime);
            Vector3 travelOffset = travelWorldDirection * (position * StrokeMetres);
            rod.position = rodHomeWorldPosition + travelOffset;
            pusher.position = pusherHomeWorldPosition + travelOffset;

            if (!autoCycle)
            {
                return;
            }

            if (Mathf.Approximately(position, targetPosition))
            {
                autoCyclePause += Time.deltaTime;
                if (autoCyclePause >= AutoCyclePauseSeconds)
                {
                    targetPosition = targetPosition > 0.5f ? 0f : 1f;
                    autoCyclePause = 0f;
                }
            }
            else
            {
                autoCyclePause = 0f;
            }
        }

        public void CommandRetract()
        {
            autoCycle = false;
            targetPosition = 0f;
        }

        public void CommandExtend()
        {
            autoCycle = false;
            targetPosition = 1f;
        }

        public void ApplyReplayPosition(float normalizedPosition)
        {
            if (rod == null || pusher == null)
            {
                return;
            }

            position = Mathf.Clamp01(normalizedPosition);
            targetPosition = position;
            Vector3 travelOffset = travelWorldDirection * (position * StrokeMetres);
            rod.position = rodHomeWorldPosition + travelOffset;
            pusher.position = pusherHomeWorldPosition + travelOffset;
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
                normal = { textColor = new Color(0.45f, 1f, 0.45f) }
            };
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(18f, 490f, 470f, 200f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(34f, 502f, 430f, 32f), "CYLINDER 2 - CALIBRATED", headingStyle);
            GUI.Label(
                new Rect(34f, 537f, 430f, 52f),
                $"Assembly: {RodComponentName} + {PusherComponentName}    Stroke: {StrokeMetres * 1000f:0} mm\n" +
                $"Position: {position * 100f:0}%    Auto cycle: {(autoCycle ? "ON" : "OFF")}",
                textStyle);

            if (GUI.Button(new Rect(34f, 595f, 130f, 42f), "RETRACT (G)"))
            {
                autoCycle = false;
                targetPosition = 0f;
            }

            if (GUI.Button(new Rect(178f, 595f, 130f, 42f), "EXTEND (T)"))
            {
                autoCycle = false;
                targetPosition = 1f;
            }

            if (GUI.Button(new Rect(322f, 595f, 142f, 42f), "TOGGLE (Y)"))
            {
                autoCycle = false;
                targetPosition = targetPosition > 0.5f ? 0f : 1f;
            }

            bool requestedAutoCycle = GUI.Toggle(
                new Rect(34f, 645f, 220f, 30f), autoCycle, " Automatic cycle");
            if (requestedAutoCycle != autoCycle)
            {
                autoCycle = requestedAutoCycle;
                autoCyclePause = 0f;
                if (autoCycle)
                {
                    targetPosition = position > 0.5f ? 0f : 1f;
                }
            }
        }
    }
}
