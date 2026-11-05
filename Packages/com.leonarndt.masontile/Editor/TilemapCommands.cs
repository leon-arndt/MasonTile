using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Editor
{
    /// <summary>
    /// Text snapshot of a tilemap region. First row is the top (highest y); (OriginX, OriginY) is the
    /// cell under the bottom-left character.
    /// </summary>
    [Serializable]
    public class AsciiSnapshot
    {
        public int OriginX;
        public int OriginY;
        public int Width;
        public int Height;
        public string Rows;
        public string Legend;
        public bool Truncated;
    }

    [Serializable]
    public class CommandResult
    {
        public bool Success;
        public string Message;
        public int CellsChanged;
        public AsciiSnapshot Snapshot;

        public static CommandResult Fail(string message) => new() { Success = false, Message = message };
        public override string ToString() => Snapshot == null ? Message : $"{Message}\n{Snapshot.Rows}";
    }

    /// <summary>
    /// Structured, validated, undoable tilemap commands. This is the single entry point shared by
    /// editor UI and agent tools: it resolves names, validates input, records Undo, and returns
    /// results that tell the caller exactly what happened (including a text snapshot to verify against).
    /// </summary>
    public static class TilemapCommands
    {
        /// <summary>Upper bound on cells touched by one command; guards against runaway edits.</summary>
        public const int MaxCellsPerCommand = 256 * 256;

        /// <summary>Largest snapshot side length; bigger regions are truncated and must be paged.</summary>
        public const int MaxSnapshotSize = 64;

        // ---------- Resolution ----------

        public static List<Tilemap> FindSceneTilemaps()
        {
            return UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include)
                .Where(t => t.gameObject.scene.IsValid() && (t.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0)
                .OrderBy(HierarchyPath)
                .ToList();
        }

        public static string HierarchyPath(Component c)
        {
            var sb = new StringBuilder(c.name);
            for (var t = c.transform.parent; t != null; t = t.parent)
                sb.Insert(0, t.name + "/");
            return sb.ToString();
        }

        /// <summary>
        /// Finds a scene tilemap by hierarchy path or name (case-insensitive). An empty name selects
        /// the only tilemap when exactly one exists.
        /// </summary>
        public static bool TryFindTilemap(string nameOrPath, out Tilemap tilemap, out string error)
        {
            tilemap = null;
            error = null;
            var all = FindSceneTilemaps();
            if (all.Count == 0)
            {
                error = "There are no Tilemaps in the open scene(s).";
                return false;
            }

            List<Tilemap> matches;
            if (string.IsNullOrWhiteSpace(nameOrPath))
                matches = all.Count == 1 ? all : new List<Tilemap>();
            else
            {
                var key = nameOrPath.Trim();
                matches = all.Where(t => string.Equals(HierarchyPath(t), key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                    matches = all.Where(t => string.Equals(t.name, key, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (matches.Count == 1)
            {
                tilemap = matches[0];
                return true;
            }

            var available = string.Join(", ", all.Select(t => $"'{HierarchyPath(t)}'"));
            error = matches.Count == 0
                ? $"No Tilemap named '{nameOrPath}'. Available: {available}."
                : $"'{nameOrPath}' is ambiguous; use the full path. Matches: {string.Join(", ", matches.Select(t => $"'{HierarchyPath(t)}'"))}.";
            return false;
        }

        /// <summary>Legend from the tilemap's <see cref="TileLegendBinding"/>, else the only TileLegend in the project.</summary>
        public static bool TryResolveLegend(Tilemap tilemap, out TileLegend legend, out string error)
        {
            error = null;
            var binding = tilemap.GetComponent<TileLegendBinding>();
            legend = binding != null ? binding.legend : null;
            if (legend != null) return true;

            var guids = AssetDatabase.FindAssets("t:" + nameof(TileLegend));
            if (guids.Length == 1)
            {
                legend = AssetDatabase.LoadAssetAtPath<TileLegend>(AssetDatabase.GUIDToAssetPath(guids[0]));
                return legend != null;
            }

            error = guids.Length == 0
                ? $"Tilemap '{tilemap.name}' has no legend. Create one (Assets > Create > MasonTile > Tile Legend) and add a TileLegendBinding to the Tilemap."
                : $"Tilemap '{tilemap.name}' has no TileLegendBinding and the project has {guids.Length} legends; add a binding to choose one.";
            return false;
        }

        // ---------- Name-based facade (used by agent tools) ----------

        public static CommandResult List()
        {
            var all = FindSceneTilemaps();
            if (all.Count == 0) return new CommandResult { Success = true, Message = "There are no Tilemaps in the open scene(s)." };

            var sb = new StringBuilder($"{all.Count} tilemap(s):");
            foreach (var tm in all)
            {
                var used = TilemapOps.UsedBounds(tm);
                sb.Append($"\n- '{HierarchyPath(tm)}': ");
                sb.Append(used.width == 0 ? "empty" : $"used area x={used.xMin} y={used.yMin} width={used.width} height={used.height}");
                sb.Append(TryResolveLegend(tm, out var legend, out _) ? $"; {legend.Describe()}" : "; no legend");
                if (legend != null && used.width > 0)
                {
                    var counts = TilemapOps.CountTiles(tm, used);
                    sb.Append("; counts: ").Append(string.Join(", ", counts.Select(kv => $"'{legend.GetSymbol(kv.Key)}'({kv.Key.name})={kv.Value}")));
                }
            }
            return new CommandResult { Success = true, Message = sb.ToString() };
        }

        public static CommandResult Read(string tilemapName, RectInt? area = null)
            => WithTilemap(tilemapName, (tm, legend) => Read(tm, legend, area));

        public static CommandResult Fill(string tilemapName, RectInt rect, string tile, bool outlineOnly = false)
            => WithTilemap(tilemapName, (tm, legend) => Fill(tm, legend, rect, tile, outlineOnly));

        public static CommandResult Line(string tilemapName, Vector2Int from, Vector2Int to, string tile)
            => WithTilemap(tilemapName, (tm, legend) => Line(tm, legend, from, to, tile));

        public static CommandResult FloodFill(string tilemapName, Vector2Int start, string tile, RectInt? limit = null)
            => WithTilemap(tilemapName, (tm, legend) => FloodFill(tm, legend, start, tile, limit));

        public static CommandResult WriteText(string tilemapName, Vector2Int origin, string text)
            => WithTilemap(tilemapName, (tm, legend) => WriteText(tm, legend, origin, text));

        public static CommandResult Replace(string tilemapName, string fromTile, string toTile, RectInt? area = null)
            => WithTilemap(tilemapName, (tm, legend) => Replace(tm, legend, fromTile, toTile, area));

        public static CommandResult GenerateCave(string tilemapName, RectInt rect, int seed, string wallTile, string floorTile,
            float fillPercent = 0.47f, int smoothSteps = 5)
            => WithTilemap(tilemapName, (tm, legend) => GenerateCave(tm, legend, rect, seed, wallTile, floorTile, fillPercent, smoothSteps));

        static CommandResult WithTilemap(string tilemapName, Func<Tilemap, TileLegend, CommandResult> command)
        {
            if (!TryFindTilemap(tilemapName, out var tm, out var error)) return CommandResult.Fail(error);
            if (!TryResolveLegend(tm, out var legend, out error)) return CommandResult.Fail(error);
            return command(tm, legend);
        }

        // ---------- Object-based commands (used by editor UI and tests) ----------

        public static CommandResult Read(Tilemap tm, TileLegend legend, RectInt? area = null)
        {
            var region = area ?? TilemapOps.UsedBounds(tm);
            if (region.width <= 0 || region.height <= 0)
            {
                return area.HasValue
                    ? CommandResult.Fail($"Invalid area size {region.width}x{region.height}; width and height must be at least 1.")
                    : new CommandResult { Success = true, Message = $"Tilemap '{tm.name}' is empty. {legend.Describe()}" };
            }

            var snapshot = Snapshot(tm, legend, region);
            var msg = $"Tilemap '{tm.name}' region x={region.xMin} y={region.yMin} width={region.width} height={region.height}.";
            if (snapshot.Truncated)
                msg += $" Truncated to {snapshot.Width}x{snapshot.Height} from the top-left; read the rest with a smaller area.";
            return new CommandResult { Success = true, Message = msg, Snapshot = snapshot };
        }

        public static CommandResult Fill(Tilemap tm, TileLegend legend, RectInt rect, string tile, bool outlineOnly = false)
        {
            if (!ValidateArea(rect, out var error) || !legend.TryResolve(tile, out var t, out error)) return CommandResult.Fail(error);
            var verb = outlineOnly ? "Outline" : "Fill";
            return Mutate(tm, legend, $"MasonTile {verb}",
                () => outlineOnly ? TilemapOps.Outline(tm, rect, t) : TilemapOps.Fill(tm, rect, t),
                $"{verb} x={rect.xMin} y={rect.yMin} width={rect.width} height={rect.height} with '{legend.GetSymbol(t)}'");
        }

        public static CommandResult Line(Tilemap tm, TileLegend legend, Vector2Int from, Vector2Int to, string tile)
        {
            if (!legend.TryResolve(tile, out var t, out var error)) return CommandResult.Fail(error);
            var length = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) + 1;
            if (length > MaxCellsPerCommand) return CommandResult.Fail($"Line is {length} cells; the limit is {MaxCellsPerCommand}.");
            return Mutate(tm, legend, "MasonTile Line", () => TilemapOps.Line(tm, from, to, t),
                $"Line ({from.x},{from.y})->({to.x},{to.y}) with '{legend.GetSymbol(t)}'");
        }

        public static CommandResult FloodFill(Tilemap tm, TileLegend legend, Vector2Int start, string tile, RectInt? limit = null)
        {
            if (!legend.TryResolve(tile, out var t, out var error)) return CommandResult.Fail(error);

            var region = limit ?? Expand(TilemapOps.UsedBounds(tm), 1);
            if (region.width <= 0) return CommandResult.Fail("Tilemap is empty; pass an explicit limit area for the flood fill.");
            if (!ValidateArea(region, out error)) return CommandResult.Fail(error);
            if (!region.Contains(start))
                return CommandResult.Fail($"Start ({start.x},{start.y}) is outside the fill limit x={region.xMin} y={region.yMin} width={region.width} height={region.height}.");

            var touched = false;
            var result = Mutate(tm, legend, "MasonTile Flood Fill", () => TilemapOps.FloodFill(tm, start, t, region, out touched),
                $"Flood fill from ({start.x},{start.y}) with '{legend.GetSymbol(t)}'");
            if (touched)
                result.Message += " Warning: the fill reached the edge of the limit area, so the region was probably not enclosed.";
            return result;
        }

        public static CommandResult WriteText(Tilemap tm, TileLegend legend, Vector2Int origin, string text)
        {
            var parsed = AsciiMap.Parse(text, legend);
            if (!parsed.Success) return CommandResult.Fail("Text not written. " + string.Join(" ", parsed.Errors));
            if ((long)parsed.Width * parsed.Height > MaxCellsPerCommand)
                return CommandResult.Fail($"Text is {parsed.Width}x{parsed.Height}; the limit is {MaxCellsPerCommand} cells.");

            return Mutate(tm, legend, "MasonTile Write Text", () => AsciiMap.Write(tm, origin, parsed, legend),
                $"Wrote {parsed.Width}x{parsed.Height} text with bottom-left at ({origin.x},{origin.y})");
        }

        public static CommandResult Replace(Tilemap tm, TileLegend legend, string fromTile, string toTile, RectInt? area = null)
        {
            if (!legend.TryResolve(fromTile, out var from, out var error) || !legend.TryResolve(toTile, out var to, out error))
                return CommandResult.Fail(error);

            var region = area ?? TilemapOps.UsedBounds(tm);
            if (region.width <= 0) return new CommandResult { Success = true, Message = "Tilemap is empty; nothing to replace." };
            if (!ValidateArea(region, out error)) return CommandResult.Fail(error);
            return Mutate(tm, legend, "MasonTile Replace", () => TilemapOps.Replace(tm, region, from, to),
                $"Replaced '{legend.GetSymbol(from)}' with '{legend.GetSymbol(to)}'");
        }

        public static CommandResult GenerateCave(Tilemap tm, TileLegend legend, RectInt rect, int seed, string wallTile, string floorTile,
            float fillPercent = 0.47f, int smoothSteps = 5)
        {
            if (!ValidateArea(rect, out var error)) return CommandResult.Fail(error);
            if (rect.width < 3 || rect.height < 3) return CommandResult.Fail("Cave area must be at least 3x3.");
            if (!legend.TryResolve(wallTile, out var wall, out error) || !legend.TryResolve(floorTile, out var floor, out error))
                return CommandResult.Fail(error);
            if (fillPercent < 0f || fillPercent > 1f) return CommandResult.Fail("fillPercent must be between 0 and 1.");
            if (smoothSteps < 0 || smoothSteps > 20) return CommandResult.Fail("smoothSteps must be between 0 and 20.");

            var walls = CaveGenerator.Generate(rect.width, rect.height, seed, fillPercent, smoothSteps);
            return Mutate(tm, legend, "MasonTile Generate Cave", () => CaveGenerator.Paint(tm, rect.position, walls, wall, floor),
                $"Generated cave (seed {seed}) at x={rect.xMin} y={rect.yMin} width={rect.width} height={rect.height}");
        }

        // ---------- Helpers ----------

        static CommandResult Mutate(Tilemap tm, TileLegend legend, string undoName, Func<TileEdit> op, string description)
        {
            var recordUndo = !Application.isPlaying;
            var group = 0;
            if (recordUndo)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                group = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(tm, undoName);
            }

            var edit = op();

            if (recordUndo)
            {
                Undo.CollapseUndoOperations(group);
                if (edit.CellsChanged > 0) EditorUtility.SetDirty(tm);
            }

            var result = new CommandResult
            {
                Success = true,
                CellsChanged = edit.CellsChanged,
                Message = edit.CellsChanged == 0
                    ? $"{description}: 0 cells changed (they already had that tile)."
                    : $"{description}: {edit.CellsChanged} cell(s) changed. Snapshot shows the changed area plus a 1-cell margin.",
            };
            if (edit.CellsChanged > 0) result.Snapshot = Snapshot(tm, legend, Expand(edit.ChangedBounds, 1));
            return result;
        }

        static AsciiSnapshot Snapshot(Tilemap tm, TileLegend legend, RectInt region)
        {
            var truncated = region.width > MaxSnapshotSize || region.height > MaxSnapshotSize;
            if (truncated)
            {
                // Keep the top-left corner, which is where text rows start.
                var w = Mathf.Min(region.width, MaxSnapshotSize);
                var h = Mathf.Min(region.height, MaxSnapshotSize);
                region = new RectInt(region.xMin, region.yMax - h, w, h);
            }

            return new AsciiSnapshot
            {
                OriginX = region.xMin,
                OriginY = region.yMin,
                Width = region.width,
                Height = region.height,
                Rows = AsciiMap.Read(tm, region, legend),
                Legend = legend.Describe(),
                Truncated = truncated,
            };
        }

        static bool ValidateArea(RectInt rect, out string error)
        {
            error = null;
            if (rect.width <= 0 || rect.height <= 0)
                error = $"Invalid area size {rect.width}x{rect.height}; width and height must be at least 1.";
            else if ((long)rect.width * rect.height > MaxCellsPerCommand)
                error = $"Area {rect.width}x{rect.height} exceeds the {MaxCellsPerCommand}-cell limit per command.";
            return error == null;
        }

        static RectInt Expand(RectInt r, int margin)
            => r.width <= 0 ? r : new RectInt(r.xMin - margin, r.yMin - margin, r.width + 2 * margin, r.height + 2 * margin);
    }
}
