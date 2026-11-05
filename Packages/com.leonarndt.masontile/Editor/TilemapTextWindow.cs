using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace MasonTile.Editor
{
    /// <summary>
    /// View and edit a tilemap as text. Read pulls the used area into the text box, Apply writes it
    /// back through <see cref="TilemapCommands"/> (undoable, with the same validation agents get).
    /// </summary>
    public class TilemapTextWindow : EditorWindow
    {
        ObjectField m_TilemapField;
        ObjectField m_LegendField;
        Vector2IntField m_OriginField;
        TextField m_Text;
        Label m_Status;
        Label m_LegendLabel;

        [MenuItem("Window/MasonTile/Tilemap Text View")]
        public static void Open() => GetWindow<TilemapTextWindow>("MasonTile Text");

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;

            m_TilemapField = new ObjectField("Tilemap") { objectType = typeof(Tilemap), allowSceneObjects = true };
            m_TilemapField.RegisterValueChangedCallback(_ => OnTilemapChanged());
            m_LegendField = new ObjectField("Legend") { objectType = typeof(TileLegend), allowSceneObjects = false };
            m_LegendField.RegisterValueChangedCallback(_ => RefreshLegendLabel());
            m_OriginField = new Vector2IntField("Origin (bottom-left)");

            m_LegendLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, marginBottom = 4 } };

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            buttons.Add(new Button(ReadFromTilemap) { text = "Read", tooltip = "Load the tilemap's used area into the text box." });
            buttons.Add(new Button(ApplyToTilemap) { text = "Apply", tooltip = "Write the text to the tilemap at Origin (undoable)." });
            buttons.Add(new Button(CopyForPrompt) { text = "Copy for prompt", tooltip = "Copy the text, origin, and legend to the clipboard." });

            m_Text = new TextField { multiline = true, style = { flexGrow = 1, minHeight = 120 } };
            m_Text.verticalScrollerVisibility = ScrollerVisibility.Auto;
            var mono = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
            if (mono != null) m_Text.style.unityFontDefinition = FontDefinition.FromFont(mono);

            m_Status = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } };

            root.Add(m_TilemapField);
            root.Add(m_LegendField);
            root.Add(m_OriginField);
            root.Add(m_LegendLabel);
            root.Add(buttons);
            root.Add(m_Text);
            root.Add(m_Status);

            OnSelectionChange();
            RefreshLegendLabel();
        }

        void OnSelectionChange()
        {
            if (m_TilemapField == null) return;
            var tm = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Tilemap>() : null;
            if (tm != null && tm != m_TilemapField.value) m_TilemapField.value = tm;
        }

        void OnTilemapChanged()
        {
            var tm = m_TilemapField.value as Tilemap;
            if (tm != null && TilemapCommands.TryResolveLegend(tm, out var legend, out _))
                m_LegendField.value = legend;
        }

        void RefreshLegendLabel()
        {
            var legend = m_LegendField.value as TileLegend;
            if (legend == null)
            {
                m_LegendLabel.text = "Assign a Tile Legend (Assets > Create > MasonTile > Tile Legend).";
                return;
            }
            var problems = legend.Validate();
            m_LegendLabel.text = legend.Describe() + ", ' '=keep existing, '?'=tile not in legend" +
                                 (problems.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", problems) : "");
        }

        bool TryGetTargets(out Tilemap tm, out TileLegend legend)
        {
            tm = m_TilemapField.value as Tilemap;
            legend = m_LegendField.value as TileLegend;
            if (tm != null && legend != null) return true;
            m_Status.text = "Assign both a Tilemap and a Legend.";
            return false;
        }

        void ReadFromTilemap()
        {
            if (!TryGetTargets(out var tm, out var legend)) return;
            var result = TilemapCommands.Read(tm, legend);
            m_Status.text = result.Message;
            if (result.Snapshot == null) return;
            m_Text.value = result.Snapshot.Rows;
            m_OriginField.value = new Vector2Int(result.Snapshot.OriginX, result.Snapshot.OriginY);
        }

        void ApplyToTilemap()
        {
            if (!TryGetTargets(out var tm, out var legend)) return;
            var result = TilemapCommands.WriteText(tm, legend, m_OriginField.value, m_Text.value);
            m_Status.text = result.Message;
        }

        void CopyForPrompt()
        {
            var legend = m_LegendField.value as TileLegend;
            var o = m_OriginField.value;
            EditorGUIUtility.systemCopyBuffer =
                $"Tilemap region, first row is the top, bottom-left cell is ({o.x},{o.y}).\n" +
                $"{(legend != null ? legend.Describe() : "")}, ' '=keep existing\n```\n{m_Text.value}\n```";
            m_Status.text = "Copied to clipboard.";
        }
    }
}
