using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Smoking.LevelTools.Editor
{
    internal sealed class SnapCandidate
    {
        public Vector3 Offset;
        public float Cost;
        public Bounds Moved;   // the moved box that connects (before Offset)
        public Bounds Target;  // the box it connects to
        public Vector3 EdgeFrom, EdgeTo; // the shared edge, on the target's top face
    }

    // Finds the smallest move that puts one of the moved boxes edge-to-edge with a nearby box:
    //  - side faces touch (no gap), on X or Z, so straight runs, L corners and T joints all work;
    //  - the piece slides along the edge to line up ends or centres when close;
    //  - tops end up flush, or exactly one stair rise apart (stairs onto platforms / stairs),
    //    and never more than a walkable step apart;
    //  - the move must not push the piece further into any other box than it already is.
    internal static class EdgeSnapSolver
    {
        private const float PenetrationSlack = 0.02f;

        public static SnapCandidate Solve(IReadOnlyList<Bounds> moved, IReadOnlyList<SurfaceBox> others)
        {
            if (moved.Count == 0 || others.Count == 0) return null;

            float radius = LevelToolsSettings.SnapRadius;
            Bounds area = moved[0];
            foreach (Bounds b in moved) area.Encapsulate(b);
            area.Expand(radius * 2f + 0.1f);
            List<SurfaceBox> near = others.Where(o => o.Bounds.Intersects(area)).ToList();
            if (near.Count == 0) return null;

            float movedRise = SurfaceBoxes.DetectRise(moved.Select(b => b.max.y));
            var pieceRise = near.GroupBy(o => o.Piece)
                .ToDictionary(g => g.Key, g => SurfaceBoxes.DetectRise(g.Select(o => o.Bounds.max.y)));

            var candidates = new List<SnapCandidate>();
            foreach (Bounds a in moved)
            {
                foreach (SurfaceBox other in near)
                {
                    float rise = movedRise > 0f ? movedRise : pieceRise[other.Piece];
                    AddCandidates(a, other.Bounds, 0, rise, radius, candidates);
                    AddCandidates(a, other.Bounds, 2, rise, radius, candidates);
                }
            }
            if (candidates.Count == 0) return null;
            candidates.Sort((x, y) => x.Cost.CompareTo(y.Cost));

            // How deep each moved box already sits in each nearby box (e.g. stairs sunk into the floor).
            var baseline = new float[moved.Count, near.Count];
            for (int i = 0; i < moved.Count; i++)
                for (int j = 0; j < near.Count; j++)
                    baseline[i, j] = Penetration(moved[i], near[j].Bounds, Vector3.zero);

            foreach (SnapCandidate c in candidates)
                if (!MakesPenetrationWorse(moved, near, baseline, c.Offset)) return c;
            return null;
        }

        // Candidates where box a's side face meets box b's side face across the given axis (0 = X, 2 = Z).
        private static void AddCandidates(Bounds a, Bounds b, int axis, float rise, float radius, List<SnapCandidate> output)
        {
            int along = 2 - axis;
            for (int side = 0; side < 2; side++)
            {
                // side 0: a ends up on b's min side; side 1: on b's max side.
                float dn = side == 0 ? b.min[axis] - a.max[axis] : b.max[axis] - a.min[axis];
                if (Mathf.Abs(dn) > radius) continue;

                float da = Align(a, b, along);
                float lo = Mathf.Max(a.min[along] + da, b.min[along]);
                float hi = Mathf.Min(a.max[along] + da, b.max[along]);
                float needed = Mathf.Min(LevelToolsSettings.MinOverlap, 0.9f * Mathf.Min(a.size[along], b.size[along]));
                if (hi - lo < needed) continue;

                if (!TrySnapHeight(a.max.y, b.max.y, rise, out float dy)) continue;

                Vector3 offset = Vector3.up * dy;
                offset[axis] = dn;
                offset[along] = da;

                Vector3 from = new Vector3(0f, b.max.y, 0f), to = from;
                from[axis] = to[axis] = side == 0 ? b.min[axis] : b.max[axis];
                from[along] = lo;
                to[along] = hi;

                output.Add(new SnapCandidate
                {
                    Offset = offset,
                    Cost = Mathf.Abs(dn) + 0.5f * Mathf.Abs(da) + 0.5f * Mathf.Abs(dy),
                    Moved = a,
                    Target = b,
                    EdgeFrom = from,
                    EdgeTo = to,
                });
            }
        }

        // Slide along the shared edge to line up the min ends, max ends or centres, whichever is closest.
        private static float Align(Bounds a, Bounds b, int along)
        {
            float best = b.min[along] - a.min[along];
            float maxEnds = b.max[along] - a.max[along];
            float centres = b.center[along] - a.center[along];
            if (Mathf.Abs(maxEnds) < Mathf.Abs(best)) best = maxEnds;
            if (Mathf.Abs(centres) < Mathf.Abs(best)) best = centres;
            return Mathf.Abs(best) <= LevelToolsSettings.AlignRadius ? best : 0f;
        }

        // Vertical move that leaves the tops flush or one rise apart. A deliberate small step
        // (within the walkable step height) that is not close to either is left as it is.
        private static bool TrySnapHeight(float aTop, float bTop, float rise, out float dy)
        {
            float diff = bTop - aTop;
            dy = diff;
            if (rise > 0f)
            {
                if (Mathf.Abs(diff - rise) < Mathf.Abs(dy)) dy = diff - rise;
                if (Mathf.Abs(diff + rise) < Mathf.Abs(dy)) dy = diff + rise;
            }
            if (Mathf.Abs(dy) <= LevelToolsSettings.HeightTolerance) return true;
            dy = 0f;
            return Mathf.Abs(diff) <= LevelToolsSettings.MaxStepHeight;
        }

        private static bool MakesPenetrationWorse(IReadOnlyList<Bounds> moved, List<SurfaceBox> near, float[,] baseline, Vector3 offset)
        {
            for (int i = 0; i < moved.Count; i++)
                for (int j = 0; j < near.Count; j++)
                    if (Penetration(moved[i], near[j].Bounds, offset) > baseline[i, j] + PenetrationSlack) return true;
            return false;
        }

        // Shortest distance that would separate the two boxes (0 when they only touch or are apart).
        private static float Penetration(Bounds a, Bounds b, Vector3 offset)
        {
            float depth = float.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                float overlap = Mathf.Min(a.max[k] + offset[k], b.max[k]) - Mathf.Max(a.min[k] + offset[k], b.min[k]);
                if (overlap <= 0f) return 0f;
                depth = Mathf.Min(depth, overlap);
            }
            return depth;
        }
    }
}
