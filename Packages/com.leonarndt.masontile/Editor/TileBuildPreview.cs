using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
#if UNITY_6000_7_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace MasonTile.Editor
{
    /// <summary>
    /// Plays <see cref="TileBuildAnimation"/>s in Edit mode, driven by EditorApplication.update, and repaints
    /// the Scene and Game views each tick. Animations are finished instantly before a script reload or
    /// entering Play Mode so a level is never left half-built.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_7_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class TileBuildPreview
    {
        // Static by necessity: an edit-mode animation queue has no instance to live on. Cleaned up by hand
        // rather than by AutoStaticsCleanup, because FinishAll must complete each animation and run its
        // callback, not just empty the list. It runs on ExitingEditMode as well as before a script reload,
        // because with domain reload disabled (Enter Play Mode Options) entering Play Mode no longer triggers a reload.
        static readonly List<(TileBuildAnimation animation, Action onComplete)> s_Active = new();

        static TileBuildPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += FinishAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) FinishAll();
        }

        public static bool IsPlaying => s_Active.Count > 0;

        public static void Play(TileBuildAnimation animation, Action onComplete = null)
        {
            if (s_Active.Count == 0) EditorApplication.update += Tick;
            s_Active.Add((animation, onComplete));
        }

        public static void FinishAll()
        {
            foreach (var (animation, onComplete) in s_Active.ToArray())
            {
                animation.Finish();
                onComplete?.Invoke();
            }
            s_Active.Clear();
            EditorApplication.update -= Tick;
        }

        static void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            for (var i = s_Active.Count - 1; i >= 0; i--)
            {
                var (animation, onComplete) = s_Active[i];
                if (animation.Tilemap == null)
                {
                    s_Active.RemoveAt(i);
                    continue;
                }

                animation.Step(now);
                if (!animation.IsDone) continue;
                s_Active.RemoveAt(i);
                onComplete?.Invoke();
            }

            InternalEditorUtility.RepaintAllViews();
            if (s_Active.Count == 0) EditorApplication.update -= Tick;
        }
    }
}
