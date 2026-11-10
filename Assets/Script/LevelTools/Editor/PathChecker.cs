using System.Collections.Generic;
using Smoking.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Smoking.LevelTools.Editor
{
    // Walks every WalkPath in the editor with the same ground probes PathWalker uses at runtime
    // (same raycasts, step height, slope limit and look-ahead, read from the walker on that path),
    // and draws the result: green = walkable, red = gap (falls), orange = step too high (turns back),
    // purple = too steep. Only the path's forward direction is checked.
    [InitializeOnLoad]
    internal static class PathChecker
    {
        public enum Status { Ok, Gap, TooHigh, Steep }

        public struct Issue
        {
            public WalkPath Path;
            public Vector3 Point;
            public Status Status;
            public float Delta;
        }

        private struct Sample
        {
            public Vector3 Point;
            public Status Status;
        }

        private struct Probe
        {
            public float ProbeHeight, ProbeDepth, MaxStep, MaxSlope, ProbeAhead;
            public int Mask;

            public static readonly Probe Default = new Probe
            {
                ProbeHeight = 1.5f, ProbeDepth = 3f, MaxStep = 0.4f, MaxSlope = 20f, ProbeAhead = 0.25f, Mask = ~0,
            };

            public static Probe From(SerializedObject walker)
            {
                Probe p = Default;
                Read(walker, "probeHeight", ref p.ProbeHeight);
                Read(walker, "probeDepth", ref p.ProbeDepth);
                Read(walker, "maxStepHeight", ref p.MaxStep);
                Read(walker, "maxSlope", ref p.MaxSlope);
                Read(walker, "probeAhead", ref p.ProbeAhead);
                SerializedProperty mask = walker.FindProperty("groundMask");
                if (mask != null) p.Mask = mask.intValue;
                return p;
            }

            private static void Read(SerializedObject so, string name, ref float value)
            {
                SerializedProperty prop = so.FindProperty(name);
                if (prop != null) value = prop.floatValue;
            }
        }

        private const string MenuPath = "Tools/Smoking/Level Tools/Show Path Check";
        private const float SampleStep = 0.05f;
        private const double RefreshInterval = 0.25;

        private static readonly List<List<Sample>> paths = new List<List<Sample>>();
        private static readonly List<Issue> issues = new List<Issue>();
        private static double nextRefresh;
        private static bool dirty = true;
        private static int focusIndex = -1;
        private static GUIStyle labelStyle;

        public static IReadOnlyList<Issue> Issues => issues;
        public static int PathCount => paths.Count;

        static PathChecker()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => dirty = true;
            Undo.undoRedoPerformed += () => dirty = true;
            EditorSceneManager.sceneOpened += (_, __) => dirty = true;
            EditorApplication.update += () =>
            {
                // Redraw once things settle after an edit, so the colours never show a stale layout.
                if (dirty && LevelToolsSettings.ShowPathCheck && EditorApplication.timeSinceStartup >= nextRefresh)
                    SceneView.RepaintAll();
            };
        }

        [MenuItem(MenuPath, priority = 21)]
        private static void Toggle()
        {
            LevelToolsSettings.ShowPathCheck = !LevelToolsSettings.ShowPathCheck;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, LevelToolsSettings.ShowPathCheck);
            return true;
        }

        public static string Summary
        {
            get
            {
                if (!LevelToolsSettings.ShowPathCheck) return "Path check off";
                if (paths.Count == 0) return "No WalkPath in scene";
                return issues.Count == 0
                    ? $"{paths.Count} path(s): all walkable"
                    : $"{issues.Count} problem(s) on {paths.Count} path(s)";
            }
        }

        public static void FrameNextIssue()
        {
            if (issues.Count == 0) return;
            focusIndex = (focusIndex + 1) % issues.Count;
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null) view.Frame(new Bounds(issues[focusIndex].Point, Vector3.one * 3f), false);
        }

        private static void OnSceneGUI(SceneView view)
        {
            if (!LevelToolsSettings.ShowPathCheck || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Event.current.type != EventType.Repaint) return;

            if (EditorApplication.timeSinceStartup >= nextRefresh)
            {
                Refresh();
                dirty = false;
                nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            }
            Draw();
        }

        private static void Refresh()
        {
            paths.Clear();
            issues.Clear();
            Physics.SyncTransforms();

            StageHandle stage = StageUtility.GetCurrentStageHandle();
            var probes = new Dictionary<WalkPath, Probe>();
            foreach (PathWalker walker in stage.FindComponentsOfType<PathWalker>())
            {
                var so = new SerializedObject(walker);
                if (so.FindProperty("path")?.objectReferenceValue is WalkPath path && !probes.ContainsKey(path))
                    probes[path] = Probe.From(so);
            }

            foreach (WalkPath path in stage.FindComponentsOfType<WalkPath>())
            {
                if (!path.gameObject.activeInHierarchy) continue;
                if (!probes.TryGetValue(path, out Probe probe)) probe = Probe.Default;
                List<Sample> samples = Simulate(path, probe);
                if (samples != null) paths.Add(samples);
            }
        }

        // Mirrors PathWalker.Start / Update, sampling the path every few centimetres.
        private static List<Sample> Simulate(WalkPath path, Probe p)
        {
            var points = new List<Vector3>();
            foreach (Transform child in path.transform) points.Add(child.position);
            if (points.Count < 2) return null;

            var lengths = new List<float> { 0f };
            for (int i = 1; i < points.Count; i++) lengths.Add(lengths[i - 1] + Vector3.Distance(points[i - 1], points[i]));
            float length = lengths[lengths.Count - 1];

            PhysicsScene physics = path.gameObject.scene.GetPhysicsScene();
            float ground = points[0].y;
            if (TryGround(physics, points[0], p, out float startY, out _)) ground = startY;

            var samples = new List<Sample>();
            Status previous = Status.Ok;
            for (float d = 0f; ; d += SampleStep)
            {
                bool last = d >= length;
                if (last) d = length;

                Vector3 target = Evaluate(points, lengths, d, out Vector3 forward);
                target.y = ground;
                Status status = Status.Ok;
                float delta = 0f;

                if (TryWallAhead(physics, target, forward, ground, p, out float wallY, out bool wallSteep))
                {
                    status = wallSteep ? Status.Steep : Status.TooHigh;
                    delta = wallY - ground;
                }

                if (!TryGround(physics, target, p, out float groundY, out bool groundSteep))
                {
                    status = groundSteep ? Status.Steep : Status.Gap;
                }
                else if (groundY < ground - p.MaxStep)
                {
                    status = Status.Gap;
                    delta = groundY - ground;
                    ground = groundY;
                }
                else ground = groundY;

                Vector3 point = new Vector3(target.x, ground, target.z);
                samples.Add(new Sample { Point = point, Status = status });
                if (status != Status.Ok && status != previous)
                    issues.Add(new Issue { Path = path, Point = point, Status = status, Delta = delta });
                previous = status;
                if (last) break;
            }
            return samples;
        }

        private static Vector3 Evaluate(List<Vector3> points, List<float> lengths, float d, out Vector3 forward)
        {
            int i = 1;
            while (i < points.Count - 1 && lengths[i] < d) i++;
            float segment = lengths[i] - lengths[i - 1];
            float t = segment > 0f ? (d - lengths[i - 1]) / segment : 0f;
            forward = (points[i] - points[i - 1]).normalized;
            return Vector3.Lerp(points[i - 1], points[i], t);
        }

        private static bool TryWallAhead(PhysicsScene physics, Vector3 point, Vector3 forward, float ground, Probe p, out float hitY, out bool steep)
        {
            Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
            Vector3 origin = point + flat * p.ProbeAhead + Vector3.up * p.ProbeHeight;
            hitY = 0f;
            steep = false;
            if (!physics.Raycast(origin, Vector3.down, out RaycastHit hit, p.ProbeHeight + p.ProbeDepth, p.Mask, QueryTriggerInteraction.Ignore))
                return false;
            hitY = hit.point.y;
            steep = Vector3.Angle(hit.normal, Vector3.up) > p.MaxSlope;
            return hitY > ground + p.MaxStep || steep;
        }

        private static bool TryGround(PhysicsScene physics, Vector3 point, Probe p, out float y, out bool steep)
        {
            y = point.y;
            steep = false;
            if (!physics.Raycast(point + Vector3.up * p.ProbeHeight, Vector3.down, out RaycastHit hit, p.ProbeHeight + p.ProbeDepth, p.Mask, QueryTriggerInteraction.Ignore))
                return false;
            steep = Vector3.Angle(hit.normal, Vector3.up) > p.MaxSlope;
            if (steep) return false;
            y = hit.point.y;
            return true;
        }

        private static void Draw()
        {
            Vector3 lift = Vector3.up * 0.03f;
            var run = new List<Vector3>();
            foreach (List<Sample> samples in paths)
            {
                int start = 0;
                for (int i = 1; i <= samples.Count; i++)
                {
                    if (i < samples.Count && samples[i].Status == samples[start].Status) continue;
                    run.Clear();
                    for (int k = start; k < Mathf.Min(i + 1, samples.Count); k++) run.Add(samples[k].Point + lift);
                    if (run.Count == 1) run.Add(run[0]);
                    Status status = samples[start].Status;
                    Handles.color = ColorOf(status);
                    Handles.DrawAAPolyLine(status == Status.Ok ? 4f : 9f, run.ToArray());
                    start = i;
                }
            }

            if (labelStyle == null)
                labelStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white }, fontSize = 12 };
            foreach (Issue issue in issues)
            {
                Handles.color = ColorOf(issue.Status);
                float size = HandleUtility.GetHandleSize(issue.Point) * 0.15f;
                Handles.SphereHandleCap(0, issue.Point + lift, Quaternion.identity, size, EventType.Repaint);
                labelStyle.normal.textColor = ColorOf(issue.Status);
                Handles.Label(issue.Point + Vector3.up * 0.35f, LabelOf(issue), labelStyle);
            }
        }

        private static Color ColorOf(Status status)
        {
            switch (status)
            {
                case Status.Gap: return new Color(1f, 0.2f, 0.2f);
                case Status.TooHigh: return new Color(1f, 0.6f, 0.1f);
                case Status.Steep: return new Color(0.8f, 0.35f, 1f);
                default: return new Color(0.3f, 1f, 0.4f, 0.8f);
            }
        }

        private static string LabelOf(Issue issue)
        {
            switch (issue.Status)
            {
                case Status.Gap: return issue.Delta < 0f ? $"DROP {-issue.Delta:0.00}m" : "GAP";
                case Status.TooHigh: return $"TOO HIGH +{issue.Delta:0.00}m";
                default: return "TOO STEEP";
            }
        }
    }
}
