using System;
using System.IO;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Loadout / encyclopedia schematic for AAM-2CV, ACM-119, and ACNM-118.
    /// Source PNG is already nose-left (vanilla). Fit pixel size + PPU to the
    /// donor weaponIcon so hangar / HUD icons match stock schematics.
    /// </summary>
    internal static class VeyrnMissileIcon
    {
        private const string FileName = "VeyrnAam_icon.png";
        private const int FallbackWidth = 512;
        private const int FallbackHeight = 128;
        private const float FallbackPpu = 100f;
        private static Sprite _sprite;
        private static bool _loadAttempted;

        internal static Sprite GetWeaponIcon()
        {
            return GetWeaponIcon(null);
        }

        internal static Sprite GetWeaponIcon(Sprite donor)
        {
            if (_sprite != null)
                return _sprite;

            string path = AgmTBusVisual.ResolveAssetPath(FileName);
            if (string.IsNullOrEmpty(path))
            {
                if (!_loadAttempted && Plugin.Log != null)
                    Plugin.Log.LogWarning("Veyrn missile icon: " + FileName + " not found in WeXonAssets/");
                _loadAttempted = true;
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.Destroy(tex);
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Veyrn missile icon: failed to decode " + path);
                    return null;
                }

                int wantW = FallbackWidth;
                int wantH = FallbackHeight;
                float ppu = FallbackPpu;
                ReadDonorSize(donor, ref wantW, ref wantH, ref ppu);

                Texture2D fitted = FitToCanvas(tex, wantW, wantH);
                if (fitted != tex)
                    UnityEngine.Object.Destroy(tex);

                fitted.name = "VeyrnAam_IconTex";
                fitted.wrapMode = TextureWrapMode.Clamp;
                fitted.filterMode = FilterMode.Bilinear;
                _sprite = Sprite.Create(
                    fitted,
                    new Rect(0f, 0f, fitted.width, fitted.height),
                    new Vector2(0.5f, 0.5f),
                    ppu);
                _sprite.name = "VeyrnAam_Icon";
                _loadAttempted = true;
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Veyrn missile icon " + fitted.width + "x" + fitted.height
                        + " ppu=" + ppu.ToString("0.##") + " from " + path);
                return _sprite;
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Veyrn missile icon load failed: " + ex.Message);
                return null;
            }
        }

        internal static void ApplyTo(WeaponInfo info)
        {
            if (info == null)
                return;
            Sprite donor = null;
            try { donor = info.weaponIcon; }
            catch { }
            Sprite icon = GetWeaponIcon(donor);
            if (icon == null)
                return;
            try { info.weaponIcon = icon; }
            catch { }
        }

        private static void ReadDonorSize(Sprite donor, ref int wantW, ref int wantH, ref float ppu)
        {
            if (donor == null)
                return;
            try
            {
                Rect r = donor.rect;
                int w = Mathf.RoundToInt(r.width);
                int h = Mathf.RoundToInt(r.height);
                if (w >= 16 && h >= 8 && w <= 2048 && h <= 1024)
                {
                    wantW = w;
                    wantH = h;
                }
                if (donor.pixelsPerUnit > 1f)
                    ppu = donor.pixelsPerUnit;
            }
            catch { }
        }

        /// <summary>Letterbox-scale src into dw x dh (black pad). Nearest-neighbor keeps line art crisp.</summary>
        private static Texture2D FitToCanvas(Texture2D src, int dw, int dh)
        {
            if (src == null)
                return src;
            if (dw < 8)
                dw = FallbackWidth;
            if (dh < 8)
                dh = FallbackHeight;
            if (src.width == dw && src.height == dh)
                return src;

            Texture2D dst = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
            Color[] fill = new Color[dw * dh];
            Color black = new Color(0f, 0f, 0f, 1f);
            for (int i = 0; i < fill.Length; i++)
                fill[i] = black;

            float sx = (float)dw / (float)src.width;
            float sy = (float)dh / (float)src.height;
            float scale = sx < sy ? sx : sy;
            int nw = Mathf.Max(1, Mathf.RoundToInt(src.width * scale));
            int nh = Mathf.Max(1, Mathf.RoundToInt(src.height * scale));
            int ox = (dw - nw) / 2;
            int oy = (dh - nh) / 2;

            Color[] srcPx = src.GetPixels();
            int sw = src.width;
            int sh = src.height;
            for (int y = 0; y < nh; y++)
            {
                int syi = (int)(((y + 0.5f) * sh) / nh);
                if (syi < 0)
                    syi = 0;
                if (syi >= sh)
                    syi = sh - 1;
                int srcRow = syi * sw;
                int dstRow = (oy + y) * dw + ox;
                for (int x = 0; x < nw; x++)
                {
                    int sxi = (int)(((x + 0.5f) * sw) / nw);
                    if (sxi < 0)
                        sxi = 0;
                    if (sxi >= sw)
                        sxi = sw - 1;
                    fill[dstRow + x] = srcPx[srcRow + sxi];
                }
            }

            dst.SetPixels(fill);
            dst.Apply(false, false);
            return dst;
        }
    }
}
