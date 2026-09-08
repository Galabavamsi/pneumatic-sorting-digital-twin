using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kit1DigitalTwinEditor
{
    public static class Kit2ProjectValidator
    {
        private const string ScenePath = "Assets/Scenes/Kit2Viewer.unity";
        private const string PrefabPath = "Assets/Prefabs/Kit2_StampingStation.prefab";
        private const string ModelPath = "Assets/Models/Kit2/Kit2_components.obj";

        private static readonly string[] RequiredMechanismComponents =
        {
            "Component_018", "Component_029", "Component_030", "Component_031",
            "Component_032", "Component_033", "Component_034", "Component_035"
        };

        [MenuItem("Tools/Kit 2 Digital Twin/Validate Project")]
        public static void ValidateProject()
        {
            List<string> failures = new List<string>();
            ValidateAsset<GameObject>(ModelPath, "segmented OBJ model", failures);
            ValidateAsset<GameObject>(PrefabPath, "stamping-station prefab", failures);
            ValidateAsset<SceneAsset>(ScenePath, "viewer scene", failures);
            ValidateAsset<TextAsset>("Assets/Settings/Kit2_Component_Map.csv", "component map", failures);
            ValidateAsset<TextAsset>("Assets/Settings/Kit2_PLC_IO_Map.csv", "logical PLC tag map", failures);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
            {
                HashSet<string> names = prefab.GetComponentsInChildren<Transform>(true)
                    .Select(item => item.name)
                    .ToHashSet();

                foreach (string componentName in RequiredMechanismComponents)
                {
                    if (!names.Contains(componentName))
                    {
                        failures.Add($"Prefab is missing required mechanism component {componentName}.");
                    }
                }

                for (int index = 36; index <= 51; index++)
                {
                    string workpieceName = $"Component_{index:000}";
                    if (!names.Contains(workpieceName))
                    {
                        failures.Add($"Prefab is missing magazine workpiece {workpieceName}.");
                    }
                }

                int meshCount = prefab.GetComponentsInChildren<MeshFilter>(true).Length;
                if (meshCount < 66)
                {
                    failures.Add($"Expected the 66-component segmented station; found only {meshCount} mesh objects.");
                }
            }

            bool sceneEnabled = EditorBuildSettings.scenes.Any(
                scene => scene.enabled && scene.path == ScenePath);
            if (!sceneEnabled)
            {
                failures.Add("Kit2Viewer is not enabled in Build Settings.");
            }

            if (failures.Count == 0)
            {
                Debug.Log("KIT2_VALIDATION_PASS: model, prefab, mechanisms, magazine, PLC map, scene and build settings are ready.");
                EditorUtility.DisplayDialog(
                    "Kit 2 validation passed",
                    "All required project assets and build settings were found.\n\n" +
                    "Run the play-mode checklist in Documentation/KIT2_VALIDATION.md before a release.",
                    "OK");
                return;
            }

            string report = string.Join("\n• ", failures);
            Debug.LogError($"KIT2_VALIDATION_FAILED:\n• {report}");
            EditorUtility.DisplayDialog("Kit 2 validation failed", $"• {report}", "OK");
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
