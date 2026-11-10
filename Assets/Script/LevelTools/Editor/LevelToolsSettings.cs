using UnityEditor;

namespace Smoking.LevelTools.Editor
{
    // Per-user tool settings, stored in EditorPrefs (not in the project).
    internal static class LevelToolsSettings
    {
        private const string Prefix = "Smoking.LevelTools.";

        private static readonly BoolPref autoSnap = new BoolPref(Prefix + "AutoSnap", true);
        private static readonly BoolPref showPathCheck = new BoolPref(Prefix + "ShowPathCheck", true);
        private static readonly FloatPref snapRadius = new FloatPref(Prefix + "SnapRadius", 0.5f);

        // Snap pieces edge-to-edge after every drag in the Scene view.
        public static bool AutoSnap { get => autoSnap.Value; set => autoSnap.Value = value; }
        // Draw the walkability check along every WalkPath.
        public static bool ShowPathCheck { get => showPathCheck.Value; set => showPathCheck.Value = value; }
        // How far (m) a dragged edge reaches to find a neighbouring edge.
        public static float SnapRadius { get => snapRadius.Value; set => snapRadius.Value = value < 0.05f ? 0.05f : value; }

        // How far along the shared edge the piece slides to line up ends / centres.
        public const float AlignRadius = 0.35f;
        // How far up/down the piece moves to become flush (or exactly one stair rise apart).
        public const float HeightTolerance = 0.15f;
        // Matches PathWalker.maxStepHeight's default; the snap never leaves a bigger step.
        public const float MaxStepHeight = 0.4f;
        // Shortest shared edge that still counts as a connection.
        public const float MinOverlap = 0.2f;

        private sealed class BoolPref
        {
            private readonly string key;
            private bool value;
            public BoolPref(string key, bool fallback) { this.key = key; value = EditorPrefs.GetBool(key, fallback); }
            public bool Value { get => value; set { this.value = value; EditorPrefs.SetBool(key, value); } }
        }

        private sealed class FloatPref
        {
            private readonly string key;
            private float value;
            public FloatPref(string key, float fallback) { this.key = key; value = EditorPrefs.GetFloat(key, fallback); }
            public float Value { get => value; set { this.value = value; EditorPrefs.SetFloat(key, value); } }
        }
    }
}
