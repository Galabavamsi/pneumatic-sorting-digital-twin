using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class ComponentIdentifier : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color HighlightColor = new Color(1f, 0.38f, 0.04f, 1f);

        private MaterialPropertyBlock highlightBlock;
        private List<MeshRenderer> components = new List<MeshRenderer>();
        private Camera viewerCamera;
        private OrbitCamera orbitCamera;
        private Transform originalCameraTarget;
        private Transform focusTarget;
        private MeshRenderer selected;
        private int selectedIndex = -1;
        private bool isolated;
        private GUIStyle titleStyle;
        private GUIStyle detailStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!KitSceneContext.HasViewerKit || FindFirstObjectByType<ComponentIdentifier>() != null)
            {
                return;
            }

            new GameObject("Component Identifier").AddComponent<ComponentIdentifier>();
        }

        private void Start()
        {
            highlightBlock = new MaterialPropertyBlock();

            KitComponentRegistry registry = FindFirstObjectByType<KitComponentRegistry>();
            if (registry == null)
            {
                enabled = false;
                return;
            }

            components = registry.GetComponentsInChildren<MeshRenderer>(true)
                .OrderBy(rendererComponent => rendererComponent.name)
                .ToList();

            viewerCamera = Camera.main;
            orbitCamera = viewerCamera != null ? viewerCamera.GetComponent<OrbitCamera>() : null;
            originalCameraTarget = orbitCamera != null ? orbitCamera.Target : null;

            GameObject focusObject = new GameObject("Component Focus Target");
            focusTarget = focusObject.transform;
            highlightBlock.SetColor(BaseColorId, HighlightColor);
        }

        private void Update()
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 pointerPosition = Mouse.current.position.ReadValue();
                if (!Kit1DigitalTwin.Hmi.Kit1EngineeringDashboard.ContainsScreenPoint(pointerPosition))
                {
                    SelectFromPointer(pointerPosition);
                }
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.leftBracketKey.wasPressedThisFrame ||
                Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                SelectByIndex(selectedIndex <= 0 ? components.Count - 1 : selectedIndex - 1);
            }

            if (Keyboard.current.rightBracketKey.wasPressedThisFrame ||
                Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                SelectByIndex((selectedIndex + 1) % components.Count);
            }

            if (Keyboard.current.hKey.wasPressedThisFrame && selected != null)
            {
                SetIsolation(!isolated);
            }

            if (Keyboard.current.fKey.wasPressedThisFrame && selected != null)
            {
                FocusSelected();
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ClearSelection();
            }
        }

        private void SelectFromPointer(Vector2 screenPosition)
        {
            if (viewerCamera == null)
            {
                return;
            }

            Ray ray = viewerCamera.ScreenPointToRay(screenPosition);
            MeshRenderer closest = null;
            float closestDistance = float.PositiveInfinity;

            foreach (MeshRenderer component in components)
            {
                if (!component.enabled || !component.bounds.IntersectRay(ray, out float distance))
                {
                    continue;
                }

                if (distance < closestDistance)
                {
                    closest = component;
                    closestDistance = distance;
                }
            }

            if (closest != null)
            {
                SelectByIndex(components.IndexOf(closest));
            }
        }

        private void SelectByIndex(int index)
        {
            if (components.Count == 0 || index < 0 || index >= components.Count)
            {
                return;
            }

            if (selected != null)
            {
                selected.SetPropertyBlock(null);
            }

            selectedIndex = index;
            selected = components[index];
            selected.SetPropertyBlock(highlightBlock);
            KitComponentRegistry registry = FindFirstObjectByType<KitComponentRegistry>();
            string kitId = registry != null ? registry.KitId.ToUpperInvariant() : "KIT";
            Debug.Log($"{kitId} SELECTED COMPONENT: {selected.name}", selected.gameObject);

            if (isolated)
            {
                SetIsolation(true);
            }
        }

        private void SetIsolation(bool enableIsolation)
        {
            isolated = enableIsolation;
            foreach (MeshRenderer component in components)
            {
                component.enabled = !isolated || component == selected;
            }
        }

        private void FocusSelected()
        {
            if (orbitCamera == null || selected == null)
            {
                return;
            }

            focusTarget.position = selected.bounds.center;
            orbitCamera.Target = focusTarget;
        }

        private void ClearSelection()
        {
            if (selected != null)
            {
                selected.SetPropertyBlock(null);
            }

            SetIsolation(false);
            selected = null;
            selectedIndex = -1;
            if (orbitCamera != null)
            {
                orbitCamera.Target = originalCameraTarget;
            }
        }

        private void OnGUI()
        {
            if (!Kit1DebugOverlay.Visible || !Kit1DebugOverlay.LegacyPanelsVisible)
            {
                return;
            }

            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                normal = { textColor = HighlightColor }
            };
            detailStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                normal = { textColor = Color.white }
            };

            Rect panelRect = new Rect(18f, 118f, 470f, 142f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 0.96f);
            GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
            GUI.color = previousColor;

            string title = selected == null
                ? "COMPONENT IDENTIFIER"
                : $"SELECTED: {selected.name}";
            string details = selected == null
                ? "Click a machine part to identify it.\n[ / ] or arrows: cycle components"
                : $"F: focus    H: isolate ({(isolated ? "ON" : "OFF")})\nEsc: clear    |    {selectedIndex + 1} of {components.Count}";

            GUI.Label(new Rect(34f, 130f, 440f, 38f), title, titleStyle);
            GUI.Label(new Rect(34f, 170f, 440f, 72f), details, detailStyle);
        }
    }
}
