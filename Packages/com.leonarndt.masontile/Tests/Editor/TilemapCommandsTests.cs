using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using MasonTile.Editor;
using UnityEditor;
using UnityEngine;

namespace MasonTile.Tests
{
    /// <summary>
    /// Tests for the structured command layer that the editor UI and agent tools share. These
    /// assert what a caller (human or LLM) sees: success flags, change counts, snapshots, and
    /// error messages that say how to fix the call.
    /// </summary>
    public class TilemapCommandsTests : TilemapFixture
    {
        [Test]
        public void Fill_ByName_ReturnsCountAndSnapshotWithMargin()
        {
            var r = TilemapCommands.Fill(MapName, new RectInt(0, 0, 2, 2), "wall");
            Assert.That(r.Success, Is.True, r.Message);
            Assert.That(r.CellsChanged, Is.EqualTo(4));
            Assert.That(r.Snapshot.OriginX, Is.EqualTo(-1));
            Assert.That(r.Snapshot.OriginY, Is.EqualTo(-1));
            Assert.That(r.Snapshot.Rows, Is.EqualTo("....\n.##.\n.##.\n...."));
        }

        [Test]
        public void Fill_Repeated_ReportsZeroChangedAndNoSnapshot()
        {
            TilemapCommands.Fill(MapName, new RectInt(0, 0, 2, 2), "#");
            var r = TilemapCommands.Fill(MapName, new RectInt(0, 0, 2, 2), "#");
            Assert.That(r.Success, Is.True);
            Assert.That(r.CellsChanged, Is.Zero);
            Assert.That(r.Snapshot, Is.Null);
            Assert.That(r.Message, Does.Contain("0 cells changed"));
        }

        [Test]
        public void UnknownTilemap_ErrorListsAvailableTilemaps()
        {
            var r = TilemapCommands.Fill("Nope", new RectInt(0, 0, 1, 1), "#");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Message, Does.Contain("No Tilemap named 'Nope'").And.Contain(MapName));
        }

        [Test]
        public void TilemapLookup_AcceptsHierarchyPathAndIgnoresCase()
        {
            Assert.That(TilemapCommands.TryFindTilemap("MasonTileTest_Grid/" + MapName, out var a, out _), Is.True);
            Assert.That(TilemapCommands.TryFindTilemap(MapName.ToUpperInvariant(), out var b, out _), Is.True);
            Assert.That(a, Is.SameAs(Map));
            Assert.That(b, Is.SameAs(Map));
        }

        [Test]
        public void UnknownTile_ErrorIncludesLegend_AndNothingChanges()
        {
            var r = TilemapCommands.Fill(MapName, new RectInt(0, 0, 2, 2), "lava");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Message, Does.Contain("'#'=wall"));
            Assert.That(TilemapOps.UsedBounds(Map).width, Is.Zero);
        }

        [TestCase(0, 3)]
        [TestCase(3, -1)]
        public void InvalidArea_IsRejected(int width, int height)
        {
            var r = TilemapCommands.Fill(MapName, new RectInt(0, 0, width, height), "#");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Message, Does.Contain("at least 1"));
        }

        [Test]
        public void OversizedArea_IsRejected()
        {
            var r = TilemapCommands.Fill(MapName, new RectInt(0, 0, 1000, 1000), "#");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Message, Does.Contain("limit"));
        }

        [Test]
        public void WriteText_ParseErrors_AreReturnedNotThrown_AndNothingChanges()
        {
            var r = TilemapCommands.WriteText(MapName, Vector2Int.zero, "##\n#Z");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Message, Does.Contain("Row 2, column 2"));
            Assert.That(TilemapOps.UsedBounds(Map).width, Is.Zero);
        }

        [Test]
        public void FloodFill_OpenRegion_WarnsAboutLeak()
        {
            TilemapCommands.WriteText(MapName, Vector2Int.zero, "###\n#.#\n#.#");
            var r = TilemapCommands.FloodFill(MapName, new Vector2Int(1, 0), "~");
            Assert.That(r.Success, Is.True);
            Assert.That(r.Message, Does.Contain("not enclosed"));
        }

        [Test]
        public void Read_LargeArea_IsTruncatedFromTopLeft()
        {
            var size = TilemapCommands.MaxSnapshotSize + 10;
            TilemapOps.Fill(Map, new RectInt(0, 0, size, size), Wall);
            var r = TilemapCommands.Read(MapName);
            Assert.That(r.Snapshot.Truncated, Is.True);
            Assert.That(r.Snapshot.Width, Is.EqualTo(TilemapCommands.MaxSnapshotSize));
            Assert.That(r.Snapshot.OriginY, Is.EqualTo(10), "keeps the top rows");
            Assert.That(r.Message, Does.Contain("Truncated"));
        }

        [Test]
        public void GenerateCave_SameSeed_ProducesSameTiles()
        {
            var first = TilemapCommands.GenerateCave(MapName, new RectInt(0, 0, 20, 12), 7, "#", "f");
            var text1 = ReadText(new RectInt(0, 0, 20, 12));
            Map.ClearAllTiles();
            TilemapCommands.GenerateCave(MapName, new RectInt(0, 0, 20, 12), 7, "#", "f");
            Assert.That(first.Success, Is.True, first.Message);
            Assert.That(ReadText(new RectInt(0, 0, 20, 12)), Is.EqualTo(text1));
        }

        [Test]
        public void Commands_AreUndoable()
        {
            TilemapCommands.Fill(MapName, new RectInt(0, 0, 3, 3), "#");
            TilemapCommands.Fill(MapName, new RectInt(1, 1, 1, 1), "~");
            Undo.PerformUndo();
            Assert.That(At(1, 1), Is.EqualTo(Wall), "second command undone");
            Undo.PerformUndo();
            Assert.That(At(0, 0), Is.Null, "first command undone");
        }

        [Test]
        public void List_DescribesTilemapLegendAndCounts()
        {
            TilemapCommands.WriteText(MapName, Vector2Int.zero, "##~");
            var r = TilemapCommands.List();
            Assert.That(r.Message, Does.Contain(MapName).And.Contain("'#'(wall)=2").And.Contain("'~'(water)=1"));
        }
    }

    /// <summary>
    /// Contract tests for the agent tool definitions, mirroring what the AI Assistant requires at
    /// registration time. Catches a broken tool before an agent ever sees it.
    /// </summary>
    public class AgentToolContractTests
    {
        static MethodInfo[] ToolMethods()
        {
            // Looked up by name: the test assembly must not depend on the optional AI Assistant package.
            var tools = Type.GetType("MasonTile.Agent.TilemapAgentTools, MasonTile.Agent");
            if (tools == null) return Array.Empty<MethodInfo>();
            return tools.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "AgentToolAttribute"))
                .ToArray();
        }

        [Test]
        public void AgentTools_AreRegisteredWithValidSignatures()
        {
            var methods = ToolMethods();
            if (methods.Length == 0) Assert.Ignore("MasonTile.Agent is not compiled (AI Assistant package missing).");

            Assert.That(methods.Length, Is.GreaterThanOrEqualTo(8));
            foreach (var m in methods)
            {
                var attr = m.GetCustomAttributes().First(a => a.GetType().Name == "AgentToolAttribute");
                var id = (string)attr.GetType().GetField("Id").GetValue(attr);
                var description = (string)attr.GetType().GetField("Description").GetValue(attr);
                Assert.That(id, Does.StartWith("MasonTile."), m.Name);
                Assert.That(description.Length, Is.GreaterThan(40), $"{m.Name}: describe the tool well enough for an LLM");

                foreach (var p in m.GetParameters().Where(p => p.ParameterType.Name != "ToolExecutionContext"))
                {
                    var hasAttr = p.GetCustomAttributes().Any(a => a.GetType().Name == "ToolParameterAttribute");
                    Assert.That(hasAttr, $"{m.Name}.{p.Name} lacks [ToolParameter]; the Assistant would silently drop the tool.");
                }
            }

            var ids = methods.Select(m => m.Name).ToList();
            Assert.That(ids, Is.Unique);
        }
    }
}
