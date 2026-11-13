using System.Collections.Generic;
using System.Linq;
using AgentBench;
using UnityEngine;
using static MasonTile.Bench.TileWorld;

namespace MasonTile.Bench
{
    /// <summary>
    /// Creator tasks for the MasonTile agent tools. Each one is a request a level designer could type into AI
    /// Assistant, judged only on the tilemaps it leaves behind.
    ///
    /// Tags say what a task is meant to exercise:
    /// basic (one obvious call), read-first (the prompt leaves out positions the agent has to look up),
    /// feedback (a tool result warns about something the agent has to act on), coordinates (y-up vs. text
    /// rows), multi-tilemap, generation, planning (several dependent edits), and tool-gap (no single tool
    /// does it, so the agent has to combine them).
    /// </summary>
    public sealed class MasonTileSuite : BenchSuite
    {
        const string k_Paint = "MasonTile.PaintRect";
        const string k_Line = "MasonTile.DrawLine";
        const string k_Flood = "MasonTile.FloodFill";
        const string k_Write = "MasonTile.WriteText";
        const string k_Replace = "MasonTile.ReplaceTile";
        const string k_Cave = "MasonTile.GenerateCave";

        public override string Name => "masontile";
        public override string ToolIdPrefix => "MasonTile.";

        public override void Cleanup() => TileWorld.Cleanup();

        public override IEnumerable<BenchTask> CreateTasks()
        {
            yield return RoomFromSpec();
            yield return GrassToFloor();
            yield return CenterPool();
            yield return SealAndFlood();
            yield return DecorDiagonal();
            yield return StampTopLeft();
            yield return CaveWithCoins();
            yield return Corridor();
            yield return SpikesLeftHalf();

            // Harder tasks, so the suite has headroom: a stronger model or better tools should show up as more passes.
            yield return CoinInEveryRoom();
            yield return SealManyGaps();
            yield return MazeRoute();
            yield return MirrorRoom();
        }

        static BenchTask RoomFromSpec()
        {
            var room = Layout(2, 1, Room(9, 7));
            return new BenchTask("room-from-spec",
                "On the 'Ground' tilemap, build a room 9 cells wide and 7 cells tall with its bottom-left corner at x=2, y=1: " +
                "a wall border with floor inside.",
                () => Build(Ground, Decor),
                () =>
                {
                    var ground = Cells(Ground);
                    return new Checks()
                        .ExpectCells("the border is wall", ground, room.Where(kv => kv.Value == '#').Select(kv => kv.Key), _ => '#')
                        .ExpectCells("the inside is floor", ground, room.Where(kv => kv.Value == 'f').Select(kv => kv.Key), _ => 'f')
                        .ExpectCells("nothing is painted outside the room", ground, ground.Keys.Where(p => !room.ContainsKey(p)), _ => '.')
                        .Expect(Cells(Decor).Count == 0, "Decor is untouched")
                        .Result();
                },
                new ToolCall(k_Paint, new { tilemap = Ground, x = 2, y = 1, width = 9, height = 7, tile = "floor" }),
                new ToolCall(k_Paint, new { tilemap = Ground, x = 2, y = 1, width = 9, height = 7, tile = "wall", outlineOnly = true }))
            { Tags = new[] { "basic" } };
        }

        static BenchTask GrassToFloor()
        {
            const string rows =
                "############\n" +
                "#gggff~~fgg#\n" +
                "#gff#gg#ffg#\n" +
                "#ggggff~~gg#\n" +
                "############";
            var initial = Layout(0, 0, rows);
            var expected = initial.ToDictionary(kv => kv.Key, kv => kv.Value == 'g' ? 'f' : kv.Value);
            return new BenchTask("grass-to-floor",
                "Replace every grass tile on the 'Ground' tilemap with floor. Don't change anything else.",
                () =>
                {
                    Build(Ground);
                    Write(Ground, 0, 0, rows);
                },
                () =>
                {
                    var ground = Cells(Ground);
                    return new Checks()
                        .Expect(!ground.ContainsValue('g'), "no grass is left", () => $"{ground.Values.Count(v => v == 'g')} grass tile(s) remain")
                        .ExpectCells("former grass is floor", ground, initial.Where(kv => kv.Value == 'g').Select(kv => kv.Key), _ => 'f')
                        .ExpectExactly("everything else is unchanged", ground, expected)
                        .Result();
                },
                new ToolCall(k_Replace, new { tilemap = Ground, fromTile = "grass", toTile = "floor" }))
            { Tags = new[] { "basic" } };
        }

        static BenchTask CenterPool()
        {
            // Room 14x10 at (-6,3): floor x -5..6 (12 wide), y 4..11 (8 tall). A 4x2 pool centred on it is x -1..2, y 7..8.
            var initial = Layout(-6, 3, Room(14, 10));
            var pool = RectCells(-1, 7, 4, 2).ToList();
            var expected = new Dictionary<Vector2Int, char>(initial);
            foreach (var p in pool) expected[p] = '~';

            return new BenchTask("center-pool",
                "There is a room on the 'Ground' tilemap. Add a 4x2 water pool (4 wide, 2 tall) exactly in the middle of its floor.",
                () =>
                {
                    Build(Ground, Decor);
                    Write(Ground, -6, 3, Room(14, 10));
                },
                () =>
                {
                    var ground = Cells(Ground);
                    var strayWater = ground.Where(kv => kv.Value == '~' && !pool.Contains(kv.Key)).Select(kv => kv.Key).ToList();
                    return new Checks()
                        .ExpectCells("the pool covers the centre 4x2 cells", ground, pool, _ => '~')
                        .Expect(strayWater.Count == 0, "there is no water outside the pool",
                            () => $"{strayWater.Count} cell(s), e.g. " + string.Join(", ", strayWater.Take(5).Select(p => $"({p.x},{p.y})")))
                        .ExpectExactly("the rest of the room is unchanged", ground, expected)
                        .Expect(Cells(Decor).Count == 0, "Decor is untouched")
                        .Result();
                },
                new ToolCall(k_Paint, new { tilemap = Ground, x = -1, y = 7, width = 4, height = 2, tile = "water" }))
            { Tags = new[] { "read-first" } };
        }

        static BenchTask SealAndFlood()
        {
            // A 10x6 wall ring at (0,0) with a gap in the right wall at (9,3). Flooding before closing it leaks out.
            var ring = Layout(0, 0, Room(10, 6, '#', '.'));
            var gap = new Vector2Int(9, 3);
            var expected = Layout(0, 0, Room(10, 6, '#', '~'));

            return new BenchTask("seal-and-flood",
                "Fill the inside of the walled enclosure on the 'Ground' tilemap with water. Water must stay inside the " +
                "enclosure; if its wall has gaps, close them with wall first.",
                () =>
                {
                    Build(Ground);
                    Write(Ground, 0, 0, Room(10, 6, '#', '.'));
                    Map(Ground).SetTile(new Vector3Int(gap.x, gap.y, 0), null);
                },
                () =>
                {
                    var ground = Cells(Ground);
                    var inside = RectCells(1, 1, 8, 4).ToList();
                    return new Checks()
                        .ExpectCells("the gap in the wall is closed", ground, new[] { gap }, _ => '#')
                        .ExpectCells("the wall is intact", ground, ring.Keys, _ => '#')
                        .ExpectCells("the inside is water", ground, inside, _ => '~')
                        .ExpectExactly("nothing leaked outside the enclosure", ground, expected)
                        .Result();
                },
                new ToolCall(k_Paint, new { tilemap = Ground, x = gap.x, y = gap.y, width = 1, height = 1, tile = "wall" }),
                new ToolCall(k_Flood, new { tilemap = Ground, x = 1, y = 1, tile = "water" }))
            { Tags = new[] { "feedback" } };
        }

        static BenchTask DecorDiagonal()
        {
            var floor = Layout(0, 0, string.Join("\n", Enumerable.Repeat(new string('f', 8), 8)));
            var coins = Enumerable.Range(0, 8).ToDictionary(i => new Vector2Int(i, i), _ => 'c');

            return new BenchTask("decor-diagonal",
                "Place coins on the 'Decor' tilemap along the diagonal from (0,0) to (7,7), one per cell. Leave the 'Ground' tilemap as it is.",
                () =>
                {
                    Build(Ground, Decor);
                    Write(Ground, 0, 0, string.Join("\n", Enumerable.Repeat(new string('f', 8), 8)));
                },
                () => new Checks()
                    .ExpectExactly("Decor has a coin on each diagonal cell and nothing else", Cells(Decor), coins)
                    .ExpectExactly("Ground is unchanged", Cells(Ground), floor)
                    .Result(),
                new ToolCall(k_Line, new { tilemap = Decor, x1 = 0, y1 = 0, x2 = 7, y2 = 7, tile = "coin" }))
            { Tags = new[] { "multi-tilemap" } };
        }

        static BenchTask StampTopLeft()
        {
            const string stamp =
                "#####\n" +
                "#c.c#\n" +
                "#...#\n" +
                "##.##";
            // Top-left character at (3,10) and 4 rows, so the bottom-left character is at (3,7).
            var expected = Layout(3, 7, stamp);

            return new BenchTask("stamp-top-left",
                "Write this layout on the 'Ground' tilemap so that its TOP-left character lands on cell x=3, y=10 " +
                "('.' means an empty cell):\n" + stamp,
                () => Build(Ground),
                () =>
                {
                    var ground = Cells(Ground);
                    return new Checks()
                        .ExpectExactly("the layout is at the right place, top row at y=10", ground, expected)
                        .Result();
                },
                new ToolCall(k_Write, new { tilemap = Ground, x = 3, y = 7, rows = stamp }))
            { Tags = new[] { "coordinates" } };
        }

        static BenchTask CaveWithCoins()
        {
            var area = new RectInt(0, 0, 30, 18);
            return new BenchTask("cave-with-coins",
                "On the 'Ground' tilemap, generate a cave 30 cells wide and 18 tall with its bottom-left corner at (0,0), " +
                "using wall for the walls and leaving the open space empty. Then put exactly 3 coins on the 'Decor' tilemap, " +
                "each on an open (non-wall) cell inside the cave.",
                () => Build(Ground, Decor),
                () =>
                {
                    var ground = Cells(Ground);
                    var decor = Cells(Decor);
                    var border = RectCells(area.x, area.y, area.width, area.height)
                        .Where(p => p.x == area.xMin || p.y == area.yMin || p.x == area.xMax - 1 || p.y == area.yMax - 1).ToList();
                    var wallShare = ground.Count / (float)(area.width * area.height);
                    var coins = decor.Where(kv => kv.Value == 'c').Select(kv => kv.Key).ToList();
                    return new Checks()
                        .ExpectCells("the cave border is wall", ground, border, _ => '#')
                        .Expect(ground.Keys.All(area.Contains) && ground.Values.All(v => v == '#'), "Ground holds only wall, inside the area")
                        .Expect(wallShare > 0.25f && wallShare < 0.85f, "the area is a cave, not solid or empty", () => $"{wallShare:P0} of it is wall")
                        .Expect(coins.Count == 3 && decor.Count == 3, "Decor has exactly 3 coins and nothing else", () => $"{coins.Count} coin(s), {decor.Count} tile(s)")
                        .Expect(coins.Count > 0 && coins.All(p => area.Contains(p) && !ground.ContainsKey(p)), "every coin is on an open cell of the cave",
                            () => string.Join(", ", coins.Where(p => !area.Contains(p) || ground.ContainsKey(p)).Select(p => $"({p.x},{p.y})")))
                        .Result();
                },
                () => CaveWithCoinsReference(area))
            { Tags = new[] { "generation", "read-first" } };
        }

        static IEnumerable<ToolCall> CaveWithCoinsReference(RectInt area)
        {
            yield return new ToolCall(k_Cave, new { tilemap = Ground, x = area.x, y = area.y, width = area.width, height = area.height, seed = 7, wallTile = "wall" });
            var ground = Cells(Ground); // runs after the cave call, so it sees the generated cave
            foreach (var p in RectCells(area.x, area.y, area.width, area.height).Where(p => !ground.ContainsKey(p)).Take(3))
                yield return new ToolCall(k_Write, new { tilemap = Decor, x = p.x, y = p.y, rows = "c" });
        }

        static BenchTask Corridor()
        {
            // Rooms 7x7 at (0,0) and (14,0). The corridor runs along y=3 from x=7 to x=13 between them.
            var initial = Layout(0, 0, Room(7, 7));
            Layout(14, 0, Room(7, 7), initial);
            var doorways = new[] { new Vector2Int(6, 3), new Vector2Int(14, 3) };
            var corridor = Enumerable.Range(7, 7).Select(x => new Vector2Int(x, 3)).ToList();
            var sides = Enumerable.Range(7, 7).SelectMany(x => new[] { new Vector2Int(x, 2), new Vector2Int(x, 4) }).ToList();
            var expected = new Dictionary<Vector2Int, char>(initial);
            foreach (var p in doorways.Concat(corridor)) expected[p] = 'f';
            foreach (var p in sides) expected[p] = '#';

            return new BenchTask("corridor",
                "The 'Ground' tilemap has two rooms side by side. Connect them with a straight horizontal corridor along y=3: " +
                "floor 1 cell wide, with wall on both sides of it (y=2 and y=4). Open a doorway (floor) in each room's wall " +
                "where the corridor meets it. Don't change anything else.",
                () =>
                {
                    Build(Ground);
                    Write(Ground, 0, 0, Room(7, 7));
                    Write(Ground, 14, 0, Room(7, 7));
                },
                () =>
                {
                    var ground = Cells(Ground);
                    return new Checks()
                        .ExpectCells("both doorways are open", ground, doorways, _ => 'f')
                        .ExpectCells("the corridor is floor", ground, corridor, _ => 'f')
                        .ExpectCells("the corridor has wall on both sides", ground, sides, _ => '#')
                        .ExpectExactly("nothing else changed", ground, expected)
                        .Result();
                },
                new ToolCall(k_Paint, new { tilemap = Ground, x = 7, y = 2, width = 7, height = 3, tile = "wall" }),
                new ToolCall(k_Line, new { tilemap = Ground, x1 = 6, y1 = 3, x2 = 14, y2 = 3, tile = "floor" }))
            { Tags = new[] { "planning", "read-first" } };
        }

        static BenchTask SpikesLeftHalf()
        {
            var spikes = new[] { new Vector2Int(2, 1), new Vector2Int(5, 3), new Vector2Int(8, 0), new Vector2Int(12, 2), new Vector2Int(15, 4), new Vector2Int(18, 1) };
            var expected = RectCells(0, 0, 20, 5).ToDictionary(p => p, p => spikes.Contains(p) && p.x >= 10 ? '*' : 'f');

            return new BenchTask("spikes-left-half",
                "On the 'Ground' tilemap, replace the spikes whose x is 9 or less with floor. Keep the spikes at x=10 and beyond.",
                () =>
                {
                    Build(Ground);
                    Write(Ground, 0, 0, string.Join("\n", Enumerable.Repeat(new string('f', 20), 5)));
                    var tm = Map(Ground);
                    var spike = Legend(tm).TryGetTile('*', out var t) ? t : null;
                    foreach (var p in spikes) tm.SetTile(new Vector3Int(p.x, p.y, 0), spike);
                },
                () =>
                {
                    var ground = Cells(Ground);
                    return new Checks()
                        .ExpectCells("spikes left of x=10 are floor", ground, spikes.Where(p => p.x < 10), _ => 'f')
                        .ExpectCells("spikes from x=10 on are kept", ground, spikes.Where(p => p.x >= 10), _ => '*')
                        .ExpectExactly("nothing else changed", ground, expected)
                        .Result();
                },
                () => spikes.Where(p => p.x < 10)
                    .Select(p => new ToolCall(k_Paint, new { tilemap = Ground, x = p.x, y = p.y, width = 1, height = 1, tile = "floor" })))
            { Tags = new[] { "tool-gap" } };
        }

        static BenchTask CoinInEveryRoom()
        {
            // The map is 95 cells wide. ReadTilemap returns at most 64x64 cells (and says so), so the two rooms
            // right of x=63 only show up if the agent acts on that notice and reads the rest.
            var rooms = new[]
            {
                new RectInt(2, 3, 9, 7), new RectInt(18, 10, 11, 9), new RectInt(40, 2, 7, 5),
                new RectInt(70, 6, 13, 11), new RectInt(88, 1, 9, 7),
            };
            var initial = new Dictionary<Vector2Int, char>();
            foreach (var r in rooms) Layout(r.x, r.y, Room(r.width, r.height), initial);
            // Odd sizes, so each floor has one centre cell.
            var centres = rooms.Select(r => new Vector2Int(r.x + r.width / 2, r.y + r.height / 2)).ToList();

            return new BenchTask("coin-in-every-room",
                "Every room on the 'Ground' tilemap needs a coin at the exact centre of its floor. Put one coin per room " +
                "on the 'Decor' tilemap. Don't change 'Ground'.",
                () =>
                {
                    Build(Ground, Decor);
                    foreach (var r in rooms) Write(Ground, r.x, r.y, Room(r.width, r.height));
                },
                () =>
                {
                    var decor = Cells(Decor);
                    return new Checks()
                        .ExpectCells("every room has a coin at its centre", decor, centres, _ => 'c')
                        .ExpectExactly("there are no other coins", decor, centres.ToDictionary(p => p, _ => 'c'))
                        .ExpectExactly("Ground is unchanged", Cells(Ground), initial)
                        .Result();
                },
                () => centres.Select(p => new ToolCall(k_Write, new { tilemap = Decor, x = p.x, y = p.y, rows = "c" })))
            { Tags = new[] { "large-map", "read-first" } };
        }

        static BenchTask SealManyGaps()
        {
            // An L-shaped enclosure ('i' marks its inside) with three gaps: in the left wall, the top wall and the top of
            // the lower arm. The prompt asks for a closed wall but doesn't say where the gaps are: the agent has to find
            // them, or notice FloodFill's leak warning.
            const string shape =
                "##########......\n" +
                "#iiiiiiii#......\n" +
                "#iiiiiiii#......\n" +
                "#iiiiiiii#######\n" +
                "#iiiiiiiiiiiiii#\n" +
                "#iiiiiiiiiiiiii#\n" +
                "################";
            var gaps = new[] { new Vector2Int(0, 2), new Vector2Int(5, 6), new Vector2Int(12, 3) };
            var expected = Layout(0, 0, shape).ToDictionary(kv => kv.Key, kv => kv.Value == 'i' ? '~' : kv.Value);
            var inside = expected.Where(kv => kv.Value == '~').Select(kv => kv.Key).ToList();
            var walls = expected.Where(kv => kv.Value == '#').Select(kv => kv.Key).ToList();

            return new BenchTask("seal-many-gaps",
                "Fill the inside of the L-shaped walled area on the 'Ground' tilemap with water, and make sure its wall is " +
                "closed all the way round so the water can't get out. No water may end up outside it.",
                () =>
                {
                    Build(Ground);
                    Write(Ground, 0, 0, shape.Replace('i', '.'));
                    foreach (var g in gaps) Map(Ground).SetTile(new Vector3Int(g.x, g.y, 0), null);
                },
                () =>
                {
                    var ground = Cells(Ground);
                    return new Checks()
                        .ExpectCells("every gap in the wall is closed", ground, gaps, _ => '#')
                        .ExpectCells("the wall is intact", ground, walls, _ => '#')
                        .ExpectCells("the inside is water", ground, inside, _ => '~')
                        .ExpectExactly("nothing leaked outside the enclosure", ground, expected)
                        .Result();
                },
                () => gaps.Select(g => new ToolCall(k_Paint, new { tilemap = Ground, x = g.x, y = g.y, width = 1, height = 1, tile = "wall" }))
                    .Append(new ToolCall(k_Flood, new { tilemap = Ground, x = 1, y = 1, tile = "water" })))
            { Tags = new[] { "feedback", "read-first" } };
        }

        static BenchTask MazeRoute()
        {
            // A perfect maze (exactly one route between any two cells), so the shortest route is unique. Openings:
            // left side at (0,3), right side at (16,7).
            const string maze =
                "#################\n" +
                "#f#fffffffff#fff#\n" +
                "#f###f#####f#f###\n" +
                "#f#fff#fffff#ffff\n" +
                "#f#f###f#######f#\n" +
                "#f#fff#fffffffff#\n" +
                "#f###f#########f#\n" +
                "ffffff#fffff#fff#\n" +
                "#######f###f#f###\n" +
                "#fffffffff#fffff#\n" +
                "#################";
            var ground = Layout(0, 0, maze);
            var route = ShortestRoute(ground, new Vector2Int(0, 3), new Vector2Int(16, 7));
            var coins = route.ToDictionary(p => p, _ => 'c');

            return new BenchTask("maze-route",
                "The 'Ground' tilemap holds a maze with one opening in its left wall and one in its right wall. Mark the " +
                "shortest route between the two openings with coins on the 'Decor' tilemap: one coin on every floor cell " +
                "of the route, both openings included. Don't change 'Ground'.",
                () =>
                {
                    Build(Ground, Decor);
                    Write(Ground, 0, 0, maze);
                },
                () =>
                {
                    var decor = Cells(Decor);
                    return new Checks()
                        .ExpectCells("every cell of the route has a coin", decor, route, _ => 'c')
                        .ExpectExactly("there are no coins off the route", decor, coins)
                        .ExpectExactly("Ground is unchanged", Cells(Ground), ground)
                        .Result();
                },
                StampCells(Decor, route, 'c'))
            { Tags = new[] { "spatial", "planning", "read-first" } };
        }

        static BenchTask MirrorRoom()
        {
            const string room =
                "#########\n" +
                "#~~ff#gg#\n" +
                "#~fff#gg#\n" +
                "#ffffffg#\n" +
                "#*ffff#g#\n" +
                "#########";
            var mirrored = string.Join("\n", room.Split('\n').Select(r => new string(Enumerable.Reverse(r).ToArray())));
            var original = Layout(0, 0, room);
            var expected = Layout(12, 0, mirrored, new Dictionary<Vector2Int, char>(original));
            var copy = Layout(12, 0, mirrored);

            return new BenchTask("mirror-room",
                "The 'Ground' tilemap has one room. Build a mirror image of it, flipped left to right, with the copy's " +
                "left wall at x=12 and its bottom row level with the original's. Don't change the original.",
                () =>
                {
                    Build(Ground);
                    Write(Ground, 0, 0, room);
                },
                () =>
                {
                    var ground = Cells(Ground);
                    return new Checks()
                        .ExpectCells("the original is unchanged", ground, original.Keys, p => original[p])
                        .ExpectCells("the copy is the room flipped left to right, at x=12", ground, copy.Keys, p => copy[p])
                        .ExpectExactly("nothing else was painted", ground, expected)
                        .Result();
                },
                new ToolCall(k_Write, new { tilemap = Ground, x = 12, y = 0, rows = mirrored }))
            { Tags = new[] { "coordinates", "read-first" } };
        }

        /// <summary>Breadth-first search over floor cells ('f'), 4-connected. Returns the route from start to end, both included.</summary>
        static List<Vector2Int> ShortestRoute(Dictionary<Vector2Int, char> map, Vector2Int start, Vector2Int end)
        {
            var previous = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0 && !previous.ContainsKey(end))
            {
                var cell = queue.Dequeue();
                foreach (var step in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    var next = cell + step;
                    if (map.TryGetValue(next, out var s) && s == 'f' && !previous.ContainsKey(next))
                    {
                        previous[next] = cell;
                        queue.Enqueue(next);
                    }
                }
            }
            if (!previous.ContainsKey(end)) throw new System.InvalidOperationException("The maze has no route between its openings.");

            var route = new List<Vector2Int> { end };
            while (route[^1] != start) route.Add(previous[route[^1]]);
            route.Reverse();
            return route;
        }

        /// <summary>
        /// One WriteText call that puts <paramref name="symbol"/> on the given cells and leaves the rest unchanged
        /// (spaces). The text is cropped to the cells' bounding box: WriteText drops blank leading and trailing
        /// rows, which would shift a grid whose top or bottom row is all spaces.
        /// </summary>
        static ToolCall StampCells(string tilemap, IReadOnlyCollection<Vector2Int> cells, char symbol)
        {
            int minX = cells.Min(p => p.x), maxX = cells.Max(p => p.x), minY = cells.Min(p => p.y), maxY = cells.Max(p => p.y);
            var rows = Enumerable.Range(0, maxY - minY + 1)
                .Select(r => new string(Enumerable.Range(minX, maxX - minX + 1)
                    .Select(x => cells.Contains(new Vector2Int(x, maxY - r)) ? symbol : ' ').ToArray()));
            return new ToolCall(k_Write, new { tilemap, x = minX, y = minY, rows = string.Join("\n", rows) });
        }
    }
}
