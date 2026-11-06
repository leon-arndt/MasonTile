using UnityEditor;
using UnityEngine;

namespace MasonTile.Editor
{
    /// <summary>
    /// Per-user MasonTile preferences (Edit > Preferences > MasonTile). Values live in EditorPrefs, not in
    /// static fields, so nothing carries over between Play sessions (see CLAUDE.md).
    /// </summary>
    public static class MasonTileSettings
    {
        const string k_BuildSecondsKey = "MasonTile.BuildAnimationSeconds";
        public const float DefaultBuildSeconds = 4f;

        /// <summary>Length of the Scene-view build animation played by the Demos menu.</summary>
        public static float BuildAnimationSeconds
        {
            get => EditorPrefs.GetFloat(k_BuildSecondsKey, DefaultBuildSeconds);
            set => EditorPrefs.SetFloat(k_BuildSecondsKey, Mathf.Clamp(value, 0.5f, 20f));
        }

        [SettingsProvider]
        static SettingsProvider CreateProvider() => new("Preferences/MasonTile", SettingsScope.User)
        {
            keywords = new[] { "MasonTile", "tilemap", "build", "animation" },
            guiHandler = _ =>
            {
                EditorGUIUtility.labelWidth = 220;
                BuildAnimationSeconds = EditorGUILayout.Slider(
                    new GUIContent("Build animation (seconds)", "How long the Scene-view build animation takes when a demo is opened or rebuilt."),
                    BuildAnimationSeconds, 0.5f, 20f);
            },
        };
    }
}
