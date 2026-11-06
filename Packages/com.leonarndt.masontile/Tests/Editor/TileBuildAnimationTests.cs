using NUnit.Framework;
using UnityEngine;

namespace MasonTile.Tests
{
    public class TileBuildAnimationTests : TilemapFixture
    {
        const string Level = "#..#\n#ff#\n####";

        [Test]
        public void Rebuild_ClearsFirst_AndEndsWithTheOriginalTiles()
        {
            AsciiMap.Write(Map, Vector2Int.zero, Level, Legend);
            var build = TileBuildAnimation.Rebuild(Map, BuildOrder.SweepRight, seconds: 1f);

            Assert.That(TilemapOps.UsedBounds(Map).width, Is.Zero, "tilemap is cleared until the animation runs");
            build.Step(0);
            build.Step(10);

            Assert.That(build.IsDone, Is.True);
            Assert.That(ReadText(new RectInt(0, 0, 4, 3)), Is.EqualTo(Level));
            Assert.That(Map.GetTransformMatrix(new Vector3Int(0, 0, 0)), Is.EqualTo(Matrix4x4.identity), "no leftover pop transform");
        }

        [Test]
        public void Step_PlacesTilesGradually()
        {
            AsciiMap.Write(Map, Vector2Int.zero, Level, Legend);
            var build = TileBuildAnimation.Rebuild(Map, BuildOrder.SweepRight, seconds: 2f);
            var total = build.TotalCells;

            build.Step(0);
            build.Step(0.6);
            var midway = CountTiles();

            Assert.That(midway, Is.GreaterThan(0).And.LessThan(total));
            Assert.That(build.IsDone, Is.False);
        }

        [Test]
        public void OneLargeStep_PastTheEnd_FinishesEverything()
        {
            AsciiMap.Write(Map, Vector2Int.zero, Level, Legend);
            var build = TileBuildAnimation.Rebuild(Map, BuildOrder.SweepRight, seconds: 2f);

            build.Step(0);
            build.Step(5); // e.g. a long frame hitch

            Assert.That(build.IsDone, Is.True, "late tiles must not restart their pop at the time of the step");
            Assert.That(Map.GetTransformMatrix(new Vector3Int(3, 0, 0)), Is.EqualTo(Matrix4x4.identity));
        }

        [Test]
        public void SweepRight_PlacesLeftColumnBeforeRightColumn()
        {
            TilemapOps.Fill(Map, new RectInt(0, 0, 10, 1), Wall);
            var build = TileBuildAnimation.Rebuild(Map, BuildOrder.SweepRight, seconds: 2f);

            build.Step(0);
            build.Step(0.3);

            Assert.That(At(0, 0), Is.EqualTo(Wall));
            Assert.That(At(9, 0), Is.Null);
        }

        [Test]
        public void FromText_KeepsSpaces_ErasesDots()
        {
            TilemapOps.Fill(Map, new RectInt(0, 0, 3, 1), Water);
            var parsed = AsciiMap.Parse("# .", Legend);
            var build = TileBuildAnimation.FromText(Map, Vector2Int.zero, parsed, Legend, BuildOrder.SweepRight, 1f);

            build.Finish();

            Assert.That(At(0, 0), Is.EqualTo(Wall));
            Assert.That(At(1, 0), Is.EqualTo(Water), "space keeps the existing tile");
            Assert.That(At(2, 0), Is.Null, "'.' erases");
        }

        int CountTiles()
        {
            var n = 0;
            foreach (var p in Map.cellBounds.allPositionsWithin) if (Map.HasTile(p)) n++;
            return n;
        }
    }
}
