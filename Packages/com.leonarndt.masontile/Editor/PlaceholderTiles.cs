using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TileKit.Editor
{
    /// <summary>
    /// Generates flat-colour placeholder tiles plus a matching legend, so level layout can start
    /// before any art exists. Also sets up a demo tilemap in the open scene.
    /// </summary>
    public static class PlaceholderTiles
    {
        const string k_Folder = "Assets/TileKit/Placeholders";
        const int k_Size = 16;

        static readonly (char symbol, string name, Color fill, Color edge)[] k_Defaults =
        {
            ('#', "wall", new Color(0.25f, 0.25f, 0.3f), new Color(0.15f, 0.15f, 0.18f)),
            ('f', "floor", new Color(0.76f, 0.66f, 0.48f), new Color(0.68f, 0.58f, 0.42f)),
            ('~', "water", new Color(0.2f, 0.45f, 0.85f), new Color(0.15f, 0.35f, 0.7f)),
            ('g', "grass", new Color(0.35f, 0.7f, 0.3f), new Color(0.28f, 0.58f, 0.24f)),
            ('*', "spikes", new Color(0.85f, 0.25f, 0.25f), new Color(0.6f, 0.1f, 0.1f)),
            ('c', "coin", new Color(1f, 0.85f, 0.2f), new Color(0.85f, 0.65f, 0.1f)),
        };

        const string k_DemoLevel =
            "##################\n" +
            "#gggg.....~~~~...#\n" +
            "#gggg.....~~~~.c.#\n" +
            "#.....ffff.......#\n" +
            "#.....ffff..###..#\n" +
            "#..c..........*..#\n" +
            "##################";

        [MenuItem("Tools/TileKit/Create Placeholder Tiles + Legend")]
        public static TileLegend CreatePlaceholders()
        {
            Directory.CreateDirectory(k_Folder);
            var legendPath = $"{k_Folder}/PlaceholderLegend.asset";
            var legend = AssetDatabase.LoadAssetAtPath<TileLegend>(legendPath);
            if (legend == null)
            {
                legend = ScriptableObject.CreateInstance<TileLegend>();
                AssetDatabase.CreateAsset(legend, legendPath);
            }

            foreach (var (symbol, tileName, fill, edge) in k_Defaults)
                legend.Set(symbol, tileName, CreateTile(tileName, fill, edge));

            EditorUtility.SetDirty(legend);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TileKit] Placeholder tiles ready in {k_Folder}. {legend.Describe()}", legend);
            return legend;
        }

        [MenuItem("Tools/TileKit/Create Demo Tilemap In Scene")]
        public static void CreateDemoTilemap()
        {
            var legend = CreatePlaceholders();

            var grid = Object.FindAnyObjectByType<Grid>();
            if (grid == null)
            {
                grid = new GameObject("Grid", typeof(Grid)).GetComponent<Grid>();
                Undo.RegisterCreatedObjectUndo(grid.gameObject, "Create Grid");
            }

            var go = new GameObject("Ground", typeof(Tilemap), typeof(TilemapRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Create Demo Tilemap");
            go.transform.SetParent(grid.transform, false);
            go.AddComponent<TileLegendBinding>().legend = legend;

            var result = TilemapCommands.WriteText(go.GetComponent<Tilemap>(), legend, Vector2Int.zero, k_DemoLevel);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"[TileKit] {result.Message}", go);
        }

        static Tile CreateTile(string tileName, Color fill, Color edge)
        {
            var pngPath = $"{k_Folder}/{tileName}.png";
            if (!File.Exists(pngPath))
            {
                var tex = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);
                for (var y = 0; y < k_Size; y++)
                for (var x = 0; x < k_Size; x++)
                {
                    var border = x == 0 || y == 0 || x == k_Size - 1 || y == k_Size - 1;
                    tex.SetPixel(x, y, border ? edge : fill);
                }
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);

                var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = k_Size;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var tilePath = $"{k_Folder}/{tileName}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            EditorUtility.SetDirty(tile);
            return tile;
        }
    }
}
