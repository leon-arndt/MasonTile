using NUnit.Framework;
using UnityEngine;

namespace TileKit.Tests
{
    public class AsciiMapTests : TilemapFixture
    {
        [Test]
        public void Write_ThenRead_RoundTrips()
        {
            const string text = "####\n#f~#\n####";
            AsciiMap.Write(Map, new Vector2Int(3, -2), text, Legend);
            Assert.That(ReadText(new RectInt(3, -2, 4, 3)), Is.EqualTo(text));
        }

        [Test]
        public void Write_FirstRowIsTop_OriginIsBottomLeft()
        {
            AsciiMap.Write(Map, Vector2Int.zero, "#.\n.~", Legend);
            Assert.That(At(0, 1), Is.EqualTo(Wall), "top-left char lands at y = height-1");
            Assert.That(At(1, 0), Is.EqualTo(Water), "bottom-right char lands at the origin row");
            Assert.That(At(0, 0), Is.Null);
        }

        [Test]
        public void Write_SpaceKeepsExistingCell_DotErases()
        {
            AsciiMap.Write(Map, Vector2Int.zero, "###", Legend);
            var edit = AsciiMap.Write(Map, Vector2Int.zero, " .", Legend);
            Assert.That(At(0, 0), Is.EqualTo(Wall));
            Assert.That(At(1, 0), Is.Null);
            Assert.That(At(2, 0), Is.EqualTo(Wall), "short rows are padded with keep");
            Assert.That(edit.CellsChanged, Is.EqualTo(1));
        }

        [Test]
        public void Write_SameTextTwice_SecondWriteChangesNothing()
        {
            AsciiMap.Write(Map, Vector2Int.zero, "#f\nf#", Legend);
            Assert.That(AsciiMap.Write(Map, Vector2Int.zero, "#f\nf#", Legend).CellsChanged, Is.Zero);
        }

        [Test]
        public void Parse_StripsCodeFencesCrLfAndBlankEdgeLines()
        {
            var parsed = AsciiMap.Parse("\r\n```text\r\n##\r\n#f\r\n```\r\n\r\n", Legend);
            Assert.That(parsed.Success, Is.True, string.Join(" ", parsed.Errors));
            Assert.That(parsed.Rows, Is.EqualTo(new[] { "##", "#f" }));
            Assert.That(parsed.Width, Is.EqualTo(2));
        }

        [Test]
        public void Parse_UnknownSymbol_ReportsRowColumnAndLegend()
        {
            var parsed = AsciiMap.Parse("##\n#X", Legend);
            Assert.That(parsed.Success, Is.False);
            Assert.That(parsed.Errors[0], Does.Contain("Row 2, column 2").And.Contain("'X'"));
            Assert.That(parsed.Errors[^1], Does.Contain("'#'=wall"));
        }

        [Test]
        public void Parse_QuestionMark_IsRejectedWithExplanation()
        {
            var parsed = AsciiMap.Parse("#?", Legend);
            Assert.That(parsed.Errors[0], Does.Contain("missing from the legend"));
        }

        [Test]
        public void Read_TileNotInLegend_ShowsQuestionMark()
        {
            var stray = MakeTile("stray");
            Map.SetTile(Vector3Int.zero, stray);
            Assert.That(ReadText(new RectInt(0, 0, 2, 1)), Is.EqualTo("?."));
            Object.DestroyImmediate(stray);
        }
    }
}
