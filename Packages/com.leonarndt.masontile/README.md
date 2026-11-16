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
https://github.com/leon-arndt/MasonTile.git?path=/Packages/com.leonarndt.masontile#masontile-v0.2.0
```

Or add it to `Packages/manifest.json`:

```json
"com.leonarndt.masontile": "https://github.com/leon-arndt/MasonTile.git?path=/Packages/com.leonarndt.masontile#masontile-v0.2.0"
```

The agent tools turn on automatically when `com.unity.ai.assistant` is installed. Everything else works without it.

## Quick start

1. **Tools > MasonTile > Demos > Platformer Demo** or **Top-Down Demo** imports a playable sample and opens it.
   Each sample is only text and code; on import it draws its own pixel art and builds its scene.
   You can also import them from Package Manager > MasonTile > Samples.
2. **Window > MasonTile > Tilemap Text View**: select a Tilemap, press **Read**, edit the text, press **Apply**. Ctrl+Z works.
3. With **AI Assistant** in Agent mode, ask for something like *"Add a 6x4 room with a water pool in the middle at x=20, y=0 on Ground"*.

For your own art, select tiles, a folder, or a Tile Palette and choose **Assets > MasonTile > Create Legend From Selection**.
Add a **MasonTile/Tile Legend Binding** to each Tilemap to pick its legend. The binding can also build the tilemap
from a `.txt` level file when Play mode starts.

## Contents

| Assembly | Contents |
|---|---|
| `MasonTile.Runtime` | `TilemapOps` (fill, outline, line, flood fill, replace), `AsciiMap` (text read/write), `CaveGenerator`, `TileLegend`, `TileLegendBinding`, `TileBuildAnimation` (place tiles over time with a pop-in effect). Works in builds, e.g. for procedural levels. |
| `MasonTile.Editor` | `TilemapCommands`, the validated and undoable command layer. Also the text view window, legend builder, placeholder tiles, `PixelCanvas` and `TileSetBuilder` (define a tileset in code), and `SceneBuilder`. |
| `MasonTile.Agent` | Eight `[AgentTool]`s for Unity AI Assistant. Compiled only when `com.unity.ai.assistant` is installed. |
| `MasonTile.Tests.Editor` | EditMode tests for operations, text parsing, commands (including undo), and agent tool contracts. |
| `MasonTile.Agent.Tests` | EditMode integration tests that call each agent tool as AI Assistant does: permission checks, denial, errors, undo. Compiled only with `com.unity.ai.assistant`. |
| `MasonTile.Tests.Runtime` | PlayMode tests for building a tilemap from a text level when the scene starts. |

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

Then use **Window > General > Test Runner** (EditMode and PlayMode tabs), or from a terminal with the Editor open:

```
unity command test run --mode editor --filter MasonTile --filter-type assembly
unity command test run --mode playmode --filter MasonTile.Tests.Runtime --filter-type assembly --async-tests true
unity command test status
```

PlayMode runs reload scripts, so start them with `--async-tests` and poll `test status` for the results.

## Benchmarking the agent tools

`Bench/` holds the MasonTile suite for [Unity Agent Bench](../com.leonarndt.agentbench/README.md): 13 creator
tasks (build a room from a spec, centre a pool in an existing room, seal a leaking enclosure before flooding it,
stamp a layout by its top-left corner, connect two rooms with a corridor, and more), each with a checker and a
reference solution. Four of them are deliberately hard (a map wider than one read, an enclosure with hidden gaps,
the shortest route through a maze, a mirrored copy of a room), so the suite has room to show improvements. The assembly compiles only when both AI Assistant and the bench package are installed.

```bash
H=$(ls -d Packages/com.leonarndt.agentbench Library/PackageCache/com.leonarndt.agentbench@* 2>/dev/null | head -1)/Harness~/agentbench.py
python3 $H replay           # reference solutions, no model
python3 $H run --trials 3   # Claude Code on every task
```

Before you change a tool's behaviour, description or parameters, run the suite on `main` and on your change, and
`compare` the two runs. See the bench README for the workflow.
