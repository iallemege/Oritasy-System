using System;
using System.IO;
using BepInEx;

namespace WeXon
{
    /// <summary>
    /// Copy com.qiaochen.wexon.cfg → com.iallemege.wexon.cfg once (standalone WeXon).
    /// Combined pack uses the Oritasy host config instead.
    /// </summary>
    internal static class ConfigGuidMigrate
    {
        internal static void CopyLegacy(string newGuid, string legacyGuid)
        {
            if (string.IsNullOrEmpty(newGuid) || string.IsNullOrEmpty(legacyGuid))
                return;
            if (string.Equals(newGuid, legacyGuid, StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                string dir = Paths.ConfigPath;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return;
                string dst = Path.Combine(dir, newGuid + ".cfg");
                string src = Path.Combine(dir, legacyGuid + ".cfg");
                if (File.Exists(dst) || !File.Exists(src))
                    return;
                File.Copy(src, dst, false);
            }
            catch { }
        }
    }
}
