# Top-Down Demo

A small village in the style of top-down RPGs and farming games: grid movement, houses, a garden,
a pond with a bridge, and signs to read. The whole map is one text file.

**Open it:** Tools > MasonTile > Demos > Top-Down Demo. On first import the setup script draws the tile
art and builds `Top-Down Demo.unity` in this folder.

**Controls:** WASD or arrow keys to walk, E/Space/Enter to read signs and knock on doors.

## How it works

- `Levels/TopDownVillage.txt` is painted into the scene.
- Walkable or blocked comes from each tile's `colliderType` (None = walk, Grid = blocked).
  `TopDownPlayer` checks the next cell before stepping, so no physics is involved.
- Signs get their texts in reading order (top row first, left to right) from `TopDownPlayer.signTexts`.
- On Play the village ripples into place around the player with `TileBuildAnimation` (turn off `Animate Map Build`).

| Symbol | Tile | | Symbol | Tile |
|---|---|---|---|---|
| `g` | grass | | `R` | roof (blocked) |
| `,` | tall grass | | `W` | wall (blocked) |
| `f` | flowers | | `w` | window (blocked) |
| `_` | path | | `D` | door (blocked, interactable) |
| `S` | player start | | `s` | sign (blocked, interactable) |
| `B` | bridge | | `#` | fence (blocked) |
| `~` | water (blocked) | | `T` | tree (blocked) |
| `r` | rock (blocked) | | | |

## Try

- Edit the map in **Window > MasonTile > Tilemap Text View**, or edit the `.txt` and run
  **Tools > MasonTile > Demos > Rebuild Top-Down Demo**.
- Ask AI Assistant: *"Add a third house east of the pond with a path to the main road on Village."*
