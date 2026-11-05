using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Editor
{
    /// <summary>Creates a <see cref="TileLegend"/> from selected tiles, folders, or Tile Palettes.</summary>
    public static class LegendBuilder
    {
        const string k_SymbolPool = "#=+*%@&$~^:;!|ox0123456789abcdefghijklmnpqrstuvwyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        [MenuItem("Assets/MasonTile/Create Legend From Selection", priority = 1000)]
        static void CreateFromSelection()
        {
            var tiles = CollectTiles(Selection.objects);
            if (tiles.Count == 0)
            {
                EditorUtility.DisplayDialog("MasonTile", "Select tiles, folders containing tiles, or a Tile Palette.", "OK");
                return;
            }

            var legend = Build(tiles);
            var first = AssetDatabase.GetAssetPath(Selection.objects[0]);
            var dir = AssetDatabase.IsValidFolder(first) ? first : Path.GetDirectoryName(first);
            var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(dir ?? "Assets", "TileLegend.asset").Replace('\\', '/'));
            AssetDatabase.CreateAsset(legend, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = legend;
            EditorGUIUtility.PingObject(legend);
            Debug.Log($"[MasonTile] Created {path} with {tiles.Count} tiles. {legend.Describe()}", legend);
        }

        [MenuItem("Assets/MasonTile/Create Legend From Selection", true)]
        static bool CanCreateFromSelection() => Selection.objects.Length > 0;

        /// <summary>
        /// Assigns each tile a symbol: the first letter of its name if free (lowercase, then uppercase),
        /// otherwise the next free symbol from a fixed pool.
        /// </summary>
        public static TileLegend Build(IEnumerable<TileBase> tiles)
        {
            var legend = ScriptableObject.CreateInstance<TileLegend>();
            var used = new HashSet<char>();
            foreach (var tile in tiles.Distinct())
            {
                var symbol = PickSymbol(tile.name, used);
                if (symbol == '\0')
                {
                    Debug.LogWarning($"[MasonTile] Ran out of symbols; '{tile.name}' was skipped.");
                    continue;
                }
                used.Add(symbol);
                legend.Set(symbol, tile.name, tile);
            }
            return legend;
        }

        static char PickSymbol(string name, HashSet<char> used)
        {
            var letter = name.FirstOrDefault(char.IsLetter);
            if (letter != '\0')
            {
                foreach (var c in new[] { char.ToLowerInvariant(letter), char.ToUpperInvariant(letter) })
                    if (!used.Contains(c) && !TileLegend.IsReserved(c)) return c;
            }
            return k_SymbolPool.FirstOrDefault(c => !used.Contains(c));
        }

        static List<TileBase> CollectTiles(Object[] selection)
        {
            var tiles = new List<TileBase>();
            foreach (var obj in selection)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                switch (obj)
                {
                    case TileBase tile:
                        tiles.Add(tile);
                        break;
                    case GameObject go:
                        // Tile Palettes are prefabs with a Grid and a Tilemap child.
                        foreach (var tm in go.GetComponentsInChildren<Tilemap>(true))
                        {
                            var all = new TileBase[tm.GetUsedTilesCount()];
                            tm.GetUsedTilesNonAlloc(all);
                            tiles.AddRange(all.Where(t => t != null).OrderBy(t => t.name));
                        }
                        break;
                    default:
                        if (AssetDatabase.IsValidFolder(path))
                        {
                            tiles.AddRange(AssetDatabase.FindAssets("t:TileBase", new[] { path })
                                .Select(g => AssetDatabase.LoadAssetAtPath<TileBase>(AssetDatabase.GUIDToAssetPath(g)))
                                .Where(t => t != null)
                                .OrderBy(t => t.name));
                        }
                        break;
                }
            }
            return tiles;
        }
    }
}
