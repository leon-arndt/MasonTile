using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile
{
    /// <summary>
    /// Cellular-automata caves. Deterministic per seed, so a generated level can be reproduced
    /// from (width, height, seed, fill, steps) alone.
    /// </summary>
    public static class CaveGenerator
    {
        /// <summary>Returns walls[x, y] (true = wall). The outer border is always wall.</summary>
        public static bool[,] Generate(int width, int height, int seed, float fillPercent = 0.47f, int smoothSteps = 5)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Size must be positive.");

            var rng = new System.Random(seed);
            var walls = new bool[width, height];
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                walls[x, y] = IsBorder(x, y, width, height) || rng.NextDouble() < fillPercent;

            for (var step = 0; step < smoothSteps; step++)
            {
                var next = new bool[width, height];
                for (var x = 0; x < width; x++)
                for (var y = 0; y < height; y++)
                {
                    if (IsBorder(x, y, width, height)) { next[x, y] = true; continue; }
                    var n = WallNeighbours(walls, x, y, width, height);
                    next[x, y] = n > 4 || (n == 4 && walls[x, y]);
                }
                walls = next;
            }
            return walls;
        }

        /// <summary>Paints walls[x, y] with its [0, 0] at <paramref name="origin"/>. A null floor erases.</summary>
        public static TileEdit Paint(Tilemap tilemap, Vector2Int origin, bool[,] walls, TileBase wall, TileBase floor)
        {
            var batch = new CellBatch(tilemap);
            for (var x = 0; x < walls.GetLength(0); x++)
            for (var y = 0; y < walls.GetLength(1); y++)
                batch.Set(new Vector2Int(origin.x + x, origin.y + y), walls[x, y] ? wall : floor);
            return batch.Apply();
        }

        static bool IsBorder(int x, int y, int w, int h) => x == 0 || y == 0 || x == w - 1 || y == h - 1;

        static int WallNeighbours(bool[,] walls, int cx, int cy, int w, int h)
        {
            var count = 0;
            for (var x = cx - 1; x <= cx + 1; x++)
            for (var y = cy - 1; y <= cy + 1; y++)
            {
                if (x == cx && y == cy) continue;
                if (x < 0 || y < 0 || x >= w || y >= h || walls[x, y]) count++;
            }
            return count;
        }
    }
}
