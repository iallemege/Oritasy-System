using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// In-game semi-transparent performance menu (backtick `).
    /// Tabs: Subsystems, Harmony (+ timing), GC/Alloc, Scene, Compare.
    /// </summary>
    internal static class PerfProbeMenu
    {
        private static ConfigEntry<KeyCode> _toggleKey;
        private static ConfigEntry<bool> _enabled;
        private static bool _open;
        private static bool _bound;
        private static Vector2 _scroll;
        private static Rect _win = new Rect(36f, 36f, 780f, 560f);
        private static string _status = "";
        private static float _statusUntil;
        // 0 subsystems, 1 harmony, 2 gc, 3 scene, 4 compare
        private static int _tab;
        private static List<string> _compareCache;
        private static float _compareCacheAt = -100f;

        private static GUIStyle _title;
        private static GUIStyle _label;
        private static GUIStyle _section;
        private static GUIStyle _btn;
        private static GUIStyle _mono;
        private static Texture2D _panelTex;

        internal static bool IsOpen
        {
            get { return _open; }
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null || _bound)
                return;
            _bound = true;
            _enabled = config.Bind("Performance", "PerfProbeMenu", false,
                "Enable in-game performance probe menu.");
            _toggleKey = config.Bind("Performance", "PerfProbeKey", KeyCode.BackQuote,
                "Toggle performance probe menu (default backtick `).");
        }

        internal static void TickInput()
        {
            if (_enabled != null && !_enabled.Value)
                return;
            KeyCode key = _toggleKey != null ? _toggleKey.Value : KeyCode.BackQuote;
            if (Input.GetKeyDown(KeyCode.Escape) && _open)
            {
                _open = false;
                return;
            }
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                _open = !_open;
                if (_open)
                    PerfProbeService.Sampling = true;
            }

            // Heavy sample hotkeys while probe menu is open.
            if (_open)
            {
                if (!HarmonyTimingSample.Active)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
                    {
                        HarmonyTimingSample.StartSample(12, 5f);
                        Flash(HarmonyTimingSample.Status);
                    }
                    else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
                    {
                        HarmonyTimingSample.StartSample(20, 10f);
                        Flash(HarmonyTimingSample.Status);
                    }
                }
                else if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9))
                {
                    HarmonyTimingSample.StopSample("user stop");
                    Flash("Stopped");
                }
            }
        }

        internal static void Draw()
        {
            if (!_open)
                return;
            if (_enabled != null && !_enabled.Value)
                return;

            EnsureStyles();
            bool zh = UiLang.IsChinese;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            _win = GUILayout.Window(87231405, _win, DrawWindow,
                zh ? "性能探针  [`]" : "PERF PROBE  [`]",
                GUILayout.MinWidth(700f), GUILayout.MinHeight(460f));
            GUI.color = prev;
        }

        private static void DrawWindow(int id)
        {
            bool zh = UiLang.IsChinese;
            DrawPanelBackground();

            float avgFps, avgDt, peakDt, winSec, instant, p1, stallSec;
            int frames, hitches;
            PerfProbeService.GetFrameStatsEx(out avgFps, out avgDt, out peakDt, out frames, out winSec,
                out instant, out p1, out hitches, out stallSec);

            int g0, g1, g2, g2h;
            long mem, peakMem, alloc, freed;
            float rate, peakFrameAlloc, gcWin;
            PerfProbeService.GetGcStats(out g0, out g1, out g2, out mem, out peakMem, out alloc, out freed,
                out rate, out peakFrameAlloc, out g2h, out gcWin);

            GUILayout.Label(zh ? "概览" : "OVERVIEW", _section);
            GUILayout.Label(string.Format(
                zh
                    ? "平均 {0:0.1} FPS（= 1000/平均帧时 {1:0.00} ms）· 窗口 {2:0.0}s · {3} 帧"
                    : "Average {0:0.1} FPS (= 1000/mean dt {1:0.00} ms) · window {2:0.0}s · {3} frames",
                avgFps, avgDt, winSec, frames), _label);
            GUILayout.Label(string.Format(
                zh
                    ? "当前帧 {0:0.0} FPS（不是平均）· 1%低 {1:0.0} FPS · 峰值帧 {2:0.0} ms"
                    : "Current frame {0:0.0} FPS (not an average) · 1% low {1:0.0} FPS · peak {2:0.0} ms",
                instant, p1, peakDt), _label);
            GUILayout.Label(string.Format(
                zh
                    ? "托管 {0:0.0} MB · ΔG0/1/2={1}/{2}/{3} · 分配≈{4:0.00} MB/s · 采样 {5} · 档位 {6}"
                    : "Mono {0:0.0} MB · ΔG0/1/2={1}/{2}/{3} · alloc≈{4:0.00} MB/s · sampling {5} · tier {6}",
                mem / (1024f * 1024f), g0, g1, g2,
                gcWin > 0.001f ? (alloc / (1024f * 1024f)) / gcWin : 0f,
                PerfProbeService.Sampling
                    ? (zh ? "开" : "ON")
                    : (zh ? "关" : "OFF"),
                PerfMode.TierName), _label);
            GUILayout.Label(string.Format(
                zh ? "≥100ms卡顿 {0} 次 / {1:0.0}s · Harmony重采样 {2}"
                    : "hitches≥100ms {0} / {1:0.0}s · Harmony heavy {2}",
                hitches, stallSec,
                HarmonyTimingSample.Active
                    ? (zh ? "开" : "ON") + string.Format(" ({0:0.0}s)", HarmonyTimingSample.RemainingSec)
                    : (zh ? "关" : "OFF")), _label);
            GUILayout.Label(OritasyWorker.SnapshotLine() + "  ·  " + PerfFrameGate.SnapshotLine(),
                _mono != null ? _mono : _label);

            GUILayout.BeginHorizontal();
            if (TabBtn(zh ? "子系统" : "Subsystems", _tab == 0))
                _tab = 0;
            if (TabBtn(zh ? "Harmony" : "Harmony", _tab == 1))
                _tab = 1;
            if (TabBtn(zh ? "GC/分配" : "GC/Alloc", _tab == 2))
                _tab = 2;
            if (TabBtn(zh ? "场景" : "Scene", _tab == 3))
                _tab = 3;
            if (TabBtn(zh ? "对比" : "Compare", _tab == 4))
                _tab = 4;
            GUILayout.EndHorizontal();

            DrawPinnedHeavyBar(zh);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_tab == 0)
                DrawBuckets(zh);
            else if (_tab == 1)
                DrawHarmony(zh);
            else if (_tab == 2)
                DrawLines(PerfProbeService.ListGcLines(false),
                    zh ? "GC / 分配（窗口增量）" : "GC / allocations (window deltas)");
            else if (_tab == 3)
                DrawScene(zh);
            else
                DrawCompare(zh);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(PerfProbeService.Sampling
                ? (zh ? "暂停采样" : "Pause sampling")
                : (zh ? "继续采样" : "Resume sampling"), _btn, GUILayout.Height(28f)))
                PerfProbeService.Sampling = !PerfProbeService.Sampling;
            if (GUILayout.Button(zh ? "重置窗口" : "Reset window", _btn, GUILayout.Height(28f)))
            {
                PerfProbeService.ResetWindow();
                Flash(zh ? "已重置计时窗口" : "Timing window reset");
            }
            if (GUILayout.Button(zh ? "清空全部" : "Clear all", _btn, GUILayout.Height(28f)))
            {
                PerfProbeService.ClearAll();
                _compareCache = null;
                Flash(zh ? "已清空" : "Cleared");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(zh ? "生成性能报告 v2" : "Write performance report v2", _btn, GUILayout.Height(32f)))
            {
                string path = PerfProbeService.WriteReport(zh);
                if (!string.IsNullOrEmpty(path))
                    Flash((zh ? "已写入 " : "Wrote ") + path);
                else
                    Flash(zh ? "写入失败" : "Write failed");
            }
            if (GUILayout.Button(zh ? "对比最近两份" : "Diff last two", _btn, GUILayout.Height(32f)))
            {
                _tab = 4;
                _compareCache = PerfProbeService.CompareLastTwoReports();
                _compareCacheAt = Time.unscaledTime;
                Flash(zh ? "已生成对比" : "Compare ready");
            }
            if (GUILayout.Button(zh ? "关闭" : "Close", _btn, GUILayout.Height(32f), GUILayout.Width(90f)))
                _open = false;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
                GUILayout.Label(_status, _label);
            else if (!string.IsNullOrEmpty(PerfProbeService.LastReportPath))
                GUILayout.Label((zh ? "最近报告: " : "Last report: ") + PerfProbeService.LastReportPath, _label);

            GUILayout.Label(zh
                ? "` 开关 · Esc 关闭 · 报告目录 OritasyPerf/ · 重采样：顶栏按钮或 5/0/9 · 默认关"
                : "` toggle · Esc close · reports in OritasyPerf/ · Heavy: pinned bar / keys 5·0·9 while open · OFF by default", _label);

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private static bool TabBtn(string label, bool on)
        {
            Color c = GUI.backgroundColor;
            if (on)
                GUI.backgroundColor = new Color(0.25f, 0.55f, 0.4f, 1f);
            bool hit = GUILayout.Toggle(on, label, _btn, GUILayout.Height(26f));
            GUI.backgroundColor = c;
            return hit;
        }

        private static void DrawBuckets(bool zh)
        {
            GUILayout.Label(zh
                ? "子系统占用（本窗口累计，Oritasy/WeXon 已插桩路径）"
                : "Subsystem cost (window sum — instrumented Oritasy/WeXon paths)", _section);
            List<string> lines = PerfProbeService.SnapshotBucketLines(true);
            if (lines.Count == 0)
            {
                GUILayout.Label(zh
                    ? "尚无样本。保持菜单打开并飞行数秒。"
                    : "No samples yet. Keep menu open and play a few seconds.", _label);
                return;
            }
            for (int i = 0; i < lines.Count; i++)
                GUILayout.Label(lines[i], _mono);
            GUILayout.Space(8f);
            DrawLines(PerfProbeService.ListRecentSpikes(12),
                zh ? "近期帧尖峰 (≥40ms)" : "Recent spikes (≥40ms)");
        }

                private static void DrawHarmony(bool zh)
        {
            // Heavy controls first (also pinned above scroll) — owner list used to bury the Start buttons.
            GUILayout.Label(zh
                ? "重采样计时（临时 Prefix+Finalizer，默认关）"
                : "Heavy timing sample (temp Prefix+Finalizer, default OFF)", _section);
            GUILayout.Label(zh
                ? "对最热已补丁方法装短时计时器。量原方法+全部补丁墙钟。可能顿帧；到期自动卸载。菜单开时按 5=5s x12，0=10s x20，9=停止。"
                : "Installs short-lived timers on hottest patched methods. Measures original+all patches wall-clock. May hitch; auto-uninstalls. With menu open: 5=5s x12, 0=10s x20, 9=stop.",
                _label);
            DrawHeavyButtons(zh);
            DrawLinesRaw(HarmonyTimingSample.SnapshotLines());

            GUILayout.Space(8f);
            GUILayout.Label(zh ? "Harmony 补丁所有者（计数，不是耗时）" : "Harmony patch owners (counts, not wall-clock)", _section);
            DrawLinesRaw(PerfProbeService.ListHarmonyOwners());

            GUILayout.Space(6f);
            DrawLines(PerfProbeService.ListPlugins(),
                zh ? "已加载 BepInEx 插件" : "Loaded BepInEx plugins");
        }

                private static void DrawPinnedHeavyBar(bool zh)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("HEAVY SAMPLE", _section, GUILayout.Width(120f));
            if (HarmonyTimingSample.Active)
            {
                GUILayout.Label(string.Format("RUNNING {0:0.0}s  |  {1}",
                    HarmonyTimingSample.RemainingSec, HarmonyTimingSample.Status), _label);
            }
            else
            {
                GUILayout.Label("OFF — use buttons below, or keys 5 / 0 / 9 while this menu is open", _label);
            }
            GUILayout.EndHorizontal();
            DrawHeavyButtons(zh);
            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }

                private static void DrawHeavyButtons(bool zh)
        {
            // ASCII labels only — CJK in this file was getting corrupted on some saves.
            GUILayout.BeginHorizontal();
            if (!HarmonyTimingSample.Active)
            {
                if (GUILayout.Button("Start 5s x12  [key 5]", _btn, GUILayout.Height(28f)))
                {
                    HarmonyTimingSample.StartSample(12, 5f);
                    Flash(HarmonyTimingSample.Status);
                    _tab = 1;
                }
                if (GUILayout.Button("Start 10s x20  [key 0]", _btn, GUILayout.Height(28f)))
                {
                    HarmonyTimingSample.StartSample(20, 10f);
                    Flash(HarmonyTimingSample.Status);
                    _tab = 1;
                }
            }
            else
            {
                if (GUILayout.Button("Stop heavy  [key 9]", _btn, GUILayout.Height(28f)))
                {
                    HarmonyTimingSample.StopSample("user stop");
                    Flash("Stopped");
                }
            }
            if (GUILayout.Button("Clear timings", _btn, GUILayout.Height(28f)))
            {
                HarmonyTimingSample.ClearBuckets();
                Flash("Timings cleared");
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawScene(bool zh)
        {
            GUILayout.Label(zh
                ? "场景对象普查（FindObjectsOfType，约 2.5s 缓存）"
                : "Scene census (FindObjectsOfType, ~2.5s cache)", _section);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(zh ? "强制刷新" : "Force refresh", _btn, GUILayout.Height(26f)))
            {
                PerfProbeService.ListSceneCensus(true);
                Flash(zh ? "场景已刷新" : "Scene refreshed");
            }
            GUILayout.EndHorizontal();
            DrawLinesRaw(PerfProbeService.ListSceneCensus(false));
        }

        private static void DrawCompare(bool zh)
        {
            GUILayout.Label(zh ? "报告对比（perf_report_v2）" : "Report compare (perf_report_v2)", _section);
            GUILayout.Label(zh
                ? "写入两份报告后点「对比最近两份」。稳定 SECTION 标记便于机器解析。"
                : "Write two reports, then Diff last two. Stable SECTION markers for machine parsing.",
                _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(zh ? "刷新对比" : "Refresh diff", _btn, GUILayout.Height(26f)))
            {
                _compareCache = PerfProbeService.CompareLastTwoReports();
                _compareCacheAt = Time.unscaledTime;
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(PerfProbeService.PrevReportPath))
                GUILayout.Label("A: " + PerfProbeService.PrevReportPath, _mono);
            if (!string.IsNullOrEmpty(PerfProbeService.LastReportPath))
                GUILayout.Label("B: " + PerfProbeService.LastReportPath, _mono);

            if (_compareCache == null || _compareCache.Count == 0)
            {
                GUILayout.Label(zh
                    ? "尚无对比结果。"
                    : "No compare result yet.", _label);
                return;
            }
            for (int i = 0; i < _compareCache.Count; i++)
                GUILayout.Label(_compareCache[i], _mono);
        }

        private static void DrawLines(List<string> lines, string title)
        {
            GUILayout.Label(title, _section);
            DrawLinesRaw(lines);
        }

        private static void DrawLinesRaw(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                GUILayout.Label("(empty)", _label);
                return;
            }
            for (int i = 0; i < lines.Count; i++)
                GUILayout.Label(lines[i], _mono);
        }

        private static void Flash(string msg)
        {
            _status = msg ?? "";
            _statusUntil = Time.unscaledTime + 4f;
        }

        private static void DrawPanelBackground()
        {
            if (_panelTex == null)
            {
                _panelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _panelTex.SetPixel(0, 0, new Color(0.06f, 0.09f, 0.1f, 0.82f));
                _panelTex.Apply(false, true);
            }
            Rect r = new Rect(0f, 0f, _win.width, _win.height);
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, _panelTex);
            GUI.color = prev;
        }

        private static void EnsureStyles()
        {
            if (_title != null)
            {
                ChineseFontPatch.ApplyTo(_title);
                ChineseFontPatch.ApplyTo(_label);
                ChineseFontPatch.ApplyTo(_section);
                ChineseFontPatch.ApplyTo(_btn);
                ChineseFontPatch.ApplyTo(_mono);
                return;
            }
            _title = new GUIStyle(GUI.skin.window);
            _title.fontSize = 15;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = new Color(0.85f, 0.92f, 0.7f, 1f);

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 12;
            _label.wordWrap = true;
            _label.normal.textColor = new Color(0.88f, 0.92f, 0.95f, 0.95f);

            _section = new GUIStyle(GUI.skin.label);
            _section.fontSize = 13;
            _section.fontStyle = FontStyle.Bold;
            _section.normal.textColor = new Color(0.45f, 0.85f, 0.65f, 1f);

            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 12;
            _btn.fontStyle = FontStyle.Bold;
            _btn.normal.textColor = Color.white;

            _mono = new GUIStyle(GUI.skin.label);
            _mono.fontSize = 11;
            _mono.normal.textColor = new Color(0.75f, 0.9f, 0.8f, 0.95f);
            _mono.wordWrap = false;

            ChineseFontPatch.ApplyTo(_title);
            ChineseFontPatch.ApplyTo(_label);
            ChineseFontPatch.ApplyTo(_section);
            ChineseFontPatch.ApplyTo(_btn);
            ChineseFontPatch.ApplyTo(_mono);
        }
    }
}
