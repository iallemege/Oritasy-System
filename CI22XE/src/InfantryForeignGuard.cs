using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace Oritasy
{
    /// <summary>Oritasy infantry is exclusive: other *Infantry* / *Soldier* plugins are turned off.</summary>
    internal static class InfantryForeignGuard
    {
        internal static void DisableOthers(ManualLogSource log)
        {
            string dir = null;
            try { dir = Paths.PluginPath; }
            catch { }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            string[] files = null;
            try { files = Directory.GetFiles(dir, "*.dll"); }
            catch { return; }
            if (files == null)
                return;
            int n = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                    continue;
                string lower = name.ToLowerInvariant();
                if (lower == "groundarsenal.dll")
                    continue;
                if (lower.StartsWith("groundarsenal"))
                    continue;
                if (lower.StartsWith("oritasy") && lower.IndexOf("infantry") < 0)
                    continue;
                bool hit = lower.IndexOf("infantry") >= 0
                    || lower.IndexOf("soldier") >= 0
                    || lower.IndexOf("fpsinf") >= 0;
                if (!hit)
                    continue;
                string off = path + ".off";
                try
                {
                    if (File.Exists(off))
                        File.Delete(off);
                    File.Move(path, off);
                    n++;
                    if (log != null)
                        log.LogInfo("Disabled foreign infantry plugin: " + name);
                }
                catch (System.Exception ex)
                {
                    if (log != null)
                        log.LogWarning("Could not disable " + name + ": " + ex.Message);
                }
            }
            if (n > 0 && log != null)
                log.LogInfo("Oritasy infantry exclusive: disabled " + n + " other plugin(s)");
        }
    }
}
