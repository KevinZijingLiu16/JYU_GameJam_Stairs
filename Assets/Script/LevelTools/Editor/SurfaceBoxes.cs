using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Smoking.LevelTools.Editor
{
    // A solid, axis-aligned BoxCollider in world space. Its top face is a walkable rectangle.
    internal readonly struct SurfaceBox
    {
        public readonly Bounds Bounds;
        // Colliders sharing a parent form one piece (e.g. the Step_N children of a staircase).
        public readonly Transform Piece;

        public SurfaceBox(Bounds bounds, Transform piece) { Bounds = bounds; Piece = piece; }
    }

    internal static class SurfaceBoxes
    {
        // Every usable BoxCollider under the given roots.
        public static List<Bounds> Under(IEnumerable<Transform> roots, HashSet<BoxCollider> collected)
        {
            var result = new List<Bounds>();
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>())
                {
                    if (!collected.Add(box)) continue;
                    if (TryGetBounds(box, out Bounds bounds)) result.Add(bounds);
                }
            }
            return result;
        }

        // Every usable BoxCollider in the open scene(s) / prefab stage, except the excluded ones.
        public static List<SurfaceBox> InStage(HashSet<BoxCollider> exclude)
        {
            var result = new List<SurfaceBox>();
            foreach (BoxCollider box in StageUtility.GetCurrentStageHandle().FindComponentsOfType<BoxCollider>())
            {
                if (exclude.Contains(box) || !TryGetBounds(box, out Bounds bounds)) continue;
                Transform t = box.transform;
                result.Add(new SurfaceBox(bounds, t.parent != null ? t.parent : t));
            }
            return result;
        }

        // World box of a collider, computed from the transform so it is right mid-drag
        // (Collider.bounds lags until physics syncs). Rotated (non-90°) boxes are skipped.
        public static bool TryGetBounds(BoxCollider box, out Bounds bounds)
        {
            bounds = default;
            if (!box.enabled || box.isTrigger || !box.gameObject.activeInHierarchy) return false;
            if (!IsAxisAligned(box.transform.rotation)) return false;

            Matrix4x4 m = box.transform.localToWorldMatrix;
            Vector3 half = box.size * 0.5f;
            Vector3 extents = new Vector3(
                Mathf.Abs(m.m00) * half.x + Mathf.Abs(m.m01) * half.y + Mathf.Abs(m.m02) * half.z,
                Mathf.Abs(m.m10) * half.x + Mathf.Abs(m.m11) * half.y + Mathf.Abs(m.m12) * half.z,
                Mathf.Abs(m.m20) * half.x + Mathf.Abs(m.m21) * half.y + Mathf.Abs(m.m22) * half.z);
            bounds = new Bounds(m.MultiplyPoint3x4(box.center), extents * 2f);
            return true;
        }

        // The usual height difference between neighbouring step tops, or 0 if the tops don't form stairs.
        public static float DetectRise(IEnumerable<float> tops)
        {
            List<float> levels = tops.Select(Round).Distinct().OrderBy(y => y).ToList();
            var counts = new Dictionary<float, int>();
            for (int i = 1; i < levels.Count; i++)
            {
                float rise = Round(levels[i] - levels[i - 1]);
                if (rise < 0.02f || rise > LevelToolsSettings.MaxStepHeight + 0.001f) continue;
                counts.TryGetValue(rise, out int n);
                counts[rise] = n + 1;
            }
            return counts.Count == 0 ? 0f : counts.OrderByDescending(kv => kv.Value).First().Key;
        }

        private static float Round(float v) => Mathf.Round(v * 200f) / 200f;

        private static bool IsAxisAligned(Quaternion q) =>
            IsWorldAxis(q * Vector3.right) && IsWorldAxis(q * Vector3.up) && IsWorldAxis(q * Vector3.forward);

        private static bool IsWorldAxis(Vector3 v) =>
            Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z))) > 0.999f;
    }
}
