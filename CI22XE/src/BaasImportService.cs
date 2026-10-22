using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Loads BIA / Icarus *.baas (and leftover *.iaab). Hangars stay in Oritasy.
    /// Pack progress is reported to BIA.Runtime's boot bar.
    /// </summary>
    internal static class BaasImportService
    {
        private static bool _done;
        private static bool _started;
        private static bool _prepared;
        private static float _readyAt = -1f;
        private static readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Prepared = new List<string>(16);

        internal static bool Done
        {
            get { return _done; }
        }

        internal static void PrepareFiles()
        {
            if (_prepared)
                return;
            _prepared = true;
            Prepared.Clear();
            string dir = PluginDir();
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                CollectBaas(Prepared, dir);
            string plugins = PathsPlugin();
            if (!string.IsNullOrEmpty(plugins)
                && (string.IsNullOrEmpty(dir)
                    || !string.Equals(Path.GetFullPath(plugins), Path.GetFullPath(dir),
                        StringComparison.OrdinalIgnoreCase)))
                CollectBaas(Prepared, plugins);
            if (Prepared.Count == 0)
            {
                BiaBoot.SetStatus("No aircraft packs", "没有飞机包");
                BiaBoot.MarkDone();
                _done = true;
                return;
            }
            for (int i = 0; i < Prepared.Count; i++)
                BiaBoot.SetRow(FileLabel(Prepared[i]), 0, 1, false);
            BiaBoot.SetStatus(
                "Waiting for encyclopedia…",
                "等待百科全书…");
        }

        internal static void Tick()
        {
            if (_done || _started)
                return;
            try
            {
                if (MainMenu.State != MainMenu.LoadingState.Loaded)
                    return;
            }
            catch { return; }
            if (Encyclopedia.i == null)
                return;
            if (_readyAt < 0f)
            {
                // Leave the first Loaded frames to vanilla uGUI — sync LoadFromFile froze the menu.
                _readyAt = Time.unscaledTime + 0.55f;
                return;
            }
            if (Time.unscaledTime < _readyAt)
                return;
            if (Plugin.Instance == null)
                return;
            _started = true;
            Plugin.Instance.StartCoroutine(LoadAllRoutine());
        }

        private static IEnumerator LoadAllRoutine()
        {
            yield return null;
            PrepareFiles();
            List<string> files = Prepared;
            if (files.Count == 0)
            {
                FinishImport(0, 0);
                yield break;
            }
            BiaBoot.SetStatus("Loading aircraft packs…", "正在加载飞机包…");
            int imported = 0;
            int attempted = 0;
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                if (string.IsNullOrEmpty(path) || !Loaded.Add(path))
                    continue;
                attempted++;
                string label = FileLabel(path);
                BiaBoot.SetStatus("Loading " + label, "正在加载 " + label);
                BiaBoot.SetRow(label, 0, 2, false);

                AssetBundleCreateRequest req = BeginLoad(path);
                if (req == null)
                {
                    BiaBoot.SetRow(label, 0, 2, true);
                    continue;
                }
                float until = Time.unscaledTime + 10f;
                bool timedOut = false;
                while (!req.isDone)
                {
                    if (Time.unscaledTime >= until)
                    {
                        timedOut = true;
                        break;
                    }
                    yield return null;
                }
                if (timedOut)
                {
                    BiaBoot.SetRow(label, 0, 2, true);
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Baas timeout: " + Path.GetFileName(path));
                    continue;
                }

                AssetBundle bundle = req.assetBundle;
                if (bundle == null)
                {
                    BiaBoot.SetRow(label, 0, 2, true);
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Baas invalid: " + Path.GetFileName(path));
                    continue;
                }
                BiaBoot.SetRow(label, 1, 2, false);

                AssetBundleRequest assetsReq = BeginLoadAssets(bundle);
                if (assetsReq == null)
                {
                    BiaBoot.SetRow(label, 1, 2, true);
                    continue;
                }
                until = Time.unscaledTime + 10f;
                timedOut = false;
                while (!assetsReq.isDone)
                {
                    if (Time.unscaledTime >= until)
                    {
                        timedOut = true;
                        break;
                    }
                    yield return null;
                }
                if (timedOut)
                {
                    BiaBoot.SetRow(label, 1, 2, true);
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Baas assets timeout: " + Path.GetFileName(path));
                    continue;
                }

                if (AdoptAssets(assetsReq.allAssets))
                    imported++;
                BiaBoot.SetRow(label, 2, 2, false);
                IcarusAircraftRegistry.MarkDirty();
            }
            FinishImport(imported, attempted);
        }

        private static void FinishImport(int imported, int attempted)
        {
            _done = true;
            if (attempted <= 0)
                BiaBoot.SetStatus("No aircraft packs", "没有飞机包");
            else
                BiaBoot.SetStatus(
                    "Loaded " + imported.ToString() + " / " + attempted.ToString(),
                    "已加载 " + imported.ToString() + " / " + attempted.ToString());
            BiaBoot.MarkDone();
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Baas import: " + imported + " aircraft from " + attempted + " bundles.");
        }

        private static string FileLabel(string path)
        {
            try { return Path.GetFileName(path); }
            catch { return path; }
        }

        private static void CollectBaas(List<string> dest, string dir)
        {
            AddFiles(dest, dir, "*.baas");
            AddFiles(dest, dir, "*.iaab");
        }

        private static void AddFiles(List<string> dest, string dir, string pattern)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || dest == null)
                return;
            // Top-level + one child folder. Do not walk OritasyFonts / *Assets /
            // reshade-shaders — that AllDirectories scan hitch the title screen.
            AddFilesInFolder(dest, dir, pattern);
            string[] kids = null;
            try { kids = Directory.GetDirectories(dir); }
            catch { kids = null; }
            if (kids == null)
                return;
            for (int i = 0; i < kids.Length; i++)
            {
                string kid = kids[i];
                if (SkipBaasFolder(kid))
                    continue;
                AddFilesInFolder(dest, kid, pattern);
            }
        }

        private static bool SkipBaasFolder(string dir)
        {
            string name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name))
                return true;
            if (name.Equals("archive", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("OritasyFonts", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("reshade-shaders", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("NuclearDlssNative", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.EndsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static void AddFilesInFolder(List<string> dest, string dir, string pattern)
        {
            string[] found = null;
            try { found = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { found = null; }
            if (found == null)
                return;
            for (int i = 0; i < found.Length; i++)
            {
                string path = found[i];
                if (string.IsNullOrEmpty(path))
                    continue;
                bool dup = false;
                for (int d = 0; d < dest.Count; d++)
                {
                    if (string.Equals(dest[d], path, StringComparison.OrdinalIgnoreCase))
                    {
                        dup = true;
                        break;
                    }
                }
                if (!dup)
                    dest.Add(path);
            }
        }

        private static AssetBundleCreateRequest BeginLoad(string path)
        {
            try { return AssetBundle.LoadFromFileAsync(path); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Baas load failed " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        private static AssetBundleRequest BeginLoadAssets(AssetBundle bundle)
        {
            try { return bundle.LoadAllAssetsAsync(); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Baas assets: " + ex.Message);
                return null;
            }
        }

        private static bool AdoptAssets(UnityEngine.Object[] assets)
        {
            if (assets == null)
                return false;
            IcarusAircraftRegistry.MarkDirty();
            bool any = false;
            for (int i = 0; i < assets.Length; i++)
            {
                AircraftDefinition def = assets[i] as AircraftDefinition;
                if (def == null)
                    continue;
                if (Adopt(def))
                    any = true;
            }
            return any;
        }

        private static bool Adopt(AircraftDefinition def)
        {
            if (def == null)
                return false;
            string key = def.jsonKey;
            Encyclopedia enc = Encyclopedia.i;
            if (enc == null || enc.aircraft == null)
                return false;
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition cur = enc.aircraft[i];
                    if (cur != null && string.Equals(cur.jsonKey, key, StringComparison.Ordinal))
                    {
                        BaasNetworkFixer.EnsureDefinition(cur);
                        IcarusAircraftRegistry.AddDefinition(cur);
                        IcarusHangarSpreadService.Register(cur, HangarsFor(cur));
                        return false;
                    }
                }
            }
            enc.aircraft.Add(def);
            BaasNetworkFixer.EnsureDefinition(def);
            IcarusAircraftRegistry.AddDefinition(def);
            IcarusAircraftRegistry.RegisterNetwork(def);
            IcarusHangarSpreadService.Register(def, HangarsFor(def));
            if (Plugin.Log != null)
            {
                string n = !string.IsNullOrEmpty(key) ? key : def.name;
                Plugin.Log.LogInfo("Baas aircraft: " + n);
            }
            return true;
        }

        private static string[] HangarsFor(AircraftDefinition def)
        {
            string[] fromDetail = IcarusAircraftRegistry.HangarsFor(def);
            if (fromDetail != null && fromDetail.Length > 0)
                return fromDetail;
            return new string[] { "hangar_med", "revetment1", "shelter1", "Helipad" };
        }

        private static string PluginDir()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc))
                    return Path.GetDirectoryName(loc);
            }
            catch { }
            try { return PathsPlugin(); }
            catch { return null; }
        }

        private static string PathsPlugin()
        {
            return BepInEx.Paths.PluginPath;
        }
    }
}
