using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MasonTile.Tests
{
    public class TilemapOpsTests : TilemapFixture
    {
        [Test]
        public void Fill_PaintsEveryCellAndReportsBounds()
        {
            var edit = TilemapOps.Fill(Map, new RectInt(1, 1, 3, 2), Wall);
            Assert.That(edit.CellsChanged, Is.EqualTo(6));
            Assert.That(edit.ChangedBounds, Is.EqualTo(new RectInt(1, 1, 3, 2)));
            Assert.That(ReadText(new RectInt(0, 0, 5, 4)), Is.EqualTo(".....\n.###.\n.###.\n....."));
        }

        [Test]
        public void Fill_WithNull_Erases()
        {
            TilemapOps.Fill(Map, new RectInt(0, 0, 3, 3), Wall);
            TilemapOps.Fill(Map, new RectInt(1, 1, 1, 1), null);
            Assert.That(ReadText(new RectInt(0, 0, 3, 3)), Is.EqualTo("###\n#.#\n###"));
        }

        [Test]
        public void Outline_PaintsBorderOnly()
        {
            var edit = TilemapOps.Outline(Map, new RectInt(0, 0, 4, 3), Wall);
            Assert.That(edit.CellsChanged, Is.EqualTo(10));
            Assert.That(ReadText(new RectInt(0, 0, 4, 3)), Is.EqualTo("####\n#..#\n####"));
        }

        [Test]
        public void Outline_OneCellWide_DoesNotDoubleCount()
        {
            Assert.That(TilemapOps.Outline(Map, new RectInt(0, 0, 1, 4), Wall).CellsChanged, Is.EqualTo(4));
        }

        [TestCase(0, 0, 4, 0, 5)]
        [TestCase(0, 0, 0, -3, 4)]
        [TestCase(0, 0, 3, 3, 4)]
        [TestCase(0, 0, 4, 2, 5)]
        public void LineCells_IsContinuousAndInclusive(int x1, int y1, int x2, int y2, int expectedCount)
        {
            var cells = TilemapOps.LineCells(new Vector2Int(x1, y1), new Vector2Int(x2, y2)).ToList();
            Assert.That(cells.Count, Is.EqualTo(expectedCount));
            Assert.That(cells[0], Is.EqualTo(new Vector2Int(x1, y1)));
            Assert.That(cells[^1], Is.EqualTo(new Vector2Int(x2, y2)));
            for (var i = 1; i < cells.Count; i++)
            {
                var d = cells[i] - cells[i - 1];
                Assert.That(Mathf.Abs(d.x) <= 1 && Mathf.Abs(d.y) <= 1, $"gap between {cells[i - 1]} and {cells[i]}");
            }
        }

        [Test]
        public void FloodFill_FillsEnclosedRoomOnly()
        {
            AsciiMap.Write(Map, Vector2Int.zero, "#####\n#...#\n#...#\n#####", Legend);
            var edit = TilemapOps.FloodFill(Map, new Vector2Int(2, 1), Water, new RectInt(-1, -1, 7, 6), out var touched);
            Assert.That(edit.CellsChanged, Is.EqualTo(6));
            Assert.That(touched, Is.False);
            Assert.That(ReadText(new RectInt(0, 0, 5, 4)), Is.EqualTo("#####\n#~~~#\n#~~~#\n#####"));
        }

        [Test]
        public void FloodFill_OpenRegion_IsClampedAndReportsTouchedLimit()
        {
            AsciiMap.Write(Map, Vector2Int.zero, "#.#", Legend);
            var limit = new RectInt(0, 0, 3, 3);
            var edit = TilemapOps.FloodFill(Map, new Vector2Int(1, 0), Water, limit, out var touched);
            Assert.That(touched, Is.True);
            Assert.That(edit.CellsChanged, Is.EqualTo(7), "3x3 minus the two walls");
        }

        [Test]
        public void FloodFill_SameTile_IsNoOp()
        {
            Map.SetTile(Vector3Int.zero, Wall);
            Assert.That(TilemapOps.FloodFill(Map, Vector2Int.zero, Wall, new RectInt(0, 0, 5, 5), out _).CellsChanged, Is.Zero);
        }

        [Test]
        public void Replace_SwapsOnlyMatchingTiles()
        {
            AsciiMap.Write(Map, Vector2Int.zero, "#f#f", Legend);
            var edit = TilemapOps.Replace(Map, new RectInt(0, 0, 4, 1), Floor, Water);
            Assert.That(edit.CellsChanged, Is.EqualTo(2));
            Assert.That(ReadText(new RectInt(0, 0, 4, 1)), Is.EqualTo("#~#~"));
        }

        [Test]
        public void UsedBounds_IsTightAndEmptyWhenNoTiles()
        {
            Assert.That(TilemapOps.UsedBounds(Map).width, Is.Zero);
            Map.SetTile(new Vector3Int(-2, 5, 0), Wall);
            Map.SetTile(new Vector3Int(3, 1, 0), Wall);
            Assert.That(TilemapOps.UsedBounds(Map), Is.EqualTo(new RectInt(-2, 1, 6, 5)));
        }

        [Test]
        public void CaveGenerator_IsDeterministicPerSeed_WithSolidBorder()
        {
            var a = CaveGenerator.Generate(30, 20, seed: 42);
            var b = CaveGenerator.Generate(30, 20, seed: 42);
            var c = CaveGenerator.Generate(30, 20, seed: 43);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
            for (var x = 0; x < 30; x++) Assert.That(a[x, 0] && a[x, 19], $"border column {x}");
            for (var y = 0; y < 20; y++) Assert.That(a[0, y] && a[29, y], $"border row {y}");
        }

        [Test]
        public void Legend_Validate_FlagsDuplicatesAndMissingTiles()
        {
            Legend.Set('w', "wall again", Wall);
            Legend.Set('n', "nothing", null);
            var problems = Legend.Validate();
            Assert.That(problems, Has.Some.Contains("already mapped"));
            Assert.That(problems, Has.Some.Contains("no tile assigned"));
        }

        [Test]
        public void Legend_TryResolve_AcceptsSymbolNameAndEmpty()
        {
            Assert.That(Legend.TryResolve("#", out var t1, out _) && t1 == Wall);
            Assert.That(Legend.TryResolve("WATER", out var t2, out _) && t2 == Water);
            Assert.That(Legend.TryResolve("empty", out var t3, out _) && t3 == null);
            Assert.That(Legend.TryResolve("lava", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("Unknown tile 'lava'").And.Contain("'~'=water"));
        }
    }
}
