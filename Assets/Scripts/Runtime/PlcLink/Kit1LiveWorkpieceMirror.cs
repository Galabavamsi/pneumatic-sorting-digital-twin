using Kit1DigitalTwin.Hmi;
using UnityEngine;

namespace Kit1DigitalTwin.PlcLink
{
    // While the PLC link is live, keeps the twin's workpiece story consistent
    // with the real machine. The actuators already follow the real solenoids
    // through Kit1PlcIo, so MagazineFeedTest moves the part physically; this
    // component adds what only the PLC knows:
    //  - the part shows grey until the real IS-01/CS-01 classify it, then
    //    turns blue (metal) or orange (plastic);
    //  - the twin's magazine stack drops when the real feeder has fed;
    //  - the next twin workpiece is loaded once the previous one has left and
    //    the machine is home;
    //  - PLC counts, cycle times and faults land in ProductionMonitor, so the
    //    existing F1 dashboard and historian show the real production.
    public sealed class Kit1LiveWorkpieceMirror : MonoBehaviour
    {
        // Dark charcoal: clearly "not yet classified" against the light blue-grey station.
        private static readonly Color UnknownColour = new Color(0.16f, 0.17f, 0.19f, 1f);

        private MagazineFeedTest workpieceFlow;
        private Kit1MagazineBatch magazine;
        private Kit1SensorSimulation sensors;
        private PneumaticCylinderTest feedCylinder;
        private ProductionMonitor productionMonitor;
        private bool wasLive;
        private Kit1PlcMaterial lastMaterial = Kit1PlcMaterial.None;
        private int lastPartsCompleted = -1;
        private Kit1PlcFault lastFault = Kit1PlcFault.None;
        private bool lastEStopOk = true;
        private int lastTwinRoute;
        private bool wasRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindAnyObjectByType<Kit1LiveWorkpieceMirror>() == null)
            {
                new GameObject("Kit 1 Live Workpiece Mirror").AddComponent<Kit1LiveWorkpieceMirror>();
            }
        }

        private void Start()
        {
            workpieceFlow = FindAnyObjectByType<MagazineFeedTest>();
            magazine = FindAnyObjectByType<Kit1MagazineBatch>();
            sensors = FindAnyObjectByType<Kit1SensorSimulation>();
            feedCylinder = FindAnyObjectByType<PneumaticCylinderTest>();
            productionMonitor = FindAnyObjectByType<ProductionMonitor>();
        }

        private void Update()
        {
            Kit1PlcLink link = Kit1PlcLink.Instance;
            Kit1PlcSnapshot snapshot = link != null && Kit1PlcLink.LinkRequested ? link.LatestFresh : null;
            bool live = snapshot != null;

            if (wasRequested && !Kit1PlcLink.LinkRequested)
            {
                magazine?.RestoreStackColours();
            }

            wasRequested = Kit1PlcLink.LinkRequested;

            if (live != wasLive)
            {
                productionMonitor?.RecordEvent(
                    live ? "PLC live link: twin now mirrors the real machine."
                         : "PLC live link: no fresh data; twin holding last pose.",
                    live ? ProductionEventSeverity.Success : ProductionEventSeverity.Warning);
                if (live)
                {
                    lastPartsCompleted = snapshot.PartsCompleted;
                    lastMaterial = Kit1PlcMaterial.None;
                    lastFault = snapshot.FaultCode;
                    lastEStopOk = snapshot.EStopOk;
                    if (workpieceFlow != null && !workpieceFlow.IsFed)
                    {
                        workpieceFlow.SetWorkpieceColor(UnknownColour);
                    }

                    magazine?.SetStackColour(UnknownColour);
                }

                wasLive = live;
            }

            if (!live || workpieceFlow == null || sensors == null)
            {
                return;
            }

            MirrorMaterial(snapshot);
            CheckRouteSync(snapshot);
            MirrorMagazine();
            MirrorProduction(snapshot);
        }

        // When the twin's ejector picks up the part, the PLC must be in the
        // matching eject step. A mismatch means the twin's workpiece story has
        // drifted (e.g. the link was switched on mid-cycle): press M to resync.
        private void CheckRouteSync(Kit1PlcSnapshot snapshot)
        {
            int route = workpieceFlow.ActiveRoute;
            if (route == lastTwinRoute)
            {
                return;
            }

            lastTwinRoute = route;
            if (route == 0)
            {
                return;
            }

            bool twinUpper = route == 3;
            bool plcAgrees = twinUpper
                ? snapshot.Step == Kit1PlcStep.UpperEject || snapshot.Step == Kit1PlcStep.UpperRetract
                : snapshot.Step == Kit1PlcStep.LowerEject || snapshot.Step == Kit1PlcStep.LowerRetract;
            string message = $"Twin routed part {(twinUpper ? "UPPER" : "LOWER")} while PLC is at {snapshot.Step} " +
                $"(material {snapshot.Material}, PLC parts done {snapshot.PartsCompleted}).";
            if (plcAgrees)
            {
                Debug.Log(message + " In sync.");
            }
            else
            {
                Debug.LogWarning(message + " OUT OF SYNC: press M with the machine home to resync the twin.");
                productionMonitor?.RecordAlarm("Twin workpiece out of sync with PLC; press M when home.");
            }
        }

        private void MirrorMaterial(Kit1PlcSnapshot snapshot)
        {
            if (snapshot.Material == lastMaterial)
            {
                return;
            }

            if (snapshot.Material == Kit1PlcMaterial.Metal || snapshot.Material == Kit1PlcMaterial.Plastic)
            {
                Kit1SensorSimulation.WorkpieceMaterial material = snapshot.Material == Kit1PlcMaterial.Metal
                    ? Kit1SensorSimulation.WorkpieceMaterial.Metal
                    : Kit1SensorSimulation.WorkpieceMaterial.Plastic;
                sensors.SetAutomaticMaterial(material);
                productionMonitor?.BeginPart(material.ToString(), magazine != null ? magazine.RemainingCount : 0);
                productionMonitor?.RecordDetection("per PLC recipe (real IS-01 / CS-01)");
            }

            lastMaterial = snapshot.Material;
        }

        private void MirrorMagazine()
        {
            if (magazine == null || feedCylinder == null)
            {
                return;
            }

            // The real feeder pushed the part and came back: release the twin stack.
            if (workpieceFlow.IsFed && feedCylinder.NormalizedPosition <= 0.02f)
            {
                magazine.NotifyCurrentWorkpieceFed();
            }

            // Previous part has left the platform and the machine is home: load the next one.
            if (workpieceFlow.ReleasedToGravity && workpieceFlow.CycleHomeComplete && magazine.IsSettled)
            {
                if (magazine.PromotePreparedWorkpiece(out string componentName, out _) &&
                    workpieceFlow.ActivateWorkpiece(componentName))
                {
                    workpieceFlow.SetWorkpieceColor(UnknownColour);
                }
                else
                {
                    // Twin magazine has run out of modelled parts: recycle the visual one.
                    workpieceFlow.RequestMasterReset();
                    workpieceFlow.SetWorkpieceColor(UnknownColour);
                }
            }
        }

        private void MirrorProduction(Kit1PlcSnapshot snapshot)
        {
            if (productionMonitor == null)
            {
                return;
            }

            if (snapshot.PartsCompleted > lastPartsCompleted && lastPartsCompleted >= 0)
            {
                productionMonitor.CompletePart();
                Debug.Log($"PLC completed part {snapshot.PartsCompleted} " +
                    $"(metal {snapshot.MetalCount} / plastic {snapshot.PlasticCount}, cycle {snapshot.LastCycleTimeMs} ms).");
            }

            lastPartsCompleted = snapshot.PartsCompleted;

            if (snapshot.FaultCode != lastFault)
            {
                if (snapshot.FaultCode != Kit1PlcFault.None)
                {
                    productionMonitor.AbortPart($"PLC fault {(int)snapshot.FaultCode}: {snapshot.FaultCode}.");
                    productionMonitor.RecordAlarm($"PLC fault {(int)snapshot.FaultCode}: {snapshot.FaultCode}.");
                }
                else
                {
                    productionMonitor.RecordEvent("PLC fault cleared.", ProductionEventSeverity.Info);
                }

                lastFault = snapshot.FaultCode;
            }

            if (snapshot.EStopOk != lastEStopOk)
            {
                if (!snapshot.EStopOk)
                {
                    productionMonitor.RecordAlarm("Real E-stop pressed.");
                }

                lastEStopOk = snapshot.EStopOk;
            }
        }
    }
}
