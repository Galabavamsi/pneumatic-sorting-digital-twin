using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class ThirdCylinderTest : MonoBehaviour
    {
        private const string RodComponentName = "Component_027";
        private const string PusherComponentName = "Component_056";
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
            if (FindFirstObjectByType<ThirdCylinderTest>() == null)
            {
                new GameObject("Third Cylinder Motion Test").AddComponent<ThirdCylinderTest>();
            }
        }

        private void Start()
        {
            GameObject rodObject = GameObject.Find(RodComponentName);
            GameObject pusherObject = GameObject.Find(PusherComponentName);
            if (rodObject == null || pusherObject == null)
            {
                Debug.LogError(
                    $"Cylinder 3 requires {RodComponentName} and {PusherComponentName}.");
                enabled = false;
                return;
            }

            rod = rodObject.transform;
            pusher = pusherObject.transform;
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();
            rodHomeWorldPosition = rod.position;
            pusherHomeWorldPosition = pusher.position;

            // Extension travels from the side cylinder toward the central sorting tower.
            travelWorldDirection = rod.TransformDirection(Vector3.right).normalized;
            Debug.Log(
                $"Cylinder 3 attached to {RodComponentName} + {PusherComponentName}. " +
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
                if (Keyboard.current.uKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = 1f;
                }

                if (Keyboard.current.jKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = 0f;
                }

                if (Keyboard.current.iKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = targetPosition > 0.5f ? 0f : 1f;
                }
            }

            float speedFactor = utilities != null
                ? utilities.GetMotionSpeedFactor(SimulatedActuatorFault.Cylinder3)
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
                normal = { textColor = new Color(1f, 0.72f, 0.25f) }
            };
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(510f, 280f, 470f, 200f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(526f, 292f, 430f, 32f), "CYLINDER 3 - CALIBRATED", headingStyle);
            GUI.Label(
                new Rect(526f, 327f, 430f, 52f),
                $"Assembly: {RodComponentName} + {PusherComponentName}    Stroke: {StrokeMetres * 1000f:0} mm\n" +
                $"Position: {position * 100f:0}%    Auto cycle: {(autoCycle ? "ON" : "OFF")}",
                textStyle);

            if (GUI.Button(new Rect(526f, 385f, 130f, 42f), "RETRACT (J)"))
            {
                autoCycle = false;
                targetPosition = 0f;
            }

            if (GUI.Button(new Rect(670f, 385f, 130f, 42f), "EXTEND (U)"))
            {
                autoCycle = false;
                targetPosition = 1f;
            }

            if (GUI.Button(new Rect(814f, 385f, 142f, 42f), "TOGGLE (I)"))
            {
                autoCycle = false;
                targetPosition = targetPosition > 0.5f ? 0f : 1f;
            }

            bool requestedAutoCycle = GUI.Toggle(
                new Rect(526f, 435f, 220f, 30f), autoCycle, " Automatic cycle");
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
