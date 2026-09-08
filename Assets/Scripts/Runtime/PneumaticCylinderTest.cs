using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class PneumaticCylinderTest : MonoBehaviour
    {
        private const string RodComponentName = "Component_025";
        private const string PusherComponentName = "Component_028";
        private const float StrokeMetres = 0.04f;
        private const float TravelSpeedMetresPerSecond = 0.12f;
        private const float AutoCyclePauseSeconds = 0.75f;

        private Transform rod;
        private Transform pusher;
        private Vector3 homeWorldPosition;
        private Vector3 pusherHomeWorldPosition;
        private Vector3 travelWorldDirection;
        private float position;
        private float targetPosition;
        private float autoCyclePause;
        private bool autoCycle;
        private ElectropneumaticUtilities utilities;
        private GUIStyle headingStyle;
        private GUIStyle textStyle;

        public float NormalizedPosition => position;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindFirstObjectByType<PneumaticCylinderTest>() == null)
            {
                new GameObject("Front Cylinder Motion Test").AddComponent<PneumaticCylinderTest>();
            }
        }

        private void Start()
        {
            GameObject rodObject = GameObject.Find(RodComponentName);
            if (rodObject == null)
            {
                Debug.LogError($"Cylinder test could not find {RodComponentName}.");
                enabled = false;
                return;
            }

            rod = rodObject.transform;
            GameObject pusherObject = GameObject.Find(PusherComponentName);
            if (pusherObject == null)
            {
                Debug.LogError($"Cylinder test could not find attached pusher {PusherComponentName}.");
                enabled = false;
                return;
            }

            pusher = pusherObject.transform;
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();
            homeWorldPosition = rod.position;
            pusherHomeWorldPosition = pusher.position;
            // The rod's positive CAD Y-axis points back into the cylinder, so extension
            // must use the opposite direction toward the front of the machine.
            travelWorldDirection = -rod.TransformDirection(Vector3.up).normalized;
            Debug.Log(
                $"Front cylinder assembly attached to {RodComponentName} + {PusherComponentName}. " +
                $"Auto-cycling over a {StrokeMetres * 1000f:0} mm world-space stroke along {travelWorldDirection}.");
        }

        private void Update()
        {
            if (rod == null)
            {
                return;
            }

            if (!Kit1AutomaticSequence.IsRunning && Keyboard.current != null)
            {
                if (Keyboard.current.eKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    Extend();
                }

                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    Retract();
                }

                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    autoCycle = false;
                    targetPosition = targetPosition > 0.5f ? 0f : 1f;
                }
            }

            float speedFactor = utilities != null
                ? utilities.GetMotionSpeedFactor(SimulatedActuatorFault.Cylinder1)
                : 1f;
            float normalizedSpeed = TravelSpeedMetresPerSecond / StrokeMetres * speedFactor;
            position = Mathf.MoveTowards(position, targetPosition, normalizedSpeed * Time.deltaTime);

            // The imported OBJ has a 0.001 scale on an ancestor. Applying the stroke
            // in world space keeps the requested millimetre travel physically correct.
            Vector3 travelOffset = travelWorldDirection * (position * StrokeMetres);
            rod.position = homeWorldPosition + travelOffset;
            pusher.position = pusherHomeWorldPosition + travelOffset;

            if (autoCycle)
            {
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
        }

        private void Extend()
        {
            targetPosition = 1f;
        }

        private void Retract()
        {
            targetPosition = 0f;
        }

        public void CommandRetract()
        {
            autoCycle = false;
            Retract();
        }

        public void CommandExtend()
        {
            autoCycle = false;
            Extend();
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
            rod.position = homeWorldPosition + travelOffset;
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
                normal = { textColor = new Color(0.2f, 0.82f, 1f) }
            };
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(18f, 280f, 470f, 200f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(34f, 292f, 430f, 32f), "CYLINDER 1 - CALIBRATED", headingStyle);
            GUI.Label(
                new Rect(34f, 327f, 430f, 52f),
                $"Assembly: {RodComponentName} + {PusherComponentName}    Stroke: {StrokeMetres * 1000f:0} mm\n" +
                $"Position: {position * 100f:0}%    Auto cycle: {(autoCycle ? "ON" : "OFF")}",
                textStyle);

            if (GUI.Button(new Rect(34f, 385f, 130f, 42f), "RETRACT (R)"))
            {
                autoCycle = false;
                Retract();
            }

            if (GUI.Button(new Rect(178f, 385f, 130f, 42f), "EXTEND (E)"))
            {
                autoCycle = false;
                Extend();
            }

            if (GUI.Button(new Rect(322f, 385f, 142f, 42f), "TOGGLE (Space)"))
            {
                autoCycle = false;
                targetPosition = targetPosition > 0.5f ? 0f : 1f;
            }

            bool requestedAutoCycle = GUI.Toggle(
                new Rect(34f, 435f, 220f, 30f), autoCycle, " Automatic cycle");
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
