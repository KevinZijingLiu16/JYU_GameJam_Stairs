using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Smoking.LevelTools.Editor
{
    // Scene view panel (toggle it from the Overlays menu, or the ` key) for the level tools.
    [Overlay(typeof(SceneView), "smoking-level-tools", "Level Tools", true)]
    internal sealed class LevelToolsOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { minWidth = 210, paddingLeft = 4, paddingRight = 4, paddingBottom = 4 } };

            var snap = new Toggle("Edge snap on drag") { value = LevelToolsSettings.AutoSnap };
            snap.RegisterValueChangedCallback(e => LevelToolsSettings.AutoSnap = e.newValue);
            root.Add(snap);

            var radius = new FloatField("Snap radius (m)") { value = LevelToolsSettings.SnapRadius };
            radius.RegisterValueChangedCallback(e => LevelToolsSettings.SnapRadius = e.newValue);
            root.Add(radius);

            root.Add(new Button(EdgeSnapTool.SnapSelection) { text = "Snap selection  (Alt+S)" });

            var check = new Toggle("Path check") { value = LevelToolsSettings.ShowPathCheck };
            check.RegisterValueChangedCallback(e =>
            {
                LevelToolsSettings.ShowPathCheck = e.newValue;
                SceneView.RepaintAll();
            });
            root.Add(check);

            var summary = new Label { style = { marginTop = 2, whiteSpace = WhiteSpace.Normal } };
            root.Add(summary);
            var next = new Button(PathChecker.FrameNextIssue) { text = "Frame next problem" };
            root.Add(next);

            // Keep in sync with the Tools menu toggles and the latest check.
            root.schedule.Execute(() =>
            {
                snap.SetValueWithoutNotify(LevelToolsSettings.AutoSnap);
                check.SetValueWithoutNotify(LevelToolsSettings.ShowPathCheck);
                summary.text = PathChecker.Summary;
                next.SetEnabled(LevelToolsSettings.ShowPathCheck && PathChecker.Issues.Count > 0);
            }).Every(300);

            return root;
        }
    }
}
