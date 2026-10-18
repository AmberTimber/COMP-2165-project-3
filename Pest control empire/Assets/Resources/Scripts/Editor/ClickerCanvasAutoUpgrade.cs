using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COMP2165.Clicker.Editor
{
    // Converts the existing Clicker scene once when this version of the project is opened.
    [InitializeOnLoad]
    internal static class ClickerCanvasAutoUpgrade
    {
        static ClickerCanvasAutoUpgrade()
        {
            EditorApplication.delayCall += UpgradeIfNeeded;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += UpgradeIfNeeded;
            };
        }

        static void UpgradeIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.path.EndsWith("/Clicker.unity")) return;
            if (GameObject.Find("Editable Clicker Canvas") == null) ClickerCanvasSceneBuilder.CreateEditableCanvas();
            ClickerCanvasSceneBuilder.FinishExistingCanvas();
        }
    }
}
