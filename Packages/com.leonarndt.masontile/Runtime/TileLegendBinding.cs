using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile
{
    /// <summary>
    /// Tells MasonTile tools (text view, agent tools) which legend a tilemap uses.
    /// Optionally builds the tilemap from a text level file when Play mode starts. Handy for jam levels
    /// that are authored or tweaked as plain text.
    /// </summary>
    [RequireComponent(typeof(Tilemap))]
    [AddComponentMenu("MasonTile/Tile Legend Binding")]
    public class TileLegendBinding : MonoBehaviour
    {
        public TileLegend legend;

        [Header("Optional: build from text on Awake")]
        public TextAsset levelText;
        [Tooltip("Cell under the bottom-left character of the level text.")]
        public Vector2Int levelOrigin;
        public bool clearBeforeLoad = true;

        void Awake()
        {
            if (levelText != null && legend != null) LoadLevel();
        }

        public TileEdit LoadLevel()
        {
            var tilemap = GetComponent<Tilemap>();
            if (clearBeforeLoad) tilemap.ClearAllTiles();

            var parsed = AsciiMap.Parse(levelText.text, legend);
            if (!parsed.Success)
            {
                Debug.LogError($"[MasonTile] '{levelText.name}' has errors:\n{string.Join("\n", parsed.Errors)}", this);
                return TileEdit.None;
            }
            return AsciiMap.Write(tilemap, levelOrigin, parsed, legend);
        }
    }
}
