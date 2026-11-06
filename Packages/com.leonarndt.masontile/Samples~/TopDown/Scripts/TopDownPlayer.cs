using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Samples.TopDown
{
    /// <summary>
    /// Grid-based top-down movement. Whether a cell blocks movement comes from the tile itself
    /// (<see cref="Tile.ColliderType"/> other than None), so the map text alone defines where you can walk.
    /// Facing a sign or door and pressing interact shows a text box.
    /// </summary>
    public class TopDownPlayer : MonoBehaviour
    {
        public Tilemap level;
        public TileBase startTile;
        public TileBase signTile;
        public TileBase doorTile;

        [Tooltip("Facing sprites in the order down, up, left, right.")]
        public Sprite[] facingSprites = new Sprite[4];
        public float tilesPerSecond = 5f;

        [Tooltip("Texts for the signs in reading order: top row first, left to right.")]
        [TextArea(2, 4)] public string[] signTexts;
        [TextArea(2, 4)] public string doorText = "It's locked. Nobody seems to be home.";

        [Header("Map build animation")]
        [Tooltip("Build the map tile by tile, rippling out from the player, when the scene starts.")]
        public bool animateMapBuild = true;
        public float buildSeconds = 1.6f;

        SpriteRenderer m_Renderer;
        readonly Dictionary<Vector3Int, string> m_Signs = new();
        Vector3Int m_Cell;
        Vector3 m_Target;
        bool m_Moving;
        Vector2Int m_Facing = Vector2Int.down;
        string m_Message;
        bool m_Building;

        void Awake() => m_Renderer = GetComponent<SpriteRenderer>();

        void Start()
        {
            var b = level.cellBounds;
            var signIndex = 0;
            var foundStart = false;
            for (var y = b.yMax - 1; y >= b.yMin; y--)
            for (var x = b.xMin; x < b.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                var tile = level.GetTile(cell);
                if (tile == startTile && !foundStart)
                {
                    m_Cell = cell;
                    foundStart = true;
                }
                else if (tile == signTile)
                {
                    m_Signs[cell] = signTexts != null && signIndex < signTexts.Length ? signTexts[signIndex] : "The writing has faded.";
                    signIndex++;
                }
            }

            if (!foundStart) m_Cell = level.WorldToCell(transform.position);
            transform.position = m_Target = level.GetCellCenterWorld(m_Cell);
            Face(m_Facing);

            if (animateMapBuild) StartCoroutine(BuildMap());
        }

        IEnumerator BuildMap()
        {
            m_Building = true;
            yield return TileBuildAnimation.Rebuild(level, BuildOrder.Ripple, buildSeconds, m_Cell).Play();
            m_Building = false;
        }

        void Update()
        {
            if (m_Building) return;
            if (m_Message != null)
            {
                if (DemoInput.InteractPressed) m_Message = null;
                return;
            }

            if (m_Moving)
            {
                transform.position = Vector3.MoveTowards(transform.position, m_Target, tilesPerSecond * level.layoutGrid.cellSize.x * Time.deltaTime);
                if (transform.position != m_Target) return;
                m_Moving = false; // arrived: fall through so held keys keep walking without a pause
            }

            if (DemoInput.InteractPressed)
            {
                Interact();
                return;
            }

            var dir = DemoInput.Direction;
            if (dir == Vector2Int.zero) return;

            Face(dir);
            var next = m_Cell + new Vector3Int(dir.x, dir.y, 0);
            if (!IsWalkable(next)) return;
            m_Cell = next;
            m_Target = level.GetCellCenterWorld(next);
            m_Moving = true;
        }

        bool IsWalkable(Vector3Int cell) => level.HasTile(cell) && level.GetColliderType(cell) == Tile.ColliderType.None;

        void Interact()
        {
            var cell = m_Cell + new Vector3Int(m_Facing.x, m_Facing.y, 0);
            var tile = level.GetTile(cell);
            if (tile == signTile && m_Signs.TryGetValue(cell, out var text)) m_Message = text;
            else if (tile == doorTile) m_Message = doorText;
        }

        void Face(Vector2Int dir)
        {
            m_Facing = dir;
            var index = dir == Vector2Int.up ? 1 : dir == Vector2Int.left ? 2 : dir == Vector2Int.right ? 3 : 0;
            if (m_Renderer != null && facingSprites != null && index < facingSprites.Length && facingSprites[index] != null)
                m_Renderer.sprite = facingSprites[index];
        }

        void OnGUI()
        {
            var size = Mathf.Max(14, Screen.height / 30);
            var hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 0.7f) };
            GUI.Label(new Rect(12, 8, Screen.width, size * 1.5f), "WASD or arrows: walk    E / Space: read signs and knock on doors", hint);

            if (m_Message == null) return;
            var box = new Rect(Screen.width * 0.08f, Screen.height * 0.72f, Screen.width * 0.84f, Screen.height * 0.22f);
            GUI.color = new Color(0.12f, 0.16f, 0.28f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var textStyle = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true, alignment = TextAnchor.UpperLeft };
            textStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(box.x + size, box.y + size * 0.6f, box.width - size * 2, box.height - size), m_Message, textStyle);
            var more = new GUIStyle(textStyle) { alignment = TextAnchor.LowerRight, fontSize = Mathf.RoundToInt(size * 0.6f) };
            GUI.Label(new Rect(box.x, box.y, box.width - size * 0.6f, box.height - size * 0.4f), "[E]", more);
        }
    }
}
