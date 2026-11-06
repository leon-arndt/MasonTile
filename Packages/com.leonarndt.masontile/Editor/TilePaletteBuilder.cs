using System.IO;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Editor
{
    /// <summary>Creates or updates a Tile Palette asset holding every tile of a <see cref="TileLegend"/>.</summary>
    public static class TilePaletteBuilder
    {
        /// <summary>
        /// Creates <paramref name="folder"/>/<paramref name="name"/>.prefab as a rectangular Tile Palette if it
        /// does not exist, then lays out the legend's tiles in rows of <paramref name="columns"/> (legend order,
        /// left to right, top to bottom). Returns the palette prefab.
        /// </summary>
        public static GameObject CreateOrUpdate(TileLegend legend, string folder, string name, int columns = 8)
        {
            folder = folder.TrimEnd('/');
            var path = $"{folder}/{name}.prefab";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                GridPaletteUtility.CreateNewPalette(folder, name, GridLayout.CellLayout.Rectangle,
                    GridPalette.CellSizing.Automatic, Vector3.one, GridLayout.CellSwizzle.XYZ);
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var tilemap = root.GetComponentInChildren<Tilemap>();
                tilemap.ClearAllTiles();
                for (var i = 0; i < legend.Entries.Count; i++)
                    tilemap.SetTile(new Vector3Int(i % columns, -(i / columns), 0), legend.Entries[i].tile);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }
}
