using System.IO;
using Kit1DigitalTwin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kit1DigitalTwinEditor
{
    public static class Kit1ProjectBootstrap
    {
        private const string ModelPath = "Assets/Models/Kit1/Kit1_components.obj";
        private const string ScenePath = "Assets/Scenes/Kit1Viewer.unity";
        private const string MaterialPath = "Assets/Materials/Kit1Metal.mat";
        private const string FloorMaterialPath = "Assets/Materials/Floor.mat";

        public static void Run()
        {
            ConfigureModelImporter();
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Prefabs");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Unity could not import the Kit 1 OBJ.", ModelPath);
            }

            GameObject stationRoot = new GameObject("Kit1_SortingStation");
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            modelInstance.name = "Components_63";
            modelInstance.transform.SetParent(stationRoot.transform, false);
            modelInstance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            Material kitMaterial = GetOrCreateMaterial(
                MaterialPath,
                new Color(0.48f, 0.62f, 0.72f),
                0.15f,
                0.42f);
            Renderer[] renderers = stationRoot.GetComponentsInChildren<Renderer>();
            foreach (Renderer rendererComponent in renderers)
            {
                rendererComponent.sharedMaterial = kitMaterial;
            }

            Bounds modelBounds = CalculateBounds(renderers);
            stationRoot.transform.position = new Vector3(
                -modelBounds.center.x,
                -modelBounds.min.y,
                -modelBounds.center.z);
            modelBounds = CalculateBounds(stationRoot.GetComponentsInChildren<Renderer>());

            stationRoot.AddComponent<Kit1ComponentRegistry>();
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                stationRoot,
                "Assets/Prefabs/Kit1_SortingStation.prefab",
                InteractionMode.AutomatedAction);

            CreateFloor(modelBounds);
            Transform cameraTarget = CreateCameraTarget(modelBounds);
            CreateCamera(modelBounds, cameraTarget);
            CreateLighting();
            CreateInstructions();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("KIT1_BOOTSTRAP_COMPLETE: viewer scene and 63-component prefab created.");
        }

        private static void ConfigureModelImporter()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("No ModelImporter was created for Kit 1.", ModelPath);
            }

            importer.globalScale = 0.001f;
            importer.useFileScale = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importAnimation = false;
            importer.meshCompression = ModelImporterMeshCompression.Low;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            importer.normalSmoothingAngle = 60f;
            importer.SaveAndReimport();
        }

        private static Bounds CalculateBounds(Renderer[] renderers)
        {
            if (renderers.Length == 0)
            {
                throw new MissingComponentException("Kit 1 has no renderable mesh components.");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void CreateFloor(Bounds modelBounds)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            float floorSize = Mathf.Max(modelBounds.size.x, modelBounds.size.z) * 2.4f;
            floor.transform.position = new Vector3(0f, -0.0125f, 0f);
            floor.transform.localScale = new Vector3(floorSize, 0.025f, floorSize);
            floor.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial(
                FloorMaterialPath,
                new Color(0.075f, 0.09f, 0.11f),
                0f,
                0.18f);
        }

        private static Transform CreateCameraTarget(Bounds modelBounds)
        {
            GameObject target = new GameObject("CameraTarget");
            target.transform.position = modelBounds.center;
            return target.transform;
        }

        private static void CreateCamera(Bounds modelBounds, Transform target)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera cameraComponent = cameraObject.AddComponent<Camera>();
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.025f, 0.032f, 0.045f);
            cameraComponent.nearClipPlane = 0.01f;
            cameraComponent.farClipPlane = 100f;

            float distance = Mathf.Max(modelBounds.size.x, modelBounds.size.y, modelBounds.size.z) * 2.15f;
            cameraObject.transform.position = target.position + new Vector3(distance, distance * 0.72f, -distance);
            cameraObject.transform.LookAt(target);

            OrbitCamera orbitCamera = cameraObject.AddComponent<OrbitCamera>();
            orbitCamera.Target = target;
        }

        private static void CreateLighting()
        {
            GameObject keyLightObject = new GameObject("Key Light");
            Light keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 2.2f;
            keyLight.shadows = LightShadows.Soft;
            keyLightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.37f, 0.42f);
        }

        private static void CreateInstructions()
        {
            GameObject canvasObject = new GameObject("Viewer UI");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject panelObject = new GameObject("Instructions");
            panelObject.transform.SetParent(canvasObject.transform, false);
            Image panel = panelObject.AddComponent<Image>();
            panel.color = new Color(0.02f, 0.025f, 0.035f, 0.88f);
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f);
            panelRect.sizeDelta = new Vector2(410f, 92f);

            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(panelObject.transform, false);
            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.text = "KIT 1 - SORTING STATION\nRight mouse: orbit   |   Wheel: zoom\nDesktop geometry viewer - simulation controls next";
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 8f);
            textRect.offsetMax = new Vector2(-12f, -8f);
        }

        private static Material GetOrCreateMaterial(
            string path,
            Color color,
            float metallic,
            float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
