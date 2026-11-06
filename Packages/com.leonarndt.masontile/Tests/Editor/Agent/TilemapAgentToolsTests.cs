using System;
using System.Threading;
using System.Threading.Tasks;
using MasonTile.Agent;
using MasonTile.Editor;
using NUnit.Framework;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Tests.Agent
{
    /// <summary>
    /// Integration tests for the agent tools: each test calls a tool the way AI Assistant does,
    /// with a ToolExecutionContext and a permission provider, and checks the tilemap and the result
    /// the agent would see. Complements the contract test, which only checks tool signatures.
    /// </summary>
    public class TilemapAgentToolsTests : TilemapFixture
    {
        TestToolPermissions m_Permissions;

        public override void SetUp()
        {
            base.SetUp();
            m_Permissions = new TestToolPermissions();
        }

        ToolExecutionContext Context(string toolId)
        {
            var call = new ToolExecutionContext.CallInfo(toolId, Guid.NewGuid(), null);
            return new ToolExecutionContext(null, call,
                new ToolCallPermissions(call, m_Permissions, CancellationToken.None), default);
        }

        // Permission checks complete synchronously here, so the tool's task is already finished.
        static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();

        [Test]
        public void PaintRect_PaintsTiles_AndReturnsSnapshotForSelfCheck()
        {
            var result = Run(TilemapAgentTools.PaintRect(Context("MasonTile.PaintRect"), MapName, 0, 0, 3, 2, "wall"));

            Assert.That(result.CellsChanged, Is.EqualTo(6));
            Assert.That(result.Snapshot.Rows, Is.EqualTo(".....\n.###.\n.###.\n....."));
            Assert.That(At(2, 1), Is.EqualTo(Wall));
        }

        [Test]
        public void EditingTools_AskForModifyPermissionOnTheTargetTilemap()
        {
            Run(TilemapAgentTools.DrawLine(Context("MasonTile.DrawLine"), MapName, 0, 0, 4, 0, "#"));

            Assert.That(m_Permissions.ObjectChecks, Has.Count.EqualTo(1));
            var check = m_Permissions.ObjectChecks[0];
            Assert.That(check.Operation, Is.EqualTo(PermissionItemOperation.Modify));
            Assert.That(check.Type, Is.EqualTo(typeof(Tilemap)));
            Assert.That(check.Target, Is.SameAs(Map));
        }

        [Test]
        public void PermissionDenied_LeavesTilemapUntouched()
        {
            m_Permissions.Deny = true;

            Assert.Throws<UnauthorizedAccessException>(() =>
                Run(TilemapAgentTools.PaintRect(Context("MasonTile.PaintRect"), MapName, 0, 0, 5, 5, "#")));
            Assert.That(TilemapOps.UsedBounds(Map).width, Is.Zero);
        }

        [Test]
        public void UnknownTilemap_FailsWithAvailableNames_BeforeAskingPermission()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                Run(TilemapAgentTools.FloodFill(Context("MasonTile.FloodFill"), "Background", 0, 0, "~")));

            Assert.That(ex.Message, Does.Contain("No Tilemap named 'Background'").And.Contain(MapName));
            Assert.That(m_Permissions.ObjectChecks, Is.Empty, "the user should not be asked about a call that cannot succeed");
        }

        [Test]
        public void UnknownTile_FailsAsToolError_WithLegendInMessage()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Run(TilemapAgentTools.PaintRect(Context("MasonTile.PaintRect"), MapName, 0, 0, 2, 2, "lava")));

            Assert.That(ex.Message, Does.Contain("Unknown tile 'lava'").And.Contain("'~'=water"));
            Assert.That(TilemapOps.UsedBounds(Map).width, Is.Zero);
        }

        [Test]
        public void WriteText_AcceptsTypicalLlmOutput_WithCodeFences()
        {
            const string llmRows = "```\n####\n#~~#\n####\n```";
            var result = Run(TilemapAgentTools.WriteText(Context("MasonTile.WriteText"), MapName, 0, 0, llmRows));

            Assert.That(result.CellsChanged, Is.EqualTo(12));
            Assert.That(ReadText(new RectInt(0, 0, 4, 3)), Is.EqualTo("####\n#~~#\n####"));
        }

        [Test]
        public void GenerateCave_WithDefaultArguments_FillsAreaWithWallAndEmpty()
        {
            var result = Run(TilemapAgentTools.GenerateCave(Context("MasonTile.GenerateCave"), MapName, 0, 0, 20, 12, seed: 5, wallTile: "#"));

            Assert.That(result.CellsChanged, Is.GreaterThan(0));
            Assert.That(TilemapOps.UsedBounds(Map), Is.EqualTo(new RectInt(0, 0, 20, 12)), "the border is always wall");
            var counts = TilemapOps.CountTiles(Map, new RectInt(0, 0, 20, 12));
            Assert.That(counts.Keys, Is.EquivalentTo(new[] { Wall }), "default floor is 'empty'");
        }

        [Test]
        public void ReplaceTile_SwapsAcrossUsedArea()
        {
            TilemapCommands.WriteText(Map, Legend, Vector2Int.zero, "#f#f\nffff");
            var result = Run(TilemapAgentTools.ReplaceTile(Context("MasonTile.ReplaceTile"), MapName, "floor", "~"));

            Assert.That(result.CellsChanged, Is.EqualTo(6));
            Assert.That(ReadText(new RectInt(0, 0, 4, 2)), Is.EqualTo("#~#~\n~~~~"));
        }

        [Test]
        public void ReadTools_ReturnWhatEditToolsWrote()
        {
            Run(TilemapAgentTools.PaintRect(Context("MasonTile.PaintRect"), MapName, 2, 1, 3, 2, "#", outlineOnly: true));

            var read = TilemapAgentTools.ReadTilemap(MapName);
            Assert.That(read.Snapshot.OriginX, Is.EqualTo(2));
            Assert.That(read.Snapshot.OriginY, Is.EqualTo(1));
            Assert.That(read.Snapshot.Rows, Is.EqualTo("###\n###"));

            var paged = TilemapAgentTools.ReadTilemap(MapName, x: 2, y: 2, width: 2, height: 1);
            Assert.That(paged.Snapshot.Rows, Is.EqualTo("##"));

            Assert.That(TilemapAgentTools.ListTilemaps().Message, Does.Contain(MapName).And.Contain("'#'(wall)=6"));
        }

        [Test]
        public void AgentEdits_AreUndoableLikeUserEdits()
        {
            Run(TilemapAgentTools.PaintRect(Context("MasonTile.PaintRect"), MapName, 0, 0, 3, 3, "#"));
            Undo.PerformUndo();
            Assert.That(TilemapOps.UsedBounds(Map).width, Is.Zero);
        }
    }
}
