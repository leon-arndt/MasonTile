using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace AgentBench
{
    /// <summary>
    /// Window > Agent Bench: watch a bench run as it happens. Shows the task's prompt, every tool call the agent
    /// makes (and what it says between calls, when the harness forwards it), the checker's verdict, and a tally
    /// of the tasks so far. Optionally keeps the Scene view framed on what the agent builds.
    ///
    /// It can also replay reference solutions slowly, one call at a time, to see what each task asks for.
    /// Everything it shows lives on this instance; <see cref="BenchMonitor"/> feeds it.
    /// </summary>
    public sealed class AgentBenchWindow : EditorWindow
    {
        const float k_DefaultPace = 0.45f;
        const double k_ShowSetupSeconds = 0.9;
        const double k_ShowResultSeconds = 1.5;

        [Serializable]
        sealed class Entry
        {
            public bool IsNote;
            public string Tool;
            public string Args;
            public string Text; // the note, the error, or a preview of the response
            public bool Ok;
            public double Milliseconds;
        }

        [Serializable]
        sealed class Finished
        {
            public string Task;
            public string Agent;
            public bool Passed;
            public float Score;
            public int Calls;
            public int Errors;
        }

        [SerializeField] string m_Task;
        [SerializeField] string m_Prompt;
        [SerializeField] string m_Agent;
        [SerializeField] string[] m_Tags = Array.Empty<string>();
        [SerializeField] bool m_Active;
        [SerializeField] List<Entry> m_Entries = new();
        [SerializeField] CheckResult m_Check;
        [SerializeField] List<Finished> m_History = new();
        [SerializeField] bool m_Follow = true;
        [SerializeField] double m_LastEventTime;
        [SerializeField] bool m_HasFramed;
        [SerializeField] Bounds m_Framed;
        [SerializeField] bool m_FramedHasContent;

        // The user's Scene view camera before the bench moved it, put back when the bench scene closes.
        [SerializeField] bool m_HasSavedCamera;
        [SerializeField] Vector3 m_SavedPivot;
        [SerializeField] Quaternion m_SavedRotation;
        [SerializeField] float m_SavedSize;
        [SerializeField] bool m_SavedOrthographic;
        [SerializeField] bool m_Saved2D;

        // Paced replay. Not serialised: a script reload ends it (the enumerator can't survive one).
        readonly Queue<string> m_ReplayQueue = new();
        IEnumerator<ToolCall> m_ReplayCalls;
        string m_ReplayTask;
        double m_ReplayNextAt;
        float m_ReplayPace = k_DefaultPace;
        bool m_Replaying;

        Vector2 m_Scroll;
        double m_LastRepaint;
        GUIStyle m_Title, m_PromptStyle, m_Line, m_Dim, m_Note, m_Error, m_Badge, m_Verdict, m_Section;

        public bool IsReplaying => m_Replaying;

        [MenuItem("Window/Agent Bench")]
        static void OpenMenu() => Open();

        /// <summary>Opens the window, or focuses it. With <paramref name="floating"/>, it floats at that screen rect.</summary>
        public static AgentBenchWindow Open(Rect? floating = null)
        {
            var window = GetWindow<AgentBenchWindow>("Agent Bench");
            if (floating.HasValue) window.position = floating.Value;
            window.Focus();
            return window;
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Agent Bench");
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (m_Replaying) StopReplay();
        }

        // ---------- Events from BenchMonitor ----------

        internal void OnTaskStarted(string task, string prompt, string[] tags, string agent)
        {
            m_Task = task;
            m_Prompt = prompt;
            m_Tags = tags ?? Array.Empty<string>();
            m_Agent = string.IsNullOrEmpty(agent) ? "Agent" : agent;
            m_Active = true;
            m_Entries.Clear();
            m_Check = null;
            m_Scroll = Vector2.zero;
            Touch();
            FollowSceneView(newTask: true);
        }

        internal void OnToolCalled(string toolId, JObject args, ToolCallOutcome outcome)
        {
            if (!m_Active) return;
            m_Entries.Add(new Entry
            {
                Tool = toolId,
                Args = FormatArgs(args),
                Ok = outcome.Ok,
                Text = outcome.Ok ? Preview(outcome.Response) : outcome.Error,
                Milliseconds = outcome.Milliseconds,
            });
            Touch();
            ScrollToEnd();
            FollowSceneView(newTask: false);
        }

        internal void OnNote(string text)
        {
            if (!m_Active || string.IsNullOrWhiteSpace(text)) return;
            m_Entries.Add(new Entry { IsNote = true, Text = text.Trim() });
            Touch();
            ScrollToEnd();
        }

        internal void OnChecked(string task, CheckResult result)
        {
            if (task != m_Task) return;
            m_Check = result;
            var row = new Finished
            {
                Task = task,
                Agent = m_Agent,
                Passed = result.Passed,
                Score = result.Score,
                Calls = m_Entries.Count(e => !e.IsNote),
                Errors = m_Entries.Count(e => !e.IsNote && !e.Ok),
            };
            // Checking the same task again (by hand) updates its row instead of adding one.
            if (m_History.Count > 0 && m_History[^1].Task == task && m_Active) m_History[^1] = row;
            else m_History.Add(row);
            Touch();
            ScrollToEnd();
            FollowSceneView(newTask: false);
        }

        internal void OnTaskEnded()
        {
            m_Active = false;
            m_HasFramed = false;
            RestoreSceneCamera();
            Touch();
        }

        void Touch()
        {
            m_LastEventTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        void ScrollToEnd() => m_Scroll.y = float.MaxValue;

        // ---------- Paced replay ----------

        /// <summary>Plays the reference solutions of these tasks one call at a time, then reopens the user's scenes.</summary>
        public void StartReplay(IEnumerable<string> qualifiedTaskIds, float pace = k_DefaultPace)
        {
            if (m_Replaying) StopReplay();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the bench.");
            m_ReplayQueue.Clear();
            foreach (var id in qualifiedTaskIds) m_ReplayQueue.Enqueue(id);
            m_ReplayPace = Mathf.Max(0f, pace);
            m_ReplayCalls = null;
            m_ReplayTask = null;
            m_ReplayNextAt = 0;
            m_Replaying = m_ReplayQueue.Count > 0;
        }

        /// <summary>Forgets the finished tasks, and the last task too unless one is running.</summary>
        public void ClearHistory()
        {
            m_History.Clear();
            if (!m_Active) m_Task = null;
            Repaint();
        }

        public void StopReplay()
        {
            m_Replaying = false;
            m_ReplayQueue.Clear();
            m_ReplayCalls?.Dispose();
            m_ReplayCalls = null;
            m_ReplayTask = null;
            BenchRunner.Teardown();
        }

        void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (m_Replaying && now >= m_ReplayNextAt)
            {
                try
                {
                    ReplayStep(now);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    StopReplay();
                }
            }
            // The "working" timer ticks while an agent thinks.
            if (m_Active && m_Check == null && now - m_LastRepaint > 0.1)
            {
                m_LastRepaint = now;
                Repaint();
            }
        }

        void ReplayStep(double now)
        {
            if (m_ReplayCalls == null)
            {
                if (m_ReplayQueue.Count == 0)
                {
                    StopReplay();
                    return;
                }
                m_ReplayTask = m_ReplayQueue.Dequeue();
                // The user's scenes were checked for unsaved changes by the first setup; later ones reuse that.
                var (_, task) = BenchRunner.Setup(m_ReplayTask, agent: BenchRunner.ReferenceAgent);
                m_ReplayCalls = task.Reference().GetEnumerator();
                m_ReplayNextAt = now + k_ShowSetupSeconds;
                return;
            }

            if (m_ReplayCalls.MoveNext())
            {
                var outcome = ToolInvoker.Invoke(m_ReplayCalls.Current);
                m_ReplayNextAt = now + m_ReplayPace;
                if (outcome.Ok) return;
            }

            // Out of calls, or one failed: judge it and move on.
            m_ReplayCalls.Dispose();
            m_ReplayCalls = null;
            BenchRunner.Check(m_ReplayTask);
            m_ReplayNextAt = now + k_ShowResultSeconds;
        }

        // ---------- Scene view ----------

        /// <summary>
        /// Frames what the bench scene contains. A new task frames it at once; after that the view only zooms out,
        /// smoothly, when the agent builds beyond what is in view, so it doesn't jump on every call.
        /// </summary>
        void FollowSceneView(bool newTask)
        {
            if (!m_Follow) return;
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;

            var hasContent = TryGetContentBounds(out var content);
            if (!hasContent) content = new Bounds(Vector3.zero, new Vector3(16f, 10f, 0f));
            content.Expand(new Vector3(2f, 2f, 0f));
            if (!newTask && m_HasFramed && m_FramedHasContent && m_Framed.Contains(content.min) && m_Framed.Contains(content.max)) return;

            var target = content;
            if (!newTask && m_HasFramed && m_FramedHasContent) target.Encapsulate(m_Framed); // only zoom out mid-task
            m_Framed = target;
            m_FramedHasContent = hasContent;
            m_HasFramed = true;
            SaveSceneCamera(view);

            if (target.size.z > 0.01f)
            {
                view.Frame(target, newTask); // 3D content: Unity's own framing
                return;
            }
            view.in2DMode = true;
            var (pivot, size) = Fit2D(view, target);
            view.LookAt(pivot, Quaternion.identity, size, true, newTask);
        }

        /// <summary>
        /// The pivot and orthographic size that fit <paramref name="bounds"/> into the Scene view. If this window floats
        /// over the right of the Scene view, the bounds are fitted into the part of the view left of it.
        /// </summary>
        (Vector3 pivot, float size) Fit2D(SceneView view, Bounds bounds)
        {
            var scene = view.position;
            var aspect = view.camera != null && view.camera.aspect > 0f ? view.camera.aspect : scene.width / Mathf.Max(1f, scene.height);

            var visible = 1f;
            var self = position;
            if (!docked && self.yMin < scene.yMax && self.yMax > scene.yMin && self.xMin > scene.xMin && self.xMin < scene.xMax)
                visible = Mathf.Clamp((self.xMin - scene.xMin - 12f) / scene.width, 0.3f, 1f);

            // In 2D the Scene view's size is the camera's orthographic half-height.
            var halfHeight = Mathf.Max(bounds.extents.y, bounds.extents.x / (visible * aspect));
            var worldWidth = 2f * halfHeight * aspect;
            var pivot = new Vector3(bounds.center.x + worldWidth * (1f - visible) * 0.5f, bounds.center.y, 0f);
            return (pivot, halfHeight);
        }

        void SaveSceneCamera(SceneView view)
        {
            if (m_HasSavedCamera) return;
            m_HasSavedCamera = true;
            m_SavedPivot = view.pivot;
            m_SavedRotation = view.rotation;
            m_SavedSize = view.size;
            m_SavedOrthographic = view.orthographic;
            m_Saved2D = view.in2DMode;
        }

        void RestoreSceneCamera()
        {
            if (!m_HasSavedCamera) return;
            m_HasSavedCamera = false;
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.in2DMode = m_Saved2D;
            view.LookAt(m_SavedPivot, m_SavedRotation, m_SavedSize, m_SavedOrthographic, true);
        }

        /// <summary>Bounds of everything visible in the active scene. Tilemaps use their painted cells.</summary>
        static bool TryGetContentBounds(out Bounds bounds)
        {
            var found = new List<Bounds>();
            void Add(Bounds b)
            {
                if (b.size.x > 0f || b.size.y > 0f) found.Add(b);
            }

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var tilemap in root.GetComponentsInChildren<Tilemap>())
                {
                    if (tilemap.GetUsedTilesCount() == 0) continue;
                    var cells = tilemap.cellBounds;
                    // cellBounds is one cell deep in z; only x and y matter for framing a 2D tilemap.
                    var min = tilemap.CellToWorld(new Vector3Int(cells.xMin, cells.yMin, 0));
                    var max = tilemap.CellToWorld(new Vector3Int(cells.xMax, cells.yMax, 0));
                    var b = new Bounds((min + max) * 0.5f, Vector3.zero);
                    b.Encapsulate(min);
                    b.Encapsulate(max);
                    Add(b);
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled && renderer is not TilemapRenderer) Add(renderer.bounds);
            }
            bounds = found.FirstOrDefault();
            foreach (var b in found.Skip(1)) bounds.Encapsulate(b);
            return found.Count > 0;
        }

        // ---------- Formatting ----------

        static string ShortTool(string id)
        {
            var dot = id?.LastIndexOf('.') ?? -1;
            return dot >= 0 ? id.Substring(dot + 1) : id;
        }

        /// <summary>"tilemap=Ground x=2 y=1", with multi-line text shown as its first row and a row count.</summary>
        static string FormatArgs(JObject args)
        {
            if (args == null || !args.HasValues) return "";
            return string.Join("  ", args.Properties().Select(p =>
            {
                string value;
                if (p.Value.Type == JTokenType.String)
                {
                    var s = (string)p.Value;
                    var rows = s.Split('\n');
                    value = rows.Length > 1 ? $"\"{Truncate(rows[0], 24)}…\" ({rows.Length} rows)" : Truncate(s, 32);
                }
                else value = Truncate(p.Value.ToString(Formatting.None), 32);
                return $"{p.Name}={value}";
            }));
        }

        static string Preview(string response)
        {
            if (string.IsNullOrEmpty(response) || response == "null") return "";
            return Truncate(response.Replace("\\n", " ⏎ ").Replace('\n', ' '), 150);
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        // ---------- GUI ----------

        // Getters rather than static fields: nothing static to clean up (CLAUDE.md).
        static Color k_Pass => new(0.30f, 0.82f, 0.45f);
        static Color k_Fail => new(0.95f, 0.36f, 0.33f);
        static Color k_Note => new(0.62f, 0.72f, 0.95f);
        static Color k_Accent => new(1f, 0.82f, 0.25f);

        void InitStyles()
        {
            if (m_Title != null) return;
            m_Title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, wordWrap = true };
            m_Section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            m_Section.normal.textColor = new Color(0.6f, 0.65f, 0.75f);
            m_PromptStyle = new GUIStyle(EditorStyles.label) { fontSize = 15, wordWrap = true, richText = false, padding = new RectOffset(10, 10, 8, 8) };
            const int indent = 14; // room for the status dot
            m_Line = new GUIStyle(EditorStyles.label) { fontSize = 13, wordWrap = true, richText = true };
            m_Line.padding.left += indent;
            m_Dim = new GUIStyle(m_Line) { fontSize = 11 };
            m_Dim.normal.textColor = new Color(0.55f, 0.58f, 0.64f);
            m_Error = new GUIStyle(m_Dim);
            m_Error.normal.textColor = k_Fail;
            m_Note = new GUIStyle(m_Line) { fontStyle = FontStyle.Italic, richText = false };
            m_Note.normal.textColor = k_Note;
            m_Badge = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(6, 6, 2, 2) };
            m_Verdict = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
        }

        void OnGUI()
        {
            InitStyles();
            DrawToolbar();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.Space(6);
            if (string.IsNullOrEmpty(m_Task))
            {
                EditorGUILayout.LabelField("Waiting for a bench task…", m_Title);
                EditorGUILayout.LabelField("Start one with agentbench.py run, or press Replay to watch the reference solutions.", m_Dim);
            }
            else
            {
                DrawTask();
                DrawEntries();
                DrawVerdict();
            }
            EditorGUILayout.Space(8);
            DrawHistory();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (!m_Replaying && EditorGUILayout.DropdownButton(new GUIContent("Replay"), FocusType.Passive, EditorStyles.toolbarDropDown))
                        ShowReplayMenu();
                    if (m_Replaying && GUILayout.Button("Stop", EditorStyles.toolbarButton)) StopReplay();
                }
                GUILayout.FlexibleSpace();
                m_Follow = GUILayout.Toggle(m_Follow, "Follow in Scene view", EditorStyles.toolbarButton);
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton)) ClearHistory();
            }
        }

        void ShowReplayMenu()
        {
            var menu = new GenericMenu();
            foreach (var suite in BenchSuite.All())
            {
                var ids = suite.CreateTasks().Select(suite.QualifiedId).ToList();
                menu.AddItem(new GUIContent($"{suite.Name}/All tasks"), false, () => StartReplay(ids));
                menu.AddSeparator($"{suite.Name}/");
                foreach (var id in ids) menu.AddItem(new GUIContent($"{suite.Name}/{id.Substring(id.IndexOf('/') + 1)}"), false, () => StartReplay(new[] { id }));
            }
            menu.ShowAsContext();
        }

        void DrawTask()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(m_Task, m_Title);
                GUILayout.FlexibleSpace();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Badge(m_Agent, m_Agent == BenchRunner.ReferenceAgent ? new Color(0.35f, 0.42f, 0.55f) : new Color(0.55f, 0.36f, 0.85f));
                foreach (var tag in m_Tags) Badge(tag, new Color(0.25f, 0.28f, 0.33f));
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("PROMPT", m_Section);
            var rect = GUILayoutUtility.GetRect(new GUIContent(m_Prompt), m_PromptStyle);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), k_Accent);
            GUI.Label(rect, m_Prompt, m_PromptStyle);
            EditorGUILayout.Space(8);
        }

        void DrawEntries()
        {
            var calls = m_Entries.Count(e => !e.IsNote);
            var errors = m_Entries.Count(e => !e.IsNote && !e.Ok);
            EditorGUILayout.LabelField($"TOOL CALLS  {calls}" + (errors > 0 ? $"   ERRORS  {errors}" : ""), m_Section);
            var n = 0;
            foreach (var e in m_Entries)
            {
                if (e.IsNote)
                {
                    Row(e.Text, m_Note);
                    EditorGUILayout.Space(2);
                    continue;
                }

                n++;
                Row($"<b>{n}. {ShortTool(e.Tool)}</b>  {Escape(e.Args)}", m_Line, e.Ok ? k_Pass : k_Fail);
                if (!string.IsNullOrEmpty(e.Text)) Row(e.Ok ? "→ " + e.Text : "✗ " + e.Text, e.Ok ? m_Dim : m_Error);
                EditorGUILayout.Space(2);
            }

            if (m_Active && m_Check == null)
            {
                var waited = EditorApplication.timeSinceStartup - m_LastEventTime;
                var dots = new string('.', 1 + (int)(EditorApplication.timeSinceStartup * 3) % 3);
                var who = m_Agent == BenchRunner.ReferenceAgent ? "Replaying" : "Agent working";
                Row($"{who}{dots}  {waited:0.0} s", m_Dim);
            }
        }

        void DrawVerdict()
        {
            if (m_Check == null) return;
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("CHECKER", m_Section);
            var verdict = new GUIStyle(m_Verdict) { normal = { textColor = m_Check.Passed ? k_Pass : k_Fail } };
            EditorGUILayout.LabelField(m_Check.Passed ? $"PASS  {m_Check.Score:P0}" : $"FAIL  {m_Check.Score:P0}", verdict, GUILayout.Height(30));
            foreach (var item in m_Check.Items)
                Row(item.Passed ? item.Expectation : $"{item.Expectation}\n<color=#f05c54>{Escape(item.Detail)}</color>", m_Line, item.Passed ? k_Pass : k_Fail);
        }

        void DrawHistory()
        {
            if (m_History.Count == 0) return;
            var passed = m_History.Count(h => h.Passed);
            EditorGUILayout.LabelField($"THIS SESSION  {passed} / {m_History.Count} PASSED", m_Section);
            foreach (var h in m_History)
            {
                var name = h.Task.Substring(h.Task.IndexOf('/') + 1);
                Row($"{name}   {h.Calls} calls" + (h.Errors > 0 ? $", {h.Errors} errors" : "") + $"  ·  {h.Agent}", m_Dim, h.Passed ? k_Pass : k_Fail);
            }
        }

        /// <summary>
        /// One wrapped line of text with an optional status dot in its left padding. The styles carry the indent as
        /// padding, so the height the layout reserves matches the text as drawn.
        /// </summary>
        static void Row(string text, GUIStyle style, Color? dot = null)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(text), style);
            if (dot.HasValue) EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + style.padding.top + 4f, 8f, 8f), dot.Value);
            GUI.Label(rect, text, style);
        }

        void Badge(string text, Color color)
        {
            var content = new GUIContent(text);
            var rect = GUILayoutUtility.GetRect(content, m_Badge, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(rect, color);
            var style = new GUIStyle(m_Badge) { normal = { textColor = Color.white } };
            GUI.Label(rect, content, style);
            GUILayout.Space(4);
        }

        /// <summary>Keeps agent-controlled text from being read as rich-text tags.</summary>
        static string Escape(string s) => s?.Replace("<", "<​");
    }
}
