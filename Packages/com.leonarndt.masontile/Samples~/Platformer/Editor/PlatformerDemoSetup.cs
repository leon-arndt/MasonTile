using System.IO;
using System.Linq;
using MasonTile.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Samples.Platformer.Editor
{
    /// <summary>
    /// Builds the Platformer Demo after the sample is imported: draws the tile art, creates the tiles and
    /// legend, paints level 1 from Levels/PlatformerLevel1.txt and assembles the scene. Generating the
    /// content (instead of shipping scenes and textures) keeps the sample independent of the Unity
    /// version and render pipeline. Re-run it with Tools > MasonTile > Demos > Rebuild Platformer Demo.
    /// </summary>
    [InitializeOnLoad]
    static class PlatformerDemoSetup
    {
        const string k_FirstLevel = "PlatformerLevel1";
        const string k_SceneName = "Platformer Demo.unity";

        static PlatformerDemoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                var root = FindRoot();
                if (root != null && !File.Exists($"{root}/{k_SceneName}")) Build(root, fromMenu: false);
            };
        }

        [MenuItem("Tools/MasonTile/Demos/Rebuild Platformer Demo", priority = 200)]
        static void Rebuild()
        {
            var root = FindRoot();
            if (root == null) Debug.LogError("[MasonTile] Could not find the Platformer Demo sample folder.");
            else Build(root, fromMenu: true);
        }

        /// <summary>The sample's root folder, found via its first level file.</summary>
        static string FindRoot()
        {
            var level = AssetDatabase.FindAssets($"{k_FirstLevel} t:TextAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == k_FirstLevel);
            return level == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(level))?.Replace('\\', '/');
        }

        /// <param name="fromMenu">Rebuild menu: open the scene and play the build animation.</param>
        static void Build(string root, bool fromMenu)
        {
            var legend = BuildTiles($"{root}/Tiles");
            var playerSprite = PlayerArt().SaveSprite($"{root}/Tiles/Art/player.png");
            var noFriction = LoadOrCreate($"{root}/Tiles/NoFriction.physicsMaterial2D", () => new PhysicsMaterial2D("NoFriction") { friction = 0f });
            var levels = Directory.GetFiles($"{root}/Levels", "PlatformerLevel*.txt").OrderBy(p => p)
                .Select(p => AssetDatabase.LoadAssetAtPath<TextAsset>(p.Replace('\\', '/'))).ToArray();

            var scenePath = $"{root}/{k_SceneName}";
            var legendPath = AssetDatabase.GetAssetPath(legend);
            var built = SceneBuilder.Build(scenePath, () =>
            {
                // Replacing an untitled scene unloads unused assets, so reload the legend (and its tiles) from disk.
                legend = AssetDatabase.LoadAssetAtPath<TileLegend>(legendPath);
                // Level: solid tiles collide through a merged composite collider, so the player never snags on seams.
                var grid = new GameObject("Grid", typeof(Grid));
                var levelGo = new GameObject("Level", typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D), typeof(CompositeCollider2D));
                levelGo.transform.SetParent(grid.transform);
                levelGo.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                levelGo.GetComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
                levelGo.GetComponent<CompositeCollider2D>().geometryType = CompositeCollider2D.GeometryType.Polygons;
                levelGo.AddComponent<TileLegendBinding>().legend = legend;
                var tilemap = levelGo.GetComponent<Tilemap>();
                AsciiMap.Write(tilemap, Vector2Int.zero, levels[0].text, legend);

                var playerGo = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(PlatformerPlayer));
                var sr = playerGo.GetComponent<SpriteRenderer>();
                sr.sprite = playerSprite;
                sr.sortingOrder = 10;
                var body = playerGo.GetComponent<Rigidbody2D>();
                body.gravityScale = 4f;
                body.freezeRotation = true;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var capsule = playerGo.GetComponent<CapsuleCollider2D>();
                capsule.size = new Vector2(0.7f, 0.95f);
                capsule.sharedMaterial = noFriction;

                var player = playerGo.GetComponent<PlatformerPlayer>();
                player.level = tilemap;
                player.legend = legend;
                player.nextLevels = levels.Skip(1).ToArray();
                legend.TryGetTile('S', out player.startTile);
                legend.TryGetTile('c', out player.coinTile);
                legend.TryGetTile('*', out player.spikeTile);
                legend.TryGetTile('F', out player.flagTile);
                playerGo.transform.position = StartPosition(tilemap, player.startTile);

                var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CameraFollow2D)) { tag = "MainCamera" };
                var cam = cameraGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 6f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = PixelCanvas.Hex("#7ec8f0");
                cameraGo.transform.position = playerGo.transform.position + new Vector3(0f, 0f, -10f);
                var follow = cameraGo.GetComponent<CameraFollow2D>();
                follow.target = playerGo.transform;
                follow.bounds = tilemap;
            });
            if (!built) return;

            AssetDatabase.SaveAssets();
            Debug.Log($"[MasonTile] Platformer Demo ready: {scenePath}. Levels are the .txt files in {root}/Levels.",
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
            DemoMenu.Present(DemoMenu.PlatformerSample, scenePath, open: fromMenu);
        }

        static Vector3 StartPosition(Tilemap tilemap, TileBase startTile)
        {
            foreach (var cell in tilemap.cellBounds.allPositionsWithin)
                if (tilemap.GetTile(cell) == startTile) return tilemap.GetCellCenterWorld(cell);
            return Vector3.zero;
        }

        static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---------- Art ----------

        static Color C(string hex) => PixelCanvas.Hex(hex);

        static TileLegend BuildTiles(string folder) => new TileSetBuilder(folder)
            .Add('G', "grass", Tile.ColliderType.Grid, c => Dirt(c, 1)
                .Rect(0, 11, 16, 5, C("#5cb83a")).Speckle(C("#4aa02d"), 0.15f, 2, 0, 11, 16, 4)
                .Rect(0, 15, 16, 1, C("#86dc5c"))
                .Set(1, 10, C("#5cb83a")).Set(4, 10, C("#5cb83a")).Set(5, 10, C("#5cb83a")).Set(9, 10, C("#5cb83a")).Set(13, 10, C("#5cb83a")))
            .Add('#', "dirt", Tile.ColliderType.Grid, c => Dirt(c, 3))
            .Add('B', "brick", Tile.ColliderType.Grid, Brick)
            .Add('*', "spikes", Tile.ColliderType.None, c =>
            {
                c.Rect(0, 0, 16, 2, C("#4b5563"));
                for (var i = 0; i < 4; i++)
                {
                    c.Triangle(new Vector2(i * 4, 2), new Vector2(i * 4 + 4, 2), new Vector2(i * 4 + 2, 10), C("#9ca3af"));
                    c.Triangle(new Vector2(i * 4 + 1, 2), new Vector2(i * 4 + 2.5f, 2), new Vector2(i * 4 + 2, 8.5f), C("#e5e7eb"));
                }
            })
            .Add('c', "coin", Tile.ColliderType.None, c => c
                .Circle(8, 8, 5.5f, C("#c98a00")).Circle(8, 8, 4.5f, C("#ffd23f"))
                .Rect(7, 5, 2, 6, C("#e0a800")).Set(6, 11, C("#fff4b8")).Set(5, 10, C("#fff4b8")))
            .Add('F', "flag", Tile.ColliderType.None, c => c
                .Rect(4, 0, 2, 15, C("#d1d5db")).Rect(5, 0, 1, 15, C("#9ca3af")).Circle(5, 15, 1.5f, C("#facc15"))
                .Triangle(new Vector2(6, 14), new Vector2(6, 8), new Vector2(15, 11), C("#ef4444")))
            .Add('S', "start", Tile.ColliderType.None, c => c
                .Rect(3, 0, 10, 11, C("#5b3a1e")).Circle(8, 10, 5f, C("#5b3a1e"))
                .Rect(4, 0, 8, 11, C("#8b5a2b")).Circle(8, 10, 4f, C("#8b5a2b"))
                .Rect(4, 0, 8, 1, C("#5b3a1e")).Set(10, 6, C("#facc15")))
            .Add('o', "cloud", Tile.ColliderType.None, c => c
                .Circle(4.5f, 6, 3.5f, C("#dbeafe")).Circle(10.5f, 6, 4.5f, C("#dbeafe")).Circle(8, 9, 4.5f, C("#dbeafe"))
                .Circle(4.5f, 7, 3.5f, Color.white).Circle(10.5f, 7, 4.5f, Color.white).Circle(8, 10, 4.5f, Color.white))
            .Build($"{folder}/PlatformerLegend.asset");

        static PixelCanvas Dirt(PixelCanvas c, int seed) => c.Fill(C("#8b5a2b"))
            .Speckle(C("#6e4521"), 0.12f, seed).Speckle(C("#a3703c"), 0.08f, seed + 10);

        static void Brick(PixelCanvas c)
        {
            c.Fill(C("#b5562f"));
            var mortar = C("#7a3b22");
            for (var row = 0; row < 4; row++)
            {
                c.Rect(0, row * 4, 16, 1, mortar);
                var offset = row % 2 == 0 ? 0 : 4;
                for (var x = offset; x < 16; x += 8) c.Rect(x, row * 4, 1, 4, mortar);
                c.Rect(0, row * 4 + 3, 16, 1, C("#c8693f"));
            }
        }

        static PixelCanvas PlayerArt() => new PixelCanvas()
            .Rect(4, 0, 3, 2, C("#1e3a8a")).Rect(9, 0, 3, 2, C("#1e3a8a"))
            .Rect(3, 2, 10, 9, C("#1d4ed8")).Rect(4, 11, 8, 2, C("#1d4ed8"))
            .Rect(4, 3, 8, 8, C("#3b82f6")).Rect(5, 11, 6, 1, C("#3b82f6"))
            .Rect(6, 7, 2, 3, Color.white).Rect(9, 7, 2, 3, Color.white)
            .Rect(7, 7, 1, 2, C("#111827")).Rect(10, 7, 1, 2, C("#111827"));
    }
}
