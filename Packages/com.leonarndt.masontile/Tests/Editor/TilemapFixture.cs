using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Tests
{
    /// <summary>Creates a Grid + Tilemap with a legend of '#'=wall, 'f'=floor, '~'=water; destroys it afterwards.</summary>
    public abstract class TilemapFixture
    {
        protected const string MapName = "MasonTileTest_Map";

        protected GameObject GridGo;
        protected Tilemap Map;
        protected TileLegend Legend;
        protected Tile Wall, Floor, Water;

        [SetUp]
        public virtual void SetUp()
        {
            GridGo = new GameObject("MasonTileTest_Grid", typeof(Grid));
            var mapGo = new GameObject(MapName, typeof(Tilemap), typeof(TilemapRenderer));
            mapGo.transform.SetParent(GridGo.transform);
            Map = mapGo.GetComponent<Tilemap>();

            Wall = MakeTile("wall");
            Floor = MakeTile("floor");
            Water = MakeTile("water");

            Legend = ScriptableObject.CreateInstance<TileLegend>();
            Legend.Set('#', "wall", Wall);
            Legend.Set('f', "floor", Floor);
            Legend.Set('~', "water", Water);

            mapGo.AddComponent<TileLegendBinding>().legend = Legend;
        }

        [TearDown]
        public virtual void TearDown()
        {
            Object.DestroyImmediate(GridGo);
            Object.DestroyImmediate(Legend);
            Object.DestroyImmediate(Wall);
            Object.DestroyImmediate(Floor);
            Object.DestroyImmediate(Water);
        }

        protected static Tile MakeTile(string name)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.name = name;
            return t;
        }

        protected TileBase At(int x, int y) => Map.GetTile(new Vector3Int(x, y, 0));

        protected string ReadText(RectInt area) => AsciiMap.Read(Map, area, Legend);
    }
}
