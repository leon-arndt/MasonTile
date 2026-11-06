using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Samples.Platformer
{
    /// <summary>
    /// Side-scrolling player that treats the tilemap as game data: coins, spikes and the goal flag are
    /// tiles it looks up under itself, and every level is a text file loaded with <see cref="AsciiMap"/>.
    /// Level 1 is whatever is painted in the scene (snapshotted as text on Start, so R can restore it).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class PlatformerPlayer : MonoBehaviour
    {
        [Header("Level")]
        public Tilemap level;
        public TileLegend legend;
        [Tooltip("Levels played after the one painted in the scene, in order. Same legend, origin (0, 0).")]
        public TextAsset[] nextLevels;

        [Header("Special tiles")]
        public TileBase startTile;
        public TileBase coinTile;
        public TileBase spikeTile;
        public TileBase flagTile;

        [Header("Movement")]
        public float moveSpeed = 7f;
        public float jumpSpeed = 15.5f;
        [Tooltip("Seconds after leaving a ledge during which a jump still works.")]
        public float coyoteTime = 0.1f;
        [Tooltip("Seconds a jump press is remembered before landing.")]
        public float jumpBuffer = 0.12f;
        [Range(0f, 1f), Tooltip("Upward speed multiplier when jump is released early (short hops).")]
        public float jumpCut = 0.5f;

        [Header("Level build animation")]
        [Tooltip("Build each level tile by tile (left to right) when it starts.")]
        public bool animateLevelBuild = true;
        public float buildSeconds = 3f;

        static readonly Vector3[] k_TileProbes = { Vector3.zero, new(0f, -0.4f, 0f), new(0f, 0.4f, 0f) };

        Rigidbody2D m_Body;
        SpriteRenderer m_Renderer;
        CameraFollow2D m_Camera;
        ContactFilter2D m_GroundFilter;

        string m_FirstLevelText;
        Vector2Int m_FirstLevelOrigin;
        int m_LevelIndex;
        int m_Coins, m_TotalCoins, m_Deaths;
        bool m_Finished;
        bool m_Building;

        // Read-only state for tests, recording autopilots and agents.
        public bool IsBuilding => m_Building;
        public bool IsFinished => m_Finished;
        public int LevelIndex => m_LevelIndex;
        public int Coins => m_Coins;

        Vector3 m_Spawn;
        float m_KillY;
        float m_LastGrounded = -1f, m_LastJumpPressed = -1f;
        float m_MoveInput; // read in Update: the Input System updates per frame, not per physics step
        string m_Banner;
        float m_BannerUntil;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody2D>();
            m_Renderer = GetComponent<SpriteRenderer>();
            m_GroundFilter = new ContactFilter2D { useNormalAngle = true, minNormalAngle = 45f, maxNormalAngle = 135f };
        }

        void Start()
        {
            m_Camera = Camera.main != null ? Camera.main.GetComponent<CameraFollow2D>() : null;
            var used = TilemapOps.UsedBounds(level);
            m_FirstLevelOrigin = used.position;
            m_FirstLevelText = AsciiMap.Read(level, used, legend);
            if (animateLevelBuild) StartCoroutine(BuildThenBegin(TileBuildAnimation.Rebuild(level, BuildOrder.SweepRight, buildSeconds)));
            else BeginLevel();
        }

        void Update()
        {
            if (m_Building) return;
            if (DemoInput.RestartPressed)
            {
                if (m_Finished) { m_Finished = false; m_LevelIndex = 0; }
                ReloadCurrentLevel();
                return;
            }
            if (m_Finished) return;

            m_MoveInput = DemoInput.Horizontal;
            if (DemoInput.JumpPressed) m_LastJumpPressed = Time.time;
            if (DemoInput.JumpReleased && m_Body.linearVelocity.y > 0f)
                m_Body.linearVelocity = new Vector2(m_Body.linearVelocity.x, m_Body.linearVelocity.y * jumpCut);
        }

        void FixedUpdate()
        {
            if (m_Building) return;
            var v = m_Body.linearVelocity;
            if (m_Finished)
            {
                m_Body.linearVelocity = new Vector2(0f, v.y);
                return;
            }

            if (m_Body.IsTouching(m_GroundFilter)) m_LastGrounded = Time.time;

            v.x = m_MoveInput * moveSpeed;
            if (Time.time - m_LastJumpPressed <= jumpBuffer && Time.time - m_LastGrounded <= coyoteTime)
            {
                v.y = jumpSpeed;
                m_LastJumpPressed = m_LastGrounded = -1f;
            }
            m_Body.linearVelocity = v;
            if (m_Renderer != null && Mathf.Abs(v.x) > 0.01f) m_Renderer.flipX = v.x < 0f;

            CheckTilesUnderPlayer();
            if (transform.position.y < m_KillY) Die();
        }

        void CheckTilesUnderPlayer()
        {
            foreach (var probe in k_TileProbes)
            {
                var cell = level.WorldToCell(transform.position + probe);
                var tile = level.GetTile(cell);
                if (tile == null) continue;

                if (tile == coinTile)
                {
                    level.SetTile(cell, null);
                    m_Coins++;
                }
                else if (tile == spikeTile)
                {
                    Die();
                    return;
                }
                else if (tile == flagTile)
                {
                    CompleteLevel();
                    return;
                }
            }
        }

        void BeginLevel()
        {
            level.CompressBounds();
            var used = TilemapOps.UsedBounds(level);
            m_TotalCoins = TilemapOps.CountTiles(level, used).TryGetValue(coinTile, out var n) ? n : 0;
            m_Coins = 0;
            m_KillY = level.CellToWorld(new Vector3Int(0, used.yMin, 0)).y - 3f;
            m_Spawn = FindStart(used);
            Respawn();
            Banner($"Level {m_LevelIndex + 1}");
        }

        Vector3 FindStart(RectInt used)
        {
            for (var y = used.yMax - 1; y >= used.yMin; y--)
            for (var x = used.xMin; x < used.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (level.GetTile(cell) == startTile) return level.GetCellCenterWorld(cell);
            }
            Debug.LogWarning("[MasonTile] No start tile in this level; spawning at the top-left.", this);
            return level.GetCellCenterWorld(new Vector3Int(used.xMin + 1, used.yMax - 1, 0));
        }

        void Respawn()
        {
            transform.position = m_Spawn;
            m_Body.linearVelocity = Vector2.zero;
            if (m_Camera != null) m_Camera.Snap();
        }

        void Die()
        {
            m_Deaths++;
            Respawn();
            Banner("Ouch!");
        }

        void CompleteLevel()
        {
            if (m_LevelIndex >= nextLevels.Length)
            {
                m_Finished = true;
                return;
            }
            m_LevelIndex++;
            ReloadCurrentLevel();
        }

        /// <summary>Rebuilds the current level from its text, which also restores collected coins.</summary>
        void ReloadCurrentLevel()
        {
            var text = m_LevelIndex == 0 ? m_FirstLevelText : nextLevels[m_LevelIndex - 1].text;
            var origin = m_LevelIndex == 0 ? m_FirstLevelOrigin : Vector2Int.zero;
            var parsed = AsciiMap.Parse(text, legend);
            if (!parsed.Success)
            {
                Debug.LogError($"[MasonTile] Level {m_LevelIndex + 1} has errors:\n{string.Join("\n", parsed.Errors)}", this);
                return;
            }

            level.ClearAllTiles();
            if (animateLevelBuild)
            {
                // Put the (hidden) player and the camera at the new start so the build plays in view.
                var startSymbol = legend.GetSymbol(startTile);
                for (var r = 0; r < parsed.Height; r++)
                {
                    var c = parsed.Rows[r].IndexOf(startSymbol);
                    if (c < 0) continue;
                    transform.position = level.GetCellCenterWorld(new Vector3Int(origin.x + c, origin.y + parsed.Height - 1 - r, 0));
                    if (m_Camera != null) m_Camera.Snap();
                    break;
                }
                StartCoroutine(BuildThenBegin(TileBuildAnimation.FromText(level, origin, parsed, legend, BuildOrder.SweepRight, buildSeconds)));
                return;
            }
            AsciiMap.Write(level, origin, parsed, legend);
            BeginLevel();
        }

        /// <summary>Hides and freezes the player while the level builds itself, then starts the level.</summary>
        IEnumerator BuildThenBegin(TileBuildAnimation build)
        {
            m_Building = true;
            m_Body.simulated = false;
            if (m_Renderer != null) m_Renderer.enabled = false;
            yield return build.Play();
            m_Building = false;
            m_Body.simulated = true;
            if (m_Renderer != null) m_Renderer.enabled = true;
            BeginLevel();
        }

        void Banner(string text, float seconds = 1.5f)
        {
            m_Banner = text;
            m_BannerUntil = Time.time + seconds;
        }

        void OnGUI()
        {
            var size = Mathf.Max(14, Screen.height / 26);
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold };

            Shadowed(new Rect(16, 10, Screen.width, size * 1.6f), $"Level {m_LevelIndex + 1}/{nextLevels.Length + 1}    Coins {m_Coins}/{m_TotalCoins}    Deaths {m_Deaths}", style);
            var hint = new GUIStyle(style) { fontSize = Mathf.RoundToInt(size * 0.6f), fontStyle = FontStyle.Normal };
            Shadowed(new Rect(16, Screen.height - size * 1.4f, Screen.width, size * 1.4f), "A/D or arrows: move    Space: jump    R: restart level", hint);

            var center = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = size * 2 };
            var message = m_Finished ? "You win!\nPress R to play again" : Time.time < m_BannerUntil ? m_Banner : null;
            if (message != null) Shadowed(new Rect(0, 0, Screen.width, Screen.height * 0.6f), message, center);
        }

        static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var color = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, text, style);
            style.normal.textColor = color;
        }
    }
}
