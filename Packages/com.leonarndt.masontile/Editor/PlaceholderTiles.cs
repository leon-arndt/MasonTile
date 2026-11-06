using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Editor
{
    /// <summary>
    /// Generates flat-colour placeholder tiles plus a matching legend, so level layout can start
    /// before any art exists. For playable examples see Tools > MasonTile > Demos.
    /// </summary>
    public static class PlaceholderTiles
    {
        const string k_Folder = "Assets/MasonTile/Placeholders";

        [MenuItem("Tools/MasonTile/Create Placeholder Tiles + Legend")]
        public static TileLegend CreatePlaceholders()
        {
            var legend = new TileSetBuilder(k_Folder, spriteSubfolder: "")
                .Add('#', "wall", Tile.ColliderType.Grid, Flat("#40404d", "#26262e"))
                .Add('f', "floor", Tile.ColliderType.None, Flat("#c2a87a", "#ad946b"))
                .Add('~', "water", Tile.ColliderType.Grid, Flat("#3373d9", "#2659b3"))
                .Add('g', "grass", Tile.ColliderType.None, Flat("#59b34d", "#47943d"))
                .Add('*', "spikes", Tile.ColliderType.None, Flat("#d94040", "#991a1a"))
                .Add('c', "coin", Tile.ColliderType.None, Flat("#ffd933", "#d9a61a"))
                .Build($"{k_Folder}/PlaceholderLegend.asset");

            Debug.Log($"[MasonTile] Placeholder tiles ready in {k_Folder}. {legend.Describe()}", legend);
            return legend;
        }

        static System.Action<PixelCanvas> Flat(string fill, string edge)
            => c => c.Fill(PixelCanvas.Hex(fill)).Border(PixelCanvas.Hex(edge));
    }
}
