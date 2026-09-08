using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class Kit1DebugOverlay : MonoBehaviour
    {
        private static Kit1DebugOverlay instance;
        private static int lastToggleFrame = -1;
        private GameObject viewerUi;

        public static bool Visible { get; private set; }
        public static bool LegacyPanelsVisible => false;

        public static void ToggleVisibility()
        {
            if (lastToggleFrame == Time.frameCount)
            {
                return;
            }

            lastToggleFrame = Time.frameCount;
            Visible = !Visible;
            Debug.Log($"Engineering dashboard {(Visible ? "shown" : "hidden")}.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!KitSceneContext.IsKit1Scene)
            {
                return;
            }

            Visible = false;
            lastToggleFrame = -1;
            if (FindFirstObjectByType<Kit1DebugOverlay>() == null)
            {
                new GameObject("Debug Overlay Toggle").AddComponent<Kit1DebugOverlay>();
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Start()
        {
            viewerUi = GameObject.Find("Viewer UI");
            ApplyVisibility();
            Debug.Log("Engineering dashboard hidden. Press F1 to show or hide it.");
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                ToggleVisibility();
                ApplyVisibility();
            }
        }

        private void ApplyVisibility()
        {
            if (viewerUi != null)
            {
                viewerUi.SetActive(false);
            }
        }
    }
}
