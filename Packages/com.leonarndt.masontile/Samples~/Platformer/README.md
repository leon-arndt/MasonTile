# Platformer Demo

A two-level side-scroller where every level is a text file.

**Open it:** Tools > MasonTile > Demos > Platformer Demo. On first import the setup script draws the tile
art and builds `Platformer Demo.unity` in this folder.

**Controls:** A/D or arrow keys to run, Space/W/Up to jump (hold for higher jumps), R to restart the level.

## How it works

- `Levels/PlatformerLevel1.txt` is painted into the scene so you can see and edit it in the Editor.
  `PlatformerLevel2.txt` and any further `PlatformerLevelN.txt` load at runtime when you reach the flag.
- Coins, spikes and the flag are tiles that `PlatformerPlayer` looks up under itself. There are no extra GameObjects.
- Solid tiles have `colliderType = Grid` and collide through a merged `CompositeCollider2D`.
- R rebuilds the current level from its text, which also brings back collected coins.

| Symbol | Tile | | Symbol | Tile |
|---|---|---|---|---|
| `G` | grass (solid) | | `c` | coin |
| `#` | dirt (solid) | | `F` | goal flag |
| `B` | brick (solid) | | `S` | player start |
| `*` | spikes | | `o` | cloud (decoration) |

## Try

- Edit a level in **Window > MasonTile > Tilemap Text View**, or edit the `.txt` and run
  **Tools > MasonTile > Demos > Rebuild Platformer Demo**.
- Add `PlatformerLevel3.txt` (same symbols, one `S`, one `F`) and rebuild to get a third level.
- Ask AI Assistant: *"Add a floating brick platform with three coins above the second spike pit on Level."*
