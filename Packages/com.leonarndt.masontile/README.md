# MasonTile

**Text-first tilemap editing for creators and LLM agents.**

MasonTile lets you read and write Unity Tilemaps as character grids, run undoable fill, line, flood-fill and cave
commands, and hand those same commands to Unity AI Assistant as agent tools.

```
##################
#gggg.....~~~~...#
#gggg.....~~~~.c.#      '#' wall   'g' grass   '~' water
#.....ffff.......#      'f' floor  'c' coin    '*' spikes
#.....ffff..###..#      '.' empty
#..c..........*..#
##################
```

## Install

Requires Unity 6000.4 or newer. In Package Manager choose **Install package from git URL** and enter:

```
https://github.com/leon-arndt/UnityJam2026.git?path=/Packages/com.leonarndt.masontile#v0.1.0
```

Or add it to `Packages/manifest.json`:

```json
"com.leonarndt.masontile": "https://github.com/leon-arndt/UnityJam2026.git?path=/Packages/com.leonarndt.masontile#v0.1.0"
```

The agent tools turn on automatically when `com.unity.ai.assistant` is installed. Everything else works without it.

## Quick start

1. **Tools > MasonTile > Create Demo Tilemap In Scene** creates placeholder tiles, a legend, and a sample level.
2. **Window > MasonTile > Tilemap Text View**: select a Tilemap, press **Read**, edit the text, press **Apply**. Ctrl+Z works.
3. With **AI Assistant** in Agent mode, ask for something like *"Add a 6x4 room with a water pool in the middle at x=20, y=0 on Ground"*.

For your own art, select tiles, a folder, or a Tile Palette and choose **Assets > MasonTile > Create Legend From Selection**.
Add a **MasonTile/Tile Legend Binding** to each Tilemap to pick its legend. The binding can also build the tilemap
from a `.txt` level file when Play mode starts.

## Contents

| Assembly | Contents |
|---|---|
| `MasonTile.Runtime` | `TilemapOps` (fill, outline, line, flood fill, replace), `AsciiMap` (text read/write), `CaveGenerator`, `TileLegend`, `TileLegendBinding`. Works in builds, e.g. for procedural levels. |
| `MasonTile.Editor` | `TilemapCommands`, the validated and undoable command layer. Also the text view window, legend builder, and placeholder tiles. |
| `MasonTile.Agent` | Eight `[AgentTool]`s for Unity AI Assistant. Compiled only when `com.unity.ai.assistant` is installed. |
| `MasonTile.Tests.Editor` | EditMode tests for operations, text parsing, commands (including undo), and agent tool contracts. |

## Conventions

- Cell coordinates: x right, **y up**, layer z = 0.
- Text grids: **the first row is the top**. The origin is the cell under the **bottom-left** character, so a read and a
  write with the same origin round-trip.
- `.` = empty (writing it erases), space = keep the existing cell, `?` = tile missing from the legend (read-only).

## Design decisions for the agent layer

- **One command layer for UI and agents.** Agent tools are thin wrappers around `TilemapCommands`, so the agent can't do
  anything a user couldn't, and testing that one layer covers both.
- **Text as the shared format.** LLMs read and write character grids reliably; asset paths and instance ids they don't.
  Tiles are referenced by legend symbol or name.
- **Every edit returns its result.** The changed-cell count and a snapshot of the changed area (with a 1-cell margin) let
  the agent check its own work. "0 cells changed" is a strong signal that a call was wrong.
- **Errors say how to fix the call.** An unknown tilemap lists the available ones, an unknown tile prints the legend, and
  parse errors give the row and column.
- **Guard rails.** A per-command cell limit, snapshots truncated to 64x64 (read larger areas in pages), flood fills
  clamped to the used area with a warning when the region wasn't enclosed, permission checks before edits, and every
  edit undoable.
- **Robust to LLM output.** The text parser strips Markdown code fences, CRLF line endings, and blank edge lines.

## Running the tests

In a project that installs the package from git, list it as testable in `Packages/manifest.json` so its tests appear in
the Test Runner:

```json
"testables": ["com.leonarndt.masontile"]
```

Then run **Window > General > Test Runner > EditMode**, or from a terminal with the Editor open:

```
unity command test run --mode EditMode --filter MasonTile.Tests.Editor --filter-type assembly
```
