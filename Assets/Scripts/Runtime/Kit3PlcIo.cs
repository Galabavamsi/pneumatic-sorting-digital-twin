using UnityEngine;

namespace Kit1DigitalTwin
{
    [DefaultExecutionOrder(-50)]
    public sealed class Kit3PlcIo : MonoBehaviour
    {
        private Kit3ManualCommissioning handling;
        private Kit3RotaryCalibration rotary;
        private bool outputsDirty;

        public bool DI_MagazineReady { get; private set; }
        public bool DI_MagazineEmpty { get; private set; }
        public bool DI_PartAtStamp { get; private set; }
        public bool DI_PartBlue { get; private set; }
        public bool DI_PartOrange => DI_PartAtStamp && !DI_PartBlue;
        public bool DI_FeedHome { get; private set; }
        public bool DI_FeedExtended { get; private set; }
        public bool DI_StampUp { get; private set; }
        public bool DI_StampDown { get; private set; }
        public bool DI_VacuumGrip { get; private set; }
        public bool DI_SlideHome { get; private set; }
        public bool DI_SlideExtended { get; private set; }
        public bool DI_LiftDown { get; private set; }
        public bool DI_LiftUp { get; private set; }
        public bool DI_RotaryBin1 { get; private set; }
        public bool DI_RotaryBin2 { get; private set; }

        public bool DO_FeedExtend { get; private set; }
        public bool DO_StampDown { get; private set; }
        public bool DO_VacuumOn { get; private set; }
        public bool DO_SlideExtend { get; private set; }
        public bool DO_LiftUp { get; private set; }
        public bool DO_RouteBin2 { get; private set; }
        public bool OutputControlEnabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (GameObject.Find("Kit3_AssemblyStation") != null &&
                FindAnyObjectByType<Kit3PlcIo>() == null)
            {
                new GameObject("Kit 3 PLC IO").AddComponent<Kit3PlcIo>();
            }
        }

        private void Start()
        {
            handling = FindFirstObjectByType<Kit3ManualCommissioning>();
            rotary = FindFirstObjectByType<Kit3RotaryCalibration>();
            if (handling == null || rotary == null)
            {
                Debug.LogError("KIT3_PLC_IO_MISSING: mechanism controllers unavailable.");
                enabled = false;
                return;
            }
            SampleInputs();
            Debug.Log("KIT3_PLC_IO_READY: controller-neutral logical tags online.");
        }

        private void Update()
        {
            SampleInputs();
            if (OutputControlEnabled && outputsDirty)
            {
                ApplyOutputs();
                outputsDirty = false;
            }
        }

        public void EnableOutputControl(bool enabledState)
        {
            OutputControlEnabled = enabledState;
            outputsDirty |= enabledState;
        }
        public void SetFeedExtended(bool value) { DO_FeedExtend = value; outputsDirty = true; }
        public void SetStampDown(bool value) { DO_StampDown = value; outputsDirty = true; }
        public void SetVacuum(bool value) { DO_VacuumOn = value; outputsDirty = true; }
        public void SetSlideExtended(bool value) { DO_SlideExtend = value; outputsDirty = true; }
        public void SetLiftUp(bool value) { DO_LiftUp = value; outputsDirty = true; }
        public void SetRouteBin2(bool value) { DO_RouteBin2 = value; outputsDirty = true; }

        public void SetSafeHomeOutputs()
        {
            DO_FeedExtend = false;
            DO_StampDown = false;
            DO_VacuumOn = false;
            DO_SlideExtend = false;
            DO_LiftUp = false;
            DO_RouteBin2 = false;
            outputsDirty = true;
        }

        public void ForceSafeHome()
        {
            SetSafeHomeOutputs();
            ApplyOutputs();
            outputsDirty = false;
            OutputControlEnabled = false;
        }

        private void SampleInputs()
        {
            if (handling == null || rotary == null)
            {
                return;
            }
            DI_MagazineReady = handling.CanFeed;
            DI_MagazineEmpty = handling.MagazineEmpty;
            DI_PartAtStamp = handling.PartAtStamp;
            DI_PartBlue = handling.PartAtStampIsBlue;
            DI_FeedHome = handling.FeedIsHome;
            DI_FeedExtended = handling.FeedIsExtended;
            DI_StampUp = handling.StampIsUp;
            DI_StampDown = handling.StampIsDown;
            DI_VacuumGrip = handling.VacuumHasWorkpiece;
            DI_SlideHome = rotary.SlideIsHome;
            DI_SlideExtended = rotary.SlideIsExtended;
            DI_LiftDown = rotary.LiftIsDown;
            DI_LiftUp = rotary.LiftIsUp;
            DI_RotaryBin1 = rotary.IsAtBin1;
            DI_RotaryBin2 = rotary.IsAtBin2;
        }

        private void ApplyOutputs()
        {
            if (DO_FeedExtend) handling.CommandFeedExtend();
            else handling.CommandFeedRetract();
            if (DO_StampDown) handling.CommandStampDown();
            else handling.CommandStampUp();
            handling.CommandVacuum(DO_VacuumOn);
            if (DO_SlideExtend) rotary.CommandSlideExtend();
            else rotary.CommandSlideRetract();
            if (DO_LiftUp) rotary.CommandLiftUp();
            else rotary.CommandLiftDown();
            if (DO_RouteBin2) rotary.CommandBin2();
            else rotary.CommandBin1();
        }
    }
}
