using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MasonTile.Editor
{
    /// <summary>
    /// One-click access to the package samples: imports the sample through Package Manager if needed,
    /// then opens its scene. Each sample builds its own art, tiles and scene after import
    /// (see the sample's Editor/*DemoSetup.cs) and calls <see cref="ConsumeOpenRequest"/> when done.
    /// </summary>
    public static class DemoMenu
    {
        public const string PackageName = "com.leonarndt.masontile";
        public const string PlatformerSample = "Platformer Demo";
        public const string TopDownSample = "Top-Down Demo";

        const string k_OpenRequestKey = "MasonTile.OpenDemoAfterSetup";

        [MenuItem("Tools/MasonTile/Demos/Platformer Demo", priority = 100)]
        static void OpenPlatformer() => OpenOrImport(PlatformerSample);

        [MenuItem("Tools/MasonTile/Demos/Top-Down Demo", priority = 101)]
        static void OpenTopDown() => OpenOrImport(TopDownSample);

        public static void OpenOrImport(string sampleName)
        {
            var sample = Sample.FindByPackage(PackageName, null).FirstOrDefault(s => s.displayName == sampleName);
            if (string.IsNullOrEmpty(sample.displayName))
            {
                Debug.LogError($"[MasonTile] Sample '{sampleName}' not found in {PackageName}.");
                return;
            }

            if (sample.isImported)
            {
                var scene = FindScene(FileUtil.GetProjectRelativePath(sample.importPath.Replace('\\', '/')));
                if (scene != null)
                {
                    OpenScene(scene);
                    return;
                }
            }

            // The sample's setup script runs after the import's script reload, builds the scene, then opens it.
            SessionState.SetString(k_OpenRequestKey, sampleName);
            if (!sample.isImported) sample.Import(Sample.ImportOptions.HideImportWindow);
            else Debug.Log($"[MasonTile] '{sampleName}' is imported but has no scene yet; use its Rebuild menu item.");
        }

        /// <summary>Called by a sample's setup after it built its scene. Opens it if the user asked for it via this menu.</summary>
        public static void ConsumeOpenRequest(string sampleName, string scenePath)
        {
            if (SessionState.GetString(k_OpenRequestKey, "") != sampleName) return;
            SessionState.EraseString(k_OpenRequestKey);
            OpenScene(scenePath);
        }

        static string FindScene(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return null;
            var guid = AssetDatabase.FindAssets("t:Scene", new[] { folder }).FirstOrDefault();
            return guid == null ? null : AssetDatabase.GUIDToAssetPath(guid);
        }

        static void OpenScene(string scenePath)
        {
            if (!File.Exists(scenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(scenePath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
        }
    }
}
