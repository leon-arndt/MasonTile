using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AgentBench
{
    /// <summary>
    /// Gives each task a fresh, empty scene and puts the user's scenes back afterwards.
    ///
    /// A task spans several Pipeline requests (setup, many tool calls, check), so which task is active and which
    /// scenes to restore are kept in <see cref="SessionState"/> rather than in a static field (see CLAUDE.md):
    /// it survives domain reloads and ends with the Editor session.
    /// </summary>
    public static class BenchScene
    {
        const string k_ActiveTaskKey = "AgentBench.ActiveTask";
        const string k_RestoreKey = "AgentBench.RestoreScenes";

        /// <summary>Qualified id of the task whose scene is open, or empty.</summary>
        public static string ActiveTask => SessionState.GetString(k_ActiveTaskKey, "");

        /// <summary>
        /// Replaces the open scenes with an empty one. The first call remembers the user's scenes for
        /// <see cref="Close"/>; later calls (the next task) reuse that.
        /// </summary>
        /// <param name="discardUnsaved">
        /// Allow throwing away unsaved scene changes. Only the tests pass true: the Test Runner already asked to
        /// save, and the scene it runs in is its own.
        /// </param>
        public static void Open(string qualifiedTaskId, bool discardUnsaved = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before running the bench.");

            if (string.IsNullOrEmpty(ActiveTask))
            {
                var loaded = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToList();
                var dirty = loaded.Where(s => s.isDirty).Select(s => string.IsNullOrEmpty(s.path) ? "Untitled" : s.path).ToList();
                if (dirty.Count > 0 && !discardUnsaved)
                    throw new InvalidOperationException(
                        $"Unsaved changes in {string.Join(", ", dirty)}. Save or revert them first: the bench opens its own scene and reopens yours afterwards.");

                var setup = EditorSceneManager.GetSceneManagerSetup()
                    .Where(s => !string.IsNullOrEmpty(s.path))
                    .Select(s => new SavedScene { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })
                    .ToArray();
                SessionState.SetString(k_RestoreKey, JsonUtility.ToJson(new SavedSetup { scenes = setup }));
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetString(k_ActiveTaskKey, qualifiedTaskId);
        }

        /// <summary>Reopens the scenes that were open before the first <see cref="Open"/>, discarding the bench scene.</summary>
        public static void Close()
        {
            var json = SessionState.GetString(k_RestoreKey, "");
            var saved = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SavedSetup>(json).scenes;
            var setup = (saved ?? Array.Empty<SavedScene>())
                .Select(s => new SceneSetup { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })
                .ToArray();

            SessionState.EraseString(k_ActiveTaskKey);
            SessionState.EraseString(k_RestoreKey);

            // An untitled scene has no path to reopen, so the user gets an empty scene back instead.
            if (setup.Length > 0 && setup.Any(s => s.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        [Serializable]
        sealed class SavedSetup
        {
            public SavedScene[] scenes;
        }

        [Serializable]
        sealed class SavedScene
        {
            public string path;
            public bool isLoaded;
            public bool isActive;
        }
    }
}
