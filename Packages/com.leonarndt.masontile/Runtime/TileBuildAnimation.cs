using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile
{
    /// <summary>Order in which <see cref="TileBuildAnimation"/> places tiles.</summary>
    public enum BuildOrder
    {
        /// <summary>Columns from left to right, each column rising slightly. Good for side-scrollers.</summary>
        SweepRight,
        /// <summary>Rows from bottom to top.</summary>
        BottomUp,
        /// <summary>Outward from a centre cell. Good for top-down maps around the player.</summary>
        Ripple,
        /// <summary>Random but repeatable order.</summary>
        Random,
    }

    /// <summary>
    /// Places tiles over time instead of all at once, with each tile popping in (a short drop and overshoot
    /// via the per-cell transform matrix). Drive it with <see cref="Play"/> as a coroutine in Play mode,
    /// or call <see cref="Step"/> from any clock (the editor uses EditorApplication.update).
    /// </summary>
    public sealed class TileBuildAnimation
    {
        readonly struct Placement
        {
            public readonly Vector3Int Cell;
            public readonly TileBase Tile;
            public readonly float Delay;

            public Placement(Vector3Int cell, TileBase tile, float delay)
            {
                Cell = cell;
                Tile = tile;
                Delay = delay;
            }
        }

        struct Pop
        {
            public Vector3Int Cell;
            public float Start;
            public bool Relock;
        }

        readonly List<Placement> m_Placements;
        readonly List<Pop> m_Popping = new();
        int m_Next;
        double m_StartTime = -1;

        public Tilemap Tilemap { get; }

        /// <summary>Seconds each tile takes to pop in. Scales with the build length (0.22 to 0.6 s).</summary>
        public float PopSeconds { get; set; } = 0.22f;

        public int TotalCells => m_Placements.Count;
        public bool IsDone => m_Next >= m_Placements.Count && m_Popping.Count == 0;

        TileBuildAnimation(Tilemap tilemap, List<Placement> placements)
        {
            Tilemap = tilemap;
            m_Placements = placements;
        }

        /// <summary>
        /// Snapshots the tiles currently on layer 0, clears the tilemap, and returns an animation that puts them back.
        /// Only the tile assets are restored (not per-cell colours or transforms).
        /// </summary>
        public static TileBuildAnimation Rebuild(Tilemap tilemap, BuildOrder order = BuildOrder.SweepRight, float seconds = 1.5f, Vector3Int? rippleCenter = null)
        {
            var cells = new List<(Vector3Int, TileBase)>();
            var b = tilemap.cellBounds;
            for (var y = b.yMin; y < b.yMax; y++)
            for (var x = b.xMin; x < b.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                var tile = tilemap.GetTile(cell);
                if (tile != null) cells.Add((cell, tile));
            }
            tilemap.ClearAllTiles();
            return Create(tilemap, cells, order, seconds, rippleCenter);
        }

        /// <summary>
        /// Animates writing parsed text (same rules as <see cref="AsciiMap.Write(Tilemap, Vector2Int, AsciiMap.ParseResult, TileLegend)"/>):
        /// spaces keep the existing cell, '.' erases. Does not clear the tilemap first.
        /// </summary>
        public static TileBuildAnimation FromText(Tilemap tilemap, Vector2Int origin, AsciiMap.ParseResult parsed, TileLegend legend,
            BuildOrder order = BuildOrder.SweepRight, float seconds = 1.5f, Vector3Int? rippleCenter = null)
        {
            var cells = new List<(Vector3Int, TileBase)>();
            for (var r = 0; r < parsed.Height; r++)
            {
                var row = parsed.Rows[r];
                var y = origin.y + parsed.Height - 1 - r;
                for (var c = 0; c < row.Length; c++)
                {
                    if (row[c] == TileLegend.KeepSymbol || !legend.TryGetTile(row[c], out var tile)) continue;
                    cells.Add((new Vector3Int(origin.x + c, y, 0), tile));
                }
            }
            return Create(tilemap, cells, order, seconds, rippleCenter);
        }

        static TileBuildAnimation Create(Tilemap tilemap, List<(Vector3Int, TileBase)> cells, BuildOrder order, float seconds, Vector3Int? rippleCenter)
        {
            // Longer builds get slower pops, so a slow build reads as deliberate rather than as a stretched fast one.
            var pop = Mathf.Clamp(seconds * 0.12f, 0.22f, 0.6f);
            return new TileBuildAnimation(tilemap, Schedule(cells, order, seconds - pop, rippleCenter)) { PopSeconds = pop };
        }

        /// <summary>Advances the animation to <paramref name="now"/> (seconds on any clock; the first call starts it).</summary>
        public void Step(double now)
        {
            if (Tilemap == null) return;
            if (m_StartTime < 0) m_StartTime = now;
            var t = (float)(now - m_StartTime);

            while (m_Next < m_Placements.Count && m_Placements[m_Next].Delay <= t)
                Place(m_Placements[m_Next++]);

            for (var i = m_Popping.Count - 1; i >= 0; i--)
            {
                var pop = m_Popping[i];
                var p = (t - pop.Start) / PopSeconds;
                if (p >= 1f)
                {
                    EndPop(pop);
                    m_Popping.RemoveAt(i);
                }
                else Tilemap.SetTransformMatrix(pop.Cell, PopMatrix(p));
            }
        }

        /// <summary>Places every remaining tile immediately and resets all transforms.</summary>
        public void Finish()
        {
            if (Tilemap == null) return;
            while (m_Next < m_Placements.Count)
            {
                var p = m_Placements[m_Next++];
                Tilemap.SetTile(p.Cell, p.Tile);
            }
            foreach (var pop in m_Popping) EndPop(pop);
            m_Popping.Clear();
        }

        /// <summary>Coroutine driver using scaled game time.</summary>
        public IEnumerator Play()
        {
            while (!IsDone && Tilemap != null)
            {
                Step(Time.timeAsDouble);
                yield return null;
            }
        }

        // Pops are timed from the scheduled delay, not from when Step ran, so a long frame (or a large
        // step) does not leave late tiles stuck at the start of their pop.
        void Place(Placement p)
        {
            Tilemap.SetTile(p.Cell, p.Tile);
            if (p.Tile == null) return;

            var relock = (Tilemap.GetTileFlags(p.Cell) & TileFlags.LockTransform) != 0;
            if (relock) Tilemap.RemoveTileFlags(p.Cell, TileFlags.LockTransform);
            Tilemap.SetTransformMatrix(p.Cell, PopMatrix(0f));
            m_Popping.Add(new Pop { Cell = p.Cell, Start = p.Delay, Relock = relock });
        }

        void EndPop(Pop pop)
        {
            Tilemap.SetTransformMatrix(pop.Cell, Matrix4x4.identity);
            if (pop.Relock) Tilemap.AddTileFlags(pop.Cell, TileFlags.LockTransform);
        }

        /// <summary>Scale with overshoot (ease-out-back) plus a short drop from above.</summary>
        static Matrix4x4 PopMatrix(float p)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            var q = p - 1f;
            var scale = Mathf.Max(0f, 1f + c3 * q * q * q + c1 * q * q);
            var drop = (1f - p) * (1f - p) * 0.6f;
            return Matrix4x4.TRS(new Vector3(0f, drop, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));
        }

        static List<Placement> Schedule(List<(Vector3Int cell, TileBase tile)> cells, BuildOrder order, float spreadSeconds, Vector3Int? rippleCenter)
        {
            var result = new List<Placement>(cells.Count);
            if (cells.Count == 0) return result;

            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            foreach (var (c, _) in cells)
            {
                xMin = Mathf.Min(xMin, c.x); yMin = Mathf.Min(yMin, c.y);
                xMax = Mathf.Max(xMax, c.x); yMax = Mathf.Max(yMax, c.y);
            }
            var center = rippleCenter ?? new Vector3Int((xMin + xMax) / 2, (yMin + yMax) / 2, 0);

            var keys = new float[cells.Count];
            var maxKey = 0f;
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i].cell;
                keys[i] = order switch
                {
                    BuildOrder.SweepRight => (c.x - xMin) + 0.35f * (c.y - yMin),
                    BuildOrder.BottomUp => (c.y - yMin) + 0.2f * (c.x - xMin),
                    BuildOrder.Ripple => Vector2.Distance(new Vector2(c.x, c.y), new Vector2(center.x, center.y)),
                    _ => Hash(c),
                };
                maxKey = Mathf.Max(maxKey, keys[i]);
            }

            var spread = Mathf.Max(0f, spreadSeconds);
            for (var i = 0; i < cells.Count; i++)
                result.Add(new Placement(cells[i].cell, cells[i].tile, maxKey > 0f ? keys[i] / maxKey * spread : 0f));
            result.Sort((a, b) => a.Delay.CompareTo(b.Delay));
            return result;
        }

        static float Hash(Vector3Int c)
        {
            unchecked
            {
                var h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }
    }
}
