using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class MagazineFeedTest : MonoBehaviour
    {
        private const string RodComponentName = "Component_025";
        private const string PusherComponentName = "Component_028";
        private const string WorkpieceComponentName = "Component_042";
        private const float FeedStrokeMetres = 0.04f;
        private const float LowerEjectorApproachMetres = 0.008f;
        private const float UpperEjectorApproachMetres = 0.012f;
        private const float LowerReleaseDistanceMetres = 0.0415f;
        private const float UpperReleaseDistanceMetres = 0.0375f;

        private Transform rod;
        private Transform pusher;
        private Transform workpiece;
        private Transform workpieceMotionRoot;
        private Vector3 pusherHomeWorldPosition;
        private Vector3 workpieceHomeWorldPosition;
        private Vector3 feedWorldDirection;
        private float deliveredDistance;
        private LiftCylinderTest lift;
        private PneumaticCylinderTest feedCylinder;
        private SecondCylinderTest lowerEjector;
        private ThirdCylinderTest upperEjector;
        private int activeRoute;
        private float routedDistance;
        private Vector3 routeStartWorldPosition;
        private Quaternion workpieceHomeWorldRotation;
        private Rigidbody workpieceBody;
        private bool releasedToGravity;
        private bool resetPending;
        private bool cycleHomePending;
        private GUIStyle headingStyle;
        private GUIStyle textStyle;
        private GUIStyle warningStyle;

        public bool IsFed => deliveredDistance >= FeedStrokeMetres - 0.0005f;
        public bool IsRouted => activeRoute != 0;
        public int ActiveRoute => activeRoute;
        public float RoutedDistanceMetres => routedDistance;
        public bool ReleasedToGravity => releasedToGravity;
        public bool ResetPending => resetPending;
        public bool ReadyAtMagazine => !resetPending && !IsFed && !releasedToGravity;
        public bool CycleHomeComplete => !cycleHomePending && MechanismsAreHome();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindFirstObjectByType<MagazineFeedTest>() == null)
            {
                new GameObject("Magazine Feed Test").AddComponent<MagazineFeedTest>();
            }
        }

        private void Start()
        {
            GameObject rodObject = GameObject.Find(RodComponentName);
            GameObject pusherObject = GameObject.Find(PusherComponentName);
            GameObject workpieceObject = GameObject.Find(WorkpieceComponentName);
            if (rodObject == null || pusherObject == null || workpieceObject == null)
            {
                Debug.LogError(
                    $"Magazine feed requires {RodComponentName}, {PusherComponentName}, and {WorkpieceComponentName}.");
                enabled = false;
                return;
            }

            rod = rodObject.transform;
            pusher = pusherObject.transform;
            workpiece = workpieceObject.transform;
            CreateCenteredWorkpieceRoot();
            pusherHomeWorldPosition = pusher.position;
            feedWorldDirection = -rod.TransformDirection(Vector3.up).normalized;
            lift = FindFirstObjectByType<LiftCylinderTest>();
            feedCylinder = FindFirstObjectByType<PneumaticCylinderTest>();
            lowerEjector = FindFirstObjectByType<SecondCylinderTest>();
            upperEjector = FindFirstObjectByType<ThirdCylinderTest>();
            if (lift == null || feedCylinder == null || lowerEjector == null || upperEjector == null)
            {
                Debug.LogError("Workpiece flow could not find the lift and both ejector controllers.");
                enabled = false;
                return;
            }

            SetWorkpieceColor(new Color(0.15f, 0.72f, 1f, 1f));
            ConfigureWorkpiecePhysics();
            AddStaticMeshCollider("Component_014");
            AddStaticMeshCollider("Component_023");
            AddStaticMeshCollider("Component_024");
            Debug.Log($"Magazine feed linked {WorkpieceComponentName} to Cylinder 1's forward motion.");
        }

        private void LateUpdate()
        {
            if (pusher == null || workpieceMotionRoot == null)
            {
                return;
            }

            float pusherDistance = Mathf.Clamp(
                Vector3.Dot(pusher.position - pusherHomeWorldPosition, feedWorldDirection),
                0f,
                FeedStrokeMetres);

            if (!Kit1AutomaticSequence.IsRunning && Keyboard.current != null &&
                Keyboard.current.mKey.wasPressedThisFrame)
            {
                BeginMasterReset();
            }

            if (resetPending)
            {
                CompleteMasterResetWhenSafe();
                return;
            }

            if (cycleHomePending)
            {
                if (MechanismsAreHome())
                {
                    cycleHomePending = false;
                    Debug.Log("Cycle mechanisms homed; queued magazine workpiece may now feed.");
                }
                else
                {
                    return;
                }
            }

            if (releasedToGravity)
            {
                return;
            }

            // A fed workpiece advances with the pusher but does not return when the
            // pneumatic cylinder retracts.
            deliveredDistance = Mathf.Max(deliveredDistance, pusherDistance);
            Vector3 workpiecePosition = workpieceHomeWorldPosition + feedWorldDirection * deliveredDistance;
            bool loadedOnLift = deliveredDistance >= FeedStrokeMetres - 0.0005f;
            if (loadedOnLift)
            {
                // The CAD workpiece location is the upper transfer level. Once loaded,
                // follow the platform downward from that upper reference position.
                float liftOffset = (lift.NormalizedPosition - 1f) * LiftCylinderTest.LiftStrokeMetres;
                workpiecePosition += lift.LiftWorldDirection * liftOffset;

                if (activeRoute == 0)
                {
                    TryStartRoute(workpiecePosition);
                }

                if (activeRoute != 0)
                {
                    UpdateRoutedWorkpiece(ref workpiecePosition);
                }
            }

            workpieceMotionRoot.position = workpiecePosition;

            if ((activeRoute == 3 && routedDistance >= UpperReleaseDistanceMetres) ||
                (activeRoute == 2 && routedDistance >= LowerReleaseDistanceMetres))
            {
                ReleaseToGravity();
            }

        }

        private void BeginMasterReset()
        {
            feedCylinder.CommandRetract();
            lowerEjector.CommandRetract();
            upperEjector.CommandRetract();
            lift.CommandUp();

            workpieceBody.isKinematic = true;
            workpieceBody.useGravity = false;
            workpieceBody.linearVelocity = Vector3.zero;
            workpieceBody.angularVelocity = Vector3.zero;
            resetPending = true;
            Debug.Log("Master reset requested: retracting ejectors and raising lift.");
        }

        public void RequestMasterReset()
        {
            BeginMasterReset();
        }

        public void RequestCycleHome()
        {
            feedCylinder.CommandRetract();
            lowerEjector.CommandRetract();
            upperEjector.CommandRetract();
            lift.CommandUp();
            cycleHomePending = true;
        }

        public bool ActivateWorkpiece(string componentName)
        {
            GameObject nextObject = GameObject.Find(componentName);
            if (nextObject == null)
            {
                Debug.LogError($"Cannot activate missing magazine workpiece {componentName}.");
                return false;
            }

            workpiece = nextObject.transform;
            CreateCenteredWorkpieceRoot();
            ConfigureWorkpiecePhysics();
            deliveredDistance = 0f;
            activeRoute = 0;
            routedDistance = 0f;
            releasedToGravity = false;
            resetPending = false;
            cycleHomePending = false;
            Debug.Log($"{componentName} loaded into the workpiece flow controller.");
            return true;
        }

        private bool MechanismsAreHome()
        {
            return feedCylinder.NormalizedPosition <= 0.02f &&
                lowerEjector.NormalizedPosition <= 0.02f &&
                upperEjector.NormalizedPosition <= 0.02f &&
                lift.NormalizedPosition >= 0.98f;
        }

        private void CompleteMasterResetWhenSafe()
        {
            if (!MechanismsAreHome())
            {
                return;
            }

            deliveredDistance = 0f;
            activeRoute = 0;
            routedDistance = 0f;
            releasedToGravity = false;
            resetPending = false;
            workpieceBody.isKinematic = true;
            workpieceBody.useGravity = false;
            workpieceBody.linearVelocity = Vector3.zero;
            workpieceBody.angularVelocity = Vector3.zero;
            workpieceMotionRoot.position = workpieceHomeWorldPosition;
            workpieceMotionRoot.rotation = workpieceHomeWorldRotation;
            workpieceMotionRoot.localScale = Vector3.one;
            workpieceBody.Sleep();
            Debug.Log($"Master reset complete; {WorkpieceComponentName} returned to the magazine.");
        }

        private void TryStartRoute(Vector3 currentWorkpiecePosition)
        {
            bool atUpperLevel = lift.NormalizedPosition >= 0.98f;
            bool atLowerLevel = lift.NormalizedPosition <= 0.02f;

            if (atUpperLevel && upperEjector.TravelDistanceMetres > UpperEjectorApproachMetres)
            {
                activeRoute = 3;
                routeStartWorldPosition = currentWorkpiecePosition;
                routedDistance = upperEjector.TravelDistanceMetres - UpperEjectorApproachMetres;
                Debug.Log("Workpiece engaged by Cylinder 3 at the upper sorting level.");
            }
            else if (atLowerLevel && lowerEjector.TravelDistanceMetres > LowerEjectorApproachMetres)
            {
                activeRoute = 2;
                routeStartWorldPosition = currentWorkpiecePosition;
                routedDistance = lowerEjector.TravelDistanceMetres - LowerEjectorApproachMetres;
                Debug.Log("Workpiece engaged by Cylinder 2 at the lower sorting level.");
            }
        }

        private void UpdateRoutedWorkpiece(ref Vector3 workpiecePosition)
        {
            if (activeRoute == 3)
            {
                float pushDistance = Mathf.Max(
                    0f, upperEjector.TravelDistanceMetres - UpperEjectorApproachMetres);
                routedDistance = Mathf.Max(routedDistance, pushDistance);
                workpiecePosition = routeStartWorldPosition +
                    upperEjector.TravelWorldDirection * routedDistance;
            }
            else
            {
                float pushDistance = Mathf.Max(
                    0f, lowerEjector.TravelDistanceMetres - LowerEjectorApproachMetres);
                routedDistance = Mathf.Max(routedDistance, pushDistance);
                workpiecePosition = routeStartWorldPosition +
                    lowerEjector.TravelWorldDirection * routedDistance;
            }
        }

        private void CreateCenteredWorkpieceRoot()
        {
            MeshRenderer rendererComponent = workpiece.GetComponent<MeshRenderer>();
            Vector3 visualCenter = rendererComponent != null
                ? rendererComponent.bounds.center
                : workpiece.position;

            GameObject rootObject = new GameObject("Workpiece Physics Root");
            workpieceMotionRoot = rootObject.transform;
            workpieceMotionRoot.SetPositionAndRotation(visualCenter, Quaternion.identity);
            workpieceMotionRoot.localScale = Vector3.one;
            workpiece.SetParent(workpieceMotionRoot, true);

            workpieceHomeWorldPosition = workpieceMotionRoot.position;
            workpieceHomeWorldRotation = workpieceMotionRoot.rotation;
        }

        private void ConfigureWorkpiecePhysics()
        {
            MeshRenderer rendererComponent = workpiece.GetComponent<MeshRenderer>();
            BoxCollider boxCollider = workpieceMotionRoot.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = workpieceMotionRoot.gameObject.AddComponent<BoxCollider>();
            }

            if (rendererComponent != null)
            {
                boxCollider.center = Vector3.zero;
                // Leave clearance inside the narrow sheet-metal chute. The rendered
                // workpiece remains full size; only its collision envelope is reduced.
                boxCollider.size = Vector3.Scale(
                    rendererComponent.bounds.size,
                    new Vector3(0.86f, 0.86f, 0.92f));
            }

            PhysicsMaterial material = new PhysicsMaterial("Sorting Workpiece")
            {
                dynamicFriction = 0.04f,
                staticFriction = 0.04f,
                bounciness = 0.01f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            boxCollider.material = material;

            workpieceBody = workpieceMotionRoot.GetComponent<Rigidbody>();
            if (workpieceBody == null)
            {
                workpieceBody = workpieceMotionRoot.gameObject.AddComponent<Rigidbody>();
            }

            workpieceBody.mass = 0.05f;
            workpieceBody.useGravity = false;
            workpieceBody.isKinematic = true;
            workpieceBody.interpolation = RigidbodyInterpolation.Interpolate;
            workpieceBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            workpieceBody.constraints = RigidbodyConstraints.None;
        }

        private static void AddStaticMeshCollider(string componentName)
        {
            GameObject componentObject = GameObject.Find(componentName);
            if (componentObject == null || componentObject.GetComponent<Collider>() != null)
            {
                return;
            }

            MeshFilter meshFilter = componentObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return;
            }

            MeshCollider colliderComponent = componentObject.AddComponent<MeshCollider>();
            colliderComponent.sharedMesh = meshFilter.sharedMesh;
            colliderComponent.convex = false;
        }

        private void ReleaseToGravity()
        {
            if (releasedToGravity)
            {
                return;
            }

            releasedToGravity = true;
            Vector3 dischargeDirection = activeRoute == 3
                ? upperEjector.TravelWorldDirection
                : lowerEjector.TravelWorldDirection;

            workpieceBody.isKinematic = false;
            workpieceBody.useGravity = true;
            workpieceBody.linearVelocity = dischargeDirection * 0.28f + Vector3.down * 0.06f;
            Debug.Log($"Workpiece released to gravity from route {activeRoute}.");
        }

        public void SetWorkpieceColor(Color color)
        {
            if (workpiece == null)
            {
                GameObject workpieceObject = GameObject.Find(WorkpieceComponentName);
                workpiece = workpieceObject != null ? workpieceObject.transform : null;
            }

            if (workpiece == null)
            {
                return;
            }

            MeshRenderer rendererComponent = workpiece.GetComponent<MeshRenderer>();
            if (rendererComponent == null)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            rendererComponent.GetPropertyBlock(block);
            block.SetColor(Shader.PropertyToID("_BaseColor"), color);
            rendererComponent.SetPropertyBlock(block);
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
                normal = { textColor = new Color(0.15f, 0.72f, 1f) }
            };
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };
            warningStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = new Color(1f, 0.78f, 0.3f) }
            };

            Rect panel = new Rect(510f, 490f, 470f, 205f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(526f, 502f, 430f, 32f), "MAGAZINE FEED TEST", headingStyle);
            GUI.Label(
                new Rect(526f, 537f, 430f, 72f),
                $"Workpiece: {WorkpieceComponentName}\n" +
                $"Feed: {deliveredDistance * 1000f:0}/{FeedStrokeMetres * 1000f:0} mm    " +
                $"Lift load: {(deliveredDistance >= FeedStrokeMetres - 0.0005f ? "ATTACHED" : "WAITING")}\n" +
                $"Route: {(activeRoute == 3 ? "UPPER / CYLINDER 3" : activeRoute == 2 ? "LOWER / CYLINDER 2" : "NOT EJECTED")}    " +
                $"Gravity: {(releasedToGravity ? "ON" : "OFF")}",
                textStyle);
            GUI.Label(
                new Rect(526f, 616f, 430f, 70f),
                "Upper: V then U    |    Lower: C then T\n" +
                "Retract: J/G    |    M: master reset anytime",
                warningStyle);
        }
    }
}
