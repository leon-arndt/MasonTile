using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace MasonTile.Tests.Runtime
{
    /// <summary>
    /// PlayMode tests for building a tilemap from a text level when the scene starts.
    /// Uses only the runtime assembly, so it also runs on device via a player test build.
    /// </summary>
    public class TileLegendBindingTests
    {
        GameObject m_Grid;
        TileLegend m_Legend;
        Tile m_Wall, m_Floor;
        TextAsset m_Level;

        [SetUp]
        public void SetUp()
        {
            m_Grid = new GameObject("Grid", typeof(Grid));
            m_Wall = ScriptableObject.CreateInstance<Tile>();
            m_Wall.name = "wall";
            m_Floor = ScriptableObject.CreateInstance<Tile>();
            m_Floor.name = "floor";
            m_Legend = ScriptableObject.CreateInstance<TileLegend>();
            m_Legend.Set('#', "wall", m_Wall);
            m_Legend.Set('f', "floor", m_Floor);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(m_Grid);
            Object.Destroy(m_Legend);
            Object.Destroy(m_Wall);
            Object.Destroy(m_Floor);
            if (m_Level != null) Object.Destroy(m_Level);
        }

        /// <summary>Builds the binding on an inactive object, then activates it so Awake runs with fields set, as in a loaded scene.</summary>
        Tilemap Spawn(string levelText, Vector2Int origin = default, bool clearBeforeLoad = true, System.Action<Tilemap> beforeAwake = null)
        {
            var go = new GameObject("Level");
            go.SetActive(false);
            go.transform.SetParent(m_Grid.transform);
            var tilemap = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();

            var binding = go.AddComponent<TileLegendBinding>();
            binding.legend = m_Legend;
            if (levelText != null) binding.levelText = m_Level = new TextAsset(levelText);
            binding.levelOrigin = origin;
            binding.clearBeforeLoad = clearBeforeLoad;

            beforeAwake?.Invoke(tilemap);
            go.SetActive(true);
            return tilemap;
        }

        static TileBase At(Tilemap tilemap, int x, int y) => tilemap.GetTile(new Vector3Int(x, y, 0));

        [UnityTest]
        public IEnumerator Awake_BuildsLevelFromText_AndItPersistsIntoTheNextFrame()
        {
            var tilemap = Spawn("###\n#f#\n###", origin: new Vector2Int(10, 5));
            yield return null;

            Assert.That(At(tilemap, 10, 7), Is.EqualTo(m_Wall), "first text row is the top row");
            Assert.That(At(tilemap, 11, 6), Is.EqualTo(m_Floor));
            Assert.That(At(tilemap, 12, 5), Is.EqualTo(m_Wall), "bottom-right character");
            Assert.That(TilemapOps.UsedBounds(tilemap), Is.EqualTo(new RectInt(10, 5, 3, 3)));
        }

        [Test]
        public void Awake_ClearBeforeLoad_RemovesTilesAlreadyOnTheTilemap()
        {
            var tilemap = Spawn("##", beforeAwake: t => t.SetTile(new Vector3Int(-5, -5, 0), m_Floor));
            Assert.That(At(tilemap, -5, -5), Is.Null);
            Assert.That(At(tilemap, 1, 0), Is.EqualTo(m_Wall));
        }

        [Test]
        public void Awake_WithoutClear_KeepsExistingTilesWhereTextHasSpaces()
        {
            var tilemap = Spawn("# #", clearBeforeLoad: false, beforeAwake: t => t.SetTile(new Vector3Int(1, 0, 0), m_Floor));
            Assert.That(At(tilemap, 0, 0), Is.EqualTo(m_Wall));
            Assert.That(At(tilemap, 1, 0), Is.EqualTo(m_Floor), "space means keep");
            Assert.That(At(tilemap, 2, 0), Is.EqualTo(m_Wall));
        }

        [Test]
        public void Awake_InvalidText_LogsRowAndColumn_AndPaintsNothing()
        {
            LogAssert.Expect(LogType.Error, new Regex("Row 2, column 2: unknown symbol 'X'"));
            var tilemap = Spawn("##\n#X");
            Assert.That(TilemapOps.UsedBounds(tilemap).width, Is.Zero);
        }

        [Test]
        public void Awake_WithoutLevelText_LeavesTilemapAlone()
        {
            var tilemap = Spawn(null, beforeAwake: t => t.SetTile(Vector3Int.zero, m_Floor));
            Assert.That(At(tilemap, 0, 0), Is.EqualTo(m_Floor));
        }
    }
}
