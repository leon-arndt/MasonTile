# TileKit

2D Tilemap tools that work the same for a person in the Editor and for an LLM agent.

## Quick start

1. **Tools > TileKit > Create Demo Tilemap In Scene** creates placeholder tiles, a legend, and a sample level.
2. **Window > TileKit > Tilemap Text View**: select a Tilemap, press **Read**, edit the text, press **Apply** (Ctrl+Z works).
3. With the **AI Assistant** in Agent mode, ask e.g. *"Add a 6x4 room with a water pool in the middle at x=20, y=0 on Ground"*.

For your own art: select tiles, a folder, or a Tile Palette, then use **Assets > TileKit > Create Legend From Selection**.
Add a **TileKit/Tile Legend Binding** to each Tilemap to choose its legend. The binding can also
build the tilemap from a `.txt` level file when Play mode starts.

## Layout

| Assembly | Contents |
|---|---|
| `TileKit.Runtime` | `TilemapOps` (fill/outline/line/flood fill/replace), `AsciiMap` (text read/write), `CaveGenerator`, `TileLegend`, `TileLegendBinding`. Works in builds, for example procedural levels. |
| `TileKit.Editor` | `TilemapCommands`: the validated, undoable command layer. Also the text view window, legend builder, and placeholder tiles. |
| `TileKit.Agent` | `[AgentTool]` wrappers for Unity AI Assistant. Compiles only when `com.unity.ai.assistant` is installed. |
| `TileKit.Tests.Editor` | EditMode tests for ops, text parsing, commands (including undo), and agent tool contracts. |

## Conventions

- Cell coordinates, x right, **y up**, layer z = 0.
- Text grids: **first row is the top**; the origin is the cell under the **bottom-left** character, so read and write round-trip.
- `.` = empty (writing it erases), space = keep existing cell, `?` = tile missing from the legend (read-only).

## Design decisions for the agent layer

- **One command layer for UI and agents.** Tools are thin wrappers around `TilemapCommands`, so the agent can't
  do anything a user couldn't, and testing the command layer covers both.
- **Text as the shared format.** LLMs read and write character grids reliably; tile asset paths and instance ids they don't.
  Tiles are referenced by legend symbol or name.
- **Every edit returns its result.** The change count plus a snapshot of the changed area (with a margin) lets the agent
  check its own work. "0 cells changed" is a strong signal that a call was wrong.
- **Errors say how to fix the call.** An unknown tilemap lists the available ones, an unknown tile prints the legend,
  and parse errors give the row and column.
- **Guard rails.** Per-command cell limits, snapshots truncated to 64x64 (read in pages), flood fills clamped to the used area
  with a warning when the region wasn't enclosed, permission checks before edits, and every edit undoable.
- **Robust to LLM output.** The text parser strips Markdown code fences, CRLF, and blank edge lines.

## Running tests

Test Runner > EditMode > `TileKit.Tests.Editor`, or from a terminal with the Editor open:

```
unity command test run --mode EditMode --filter TileKit.Tests.Editor --filter-type assembly
```
