using System;
using System.IO;
using Kit1DigitalTwin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PresentationVideoCapture
{
    private static readonly bool CaptureRequested = HasArgument("-presentationCapture");
    private static bool finishing;

    static PresentationVideoCapture()
    {
        if (CaptureRequested)
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
    }

    public static void Run()
    {
        string scenePath = GetArgument("-captureScene");
        string outputPath = GetArgument("-captureOut");

        if (string.IsNullOrWhiteSpace(scenePath) || string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("-captureScene and -captureOut are required.");
        }

        Directory.CreateDirectory(outputPath);
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    internal static void Finish()
    {
        if (finishing)
        {
            return;
        }

        finishing = true;
        EditorApplication.ExitPlaymode();
    }

    internal static string GetArgument(string name, string fallback = "")
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static bool HasArgument(string name)
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            new GameObject("Presentation Video Capture").AddComponent<PresentationCaptureDriver>();
        }
        else if (state == PlayModeStateChange.EnteredEditMode && finishing)
        {
            EditorApplication.Exit(0);
        }
    }
}

public sealed class PresentationCaptureDriver : MonoBehaviour
{
    private const float LeadInSeconds = 1f;
    private Camera captureCamera;
    private RenderTexture renderTexture;
    private Texture2D readbackTexture;
    private string outputPath;
    private int kitNumber;
    private int fps;
    private int width;
    private int height;
    private int frameIndex;
    private int maximumFrames;
    private bool sequenceStarted;
    private bool cameraFramed;
    private Vector3 framedCameraPosition;
    private Quaternion framedCameraRotation;

    private void Awake()
    {
        outputPath = PresentationVideoCapture.GetArgument("-captureOut");
        kitNumber = ParseInt("-captureKit", 1);
        fps = ParseInt("-captureFps", 24);
        width = ParseInt("-captureWidth", 1280);
        height = ParseInt("-captureHeight", 720);
        float duration = ParseFloat("-captureSeconds", 24f);

        maximumFrames = Mathf.CeilToInt(duration * fps);
        Time.captureFramerate = fps;
        Application.runInBackground = true;

        renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1,
            name = "Presentation Capture Target"
        };
        renderTexture.Create();
        readbackTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    private void LateUpdate()
    {
        captureCamera ??= Camera.main;
        if (captureCamera == null)
        {
            if (frameIndex > fps * 5)
            {
                Debug.LogError("Presentation capture failed: no Main Camera was found.");
                PresentationVideoCapture.Finish();
            }

            frameIndex++;
            return;
        }

        if (!cameraFramed)
        {
            FrameMachine();
            cameraFramed = true;
        }

        if (!sequenceStarted && frameIndex >= Mathf.RoundToInt(LeadInSeconds * fps))
        {
            StartAutomaticSequence();
            sequenceStarted = true;
        }

        CaptureFrame();
        frameIndex++;

        if (frameIndex >= maximumFrames)
        {
            Debug.Log($"Presentation capture completed: {frameIndex} frames written to {outputPath}");
            PresentationVideoCapture.Finish();
        }
    }

    private void StartAutomaticSequence()
    {
        switch (kitNumber)
        {
            case 1:
                FindFirstObjectByType<Kit1AutomaticSequence>()?.RequestStart();
                break;
            case 2:
                FindFirstObjectByType<Kit2AutomaticSequence>()?.RequestStartContinuous();
                break;
            case 3:
                FindFirstObjectByType<Kit3AutomaticSequence>()?.RequestStartContinuous();
                break;
            default:
                Debug.LogError($"Unsupported presentation capture kit: {kitNumber}");
                PresentationVideoCapture.Finish();
                break;
        }
    }

    private void CaptureFrame()
    {
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = captureCamera.targetTexture;
        float previousAspect = captureCamera.aspect;

        captureCamera.transform.SetPositionAndRotation(framedCameraPosition, framedCameraRotation);
        captureCamera.aspect = width / (float)height;
        captureCamera.targetTexture = renderTexture;
        captureCamera.Render();
        RenderTexture.active = renderTexture;
        readbackTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        readbackTexture.Apply(false, false);

        byte[] bytes = readbackTexture.EncodeToJPG(90);
        string framePath = Path.Combine(outputPath, $"frame-{frameIndex:D5}.jpg");
        File.WriteAllBytes(framePath, bytes);

        captureCamera.targetTexture = previousTarget;
        captureCamera.aspect = previousAspect;
        RenderTexture.active = previousActive;
    }

    private void FrameMachine()
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        Bounds bounds = default;
        bool found = false;

        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || IsPresentationExcluded(renderer.transform))
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!found)
        {
            return;
        }

        captureCamera.fieldOfView = 32f;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
        float halfFovRadians = captureCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float distance = radius / Mathf.Sin(halfFovRadians) * 0.90f;
        Vector3 viewDirection = new Vector3(1.05f, 0.62f, -1.15f).normalized;
        Vector3 target = bounds.center + Vector3.up * bounds.extents.y * 0.02f;

        framedCameraPosition = target + viewDirection * distance;
        framedCameraRotation = Quaternion.LookRotation(target - framedCameraPosition, Vector3.up);
        captureCamera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2.5f);
        captureCamera.farClipPlane = distance + radius * 4f;
    }

    private static bool IsPresentationExcluded(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            string objectName = current.name;
            if (objectName.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Viewer UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("CameraTarget", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private int ParseInt(string name, int fallback)
    {
        return int.TryParse(PresentationVideoCapture.GetArgument(name), out int value)
            ? value
            : fallback;
    }

    private float ParseFloat(string name, float fallback)
    {
        return float.TryParse(
            PresentationVideoCapture.GetArgument(name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float value)
            ? value
            : fallback;
    }

    private void OnDestroy()
    {
        Time.captureFramerate = 0;
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (readbackTexture != null)
        {
            Destroy(readbackTexture);
        }
    }
}
