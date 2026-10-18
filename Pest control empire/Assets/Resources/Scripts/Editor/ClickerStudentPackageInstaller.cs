using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace COMP2165.Clicker.Editor
{
    [InitializeOnLoad]
    public sealed class ClickerStudentPackageInstaller : AssetPostprocessor
    {
        const string SeedAsset = "Assets/ClickerPackageSeed/StudentContentSeed.zip";
        const string SceneAsset = "Assets/Resources/Scenes/Clicker.unity";
        static bool installing;

        static ClickerStudentPackageInstaller()
        {
            InstallMissingFiles();
            EditorApplication.delayCall += InstallMissingFiles;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Contains(SeedAsset)) InstallMissingFiles();
        }

        [MenuItem("COMP2165/Install Student Content")]
        public static void InstallMissingFiles()
        {
            if (installing) return;
            string source = Path.Combine(Application.dataPath, "ClickerPackageSeed", "StudentContentSeed.zip");
            if (!File.Exists(source)) return;
            installing = true;
            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                string rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                int copied = 0;
                using (var archive = ZipFile.OpenRead(source))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string destination = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Package seed contains an invalid path: " + entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                        if (File.Exists(destination)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination);
                        copied++;
                    }
                }
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneAsset) != null &&
                    !EditorBuildSettings.scenes.Any(scene => scene.path == SceneAsset))
                {
                    var scenes = EditorBuildSettings.scenes.ToList();
                    scenes.Insert(0, new EditorBuildSettingsScene(SceneAsset, true));
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                if (copied > 0) Debug.Log("Installed " + copied + " Student Content files beside the Unity project. Open Student Content/START HERE.txt.");
            }
            catch (Exception exception) { Debug.LogError("Could not install Student Content: " + exception); }
            finally { installing = false; }
        }
    }
}
