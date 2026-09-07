using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class LiftCylinderTest : MonoBehaviour
    {
        private const string RodComponentName = "Component_029";
        private const string PlatformComponentName = "Component_052";
        private const string FixedBodyComponentName = "Component_063";
        public const float LiftStrokeMetres = 0.0312f;
        private const float TravelSpeedMetresPerSecond = 0.08f;
        private const float AutoCyclePauseSeconds = 0.9f;

        private Transform rod;
        private Transform platform;
        private Vector3 rodDownWorldPosition;
        private Vector3 platformDownWorldPosition;
        private Vector3 liftWorldDirection;
        private float position;
        private float targetPosition;
        private float autoCyclePause;
        private bool autoCycle;
        private ElectropneumaticUtilities utilities;
        private GUIStyle headingStyle;
        private GUIStyle textStyle;

        public float NormalizedPosition => position;
        public Transform Platform => platform;
        public Vector3 LiftWorldDirection => liftWorldDirection;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<LiftCylinderTest>() == null)
            {
                new GameObject("Vertical Lift Motion Test").AddComponent<LiftCylinderTest>();
            }
        }

        private void Start()
        {
            GameObject rodObject = GameObject.Find(RodComponentName);
            GameObject platformObject = GameObject.Find(PlatformComponentName);
            GameObject fixedBodyObject = GameObject.Find(FixedBodyComponentName);
            if (rodObject == null || platformObject == null || fixedBodyObject == null)
            {
                Debug.LogError(
                    $"Lift requires moving {RodComponentName} + {PlatformComponentName}, " +
                    $"with fixed body {FixedBodyComponentName}.");
                enabled = false;
                return;
            }

            rod = rodObject.transform;
            platform = platformObject.transform;
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();
            rodDownWorldPosition = rod.position;
            platformDownWorldPosition = platform.position;

            // Source CAD Z is the lift's vertical axis. The imported model rotation
            // converts this direction to Unity world-up.
            liftWorldDirection = rod.TransformDirection(Vector3.forward).normalized;
            targetPosition = 1f;

            Debug.Log(
                $"Vertical lift attached to {RodComponentName} + {PlatformComponentName}; " +
                $"{FixedBodyComponentName} remains fixed. Stroke: {LiftStrokeMetres * 1000f:0.0} mm.");
        }

        private void Update()
        {
            if (rod == null || platform == null)
            {
                return;
            }

            if (!Kit1AutomaticSequence.IsRunning && Keyboard.current != null)
            {
                if (Keyboard.current.vKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = 1f;
                }

                if (Keyboard.current.cKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = 0f;
                }

                if (Keyboard.current.bKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = targetPosition > 0.5f ? 0f : 1f;
                }
            }

            float speedFactor = utilities != null
                ? utilities.GetMotionSpeedFactor(SimulatedActuatorFault.VerticalLift)
                : 1f;
            float normalizedSpeed = TravelSpeedMetresPerSecond / LiftStrokeMetres * speedFactor;
            position = Mathf.MoveTowards(position, targetPosition, normalizedSpeed * Time.deltaTime);
            Vector3 travelOffset = liftWorldDirection * (position * LiftStrokeMetres);
            rod.position = rodDownWorldPosition + travelOffset;
            platform.position = platformDownWorldPosition + travelOffset;

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

        public void CommandUp()
        {
            autoCycle = false;
            targetPosition = 1f;
        }

        public void CommandDown()
        {
            autoCycle = false;
            targetPosition = 0f;
        }

        public void ApplyReplayPosition(float normalizedPosition)
        {
            if (rod == null || platform == null)
            {
                return;
            }

            position = Mathf.Clamp01(normalizedPosition);
            targetPosition = position;
            Vector3 travelOffset = liftWorldDirection * (position * LiftStrokeMetres);
            rod.position = rodDownWorldPosition + travelOffset;
            platform.position = platformDownWorldPosition + travelOffset;
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
                normal = { textColor = new Color(0.82f, 0.45f, 1f) }
            };
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(1002f, 280f, 470f, 200f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(1018f, 292f, 430f, 32f), "VERTICAL LIFT - CALIBRATION", headingStyle);
            GUI.Label(
                new Rect(1018f, 327f, 430f, 52f),
                $"Assembly: {RodComponentName} + {PlatformComponentName}    Stroke: {LiftStrokeMetres * 1000f:0.0} mm\n" +
                $"Position: {position * 100f:0}%    Auto cycle: {(autoCycle ? "ON" : "OFF")}",
                textStyle);

            if (GUI.Button(new Rect(1018f, 385f, 130f, 42f), "DOWN (C)"))
            {
                autoCycle = false;
                targetPosition = 0f;
            }

            if (GUI.Button(new Rect(1162f, 385f, 130f, 42f), "UP (V)"))
            {
                autoCycle = false;
                targetPosition = 1f;
            }

            if (GUI.Button(new Rect(1306f, 385f, 142f, 42f), "TOGGLE (B)"))
            {
                autoCycle = false;
                targetPosition = targetPosition > 0.5f ? 0f : 1f;
            }

            bool requestedAutoCycle = GUI.Toggle(
                new Rect(1018f, 435f, 220f, 30f), autoCycle, " Automatic cycle");
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
