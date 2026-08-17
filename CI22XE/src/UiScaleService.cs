using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Global IMGUI scale: when the game resolution exceeds the design reference
    /// (default 1920×1080), scale the whole Oritasy/WeXon UI by LargeFactor (1.5).
    /// Layout must use Width/Height (logical px); WorldToScreen points use FromScreen*.
    /// </summary>
    internal static class UiScaleService
    {
        internal const float DefaultRefWidth = 1920f;
        internal const float DefaultRefHeight = 1080f;
        internal const float DefaultLargeFactor = 1.5f;

        private static Matrix4x4 _prevMatrix;
        private static bool _pushed;
        private static float _cachedScale = 1f;
        private static int _cachedSw;
        private static int _cachedSh;

        internal static float Scale
        {
            get
            {
                RefreshCache();
                return _cachedScale;
            }
        }

        /// <summary>Logical GUI width (Screen.width / Scale).</summary>
        internal static float Width
        {
            get
            {
                RefreshCache();
                return _cachedSw / _cachedScale;
            }
        }

        /// <summary>Logical GUI height (Screen.height / Scale).</summary>
        internal static float Height
        {
            get
            {
                RefreshCache();
                return _cachedSh / _cachedScale;
            }
        }

        internal static bool IsLarge
        {
            get { return Scale > 1.001f; }
        }

        private static void RefreshCache()
        {
            int sw = Screen.width;
            int sh = Screen.height;
            if (sw == _cachedSw && sh == _cachedSh && _cachedScale > 0f)
                return;
            _cachedSw = sw;
            _cachedSh = sh;
            _cachedScale = ComputeScale(sw, sh);
        }

        internal static float ComputeScale(int screenW, int screenH)
        {
            float refW = DefaultRefWidth;
            float refH = DefaultRefHeight;
            float large = DefaultLargeFactor;
            try
            {
                if (Plugin.UiScaleEnabled != null && !Plugin.UiScaleEnabled.Value)
                    return 1f;
                if (Plugin.UiScaleRefWidth != null && Plugin.UiScaleRefWidth.Value > 64f)
                    refW = Plugin.UiScaleRefWidth.Value;
                if (Plugin.UiScaleRefHeight != null && Plugin.UiScaleRefHeight.Value > 64f)
                    refH = Plugin.UiScaleRefHeight.Value;
                if (Plugin.UiScaleLargeFactor != null && Plugin.UiScaleLargeFactor.Value > 1.01f)
                    large = Plugin.UiScaleLargeFactor.Value;
            }
            catch { }

            if (screenW > refW + 0.5f || screenH > refH + 0.5f)
                return large;
            return 1f;
        }

        /// <summary>Call at the start of OnGUI (after skin). Nest-safe.</summary>
        internal static void BeginGui()
        {
            RefreshCache();
            if (_pushed || _cachedScale <= 1.001f)
                return;
            _prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(_cachedScale, _cachedScale, 1f)) * _prevMatrix;
            _pushed = true;
        }

        internal static void EndGui()
        {
            if (!_pushed)
                return;
            GUI.matrix = _prevMatrix;
            _pushed = false;
        }

        /// <summary>
        /// Line in current GUI space. Do not use RotateAroundPivot — it breaks when GUI.matrix is scaled.
        /// </summary>
        internal static void DrawLine(Vector2 a, Vector2 b, float thickness)
        {
            DrawLine(a, b, thickness, Texture2D.whiteTexture);
        }

        internal static void DrawLine(Vector2 a, Vector2 b, float thickness, Texture tex)
        {
            if (tex == null)
                tex = Texture2D.whiteTexture;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f)
                return;
            if (thickness < 1f)
                thickness = 1f;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = prev * Matrix4x4.TRS(
                new Vector3(a.x, a.y, 0f),
                Quaternion.Euler(0f, 0f, angle),
                Vector3.one);
            GUI.DrawTexture(new Rect(0f, -thickness * 0.5f, len, thickness), tex);
            GUI.matrix = prev;
        }

        internal static void DrawRotatedQuad(Vector2 center, float size, float angleDeg)
        {
            if (size < 0.5f)
                return;
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = prev * Matrix4x4.TRS(
                new Vector3(center.x, center.y, 0f),
                Quaternion.Euler(0f, 0f, angleDeg),
                Vector3.one);
            GUI.DrawTexture(new Rect(-size * 0.5f, -size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.matrix = prev;
        }

        internal static float FromScreenX(float screenX)
        {
            return screenX / Scale;
        }

        /// <summary>Convert Camera.WorldToScreenPoint.y (bottom-up) to GUI Y (top-down, logical).</summary>
        internal static float FromScreenYFlipped(float screenYBottomUp)
        {
            return (Screen.height - screenYBottomUp) / Scale;
        }
    }
}
