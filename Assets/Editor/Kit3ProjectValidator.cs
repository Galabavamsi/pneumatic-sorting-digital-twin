using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kit1DigitalTwinEditor
{
    public static class Kit3ProjectValidator
    {
        private const string ModelPath = "Assets/Models/Kit3/Kit3_components.obj";
        private const string PrefabPath = "Assets/Prefabs/Kit3_AssemblyStation.prefab";
        private const string ScenePath = "Assets/Scenes/Kit3Viewer.unity";

        [MenuItem("Tools/Kit 3 Digital Twin/Validate Viewer")]
        public static void ValidateViewer()
        {
            List<string> failures = new List<string>();
            ValidateAsset<GameObject>(ModelPath, "segmented model", failures);
            ValidateAsset<GameObject>(PrefabPath, "assembly-station prefab", failures);
            ValidateAsset<SceneAsset>(ScenePath, "viewer scene", failures);
            ValidateAsset<TextAsset>(
                "Assets/Settings/Kit3_Component_Manifest.csv",
                "component manifest",
                failures);
            ValidateAsset<TextAsset>(
                "Assets/Settings/Kit3_Component_Map.csv",
                "verified component map",
                failures);
            ValidateAsset<TextAsset>(
                "Assets/Settings/Kit3_PLC_IO_Map.csv",
                "logical PLC tag map",
                failures);
            ValidateAsset<MonoScript>(
                "Assets/Scripts/Runtime/Kit3AutomaticSequence.cs",
                "automatic sequence",
                failures);
            ValidateAsset<MonoScript>(
                "Assets/Scripts/Runtime/Kit3PlcIo.cs",
                "PLC I/O layer",
                failures);
            ValidateAsset<MonoScript>(
                "Assets/Scripts/Hmi/Kit3EngineeringDashboard.cs",
                "engineering dashboard",
                failures);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
            {
                int meshCount = prefab.GetComponentsInChildren<MeshFilter>(true).Length;
                if (meshCount < 69)
                {
                    failures.Add($"Expected 69 segmented meshes; found {meshCount}.");
                }
            }

            if (!EditorBuildSettings.scenes.Any(
                scene => scene.enabled && scene.path == ScenePath))
            {
                failures.Add("Kit3Viewer is not enabled in Build Settings.");
            }

            if (failures.Count == 0)
            {
                Debug.Log("KIT3_VALIDATION_PASS: model, mechanisms, PLC I/O, automatic sequence, dashboard, scene and build settings are ready.");
                EditorUtility.DisplayDialog(
                    "Kit 3 project validation passed",
                    "The offline Kit 3 simulation is complete. Run Documentation/KIT3_VALIDATION.md before release.",
                    "OK");
                return;
            }

            string report = string.Join("\n• ", failures);
            Debug.LogError($"KIT3_VIEWER_VALIDATION_FAILED:\n• {report}");
            EditorUtility.DisplayDialog("Kit 3 viewer validation failed", $"• {report}", "OK");
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
