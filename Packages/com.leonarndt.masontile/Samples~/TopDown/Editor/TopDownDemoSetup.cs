using System.IO;
using System.Linq;
using MasonTile.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Samples.TopDown.Editor
{
    /// <summary>
    /// Builds the Top-Down Demo after the sample is imported: draws the village tile art, creates the
    /// tiles and legend, paints Levels/TopDownVillage.txt and assembles the scene. Re-run it with
    /// Tools > MasonTile > Demos > Rebuild Top-Down Demo after editing the map text.
    /// </summary>
    [InitializeOnLoad]
    static class TopDownDemoSetup
    {
        const string k_Map = "TopDownVillage";
        const string k_SceneName = "Top-Down Demo.unity";

        static readonly string[] k_SignTexts =
        {
            "Welcome to Mason Town!\nThis whole village is one text file. Open Window > MasonTile > Tilemap Text View to see it.",
            "The community garden.\nTry asking the AI Assistant: \"Replace the flowers in the garden with water.\"",
            "You found the quiet corner by the pond.\nEdit TopDownVillage.txt, then use Tools > MasonTile > Demos > Rebuild Top-Down Demo.",
        };

        static TopDownDemoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                var root = FindRoot();
                if (root != null && !File.Exists($"{root}/{k_SceneName}")) Build(root);
            };
        }

        [MenuItem("Tools/MasonTile/Demos/Rebuild Top-Down Demo", priority = 201)]
        static void Rebuild()
        {
            var root = FindRoot();
            if (root == null) Debug.LogError("[MasonTile] Could not find the Top-Down Demo sample folder.");
            else Build(root);
        }

        static string FindRoot()
        {
            var map = AssetDatabase.FindAssets($"{k_Map} t:TextAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == k_Map);
            return map == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(map))?.Replace('\\', '/');
        }

        static void Build(string root)
        {
            var legend = BuildTiles($"{root}/Tiles");
            var facing = new[] { "down", "up", "left", "right" }
                .Select(dir => PlayerArt(dir).SaveSprite($"{root}/Tiles/Art/player_{dir}.png")).ToArray();
            var map = AssetDatabase.LoadAssetAtPath<TextAsset>($"{root}/Levels/{k_Map}.txt");

            var scenePath = $"{root}/{k_SceneName}";
            var legendPath = AssetDatabase.GetAssetPath(legend);
            var built = SceneBuilder.Build(scenePath, () =>
            {
                // Replacing an untitled scene unloads unused assets, so reload the legend (and its tiles) from disk.
                legend = AssetDatabase.LoadAssetAtPath<TileLegend>(legendPath);
                var grid = new GameObject("Grid", typeof(Grid));
                var levelGo = new GameObject("Village", typeof(Tilemap), typeof(TilemapRenderer));
                levelGo.transform.SetParent(grid.transform);
                levelGo.AddComponent<TileLegendBinding>().legend = legend;
                var tilemap = levelGo.GetComponent<Tilemap>();
                AsciiMap.Write(tilemap, Vector2Int.zero, map.text, legend);

                var playerGo = new GameObject("Player", typeof(SpriteRenderer), typeof(TopDownPlayer));
                var sr = playerGo.GetComponent<SpriteRenderer>();
                sr.sprite = facing[0];
                sr.sortingOrder = 10;
                var player = playerGo.GetComponent<TopDownPlayer>();
                player.level = tilemap;
                player.facingSprites = facing;
                player.signTexts = k_SignTexts;
                legend.TryGetTile('S', out player.startTile);
                legend.TryGetTile('s', out player.signTile);
                legend.TryGetTile('D', out player.doorTile);
                foreach (var cell in tilemap.cellBounds.allPositionsWithin)
                    if (tilemap.GetTile(cell) == player.startTile) playerGo.transform.position = tilemap.GetCellCenterWorld(cell);

                var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CameraFollow2D)) { tag = "MainCamera" };
                var cam = cameraGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 5.5f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = PixelCanvas.Hex("#1f4d24");
                cameraGo.transform.position = playerGo.transform.position + new Vector3(0f, 0f, -10f);
                var follow = cameraGo.GetComponent<CameraFollow2D>();
                follow.target = playerGo.transform;
                follow.bounds = tilemap;
            });
            if (!built) return;

            AssetDatabase.SaveAssets();
            Debug.Log($"[MasonTile] Top-Down Demo ready: {scenePath}. The map is {root}/Levels/{k_Map}.txt.",
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
            DemoMenu.ConsumeOpenRequest(DemoMenu.TopDownSample, scenePath);
        }

        // ---------- Art ----------

        static Color C(string hex) => PixelCanvas.Hex(hex);

        const Tile.ColliderType Walk = Tile.ColliderType.None;
        const Tile.ColliderType Block = Tile.ColliderType.Grid;

        static TileLegend BuildTiles(string folder) => new TileSetBuilder(folder)
            .Add('g', "grass", Walk, c => Grass(c, 1))
            .Add(',', "tall_grass", Walk, c =>
            {
                Grass(c, 2);
                foreach (var (x, y) in new[] { (1, 2), (6, 1), (11, 3), (3, 9), (9, 8), (13, 10) })
                    c.Rect(x, y, 1, 4, C("#2f7d2a")).Rect(x + 1, y + 1, 1, 4, C("#3f9a35")).Rect(x + 2, y, 1, 3, C("#2f7d2a"));
            })
            .Add('_', "path", Walk, PathTile)
            .Add('S', "start", Walk, PathTile)
            .Add('f', "flowers", Walk, c =>
            {
                Grass(c, 3);
                foreach (var (x, y, col) in new[] { (2, 2, "#ef4444"), (9, 3, "#facc15"), (5, 9, "#f9fafb"), (12, 10, "#ef4444"), (1, 12, "#facc15"), (13, 5, "#f9fafb") })
                    c.Rect(x, y, 2, 2, C(col)).Set(x, y + 2, C("#2f7d2a"));
            })
            .Add('~', "water", Block, c =>
            {
                c.Fill(C("#3b82c4")).Speckle(C("#3576b3"), 0.15f, 4);
                foreach (var (x, y) in new[] { (2, 3), (9, 6), (4, 11), (11, 13) }) c.Rect(x, y, 4, 1, C("#8fd0f5"));
            })
            .Add('T', "tree", Block, c =>
            {
                Grass(c, 5);
                c.Circle(8, 2.5f, 4f, C("#3c8a33")).Rect(6, 0, 4, 6, C("#7a4b2a"))
                    .Circle(8, 9.5f, 6.5f, C("#1f5f24")).Circle(8, 10f, 5.5f, C("#2f7d32")).Circle(6.5f, 11.5f, 2.5f, C("#4caf50"));
            })
            .Add('r', "rock", Block, c =>
            {
                Grass(c, 6);
                c.Circle(8, 6.5f, 5.5f, C("#6b7280")).Circle(8, 7.5f, 5f, C("#9ca3af")).Circle(6.5f, 9f, 1.8f, C("#d1d5db"));
            })
            .Add('R', "roof", Block, c =>
            {
                c.Fill(C("#c2410c"));
                for (var row = 0; row < 4; row++)
                {
                    c.Rect(0, row * 4, 16, 1, C("#9a3412"));
                    for (var x = row % 2 == 0 ? 2 : 6; x < 16; x += 8) c.Rect(x, row * 4 + 1, 1, 3, C("#9a3412"));
                }
            })
            .Add('W', "wall", Block, Wall)
            .Add('w', "window", Block, c =>
            {
                Wall(c);
                c.Rect(4, 5, 8, 8, C("#6b4f2f")).Rect(5, 6, 6, 6, C("#93c5fd")).Rect(5, 9, 6, 1, C("#6b4f2f")).Rect(8, 6, 1, 6, C("#6b4f2f")).Set(6, 11, Color.white);
            })
            .Add('D', "door", Block, c =>
            {
                Wall(c);
                c.Rect(4, 0, 8, 13, C("#5b3a1e")).Rect(5, 0, 6, 12, C("#8b5a2b")).Rect(5, 6, 6, 1, C("#5b3a1e")).Set(9, 5, C("#facc15"));
            })
            .Add('B', "bridge", Walk, c =>
            {
                c.Fill(C("#a0703c"));
                for (var x = 3; x < 16; x += 4) c.Rect(x, 0, 1, 16, C("#6e4a26"));
                c.Rect(0, 0, 16, 2, C("#5a3a1e")).Rect(0, 14, 16, 2, C("#5a3a1e"));
            })
            .Add('s', "sign", Block, c =>
            {
                Grass(c, 7);
                c.Rect(7, 0, 2, 7, C("#6e4a26")).Rect(2, 6, 12, 8, C("#7a5230")).Rect(3, 7, 10, 6, C("#c08a4d"))
                    .Rect(4, 11, 8, 1, C("#5a3a1e")).Rect(4, 9, 6, 1, C("#5a3a1e"));
            })
            .Add('#', "fence", Block, c =>
            {
                Grass(c, 8);
                c.Rect(0, 5, 16, 2, C("#8b5e34")).Rect(0, 10, 16, 2, C("#8b5e34"))
                    .Rect(2, 2, 2, 12, C("#6e4a26")).Rect(12, 2, 2, 12, C("#6e4a26"));
            })
            .Build($"{folder}/TopDownLegend.asset");

        static PixelCanvas Grass(PixelCanvas c, int seed) => c.Fill(C("#6abe45"))
            .Speckle(C("#5aa83a"), 0.14f, seed).Speckle(C("#82d65c"), 0.06f, seed + 20);

        static void PathTile(PixelCanvas c) => c.Fill(C("#d9b77e")).Speckle(C("#c9a46a"), 0.14f, 9).Speckle(C("#e8cc9a"), 0.08f, 19);

        static void Wall(PixelCanvas c)
        {
            c.Fill(C("#d6b48a"));
            for (var x = 3; x < 16; x += 4) c.Rect(x, 0, 1, 16, C("#b08860"));
            c.Rect(0, 0, 16, 2, C("#8a6a48"));
        }

        static PixelCanvas PlayerArt(string facing)
        {
            var c = new PixelCanvas()
                .Rect(4, 0, 8, 1, new Color(0f, 0f, 0f, 0.25f))
                .Rect(5, 1, 2, 3, C("#374151")).Rect(9, 1, 2, 3, C("#374151"))
                .Rect(4, 4, 8, 5, C("#2563eb")).Rect(5, 8, 6, 1, C("#1d4ed8"))
                .Circle(8, 11.5f, 3.8f, C("#f5c39e"));
            switch (facing)
            {
                case "up":
                    c.Circle(8, 11.8f, 3.8f, C("#5b3a1e"));
                    break;
                case "left":
                    c.Rect(5, 13, 7, 3, C("#5b3a1e")).Rect(9, 10, 3, 3, C("#5b3a1e")).Set(6, 11, C("#111827"));
                    break;
                case "right":
                    c.Rect(4, 13, 7, 3, C("#5b3a1e")).Rect(4, 10, 3, 3, C("#5b3a1e")).Set(9, 11, C("#111827"));
                    break;
                default:
                    c.Rect(4, 13, 8, 3, C("#5b3a1e")).Set(6, 11, C("#111827")).Set(9, 11, C("#111827"));
                    break;
            }
            return c;
        }
    }
}
