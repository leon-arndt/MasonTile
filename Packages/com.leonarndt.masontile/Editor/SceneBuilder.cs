using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MasonTile.Editor
{
    /// <summary>Creates and saves a scene from code without disturbing the scenes the user has open.</summary>
    public static class SceneBuilder
    {
        /// <summary>
        /// Creates an empty scene, makes it active while <paramref name="populate"/> adds objects, and saves
        /// it to <paramref name="scenePath"/>. Normally the scene is built additively and closed afterwards,
        /// leaving the user's scenes untouched. Two cases replace the open scenes instead (after offering to
        /// save changes): the scene at <paramref name="scenePath"/> is itself open, or the active scene is
        /// untitled (Unity cannot add a scene next to an untitled one). Returns false if the user cancelled.
        /// </summary>
        public static bool Build(string scenePath, Action populate)
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning($"[MasonTile] Leave Play mode to build {scenePath}.");
                return false;
            }

            TileBuildPreview.FinishAll();
            var target = SceneManager.GetSceneByPath(scenePath);
            var replacing = target.IsValid() && target.isLoaded;
            var previous = SceneManager.GetActiveScene();
            if ((replacing || string.IsNullOrEmpty(previous.path)) && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            previous = SceneManager.GetActiveScene();
            var additive = !replacing && !string.IsNullOrEmpty(previous.path);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                populate();
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            finally
            {
                if (additive)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            return true;
        }
    }
}
