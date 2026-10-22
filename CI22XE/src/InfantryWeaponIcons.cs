using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Hangar-style 512x256 black/white side-profile icons (same format as
    /// vanilla weaponIcon_*.png, 100 PPU). Loaded from GroundArsenal/.
    /// </summary>
    internal static class InfantryWeaponIcons
    {
        internal const int PixelW = 512;
        internal const int PixelH = 256;
        internal const float Ppu = 100f;

        private static readonly string[] Files = new string[]
        {
            "weaponIcon_m16.png",
            "weaponIcon_ak47.png",
            "weaponIcon_pistol.png",
            "weaponIcon_shotgun.png",
            "weaponIcon_sniper.png",
            "weaponIcon_at4.png",
            "weaponIcon_rpg7.png",
            "weaponIcon_rbs70.png",
            "weaponIcon_g11.png"
        };

        private static Texture2D[] _tex;
        private static bool _tried;

        internal static Texture2D Get(InfantryGun g)
        {
            Ensure();
            int i = (int)g;
            if (_tex == null || i < 0 || i >= _tex.Length)
                return null;
            return _tex[i];
        }

        internal static void Ensure()
        {
            if (_tried)
                return;
            _tried = true;
            _tex = new Texture2D[InfantryCombat.GunCount];
            string dir = ResolveDir();
            if (string.IsNullOrEmpty(dir))
                return;
            int n = 0;
            for (int i = 0; i < Files.Length && i < InfantryCombat.GunCount; i++)
            {
                Texture2D t = LoadPng(Path.Combine(dir, Files[i]));
                _tex[i] = t;
                if (t != null)
                    n++;
            }
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Infantry weapon icons " + n + "/" + InfantryCombat.GunCount
                    + " from " + dir);
        }

        private static string ResolveDir()
        {
            string dir = null;
            string[] names = new string[] { "GroundArsenal", "OritasyInfantry" };
            for (int i = 0; i < names.Length; i++)
            {
                try { dir = Path.Combine(Paths.PluginPath, names[i]); }
                catch { dir = null; }
                if (DirHasIcons(dir))
                    return dir;
            }
            try
            {
                string asm = typeof(InfantryWeaponIcons).Assembly.Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        string beside = Path.Combine(Path.GetDirectoryName(asm), names[i]);
                        if (DirHasIcons(beside))
                            return beside;
                    }
                }
            }
            catch { }
            return dir;
        }

        private static bool DirHasIcons(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;
            return File.Exists(Path.Combine(dir, Files[0]));
        }

        internal static Texture2D LoadPngFile(string path)
        {
            return LoadPng(path);
        }

        private static Texture2D LoadPng(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.name = Path.GetFileNameWithoutExtension(path);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.hideFlags = HideFlags.HideAndDontSave;
                return tex;
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Infantry icon " + path + ": " + ex.Message);
                return null;
            }
        }
    }
}
