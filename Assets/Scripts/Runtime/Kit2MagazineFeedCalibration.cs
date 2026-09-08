using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    [DefaultExecutionOrder(100)]
    public sealed class Kit2MagazineFeedCalibration : MonoBehaviour
    {
        private static readonly int[] WorkpieceOrderBottomToTop =
        {
            36, 51, 41, 45, 40, 44, 39, 50,
            38, 49, 37, 48, 43, 47, 42, 46
        };

        private static readonly HashSet<string> NonStaticColliderComponents =
            new HashSet<string>
            {
                "Component_018",
                "Component_029", "Component_030", "Component_031",
                "Component_032", "Component_033", "Component_034", "Component_035",
                "Component_052", "Component_053", "Component_054",
                "Component_055", "Component_056", "Component_057"
            };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private readonly List<Workpiece> workpieces = new List<Workpiece>();
        private readonly Queue<Workpiece> magazineQueue = new Queue<Workpiece>();

        private Kit2FeedCylinderCalibration feedCylinder;
        private Kit2SecondCylinderCalibration transferCylinder;
        private Material metalMaterial;
        private Material plasticMaterial;
        private PhysicsMaterial lowFrictionMaterial;
        private Workpiece activeWorkpiece;
        private Workpiece stampBWorkpiece;
        private Workpiece displacedByNextFeed;
        private Workpiece displacedFromStampB;
        private Vector3 feedStartPosition;
        private Vector3 stampAPosition;
        private Vector3 stampBPosition;
        private float peakCrossTransferPosition;
        private bool feedContactCaptured;
        private bool stagedAtA;
        private bool stagedAtB;
        private bool discharging;
        private bool waitingForNextWorkpiece;
        private int completedCount;
        private Vector3 stackUpDirection;
        private float magazinePitch;
        private bool stackAdvancing;
        private bool readyForNextFeedPush;
        private bool transferReservedForStampB;
        private GUIStyle headingStyle;
        private GUIStyle detailStyle;

        public bool HasActiveWorkpiece => activeWorkpiece != null;
        public bool ActiveAtStampA => activeWorkpiece != null && stagedAtA && !stagedAtB;
        public bool ActiveAtStampB => activeWorkpiece != null && stagedAtB;
        public bool StampBOccupied => stampBWorkpiece != null || ActiveAtStampB;
        public bool ActiveIsMetal => activeWorkpiece != null && activeWorkpiece.IsMetal;
        public bool MetalSensorActive => ActiveAtStampA && ActiveIsMetal;
        public bool CapacitiveSensorActive => ActiveAtStampA && !ActiveIsMetal;
        public bool IsDischarging => discharging;
        public bool MagazineAdvancing => stackAdvancing;
        public bool MagazineEmpty => magazineQueue.Count == 0 &&
            activeWorkpiece == null && stampBWorkpiece == null;
        public int CompletedCount => completedCount;
        public int RemainingCount => magazineQueue.Count +
            (activeWorkpiece != null ? 1 : 0) +
            (stampBWorkpiece != null ? 1 : 0);

        private sealed class Workpiece
        {
            public string Name;
            public Transform Transform;
            public Vector3 OriginalPosition;
            public Quaternion OriginalRotation;
            public MeshRenderer Renderer;
            public BoxCollider Collider;
            public Rigidbody Body;
            public bool IsMetal;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2MagazineFeedCalibration>() == null)
            {
                new GameObject("Kit 2 Physical Magazine Flow")
                    .AddComponent<Kit2MagazineFeedCalibration>();
            }
        }

        private void Start()
        {
            feedCylinder = FindFirstObjectByType<Kit2FeedCylinderCalibration>();
            transferCylinder = FindFirstObjectByType<Kit2SecondCylinderCalibration>();
            KitComponentRegistry registry = FindFirstObjectByType<KitComponentRegistry>();
            if (feedCylinder == null || transferCylinder == null || registry == null)
            {
                Debug.LogError("Kit 2 physical flow requires both cylinders and the component registry.");
                enabled = false;
                return;
            }

            CreatePhysicsMaterial();
            BuildStaticCadColliders(registry.transform);
            LoadWorkpieces();
            CreateWorkpieceMaterials();
            ConfigureWorkpiecePhysics();
            ConfigureMagazineGeometry();
            ResetBatch();
            Debug.Log(
                "KIT2_PHYSICS_READY: CAD platform, magazine, chute and bin colliders enabled; " +
                "16 constrained magazine workpieces loaded; discharge gravity enabled.");
        }

        private void BuildStaticCadColliders(Transform stationRoot)
        {
            foreach (MeshFilter meshFilter in stationRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                string componentName = meshFilter.gameObject.name;
                if (NonStaticColliderComponents.Contains(componentName) ||
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
                if (componentName == "Component_027" || componentName == "Component_028")
                {
                    collider.sharedMaterial = lowFrictionMaterial;
                }
            }
        }

        private void LoadWorkpieces()
        {
            foreach (int number in WorkpieceOrderBottomToTop)
            {
                string componentName = $"Component_{number:000}";
                Transform component = GameObject.Find(componentName)?.transform;
                if (component == null)
                {
                    throw new MissingComponentException(
                        $"Kit 2 magazine is missing {componentName}.");
                }

                workpieces.Add(new Workpiece
                {
                    Name = componentName,
                    Transform = component,
                    OriginalPosition = component.position,
                    OriginalRotation = component.rotation,
                    Renderer = component.GetComponent<MeshRenderer>()
                });
            }
        }

        private void ConfigureWorkpiecePhysics()
        {
            foreach (Workpiece workpiece in workpieces)
            {
                Transform cadMesh = workpiece.Transform;
                Vector3 visualCenter = workpiece.Renderer.bounds.center;
                GameObject rootObject = new GameObject($"{workpiece.Name} Physics Root");
                Transform physicsRoot = rootObject.transform;
                physicsRoot.SetPositionAndRotation(visualCenter, Quaternion.identity);
                physicsRoot.localScale = Vector3.one;
                cadMesh.SetParent(physicsRoot, true);

                workpiece.Transform = physicsRoot;
                workpiece.OriginalPosition = physicsRoot.position;
                workpiece.OriginalRotation = physicsRoot.rotation;

                BoxCollider collider = rootObject.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                // Small guide clearance avoids false contacts with the narrow CAD
                // magazine while retaining full-size rendered workpieces.
                collider.size = Vector3.Scale(
                    workpiece.Renderer.bounds.size,
                    new Vector3(0.92f, 0.96f, 0.92f));
                collider.sharedMaterial = lowFrictionMaterial;
                workpiece.Collider = collider;

                Rigidbody body = rootObject.AddComponent<Rigidbody>();

                body.mass = 0.04f;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.12f;
                body.angularDamping = 0.28f;
                body.maxAngularVelocity = 8f;
                body.isKinematic = true;
                workpiece.Body = body;
            }
        }

        private void ConfigureMagazineGeometry()
        {
            Vector3 minimum = workpieces[0].Collider.bounds.center;
            Vector3 maximum = minimum;
            foreach (Workpiece workpiece in workpieces)
            {
                Vector3 center = workpiece.Collider.bounds.center;
                minimum = Vector3.Min(minimum, center);
                maximum = Vector3.Max(maximum, center);
            }

            Vector3 range = maximum - minimum;
            if (range.y >= range.x && range.y >= range.z)
            {
                stackUpDirection = Vector3.up;
            }
            else if (range.x >= range.z)
            {
                stackUpDirection = Vector3.right;
            }
            else
            {
                stackUpDirection = Vector3.forward;
            }

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
                if (spacing > 0.0001f)
                {
                    magazinePitch = Mathf.Min(magazinePitch, spacing);
                }
            }
            if (magazinePitch == float.MaxValue)
            {
                magazinePitch = 0.01f;
            }

            float gravityAlignment = Mathf.Abs(
                Vector3.Dot(stackUpDirection, gravityDown));
            Debug.Log(
                $"KIT2_MAGAZINE_GEOMETRY: axis={stackUpDirection}, " +
                $"span={Vector3.Dot(range, new Vector3(Mathf.Abs(stackUpDirection.x), Mathf.Abs(stackUpDirection.y), Mathf.Abs(stackUpDirection.z))) * 1000f:0.0} mm, " +
                $"pitch={magazinePitch * 1000f:0.0} mm, " +
                $"bottom={workpieces[0].Name}, top={workpieces[workpieces.Count - 1].Name}, " +
                $"gravity alignment={gravityAlignment:0.000}.");
        }

        private void Update()
        {
            if (!Kit2AutomaticSequence.IsRunning && Keyboard.current != null &&
                Keyboard.current.mKey.wasPressedThisFrame)
            {
                ResetBatch();
                return;
            }

            if (discharging || stackAdvancing)
            {
                return;
            }

            TryBeginStampBFifoTransfer();
            UpdateStampBDischarge();

            if (activeWorkpiece == null)
            {
                if (waitingForNextWorkpiece &&
                    feedCylinder.TravelMetres <= 0.0001f &&
                    transferCylinder.TravelMetres <= 0.0001f)
                {
                    ArmNextWorkpiece();
                }
                return;
            }

            if (stagedAtA && !stagedAtB && feedCylinder.TravelMetres <= 0.0005f)
            {
                readyForNextFeedPush = true;
            }

            if (readyForNextFeedPush && !feedCylinder.EjectCommanded &&
                feedCylinder.TravelMetres > 0.0005f &&
                displacedByNextFeed == null)
            {
                BeginNextFeedPush();
            }

            if (!stagedAtA)
            {
                UpdateMagazineFeed();
                return;
            }

            if (!stagedAtB && feedCylinder.TravelMetres > 0.05f)
            {
                activeWorkpiece.Transform.position = stampAPosition +
                    (feedCylinder.CurrentTravelOffset - feedCylinder.StageTravelOffset);
                if (feedCylinder.TravelMetres >= 0.089f)
                {
                    ReleaseToPhysics(feedCylinder.StageTravelOffset.normalized, "BIN 1");
                }
                return;
            }

            if (!stagedAtB)
            {
                if (transferReservedForStampB)
                {
                    activeWorkpiece.Transform.position = stampAPosition;
                    return;
                }
                UpdateCrossTransfer();
                return;
            }

            activeWorkpiece.Transform.position = stampBPosition;
            if (transferCylinder.TravelMetres <= 0.0005f)
            {
                ParkTransferredWorkpieceAtStampB();
            }
        }

        private void UpdateMagazineFeed()
        {
            if (feedCylinder.TravelMetres <= 0.0005f)
            {
                feedStartPosition = activeWorkpiece.Transform.position;
                return;
            }

            CapturePusherContact();
            Vector3 feedTarget = feedStartPosition + feedCylinder.CurrentTravelOffset;
            activeWorkpiece.Body.MovePosition(feedTarget);

            if (feedCylinder.NormalizedPosition >= 0.999f)
            {
                stagedAtA = true;
                activeWorkpiece.Body.position = feedTarget;
                stampAPosition = feedTarget;
                peakCrossTransferPosition = 0f;
                StartCoroutine(AdvanceMagazineStack());
                if (displacedByNextFeed != null)
                {
                    ReleaseDisplacedToRamp(displacedByNextFeed);
                    displacedByNextFeed = null;
                }
                Debug.Log(
                    $"KIT2_WORKPIECE_AT_STAMP_A: {activeWorkpiece.Name} " +
                    $"({(activeWorkpiece.IsMetal ? "metal" : "plastic")}).");
            }
        }

        private void UpdateCrossTransfer()
        {
            float currentPosition = transferCylinder.NormalizedPosition;
            if (currentPosition <= 0.0005f)
            {
                activeWorkpiece.Transform.position = stampAPosition;
                return;
            }

            CapturePusherContact();
            if (currentPosition + 0.0001f < peakCrossTransferPosition)
            {
                stampBPosition = stampAPosition +
                    transferCylinder.StageTravelOffset * peakCrossTransferPosition;
                stagedAtB = true;
                activeWorkpiece.Transform.position = stampBPosition;
                return;
            }

            peakCrossTransferPosition = Mathf.Max(
                peakCrossTransferPosition,
                currentPosition);
            Vector3 transferTarget =
                stampAPosition + transferCylinder.CurrentTravelOffset;
            activeWorkpiece.Body.MovePosition(transferTarget);
            if (currentPosition >= 0.995f)
            {
                stampBPosition = stampAPosition + transferCylinder.StageTravelOffset;
                stagedAtB = true;
                activeWorkpiece.Body.position = stampBPosition;
                activeWorkpiece.Transform.position = stampBPosition;
                if (displacedFromStampB != null)
                {
                    ReleaseDisplacedFromStampB(displacedFromStampB);
                    displacedFromStampB = null;
                }
                Debug.Log($"KIT2_WORKPIECE_AT_STAMP_B: {activeWorkpiece.Name}.");
            }
        }

        private void TryBeginStampBFifoTransfer()
        {
            if (stampBWorkpiece == null || activeWorkpiece == null ||
                !stagedAtA || stagedAtB || displacedFromStampB != null ||
                transferCylinder.TravelMetres <= 0.0005f)
            {
                return;
            }

            displacedFromStampB = stampBWorkpiece;
            stampBWorkpiece = null;
            displacedFromStampB.Body.constraints = RigidbodyConstraints.None;
            displacedFromStampB.Body.isKinematic = false;
            displacedFromStampB.Body.useGravity = true;
            displacedFromStampB.Body.linearVelocity = Vector3.zero;
            displacedFromStampB.Body.angularVelocity = Vector3.zero;
            displacedFromStampB.Body.WakeUp();
            transferReservedForStampB = false;
            Debug.Log(
                $"KIT2_STAMP_B_FIFO: {activeWorkpiece.Name} will push " +
                $"{displacedFromStampB.Name} from Stamp B onto the chute.");
        }

        private void ReleaseDisplacedFromStampB(Workpiece workpiece)
        {
            Vector3 direction = transferCylinder.StageTravelOffset.normalized;
            float forwardSpeed = Vector3.Dot(workpiece.Body.linearVelocity, direction);
            if (forwardSpeed < 0.035f)
            {
                workpiece.Body.linearVelocity +=
                    direction * (0.035f - forwardSpeed);
            }
            workpiece.Body.useGravity = true;
            workpiece.Body.WakeUp();
            StartCoroutine(CountRampDischarge(workpiece, "Stamp B"));
        }

        // Completes the A-to-B handoff and releases Stamp A for the next feed.
        private void ParkTransferredWorkpieceAtStampB()
        {
            stampBWorkpiece = activeWorkpiece;
            stampBWorkpiece.Transform.position = stampBPosition;
            stampBWorkpiece.Body.position = stampBPosition;
            stampBWorkpiece.Body.isKinematic = true;
            stampBWorkpiece.Body.useGravity = false;
            stampBWorkpiece.Body.linearVelocity = Vector3.zero;
            stampBWorkpiece.Body.angularVelocity = Vector3.zero;

            activeWorkpiece = null;
            feedContactCaptured = false;
            stagedAtA = false;
            stagedAtB = false;
            peakCrossTransferPosition = 0f;
            readyForNextFeedPush = false;
            waitingForNextWorkpiece = true;
            Debug.Log(
                $"KIT2_STAMP_B_OCCUPIED: {stampBWorkpiece.Name}; Stamp A feed slot released.");
        }

        private void UpdateStampBDischarge()
        {
            if (stampBWorkpiece == null)
            {
                if (transferReservedForStampB &&
                    transferCylinder.TravelMetres <= 0.0005f)
                {
                    transferReservedForStampB = false;
                    Debug.Log("KIT2_CYLINDER_2_HOME: transfer path available for Stamp A.");
                }
                return;
            }

            if (transferCylinder.TravelMetres > 0.0005f)
            {
                transferReservedForStampB = true;
            }

            float extraTravel = Mathf.Max(
                0f,
                transferCylinder.TravelMetres - 0.056f);
            Vector3 dischargeDirection =
                transferCylinder.StageTravelOffset.normalized;
            stampBWorkpiece.Transform.position =
                stampBPosition + dischargeDirection * extraTravel;

            if (transferCylinder.TravelMetres < 0.089f)
            {
                return;
            }

            Workpiece discharged = stampBWorkpiece;
            stampBWorkpiece = null;
            discharging = true;
            discharged.Body.constraints = RigidbodyConstraints.None;
            discharged.Body.isKinematic = false;
            discharged.Body.useGravity = true;
            discharged.Body.linearVelocity =
                dischargeDirection * 0.11f + Vector3.down * 0.015f;
            discharged.Body.angularVelocity = new Vector3(0.8f, 0.45f, -0.65f);
            discharged.Body.WakeUp();
            StartCoroutine(WaitForStampBDischarge(discharged));
        }

        private IEnumerator WaitForStampBDischarge(Workpiece discharged)
        {
            float elapsed = 0f;
            while (elapsed < 2.5f)
            {
                elapsed += Time.deltaTime;
                if (elapsed > 0.65f && discharged.Body.IsSleeping())
                {
                    break;
                }
                yield return null;
            }

            completedCount++;
            discharging = false;
            Debug.Log(
                $"KIT2_WORKPIECE_DISCHARGED: {discharged.Name} settled toward BIN 2; Stamp B clear.");
        }

        private void CapturePusherContact()
        {
            if (feedContactCaptured)
            {
                return;
            }

            feedContactCaptured = true;
            activeWorkpiece.Body.linearVelocity = Vector3.zero;
            activeWorkpiece.Body.angularVelocity = Vector3.zero;
            activeWorkpiece.Body.constraints = RigidbodyConstraints.None;
            activeWorkpiece.Body.isKinematic = true;
        }

        private void ReleaseToPhysics(Vector3 dischargeDirection, string binLabel)
        {
            if (discharging)
            {
                return;
            }

            discharging = true;
            activeWorkpiece.Body.constraints = RigidbodyConstraints.None;
            activeWorkpiece.Body.isKinematic = false;
            activeWorkpiece.Body.useGravity = true;
            activeWorkpiece.Body.linearVelocity =
                dischargeDirection * 0.11f + Vector3.down * 0.015f;
            activeWorkpiece.Body.angularVelocity = new Vector3(0.8f, 0.45f, -0.65f);
            StartCoroutine(WaitForPhysicalDischarge(activeWorkpiece, binLabel));
        }

        private void ReleaseDisplacedToRamp(Workpiece workpiece)
        {
            workpiece.Body.constraints = RigidbodyConstraints.None;
            workpiece.Body.isKinematic = false;
            workpiece.Body.useGravity = true;
            Vector3 feedDirection = feedCylinder.StageTravelOffset.normalized;
            float forwardSpeed = Vector3.Dot(workpiece.Body.linearVelocity, feedDirection);
            if (forwardSpeed < 0.035f)
            {
                // Preserve the collision response and add only the missing actuator
                // follow-through needed to clear the CAD ramp lip.
                workpiece.Body.linearVelocity +=
                    feedDirection * (0.035f - forwardSpeed);
            }
            workpiece.Body.WakeUp();
            StartCoroutine(CountRampDischarge(workpiece, "Stamp A"));
        }

        private IEnumerator CountRampDischarge(
            Workpiece discharged,
            string stationLabel)
        {
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                elapsed += Time.deltaTime;
                if (elapsed > 0.65f && discharged.Body.IsSleeping())
                {
                    break;
                }
                yield return null;
            }
            completedCount++;
            Debug.Log(
                $"KIT2_FIFO_DISCHARGE: {discharged.Name} cleared {stationLabel} " +
                "via the next workpiece.");
        }

        private void BeginNextFeedPush()
        {
            if (magazineQueue.Count == 0)
            {
                return;
            }

            displacedByNextFeed = activeWorkpiece;
            displacedByNextFeed.Body.constraints = RigidbodyConstraints.None;
            displacedByNextFeed.Body.isKinematic = false;
            displacedByNextFeed.Body.useGravity = true;
            displacedByNextFeed.Body.linearVelocity = Vector3.zero;
            displacedByNextFeed.Body.angularVelocity = Vector3.zero;
            displacedByNextFeed.Body.WakeUp();
            activeWorkpiece = magazineQueue.Dequeue();
            activeWorkpiece.Body.constraints = RigidbodyConstraints.None;
            activeWorkpiece.Body.isKinematic = true;
            activeWorkpiece.Body.useGravity = false;
            activeWorkpiece.Body.linearVelocity = Vector3.zero;
            activeWorkpiece.Body.angularVelocity = Vector3.zero;
            feedStartPosition = activeWorkpiece.Transform.position;
            feedContactCaptured = true;
            stagedAtA = false;
            stagedAtB = false;
            readyForNextFeedPush = false;
            peakCrossTransferPosition = 0f;
            Debug.Log(
                $"KIT2_FIFO_FEED: {activeWorkpiece.Name} will push {displacedByNextFeed.Name} onto Ramp 1.");
        }

        private IEnumerator WaitForPhysicalDischarge(Workpiece discharged, string binLabel)
        {
            float elapsed = 0f;
            while (elapsed < 2.5f)
            {
                elapsed += Time.deltaTime;
                if (elapsed > 0.65f && discharged.Body.IsSleeping())
                {
                    break;
                }
                yield return null;
            }

            completedCount++;
            Debug.Log(
                $"KIT2_WORKPIECE_DISCHARGED: {discharged.Name} settled after release toward {binLabel}.");
            activeWorkpiece = null;
            discharging = false;
            waitingForNextWorkpiece = true;
        }

        private void ArmNextWorkpiece()
        {
            waitingForNextWorkpiece = false;
            feedContactCaptured = false;
            stagedAtA = false;
            stagedAtB = false;
            peakCrossTransferPosition = 0f;
            readyForNextFeedPush = false;
            if (magazineQueue.Count == 0)
            {
                activeWorkpiece = null;
                Debug.Log("KIT2_MAGAZINE_EMPTY: all workpieces completed.");
                return;
            }

            activeWorkpiece = magazineQueue.Dequeue();
            activeWorkpiece.Body.isKinematic = true;
            activeWorkpiece.Body.useGravity = false;
            activeWorkpiece.Body.constraints = RigidbodyConstraints.None;
            feedStartPosition = activeWorkpiece.Transform.position;
            Debug.Log($"KIT2_NEXT_WORKPIECE_READY: {activeWorkpiece.Name} constrained at the feed plane.");
        }

        private IEnumerator AdvanceMagazineStack()
        {
            stackAdvancing = true;
            List<Workpiece> remaining = new List<Workpiece>(magazineQueue);
            if (remaining.Count == 0)
            {
                stackAdvancing = false;
                yield break;
            }

            Vector3[] targets = new Vector3[remaining.Count];
            for (int index = 0; index < remaining.Count; index++)
            {
                Workpiece workpiece = remaining[index];
                workpiece.Body.isKinematic = true;
                workpiece.Body.useGravity = false;
                workpiece.Body.constraints = RigidbodyConstraints.None;
                targets[index] = workpiece.Transform.position -
                    stackUpDirection * magazinePitch;
            }

            float dropSpeed = 0f;
            bool settled = false;
            while (!settled)
            {
                dropSpeed += Physics.gravity.magnitude * Time.deltaTime;
                settled = true;
                for (int index = 0; index < remaining.Count; index++)
                {
                    Transform transform = remaining[index].Transform;
                    transform.position = Vector3.MoveTowards(
                        transform.position,
                        targets[index],
                        dropSpeed * Time.deltaTime);
                    if (Vector3.Distance(transform.position, targets[index]) > 0.00005f)
                    {
                        settled = false;
                    }
                }
                yield return null;
            }

            Physics.SyncTransforms();
            stackAdvancing = false;
            Debug.Log("KIT2_MAGAZINE_SETTLED: remaining workpieces advanced one CAD pitch.");
        }

        private void ResetBatch()
        {
            StopAllCoroutines();
            feedCylinder.CommandRetract();
            transferCylinder.CommandRetract();
            magazineQueue.Clear();
            activeWorkpiece = null;
            stampBWorkpiece = null;
            displacedByNextFeed = null;
            displacedFromStampB = null;
            discharging = false;
            waitingForNextWorkpiece = false;
            completedCount = 0;
            stackAdvancing = false;
            readyForNextFeedPush = false;
            transferReservedForStampB = false;

            foreach (Workpiece workpiece in workpieces)
            {
                workpiece.Body.isKinematic = true;
                workpiece.Body.useGravity = false;
                workpiece.Body.constraints = RigidbodyConstraints.None;
                workpiece.Body.linearVelocity = Vector3.zero;
                workpiece.Body.angularVelocity = Vector3.zero;
                workpiece.Transform.SetPositionAndRotation(
                    workpiece.OriginalPosition,
                    workpiece.OriginalRotation);
                workpiece.IsMetal = Random.value >= 0.5f;
                workpiece.Renderer.sharedMaterial =
                    workpiece.IsMetal ? metalMaterial : plasticMaterial;
                magazineQueue.Enqueue(workpiece);
            }

            Physics.SyncTransforms();
            ValidateMagazineSeparation();
            ArmNextWorkpiece();
            Debug.Log("KIT2_MAGAZINE_RESET: centered, constrained stack restored and randomized.");
        }

        public void RequestReset()
        {
            ResetBatch();
        }

        private void ValidateMagazineSeparation()
        {
            float minimumClearance = float.MaxValue;
            for (int index = 1; index < workpieces.Count; index++)
            {
                Bounds lower = workpieces[index - 1].Collider.bounds;
                Bounds upper = workpieces[index].Collider.bounds;
                float centerSpacing = Vector3.Dot(
                    upper.center - lower.center,
                    stackUpDirection);
                float combinedHalfSize = ProjectBoundsExtent(lower) +
                    ProjectBoundsExtent(upper);
                minimumClearance = Mathf.Min(
                    minimumClearance,
                    centerSpacing - combinedHalfSize);
            }

            if (minimumClearance < -0.00005f)
            {
                Debug.LogError(
                    $"KIT2_MAGAZINE_OVERLAP: collision envelopes overlap by " +
                    $"{-minimumClearance * 1000f:0.00} mm.");
            }
            else
            {
                Debug.Log(
                    $"KIT2_MAGAZINE_VALIDATED: minimum collider clearance " +
                    $"{minimumClearance * 1000f:0.00} mm.");
            }
        }

        private float ProjectBoundsExtent(Bounds bounds)
        {
            Vector3 axis = new Vector3(
                Mathf.Abs(stackUpDirection.x),
                Mathf.Abs(stackUpDirection.y),
                Mathf.Abs(stackUpDirection.z));
            return Vector3.Dot(bounds.extents, axis);
        }

        private void CreateWorkpieceMaterials()
        {
            Material source = workpieces[0].Renderer.sharedMaterial;
            metalMaterial = new Material(source) { name = "Kit2 Metal Workpiece (Runtime)" };
            plasticMaterial = new Material(source) { name = "Kit2 Plastic Workpiece (Runtime)" };
            metalMaterial.SetColor(BaseColorId, new Color(0.08f, 0.63f, 1f));
            plasticMaterial.SetColor(BaseColorId, new Color(1f, 0.38f, 0.04f));
        }

        private void CreatePhysicsMaterial()
        {
            lowFrictionMaterial = new PhysicsMaterial("Kit 2 Low Friction Ramp")
            {
                dynamicFriction = 0.02f,
                staticFriction = 0f,
                bounciness = 0.01f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        private static bool IsWorkpieceName(string componentName)
        {
            if (!componentName.StartsWith("Component_") ||
                !int.TryParse(componentName.Substring(10), out int number))
            {
                return false;
            }
            return number >= 36 && number <= 51;
        }

        private void OnGUI()
        {
            headingStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.45f, 0.82f, 1f) }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = Color.white }
            };

            Rect panel = new Rect(18f, Screen.height - 154f, 490f, 134f);
            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 8f, 450f, 28f),
                "KIT 2 · PHYSICAL MAGAZINE / DISCHARGE",
                headingStyle);
            string material = activeWorkpiece == null
                ? "--"
                : activeWorkpiece.IsMetal ? "METAL (blue)" : "PLASTIC (orange)";
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 39f, 458f, 88f),
                $"Active: {material}   State: {GetState()}   Done: {completedCount}\n" +
                "E / Shift+E: Stamp A / Bin 1\n" +
                "T / Shift+T: Stamp B / Bin 2   |   M: physical reset",
                detailStyle);
        }

        private string GetState()
        {
            if (discharging) return "FALLING";
            if (activeWorkpiece == null)
                return waitingForNextWorkpiece ? "RETRACT CYLINDERS" : "EMPTY";
            if (stagedAtB) return "STAMP B";
            if (stagedAtA) return "STAMP A";
            if (stackAdvancing) return "MAGAZINE ADVANCING";
            return feedCylinder.TravelMetres > 0.001f ? "FEEDING" : "READY";
        }
    }
}
