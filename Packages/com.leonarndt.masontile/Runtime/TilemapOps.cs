using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TileKit
{
    /// <summary>
    /// Stateless tilemap operations on layer z = 0. A null tile erases.
    /// Works in Edit and Play mode; undo is the caller's job (see TileKit.Editor.TilemapCommands).
    /// </summary>
    public static class TilemapOps
    {
        public static TileEdit Fill(Tilemap tilemap, RectInt rect, TileBase tile)
        {
            var batch = new CellBatch(tilemap);
            for (var y = rect.yMin; y < rect.yMax; y++)
            for (var x = rect.xMin; x < rect.xMax; x++)
                batch.Set(new Vector2Int(x, y), tile);
            return batch.Apply();
        }

        public static TileEdit Outline(Tilemap tilemap, RectInt rect, TileBase tile)
        {
            var batch = new CellBatch(tilemap);
            if (rect.width <= 0 || rect.height <= 0) return TileEdit.None;
            for (var x = rect.xMin; x < rect.xMax; x++)
            {
                batch.Set(new Vector2Int(x, rect.yMin), tile);
                batch.Set(new Vector2Int(x, rect.yMax - 1), tile);
            }
            for (var y = rect.yMin; y < rect.yMax; y++)
            {
                batch.Set(new Vector2Int(rect.xMin, y), tile);
                batch.Set(new Vector2Int(rect.xMax - 1, y), tile);
            }
            return batch.Apply();
        }

        /// <summary>Bresenham line, both endpoints inclusive.</summary>
        public static TileEdit Line(Tilemap tilemap, Vector2Int from, Vector2Int to, TileBase tile)
        {
            var batch = new CellBatch(tilemap);
            foreach (var cell in LineCells(from, to))
                batch.Set(cell, tile);
            return batch.Apply();
        }

        public static IEnumerable<Vector2Int> LineCells(Vector2Int from, Vector2Int to)
        {
            int x = from.x, y = from.y;
            int dx = Mathf.Abs(to.x - x), dy = -Mathf.Abs(to.y - y);
            int sx = x < to.x ? 1 : -1, sy = y < to.y ? 1 : -1;
            var err = dx + dy;
            while (true)
            {
                yield return new Vector2Int(x, y);
                if (x == to.x && y == to.y) yield break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        /// <summary>
        /// 4-connected flood fill of the region that shares the start cell's tile (including empty),
        /// clamped to <paramref name="limit"/>. <paramref name="touchedLimit"/> is true when the region
        /// reached the edge of the limit, which usually means it was not enclosed.
        /// </summary>
        public static TileEdit FloodFill(Tilemap tilemap, Vector2Int start, TileBase tile, RectInt limit, out bool touchedLimit)
        {
            touchedLimit = false;
            if (!limit.Contains(start)) return TileEdit.None;

            var target = tilemap.GetTile(new Vector3Int(start.x, start.y, 0));
            if (target == tile) return TileEdit.None;

            var batch = new CellBatch(tilemap);
            var visited = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                batch.Set(c, tile);
                if (c.x == limit.xMin || c.y == limit.yMin || c.x == limit.xMax - 1 || c.y == limit.yMax - 1)
                    touchedLimit = true;

                Visit(c + Vector2Int.right);
                Visit(c + Vector2Int.left);
                Visit(c + Vector2Int.up);
                Visit(c + Vector2Int.down);
            }
            return batch.Apply();

            void Visit(Vector2Int n)
            {
                if (!limit.Contains(n) || !visited.Add(n)) return;
                if (tilemap.GetTile(new Vector3Int(n.x, n.y, 0)) == target) queue.Enqueue(n);
            }
        }

        public static TileEdit Replace(Tilemap tilemap, RectInt area, TileBase from, TileBase to)
        {
            var batch = new CellBatch(tilemap);
            for (var y = area.yMin; y < area.yMax; y++)
            for (var x = area.xMin; x < area.xMax; x++)
            {
                if (tilemap.GetTile(new Vector3Int(x, y, 0)) == from)
                    batch.Set(new Vector2Int(x, y), to);
            }
            return batch.Apply();
        }

        /// <summary>Tight bounds of non-empty cells on layer 0. Zero-size if the tilemap is empty.</summary>
        public static RectInt UsedBounds(Tilemap tilemap)
        {
            var b = tilemap.cellBounds;
            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            for (var y = b.yMin; y < b.yMax; y++)
            for (var x = b.xMin; x < b.xMax; x++)
            {
                if (!tilemap.HasTile(new Vector3Int(x, y, 0))) continue;
                if (x < xMin) xMin = x;
                if (y < yMin) yMin = y;
                if (x > xMax) xMax = x;
                if (y > yMax) yMax = y;
            }
            return xMin == int.MaxValue ? default : new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        }

        /// <summary>Counts of each tile within <paramref name="area"/>. Empty cells are not counted.</summary>
        public static Dictionary<TileBase, int> CountTiles(Tilemap tilemap, RectInt area)
        {
            var counts = new Dictionary<TileBase, int>();
            for (var y = area.yMin; y < area.yMax; y++)
            for (var x = area.xMin; x < area.xMax; x++)
            {
                var t = tilemap.GetTile(new Vector3Int(x, y, 0));
                if (t == null) continue;
                counts.TryGetValue(t, out var n);
                counts[t] = n + 1;
            }
            return counts;
        }
    }
}
