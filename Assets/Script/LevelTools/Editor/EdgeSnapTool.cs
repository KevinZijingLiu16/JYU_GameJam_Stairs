using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Smoking.LevelTools.Editor
{
    // Scene view hook: while a selection is dragged, previews where it will snap; on release,
    // moves it edge-to-edge onto the nearest platform / stair (one Undo step, Ctrl+Z to keep the raw drop).
    [InitializeOnLoad]
    internal static class EdgeSnapTool
    {
        private const string MenuRoot = "Tools/Smoking/Level Tools/";
        private static readonly Color EdgeColor = new Color(1f, 0.85f, 0.1f);
        private static readonly Color LandingColor = new Color(0.2f, 0.9f, 1f);
        private static readonly Color TargetColor = new Color(1f, 1f, 1f, 0.6f);

        private static Transform[] tracked;
        private static Matrix4x4[] startMatrices;
        private static bool dragging;
        private static List<SurfaceBox> dragOthers;
        private static HashSet<BoxCollider> dragMoved;
        private static SnapCandidate preview;
        private static Matrix4x4 previewKey;

        static EdgeSnapTool() => SceneView.duringSceneGui += OnSceneGUI;

        [MenuItem(MenuRoot + "Snap Selection &s", priority = 0)]
        public static void SnapSelection()
        {
            SceneView view = SceneView.lastActiveSceneView;
            bool snapped = Snap(Selection.transforms);
            if (view != null) view.ShowNotification(new GUIContent(snapped ? "Snapped" : "No edge nearby"), 0.6);
        }

        [MenuItem(MenuRoot + "Snap Selection &s", true)]
        private static bool CanSnapSelection() => Selection.transforms.Length > 0;

        [MenuItem(MenuRoot + "Auto Snap On Drag", priority = 20)]
        private static void ToggleAutoSnap() => LevelToolsSettings.AutoSnap = !LevelToolsSettings.AutoSnap;

        [MenuItem(MenuRoot + "Auto Snap On Drag", true)]
        private static bool ToggleAutoSnapValidate()
        {
            Menu.SetChecked(MenuRoot + "Auto Snap On Drag", LevelToolsSettings.AutoSnap);
            return true;
        }

        // Moves the roots so they connect to the nearest edge. Returns false if nothing is in reach.
        public static bool Snap(Transform[] roots)
        {
            roots = roots.Where(t => t != null).ToArray();
            if (roots.Length == 0) return false;
            var movedSet = new HashSet<BoxCollider>();
            List<Bounds> moved = SurfaceBoxes.Under(roots, movedSet);
            SnapCandidate snap = EdgeSnapSolver.Solve(moved, SurfaceBoxes.InStage(movedSet));
            if (snap == null) return false;
            if (snap.Offset.sqrMagnitude < 1e-10f) return true;

            Undo.RecordObjects(roots, "Edge Snap");
            foreach (Transform t in roots) t.position += snap.Offset;
            return true;
        }

        private static void OnSceneGUI(SceneView view)
        {
            if (!LevelToolsSettings.AutoSnap || EditorApplication.isPlaying)
            {
                Clear();
                return;
            }

            Event e = Event.current;
            switch (e.rawType)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    Capture();
                    break;
                case EventType.MouseDrag when tracked != null && GUIUtility.hotControl != 0:
                    dragging = true;
                    break;
                case EventType.MouseUp when dragging:
                    Finish();
                    break;
            }
            // Missed the mouse-up (released over another window): finish anyway.
            if (dragging && GUIUtility.hotControl == 0 && e.rawType != EventType.MouseDrag) Finish();

            if (dragging && e.type == EventType.Repaint) DrawPreview();
        }

        private static void Capture()
        {
            tracked = Selection.transforms;
            startMatrices = tracked.Select(t => t.localToWorldMatrix).ToArray();
            dragging = false;
            dragOthers = null;
            preview = null;
        }

        private static void Finish()
        {
            Transform[] roots = tracked;
            bool moved = HasMoved();
            Clear();
            if (moved) EditorApplication.delayCall += () => Snap(roots);
        }

        private static void Clear()
        {
            tracked = null;
            startMatrices = null;
            dragging = false;
            dragOthers = null;
            dragMoved = null;
            preview = null;
        }

        private static bool HasMoved()
        {
            if (tracked == null) return false;
            for (int i = 0; i < tracked.Length; i++)
                if (tracked[i] != null && tracked[i].localToWorldMatrix != startMatrices[i]) return true;
            return false;
        }

        private static void DrawPreview()
        {
            if (!HasMoved()) return;
            Matrix4x4 key = tracked[0] != null ? tracked[0].localToWorldMatrix : Matrix4x4.identity;
            if (dragOthers == null || key != previewKey)
            {
                previewKey = key;
                var movedSet = new HashSet<BoxCollider>();
                List<Bounds> moved = SurfaceBoxes.Under(tracked, movedSet);
                if (dragOthers == null || !movedSet.SetEquals(dragMoved))
                {
                    dragMoved = movedSet;
                    dragOthers = SurfaceBoxes.InStage(movedSet);
                }
                preview = EdgeSnapSolver.Solve(moved, dragOthers);
            }
            if (preview == null) return;

            Bounds landing = preview.Moved;
            landing.center += preview.Offset;
            DrawTop(preview.Target, TargetColor, 2f);
            DrawTop(landing, LandingColor, 3f);
            Handles.color = EdgeColor;
            Handles.DrawAAPolyLine(8f, preview.EdgeFrom, preview.EdgeTo);
        }

        private static void DrawTop(Bounds b, Color color, float width)
        {
            float y = b.max.y + 0.01f;
            Handles.color = color;
            Handles.DrawAAPolyLine(width,
                new Vector3(b.min.x, y, b.min.z), new Vector3(b.max.x, y, b.min.z),
                new Vector3(b.max.x, y, b.max.z), new Vector3(b.min.x, y, b.max.z),
                new Vector3(b.min.x, y, b.min.z));
        }
    }
}
