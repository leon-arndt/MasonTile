using System.Collections.Generic;
using System.Linq;
using AgentBench;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Bench
{
    /// <summary>
    /// Builds and reads the bench scene for MasonTile tasks: a Grid with named Tilemaps that share one in-memory
    /// legend ('#' wall, 'f' floor, '~' water, 'g' grass, 'c' coin, '*' spikes). Tiles are flat colours so a
    /// run can be watched in the Scene view. Nothing is saved to the project.
    ///
    /// Cell maps are dictionaries from cell to legend symbol; empty cells are absent.
    /// </summary>
    static class TileWorld
    {
        public const string Ground = "Ground";
        public const string Decor = "Decor";

        /// <summary>Creates "Grid" with one Tilemap per name, each bound to a fresh legend. Later names draw on top.</summary>
        public static void Build(params string[] tilemaps)
        {
            var legend = CreateLegend();
            var grid = new GameObject("Grid", typeof(Grid));
            for (var i = 0; i < tilemaps.Length; i++)
            {
                var go = new GameObject(tilemaps[i], typeof(Tilemap), typeof(TilemapRenderer));
                go.transform.SetParent(grid.transform, false);
                go.GetComponent<TilemapRenderer>().sortingOrder = i;
                go.AddComponent<TileLegendBinding>().legend = legend;
            }
        }

        /// <summary>Writes text rows (first row on top) with the bottom-left character at (x, y).</summary>
        public static void Write(string tilemap, int x, int y, string rows)
        {
            var tm = Map(tilemap);
            var parsed = AsciiMap.Parse(rows, Legend(tm));
            if (!parsed.Success) throw new System.ArgumentException(string.Join(" ", parsed.Errors));
            AsciiMap.Write(tm, new Vector2Int(x, y), parsed, Legend(tm));
        }

        public static Tilemap Map(string name) =>
            Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include).FirstOrDefault(t => t.name == name)
            ?? throw new System.InvalidOperationException($"The bench scene has no Tilemap '{name}'.");

        public static TileLegend Legend(Tilemap tm) => tm.GetComponent<TileLegendBinding>().legend;

        /// <summary>Every non-empty cell of the tilemap, as legend symbols.</summary>
        public static Dictionary<Vector2Int, char> Cells(string tilemap)
        {
            var tm = Map(tilemap);
            var legend = Legend(tm);
            var cells = new Dictionary<Vector2Int, char>();
            var used = TilemapOps.UsedBounds(tm);
            foreach (var p in used.allPositionsWithin)
            {
                var tile = tm.GetTile(new Vector3Int(p.x, p.y, 0));
                if (tile != null) cells[p] = legend.GetSymbol(tile);
            }
            return cells;
        }

        /// <summary>Parses text rows into a cell map, bottom-left character at (x, y). '.' and ' ' are empty.</summary>
        public static Dictionary<Vector2Int, char> Layout(int x, int y, string rows, Dictionary<Vector2Int, char> into = null)
        {
            var cells = into ?? new Dictionary<Vector2Int, char>();
            var lines = rows.Replace("\r", "").Split('\n');
            for (var r = 0; r < lines.Length; r++)
            for (var c = 0; c < lines[r].Length; c++)
            {
                var symbol = lines[r][c];
                if (symbol == '.' || symbol == ' ') continue;
                cells[new Vector2Int(x + c, y + lines.Length - 1 - r)] = symbol;
            }
            return cells;
        }

        /// <summary>Text rows for a rectangle with a 1-cell border of <paramref name="edge"/> and <paramref name="fill"/> inside.</summary>
        public static string Room(int width, int height, char edge = '#', char fill = 'f')
        {
            var rows = new string[height];
            for (var r = 0; r < height; r++)
            {
                var border = r == 0 || r == height - 1;
                rows[r] = border ? new string(edge, width) : edge + new string(fill, width - 2) + edge;
            }
            return string.Join("\n", rows);
        }

        public static List<Vector2Int> RectCells(int x, int y, int width, int height)
        {
            var cells = new List<Vector2Int>(width * height);
            for (var cy = y; cy < y + height; cy++)
            for (var cx = x; cx < x + width; cx++)
                cells.Add(new Vector2Int(cx, cy));
            return cells;
        }

        /// <summary>
        /// Records one expectation: every listed cell holds the expected symbol ('.' for empty).
        /// The failure detail names the first few wrong cells, which is what you need when reading a transcript.
        /// </summary>
        public static Checks ExpectCells(this Checks checks, string expectation, Dictionary<Vector2Int, char> actual,
            IEnumerable<Vector2Int> cells, System.Func<Vector2Int, char> expected)
        {
            var wrong = cells
                .Select(p => (p, want: expected(p), got: actual.TryGetValue(p, out var s) ? s : '.'))
                .Where(x => x.want != x.got)
                .ToList();
            return checks.Expect(wrong.Count == 0, expectation, () =>
                $"{wrong.Count} cell(s) differ, e.g. " +
                string.Join(", ", wrong.Take(5).Select(w => $"({w.p.x},{w.p.y}) is '{w.got}' not '{w.want}'")));
        }

        /// <summary>Records one expectation: the tilemap holds exactly <paramref name="expected"/>, nothing more or less.</summary>
        public static Checks ExpectExactly(this Checks checks, string expectation, Dictionary<Vector2Int, char> actual,
            Dictionary<Vector2Int, char> expected)
        {
            var cells = actual.Keys.Union(expected.Keys);
            return checks.ExpectCells(expectation, actual, cells, p => expected.TryGetValue(p, out var s) ? s : '.');
        }

        /// <summary>Destroys the in-memory legend, tiles and sprite the scene's bindings point at.</summary>
        public static void Cleanup()
        {
            var legends = Object.FindObjectsByType<TileLegendBinding>(FindObjectsInactive.Include)
                .Select(b => b.legend).Where(l => l != null).Distinct().ToList();
            var tiles = legends.SelectMany(l => l.Entries).Select(e => e.tile).OfType<Tile>().Distinct().ToList();
            var sprites = tiles.Select(t => t.sprite).Where(s => s != null).Distinct().ToList();
            var textures = sprites.Select(s => s.texture).Where(t => t != null).Distinct().ToList();

            foreach (Object o in tiles.Cast<Object>().Concat(sprites).Concat(textures).Concat(legends))
                Object.DestroyImmediate(o);
        }

        static TileLegend CreateLegend()
        {
            var texture = new Texture2D(4, 4) { name = "BenchTile", filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            texture.Apply();

            var legend = ScriptableObject.CreateInstance<TileLegend>();
            legend.name = "BenchLegend";
            legend.hideFlags = HideFlags.DontSave;
            foreach (var (symbol, name, colour) in new[]
                     {
                         ('#', "wall", "#40404d"), ('f', "floor", "#c2a87a"), ('~', "water", "#3373d9"),
                         ('g', "grass", "#59b34d"), ('c', "coin", "#ffd933"), ('*', "spikes", "#d94040"),
                     })
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = name;
                tile.hideFlags = HideFlags.DontSave;
                tile.sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
                tile.sprite.hideFlags = HideFlags.DontSave;
                tile.color = ColorUtility.TryParseHtmlString(colour, out var c) ? c : Color.magenta;
                legend.Set(symbol, name, tile);
            }
            return legend;
        }
    }
}
