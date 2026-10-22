using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Public so standalone Dirjam.dll can yield when this pack owns DIRJAM.
    /// </summary>
    public static class DirjamPriority
    {
        public static bool Owns
        {
            get { return JammerQte.On(); }
        }
    }

    /// <summary>
    /// DIRJAM / 定向干扰: locked-target jammer arrow board.
    /// No lock keeps RwContinuousJammer.
    /// Success: eject aircraft, or silence non-aircraft weapons for 1 min.
    /// Fail / timeout: eject the player only. 30 s cooldown.
    /// Arrow count 40-456, time 80-500 s (random each start).
    /// F8 Profile toggle. When on, standalone Dirjam.dll yields.
    /// </summary>
    internal static class JammerQte
    {
        internal const string NameEn = "DIRJAM";
        internal const string NameZh = "定向干扰";
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> CooldownSeconds;
        internal static ConfigEntry<float> RangeMeters;
        internal static ConfigEntry<float> SilenceSeconds;

        private static readonly FieldInfo WeaponTarget =
            AccessTools.Field(typeof(Weapon), "currentTarget");
        private static readonly FieldInfo HudMarkerLookup =
            AccessTools.Field(typeof(CombatHUD), "markerLookup");

        private const int DirUp = 0;
        private const int DirDown = 1;
        private const int DirLeft = 2;
        private const int DirRight = 3;
        private const int ArrowMin = 40;
        private const int ArrowMaxInclusive = 456;
        private const float TimeMin = 80f;
        private const float TimeMax = 500f;
        private const float RangeM = 15000f;

        private static bool _active;
        private static Aircraft _self;
        private static Unit _target;
        private static int[] _seq;
        private static int _index;
        private static float _deadline;
        private static float _holdHeading;
        private static float _holdAgl;
        private static bool _savedAssist;
        private static bool _hasSavedAssist;
        private static float _cdUntil;
        private static int _cdAircraftId;
        private static readonly Dictionary<int, float> SilencedUntil =
            new Dictionary<int, float>(16);

        private static Texture2D[] _dirTex;
        private static GUIStyle _titleStyle;
        private static GUIStyle _timerStyle;
        private static GUIStyle _countStyle;
        private static GUIStyle _hintStyle;
        private static bool _stylesReady;
        private static float _hintAt;
        private static string _hintLine;
        private static float _ignoreInputUntil;
        private static float _hintPollAt;
        private static bool _hintWanted;

        internal static bool IsActive
        {
            get { return _active; }
        }

        internal static bool WantsDraw
        {
            get { return _active || _hintWanted; }
        }

        internal static bool BlocksControls
        {
            get { return _active; }
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            Enabled = config.Bind("RadarWarfare", "LockQteEnabled", true,
                "F8 Profile: Oritasy DIRJAM. On = this pack owns the arrow board (standalone Dirjam.dll yields).");
            CooldownSeconds = config.Bind("RadarWarfare", "LockQteCooldown", 30f,
                "Cooldown after DIRJAM starts (seconds).");
            RangeMeters = config.Bind("RadarWarfare", "LockQteRangeM", 15000f,
                "DIRJAM max range (m). Unused: range is 15000 m.");
            SilenceSeconds = config.Bind("RadarWarfare", "LockQteSilenceSeconds", 60f,
                "Non-aircraft lock: weapons disabled this many seconds on success.");
        }

        internal static void DrawProfileToggle()
        {
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("DIRJAM / directional jam", "定向干扰"), GUILayout.Width(140f));
            Color prev = GUI.backgroundColor;
            bool on = On();
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? UiLang.T("ON", "开") : UiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                SetEnabled(!on);
                on = !on;
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? UiLang.T("  [ON]", "  [开]") : UiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? UiLang.T(
                        "ON: lock + jammer key = arrow board (40-456 / 80-500s). Standalone Dirjam.dll yields.",
                        "开：锁定+干扰键=箭头板（40–456个 / 80–500秒）。独立 Dirjam.dll 让路。")
                    : UiLang.T(
                        "OFF: Oritasy does not run DIRJAM. Standalone Dirjam.dll may handle it if loaded.",
                        "关：Oritasy 不跑定向干扰。若装了独立 Dirjam.dll 则由它接手。"),
                GUILayout.ExpandWidth(true));
        }

        internal static void SetEnabled(bool on)
        {
            if (Enabled != null)
                Enabled.Value = on;
            if (!on && _active)
                Stop(true);
        }

        internal static string SystemName()
        {
            return UiLang.T(NameEn, NameZh);
        }

        internal static bool On()
        {
            return RadarWarfare.On() && (Enabled == null || Enabled.Value);
        }

        internal static bool IsActiveFor(Aircraft ac)
        {
            return _active && ac != null && object.ReferenceEquals(ac, _self);
        }

        internal static bool TryBegin(Aircraft ac)
        {
            if (!On() || ac == null || ac.disabled)
                return false;
            if (!ReverseThrustService.IsPlayerFlown(ac) || !ReverseThrustService.IsLocalAircraft(ac))
                return false;
            if (_active)
                return true;

            Unit locked = ResolveLock(ac);
            if (locked == null || locked.disabled)
                return false;
            if (object.ReferenceEquals(locked, ac))
                return false;
            if (RadarWarfare.SameHq(ac, locked))
                return false;

            if (IsCooling(ac))
            {
                float left = Mathf.Max(0f, _cdUntil - Time.unscaledTime);
                Flash(UiLang.T("DIRJAM cooling " + left.ToString("0") + "s",
                    "定向干扰冷却 " + left.ToString("0") + " 秒"), 1.6f);
                return true;
            }
            if (!InRange(ac, locked))
            {
                Flash(UiLang.T("DIRJAM out of range", "定向干扰超出距离"), 1.6f);
                return false;
            }

            int n = UnityEngine.Random.Range(ArrowMin, ArrowMaxInclusive + 1);
            float dur = UnityEngine.Random.Range(TimeMin, TimeMax);

            _seq = new int[n];
            for (int i = 0; i < n; i++)
                _seq[i] = UnityEngine.Random.Range(0, 4);
            _index = 0;
            _self = ac;
            _target = locked;
            _deadline = Time.unscaledTime + dur;
            _holdHeading = HeadingOf(ac);
            _holdAgl = Mathf.Max(120f, ac.radarAlt > 40f ? ac.radarAlt : 400f);
            _hasSavedAssist = false;
            try
            {
                _savedAssist = ac.flightAssist;
                _hasSavedAssist = true;
                if (!ac.flightAssist)
                    ac.SetFlightAssist(true);
            }
            catch { }
            _active = true;
            _ignoreInputUntil = Time.unscaledTime + 0.2f;
            float cd = CooldownSeconds != null ? CooldownSeconds.Value : 30f;
            _cdUntil = Time.unscaledTime + Mathf.Max(1f, cd);
            _cdAircraftId = ac.GetInstanceID();
            RwContinuousJammer jam = RwContinuousJammer.Get(ac);
            if (jam != null)
                jam.Deactivate();
            return true;
        }

        internal static bool ShouldSuppressHold(Aircraft ac)
        {
            if (!On() || ac == null)
                return false;
            if (IsActiveFor(ac))
                return true;
            if (!ReverseThrustService.IsPlayerFlown(ac) || !ReverseThrustService.IsLocalAircraft(ac))
                return false;
            Unit locked = ResolveLock(ac);
            if (locked == null || locked.disabled)
                return false;
            if (RadarWarfare.SameHq(ac, locked))
                return false;
            return InRange(ac, locked);
        }

        internal static void Tick()
        {
            PruneSilence();
            if (!_active)
            {
                float now = Time.unscaledTime;
                if (now >= _hintPollAt)
                {
                    _hintPollAt = now + 0.3f;
                    _hintWanted = false;
                    try
                    {
                        Aircraft ac;
                        if (GameManager.GetLocalAircraft(out ac) && ac != null)
                            _hintWanted = IsJammerSelected(ac);
                    }
                    catch { }
                }
                return;
            }
            _hintWanted = false;
            if (_self == null || _self.disabled || _self.HasEjected())
            {
                Stop(false);
                return;
            }
            if (_target == null || _target.disabled)
            {
                Flash(UiLang.T("Target lost", "目标丢失"), 1.8f);
                Stop(true);
                return;
            }
            if (Time.unscaledTime >= _deadline)
            {
                Fail();
                return;
            }

            if (Time.unscaledTime < _ignoreInputUntil)
                return;

            int pressed = ReadDirDown();
            if (pressed < 0)
                return;
            if (_seq == null || _index < 0 || _index >= _seq.Length)
            {
                Fail();
                return;
            }
            if (pressed != _seq[_index])
            {
                Fail();
                return;
            }
            _index++;
            if (_index >= _seq.Length)
                Succeed();
        }

        internal static void ApplyLocalHold()
        {
            if (!_active)
                return;
            ApplyLevelFlight(_self);
        }

        internal static void ApplyLevelFlight(Aircraft ac)
        {
            if (!_active || ac == null)
                return;
            if (!object.ReferenceEquals(ac, _self))
                return;
            if (ac.autopilot == null)
                return;
            AutopilotAim.HoldHeadingTerrain(ac.autopilot, ac, _holdHeading, _holdAgl,
                AutopilotAim.DefaultEffort, AutopilotAim.CruiseBank);
            try
            {
                ControlInputs inputs = ac.GetInputs();
                if (inputs != null)
                    inputs.brake = 0f;
            }
            catch { }
        }

        internal static void AbortForManualEject()
        {
            Stop(false);
        }

        internal static bool IsFireSilenced(Unit unit)
        {
            if (unit == null)
                return false;
            float until;
            if (!SilencedUntil.TryGetValue(unit.GetInstanceID(), out until))
                return false;
            if (Time.unscaledTime >= until)
            {
                SilencedUntil.Remove(unit.GetInstanceID());
                return false;
            }
            return true;
        }

        internal static void Draw()
        {
            Event e = Event.current;
            if (e != null && e.type != EventType.Repaint)
                return;
            if (!_active || _seq == null)
            {
                if (!_hintWanted)
                    return;
                DrawHint();
                return;
            }

            EnsureStyles();
            EnsureDirTex();
            int depthPrev = GUI.depth;
            GUI.depth = -80;
            float sw = UiScaleService.Width;
            float sh = UiScaleService.Height;
            int n = _seq.Length;
            int cols = 16;
            if (n <= 16)
                cols = n;
            else if (n <= 40)
                cols = 16;
            else if (n <= 80)
                cols = 20;
            else if (n <= 160)
                cols = 24;
            else if (n <= 280)
                cols = 28;
            else
                cols = 32;
            float panelW = sw * 0.92f;
            float panelH = sh * 0.28f;
            if (n > 80)
                panelH = sh * 0.48f;
            if (n > 160)
                panelH = sh * 0.62f;
            if (n > 280)
                panelH = sh * 0.74f;
            float panelY = Mathf.Max(36f, (sh - panelH) * 0.5f);
            Rect panel = new Rect((sw - panelW) * 0.5f, panelY, panelW, panelH);

            Color prev = GUI.color;
            GUI.color = new Color(0.38f, 0.03f, 0.03f, 0.72f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.45f, 0.08f, 1f);
            float b = 3f;
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, b), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - b, panel.width, b), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(panel.x, panel.y, b, panel.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(panel.xMax - b, panel.y, b, panel.height), Texture2D.whiteTexture);

            int rows = (n + cols - 1) / cols;
            if (rows < 1)
                rows = 1;
            float padX = 10f;
            float padTop = 22f;
            float padBot = 6f;
            float innerW = panel.width - padX * 2f;
            float innerH = panel.height - padTop - padBot;
            float cell = Mathf.Min(innerW / cols, innerH / rows);
            float gridW = cell * cols;
            float gridH = cell * rows;
            float ox = panel.x + (panel.width - gridW) * 0.5f;
            float oy = panel.y + padTop + (innerH - gridH) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                int row = i / cols;
                int col = i % cols;
                Rect cellR = new Rect(ox + col * cell, oy + row * cell, cell, cell);
                Color c;
                if (i < _index)
                    c = new Color(1f, 0.08f, 0.05f, 1f);
                else if (i == _index)
                    c = Color.white;
                else
                    c = new Color(0.42f, 0.42f, 0.42f, 0.88f);
                DrawArrow(cellR, _seq[i], c);
            }

            GUI.color = new Color(1f, 0.92f, 0.15f, 1f);
            GUI.Label(new Rect(panel.x + 6f, panel.y - 28f, 720f, 26f),
                SystemName() + "  " + UiLang.T("Jamming target...", "正在干扰目标..."),
                _titleStyle);

            int shown = Mathf.Min(_index + 1, n);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.xMax - 168f, panel.y + 4f, 160f, 22f),
                shown.ToString() + "/" + n.ToString(), _countStyle);

            float left = Mathf.Max(0f, _deadline - Time.unscaledTime);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 6f, panel.yMax + 4f, 320f, 32f),
                FormatTimer(left), _timerStyle);

            GUI.color = prev;
            GUI.depth = depthPrev;
        }

        private static void DrawHint()
        {
            if (!On())
                return;
            float now = Time.unscaledTime;
            if (now >= _hintAt)
            {
                _hintAt = now + 0.45f;
                _hintLine = BuildHintLine();
            }
            if (string.IsNullOrEmpty(_hintLine))
                return;
            EnsureStyles();
            float sw = UiScaleService.Width;
            Rect chip = new Rect(sw * 0.5f - 240f, 10f, 480f, 22f);
            Color prev = GUI.color;
            GUI.color = new Color(0.12f, 0.02f, 0.02f, 0.62f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.78f, 0.38f, 1f);
            GUI.Label(chip, _hintLine, _hintStyle);
            GUI.color = prev;
        }

        private static string BuildHintLine()
        {
            Aircraft ac = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out ac) || ac == null)
                    return null;
            }
            catch
            {
                return null;
            }
            if (!IsJammerSelected(ac))
                return null;
            Unit locked = ResolveLock(ac);
            if (locked == null)
                return SystemName() + UiLang.T("  no lock = hold ECM", "  无锁定 = 按住ECM");
            if (RadarWarfare.SameHq(ac, locked))
                return SystemName() + UiLang.T("  friendly lock", "  友军锁定");
            if (!InRange(ac, locked))
                return SystemName() + UiLang.T("  lock out of range", "  锁定超出距离");
            return SystemName() + UiLang.T("  press CM for arrows", "  按干扰键开始");
        }

        internal static bool IsJammerSelected(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            try
            {
                if (aircraft.countermeasureManager == null)
                    return false;
                Countermeasure cm = aircraft.countermeasureManager.GetActiveCountermeasure();
                return cm is RadarJammer;
            }
            catch
            {
                return false;
            }
        }

        private static void Succeed()
        {
            Unit victim = _target;
            Stop(true);
            if (victim == null || victim.disabled)
                return;
            Aircraft foe = victim as Aircraft;
            if (foe != null)
            {
                try { foe.StartEjectionSequence(); }
                catch { }
                Flash(UiLang.T("Target ejected", "已弹射目标"), 2.4f);
                return;
            }
            float dur = SilenceSeconds != null ? SilenceSeconds.Value : 60f;
            SilencedUntil[victim.GetInstanceID()] = Time.unscaledTime + Mathf.Max(5f, dur);
            try
            {
                Turret[] turrets = victim.GetComponentsInChildren<Turret>(true);
                if (turrets != null)
                {
                    for (int i = 0; i < turrets.Length; i++)
                    {
                        if (turrets[i] != null)
                            turrets[i].FireControlDisabled();
                    }
                }
            }
            catch { }
            Flash(UiLang.T("Weapons jammed 1 min", "无法开火 1 分钟"), 2.4f);
        }

        private static void Fail()
        {
            Aircraft me = _self;
            Stop(true);
            if (me == null || me.disabled || me.HasEjected())
                return;
            try { me.StartEjectionSequence(); }
            catch { }
            Flash(UiLang.T("Jammer failed — eject", "干扰失败 — 弹射自己"), 2.6f);
        }

        private static void Stop(bool restoreAssist)
        {
            Aircraft ac = _self;
            _active = false;
            _self = null;
            _target = null;
            _seq = null;
            _index = 0;
            if (!restoreAssist || ac == null || !_hasSavedAssist)
            {
                _hasSavedAssist = false;
                return;
            }
            try
            {
                if (ac.flightAssist != _savedAssist)
                    ac.SetFlightAssist(_savedAssist);
            }
            catch { }
            _hasSavedAssist = false;
        }

        private static bool IsCooling(Aircraft ac)
        {
            if (ac == null)
                return false;
            if (ac.GetInstanceID() != _cdAircraftId)
                return false;
            return Time.unscaledTime < _cdUntil;
        }

        private static Unit ResolveLock(Aircraft ac)
        {
            Unit u = SelectedHudMarker(ac);
            if (u != null)
                return u;
            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null)
                {
                    u = FirstEnemy(ac, hud.GetTargetList());
                    if (u != null)
                        return u;
                }
            }
            catch { }
            try
            {
                if (ac != null && ac.weaponManager != null)
                {
                    u = FirstEnemy(ac, ac.weaponManager.GetTargetList());
                    if (u != null)
                        return u;
                    WeaponStation st = ac.weaponManager.currentWeaponStation;
                    if (st != null && st.Weapons != null)
                    {
                        for (int i = 0; i < st.Weapons.Count; i++)
                        {
                            Weapon w = st.Weapons[i];
                            if (w == null)
                                continue;
                            Unit t = null;
                            try { t = w.GetTarget(); }
                            catch { }
                            if (t == null && WeaponTarget != null)
                            {
                                try { t = WeaponTarget.GetValue(w) as Unit; }
                                catch { }
                            }
                            if (IsUsableEnemy(ac, t))
                                return t;
                        }
                    }
                }
            }
            catch { }
            try
            {
                Radar radar = ac != null ? ac.GetComponentInChildren<Radar>(true) : null;
                if (radar != null && radar.detectedTargets != null)
                {
                    for (int i = 0; i < radar.detectedTargets.Count; i++)
                    {
                        Unit t = radar.detectedTargets[i];
                        if (!IsUsableEnemy(ac, t))
                            continue;
                        try
                        {
                            if (radar.CheckIsTarget(t))
                                return t;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private static Unit SelectedHudMarker(Aircraft ac)
        {
            if (HudMarkerLookup == null)
                return null;
            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud == null)
                    return null;
                Dictionary<Unit, HUDUnitMarker> lookup =
                    HudMarkerLookup.GetValue(hud) as Dictionary<Unit, HUDUnitMarker>;
                if (lookup == null)
                    return null;
                Dictionary<Unit, HUDUnitMarker>.Enumerator en = lookup.GetEnumerator();
                while (en.MoveNext())
                {
                    HUDUnitMarker mk = en.Current.Value;
                    if (mk == null || !mk.selected)
                        continue;
                    if (IsUsableEnemy(ac, en.Current.Key))
                        return en.Current.Key;
                }
            }
            catch { }
            return null;
        }

        private static Unit FirstEnemy(Aircraft ac, List<Unit> list)
        {
            if (list == null)
                return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (IsUsableEnemy(ac, list[i]))
                    return list[i];
            }
            return null;
        }

        private static bool IsUsableEnemy(Aircraft ac, Unit u)
        {
            if (ac == null || u == null || u.disabled)
                return false;
            if (object.ReferenceEquals(u, ac))
                return false;
            if (RadarWarfare.SameHq(ac, u))
                return false;
            return true;
        }

        private static bool InRange(Aircraft ac, Unit u)
        {
            if (ac == null || u == null)
                return false;
            try
            {
                float d = FastMath.Distance(
                    GlobalPositionExtensions.GlobalPosition(ac),
                    GlobalPositionExtensions.GlobalPosition(u));
                return d <= RangeM;
            }
            catch
            {
                return false;
            }
        }

        private static float HeadingOf(Aircraft ac)
        {
            Vector3 track = ac.rb != null && ac.rb.velocity.sqrMagnitude > 25f
                ? ac.rb.velocity : ac.transform.forward;
            track.y = 0f;
            if (track.sqrMagnitude < 0.01f)
                track = Vector3.forward;
            return Mathf.Atan2(track.x, track.z) * Mathf.Rad2Deg;
        }

        private static int ReadDirDown()
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
                return DirUp;
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
                return DirDown;
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
                return DirLeft;
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
                return DirRight;
            return -1;
        }

        private static void DrawArrow(Rect cell, int dir, Color color)
        {
            if (_dirTex == null || dir < 0 || dir > 3)
                return;
            Texture2D tex = _dirTex[dir];
            if (tex == null)
                return;
            float s = Mathf.Min(cell.width, cell.height) * 0.82f;
            Rect r = new Rect(cell.x + (cell.width - s) * 0.5f,
                cell.y + (cell.height - s) * 0.5f, s, s);
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        private static void EnsureDirTex()
        {
            if (_dirTex != null && _dirTex.Length == 4 && _dirTex[0] != null)
                return;
            _dirTex = new Texture2D[4];
            _dirTex[DirUp] = MakeArrowTex(DirUp);
            _dirTex[DirDown] = MakeArrowTex(DirDown);
            _dirTex[DirLeft] = MakeArrowTex(DirLeft);
            _dirTex[DirRight] = MakeArrowTex(DirRight);
        }

        private static Texture2D MakeArrowTex(int dir)
        {
            int s = 64;
            Texture2D t = new Texture2D(s, s, TextureFormat.ARGB32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.hideFlags = HideFlags.HideAndDontSave;
            Color[] px = new Color[s * s];
            Color on = Color.white;
            Color off = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float nx = (x + 0.5f) / s;
                    float ny = (y + 0.5f) / s;
                    float u = nx;
                    float v = ny;
                    if (dir == DirDown)
                    {
                        u = nx;
                        v = 1f - ny;
                    }
                    else if (dir == DirRight)
                    {
                        u = ny;
                        v = nx;
                    }
                    else if (dir == DirLeft)
                    {
                        u = ny;
                        v = 1f - nx;
                    }
                    float tPeak = (0.94f - v) / 0.82f;
                    float half = tPeak * 0.46f;
                    bool hit = v > 0.12f && v < 0.94f && Mathf.Abs(u - 0.5f) <= half && tPeak >= 0f;
                    px[y * s + x] = hit ? on : off;
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        private static void EnsureStyles()
        {
            if (_stylesReady && _titleStyle != null && _hintStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 18;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.wordWrap = false;
            _titleStyle.clipping = TextClipping.Overflow;
            _titleStyle.normal.textColor = new Color(1f, 0.92f, 0.15f, 1f);
            _timerStyle = new GUIStyle(GUI.skin.label);
            _timerStyle.fontSize = 24;
            _timerStyle.fontStyle = FontStyle.Bold;
            _timerStyle.alignment = TextAnchor.UpperLeft;
            _timerStyle.wordWrap = false;
            _timerStyle.clipping = TextClipping.Overflow;
            _timerStyle.normal.textColor = Color.white;
            _countStyle = new GUIStyle(GUI.skin.label);
            _countStyle.fontSize = 18;
            _countStyle.fontStyle = FontStyle.Bold;
            _countStyle.alignment = TextAnchor.UpperRight;
            _countStyle.wordWrap = false;
            _countStyle.clipping = TextClipping.Overflow;
            _countStyle.normal.textColor = Color.white;
            _hintStyle = new GUIStyle(GUI.skin.label);
            _hintStyle.fontSize = 15;
            _hintStyle.fontStyle = FontStyle.Bold;
            _hintStyle.alignment = TextAnchor.MiddleCenter;
            _hintStyle.wordWrap = false;
            _hintStyle.clipping = TextClipping.Clip;
            _hintStyle.normal.textColor = new Color(1f, 0.78f, 0.38f, 1f);
            try
            {
                ChineseFontPatch.ApplyTo(_titleStyle);
                ChineseFontPatch.ApplyTo(_timerStyle);
                ChineseFontPatch.ApplyTo(_countStyle);
                ChineseFontPatch.ApplyTo(_hintStyle);
            }
            catch { }
            _stylesReady = true;
        }

        private static string FormatTimer(float seconds)
        {
            int ms = Mathf.Max(0, Mathf.CeilToInt(seconds * 1000f));
            int m = ms / 60000;
            int sec = (ms / 1000) % 60;
            int milli = ms % 1000;
            return string.Format("{0:00}:{1:00}.{2:000}", m, sec, milli);
        }

        private static void Flash(string text, float seconds)
        {
            try
            {
                if (SceneSingleton<AircraftActionsReport>.i != null)
                    SceneSingleton<AircraftActionsReport>.i.ReportText(text, seconds);
            }
            catch { }
        }

        private static void PruneSilence()
        {
            if (SilencedUntil.Count == 0)
                return;
            List<int> dead = null;
            Dictionary<int, float>.Enumerator en = SilencedUntil.GetEnumerator();
            while (en.MoveNext())
            {
                if (Time.unscaledTime >= en.Current.Value)
                {
                    if (dead == null)
                        dead = new List<int>(4);
                    dead.Add(en.Current.Key);
                }
            }
            if (dead == null)
                return;
            for (int i = 0; i < dead.Count; i++)
                SilencedUntil.Remove(dead[i]);
        }
    }

    [HarmonyPatch(typeof(Weapon), "Fire", new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    internal static class Patch_Weapon_Fire_JammerQte
    {
        private static bool Prefix(Unit owner)
        {
            return !JammerQte.IsFireSilenced(owner);
        }
    }

    [HarmonyPatch(typeof(Weapon), "RemoteSingleFire", new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    internal static class Patch_Weapon_RemoteFire_JammerQte
    {
        private static bool Prefix(Unit owner)
        {
            return !JammerQte.IsFireSilenced(owner);
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire", new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    internal static class Patch_Gun_Fire_JammerQte
    {
        private static bool Prefix(Unit firingUnit)
        {
            return !JammerQte.IsFireSilenced(firingUnit);
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "Fire", new Type[] { typeof(Unit), typeof(Unit) })]
    internal static class Patch_WeaponStation_Fire_JammerQte
    {
        private static bool Prefix(Unit owner)
        {
            return !JammerQte.IsFireSilenced(owner);
        }
    }
}
