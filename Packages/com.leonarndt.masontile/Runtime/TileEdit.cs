using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile
{
    /// <summary>Outcome of a tilemap edit: how many cells actually changed, and where.</summary>
    public readonly struct TileEdit
    {
        public readonly int CellsChanged;

        /// <summary>Bounds of the changed cells. Zero-size when nothing changed.</summary>
        public readonly RectInt ChangedBounds;

        public TileEdit(int cellsChanged, RectInt changedBounds)
        {
            CellsChanged = cellsChanged;
            ChangedBounds = changedBounds;
        }

        public static TileEdit None => default;
    }

    /// <summary>
    /// Collects cell writes, skips cells that already hold the target tile, and applies them in one
    /// batched <see cref="Tilemap.SetTiles(Vector3Int[], TileBase[])"/> call.
    /// Each cell is written at most once (first write wins).
    /// </summary>
    public sealed class CellBatch
    {
        readonly Tilemap m_Tilemap;
        readonly HashSet<Vector2Int> m_Seen = new();
        readonly List<Vector3Int> m_Positions = new();
        readonly List<TileBase> m_Tiles = new();
        int m_XMin = int.MaxValue, m_YMin = int.MaxValue, m_XMax = int.MinValue, m_YMax = int.MinValue;

        public CellBatch(Tilemap tilemap) => m_Tilemap = tilemap;

        public int PendingCount => m_Positions.Count;

        public void Set(Vector2Int cell, TileBase tile)
        {
            if (!m_Seen.Add(cell)) return;
            var p = new Vector3Int(cell.x, cell.y, 0);
            if (m_Tilemap.GetTile(p) == tile) return;

            m_Positions.Add(p);
            m_Tiles.Add(tile);
            if (cell.x < m_XMin) m_XMin = cell.x;
            if (cell.y < m_YMin) m_YMin = cell.y;
            if (cell.x > m_XMax) m_XMax = cell.x;
            if (cell.y > m_YMax) m_YMax = cell.y;
        }

        public TileEdit Apply()
        {
            if (m_Positions.Count == 0) return TileEdit.None;
            m_Tilemap.SetTiles(m_Positions.ToArray(), m_Tiles.ToArray());
            var bounds = new RectInt(m_XMin, m_YMin, m_XMax - m_XMin + 1, m_YMax - m_YMin + 1);
            return new TileEdit(m_Positions.Count, bounds);
        }
    }
}
