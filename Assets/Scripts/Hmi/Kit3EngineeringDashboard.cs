using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Kit1DigitalTwin.Hmi
{
    public sealed class Kit3EngineeringDashboard : MonoBehaviour
    {
        private Kit3AutomaticSequence sequence;
        private Kit3ManualCommissioning handling;
        private Kit3RotaryCalibration rotary;
        private Kit3PlcIo io;
        private ProductionMonitor production;
        private GameObject panelObject;
        private GameObject launcherObject;
        private Text statusText;
        private bool visible;
        private bool readinessLogged;

        public static bool Visible { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Visible = false;
            if (GameObject.Find("Kit3_AssemblyStation") != null &&
                FindAnyObjectByType<Kit3EngineeringDashboard>() == null)
            {
                new GameObject("Kit 3 Engineering Dashboard")
                    .AddComponent<Kit3EngineeringDashboard>();
            }
        }

        private void Start()
        {
            ResolveDependencies();
            BuildCanvas();
            SetVisible(false);
        }

        private void Update()
        {
            ResolveDependencies();
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                SetVisible(!visible);
            }
            if (visible)
            {
                RefreshStatus();
            }
        }

        private void SetVisible(bool value)
        {
            visible = value;
            Visible = value;
            if (panelObject != null)
            {
                panelObject.SetActive(value);
            }
            if (launcherObject != null)
            {
                launcherObject.SetActive(!value);
            }
            Debug.Log(value ? "KIT3_DASHBOARD_SHOWN" : "KIT3_DASHBOARD_HIDDEN");
        }

        private void ResolveDependencies()
        {
            sequence ??= FindAnyObjectByType<Kit3AutomaticSequence>();
            handling ??= FindAnyObjectByType<Kit3ManualCommissioning>();
            rotary ??= FindAnyObjectByType<Kit3RotaryCalibration>();
            io ??= FindAnyObjectByType<Kit3PlcIo>();
            production ??= FindAnyObjectByType<ProductionMonitor>();
            if (!readinessLogged && sequence != null && handling != null &&
                rotary != null && io != null)
            {
                readinessLogged = true;
                Debug.Log("KIT3_DASHBOARD_READY: runtime controllers connected.");
            }
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("Kit 3 Dashboard Canvas");
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("Kit 3 Dashboard Event System");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<InputSystemUIInputModule>();
            }

            launcherObject = CreateLauncher(canvasObject.transform);

            panelObject = new GameObject("Engineering Panel");
            panelObject.transform.SetParent(canvasObject.transform, false);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.012f, 0.022f, 0.035f, 0.97f);
            RectTransform panel = panelImage.rectTransform;
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(24f, -165f);
            panel.sizeDelta = new Vector2(800f, 405f);

            Text title = CreateText(panelObject.transform, "Title", 28, FontStyle.Bold,
                new Color(0.25f, 0.86f, 1f), TextAnchor.UpperLeft);
            SetRect(title.rectTransform, new Vector2(24f, -18f), new Vector2(752f, 42f));
            title.text = "KIT 3 · AUTOMATIC STAMP & COLOR SORT";

            statusText = CreateText(panelObject.transform, "Live Status", 20, FontStyle.Normal,
                new Color(0.88f, 0.93f, 0.97f), TextAnchor.UpperLeft);
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText.verticalOverflow = VerticalWrapMode.Overflow;
            SetRect(statusText.rectTransform, new Vector2(24f, -68f), new Vector2(752f, 235f));

            const float buttonY = -326f;
            CreateButton(panelObject.transform, "START BATCH", 24f, buttonY, 140f,
                () => sequence?.RequestStartContinuous());
            CreateButton(panelObject.transform, "STEP", 174f, buttonY, 108f,
                () => sequence?.RequestStep());
            CreateButton(panelObject.transform, "PAUSE", 292f, buttonY, 108f,
                () => sequence?.RequestPauseToggle());
            CreateButton(panelObject.transform, "SAFE STOP", 410f, buttonY, 130f,
                () => sequence?.RequestSafeStop());
            CreateButton(panelObject.transform, "RESET", 550f, buttonY, 108f,
                () => sequence?.RequestReset());
            CreateButton(panelObject.transform, "CLOSE", 668f, buttonY, 108f,
                () => SetVisible(false));

            Text hint = CreateText(panelObject.transform, "Hint", 16, FontStyle.Normal,
                new Color(0.65f, 0.75f, 0.83f), TextAnchor.UpperLeft);
            SetRect(hint.rectTransform, new Vector2(24f, -374f), new Vector2(752f, 24f));
            hint.text = "F1 close  •  S start  •  N step  •  P pause  •  X safe stop";
        }

        private GameObject CreateLauncher(Transform parent)
        {
            GameObject item = new GameObject("F1 Engineering Launcher");
            item.transform.SetParent(parent, false);
            Image image = item.AddComponent<Image>();
            image.color = new Color(0.08f, 0.55f, 0.76f, 0.98f);
            Button button = item.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => SetVisible(true));
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(220f, 44f);
            Text text = CreateText(item.transform, "Label", 18, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.text = "F1  ENGINEERING";
            return item;
        }

        private void RefreshStatus()
        {
            if (statusText == null)
            {
                return;
            }
            if (sequence == null || handling == null || rotary == null || io == null)
            {
                statusText.text = "INITIALIZING RUNTIME CONTROLLERS…\n" +
                    $"Sequence {(sequence != null ? "OK" : "WAIT")}  •  " +
                    $"Handling {(handling != null ? "OK" : "WAIT")}  •  " +
                    $"Rotary {(rotary != null ? "OK" : "WAIT")}  •  " +
                    $"PLC I/O {(io != null ? "OK" : "WAIT")}";
                return;
            }

            string mode = Kit3AutomaticSequence.IsRunning
                ? sequence.IsPaused ? "PAUSED" : sequence.IsStepMode ? "STEP" : "AUTOMATIC"
                : "MANUAL / READY";
            string fault = sequence.FaultCount > 0 ? sequence.LastFault : "No active fault";
            statusText.text =
                $"{mode}  •  STATE: {sequence.CurrentState}  •  {sequence.RouteText}\n\n" +
                $"COMPLETED  {sequence.BlueCount + sequence.OrangeCount}     " +
                $"BLUE / BIN 1  {sequence.BlueCount}     ORANGE / BIN 2  {sequence.OrangeCount}\n\n" +
                $"Magazine {handling.RemainingCount} remaining     " +
                $"Feed {(io.DI_FeedHome ? "HOME" : io.DI_FeedExtended ? "EXTENDED" : "MOVING")}     " +
                $"Stamp {(io.DI_StampUp ? "UP" : io.DI_StampDown ? "DOWN" : "MOVING")}\n" +
                $"Vacuum {(io.DI_VacuumGrip ? "GRIPPED" : io.DO_VacuumOn ? "ON" : "OFF")}     " +
                $"Slide {rotary.SlideTravelMetres * 1000f:0} mm     " +
                $"Lift {rotary.LiftTravelMetres * 1000f:0} mm     " +
                $"Rotary {rotary.CurrentAngleDegrees:0}°\n\n" +
                $"Cycle {(production != null ? production.LastCycleSeconds : 0f):0.00} s     " +
                $"Faults {sequence.FaultCount}     {fault}";
        }

        private static Text CreateText(Transform parent, string name, int size,
            FontStyle style, Color color, TextAnchor alignment)
        {
            GameObject item = new GameObject(name);
            item.transform.SetParent(parent, false);
            Text text = item.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            return text;
        }

        private static void CreateButton(Transform parent, string label, float x,
            float y, float width, UnityEngine.Events.UnityAction action)
        {
            GameObject item = new GameObject(label);
            item.transform.SetParent(parent, false);
            Image image = item.AddComponent<Image>();
            image.color = new Color(0.08f, 0.35f, 0.52f, 1f);
            Button button = item.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            SetRect(image.rectTransform, new Vector2(x, y), new Vector2(width, 38f));
            Text text = CreateText(item.transform, "Label", 16, FontStyle.Bold,
                Color.white, TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.text = label;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
