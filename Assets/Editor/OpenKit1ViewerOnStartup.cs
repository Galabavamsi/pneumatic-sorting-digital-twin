using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kit1DigitalTwinEditor
{
    [InitializeOnLoad]
    public static class OpenKit1ViewerOnStartup
    {
        private const string SessionKey = "Kit1DigitalTwin.OpenedViewerScene";
        private const string ViewerScene = "Assets/Scenes/Kit1Viewer.unity";

        static OpenKit1ViewerOnStartup()
        {
            if (Application.isBatchMode || SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += OpenViewerScene;
        }

        private static void OpenViewerScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorSceneManager.OpenScene(ViewerScene, OpenSceneMode.Single);
        }
    }
}
