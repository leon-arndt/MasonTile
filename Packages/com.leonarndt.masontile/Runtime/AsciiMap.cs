using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile
{
    /// <summary>
    /// Text representation of a tilemap region.
    /// Convention: the first text row is the TOP row (highest y). The origin is the cell under the
    /// bottom-left character, so <see cref="Read"/> and <see cref="Write"/> round-trip with the same origin.
    /// </summary>
    public static class AsciiMap
    {
        public const int MaxReportedErrors = 10;

        public sealed class ParseResult
        {
            public readonly List<string> Rows = new();
            public readonly List<string> Errors = new();
            public int Width;
            public int Height => Rows.Count;
            public bool Success => Errors.Count == 0;
        }

        public static string Read(Tilemap tilemap, RectInt area, TileLegend legend)
        {
            if (area.width <= 0 || area.height <= 0) return string.Empty;

            var block = tilemap.GetTilesBlock(new BoundsInt(area.xMin, area.yMin, 0, area.width, area.height, 1));
            var sb = new StringBuilder((area.width + 1) * area.height);
            for (var row = area.height - 1; row >= 0; row--)
            {
                for (var col = 0; col < area.width; col++)
                    sb.Append(legend.GetSymbol(block[col + row * area.width]));
                if (row > 0) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses text against a legend. Tolerates CRLF, Markdown code fences, and blank lines
        /// around the grid, which are common in LLM output. Reports unknown symbols with row/column.
        /// </summary>
        public static ParseResult Parse(string text, TileLegend legend)
        {
            var result = new ParseResult();
            if (string.IsNullOrEmpty(text))
            {
                result.Errors.Add("Text is empty.");
                return result;
            }

            var lines = new List<string>(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
            lines.RemoveAll(l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);

            if (lines.Count == 0)
            {
                result.Errors.Add("Text contains no grid rows.");
                return result;
            }

            var unknownErrors = 0;
            for (var r = 0; r < lines.Count; r++)
            {
                var line = lines[r];
                for (var c = 0; c < line.Length; c++)
                {
                    var ch = line[c];
                    if (ch == TileLegend.KeepSymbol || legend.TryGetTile(ch, out _)) continue;

                    unknownErrors++;
                    if (unknownErrors <= MaxReportedErrors)
                    {
                        var why = ch == '\t' ? "tab characters are not allowed" :
                            ch == TileLegend.UnknownSymbol ? "'?' marks tiles missing from the legend and cannot be written" :
                            $"unknown symbol '{ch}'";
                        result.Errors.Add($"Row {r + 1}, column {c + 1}: {why}.");
                    }
                }
                result.Rows.Add(line);
                result.Width = Math.Max(result.Width, line.Length);
            }

            if (unknownErrors > MaxReportedErrors)
                result.Errors.Add($"...and {unknownErrors - MaxReportedErrors} more.");
            if (unknownErrors > 0)
                result.Errors.Add(legend.Describe() + ", ' '=keep existing.");
            return result;
        }

        /// <summary>Writes parsed text with its bottom-left character at <paramref name="origin"/>.</summary>
        public static TileEdit Write(Tilemap tilemap, Vector2Int origin, ParseResult parsed, TileLegend legend)
        {
            if (!parsed.Success) throw new ArgumentException("Cannot write text with parse errors: " + string.Join(" ", parsed.Errors));

            var batch = new CellBatch(tilemap);
            for (var r = 0; r < parsed.Height; r++)
            {
                var row = parsed.Rows[r];
                var y = origin.y + parsed.Height - 1 - r;
                for (var c = 0; c < row.Length; c++)
                {
                    var ch = row[c];
                    if (ch == TileLegend.KeepSymbol) continue;
                    legend.TryGetTile(ch, out var tile);
                    batch.Set(new Vector2Int(origin.x + c, y), tile);
                }
            }
            return batch.Apply();
        }

        /// <summary>Parse and write in one step. Throws on parse errors.</summary>
        public static TileEdit Write(Tilemap tilemap, Vector2Int origin, string text, TileLegend legend)
            => Write(tilemap, origin, Parse(text, legend), legend);
    }
}
