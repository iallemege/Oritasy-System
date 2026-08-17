using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// In-game semi-transparent performance menu (backtick `).
    /// Shows plugin inventory, Harmony owners, instrumented subsystem timings, export report.
    /// </summary>
    internal static class PerfProbeMenu
    {
        private static ConfigEntry<KeyCode> _toggleKey;
        private static ConfigEntry<bool> _enabled;
        private static bool _open;
        private static bool _bound;
        private static Vector2 _scroll;
        private static Rect _win = new Rect(40f, 40f, 720f, 520f);
        private static string _status = "";
        private static float _statusUntil;
        private static int _tab; // 0 overview, 1 plugins, 2 harmony, 3 buckets

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
            _enabled = config.Bind("Performance", "PerfProbeMenu", true,
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
                _open = !_open;
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
                GUILayout.MinWidth(640f), GUILayout.MinHeight(420f));
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
                zh ? "托管内存 {0:0.0} MB · 采样 {1} · 档位 {2} · ≥100ms卡顿 {3} 次 / {4:0.0}s"
                    : "Mono GC {0:0.0} MB · sampling {1} · tier {2} · hitches≥100ms {3} / {4:0.0}s",
                PerfProbeService.MonoUsedBytes() / (1024f * 1024f),
                PerfProbeService.Sampling
                    ? (zh ? "开" : "ON")
                    : (zh ? "关" : "OFF"),
                PerfMode.TierName, hitches, stallSec), _label);
            GUILayout.Label(OritasyWorker.SnapshotLine() + "  ·  " + PerfFrameGate.SnapshotLine(), _mono != null ? _mono : _label);

            GUILayout.BeginHorizontal();
            if (TabBtn(zh ? "子系统" : "Buckets", _tab == 0))
                _tab = 0;
            if (TabBtn(zh ? "插件" : "Plugins", _tab == 1))
                _tab = 1;
            if (TabBtn(zh ? "Harmony" : "Harmony", _tab == 2))
                _tab = 2;
            if (TabBtn(zh ? "尖峰" : "Spikes", _tab == 3))
                _tab = 3;
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_tab == 0)
                DrawBuckets(zh);
            else if (_tab == 1)
                DrawLines(PerfProbeService.ListPlugins(), zh ? "已加载 BepInEx 插件" : "Loaded BepInEx plugins");
            else if (_tab == 2)
                DrawLines(PerfProbeService.ListHarmonyOwners(), zh ? "Harmony 补丁归属" : "Harmony patch owners");
            else
                DrawLines(PerfProbeService.ListRecentSpikes(24), zh ? "近期帧尖峰 (≥40ms)" : "Recent spikes (≥40ms)");
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
                Flash(zh ? "已清空" : "Cleared");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(zh ? "生成性能报告" : "Write performance report", _btn, GUILayout.Height(32f)))
            {
                string path = PerfProbeService.WriteReport(zh);
                if (!string.IsNullOrEmpty(path))
                    Flash((zh ? "已写入 " : "Wrote ") + path);
                else
                    Flash(zh ? "写入失败" : "Write failed");
            }
            if (GUILayout.Button(zh ? "关闭" : "Close", _btn, GUILayout.Height(32f), GUILayout.Width(90f)))
                _open = false;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
                GUILayout.Label(_status, _label);
            else if (!string.IsNullOrEmpty(PerfProbeService.LastReportPath))
                GUILayout.Label((zh ? "最近报告: " : "Last report: ") + PerfProbeService.LastReportPath, _label);

            GUILayout.Label(zh
                ? "` 开关 · Esc 关闭 · 报告目录 OritasyPerf/"
                : "` toggle · Esc close · reports in OritasyPerf/", _label);

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
        }

        private static void DrawLines(List<string> lines, string title)
        {
            GUILayout.Label(title, _section);
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
