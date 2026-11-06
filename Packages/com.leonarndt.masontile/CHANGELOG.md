# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Samples: **Platformer Demo** (two levels, coins, spikes, goal flag; levels after the first load from text at
  runtime) and **Top-Down Demo** (village with grid movement, houses, garden, pond, readable signs). Samples ship
  only text and code and generate their art, tiles and scene on import.
- Tools > MasonTile > Demos: import or open each sample, plus Rebuild items. Opening or rebuilding a demo plays its
  build animation in the Scene view.
- `TileBuildAnimation` (runtime): places tiles over time with a pop-in effect, in sweep, bottom-up, ripple or random
  order. `TileBuildPreview` plays it in Edit mode. The demos also build their levels this way in Play mode.
- `PixelCanvas` and `TileSetBuilder`: define placeholder tilesets in code. `SceneBuilder`: build a scene from code
  without disturbing the open scenes (rebuilding a scene that is open replaces it in place).

### Changed
- Placeholder tiles use `TileSetBuilder`; wall and water now have grid colliders.

### Removed
- Tools > MasonTile > Create Demo Tilemap In Scene (replaced by the samples).

### Fixed
- `TileLegend` clears its lookup cache when Unity reloads the asset.

## [0.1.1] - 2026-11-06

### Added
- Agent integration tests (`MasonTile.Agent.Tests`): each tool is invoked with a `ToolExecutionContext`
  and a test permission provider, covering permission requests, denial, error messages, and undo.
- PlayMode tests (`MasonTile.Tests.Runtime`) for `TileLegendBinding` building a level on Awake.

### Fixed
- The `v0.1.0` tag points at a commit that only contains the file moves of the rename, so it does not
  install. Use `v0.1.1` or later.

## [0.1.0] - 2026-11-05

### Added
- `TilemapOps`: fill, outline, line, bounded flood fill, replace, used bounds, tile counts.
- `AsciiMap` and `TileLegend`: read and write tilemap regions as character grids.
- `CaveGenerator`: seeded cellular-automata caves.
- `TileLegendBinding`: assigns a legend to a Tilemap and can build it from a text level on Play.
- `TilemapCommands`: validated, undoable command layer shared by editor UI and agent tools.
- MasonTile Text View window, legend builder, and placeholder tile generator.
- Eight Unity AI Assistant tools, compiled only when `com.unity.ai.assistant` is installed.
- EditMode tests for operations, text parsing, commands, undo, and agent tool contracts.
