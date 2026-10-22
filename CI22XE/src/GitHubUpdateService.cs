using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace Oritasy
{
    /// <summary>
    /// Main-menu GitHub Releases check. Stages plugin DLLs; apply after a full Steam quit.
    /// Prefers the full Oritasy_*.zip (fonts + folders) when it fits AutoMaxBytes.
    /// </summary>
    internal static class GitHubUpdateService
    {
        internal const string DefaultRepo = "iallemege/Oritasy-System";
        private const long AutoMaxBytes = 120L * 1024L * 1024L;
        private const float RecheckHours = 6f;
        private const string PrefCheckAt = "Oritasy.GitHubUpdate.CheckAt";
        private const string PrefDismiss = "Oritasy.GitHubUpdate.Dismissed";

        internal static ConfigEntry<bool> AutoCheck;
        internal static ConfigEntry<string> Repo;

        internal static bool PanelOpen;
        internal static string StatusLine = "";

        private static bool _bound;
        private static bool _checkStarted;
        private static bool _busy;
        private static bool _autoPrompted;
        private static int _gen;
        private static float _progress;
        private static string _remoteTag = "";
        private static string _remoteUrl = "";
        private static string _remoteNotes = "";
        private static string _fail = "";
        private static string _pendingDir = "";
        private static string _updateRoot = "";
        private static readonly List<RemoteAsset> _assets = new List<RemoteAsset>(8);
        private static readonly List<RemoteAsset> _picked = new List<RemoteAsset>(4);
        internal const string SumsAssetName = "SHA256SUMS.txt";
        private static RemoteAsset _sumsAsset;
        private static readonly Dictionary<string, string> _sums =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _btn;
        private static GUIStyle _chip;
        private static Vector2 _scroll;

        private enum Mode
        {
            Idle = 0,
            Checking = 1,
            Current = 2,
            Available = 3,
            PageOnly = 4,
            Downloading = 5,
            Ready = 6,
            Failed = 7
        }

        private static Mode _mode;

        private sealed class RemoteAsset
        {
            public string Name;
            public string Url;
            public long Size;
        }

        internal static void Bind(ConfigFile cfg)
        {
            if (_bound || cfg == null)
                return;
            _bound = true;
            AutoCheck = cfg.Bind("Update", "AutoCheck", true,
                "On the main menu, check GitHub Releases for a newer Oritasy pack.");
            Repo = cfg.Bind("Update", "GitHubRepo", DefaultRepo,
                "owner/name for releases/latest (public repo).");
            ResolveFolders();
            WriteApplyBat();
        }

        internal static void Tick()
        {
            if (!_bound)
                return;
            if (!OritasyPresentation.MainMenuActive)
                return;
            if (OritasyPresentation.SplashActive)
                return;
            if (_checkStarted)
                return;
            if (AutoCheck != null && !AutoCheck.Value)
                return;
            if (RecentlyChecked())
                return;
            _checkStarted = true;
            BeginCheck(false);
        }

        internal static void Draw()
        {
            if (!_bound)
                return;
            if (!OritasyPresentation.MainMenuActive)
                return;
            if (OritasyPresentation.SplashActive)
                return;
            if (OritasyPresentation.WelcomeActive)
                return;

            if (_mode == Mode.Available && !_autoPrompted && !WasDismissed(_remoteTag))
            {
                _autoPrompted = true;
                PanelOpen = true;
            }

            DrawChip();
            if (PanelOpen)
                DrawPanel();
        }

        internal static void BeginCheck(bool force)
        {
            if (_busy)
                return;
            if (Plugin.Instance == null)
                return;
            _checkStarted = true;
            Plugin.Instance.StartCoroutine(CheckRoutine(++_gen, force));
        }

        private static IEnumerator CheckRoutine(int gen, bool force)
        {
            _busy = true;
            _mode = Mode.Checking;
            _fail = "";
            StatusLine = UiLang.T("Checking GitHub…", "正在检查 GitHub…");
            string repo = Repo != null ? Repo.Value : DefaultRepo;
            if (string.IsNullOrEmpty(repo) || repo.IndexOf('/') < 0)
                repo = DefaultRepo;
            string api = "https://api.github.com/repos/" + repo.Trim() + "/releases/latest";
            using (UnityWebRequest req = UnityWebRequest.Get(api))
            {
                req.timeout = 20;
                req.SetRequestHeader("User-Agent", "Oritasy/" + PluginInfo.ReleaseVersion);
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                yield return req.SendWebRequest();
                if (gen != _gen)
                    yield break;
                if (!RequestOk(req))
                {
                    _fail = req.error;
                    if (string.IsNullOrEmpty(_fail))
                        _fail = "HTTP " + req.responseCode;
                    _mode = Mode.Failed;
                    StatusLine = UiLang.T("Update check failed", "检查更新失败");
                    _busy = false;
                    yield break;
                }
                ParseRelease(req.downloadHandler != null ? req.downloadHandler.text : "");
            }

            RememberCheck();
            int cmp = CompareRelease(_remoteTag, PluginInfo.ReleaseVersion);
            PickAssets();
            if (cmp <= 0)
            {
                _mode = Mode.Current;
                StatusLine = UiLang.T("Local " + PluginInfo.ReleaseVersion + "  ·  GitHub " + _remoteTag,
                    "本地 " + PluginInfo.ReleaseVersion + "  ·  GitHub " + _remoteTag);
            }
            else if (_picked.Count == 0)
            {
                _mode = Mode.PageOnly;
                StatusLine = UiLang.T("GitHub " + _remoteTag + " (open page)",
                    "GitHub " + _remoteTag + "（打开下载页）");
            }
            else
            {
                _mode = Mode.Available;
                StatusLine = UiLang.T("GitHub " + _remoteTag + " available",
                    "GitHub " + _remoteTag + " 可更新");
            }
            _busy = false;
            if (force && _mode == Mode.Current)
                PanelOpen = true;
        }

        private static IEnumerator DownloadRoutine(int gen)
        {
            _busy = true;
            _mode = Mode.Downloading;
            _progress = 0f;
            ResolveFolders();
            try
            {
                if (Directory.Exists(_pendingDir))
                    Directory.Delete(_pendingDir, true);
                Directory.CreateDirectory(_pendingDir);
            }
            catch (Exception ex)
            {
                _fail = ex.Message;
                _mode = Mode.Failed;
                _busy = false;
                yield break;
            }

            WriteApplyBat();
            _sums.Clear();
            if (_sumsAsset != null && AllowedUrl(_sumsAsset.Url))
            {
                using (UnityWebRequest sreq = UnityWebRequest.Get(_sumsAsset.Url))
                {
                    sreq.timeout = 60;
                    sreq.SetRequestHeader("User-Agent", "Oritasy/" + PluginInfo.ReleaseVersion);
                    UnityWebRequestAsyncOperation sop = sreq.SendWebRequest();
                    while (!sop.isDone)
                    {
                        if (gen != _gen)
                            yield break;
                        yield return null;
                    }
                    if (!RequestOk(sreq))
                    {
                        _fail = "Checksum list download failed: " + sreq.error;
                        _mode = Mode.Failed;
                        _busy = false;
                        yield break;
                    }
                    ParseSums(sreq.downloadHandler != null ? sreq.downloadHandler.text : "");
                }
            }
            else if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning("GitHub update: release has no " + SumsAssetName + " — downloads are not verified.");
            }

            string activeName = "";
            for (int i = 0; i < _picked.Count; i++)
            {
                RemoteAsset a = _picked[i];
                if (a == null || !AllowedUrl(a.Url))
                {
                    _fail = "Blocked URL";
                    _mode = Mode.Failed;
                    _busy = false;
                    yield break;
                }
                string dest = Path.Combine(_pendingDir, SafeName(a.Name));
                StatusLine = UiLang.T("Downloading " + a.Name, "正在下载 " + a.Name);
                using (UnityWebRequest req = UnityWebRequest.Get(a.Url))
                {
                    req.timeout = 900;
                    req.SetRequestHeader("User-Agent", "Oritasy/" + PluginInfo.ReleaseVersion);
                    req.downloadHandler = new DownloadHandlerFile(dest);
                    UnityWebRequestAsyncOperation op = req.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (gen != _gen)
                            yield break;
                        _progress = (i + req.downloadProgress) / Math.Max(1, _picked.Count);
                        yield return null;
                    }
                    if (!RequestOk(req))
                    {
                        _fail = req.error;
                        _mode = Mode.Failed;
                        _busy = false;
                        yield break;
                    }
                }

                if (_sumsAsset != null)
                {
                    string expect;
                    if (!_sums.TryGetValue(a.Name, out expect))
                    {
                        _fail = "No checksum for " + a.Name;
                        _mode = Mode.Failed;
                        _busy = false;
                        yield break;
                    }
                    StatusLine = UiLang.T("Verifying " + a.Name, "正在校验 " + a.Name);
                    string got = null;
                    bool hashDone = false;
                    string hashPath = dest;
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        got = Sha256File(hashPath);
                        hashDone = true;
                    });
                    while (!hashDone)
                    {
                        if (gen != _gen)
                            yield break;
                        yield return null;
                    }
                    if (got == null || !string.Equals(got, expect, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(dest); }
                        catch { }
                        _fail = "Checksum mismatch: " + a.Name;
                        _mode = Mode.Failed;
                        _busy = false;
                        yield break;
                    }
                }

                if (EndsIgnoreCase(a.Name, ".zip"))
                {
                    StatusLine = UiLang.T("Unpacking " + a.Name, "正在解压 " + a.Name);
                    Process unzip = StartExtractZip(dest, _pendingDir);
                    float unzipDeadline = Time.realtimeSinceStartup + 300f;
                    while (unzip != null && !unzip.HasExited)
                    {
                        if (gen != _gen || Time.realtimeSinceStartup > unzipDeadline)
                        {
                            try { unzip.Kill(); }
                            catch { }
                            if (gen != _gen)
                                yield break;
                            unzip = null;
                            break;
                        }
                        yield return null;
                    }
                    bool unzipped = false;
                    if (unzip != null)
                    {
                        try { unzipped = unzip.ExitCode == 0; }
                        catch { }
                        try { unzip.Dispose(); }
                        catch { }
                    }
                    if (!unzipped)
                    {
                        _fail = "Unzip failed";
                        _mode = Mode.Failed;
                        _busy = false;
                        yield break;
                    }
                    try { File.Delete(dest); }
                    catch { }
                }
            }

            HoistPackTree(_pendingDir);
            FlattenDlls(_pendingDir);
            activeName = FindActiveDll(_pendingDir);
            if (!string.IsNullOrEmpty(activeName))
            {
                try { File.WriteAllText(Path.Combine(_pendingDir, "active.txt"), activeName); }
                catch { }
            }
            _progress = 1f;
            _mode = Mode.Ready;
            StatusLine = UiLang.T("Ready — quit Steam, then Apply", "已就绪 — 退出 Steam 后套用");
            _busy = false;
        }

        private static void DrawChip()
        {
            if (_mode == Mode.Idle || _mode == Mode.Current)
                return;
            EnsureStyles();
            float chipW = 210f;
            float chipH = 26f;
            float exitBand = 16f + 50f;
            float y = UiScaleService.Height - exitBand - 12f - 28f - 8f - chipH;
            if (y < 8f)
                y = 8f;
            Rect chip = new Rect(18f, y, chipW, chipH);
            if (GUI.Button(chip, GUIContent.none, _btn))
                PanelOpen = !PanelOpen;
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.82f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            bool hot = _mode == Mode.Available || _mode == Mode.Ready || _mode == Mode.PageOnly;
            GUI.color = hot
                ? new Color(0.95f, 0.8f, 0.25f, 0.95f)
                : new Color(0.45f, 0.9f, 0.6f, 0.95f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string label = ChipLabel();
            GUI.Label(new Rect(chip.x + 8f, chip.y, chip.width - 12f, chip.height), label, _chip);
            GUI.color = prev;
        }

        private static string ChipLabel()
        {
            if (_mode == Mode.Checking)
                return UiLang.T("GitHub  ·  checking", "GitHub  ·  检查中");
            if (_mode == Mode.Downloading)
                return UiLang.T("GitHub  ·  " + ((int)(_progress * 100f)).ToString() + "%",
                    "GitHub  ·  " + ((int)(_progress * 100f)).ToString() + "%");
            if (_mode == Mode.Ready)
                return UiLang.T("GitHub  ·  apply after quit", "GitHub  ·  退出后套用");
            if (_mode == Mode.Failed)
                return UiLang.T("GitHub  ·  failed", "GitHub  ·  失败");
            if (_mode == Mode.PageOnly)
                return UiLang.T("GitHub  ·  " + _remoteTag, "GitHub  ·  " + _remoteTag);
            return UiLang.T("GitHub  ·  " + _remoteTag + " ready", "GitHub  ·  " + _remoteTag);
        }

        private static void DrawPanel()
        {
            EnsureStyles();
            OritasyCursor.Hold();
            OritasyCursor.Pulse();

            int prevDepth = GUI.depth;
            GUI.depth = -997;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height), Texture2D.whiteTexture);
            GUI.color = prev;

            Rect box = PresentationLayoutService.CenteredBox(
                UiScaleService.Width, UiScaleService.Height, 720f, 520f, 0.72f, 0.70f);
            GUI.color = new Color(0.04f, 0.07f, 0.06f, 0.96f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.15f, 1f, 0.35f, 1f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 18f, box.y + 10f, box.width - 36f, 26f),
                UiLang.T("GitHub update", "GitHub 更新"), _title);
            GUI.Label(new Rect(box.x + 18f, box.y + 38f, box.width - 36f, 40f),
                LocalRemoteLine(), _body);

            Rect scrollR = new Rect(box.x + 16f, box.y + 84f, box.width - 32f, box.height - 168f);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(scrollR, Texture2D.whiteTexture);
            GUI.color = Color.white;
            float noteH = 220f;
            if (!string.IsNullOrEmpty(_remoteNotes))
                noteH = Mathf.Max(220f, _remoteNotes.Length * 0.35f);
            _scroll = GUI.BeginScrollView(scrollR, _scroll, new Rect(0f, 0f, scrollR.width - 22f, noteH));
            string body = PanelBody();
            GUI.Label(new Rect(10f, 8f, scrollR.width - 32f, noteH - 12f), body, _body);
            GUI.EndScrollView();

            float bw = 150f;
            float by = box.yMax - 52f;
            float x = box.x + 16f;
            if (GUI.Button(new Rect(x, by, bw, 32f), UiLang.T("Check now", "立即检查"), _btn))
                BeginCheck(true);
            x += bw + 8f;
            if (!string.IsNullOrEmpty(_remoteUrl)
                && GUI.Button(new Rect(x, by, bw, 32f), UiLang.T("Open GitHub", "打开 GitHub"), _btn))
                OpenRemote();
            x += bw + 8f;
            if ((_mode == Mode.Available || _mode == Mode.PageOnly) && _picked.Count > 0
                && GUI.Button(new Rect(x, by, bw, 32f), UiLang.T("Download", "下载"), _btn))
            {
                if (Plugin.Instance != null)
                    Plugin.Instance.StartCoroutine(DownloadRoutine(++_gen));
            }
            x += bw + 8f;
            if (_mode == Mode.Ready
                && GUI.Button(new Rect(x, by, bw, 32f), UiLang.T("Open folder", "打开文件夹"), _btn))
                OpenUpdateFolder();

            float cx = box.xMax - 16f - 110f;
            if (GUI.Button(new Rect(cx, by, 110f, 32f), UiLang.T("Close", "关闭"), _btn))
            {
                if (!string.IsNullOrEmpty(_remoteTag))
                    RememberDismiss(_remoteTag);
                PanelOpen = false;
                OritasyCursor.Release();
            }

            GUI.depth = prevDepth;
        }

        private static string LocalRemoteLine()
        {
            string local = PluginInfo.ReleaseVersion;
            string remote = string.IsNullOrEmpty(_remoteTag) ? "—" : _remoteTag;
            return UiLang.T("Installed " + local + "   GitHub " + remote,
                "已安装 " + local + "   GitHub " + remote);
        }

        private static string PanelBody()
        {
            StringBuilder sb = new StringBuilder(512);
            if (!string.IsNullOrEmpty(StatusLine))
                sb.AppendLine(StatusLine);
            if (_mode == Mode.Failed && !string.IsNullOrEmpty(_fail))
                sb.AppendLine(_fail);
            if (_mode == Mode.PageOnly)
            {
                sb.AppendLine(UiLang.T(
                    "This GitHub asset is larger than the in-game cap. Open the page and copy the full zip into BepInEx\\plugins (keep OritasyFonts).",
                    "这个资源超过游戏内下载上限。打开页面，把整包解压进 BepInEx\\plugins（必须保留 OritasyFonts）。"));
            }
            if (_mode == Mode.Ready)
            {
                sb.AppendLine(UiLang.T(
                    "Fully quit Steam, then run ApplyOritasyUpdate.bat in BepInEx\\OritasyUpdate.",
                    "请彻底退出 Steam，然后运行 BepInEx\\OritasyUpdate\\ApplyOritasyUpdate.bat。"));
            }
            if (_mode == Mode.Downloading)
                sb.AppendLine(((int)(_progress * 100f)).ToString() + "%");
            if (_picked.Count > 0 && _mode != Mode.Ready)
            {
                sb.AppendLine();
                for (int i = 0; i < _picked.Count; i++)
                {
                    RemoteAsset a = _picked[i];
                    if (a == null)
                        continue;
                    sb.AppendLine(a.Name + "  (" + FormatSize(a.Size) + ")");
                }
            }
            if (!string.IsNullOrEmpty(_remoteNotes))
            {
                sb.AppendLine();
                sb.AppendLine(_remoteNotes);
            }
            return sb.ToString();
        }

        private static void ParseRelease(string json)
        {
            _remoteTag = "";
            _remoteUrl = "";
            _remoteNotes = "";
            _assets.Clear();
            if (string.IsNullOrEmpty(json))
                return;
            _remoteTag = JsonString(json, "tag_name");
            if (_remoteTag.StartsWith("v") || _remoteTag.StartsWith("V"))
                _remoteTag = _remoteTag.Substring(1);
            _remoteUrl = JsonString(json, "html_url");
            _remoteNotes = Unescape(JsonString(json, "body"));
            if (_remoteNotes.Length > 1600)
                _remoteNotes = _remoteNotes.Substring(0, 1600) + "…";

            int pos = 0;
            while (pos >= 0 && pos < json.Length)
            {
                int u = json.IndexOf("\"browser_download_url\"", pos, StringComparison.Ordinal);
                if (u < 0)
                    break;
                string url = JsonStringFrom(json, u);
                int windowStart = u - 2800;
                if (windowStart < 0)
                    windowStart = 0;
                string window = json.Substring(windowStart, Math.Min(json.Length - windowStart, 3200));
                string name = JsonString(window, "name");
                long size = JsonLong(window, "size");
                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(name))
                {
                    RemoteAsset a = new RemoteAsset();
                    a.Name = name;
                    a.Url = url;
                    a.Size = size;
                    _assets.Add(a);
                }
                pos = u + 24;
            }
        }

        private static void PickAssets()
        {
            _picked.Clear();
            _sumsAsset = null;
            RemoteAsset pluginsZip = null;
            RemoteAsset fullZip = null;
            for (int i = 0; i < _assets.Count; i++)
            {
                RemoteAsset a = _assets[i];
                if (a == null || string.IsNullOrEmpty(a.Name))
                    continue;
                string n = a.Name;
                if (string.Equals(n, SumsAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    _sumsAsset = a;
                    continue;
                }
                if (ContainsIgnore(n, "OritasyPlugins") && EndsIgnoreCase(n, ".zip"))
                {
                    pluginsZip = a;
                    continue;
                }
                if (IsPluginDll(n))
                {
                    _picked.Add(a);
                    continue;
                }
                if (EndsIgnoreCase(n, ".zip") && ContainsIgnore(n, "Oritasy") && a.Size <= AutoMaxBytes)
                    fullZip = a;
            }
            if (fullZip != null)
            {
                _picked.Clear();
                _picked.Add(fullZip);
                return;
            }
            if (pluginsZip != null)
            {
                _picked.Clear();
                _picked.Add(pluginsZip);
                return;
            }
        }

        private static bool IsPluginDll(string name)
        {
            if (!EndsIgnoreCase(name, ".dll"))
                return false;
            if (ContainsIgnore(name, "OritasyHud"))
                return true;
            if (ContainsIgnore(name, "VeyrnWeapons"))
                return true;
            if (ContainsIgnore(name, "GroundArsenal"))
                return true;
            if (ContainsIgnore(name, "Oritasy_") && !ContainsIgnore(name, "D.dll")
                && (ContainsIgnore(name, "C.dll") || ContainsIgnore(name, "E.dll")))
                return true;
            return false;
        }

        internal static int CompareRelease(string remote, string local)
        {
            int rn, ln;
            char re, le;
            if (!ParseRelease(remote, out rn, out re) || !ParseRelease(local, out ln, out le))
                return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase);
            if (rn != ln)
                return rn.CompareTo(ln);
            return re.CompareTo(le);
        }

        private static bool ParseRelease(string s, out int build, out char edition)
        {
            build = 0;
            edition = 'C';
            if (string.IsNullOrEmpty(s))
                return false;
            s = s.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
                s = s.Substring(1);
            int lastDot = s.LastIndexOf('.');
            if (lastDot < 0 || lastDot + 1 >= s.Length)
                return false;
            string tail = s.Substring(lastDot + 1);
            int i = 0;
            while (i < tail.Length && tail[i] >= '0' && tail[i] <= '9')
                i++;
            if (i == 0)
                return false;
            int n;
            if (!int.TryParse(tail.Substring(0, i), out n))
                return false;
            build = n;
            if (i < tail.Length)
                edition = char.ToUpperInvariant(tail[i]);
            return true;
        }

        private static void HoistPackTree(string pending)
        {
            if (string.IsNullOrEmpty(pending) || !Directory.Exists(pending))
                return;
            string zipDir = Path.Combine(pending, "_zip");
            if (!Directory.Exists(zipDir))
                return;
            string[] starts = new string[]
            {
                zipDir,
                Path.Combine(zipDir, "plugins"),
                Path.Combine(zipDir, "BepInEx", "plugins")
            };
            for (int i = 0; i < starts.Length; i++)
            {
                string srcRoot = starts[i];
                if (!Directory.Exists(srcRoot))
                    continue;
                try
                {
                    string[] files = Directory.GetFiles(srcRoot);
                    for (int f = 0; f < files.Length; f++)
                    {
                        string name = Path.GetFileName(files[f]);
                        if (ContainsIgnore(name, "IcarusArmories"))
                            continue;
                        if (ContainsIgnore(name, "MissileEditor"))
                            continue;
                        File.Copy(files[f], Path.Combine(pending, name), true);
                    }
                    string[] dirs = Directory.GetDirectories(srcRoot);
                    for (int d = 0; d < dirs.Length; d++)
                    {
                        string name = Path.GetFileName(dirs[d]);
                        if (string.Equals(name, "_zip", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (string.Equals(name, "BepInEx", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (string.Equals(name, "plugins", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(srcRoot, zipDir, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (string.Equals(name, "MissileEditor", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "OritasyMissileEditor", StringComparison.OrdinalIgnoreCase))
                            continue;
                        CopyDirectory(dirs[d], Path.Combine(pending, name));
                    }
                }
                catch { }
            }
        }

        private static void CopyDirectory(string src, string dst)
        {
            if (string.IsNullOrEmpty(src) || !Directory.Exists(src))
                return;
            Directory.CreateDirectory(dst);
            string[] files = Directory.GetFiles(src);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                if (ContainsIgnore(name, "missed_zh"))
                    continue;
                if (ContainsIgnore(name, "CoreD.bin")
                    || ContainsIgnore(name, "Extra.bin")
                    || ContainsIgnore(name, "Core.bin"))
                    continue;
                if (EndsIgnoreCase(name, ".csv") && ContainsIgnore(src, "OritasyReplays"))
                    continue;
                try { File.Copy(files[i], Path.Combine(dst, name), true); }
                catch { }
            }
            string[] dirs = Directory.GetDirectories(src);
            for (int i = 0; i < dirs.Length; i++)
                CopyDirectory(dirs[i], Path.Combine(dst, Path.GetFileName(dirs[i])));
        }

        private static void FlattenDlls(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;
            string[] files;
            try { files = Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories); }
            catch { return; }
            for (int i = 0; i < files.Length; i++)
            {
                string src = files[i];
                string name = Path.GetFileName(src);
                if (ContainsIgnore(name, "IcarusArmories"))
                    continue;
                if (ContainsIgnore(name, "MissileEditor"))
                    continue;
                string dest = Path.Combine(root, name);
                if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Copy(src, dest, true);
                }
                catch { }
            }
        }

        private static string FindActiveDll(string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "Oritasy_*.dll");
                string best = "";
                int bestN = -1;
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    if (ContainsIgnore(name, "D.dll"))
                        continue;
                    int n;
                    char e;
                    if (!ParseRelease(name.Replace(".dll", ""), out n, out e))
                        continue;
                    if (n > bestN)
                    {
                        bestN = n;
                        best = name;
                    }
                }
                return best;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Lines of "&lt;sha256 hex&gt;  &lt;file name&gt;" (sha256sum / Get-FileHash format).</summary>
        private static void ParseSums(string text)
        {
            _sums.Clear();
            if (string.IsNullOrEmpty(text))
                return;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length < 66)
                    continue;
                int sp = l.IndexOfAny(new[] { ' ', '\t' });
                if (sp != 64)
                    continue;
                string name = l.Substring(sp).Trim().TrimStart('*');
                if (name.Length > 0)
                    _sums[name] = l.Substring(0, 64);
            }
        }

        private static string Sha256File(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(64);
                    for (int i = 0; i < h.Length; i++)
                        sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Starts Expand-Archive; caller polls HasExited so the main thread never blocks.</summary>
        private static Process StartExtractZip(string zipPath, string destDir)
        {
            try
            {
                string extract = Path.Combine(destDir, "_zip");
                if (Directory.Exists(extract))
                    Directory.Delete(extract, true);
                Directory.CreateDirectory(extract);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -Force -LiteralPath '"
                    + zipPath.Replace("'", "''") + "' -DestinationPath '"
                    + extract.Replace("'", "''") + "'\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                return Process.Start(psi);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Unzip: " + ex.Message);
                return null;
            }
        }

        private static void ResolveFolders()
        {
            try
            {
                string bep = Path.GetDirectoryName(Paths.PluginPath);
                if (string.IsNullOrEmpty(bep))
                    bep = Path.Combine(Paths.GameRootPath, "BepInEx");
                _updateRoot = Path.Combine(bep, "OritasyUpdate");
                _pendingDir = Path.Combine(_updateRoot, "pending");
                Directory.CreateDirectory(_updateRoot);
            }
            catch { }
        }

        private static void WriteApplyBat()
        {
            if (string.IsNullOrEmpty(_updateRoot))
                return;
            string path = Path.Combine(_updateRoot, "ApplyOritasyUpdate.bat");
            string bat =
                "@echo off\r\n"
                + "setlocal EnableExtensions\r\n"
                + "set HERE=%~dp0\r\n"
                + "set PLUGINS=%HERE%..\\plugins\r\n"
                + "set PENDING=%HERE%pending\r\n"
                + "if not exist \"%PENDING%\" (\r\n"
                + "  echo No pending update.\r\n"
                + "  pause\r\n"
                + "  exit /b 1\r\n"
                + ")\r\n"
                + "tasklist /FI \"IMAGENAME eq NuclearOption.exe\" | find /I \"NuclearOption.exe\" >nul\r\n"
                + "if not errorlevel 1 (\r\n"
                + "  echo Close the game and fully quit Steam first.\r\n"
                + "  pause\r\n"
                + "  exit /b 1\r\n"
                + ")\r\n"
                + "if exist \"%PENDING%\\IcarusArmories.dll\" del /f /q \"%PENDING%\\IcarusArmories.dll\"\r\n"
                + "copy /Y \"%PENDING%\\*.dll\" \"%PLUGINS%\\\"\r\n"
                + "for %%D in (OritasyFonts OritasyZh WeXonAssets VeyrnWeaponsAssets OritasyMusic OritasyMissileAudio OritasyReplays OritasyPerf OritasyExtra) do (\r\n"
                + "  if exist \"%PENDING%\\%%D\" xcopy /E /I /Y /Q \"%PENDING%\\%%D\" \"%PLUGINS%\\%%D\\\" >nul\r\n"
                + "  if exist \"%PENDING%\\_zip\\%%D\" xcopy /E /I /Y /Q \"%PENDING%\\_zip\\%%D\" \"%PLUGINS%\\%%D\\\" >nul\r\n"
                + ")\r\n"
                // CMD expands %ACTIVE% when a ( ) block is parsed: set it on its own line first.
                + "set ACTIVE=\r\n"
                + "if exist \"%PENDING%\\active.txt\" set /p ACTIVE=<\"%PENDING%\\active.txt\"\r\n"
                + "if not defined ACTIVE goto active_done\r\n"
                + "if not exist \"%PLUGINS%\\%ACTIVE%\" goto active_done\r\n"
                + "for %%F in (\"%PLUGINS%\\Oritasy_*.dll\") do (\r\n"
                + "  if /I not \"%%~nxF\"==\"%ACTIVE%\" (\r\n"
                + "    if exist \"%%~fF.off\" del /f /q \"%%~fF.off\"\r\n"
                + "    ren \"%%F\" \"%%~nxF.off\"\r\n"
                + "  )\r\n"
                + ")\r\n"
                + ":active_done\r\n"
                + "echo Applied. Start the game from Steam.\r\n"
                + "pause\r\n";
            try { File.WriteAllText(path, bat, Encoding.ASCII); }
            catch { }
        }

        private static void OpenRemote()
        {
            if (string.IsNullOrEmpty(_remoteUrl) || !AllowedUrl(_remoteUrl))
                return;
            try { Application.OpenURL(_remoteUrl); }
            catch { }
        }

        private static void OpenUpdateFolder()
        {
            ResolveFolders();
            if (string.IsNullOrEmpty(_updateRoot) || !Directory.Exists(_updateRoot))
                return;
            try { Application.OpenURL("file:///" + _updateRoot.Replace('\\', '/')); }
            catch { }
        }

        private static bool RequestOk(UnityWebRequest req)
        {
            if (req == null)
                return false;
            try
            {
                if (req.result == UnityWebRequest.Result.Success)
                    return true;
            }
            catch { }
            return string.IsNullOrEmpty(req.error) && req.responseCode >= 200 && req.responseCode < 300;
        }

        private static bool AllowedUrl(string url)
        {
            try
            {
                Uri u = new Uri(url);
                string h = u.Host.ToLowerInvariant();
                if (h == "github.com")
                    return true;
                if (h.EndsWith(".github.com"))
                    return true;
                if (h == "objects.githubusercontent.com")
                    return true;
                if (h.EndsWith(".githubusercontent.com"))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool RecentlyChecked()
        {
            try
            {
                string wall = PlayerPrefs.GetString(PrefCheckAt + ".wall", "");
                DateTime t;
                if (string.IsNullOrEmpty(wall) || !DateTime.TryParse(wall, out t))
                    return false;
                return (DateTime.UtcNow - t).TotalSeconds < RecheckHours * 3600.0;
            }
            catch
            {
                return false;
            }
        }

        private static void RememberCheck()
        {
            try
            {
                PlayerPrefs.SetString(PrefCheckAt + ".wall", DateTime.UtcNow.ToString("o"));
                PlayerPrefs.Save();
            }
            catch { }
        }

        private static bool WasDismissed(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            try { return PlayerPrefs.GetString(PrefDismiss, "") == tag; }
            catch { return false; }
        }

        private static void RememberDismiss(string tag)
        {
            try
            {
                PlayerPrefs.SetString(PrefDismiss, tag ?? "");
                PlayerPrefs.Save();
            }
            catch { }
        }

        private static string JsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return "";
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0)
                return "";
            return JsonStringFrom(json, k);
        }

        private static string JsonStringFrom(string json, int keyPos)
        {
            int colon = json.IndexOf(':', keyPos);
            if (colon < 0)
                return "";
            int q = json.IndexOf('"', colon + 1);
            if (q < 0)
                return "";
            StringBuilder sb = new StringBuilder(64);
            for (int i = q + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == 'n')
                        sb.Append('\n');
                    else if (n == 'r')
                        sb.Append('\r');
                    else if (n == 't')
                        sb.Append('\t');
                    else
                        sb.Append(n);
                    i++;
                    continue;
                }
                if (c == '"')
                    break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static long JsonLong(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0)
                return 0;
            int colon = json.IndexOf(':', k);
            if (colon < 0)
                return 0;
            int i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t'))
                i++;
            long n = 0;
            bool any = false;
            while (i < json.Length && json[i] >= '0' && json[i] <= '9')
            {
                any = true;
                n = n * 10 + (json[i] - '0');
                i++;
            }
            return any ? n : 0;
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "asset.bin";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = true;
                for (int b = 0; b < bad.Length; b++)
                {
                    if (c == bad[b])
                    {
                        ok = false;
                        break;
                    }
                }
                sb.Append(ok ? c : '_');
            }
            return sb.ToString();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "?";
            if (bytes < 1024)
                return bytes.ToString() + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024).ToString() + " KB";
            return (bytes / (1024 * 1024)).ToString() + " MB";
        }

        private static bool EndsIgnoreCase(string s, string tail)
        {
            if (s == null || tail == null || s.Length < tail.Length)
                return false;
            return string.Compare(s, s.Length - tail.Length, tail, 0, tail.Length,
                StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool ContainsIgnore(string s, string part)
        {
            if (s == null || part == null)
                return false;
            return s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EnsureStyles()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label);
                _title.fontSize = 18;
                _title.fontStyle = FontStyle.Bold;
                _title.normal.textColor = new Color(0.15f, 1f, 0.35f, 1f);
            }
            if (_body == null)
            {
                _body = new GUIStyle(GUI.skin.label);
                _body.fontSize = 13;
                _body.wordWrap = true;
                _body.normal.textColor = new Color(0.86f, 0.92f, 0.88f, 1f);
            }
            if (_btn == null)
            {
                _btn = new GUIStyle(GUI.skin.button);
                _btn.fontSize = 12;
            }
            if (_chip == null)
            {
                _chip = new GUIStyle(GUI.skin.label);
                _chip.fontSize = 10;
                _chip.alignment = TextAnchor.MiddleLeft;
                _chip.normal.textColor = new Color(1f, 0.92f, 0.55f, 0.98f);
            }
        }
    }
}
