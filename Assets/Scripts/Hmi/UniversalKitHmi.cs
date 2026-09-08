using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin.Hmi
{
    public sealed class UniversalKitHmi : MonoBehaviour
    {
        private KitHmiAdapterBase adapter;
        private bool visible = true;
        private bool expanded;
        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindFirstObjectByType<UniversalKitHmi>() == null)
            {
                new GameObject("Universal Kit HMI").AddComponent<UniversalKitHmi>();
            }
        }

        private void Start()
        {
            adapter = FindFirstObjectByType<KitHmiAdapterBase>();
        }

        private void Update()
        {
            adapter ??= FindFirstObjectByType<KitHmiAdapterBase>();
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.f2Key.wasPressedThisFrame)
            {
                visible = !visible;
            }

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                expanded = !expanded;
            }
        }

        private void OnGUI()
        {
            if (!visible || adapter == null || Kit1DebugOverlay.Visible)
            {
                return;
            }

            InitializeStyles();
            float width = expanded ? 440f : 370f;
            float height = expanded ? 610f : 142f;
            float x = Mathf.Max(12f, Screen.width - width - 18f);
            Rect panel = new Rect(x, 18f, width, height);

            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.04f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(x + 16f, 29f, width - 32f, 30f),
                $"{adapter.KitId}  |  {adapter.KitTitle.ToUpperInvariant()}", titleStyle);
            GUI.Label(new Rect(x + 16f, 61f, width - 32f, 24f),
                $"{adapter.ConnectionStatus}    •    {adapter.StatusText}", statusStyle);

            if (!string.IsNullOrEmpty(adapter.AlarmText))
            {
                GUI.Label(new Rect(x + 16f, 84f, width - 32f, 22f), adapter.AlarmText, statusStyle);
            }
            else
            {
                GUI.Label(new Rect(x + 16f, 84f, width - 32f, 22f),
                    $"Magazine remaining: {adapter.RemainingWorkpieces}", labelStyle);
            }

            DrawPrimaryControls(x, 108f, width);
            if (expanded)
            {
                DrawExpandedPanel(x, width);
            }
        }

        private void DrawPrimaryControls(float x, float y, float width)
        {
            float buttonWidth = (width - 47f) / 4f;
            if (GUI.Button(new Rect(x + 10f, y, buttonWidth, 27f),
                adapter.Mode == HmiOperatingMode.Automatic ? "AUTO" : "MANUAL"))
            {
                adapter.SetMode(adapter.Mode == HmiOperatingMode.Automatic
                    ? HmiOperatingMode.Manual
                    : HmiOperatingMode.Automatic);
            }

            if (GUI.Button(new Rect(x + 15f + buttonWidth, y, buttonWidth, 27f), "START"))
            {
                adapter.StartAutomatic();
            }

            if (GUI.Button(new Rect(x + 20f + buttonWidth * 2f, y, buttonWidth, 27f), "STOP"))
            {
                adapter.Stop();
            }

            if (GUI.Button(new Rect(x + 25f + buttonWidth * 3f, y, buttonWidth, 27f), "RESET"))
            {
                adapter.ResetMachine();
            }
        }

        private void DrawExpandedPanel(float x, float width)
        {
            GUI.Label(new Rect(x + 16f, 155f, width - 32f, 25f),
                "MANUAL OUTPUTS  •  click command to toggle", statusStyle);
            for (int index = 0; index < adapter.ManualOutputCount; index++)
            {
                float y = 184f + index * 36f;
                bool value = adapter.GetManualOutput(index);
                GUI.Label(new Rect(x + 18f, y + 5f, 245f, 24f),
                    adapter.GetManualOutputLabel(index), labelStyle);
                if (GUI.Button(new Rect(x + width - 142f, y, 122f, 29f), value ? "ON" : "OFF"))
                {
                    adapter.SetManualOutput(index, !value);
                }
            }

            GUI.Label(new Rect(x + 16f, 338f, width - 32f, 25f), "LIVE INPUTS", statusStyle);
            for (int index = 0; index < adapter.InputCount; index++)
            {
                int column = index / 7;
                int row = index % 7;
                float itemX = x + 18f + column * 208f;
                float itemY = 367f + row * 28f;
                DrawIndicator(itemX, itemY, adapter.GetInputLabel(index), adapter.GetInputState(index));
            }

            GUI.Label(new Rect(x + 16f, 574f, width - 32f, 22f),
                "Tab: collapse    F2: hide HMI    F1: engineering diagnostics", smallStyle);
        }

        private void DrawIndicator(float x, float y, string label, bool value)
        {
            Color previousColor = GUI.color;
            GUI.color = value ? new Color(0.18f, 1f, 0.45f) : new Color(0.25f, 0.29f, 0.34f);
            GUI.DrawTexture(new Rect(x, y + 4f, 13f, 13f), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 20f, y, 185f, 22f), label, smallStyle);
        }

        private void InitializeStyles()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 0.85f, 1f) }
            };
            statusStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.88f, 0.93f, 0.98f) }
            };
            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.78f, 0.84f, 0.9f) }
            };
            smallStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.68f, 0.75f, 0.82f) }
            };
        }
    }
}
