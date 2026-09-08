using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class Kit3RotaryCalibration : MonoBehaviour
    {
        private const float Bin2AngleDegrees = -90f;
        private const float RotationSpeedDegreesPerSecond = 60f;
        private const float SlideStrokeMetres = 0.05f;
        private const float SlideSpeedMetresPerSecond = 0.12f;
        private const float LiftStrokeMetres = 0.02f;
        private const float LiftSpeedMetresPerSecond = 0.08f;
        private const float InitialApproachHeightMetres = 0.005f;

        private static readonly string[] RotatingComponentNames =
        {
            "Component_014", "Component_020", "Component_022", "Component_023",
            "Component_024",
            "Component_031", "Component_033", "Component_036", "Component_040",
            "Component_041", "Component_046", "Component_048", "Component_069"
        };

        private static readonly HashSet<string> SlidingComponentNames =
            new HashSet<string>
            {
                "Component_033", "Component_040", "Component_041"
            };

        private static readonly HashSet<string> LiftedComponentNames =
            new HashSet<string>
            {
                "Component_014", "Component_020", "Component_022",
                "Component_031", "Component_033", "Component_040",
                "Component_041", "Component_046", "Component_069"
            };

        private readonly List<MovingPart> rotatingParts = new List<MovingPart>();
        private Vector3 pivot;
        private Vector3 slideDirectionHome;
        private float currentAngle;
        private float targetAngle;
        private float slidePosition;
        private float slideTarget;
        private float liftPosition;
        private float liftTarget;
        private float liftContactOffset;
        private GUIStyle headingStyle;
        private GUIStyle detailStyle;

        public float CurrentAngleDegrees => currentAngle;
        public bool IsAtBin1 => Mathf.Abs(currentAngle) <= 0.2f;
        public bool IsAtBin2 => Mathf.Abs(currentAngle - Bin2AngleDegrees) <= 0.2f;
        public float SlideTravelMetres => slidePosition;
        public bool SlideIsHome => slidePosition <= 0.0005f;
        public bool SlideIsExtended => slidePosition >= SlideStrokeMetres - 0.0005f;
        public float LiftTravelMetres => liftPosition;
        public bool LiftIsDown =>
            liftPosition <= InitialApproachHeightMetres + 0.0005f;
        public bool LiftIsUp => liftPosition >= LiftStrokeMetres - 0.0005f;

        private sealed class MovingPart
        {
            public Transform Transform;
            public Vector3 HomePosition;
            public Quaternion HomeRotation;
            public bool Slides;
            public bool Lifts;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit3_AssemblyStation") != null &&
                FindAnyObjectByType<Kit3RotaryCalibration>() == null)
            {
                new GameObject("Kit 3 Rotary Calibration")
                    .AddComponent<Kit3RotaryCalibration>();
            }
        }

        private void Start()
        {
            GameObject station = GameObject.Find("Kit3_AssemblyStation");
            if (station == null)
            {
                enabled = false;
                return;
            }

            Dictionary<string, Transform> components = station
                .GetComponentsInChildren<Transform>(true)
                .GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (string componentName in RotatingComponentNames)
            {
                if (!components.TryGetValue(componentName, out Transform component))
                {
                    Debug.LogError($"KIT3_ROTARY_MISSING: {componentName}");
                    continue;
                }
                rotatingParts.Add(new MovingPart
                {
                    Transform = component,
                    HomePosition = component.position,
                    HomeRotation = component.rotation,
                    Slides = SlidingComponentNames.Contains(componentName),
                    Lifts = LiftedComponentNames.Contains(componentName)
                });
            }

            if (!components.TryGetValue("Component_048", out Transform turntable))
            {
                Debug.LogError("Kit 3 rotary calibration requires Component_048.");
                enabled = false;
                return;
            }

            Renderer turntableRenderer = turntable.GetComponentInChildren<Renderer>();
            pivot = turntableRenderer != null
                ? turntableRenderer.bounds.center
                : turntable.position;

            Transform pickup = components["Component_033"];
            Transform guide = components["Component_040"];
            Transform actuatorBody = components["Component_069"];

            // Calibrate the down endpoint from the CAD surfaces.  The imported
            // home pose puts the cup slightly inside the first workpiece.
            Renderer pickupRenderer = pickup.GetComponentInChildren<Renderer>();
            if (components.TryGetValue("Component_050", out Transform workpiece))
            {
                Renderer workpieceRenderer = workpiece.GetComponentInChildren<Renderer>();
                if (pickupRenderer != null && workpieceRenderer != null)
                {
                    const float ContactClearanceMetres = 0.0003f;
                    liftContactOffset = Mathf.Max(
                        0f,
                        workpieceRenderer.bounds.max.y -
                        pickupRenderer.bounds.min.y +
                        ContactClearanceMetres);
                }
            }

            // Follow the guide's actual CAD axis.  A pivot-to-pickup vector is
            // diagonal because the suction head is laterally offset from the
            // rotary centre, so it cannot be used as the cylinder stroke axis.
            slideDirectionHome = Vector3.ProjectOnPlane(guide.right, Vector3.up)
                .normalized;
            Vector3 outwardDirection = Vector3.ProjectOnPlane(
                pickup.position - actuatorBody.position,
                Vector3.up);
            if (Vector3.Dot(slideDirectionHome, outwardDirection) < 0f)
            {
                slideDirectionHome = -slideDirectionHome;
            }

            if (slideDirectionHome.sqrMagnitude < 0.99f)
            {
                Debug.LogError("KIT3_SLIDE_AXIS_INVALID: Component_040 has no horizontal rail axis.");
                enabled = false;
                return;
            }
            liftPosition = InitialApproachHeightMetres;
            liftTarget = InitialApproachHeightMetres;
            Debug.Log(
                $"KIT3_ROTARY_READY: {rotatingParts.Count} attached components; " +
                $"slide axis {slideDirectionHome}; lift contact correction " +
                $"{liftContactOffset * 1000f:0.0} mm; " +
                "L = Bin 1/home, O = Bin 2/-90 degrees; E/R = slide extend/retract.");
        }

        private void Update()
        {
            if (Keyboard.current != null && !Kit3AutomaticSequence.IsRunning)
            {
                if (Keyboard.current.lKey.wasPressedThisFrame)
                {
                    CommandBin1();
                }
                if (Keyboard.current.oKey.wasPressedThisFrame)
                {
                    CommandBin2();
                }
                if (Keyboard.current.eKey.wasPressedThisFrame)
                {
                    CommandSlideExtend();
                }
                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    CommandSlideRetract();
                }
                if (Keyboard.current.qKey.wasPressedThisFrame)
                {
                    CommandLiftUp();
                }
                if (Keyboard.current.aKey.wasPressedThisFrame)
                {
                    CommandLiftDown();
                }
            }

            currentAngle = Mathf.MoveTowardsAngle(
                currentAngle,
                targetAngle,
                RotationSpeedDegreesPerSecond * Time.deltaTime);
            slidePosition = Mathf.MoveTowards(
                slidePosition,
                slideTarget,
                SlideSpeedMetresPerSecond * Time.deltaTime);
            liftPosition = Mathf.MoveTowards(
                liftPosition,
                liftTarget,
                LiftSpeedMetresPerSecond * Time.deltaTime);
            ApplyRotation();
        }

        public void CommandBin1()
        {
            targetAngle = 0f;
        }

        public void CommandBin2()
        {
            targetAngle = Bin2AngleDegrees;
        }

        public void CommandSlideExtend()
        {
            slideTarget = SlideStrokeMetres;
        }

        public void CommandSlideRetract()
        {
            slideTarget = 0f;
        }

        public void CommandLiftUp()
        {
            liftTarget = LiftStrokeMetres;
        }

        public void CommandLiftDown()
        {
            liftTarget = InitialApproachHeightMetres;
        }

        private void ApplyRotation()
        {
            Quaternion rotation = Quaternion.AngleAxis(currentAngle, Vector3.up);
            foreach (MovingPart part in rotatingParts)
            {
                Vector3 slideOffset = part.Slides
                    ? slideDirectionHome * slidePosition
                    : Vector3.zero;
                Vector3 liftOffset = part.Lifts
                    ? Vector3.up * (liftContactOffset + liftPosition)
                    : Vector3.zero;
                part.Transform.position = pivot +
                    rotation * (part.HomePosition + slideOffset + liftOffset - pivot);
                part.Transform.rotation = rotation * part.HomeRotation;
            }
        }

        private void OnGUI()
        {
            if (Kit3AutomaticSequence.IsRunning ||
                Kit1DigitalTwin.Hmi.Kit3EngineeringDashboard.Visible)
            {
                return;
            }
            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.25f, 0.85f, 1f) }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(Screen.width - 440f, 18f, 422f, 140f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, 370f, 25f),
                "KIT 3 · ROTARY CALIBRATION", headingStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 39f, 395f, 92f),
                $"Angle: {currentAngle:0.0}°   L: Bin 1/home   O: Bin 2 (-90°)\n" +
                $"Vacuum slide: {slidePosition * 1000f:0} mm   E: extend   R: retract\n" +
                $"Vacuum height: {liftPosition * 1000f:0} mm above contact   " +
                "Q: raise   A: lower",
                detailStyle);
        }
    }
}
