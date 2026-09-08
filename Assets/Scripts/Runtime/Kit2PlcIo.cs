using UnityEngine;

namespace Kit1DigitalTwin
{
    public enum Kit2CylinderCommand
    {
        Home,
        Stage,
        Eject
    }

    public sealed class Kit2PlcIo : MonoBehaviour
    {
        private Kit2MagazineFeedCalibration flow;
        private Kit2FeedCylinderCalibration feed;
        private Kit2SecondCylinderCalibration transfer;
        private Kit2StampingCylinderCalibration stamps;
        private Kit2ElectropneumaticUtilities utilities;

        public bool DI_MagazinePartPresent { get; private set; }
        public bool DI_MagazineEmpty { get; private set; }
        public bool DI_PartAtStampA { get; private set; }
        public bool DI_PartAtStampB { get; private set; }
        public bool DI_MetalDetected { get; private set; }
        public bool DI_CapacitiveDetected { get; private set; }
        public bool DI_FeedHome { get; private set; }
        public bool DI_FeedStage { get; private set; }
        public bool DI_FeedEject { get; private set; }
        public bool DI_TransferHome { get; private set; }
        public bool DI_TransferStage { get; private set; }
        public bool DI_TransferEject { get; private set; }
        public bool DI_StampAUp { get; private set; }
        public bool DI_StampADown { get; private set; }
        public bool DI_StampBUp { get; private set; }
        public bool DI_StampBDown { get; private set; }
        public bool DI_UtilityReady => utilities != null && utilities.OperationPermitted;

        public Kit2CylinderCommand DO_FeedCommand { get; private set; }
        public Kit2CylinderCommand DO_TransferCommand { get; private set; }
        public bool DO_StampADown { get; private set; }
        public bool DO_StampBDown { get; private set; }
        public bool OutputControlEnabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit2_StampingStation") != null &&
                FindFirstObjectByType<Kit2PlcIo>() == null)
            {
                new GameObject("Kit 2 PLC IO").AddComponent<Kit2PlcIo>();
            }
        }

        private void Start()
        {
            flow = FindFirstObjectByType<Kit2MagazineFeedCalibration>();
            feed = FindFirstObjectByType<Kit2FeedCylinderCalibration>();
            transfer = FindFirstObjectByType<Kit2SecondCylinderCalibration>();
            stamps = FindFirstObjectByType<Kit2StampingCylinderCalibration>();
            utilities = FindFirstObjectByType<Kit2ElectropneumaticUtilities>();
            if (flow == null || feed == null || transfer == null || stamps == null || utilities == null)
            {
                Debug.LogError("Kit 2 PLC I/O could not find every runtime controller.");
                enabled = false;
                return;
            }
            SampleInputs();
            Debug.Log("KIT2_PLC_IO_READY: controller-neutral logical tag layer online.");
        }

        private void Update()
        {
            SampleInputs();
            if (OutputControlEnabled)
            {
                ApplyOutputs();
            }
        }

        public void EnableOutputControl(bool enabledState)
        {
            OutputControlEnabled = enabledState;
        }

        public void SetFeedCommand(Kit2CylinderCommand command) => DO_FeedCommand = command;
        public void SetTransferCommand(Kit2CylinderCommand command) => DO_TransferCommand = command;
        public void SetStampADown(bool down) => DO_StampADown = down;
        public void SetStampBDown(bool down) => DO_StampBDown = down;

        public void SetSafeHomeOutputs()
        {
            DO_FeedCommand = Kit2CylinderCommand.Home;
            DO_TransferCommand = Kit2CylinderCommand.Home;
            DO_StampADown = false;
            DO_StampBDown = false;
        }

        public void ForceSafeHome()
        {
            SetSafeHomeOutputs();
            ApplyOutputs();
            OutputControlEnabled = false;
        }

        private void SampleInputs()
        {
            if (flow == null)
            {
                return;
            }
            DI_MagazinePartPresent = flow.HasActiveWorkpiece;
            DI_MagazineEmpty = flow.MagazineEmpty;
            DI_PartAtStampA = flow.ActiveAtStampA;
            DI_PartAtStampB = flow.StampBOccupied;
            DI_MetalDetected = flow.MetalSensorActive;
            DI_CapacitiveDetected = flow.CapacitiveSensorActive;
            DI_FeedHome = feed.IsHome;
            DI_FeedStage = feed.IsAtStage;
            DI_FeedEject = feed.IsAtEject;
            DI_TransferHome = transfer.IsHome;
            DI_TransferStage = transfer.IsAtStage;
            DI_TransferEject = transfer.IsAtEject;
            DI_StampAUp = stamps.StampAHome;
            DI_StampADown = stamps.StampADown;
            DI_StampBUp = stamps.StampBHome;
            DI_StampBDown = stamps.StampBDown;

            switch (utilities.SensorFault)
            {
                case Kit2SensorFault.MagazinePresence: DI_MagazinePartPresent = false; break;
                case Kit2SensorFault.MetalSensor: DI_MetalDetected = false; break;
                case Kit2SensorFault.CapacitiveSensor: DI_CapacitiveDetected = false; break;
                case Kit2SensorFault.StampAPosition:
                    DI_StampAUp = false;
                    DI_StampADown = false;
                    break;
                case Kit2SensorFault.StampBPosition:
                    DI_StampBUp = false;
                    DI_StampBDown = false;
                    break;
            }
        }

        private void ApplyOutputs()
        {
            ApplyCylinder(DO_FeedCommand,
                feed.CommandRetract, feed.CommandExtend, feed.CommandEject);
            ApplyCylinder(DO_TransferCommand,
                transfer.CommandRetract, transfer.CommandExtend, transfer.CommandEject);
            if (DO_StampADown) stamps.CommandStampADown(); else stamps.CommandStampAUp();
            if (DO_StampBDown) stamps.CommandStampBDown(); else stamps.CommandStampBUp();
        }

        private static void ApplyCylinder(
            Kit2CylinderCommand command,
            System.Action home,
            System.Action stage,
            System.Action eject)
        {
            switch (command)
            {
                case Kit2CylinderCommand.Stage: stage(); break;
                case Kit2CylinderCommand.Eject: eject(); break;
                default: home(); break;
            }
        }
    }
}
