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
        /// it to <paramref name="scenePath"/>. Normally the scene is built additively and closed afterwards.
        /// Unity cannot add a scene next to an untitled one, so in that case the user is offered to save
        /// their changes and the new scene replaces the untitled one. Returns false if the user cancelled.
        /// </summary>
        public static bool Build(string scenePath, Action populate)
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning($"[MasonTile] Leave Play mode to build {scenePath}.");
                return false;
            }

            var previous = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(previous.path) && previous.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            previous = SceneManager.GetActiveScene();
            var additive = !string.IsNullOrEmpty(previous.path);
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
