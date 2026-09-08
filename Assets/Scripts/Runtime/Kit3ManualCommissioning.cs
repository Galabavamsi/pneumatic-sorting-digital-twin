using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    [DefaultExecutionOrder(100)]
    public sealed class Kit3ManualCommissioning : MonoBehaviour
    {
        private const float FeedStrokeMetres = 0.05f;
        private const float FeedSpeedMetresPerSecond = 0.10f;
        private const float StampStrokeMetres = 0.045f;
        private const float StampSpeedMetresPerSecond = 0.08f;
        private const float VacuumHorizontalToleranceMetres = 0.018f;
        private const float VacuumVerticalToleranceMetres = 0.012f;

        private static readonly HashSet<string> DynamicCadComponents =
            new HashSet<string>
            {
                "Component_014", "Component_020", "Component_022",
                "Component_023", "Component_024", "Component_031",
                "Component_032", "Component_033", "Component_034",
                "Component_035", "Component_036", "Component_040",
                "Component_041", "Component_044", "Component_046",
                "Component_048", "Component_069"
            };

        private readonly List<Workpiece> workpieces = new List<Workpiece>();
        private readonly List<Workpiece> waitingMagazine = new List<Workpiece>();
        private Transform feedRod;
        private Transform feedHead;
        private Transform stampRod;
        private Transform stampHead;
        private Transform vacuumPickup;
        private Renderer vacuumRenderer;
        private Rigidbody pusherBody;
        private Vector3 pusherPhysicsHome;
        private Vector3 feedRodHome;
        private Vector3 feedHeadHome;
        private Vector3 stampRodHome;
        private Vector3 stampHeadHome;
        private Vector3 feedDirection;
        private float feedPosition;
        private float feedTarget;
        private float stampPosition;
        private float stampTarget;
        private float magazinePitch;
        private float magazineDropElapsed;
        private Vector3 stackUpDirection;
        private bool magazineAdvancePending;
        private bool magazineDropReleased;
        private bool nextWorkpieceWaiting;
        private bool vacuumEnabled;
        private bool stampLogged;
        private bool feedCycleCommitted;
        private bool feedStrokeActive;
        private int feedEndpointFixedSteps;
        private Workpiece activeMagazineWorkpiece;
        private Workpiece stationWorkpiece;
        private Workpiece heldWorkpiece;
        private Vector3 heldLocalPosition;
        private Quaternion heldLocalRotation;
        private PhysicsMaterial lowFrictionMaterial;
        private GUIStyle headingStyle;
        private GUIStyle detailStyle;

        public bool FeedIsHome => feedPosition <= 0.0005f;
        public bool FeedIsExtended => feedPosition >= FeedStrokeMetres - 0.0005f;
        public bool StampIsUp => stampPosition <= 0.01f;
        public bool StampIsDown => stampPosition >= 0.99f;
        public bool VacuumEnabled => vacuumEnabled;
        public bool VacuumHasWorkpiece => heldWorkpiece != null;
        public bool PartAtStamp => stationWorkpiece != null;
        public bool PartAtStampIsBlue => stationWorkpiece != null && stationWorkpiece.IsBlue;
        public bool HeldWorkpieceIsBlue => heldWorkpiece != null && heldWorkpiece.IsBlue;
        public bool MagazineSettled => !magazineAdvancePending &&
            !magazineDropReleased && !nextWorkpieceWaiting;
        public bool CanFeed => FeedIsHome && activeMagazineWorkpiece != null &&
            stationWorkpiece == null && MagazineSettled;
        public int RemainingCount => workpieces.Count(item => !item.Released);
        public bool MagazineEmpty => RemainingCount == 0;

        private sealed class Workpiece
        {
            public string Name;
            public Transform Transform;
            public Renderer Renderer;
            public BoxCollider Collider;
            public Rigidbody Body;
            public Vector3 OriginalPosition;
            public Quaternion OriginalRotation;
            public Vector3 FeedStartPosition;
            public Vector3 MagazineDropTarget;
            public bool IsBlue;
            public bool Released;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit3_AssemblyStation") != null &&
                FindAnyObjectByType<Kit3ManualCommissioning>() == null)
            {
                new GameObject("Kit 3 Manual Commissioning")
                    .AddComponent<Kit3ManualCommissioning>();
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

            if (!TryResolveMechanisms(components))
            {
                enabled = false;
                return;
            }

            lowFrictionMaterial = new PhysicsMaterial("Kit 3 low-friction handling")
            {
                dynamicFriction = 0.08f,
                staticFriction = 0.10f,
                bounciness = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            CreatePusherPhysicsProxy();
            BuildStaticCadColliders(station.transform);
            ConfigureWorkpieces(components);
            ResetWorkpieces();
            Debug.Log(
                "KIT3_MANUAL_READY: F/G feeder, U/J stamp, V vacuum, M reset; " +
                "workpieces have independent collision bodies and gravity after release.");
        }

        private void CreatePusherPhysicsProxy()
        {
            Renderer renderer = feedHead.GetComponentInChildren<Renderer>();
            GameObject proxy = new GameObject("Kit 3 Feed Pusher Physics");
            proxy.transform.SetPositionAndRotation(renderer.bounds.center, Quaternion.identity);
            BoxCollider collider = proxy.AddComponent<BoxCollider>();
            collider.size = Vector3.Scale(
                renderer.bounds.size,
                new Vector3(0.98f, 0.94f, 0.94f));
            collider.sharedMaterial = lowFrictionMaterial;
            pusherBody = proxy.AddComponent<Rigidbody>();
            pusherBody.isKinematic = true;
            pusherBody.useGravity = false;
            pusherBody.interpolation = RigidbodyInterpolation.Interpolate;
            pusherBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            pusherPhysicsHome = proxy.transform.position;
        }

        private bool TryResolveMechanisms(Dictionary<string, Transform> components)
        {
            bool found = components.TryGetValue("Component_032", out feedRod) &
                components.TryGetValue("Component_034", out feedHead) &
                components.TryGetValue("Component_035", out stampRod) &
                components.TryGetValue("Component_044", out stampHead) &
                components.TryGetValue("Component_033", out vacuumPickup);
            if (!found)
            {
                Debug.LogError(
                    "KIT3_MANUAL_MISSING: expected feed 032/034, stamp 035/044, and pickup 033.");
                return false;
            }

            feedRodHome = feedRod.position;
            feedHeadHome = feedHead.position;
            stampRodHome = stampRod.position;
            stampHeadHome = stampHead.position;
            vacuumRenderer = vacuumPickup.GetComponentInChildren<Renderer>();

            Vector3 railAxis = Vector3.ProjectOnPlane(feedRod.right, Vector3.up).normalized;
            Vector3 towardMagazine = Vector3.ProjectOnPlane(
                GetRendererCenter(components["Component_050"]) -
                GetRendererCenter(feedHead),
                Vector3.up);
            // Select the rail direction that runs from the cylinder head toward
            // the magazine and stamping station.
            feedDirection = Vector3.Dot(railAxis, towardMagazine) >= 0f
                ? railAxis
                : -railAxis;
            return feedDirection.sqrMagnitude > 0.99f;
        }

        private void BuildStaticCadColliders(Transform stationRoot)
        {
            foreach (MeshFilter meshFilter in
                stationRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                string componentName = meshFilter.gameObject.name;
                if (DynamicCadComponents.Contains(componentName) ||
                    IsWorkpieceName(componentName))
                {
                    continue;
                }

                MeshCollider collider = meshFilter.GetComponent<MeshCollider>();
                if (collider == null)
                {
                    collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                }
                collider.sharedMesh = meshFilter.sharedMesh;
                collider.convex = false;
                collider.sharedMaterial = lowFrictionMaterial;
            }
        }

        private void ConfigureWorkpieces(Dictionary<string, Transform> components)
        {
            for (int number = 50; number <= 65; number++)
            {
                string componentName = $"Component_{number:000}";
                if (!components.TryGetValue(componentName, out Transform cadMesh))
                {
                    Debug.LogError($"KIT3_WORKPIECE_MISSING: {componentName}");
                    continue;
                }

                Renderer renderer = cadMesh.GetComponentInChildren<Renderer>();
                Vector3 visualCenter = renderer.bounds.center;
                GameObject rootObject = new GameObject($"{componentName} Physics Root");
                Transform physicsRoot = rootObject.transform;
                physicsRoot.SetPositionAndRotation(visualCenter, Quaternion.identity);
                cadMesh.SetParent(physicsRoot, true);

                BoxCollider collider = rootObject.AddComponent<BoxCollider>();
                collider.size = Vector3.Scale(
                    renderer.bounds.size,
                    new Vector3(0.92f, 0.96f, 0.92f));
                collider.sharedMaterial = lowFrictionMaterial;

                Rigidbody body = rootObject.AddComponent<Rigidbody>();
                body.mass = 0.04f;
                body.useGravity = false;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.12f;
                body.angularDamping = 0.28f;

                Workpiece workpiece = new Workpiece
                {
                    Name = componentName,
                    Transform = physicsRoot,
                    Renderer = renderer,
                    Collider = collider,
                    Body = body,
                    OriginalPosition = physicsRoot.position,
                    OriginalRotation = physicsRoot.rotation,
                    IsBlue = (number - 50) % 2 == 0
                };
                ApplyWorkpieceColor(workpiece);
                workpieces.Add(workpiece);
            }

            Vector3 minimum = workpieces[0].Collider.bounds.center;
            Vector3 maximum = minimum;
            foreach (Workpiece workpiece in workpieces)
            {
                minimum = Vector3.Min(minimum, workpiece.Collider.bounds.center);
                maximum = Vector3.Max(maximum, workpiece.Collider.bounds.center);
            }

            Vector3 range = maximum - minimum;
            stackUpDirection = range.y >= range.x && range.y >= range.z
                ? Vector3.up
                : range.x >= range.z ? Vector3.right : Vector3.forward;
            Vector3 gravityDown = Physics.gravity.sqrMagnitude > 0.0001f
                ? Physics.gravity.normalized
                : Vector3.down;
            if (Vector3.Dot(stackUpDirection, -gravityDown) < 0f)
            {
                stackUpDirection = -stackUpDirection;
            }

            workpieces.Sort((left, right) =>
                Vector3.Dot(left.Collider.bounds.center, stackUpDirection)
                    .CompareTo(Vector3.Dot(
                        right.Collider.bounds.center,
                        stackUpDirection)));
            magazinePitch = float.MaxValue;
            for (int index = 1; index < workpieces.Count; index++)
            {
                float spacing = Vector3.Dot(
                    workpieces[index].Collider.bounds.center -
                    workpieces[index - 1].Collider.bounds.center,
                    stackUpDirection);
                if (spacing > 0.001f)
                {
                    magazinePitch = Mathf.Min(magazinePitch, spacing);
                }
            }
            if (magazinePitch == float.MaxValue)
            {
                magazinePitch = 0.01f;
            }
            Debug.Log(
                $"KIT3_MAGAZINE_GEOMETRY: axis={stackUpDirection}, " +
                $"pitch={magazinePitch * 1000f:0.0} mm, " +
                $"bottom={workpieces[0].Name}.");
        }

        private static void ApplyWorkpieceColor(Workpiece workpiece)
        {
            Material material = new Material(workpiece.Renderer.sharedMaterial)
            {
                name = workpiece.IsBlue
                    ? "Kit 3 blue workpiece"
                    : "Kit 3 orange workpiece"
            };
            Color color = workpiece.IsBlue
                ? new Color(0.04f, 0.42f, 0.95f)
                : new Color(1f, 0.25f, 0.035f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            workpiece.Renderer.material = material;
        }

        private void Update()
        {
            ReadManualInput();
            MoveCylinders();
            UpdateFeedFlow();
            UpdateVacuum();
        }

        private void ReadManualInput()
        {
            if (Keyboard.current == null || Kit3AutomaticSequence.IsRunning)
            {
                return;
            }
            if (Keyboard.current.fKey.wasPressedThisFrame)
            {
                CommandFeedExtend();
            }
            if (Keyboard.current.gKey.wasPressedThisFrame)
            {
                CommandFeedRetract();
            }
            if (Keyboard.current.uKey.wasPressedThisFrame)
            {
                CommandStampDown();
            }
            if (Keyboard.current.jKey.wasPressedThisFrame)
            {
                CommandStampUp();
            }
            if (Keyboard.current.vKey.wasPressedThisFrame)
            {
                CommandVacuum(!vacuumEnabled);
            }
            if (Keyboard.current.mKey.wasPressedThisFrame)
            {
                ResetWorkpieces();
            }
        }

        public void CommandFeedExtend()
        {
            if (stationWorkpiece != null)
            {
                Debug.LogWarning(
                    "KIT3_FEED_BLOCKED: vacuum must remove the processed workpiece first.");
                return;
            }
            BeginFeedStroke();
        }

        private void BeginFeedStroke()
        {
            if (!FeedIsHome || feedTarget > 0.0005f)
            {
                Debug.LogWarning(
                    "KIT3_FEED_BLOCKED: retract the pusher fully before the next stroke.");
                return;
            }

            if (activeMagazineWorkpiece == null || magazineAdvancePending)
            {
                Debug.LogWarning(
                    "KIT3_FEED_BLOCKED: retract and wait for the next magazine slot.");
                return;
            }

            feedCycleCommitted = false;
            feedStrokeActive = true;
            feedEndpointFixedSteps = 0;
            PrepareForPhysicalFeed(activeMagazineWorkpiece);
            feedTarget = FeedStrokeMetres;
        }

        private static void PrepareForPhysicalFeed(Workpiece workpiece)
        {
            workpiece.Body.isKinematic = false;
            workpiece.Body.useGravity = true;
            workpiece.Body.constraints =
                RigidbodyConstraints.FreezeRotation |
                RigidbodyConstraints.FreezePositionZ;
            workpiece.Body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            workpiece.Body.WakeUp();
        }

        public void CommandFeedRetract()
        {
            feedTarget = 0f;
        }

        public void CommandStampDown()
        {
            stampTarget = 1f;
            stampLogged = false;
        }

        public void CommandStampUp()
        {
            stampTarget = 0f;
        }

        public void CommandVacuum(bool enabledState)
        {
            vacuumEnabled = enabledState;
            if (!vacuumEnabled)
            {
                ReleaseVacuumWorkpiece();
            }
        }

        public void RequestReset()
        {
            if (!Kit3AutomaticSequence.IsRunning)
            {
                ResetWorkpieces();
            }
        }

        private void MoveCylinders()
        {
            stampPosition = Mathf.MoveTowards(
                stampPosition,
                stampTarget,
                StampSpeedMetresPerSecond / StampStrokeMetres * Time.deltaTime);
            Vector3 stampOffset = Vector3.down * (StampStrokeMetres * stampPosition);
            stampRod.position = stampRodHome + stampOffset;
            stampHead.position = stampHeadHome + stampOffset;

            if (!stampLogged && stampPosition >= 0.999f && stationWorkpiece != null)
            {
                stampLogged = true;
                Debug.Log($"KIT3_STAMPED: {stationWorkpiece.Name}.");
            }
        }

        private void FixedUpdate()
        {
            feedPosition = Mathf.MoveTowards(
                feedPosition,
                feedTarget,
                FeedSpeedMetresPerSecond * Time.fixedDeltaTime);
            Vector3 feedOffset = feedDirection * feedPosition;
            feedRod.position = feedRodHome + feedOffset;
            feedHead.position = feedHeadHome + feedOffset;
            pusherBody.MovePosition(pusherPhysicsHome + feedOffset);

            if (!feedStrokeActive || feedCycleCommitted ||
                feedPosition < FeedStrokeMetres - 0.0005f)
            {
                feedEndpointFixedSteps = 0;
                return;
            }

            // Leave two solver steps at the endpoint so the kinematic pusher's
            // contacts propagate through both rigidbodies before staging.
            feedEndpointFixedSteps++;
            if (feedEndpointFixedSteps >= 2)
            {
                CommitFeedStroke();
            }
        }

        private void UpdateFeedFlow()
        {
            if (magazineAdvancePending && FeedIsHome && feedTarget <= 0.0005f)
            {
                UpdateMagazineDrop();
            }

            if (nextWorkpieceWaiting && FeedIsHome && feedTarget <= 0.0005f)
            {
                ArmSettledWorkpiece();
            }
        }

        private void UpdateMagazineDrop()
        {
            if (!magazineDropReleased)
            {
                RigidbodyConstraints guideConstraints =
                    GetMagazineGuideConstraints();
                foreach (Workpiece workpiece in waitingMagazine)
                {
                    workpiece.MagazineDropTarget =
                        workpiece.Body.position - stackUpDirection * magazinePitch;
                    workpiece.Body.isKinematic = false;
                    workpiece.Body.useGravity = true;
                    workpiece.Body.constraints = guideConstraints;
                    workpiece.Body.collisionDetectionMode =
                        CollisionDetectionMode.ContinuousDynamic;
                    workpiece.Body.linearVelocity = Vector3.zero;
                    workpiece.Body.angularVelocity = Vector3.zero;
                    workpiece.Body.WakeUp();
                }
                magazineDropReleased = true;
                magazineDropElapsed = 0f;
                Debug.Log(
                    "KIT3_MAGAZINE_GRAVITY_DROP: pusher home; stack released one pitch.");
            }

            magazineDropElapsed += Time.deltaTime;
            bool reachedTargets = true;
            foreach (Workpiece workpiece in waitingMagazine)
            {
                float remainingHeight = Vector3.Dot(
                    workpiece.Body.position - workpiece.MagazineDropTarget,
                    stackUpDirection);
                if (remainingHeight > 0.0007f)
                {
                    reachedTargets = false;
                    break;
                }
            }
            if (!reachedTargets && magazineDropElapsed < 0.75f)
            {
                return;
            }

            foreach (Workpiece workpiece in waitingMagazine)
            {
                workpiece.Body.linearVelocity = Vector3.zero;
                workpiece.Body.angularVelocity = Vector3.zero;
                workpiece.Body.isKinematic = true;
                workpiece.Body.useGravity = false;
                workpiece.Body.constraints = RigidbodyConstraints.None;
                workpiece.Body.position = workpiece.MagazineDropTarget;
                workpiece.Transform.position = workpiece.MagazineDropTarget;
            }
            Physics.SyncTransforms();
            magazineAdvancePending = false;
            magazineDropReleased = false;
            nextWorkpieceWaiting = waitingMagazine.Count > 0;
            magazineDropElapsed = 0f;
            Debug.Log(
                $"KIT3_MAGAZINE_SETTLED: {waitingMagazine.Count} bodies settled one CAD pitch.");
        }

        private RigidbodyConstraints GetMagazineGuideConstraints()
        {
            RigidbodyConstraints constraints = RigidbodyConstraints.FreezeRotation;
            if (Mathf.Abs(stackUpDirection.y) > 0.9f)
            {
                return constraints |
                    RigidbodyConstraints.FreezePositionX |
                    RigidbodyConstraints.FreezePositionZ;
            }
            if (Mathf.Abs(stackUpDirection.x) > 0.9f)
            {
                return constraints |
                    RigidbodyConstraints.FreezePositionY |
                    RigidbodyConstraints.FreezePositionZ;
            }
            return constraints |
                RigidbodyConstraints.FreezePositionX |
                RigidbodyConstraints.FreezePositionY;
        }

        private void ArmSettledWorkpiece()
        {
            activeMagazineWorkpiece = waitingMagazine[0];
            waitingMagazine.RemoveAt(0);
            activeMagazineWorkpiece.FeedStartPosition =
                activeMagazineWorkpiece.Body.position;
            nextWorkpieceWaiting = false;
            Debug.Log($"KIT3_MAGAZINE_ADVANCED: {activeMagazineWorkpiece.Name} ready.");
        }

        private void CommitFeedStroke()
        {
            feedCycleCommitted = true;
            feedStrokeActive = false;
            stationWorkpiece = activeMagazineWorkpiece;
            activeMagazineWorkpiece = null;
            stationWorkpiece.Body.isKinematic = true;
            stationWorkpiece.Body.useGravity = false;
            stationWorkpiece.Body.constraints = RigidbodyConstraints.None;
            stationWorkpiece.Body.linearVelocity = Vector3.zero;
            stationWorkpiece.Body.angularVelocity = Vector3.zero;
            stationWorkpiece.FeedStartPosition = stationWorkpiece.Body.position;
            magazineAdvancePending = waitingMagazine.Count > 0;
            magazineDropReleased = false;
            magazineDropElapsed = 0f;
            Debug.Log($"KIT3_PART_PRESENT: {stationWorkpiece.Name} at stamp position.");
        }

        private void UpdateVacuum()
        {
            if (vacuumEnabled && !feedStrokeActive && FeedIsHome &&
                heldWorkpiece == null &&
                stationWorkpiece != null &&
                VacuumIsAligned(stationWorkpiece))
            {
                heldWorkpiece = stationWorkpiece;
                stationWorkpiece = null;
                heldWorkpiece.Body.isKinematic = true;
                heldWorkpiece.Body.useGravity = false;
                heldWorkpiece.Body.linearVelocity = Vector3.zero;
                heldWorkpiece.Body.angularVelocity = Vector3.zero;
                heldLocalPosition = vacuumPickup.InverseTransformPoint(
                    heldWorkpiece.Transform.position);
                heldLocalRotation = Quaternion.Inverse(vacuumPickup.rotation) *
                    heldWorkpiece.Transform.rotation;
                Debug.Log($"KIT3_VACUUM_GRIP: {heldWorkpiece.Name}.");
            }

            if (heldWorkpiece != null)
            {
                heldWorkpiece.Body.MovePosition(
                    vacuumPickup.TransformPoint(heldLocalPosition));
                heldWorkpiece.Body.MoveRotation(
                    vacuumPickup.rotation * heldLocalRotation);
            }
        }

        private bool VacuumIsAligned(Workpiece workpiece)
        {
            if (vacuumRenderer == null || workpiece.Collider == null)
            {
                return false;
            }

            Bounds cupBounds = vacuumRenderer.bounds;
            Bounds partBounds = workpiece.Collider.bounds;
            Vector2 cupHorizontal = new Vector2(cupBounds.center.x, cupBounds.center.z);
            Vector2 partHorizontal = new Vector2(partBounds.center.x, partBounds.center.z);
            float horizontalError = Vector2.Distance(cupHorizontal, partHorizontal);
            float verticalGap = cupBounds.min.y - partBounds.max.y;
            return horizontalError <= VacuumHorizontalToleranceMetres &&
                verticalGap >= -0.001f &&
                verticalGap <= VacuumVerticalToleranceMetres;
        }

        private void ReleaseVacuumWorkpiece()
        {
            if (heldWorkpiece == null)
            {
                return;
            }
            heldWorkpiece.Body.isKinematic = false;
            heldWorkpiece.Body.useGravity = true;
            heldWorkpiece.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            heldWorkpiece.Body.linearVelocity = Vector3.zero;
            heldWorkpiece.Body.angularVelocity = Vector3.zero;
            heldWorkpiece.Body.WakeUp();
            heldWorkpiece.Released = true;
            Debug.Log($"KIT3_VACUUM_RELEASE: {heldWorkpiece.Name} under rigid-body gravity.");
            heldWorkpiece = null;
        }

        private void ResetWorkpieces()
        {
            vacuumEnabled = false;
            heldWorkpiece = null;
            stationWorkpiece = null;
            activeMagazineWorkpiece = null;
            waitingMagazine.Clear();
            magazineDropElapsed = 0f;
            magazineAdvancePending = false;
            magazineDropReleased = false;
            nextWorkpieceWaiting = false;
            feedCycleCommitted = false;
            feedStrokeActive = false;
            feedEndpointFixedSteps = 0;
            feedPosition = 0f;
            feedTarget = 0f;
            stampPosition = 0f;
            stampTarget = 0f;
            if (pusherBody != null)
            {
                pusherBody.position = pusherPhysicsHome;
            }

            foreach (Workpiece workpiece in workpieces)
            {
                workpiece.Body.isKinematic = true;
                workpiece.Body.useGravity = false;
                workpiece.Body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousSpeculative;
                workpiece.Body.linearVelocity = Vector3.zero;
                workpiece.Body.angularVelocity = Vector3.zero;
                workpiece.Body.position = workpiece.OriginalPosition;
                workpiece.Body.rotation = workpiece.OriginalRotation;
                workpiece.Transform.SetPositionAndRotation(
                    workpiece.OriginalPosition,
                    workpiece.OriginalRotation);
                workpiece.Released = false;
            }

            if (workpieces.Count > 0)
            {
                activeMagazineWorkpiece = workpieces[0];
                activeMagazineWorkpiece.FeedStartPosition =
                    activeMagazineWorkpiece.OriginalPosition;
                for (int index = 1; index < workpieces.Count; index++)
                {
                    waitingMagazine.Add(workpieces[index]);
                }
            }
            Debug.Log("KIT3_WORKPIECES_RESET: magazine restored without overlapping bodies.");
        }

        private static bool IsWorkpieceName(string componentName)
        {
            if (!componentName.StartsWith("Component_") || componentName.Length != 13)
            {
                return false;
            }
            return int.TryParse(componentName.Substring(10), out int number) &&
                number >= 50 && number <= 65;
        }

        private static Vector3 GetRendererCenter(Transform component)
        {
            Renderer renderer = component.GetComponentInChildren<Renderer>();
            return renderer != null ? renderer.bounds.center : component.position;
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
                normal = { textColor = new Color(1f, 0.70f, 0.18f) }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(18f, 18f, 510f, 142f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.Label(new Rect(32f, 26f, 480f, 25f),
                "KIT 3 · MANUAL COMMISSIONING", headingStyle);
            GUI.Label(new Rect(32f, 56f, 480f, 96f),
                $"Feed {feedPosition * 1000f:0} mm: F present / G retract   " +
                $"Stamp {stampPosition * 100f:0}%: U down / J up\n" +
                $"Vacuum: {(vacuumEnabled ? (heldWorkpiece != null ? "GRIPPED" : "ON") : "OFF")}   " +
                $"V toggle   Magazine: {(activeMagazineWorkpiece != null ? activeMagazineWorkpiece.Name : "waiting")}\n" +
                "M reset workpieces · Released parts use gravity and CAD collisions",
                detailStyle);
        }
    }
}
