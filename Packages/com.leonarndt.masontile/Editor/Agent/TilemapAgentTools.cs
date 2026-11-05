using System;
using System.Threading.Tasks;
using MasonTile.Editor;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Agent
{
    /// <summary>
    /// Unity AI Assistant tools for operating 2D tilemaps. Thin wrappers over <see cref="TilemapCommands"/>:
    /// all validation, undo, and result formatting lives there so the agent and the editor UI behave the same.
    ///
    /// Design notes:
    /// - Tiles are referenced by legend symbol or name, never by asset path or instance id.
    /// - Every edit returns a text snapshot of what changed so the agent can check its own work.
    /// - Errors explain how to fix the call (available tilemaps, the legend, valid ranges).
    /// </summary>
    static class TilemapAgentTools
    {
        const string k_Coords =
            " Coordinates are integer cell positions; x grows right, y grows UP. " +
            "In text grids the first row is the TOP row and the origin is the cell under the bottom-left character. ";

        const string k_TileParam =
            "Tile to paint: a legend symbol (e.g. '#'), a legend tile name (e.g. 'wall'), or 'empty' to erase.";

        const string k_TilemapParam =
            "Tilemap name or hierarchy path (e.g. 'Grid/Ground'). May be empty if the scene has exactly one Tilemap.";

        [AgentTool(
            "List the Tilemaps in the open scene with their used area, legend (symbol to tile mapping), and tile counts. " +
            "Call this first to learn tilemap names and which symbols are available.",
            "MasonTile.ListTilemaps")]
        public static CommandResult ListTilemaps() => Unwrap(TilemapCommands.List());

        [AgentTool(
            "Read a region of a Tilemap as a text grid using its legend. With width/height 0 it reads the whole used area. " +
            "Large regions are truncated to 64x64; read in pages by passing x/y/width/height." + k_Coords +
            "'.' is empty, '?' is a tile missing from the legend.",
            "MasonTile.ReadTilemap")]
        public static CommandResult ReadTilemap(
            [ToolParameter(k_TilemapParam)] string tilemap,
            [ToolParameter("Left-most cell x of the region. Ignored when width or height is 0.")] int x = 0,
            [ToolParameter("Bottom-most cell y of the region. Ignored when width or height is 0.")] int y = 0,
            [ToolParameter("Region width in cells, or 0 for the whole used area.")] int width = 0,
            [ToolParameter("Region height in cells, or 0 for the whole used area.")] int height = 0)
        {
            RectInt? area = width > 0 && height > 0 ? new RectInt(x, y, width, height) : null;
            return Unwrap(TilemapCommands.Read(tilemap, area));
        }

        [AgentTool(
            "Fill a rectangle of cells with one tile, or draw only its 1-cell border (outlineOnly). " +
            "Use for floors, walls, rooms, and clearing areas (tile 'empty')." + k_Coords +
            "Returns the number of cells changed and a snapshot of the result.",
            "MasonTile.PaintRect")]
        public static async Task<CommandResult> PaintRect(
            ToolExecutionContext context,
            [ToolParameter(k_TilemapParam)] string tilemap,
            [ToolParameter("Left-most cell x.")] int x,
            [ToolParameter("Bottom-most cell y.")] int y,
            [ToolParameter("Width in cells (>= 1).")] int width,
            [ToolParameter("Height in cells (>= 1).")] int height,
            [ToolParameter(k_TileParam)] string tile,
            [ToolParameter("If true, only paint the rectangle's border, leaving the inside untouched.")] bool outlineOnly = false)
        {
            var tm = await Authorize(context, tilemap);
            return Unwrap(TilemapCommands.Fill(TilemapCommands.HierarchyPath(tm), new RectInt(x, y, width, height), tile, outlineOnly));
        }

        [AgentTool(
            "Draw a straight 1-cell-wide line between two cells (both ends inclusive). Works for horizontal, vertical, and diagonal lines." +
            k_Coords,
            "MasonTile.DrawLine")]
        public static async Task<CommandResult> DrawLine(
            ToolExecutionContext context,
            [ToolParameter(k_TilemapParam)] string tilemap,
            [ToolParameter("Start cell x.")] int x1,
            [ToolParameter("Start cell y.")] int y1,
            [ToolParameter("End cell x.")] int x2,
            [ToolParameter("End cell y.")] int y2,
            [ToolParameter(k_TileParam)] string tile)
        {
            var tm = await Authorize(context, tilemap);
            return Unwrap(TilemapCommands.Line(TilemapCommands.HierarchyPath(tm), new Vector2Int(x1, y1), new Vector2Int(x2, y2), tile));
        }

        [AgentTool(
            "Flood fill: replace the 4-connected region of cells that share the start cell's tile (including empty cells). " +
            "Stays within the used area plus a 1-cell margin; the result warns if the region was not enclosed." + k_Coords,
            "MasonTile.FloodFill")]
        public static async Task<CommandResult> FloodFill(
            ToolExecutionContext context,
            [ToolParameter(k_TilemapParam)] string tilemap,
            [ToolParameter("Start cell x.")] int x,
            [ToolParameter("Start cell y.")] int y,
            [ToolParameter(k_TileParam)] string tile)
        {
            var tm = await Authorize(context, tilemap);
            return Unwrap(TilemapCommands.FloodFill(TilemapCommands.HierarchyPath(tm), new Vector2Int(x, y), tile));
        }

        [AgentTool(
            "Write a text grid of legend symbols into the Tilemap. Best for hand-designed layouts and detailed edits. " +
            "'.' erases a cell, a space leaves it unchanged, and short rows are padded with spaces." + k_Coords +
            "Example rows \"####\\n#..#\\n####\" at x=0,y=0 puts the top row at y=2.",
            "MasonTile.WriteText")]
        public static async Task<CommandResult> WriteText(
            ToolExecutionContext context,
            [ToolParameter(k_TilemapParam)] string tilemap,
            [ToolParameter("Cell x under the bottom-left character.")] int x,
            [ToolParameter("Cell y under the bottom-left character.")] int y,
            [ToolParameter("Grid rows separated by newlines, top row first. Only legend symbols, '.', and spaces.")] string rows)
        {
            var tm = await Authorize(context, tilemap);
            return Unwrap(TilemapCommands.WriteText(TilemapCommands.HierarchyPath(tm), new Vector2Int(x, y), rows));
        }

        [AgentTool(
            "Replace every occurrence of one tile with another across the Tilemap's used area (e.g. swap 'grass' for 'snow').",
            "MasonTile.ReplaceTile")]
        public static async Task<CommandResult> ReplaceTile(
            ToolExecutionContext context,
            [ToolParameter(k_TilemapParam)] string tilemap,
            [ToolParameter("Tile to replace (legend symbol or name).")] string fromTile,
            [ToolParameter("Replacement tile (legend symbol or name), or 'empty' to erase.")] string toTile)
        {
            var tm = await Authorize(context, tilemap);
            return Unwrap(TilemapCommands.Replace(TilemapCommands.HierarchyPath(tm), fromTile, toTile));
        }

        [AgentTool(
            "Generate an organic cave in a rectangle using cellular automata. The border is always wall. " +
            "The same seed and parameters always produce the same cave; try other seeds for variations." + k_Coords,
            "MasonTile.GenerateCave")]
        public static async Task<CommandResult> GenerateCave(
            ToolExecutionContext context,
            [ToolParameter(k_TilemapParam)] string tilemap,
            [ToolParameter("Left-most cell x.")] int x,
            [ToolParameter("Bottom-most cell y.")] int y,
            [ToolParameter("Width in cells (>= 3).")] int width,
            [ToolParameter("Height in cells (>= 3).")] int height,
            [ToolParameter("Random seed.")] int seed,
            [ToolParameter("Wall tile (legend symbol or name).")] string wallTile,
            [ToolParameter("Floor tile (legend symbol or name), or 'empty'.")] string floorTile = "empty",
            [ToolParameter("Initial wall density, 0 to 1. Higher means smaller caverns. 0.45 to 0.5 works well.")] float fillPercent = 0.47f,
            [ToolParameter("Smoothing passes, 0 to 20. More means rounder caves.")] int smoothSteps = 5)
        {
            var tm = await Authorize(context, tilemap);
            return Unwrap(TilemapCommands.GenerateCave(TilemapCommands.HierarchyPath(tm), new RectInt(x, y, width, height), seed,
                wallTile, floorTile, fillPercent, smoothSteps));
        }

        /// <summary>Resolves the tilemap and asks the user's permission settings before any modification.</summary>
        static async Task<Tilemap> Authorize(ToolExecutionContext context, string tilemap)
        {
            if (!TilemapCommands.TryFindTilemap(tilemap, out var tm, out var error))
                throw new ArgumentException(error);
            await context.Permissions.CheckUnityObjectAccess(PermissionItemOperation.Modify, typeof(Tilemap), tm);
            return tm;
        }

        /// <summary>Failures are thrown so the Assistant reports them as tool errors, with the fix-it message intact.</summary>
        static CommandResult Unwrap(CommandResult result)
            => result.Success ? result : throw new InvalidOperationException(result.Message);
    }
}
