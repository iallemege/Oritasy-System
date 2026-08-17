using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Logical IMGUI size / scale bridge. When hosted inside Oritasy combined DLL,
    /// mirrors Oritasy.UiScaleService so Career / Arsenal / Accolades share the same 1.5× rule.
    /// </summary>
    internal static class GuiScale
    {
        internal static float Width
        {
            get
            {
#if ORITASY_COMBINED
                return Oritasy.UiScaleService.Width;
#else
                return Screen.width;
#endif
            }
        }

        internal static float Height
        {
            get
            {
#if ORITASY_COMBINED
                return Oritasy.UiScaleService.Height;
#else
                return Screen.height;
#endif
            }
        }

        internal static float FromScreenX(float screenX)
        {
#if ORITASY_COMBINED
            return Oritasy.UiScaleService.FromScreenX(screenX);
#else
            return screenX;
#endif
        }

        internal static float FromScreenYFlipped(float screenYBottomUp)
        {
#if ORITASY_COMBINED
            return Oritasy.UiScaleService.FromScreenYFlipped(screenYBottomUp);
#else
            return Screen.height - screenYBottomUp;
#endif
        }

        internal static void BeginGui()
        {
#if ORITASY_COMBINED
            Oritasy.UiScaleService.BeginGui();
#endif
        }

        internal static void EndGui()
        {
#if ORITASY_COMBINED
            Oritasy.UiScaleService.EndGui();
#endif
        }

        internal static void DrawLine(Vector2 a, Vector2 b, float thickness, Texture tex)
        {
#if ORITASY_COMBINED
            Oritasy.UiScaleService.DrawLine(a, b, thickness, tex);
#else
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f)
                return;
            if (thickness < 1f)
                thickness = 1f;
            if (tex == null)
                tex = Texture2D.whiteTexture;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = prev * Matrix4x4.TRS(
                new Vector3(a.x, a.y, 0f),
                Quaternion.Euler(0f, 0f, angle),
                Vector3.one);
            GUI.DrawTexture(new Rect(0f, -thickness * 0.5f, len, thickness), tex);
            GUI.matrix = prev;
#endif
        }
    }
}
