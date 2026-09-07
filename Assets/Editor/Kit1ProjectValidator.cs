using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kit1DigitalTwinEditor
{
    public static class Kit1ProjectValidator
    {
        private const string ScenePath = "Assets/Scenes/Kit1Viewer.unity";
        private const string PrefabPath = "Assets/Prefabs/Kit1_SortingStation.prefab";
        private const string ModelPath = "Assets/Models/Kit1/Kit1_components.obj";

        private static readonly string[] RequiredMovingComponents =
        {
            "Component_025", "Component_026", "Component_027", "Component_028",
            "Component_029", "Component_052", "Component_055", "Component_056",
            "Component_063"
        };

        [MenuItem("Tools/Kit 1 Digital Twin/Validate Project")]
        public static void ValidateProject()
        {
            List<string> failures = new List<string>();
            ValidateAsset<GameObject>(ModelPath, "segmented OBJ model", failures);
            ValidateAsset<GameObject>(PrefabPath, "sorting-station prefab", failures);
            ValidateAsset<SceneAsset>(ScenePath, "viewer scene", failures);
            ValidateAsset<TextAsset>("Assets/Settings/Kit1_PLC_IO_Map.csv", "logical PLC tag map", failures);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
            {
                HashSet<string> names = prefab
                    .GetComponentsInChildren<Transform>(true)
                    .Select(item => item.name)
                    .ToHashSet();
                foreach (string componentName in RequiredMovingComponents)
                {
                    if (!names.Contains(componentName))
                    {
                        failures.Add($"Prefab is missing required moving component {componentName}.");
                    }
                }

                int meshCount = prefab.GetComponentsInChildren<MeshFilter>(true).Length;
                if (meshCount < 60)
                {
                    failures.Add($"Expected the segmented station mesh; found only {meshCount} mesh objects.");
                }
            }

            bool sceneEnabled = EditorBuildSettings.scenes.Any(
                scene => scene.enabled && scene.path == ScenePath);
            if (!sceneEnabled)
            {
                failures.Add("Kit1Viewer is not enabled in Build Settings.");
            }

            if (failures.Count == 0)
            {
                Debug.Log(
                    "KIT1_VALIDATION_PASS: model, prefab, moving components, PLC map, scene and build settings are ready.");
                EditorUtility.DisplayDialog(
                    "Kit 1 validation passed",
                    "All required project assets and build settings were found.\n\n" +
                    "Run the play-mode checklist in Documentation/VALIDATION.md before a release.",
                    "OK");
                return;
            }

            string report = string.Join("\n• ", failures);
            Debug.LogError($"KIT1_VALIDATION_FAILED:\n• {report}");
            EditorUtility.DisplayDialog("Kit 1 validation failed", $"• {report}", "OK");
        }

        private static void ValidateAsset<T>(string path, string label, ICollection<string> failures)
            where T : Object
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
            {
                failures.Add($"Missing {label}: {path}");
            }
        }
    }
}
