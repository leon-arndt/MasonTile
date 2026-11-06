using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace MasonTile.Editor
{
    /// <summary>
    /// One-click access to the package samples: imports the sample through Package Manager if needed,
    /// then opens its scene and plays the level build animation. Each sample builds its own art, tiles
    /// and scene after import (see the sample's Editor/*DemoSetup.cs) and calls <see cref="Present"/> when done.
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
                    Present(sampleName, scene, open: true);
                    return;
                }
            }

            // The sample's setup script runs after the import's script reload, builds the scene, then opens it.
            SessionState.SetString(k_OpenRequestKey, sampleName);
            if (!sample.isImported) sample.Import(Sample.ImportOptions.HideImportWindow);
            else Debug.Log($"[MasonTile] '{sampleName}' is imported but has no scene yet; use its Rebuild menu item.");
        }

        /// <summary>
        /// Called by a sample's setup after it built its scene, and by this menu. Opens the scene if
        /// <paramref name="open"/> is set or the user asked for it via this menu, then, if the scene is the
        /// active one, plays the build animation.
        /// </summary>
        public static void Present(string sampleName, string scenePath, bool open)
        {
            var requested = SessionState.GetString(k_OpenRequestKey, "") == sampleName;
            if (requested) SessionState.EraseString(k_OpenRequestKey);

            var isActive = SceneManager.GetActiveScene().path == scenePath;
            if (!isActive && (open || requested)) isActive = OpenScene(scenePath);
            if (!isActive) return;

            if (sampleName == TopDownSample) PlayBuildAnimation(BuildOrder.Ripple, rippleCenterSymbol: 'S');
            else PlayBuildAnimation(BuildOrder.SweepRight);
        }

        /// <summary>Kept so samples imported from earlier package versions still compile.</summary>
        [System.Obsolete("Use Present(sampleName, scenePath, open: false).")]
        public static void ConsumeOpenRequest(string sampleName, string scenePath) => Present(sampleName, scenePath, open: false);

        /// <summary>
        /// Rebuilds the active scene's legend-bound tilemap tile by tile in the Scene view. The end state is
        /// identical, so the scene is saved afterwards unless it already had unsaved changes.
        /// </summary>
        public static void PlayBuildAnimation(BuildOrder order, char rippleCenterSymbol = '\0', float seconds = 2f)
        {
            var binding = Object.FindAnyObjectByType<TileLegendBinding>();
            if (binding == null) return;
            var tilemap = binding.GetComponent<Tilemap>();

            Vector3Int? center = null;
            if (rippleCenterSymbol != '\0' && binding.legend != null && binding.legend.TryGetTile(rippleCenterSymbol, out var centerTile))
            {
                foreach (var cell in tilemap.cellBounds.allPositionsWithin)
                    if (tilemap.GetTile(cell) == centerTile) { center = cell; break; }
            }

            FrameInSceneView(tilemap);
            var scene = tilemap.gameObject.scene;
            var wasDirty = scene.isDirty;
            TileBuildPreview.Play(TileBuildAnimation.Rebuild(tilemap, order, seconds, center), () =>
            {
                if (!wasDirty && scene.IsValid() && scene.isLoaded) EditorSceneManager.SaveScene(scene);
            });
        }

        static void FrameInSceneView(Tilemap tilemap)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            tilemap.CompressBounds();
            var b = tilemap.localBounds;
            view.in2DMode = true;
            view.Frame(new Bounds(tilemap.transform.TransformPoint(b.center), b.size + Vector3.one), false);
        }

        static string FindScene(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return null;
            var guid = AssetDatabase.FindAssets("t:Scene", new[] { folder }).FirstOrDefault();
            return guid == null ? null : AssetDatabase.GUIDToAssetPath(guid);
        }

        static bool OpenScene(string scenePath)
        {
            if (!File.Exists(scenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(scenePath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
            return true;
        }
    }
}
