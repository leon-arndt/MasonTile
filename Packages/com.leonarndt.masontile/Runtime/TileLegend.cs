using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile
{
    /// <summary>
    /// Maps single characters to tiles so tilemaps can be read and written as text.
    /// Text is the shared format for level files, the text view window, tests, and LLM agents.
    /// </summary>
    [CreateAssetMenu(menuName = "MasonTile/Tile Legend", fileName = "TileLegend", order = 0)]
    public class TileLegend : ScriptableObject
    {
        /// <summary>Always means "no tile". Writing it erases the cell.</summary>
        public const char EmptySymbol = '.';

        /// <summary>Means "leave this cell unchanged" when writing text. Short rows are padded with it.</summary>
        public const char KeepSymbol = ' ';

        /// <summary>Emitted when reading a cell whose tile is not in the legend. Cannot be written.</summary>
        public const char UnknownSymbol = '?';

        [Serializable]
        public struct Entry
        {
            public char symbol;
            public string name;
            public TileBase tile;
        }

        [SerializeField] List<Entry> entries = new();

        Dictionary<char, TileBase> m_BySymbol;
        Dictionary<TileBase, char> m_ByTile;

        public IReadOnlyList<Entry> Entries => entries;

        public static bool IsReserved(char c) => c == EmptySymbol || c == KeepSymbol || c == UnknownSymbol;

        /// <summary>Adds an entry, or replaces the existing entry with the same symbol.</summary>
        public void Set(char symbol, string entryName, TileBase tile)
        {
            if (IsReserved(symbol))
                throw new ArgumentException($"'{symbol}' is reserved and cannot be assigned to a tile.", nameof(symbol));

            var entry = new Entry { symbol = symbol, name = entryName, tile = tile };
            var index = entries.FindIndex(e => e.symbol == symbol);
            if (index >= 0) entries[index] = entry;
            else entries.Add(entry);
            Invalidate();
        }

        public bool TryGetTile(char symbol, out TileBase tile)
        {
            if (symbol == EmptySymbol)
            {
                tile = null;
                return true;
            }

            EnsureLookups();
            return m_BySymbol.TryGetValue(symbol, out tile);
        }

        /// <summary>Symbol for a tile: <see cref="EmptySymbol"/> for null, <see cref="UnknownSymbol"/> if not in the legend.</summary>
        public char GetSymbol(TileBase tile)
        {
            if (tile == null) return EmptySymbol;
            EnsureLookups();
            return m_ByTile.TryGetValue(tile, out var c) ? c : UnknownSymbol;
        }

        /// <summary>
        /// Resolves a user- or agent-supplied token: a single legend symbol, an entry name (case-insensitive),
        /// or one of "empty", "none", "erase" for no tile.
        /// </summary>
        public bool TryResolve(string token, out TileBase tile, out string error)
        {
            tile = null;
            error = null;
            if (string.IsNullOrWhiteSpace(token))
            {
                error = $"No tile given. Use a legend symbol or name. {Describe()}";
                return false;
            }

            var trimmed = token.Trim();
            if (trimmed.Length == 1 && TryGetTile(trimmed[0], out tile))
                return true;

            if (string.Equals(trimmed, "empty", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "erase", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var e in entries)
            {
                if (string.Equals(e.name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    (e.tile != null && string.Equals(e.tile.name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    tile = e.tile;
                    return true;
                }
            }

            error = $"Unknown tile '{trimmed}'. {Describe()}";
            return false;
        }

        /// <summary>One-line legend description, e.g. "Legend: '.'=empty, '#'=wall, '~'=water".</summary>
        public string Describe()
        {
            var sb = new StringBuilder("Legend: '.'=empty");
            foreach (var e in entries)
                sb.Append($", '{e.symbol}'={(string.IsNullOrEmpty(e.name) ? e.tile?.name : e.name)}");
            return sb.ToString();
        }

        /// <summary>Configuration problems, empty if the legend is valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var symbols = new HashSet<char>();
            var tiles = new HashSet<TileBase>();
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (IsReserved(e.symbol)) problems.Add($"Entry {i}: symbol '{e.symbol}' is reserved.");
                if (!symbols.Add(e.symbol)) problems.Add($"Entry {i}: duplicate symbol '{e.symbol}'.");
                if (e.tile == null) problems.Add($"Entry {i} ('{e.symbol}'): no tile assigned.");
                else if (!tiles.Add(e.tile)) problems.Add($"Entry {i} ('{e.symbol}'): tile '{e.tile.name}' is already mapped.");
            }
            return problems;
        }

        void EnsureLookups()
        {
            if (m_BySymbol != null) return;
            m_BySymbol = new Dictionary<char, TileBase>();
            m_ByTile = new Dictionary<TileBase, char>();
            foreach (var e in entries)
            {
                if (e.tile == null || IsReserved(e.symbol)) continue;
                m_BySymbol.TryAdd(e.symbol, e.tile);
                m_ByTile.TryAdd(e.tile, e.symbol);
            }
        }

        void Invalidate()
        {
            m_BySymbol = null;
            m_ByTile = null;
        }

        void OnValidate() => Invalidate();

        // Assets can be unloaded and reloaded (scene changes, reimports); never trust a cache from before.
        void OnEnable() => Invalidate();
    }
}
