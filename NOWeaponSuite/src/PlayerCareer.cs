using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using NuclearOption.Networking;
using NuclearOption.Networking.Lobbies;
using NuclearOption.SavedMission;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Cosmetic career profile for main menu: prestige level, kill stats, achievements.
    /// Inspired by community requests (level showcase, PvP/AI weapon stats, aircraft badges).
    /// Does not alter vanilla mission ranks.
    /// </summary>
    internal static class PlayerCareer
    {
        private const string Pref = "WeXon.Career.";
        private static int MaxLevel { get { return CareerXpMathService.MaxLevel; } }
        private const int MaxRecentGames = 15;
        private static int XpPerLevelMin { get { return CareerXpMathService.XpPerLevelMin; } }
        private static int FlightXpCap { get { return CareerXpMathService.FlightXpCap; } }
        private static float FlightXpCapSeconds { get { return CareerXpMathService.FlightXpCapSeconds; } }

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _toggleKey;

        private static bool _panelOpen;
        private static bool _cursorHeld;
        private static int _tab; // 0 profile, 1 stats, 2 server, 3 recent, 4 badges, 5 about
        private static Vector2 _scroll;
        private static float _playAccum;
        private static float _nextSave;
        private static bool _loaded;
        /// <summary>In-memory dirty; disk flush only on autosave / quit / explicit Save button.</summary>
        private static bool _dirty;

        private static int _xp;
        private static int _prestige;
        private static float _playSeconds;
        private static int _killsPvp;
        private static int _killsAi;
        private static int _killsFriendly;
        private static int _killsMissile;
        private static int _killsGun;
        private static int _godSlayer;
        private static int _strongKill;
        private static int _bestStreak;

        private static readonly Dictionary<string, int> WeaponKills =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> VictimAircraftKills =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> PilotAircraftKills =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ServerPvpRecord> ServerPvp =
            new Dictionary<string, ServerPvpRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<GameSessionRecord> RecentGames = new List<GameSessionRecord>();

        private static GameSessionRecord _activeSession;
        private static bool _wasInMission;
        private static Aircraft _trackedAc;
        private static bool _trackedAlive;

        /// <summary>Last finalized match flight-score XP multiplier (for Profile UI).</summary>
        private static float _lastFlightXpMul = 1f;
        private static int _lastFlightXpScore;
        private static string _lastFlightXpGrade = "";
        private static int _lastFlightXpBase;
        private static int _lastFlightXpFinal;
        private static int _sessionCombatXp;
        private static bool _combatSessionLive;

        private static readonly AchievementDef[] Achievements = BuildAchievements();

        private sealed class ServerPvpRecord
        {
            public string Server;
            public int PvpKills;
            public int Deaths;
            public int Games;
            public int AiKills;
            public int FriendlyKills;
            public long LastTicks;
        }

        private sealed class GameSessionRecord
        {
            public long StartTicks;
            public string Server;
            public string Mission;
            public int PvpKills;
            public int AiKills;
            public int FriendlyKills;
            public int Deaths;
            public int BestStreak;
            public float DurationSec;
            public bool Multiplayer;
        }

        private static GUIStyle _titleStyle;
        private static GUIStyle _bodyStyle;
        private static GUIStyle _warnStyle;
        private static GUIStyle _ftkStyle;
        private static GUIStyle _tabOn;
        private static GUIStyle _tabOff;
        private static GUIStyle _badgeOn;
        private static GUIStyle _badgeOff;
        private static GUIStyle _btnStyle;
        private static GUIStyle _chipStyle;
        private static bool _quitFlushed;

        private sealed class AchievementDef
        {
            public string Id;
            public string Title;
            public string TitleZh;
            public string Desc;
            public string DescZh;
            public Func<bool> IsDone;
            public string Badge;

            public AchievementDef(string id, string title, string titleZh, string desc, string descZh,
                string badge, Func<bool> isDone)
            {
                Id = id;
                Title = title;
                TitleZh = titleZh;
                Desc = desc;
                DescZh = descZh;
                Badge = badge;
                IsDone = isDone;
            }

            public string LocTitle()
            {
                return ModUiLang.T(Title, TitleZh ?? Title);
            }

            public string LocDesc()
            {
                return ModUiLang.T(Desc, DescZh ?? Desc);
            }
        }

        private const string SoloServerLabel = "Solo / Local";
        private const string OnlineServerLabel = "Online Server";
        private const string UnknownMissionLabel = "Unknown Mission";

        private static AchievementDef[] BuildAchievements()
        {
            return new AchievementDef[]
            {
                new AchievementDef("first_blood", "First Blood", "首杀",
                    "Score your first air kill", "取得首次空中击杀", "*",
                    delegate { return TotalAirKills() >= 1; }),
                new AchievementDef("kills_25", "Rising Pilot", "新锐飞行员",
                    "25 air kills", "空中击杀 25", "*",
                    delegate { return TotalAirKills() >= 25; }),
                new AchievementDef("kills_100", "Veteran", "老兵",
                    "100 air kills", "空中击杀 100", "**",
                    delegate { return TotalAirKills() >= 100; }),
                new AchievementDef("kills_1000", "Thousand Cuts", "千斩",
                    "1000 air kills", "空中击杀 1000", "***",
                    delegate { return TotalAirKills() >= 1000; }),
                new AchievementDef("pvp_50", "Hunter", "猎手",
                    "Shoot down 50 player pilots", "击落 50 名玩家飞行员", "PvP",
                    delegate { return _killsPvp >= 50; }),
                new AchievementDef("pvp_1000", "Ace Hunter", "王牌猎手",
                    "Shoot down 1000 player pilots", "击落 1000 名玩家飞行员", "ACE",
                    delegate { return _killsPvp >= 1000; }),
                new AchievementDef("ai_500", "Sweeper", "清扫者",
                    "Shoot down 500 AI aircraft", "击落 500 架 AI 飞机", "AI",
                    delegate { return _killsAi >= 500; }),
                new AchievementDef("missile_200", "Missile Expert", "导弹专家",
                    "200 missile kills", "导弹击杀 200", "MSL",
                    delegate { return _killsMissile >= 200; }),
                new AchievementDef("gun_50", "Gunfighter", "炮战能手",
                    "50 gun kills", "航炮击杀 50", "GUN",
                    delegate { return _killsGun >= 50; }),
                new AchievementDef("god_1", "God Slayer", "神挡杀神",
                    "Get at least 1 God Slayer accolade", "至少获得 1 次神挡杀神", "GS",
                    delegate { return _godSlayer >= 1; }),
                new AchievementDef("god_10", "Mythic", "神话",
                    "God Slayer x10", "神挡杀神 ×10", "GS+",
                    delegate { return _godSlayer >= 10; }),
                new AchievementDef("streak_5", "Flying Ace", "空中王牌",
                    "Reach a 5-kill streak in one sortie", "单次出击达成 5 连杀", "5x",
                    delegate { return _bestStreak >= 5; }),
                new AchievementDef("lvl_100", "Level 100", "等级 100",
                    "Reach cosmetic level 100", "达到展示等级 100", "Lv",
                    delegate { return GetLevel() >= 100; }),
                new AchievementDef("lvl_1000", "Max Level", "满级",
                    "Reach level 1000", "达到等级 1000", "MAX",
                    delegate { return GetLevel() >= MaxLevel; }),
                new AchievementDef("prestige_1", "Prestige I", "威望 I",
                    "Prestige once after max level", "满级后完成一次威望循环", "P1",
                    delegate { return _prestige >= 1; }),
                new AchievementDef("carrier", "Fleet Command", "舰队指挥",
                    "Unlock extra carrier purchase", "解锁额外航母购买·当局支援", "CV",
                    delegate { return KillAccolades.HasUnlock(KillAccolades.UnlockCarrier); }),
                new AchievementDef("advanced", "Nuke Clearance", "核武许可",
                    "Unlock advanced arsenal weapons", "解锁高级支援武器", "N",
                    delegate { return KillAccolades.HasUnlock(KillAccolades.UnlockAdvanced); }),
                new AchievementDef("strategic", "Strategic Strike", "战略打击",
                    "Unlock strategic salvos", "解锁战略级齐射·当局支援", "STR",
                    delegate { return KillAccolades.HasUnlock(KillAccolades.UnlockStrategic); })
            };
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("PlayerCareer", "Enabled", true,
                "Main-menu career profile (level / stats / achievements).");
            _toggleKey = config.Bind("PlayerCareer", "MenuToggleKey", KeyCode.F8,
                "Toggle career panel on main menu (also click Oritasy button).");
            Load();
        }

        private static float _nextSessionTick;
        private static float _nextPlayProbe;

        internal static void Tick()
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (!_loaded)
                Load();

            // Playtime while flying — 1 Hz probe (was every frame GetLocalAircraft).
            if (Time.unscaledTime >= _nextPlayProbe)
            {
                _nextPlayProbe = Time.unscaledTime + 1f;
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                {
                    _playAccum += 1f;
                    if (_playAccum >= 5f)
                    {
                        _playSeconds += _playAccum;
                        _playAccum = 0f;
                    }
                }
            }

            // Autosave: keep RAM state hot; flush PlayerPrefs rarely (disk I/O stalls main thread).
            float saveEvery = _activeSession != null ? 45f : 90f;
            if (_dirty && Time.unscaledTime >= _nextSave)
            {
                _nextSave = Time.unscaledTime + saveEvery;
                if (_activeSession != null)
                    RefreshSessionDuration();
                Save(true);
            }
            else if (_activeSession != null && Time.unscaledTime >= _nextSave)
            {
                // Refresh draft duration even if no kill dirty — still rare.
                _nextSave = Time.unscaledTime + saveEvery;
                RefreshSessionDuration();
                Save(true);
            }

            // Mission session / death tracking — 2 Hz is enough
            if (Time.unscaledTime >= _nextSessionTick)
            {
                _nextSessionTick = Time.unscaledTime + 0.5f;
                UpdateMissionSession();
            }

            if (!IsMainMenu())
            {
                _panelOpen = false;
                ReleaseCursor();
                return;
            }

            if (Input.GetKeyDown(_toggleKey.Value))
                _panelOpen = !_panelOpen;
            if (_panelOpen)
                HoldCursor();
            else
                ReleaseCursor();
        }

        /// <summary>Finalize live session + flush PlayerPrefs (quit / unload).</summary>
        internal static void FlushForQuit()
        {
            if (_quitFlushed)
                return;
            _quitFlushed = true;
            if (_enabled == null || !_enabled.Value)
                return;
            if (!_loaded)
                Load();

            if (_playAccum > 0f)
            {
                _playSeconds += _playAccum;
                _playAccum = 0f;
            }

            if (_activeSession != null)
            {
                RefreshSessionDuration();
                EndSession();
                _wasInMission = false;
                try { FriendlyKillHud.ClearSession(); }
                catch { }
                try { MatchScoreboard.ClearSession(); }
                catch { }
                try { KillAccolades.ClearMatchUnlocks(); }
                catch { }
            }
            else
            {
                Save(true);
            }
        }

        private static void RefreshSessionDuration()
        {
            if (_activeSession == null)
                return;
            _activeSession.DurationSec = (float)(DateTime.UtcNow.Ticks - _activeSession.StartTicks)
                / TimeSpan.TicksPerSecond;
        }

        private static void UpdateMissionSession()
        {
            bool inMission = IsInMission();
            if (inMission)
            {
                if (!_wasInMission || _activeSession == null)
                    BeginSession();
                _wasInMission = true;
                TrackLocalDeaths();
                RefreshSessionDuration();
            }
            else if (_wasInMission)
            {
                EndSession();
                _wasInMission = false;
                FriendlyKillHud.ClearSession();
                MatchScoreboard.ClearSession();
                KillAccolades.ClearMatchUnlocks();
            }
        }

        private static bool IsInMission()
        {
            if (IsMainMenu())
                return false;
            try
            {
                if (MissionManager.IsRunning)
                    return true;
            }
            catch { }
            try
            {
                if (MissionManager.CurrentMission != null)
                    return true;
            }
            catch { }
            Aircraft ac;
            if (GameManager.GetLocalAircraft(out ac) && ac != null)
                return true;
            return false;
        }

        private static void BeginSession()
        {
            GameSessionRecord s = new GameSessionRecord();
            s.StartTicks = DateTime.UtcNow.Ticks;
            s.Server = ResolveServerName();
            s.Mission = ResolveMissionName();
            s.Multiplayer = IsMultiplayerSession();
            s.PvpKills = 0;
            s.AiKills = 0;
            s.FriendlyKills = 0;
            s.Deaths = 0;
            s.BestStreak = 0;
            s.DurationSec = 0f;
            _activeSession = s;
            _trackedAc = null;
            _trackedAlive = false;
            if (!_combatSessionLive)
            {
                _sessionCombatXp = 0;
                try { CombatKillXpTracker.ResetMatch(); }
                catch { }
            }
            try { KillAccolades.ClearMatchUnlocks(); }
            catch { }
            try { FlightAnalysis.OnSessionBegin(s.StartTicks, s.Mission, s.Server); }
            catch { }
        }

        private static void EndSession()
        {
            if (_activeSession == null)
                return;
            GameSessionRecord s = _activeSession;
            _activeSession = null;
            _trackedAc = null;
            _trackedAlive = false;

            // Skip empty tiny sessions (menu flickers)
            if (s.DurationSec < 20f && s.PvpKills == 0 && s.AiKills == 0
                && s.FriendlyKills == 0 && s.Deaths == 0)
            {
                try { FlightAnalysis.OnSessionEnd(s.StartTicks, false); }
                catch { }
                PlayerPrefs.DeleteKey(Pref + "draft");
                FinishCombatSession();
                Save(true);
                return;
            }

            // Avoid duplicate if draft was already recovered this boot
            bool already = false;
            for (int i = 0; i < RecentGames.Count; i++)
            {
                if (RecentGames[i] != null && RecentGames[i].StartTicks == s.StartTicks)
                {
                    already = true;
                    break;
                }
            }
            if (!already)
            {
                RecentGames.Insert(0, s);
                while (RecentGames.Count > MaxRecentGames)
                    RecentGames.RemoveAt(RecentGames.Count - 1);

                if (s.Multiplayer || s.PvpKills > 0 || s.FriendlyKills > 0)
                    MergeServerRecord(s);

                // Score first, then settle match XP with flight-score multiplier (≤ 3.8×).
                try { FlightAnalysis.OnSessionEnd(s.StartTicks, true); }
                catch { }
                SettleMatchXp(s);
            }
            else
            {
                try { FlightAnalysis.OnSessionEnd(s.StartTicks, false); }
                catch { }
            }

            FinishCombatSession();
            Save(true);
        }

        private static void FinishCombatSession()
        {
            _sessionCombatXp = 0;
            _combatSessionLive = false;
            try { CombatKillXpTracker.ResetMatch(); }
            catch { }
        }

        /// <summary>
        /// Match XP = (combat XP already granted + flight-time XP) × flight-score mul, capped at 3.8×.
        /// Grants the remainder after in-match combat XP so the session totals target XP.
        /// </summary>
        private static void SettleMatchXp(GameSessionRecord s)
        {
            if (s == null)
                return;
            int killXp = _sessionCombatXp;
            if (killXp < 0)
                killXp = 0;
            int flightXp = CareerXpMathService.CalcFlightXp(s.DurationSec);
            int baseXp = killXp + flightXp;

            float mul = 1f;
            int score = 0;
            string grade = "";
            try
            {
                float m;
                int sc;
                string g;
                if (FlightAnalysis.TryGetSessionXpMultiplier(s.StartTicks, out m, out sc, out g))
                {
                    mul = m;
                    score = sc;
                    grade = g ?? "";
                }
            }
            catch { mul = 1f; }

            mul = CareerXpMathService.ClampMatchXpMul(mul);

            int targetXp;
            int grant;
            int prem = CareerPremiumService.BaseXpMul();
            CareerXpMathService.SettleMatchGrant(baseXp, mul, killXp, out targetXp, out grant);
            targetXp *= prem;
            grant = targetXp - (killXp * prem);
            if (grant < 0)
                grant = 0;
            if (grant > 0)
                AddXpRaw(grant);

            _lastFlightXpMul = mul;
            _lastFlightXpScore = score;
            _lastFlightXpGrade = grade;
            _lastFlightXpBase = baseXp;
            _lastFlightXpFinal = targetXp;
        }

        /// <summary>Match flight XP: linear 0→1000 over 3 hours, hard cap 1000.</summary>
        private static int CalcFlightXp(float durationSec)
        {
            return CareerXpMathService.CalcFlightXp(durationSec);
        }

        private static void MergeServerRecord(GameSessionRecord s)
        {
            if (s == null || string.IsNullOrEmpty(s.Server))
                return;
            ServerPvpRecord r;
            if (!ServerPvp.TryGetValue(s.Server, out r) || r == null)
            {
                r = new ServerPvpRecord();
                r.Server = s.Server;
                ServerPvp[s.Server] = r;
            }
            r.PvpKills += s.PvpKills;
            r.AiKills += s.AiKills;
            r.FriendlyKills += s.FriendlyKills;
            r.Deaths += s.Deaths;
            r.Games++;
            r.LastTicks = DateTime.UtcNow.Ticks;
        }

        private static void TrackLocalDeaths()
        {
            if (_activeSession == null)
                return;
            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            if (ac == null)
            {
                if (_trackedAc != null && _trackedAlive)
                {
                    _activeSession.Deaths++;
                    _trackedAlive = false;
                }
                _trackedAc = null;
                return;
            }
            bool alive = Plugin.IsUnitAlive(ac);
            if (_trackedAc != null && object.ReferenceEquals(_trackedAc, ac) && _trackedAlive && !alive)
                _activeSession.Deaths++;
            _trackedAc = ac;
            _trackedAlive = alive;
        }

        private static string ResolveServerName()
        {
            try
            {
                SteamLobby sl = SteamLobby.instance;
                if (sl != null && !string.IsNullOrEmpty(sl.CurrentLobbyName))
                    return SanitizeKey(sl.CurrentLobbyName);
            }
            catch { }
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                if (nm != null && nm.Server != null && nm.Server.Active
                    && (nm.Client == null || !nm.Client.Active || IsListenWithOthers()))
                {
                    if (IsListenWithOthers() || IsMultiplayerSession())
                        return "Listen Host";
                }
                if (nm != null && nm.Client != null && nm.Client.Active)
                    return OnlineServerLabel;
            }
            catch { }
            return SoloServerLabel;
        }

        private static string ResolveMissionName()
        {
            try
            {
                Mission m = MissionManager.CurrentMission;
                if (m != null && !string.IsNullOrEmpty(m.Name))
                    return SanitizeKey(m.Name);
            }
            catch { }
            return UnknownMissionLabel;
        }

        private static bool IsMultiplayerSession()
        {
            try
            {
                SteamLobby sl = SteamLobby.instance;
                if (sl != null && !string.IsNullOrEmpty(sl.CurrentLobbyName))
                    return true;
            }
            catch { }
            return IsListenWithOthers();
        }

        private static bool IsListenWithOthers()
        {
            try
            {
                // Count human players via UnitRegistry aircraft with Player
                int humans = 0;
                List<Unit> all = UnitRegistry.allUnits;
                if (all == null)
                    return false;
                for (int i = 0; i < all.Count; i++)
                {
                    Aircraft ac = all[i] as Aircraft;
                    if (ac == null || !IsPlayerPiloted(ac))
                        continue;
                    humans++;
                    if (humans >= 2)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string SanitizeKey(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "?";
            s = s.Replace('|', '/').Replace(';', ',').Replace('=', '-').Trim();
            if (s.Length > 48)
                s = s.Substring(0, 48);
            return s;
        }

        internal static void DrawGui()
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (!IsMainMenu())
            {
                ReleaseCursor();
                return;
            }

            EnsureStyles();

            // Always-visible entry chip — top-right of main menu (F2 tip chip style)
            float chipW = 260f;
            float chipH = 28f;
            Rect chip = new Rect(GuiScale.Width - chipW - 18f, 18f, chipW, chipH);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _panelOpen
                ? new Color(0.35f, 0.95f, 0.55f, 0.95f)
                : new Color(0.45f, 0.8f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string chipLine = _panelOpen
                ? ModUiLang.T("Profile  Lv." + GetLevel() + "  |  OPEN",
                    "档案  Lv." + GetLevel() + "  |  已打开")
                : ModUiLang.T("Profile  Lv." + GetLevel() + "  |  F8",
                    "档案  Lv." + GetLevel() + "  |  F8");
            if (GUI.Button(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), chipLine, _chipStyle))
                _panelOpen = !_panelOpen;
            GUI.color = prev;

            if (!_panelOpen)
            {
                ReleaseCursor();
                return;
            }
            HoldCursor();

            // Match F2 autopilot menu panel style
            float w = Mathf.Min(520f, GuiScale.Width * 0.94f);
            float h = Mathf.Min(560f, GuiScale.Height * 0.88f);
            Rect box = new Rect((GuiScale.Width - w) * 0.5f, (GuiScale.Height - h) * 0.5f, w, h);
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.8f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string keyHint = _toggleKey != null ? _toggleKey.Value.ToString() : "F8";
            string ver = ResolveOritasyDisplayRelease();
            // ZH: short title without parenthetical key hint (F8 stays on tip chip).
            string edition = ModUiLang.T(
                "ORITASY PROFILE  ·  " + ver + "  (" + keyHint + ")",
                "ORITASY 档案  ·  " + ver);
            GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, 26f),
                edition, _titleStyle);
            GUI.Label(new Rect(box.x + 16f, box.y + 38f, box.width - 32f, 18f),
                ModUiLang.EnglishOnlyEdition
                    ? "English-only Special Edition · local showcase · no vanilla rank effect"
                    : ModUiLang.T(
                        "Standard edition · local showcase · does not affect vanilla ranks",
                        "标准版 · 本地展示 · 不影响原版军衔"),
                _bodyStyle);

            Rect body = new Rect(box.x + 12f, box.y + 62f, box.width - 24f, box.height - 110f);
            GUILayout.BeginArea(body);

            GUILayout.BeginHorizontal();
            DrawTab(0, ModUiLang.T("Profile", "档案"));
            DrawTab(1, ModUiLang.T("Stats", "统计"));
            DrawTab(2, ModUiLang.T("Server", "服务器"));
            DrawTab(3, ModUiLang.T("Last 15", "最近15局"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawTab(4, ModUiLang.T("Badges", "徽章"));
            DrawTab(5, ModUiLang.T("About", "关于"));
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.ExpandHeight(true));
            if (_tab == 0)
                DrawProfileTab();
            else if (_tab == 1)
                DrawStatsTab();
            else if (_tab == 2)
                DrawServerPvpTab();
            else if (_tab == 3)
                DrawRecentGamesTab();
            else if (_tab == 4)
                DrawAchievementsTab();
            else
                DrawNotesTab();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            float bw = (box.width - 48f) * 0.5f;
            float by = box.yMax - 40f;
            if (GUI.Button(new Rect(box.x + 16f, by, bw, 30f),
                ModUiLang.T("Save", "保存"), _btnStyle))
                Save(true);
            if (GUI.Button(new Rect(box.x + 24f + bw, by, bw, 30f),
                ModUiLang.T("Close", "关闭"), _btnStyle))
            {
                _panelOpen = false;
                ReleaseCursor();
            }
        }

        private static void HoldCursor()
        {
#if ORITASY_COMBINED
            if (_cursorHeld)
                Oritasy.OritasyCursor.Pulse();
            else
            {
                Oritasy.OritasyCursor.Hold();
                _cursorHeld = true;
            }
            try
            {
                UnityEngine.EventSystems.EventSystem es =
                    UnityEngine.EventSystems.EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null)
                    es.SetSelectedGameObject(null);
            }
            catch { }
#endif
        }

        private static void ReleaseCursor()
        {
#if ORITASY_COMBINED
            if (!_cursorHeld)
                return;
            Oritasy.OritasyCursor.Release();
            _cursorHeld = false;
#endif
        }

        /// <summary>Same-faction kill — counted separately, no XP / accolades.</summary>
        internal static void RecordFriendlyKill(Unit victim, Aircraft killerAc)
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (!_loaded)
                Load();
            if (victim == null)
                return;

            // No XP / streaks — FTK is tracked only
            _killsFriendly++;
            if (_activeSession == null && IsInMission())
                BeginSession();
            if (_activeSession != null)
            {
                _activeSession.FriendlyKills++;
                RefreshSessionDuration();
                if (string.IsNullOrEmpty(_activeSession.Server) || _activeSession.Server == SoloServerLabel)
                    _activeSession.Server = ResolveServerName();
                if (!_activeSession.Multiplayer && IsMultiplayerSession())
                    _activeSession.Multiplayer = true;
            }
            MarkDirty(); // flushed by autosave / quit — avoid PlayerPrefs.Save per kill
        }

        /// <summary>Legacy air-only entry — forwards to RecordKill.</summary>
        internal static void RecordAirKill(Aircraft victim, Aircraft killerAc, string weaponHint, float skillGap, int streak)
        {
            RecordKill(victim, killerAc, weaponHint, skillGap, streak);
        }

        /// <summary>Called from KillAccolades after a confirmed local enemy kill (air / ground / ship / building).</summary>
        internal static void RecordKill(Unit victim, Aircraft killerAc, string weaponHint, float skillGap, int streak)
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (!_loaded)
                Load();

            bool sameFaction = killerAc != null && victim != null && Plugin.IsSameFaction(killerAc, victim);
            CareerRecordGateService.KillPath path = CareerRecordGateService.ResolveRecordKill(
                true, victim == null, sameFaction);
            if (path == CareerRecordGateService.KillPath.Skip)
                return;
            if (path == CareerRecordGateService.KillPath.Friendly)
            {
                RecordFriendlyKill(victim, killerAc);
                return;
            }

            Aircraft victimAc = victim as Aircraft;
            bool pvp = CareerRecordGateService.IsPvpVictim(victimAc != null, victimAc != null && IsPlayerPiloted(victimAc));
            if (pvp)
                _killsPvp++;
            else
                _killsAi++;

            if (_activeSession == null && IsInMission())
                BeginSession();
            if (_activeSession != null)
            {
                if (pvp)
                    _activeSession.PvpKills++;
                else
                    _activeSession.AiKills++;
                if (streak > _activeSession.BestStreak)
                    _activeSession.BestStreak = streak;
                if (string.IsNullOrEmpty(_activeSession.Server) || _activeSession.Server == SoloServerLabel)
                    _activeSession.Server = ResolveServerName();
                if (!_activeSession.Multiplayer && IsMultiplayerSession())
                    _activeSession.Multiplayer = true;
            }

            string weapon = string.IsNullOrEmpty(weaponHint) ? "Unknown" : weaponHint;
            bool missile = !CareerXpMathService.IsGunWeaponLabel(weapon);
            if (missile)
                _killsMissile++;
            else
                _killsGun++;

            AddDict(WeaponKills, weapon, 1);

            string vName = UnitLabel(victim);
            AddDict(VictimAircraftKills, vName, 1);

            if (killerAc != null)
                AddDict(PilotAircraftKills, AircraftLabel(killerAc), 1);

            CareerRecordGateService.ApplySkillCounters(
                victimAc != null, skillGap,
                KillCombatMathService.DefaultStrongGap, KillCombatMathService.DefaultGodGap,
                ref _godSlayer, ref _strongKill);
            _bestStreak = CareerRecordGateService.ApplyBestStreak(_bestStreak, streak);

            MarkDirty();
        }

        internal static string GuessWeapon(Aircraft localAc)
        {
            if (localAc == null)
                return "Missile";
            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all == null)
                    return "Missile";
                string name = null;
                float bestAge = 999f;
                for (int i = 0; i < all.Count; i++)
                {
                    Missile m = all[i] as Missile;
                    if (m == null)
                        continue;
                    Unit owner = m.owner;
                    if (owner == null || !object.ReferenceEquals(owner, localAc))
                        continue;
                    float age = 999f;
                    try { age = m.timeSinceSpawn; }
                    catch { age = 999f; }
                    if (age > 25f)
                        continue;
                    if (age > bestAge)
                        continue;
                    string wn = null;
                    try
                    {
                        if (m.definition != null)
                            wn = m.definition.unitName;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(wn))
                        wn = m.name;
                    if (!string.IsNullOrEmpty(wn))
                    {
                        name = wn.Replace("(Clone)", string.Empty).Trim();
                        bestAge = age;
                    }
                }
                if (!string.IsNullOrEmpty(name))
                    return CleanWeaponName(name);
            }
            catch { }
            return "Guns / Other";
        }

        private static string CleanWeaponName(string n)
        {
            return CareerXpMathService.CleanWeaponName(n);
        }

        private static void AddXp(int amount)
        {
            if (amount <= 0)
                return;
            AddXpRaw(amount * CareerPremiumService.BaseXpMul());
        }

        private static void AddXpRaw(int amount)
        {
            if (amount <= 0)
                return;
            _xp += amount;
            // Prestige when overflowing max level XP (one pass)
            int newXp;
            int newPrestige;
            CareerXpMathService.ApplyPrestigeOverflow(_xp, _prestige, out newXp, out newPrestige);
            _xp = newXp;
            _prestige = newPrestige;
            _dirty = true;
        }

        /// <summary>Immediate career XP (clean landing bonus, etc.). Returns false if career off.</summary>
        internal static bool TryGrantBonusXp(int amount)
        {
            if (amount <= 0)
                return false;
            if (_enabled == null || !_enabled.Value)
                return false;
            if (!_loaded)
                Load();
            AddXp(amount);
            return true;
        }

        /// <summary>Hit / module combat XP. Counts toward match settle already-granted.</summary>
        internal static bool TryGrantCombatXp(int amount)
        {
            if (!TryGrantBonusXp(amount))
                return false;
            _sessionCombatXp += amount;
            _combatSessionLive = true;
            return true;
        }

        private static int TotalAirKills()
        {
            return _killsPvp + _killsAi;
        }

        internal static int GetLevel()
        {
            return Mathf.Clamp(GetLevelFromXp(_xp), 1, MaxLevel);
        }

        internal static int DebugXp()
        {
            if (!_loaded)
                Load();
            return _xp;
        }

        internal static int DebugPrestige()
        {
            if (!_loaded)
                Load();
            return _prestige;
        }

        internal static void DebugAddXp(int amount)
        {
            if (!_loaded)
                Load();
            AddXpRaw(amount);
            Save(true);
        }

        internal static void DebugAddPrestige(int amount)
        {
            if (!_loaded)
                Load();
            _prestige += amount;
            if (_prestige < 0)
                _prestige = 0;
            if (_prestige > 999)
                _prestige = 999;
            _dirty = true;
            Save(true);
        }

        private static int XpCostToAdvance(int fromLevel)
        {
            return CareerXpMathService.XpCostToAdvance(fromLevel);
        }

        private static int GetLevelFromXp(int xp)
        {
            return CareerXpMathService.GetLevelFromXp(xp);
        }

        private static int XpForLevel(int level)
        {
            return CareerXpMathService.XpForLevel(level);
        }

        private static int XpToNext()
        {
            return CareerXpMathService.XpToNext(_xp);
        }

        private static MainMenu _cachedMainMenu;
        private static float _nextMainMenuProbe;
        private static bool _cachedIsMainMenu;

        private static bool IsMainMenu()
        {
            // Always honor probe interval — including when MainMenu is missing in-mission.
            if (Time.unscaledTime < _nextMainMenuProbe)
                return _cachedIsMainMenu;

            float interval = CareerRecordGateService.MainMenuCacheInterval(_cachedIsMainMenu);
            _nextMainMenuProbe = Time.unscaledTime + interval;
            try
            {
                if (_cachedMainMenu == null || !_cachedMainMenu)
                    _cachedMainMenu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                _cachedIsMainMenu = _cachedMainMenu != null && _cachedMainMenu.isActiveAndEnabled;
            }
            catch
            {
                _cachedMainMenu = null;
                _cachedIsMainMenu = false;
            }
            return _cachedIsMainMenu;
        }

        private static bool IsPlayerPiloted(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                Player p = ac.Player;
                if (p == null)
                    return false;
                // Human-controlled aircraft expose a Player; AI usually has null Player.
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string AircraftLabel(Aircraft ac)
        {
            return UnitLabel(ac);
        }

        private static string UnitLabel(Unit u)
        {
            if (u == null)
                return "Unknown";
            try
            {
                if (u.definition != null && !string.IsNullOrEmpty(u.definition.unitName))
                    return u.definition.unitName;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(u.name))
                    return u.name.Replace("(Clone)", string.Empty).Trim();
            }
            catch { }
            if (u is Aircraft)
                return "Aircraft";
            if (u is Ship)
                return "Ship";
            if (u is GroundVehicle)
                return "Ground";
            if (u is Building)
                return "Building";
            return "Unit";
        }

        private static void AddDict(Dictionary<string, int> dict, string key, int add)
        {
            if (dict == null || string.IsNullOrEmpty(key))
                return;
            int v;
            if (dict.TryGetValue(key, out v))
                dict[key] = v + add;
            else
                dict[key] = add;
        }

        private static void DrawTab(int id, string label)
        {
            bool on = _tab == id;
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = on
                ? new Color(0.25f, 0.7f, 0.4f, 0.95f)
                : new Color(0.2f, 0.25f, 0.3f, 0.9f);
            if (GUILayout.Button(label, _btnStyle, GUILayout.Height(28f), GUILayout.Width(76f)))
                _tab = id;
            GUI.backgroundColor = prev;
        }

        private static void DrawProfileTab()
        {
            int lvl = GetLevel();
            int need = XpToNext();
            float pct = CareerXpMathService.LevelBarPct(_xp, lvl);

            GUILayout.Label(ModUiLang.T(
                "Level  " + lvl + " / " + MaxLevel,
                "等级  " + lvl + " / " + MaxLevel), _titleStyle);
            if (_prestige > 0)
                GUILayout.Label(ModUiLang.T(
                    "Prestige  x" + _prestige + "  (cosmetic loop after max)",
                    "威望  x" + _prestige + " · 满级后的展示循环"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "XP  " + _xp + (need > 0 ? ("   to next  " + need) : "   MAX")
                    + "   (next step " + XpCostToAdvance(lvl).ToString() + ")",
                "经验  " + _xp + (need > 0 ? ("   距下级  " + need) : "   已满")
                    + " · 本级步进 " + XpCostToAdvance(lvl).ToString()), _bodyStyle);
            int premMul = CareerPremiumService.BaseXpMul();
            GUILayout.Label(ModUiLang.T(
                "XP rules  hit +" + (CareerXpMathService.XpPerHit * premMul)
                    + " (cap " + (CareerXpMathService.HitXpCapPerAircraft * premMul) + "/aircraft)"
                    + " · module +" + (CareerXpMathService.XpPerModule * premMul)
                    + " · missile +" + (CareerXpMathService.XpPerMissile * premMul)
                    + " · ground +" + (CareerXpMathService.XpPerGround * premMul)
                    + " · navy +" + (CareerXpMathService.XpPerNavy * premMul)
                    + " · carrier +" + (CareerXpMathService.XpPerCarrier * premMul)
                    + " · flight up to +" + (FlightXpCap * premMul) + "/match (3h cap)"
                    + (premMul > 1 ? ("  ·  premium x" + premMul) : ""),
                "经验规则  命中 +" + (CareerXpMathService.XpPerHit * premMul)
                    + "（每机上限 " + (CareerXpMathService.HitXpCapPerAircraft * premMul) + "）"
                    + " · 摧毁模块 +" + (CareerXpMathService.XpPerModule * premMul)
                    + " · 导弹 +" + (CareerXpMathService.XpPerMissile * premMul)
                    + " · 地面 +" + (CareerXpMathService.XpPerGround * premMul)
                    + " · 海军 +" + (CareerXpMathService.XpPerNavy * premMul)
                    + " · 航母 +" + (CareerXpMathService.XpPerCarrier * premMul)
                    + " · 飞行最多 +" + (FlightXpCap * premMul) + "/局 · 3小时封顶"
                    + (premMul > 1 ? ("  ·  高级 x" + premMul) : "")), _bodyStyle);
            if (_lastFlightXpFinal > 0 || _lastFlightXpMul > 1.001f)
            {
                string mulLine = FlightAnalysis.FormatXpMulLabel(_lastFlightXpMul, ModUiLang.IsChinese);
                if (_lastFlightXpScore > 0)
                {
                    mulLine += ModUiLang.IsChinese
                        ? ("  评分 " + _lastFlightXpScore + (_lastFlightXpGrade.Length > 0 ? ("/" + _lastFlightXpGrade) : ""))
                        : ("  score " + _lastFlightXpScore + (_lastFlightXpGrade.Length > 0 ? ("/" + _lastFlightXpGrade) : ""));
                }
                if (_lastFlightXpBase > 0)
                {
                    mulLine += ModUiLang.IsChinese
                        ? ("  本局 " + _lastFlightXpBase + "→" + _lastFlightXpFinal)
                        : ("  match " + _lastFlightXpBase + "→" + _lastFlightXpFinal);
                }
                GUILayout.Label(mulLine, _bodyStyle);
            }

            Rect bar = GUILayoutUtility.GetRect(200f, 14f);
            GUI.color = new Color(0.15f, 0.2f, 0.18f, 1f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.4f, 0.95f, 0.6f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * pct, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T(
                "Flight time  " + FormatPlaytime(_playSeconds + _playAccum),
                "飞行时间  " + FormatPlaytime(_playSeconds + _playAccum)), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "Kills  " + TotalAirKills()
                    + "   (Players " + _killsPvp + " / AI+ground " + _killsAi + ")",
                "击杀  " + TotalAirKills()
                    + " · 玩家 " + _killsPvp + " / AI+地面 " + _killsAi), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "FTK  " + _killsFriendly + "  (friendly kills, no XP)",
                "误伤  " + _killsFriendly + " · 友军击杀无经验"), _ftkStyle);
            GUILayout.Label(ModUiLang.T(
                "Support unlocks (this match)  Carrier=" + Yn(KillAccolades.HasUnlock(KillAccolades.UnlockCarrier))
                    + "  Advanced=" + Yn(KillAccolades.HasUnlock(KillAccolades.UnlockAdvanced))
                    + "  Strategic=" + Yn(KillAccolades.HasUnlock(KillAccolades.UnlockStrategic)),
                "支援解锁·当局  航母=" + Yn(KillAccolades.HasUnlock(KillAccolades.UnlockCarrier))
                    + "  高级=" + Yn(KillAccolades.HasUnlock(KillAccolades.UnlockAdvanced))
                    + "  战略=" + Yn(KillAccolades.HasUnlock(KillAccolades.UnlockStrategic))), _bodyStyle);

            GUILayout.Space(12f);
            CareerPremiumService.DrawSection(_titleStyle, _bodyStyle, _btnStyle, _warnStyle);

            GUILayout.Space(12f);
            GUILayout.Label(ModUiLang.T("Language", "语言"), _titleStyle);
            ModUiLang.DrawToggleRow();

            GUILayout.Space(12f);
            DrawPerformanceSection();

            GUILayout.Space(12f);
            GUILayout.Label(ModUiLang.T("Loadout", "挂载设置"), _titleStyle);
            DrawUnrestrictedMountToggle();

            GUILayout.Space(12f);
            DrawQolSection();

            GUILayout.Space(12f);
            DrawAiBrainSection();

            GUILayout.Space(12f);
            DrawDynamicMusicBetaSection();
            GUILayout.Space(12f);
            DrawFlightAnalysisSection();

            GUILayout.Space(12f);
            GUILayout.Label(ModUiLang.T("Unlocked badges", "已解锁徽章"), _titleStyle);
            GUILayout.BeginHorizontal();
            int shown = 0;
            for (int i = 0; i < Achievements.Length; i++)
            {
                AchievementDef a = Achievements[i];
                if (a == null || a.IsDone == null || !a.IsDone())
                    continue;
                GUILayout.Label(a.Badge + " " + a.LocTitle(), _badgeOn, GUILayout.Width(160f));
                shown++;
                if (shown % 3 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            if (shown == 0)
                GUILayout.Label(ModUiLang.T(
                    "(Get air kills to unlock badges)",
                    "取得空中击杀以解锁徽章"), _bodyStyle);
            GUILayout.EndHorizontal();
        }

        private static void DrawStatsTab()
        {
            GUILayout.Label(ModUiLang.T("Kill breakdown", "击杀细分"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "Player pilots (PvP):  " + _killsPvp,
                "玩家飞行员·PvP：  " + _killsPvp), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "AI / ground / ships:  " + _killsAi,
                "AI / 地面 / 舰船：  " + _killsAi), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "FTK:  " + _killsFriendly + "  (no XP)",
                "误伤：  " + _killsFriendly + " · 无经验"), _ftkStyle);
            GUILayout.Label(ModUiLang.T(
                "Missile kills:  " + _killsMissile + "     Guns / other:  " + _killsGun,
                "导弹击杀：  " + _killsMissile + "     航炮/其他：  " + _killsGun), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "Strong foe:  " + _strongKill + "     God Slayer:  " + _godSlayer
                    + "     Best streak:  " + _bestStreak,
                "强敌：  " + _strongKill + "     神挡杀神：  " + _godSlayer
                    + "     最高连杀：  " + _bestStreak), _bodyStyle);

            if (_activeSession != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label(ModUiLang.T("Current session (live)", "当前对局·实时"), _titleStyle);
                GUILayout.Label(_activeSession.Server + "  |  " + _activeSession.Mission
                    + (_activeSession.Multiplayer
                        ? ModUiLang.T("  [Online]", "  [联机]")
                        : ModUiLang.T("  [Solo]", "  [单机]")), _bodyStyle);
                GUILayout.Label(ModUiLang.T(
                    "This game  PvP " + _activeSession.PvpKills
                        + " / AI " + _activeSession.AiKills
                        + " / Deaths " + _activeSession.Deaths
                        + " / Streak " + _activeSession.BestStreak,
                    "本局  PvP " + _activeSession.PvpKills
                        + " / AI " + _activeSession.AiKills
                        + " / 阵亡 " + _activeSession.Deaths
                        + " / 连杀 " + _activeSession.BestStreak), _bodyStyle);
                GUILayout.Label(FtkLabel(_activeSession.FriendlyKills), _ftkStyle);
            }

            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T("Favorite Weapons", "常用武器"), _titleStyle);
            DrawTopDict(WeaponKills, 12);

            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T("Victims (air / ground / ship)", "击毁目标·空/地/舰"), _titleStyle);
            DrawTopDict(VictimAircraftKills, 12);

            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T("Your Airframes", "你驾驶的机体"), _titleStyle);
            DrawTopDict(PilotAircraftKills, 12);
        }

        private static void DrawServerPvpTab()
        {
            GUILayout.Label(ModUiLang.T("Server PvP records (by lobby name)", "服务器 PvP 记录·按大厅名"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "Saved on leave mission, return to menu, or quit mid-game.",
                "离开任务、回主菜单或中途退出时保存。"), _bodyStyle);
            GUILayout.Space(8f);
            if (ServerPvp.Count == 0)
            {
                GUILayout.Label(ModUiLang.T(
                    "(No server records yet - play online and return here.)",
                    "尚无服务器记录——联机游玩后回到此处"), _bodyStyle);
                return;
            }
            List<ServerPvpRecord> list = new List<ServerPvpRecord>(ServerPvp.Values);
            list.Sort(delegate(ServerPvpRecord a, ServerPvpRecord b)
            {
                int c = b.PvpKills.CompareTo(a.PvpKills);
                if (c != 0)
                    return c;
                return b.LastTicks.CompareTo(a.LastTicks);
            });
            for (int i = 0; i < list.Count; i++)
            {
                ServerPvpRecord r = list[i];
                if (r == null)
                    continue;
                float kd = r.Deaths > 0 ? (r.PvpKills / (float)r.Deaths) : r.PvpKills;
                GUILayout.Label((i + 1) + ".  " + r.Server, _badgeOn);
                GUILayout.Label(ModUiLang.T(
                    "      PvP " + r.PvpKills
                        + "   Deaths " + r.Deaths
                        + "   K/D " + kd.ToString("0.00")
                        + "   Games " + r.Games
                        + "   AI " + r.AiKills,
                    "      PvP " + r.PvpKills
                        + "   阵亡 " + r.Deaths
                        + "   击杀比 " + kd.ToString("0.00")
                        + "   场次 " + r.Games
                        + "   AI " + r.AiKills), _bodyStyle);
                GUILayout.Label("      " + FtkLabel(r.FriendlyKills), _ftkStyle);
                GUILayout.Space(4f);
            }
        }

        private static void DrawRecentGamesTab()
        {
            GUILayout.Label(
                ModUiLang.IsChinese
                    ? ("最近 " + MaxRecentGames + " 场对局")
                    : ("Last " + MaxRecentGames + " games"),
                _titleStyle);
            GUILayout.Label(
                ModUiLang.IsChinese
                    ? "离开任务 / 回主菜单 / 中途退出时结算。击杀即时保存。飞行评分：F1→飞行评分，或坠毁/离机自动打开；亦可点「分析」。"
                    : "Finalized on leave mission, main menu, or quit mid-game. Kills autosave. Flight score: F1 → Flight Score (auto on crash/leave), or Analysis.",
                _bodyStyle);
            GUILayout.Space(8f);
            if (RecentGames.Count == 0)
            {
                GUILayout.Label(
                    ModUiLang.IsChinese ? "（尚无对局记录）" : "(No games recorded yet)",
                    _bodyStyle);
                return;
            }
            for (int i = 0; i < RecentGames.Count; i++)
            {
                GameSessionRecord g = RecentGames[i];
                if (g == null)
                    continue;
                string when = "?";
                try
                {
                    when = new DateTime(g.StartTicks, DateTimeKind.Utc).ToLocalTime()
                        .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
                }
                catch { }
                string dur = FormatPlaytime(g.DurationSec);
                string mp = g.Multiplayer
                    ? ModUiLang.T("Online", "联机")
                    : ModUiLang.T("Solo", "单机");
                GUILayout.Label((i + 1) + ".  [" + when + "]  " + g.Server + "  |  " + mp, _badgeOn);
                GUILayout.Label(ModUiLang.T(
                    "      Mission: " + g.Mission + "   Time " + dur,
                    "      任务：" + g.Mission + "   时长 " + dur), _bodyStyle);
                GUILayout.Label(ModUiLang.T(
                    "      PvP " + g.PvpKills
                        + " / AI " + g.AiKills
                        + " / Deaths " + g.Deaths
                        + " / Best streak " + g.BestStreak,
                    "      PvP " + g.PvpKills
                        + " / AI " + g.AiKills
                        + " / 阵亡 " + g.Deaths
                        + " / 最高连杀 " + g.BestStreak), _bodyStyle);
                GUILayout.Label("      " + FtkLabel(g.FriendlyKills), _ftkStyle);

                bool hasAnalysis = false;
                try { hasAnalysis = FlightAnalysis.HasAnalysis(g.StartTicks); }
                catch { }
                GUILayout.BeginHorizontal();
                GUILayout.Space(18f);
                if (hasAnalysis)
                {
                    string anLabel = ModUiLang.IsChinese ? "分析 Analysis" : "Analysis";
                    if (GUILayout.Button(anLabel, _btnStyle, GUILayout.Width(120f), GUILayout.Height(26f)))
                        FlightAnalysis.TryShowAnalysis(g.StartTicks);
                    if (i == 0 && _lastFlightXpMul > 1.001f)
                    {
                        GUILayout.Label("  " + FlightAnalysis.FormatXpMulLabel(_lastFlightXpMul, ModUiLang.IsChinese),
                            _bodyStyle);
                    }
                }
                else
                {
                    GUILayout.Label(
                        ModUiLang.IsChinese ? "（无飞行分析）" : "(no flight analysis)",
                        _bodyStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }
        }

        private static void DrawAchievementsTab()
        {
            GUILayout.Label(ModUiLang.T(
                "Career badges (cosmetic). Vanilla liveries need an official skin API.",
                "生涯徽章·展示用。原版涂装需官方皮肤接口。"), _bodyStyle);
            GUILayout.Space(6f);
            for (int i = 0; i < Achievements.Length; i++)
            {
                AchievementDef a = Achievements[i];
                bool done = a.IsDone != null && a.IsDone();
                GUIStyle st = done ? _badgeOn : _badgeOff;
                string mark = done
                    ? ModUiLang.T("[DONE] ", "[完成] ")
                    : "[    ] ";
                GUILayout.Label(mark + a.Badge + "  " + a.LocTitle(), st);
                GUILayout.Label("      " + a.LocDesc(), _bodyStyle);
                GUILayout.Space(4f);
            }
        }

        private static void DrawNotesTab()
        {
            GUILayout.Label(ModUiLang.T("Change log", "更新说明"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "Latest: Oritasy " + ResolveOritasyDisplayRelease(),
                "最新：Oritasy " + ResolveOritasyDisplayRelease()), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "Dynamic music BETA: Profile → Experimental (off by default).",
                "动态音乐 BETA：档案 → 实验功能·默认关闭。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "Aircraft, Vehicle, Buildings in this mod were modified and fully customizable.",
                "本模组中的飞机、载具、建筑均已修改，且可完全自定义。"), _warnStyle);
            GUILayout.Label(ModUiLang.T(
                "Because of GUID difference from original version of the game, the skin from workshop may not work.",
                "由于与原版 GUID 不同，创意工坊皮肤可能无法使用。"), _warnStyle);
            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T("Community feature notes", "功能说明"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "1) Level / prestige 1-1000: cosmetic only, does not affect vanilla progress.",
                "1）等级 / 威望 1–1000：仅展示，不影响原版进度。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "2) XP: hit +" + CareerXpMathService.XpPerHit
                    + " (cap " + CareerXpMathService.HitXpCapPerAircraft + "/aircraft), module +"
                    + CareerXpMathService.XpPerModule
                    + ", missile +" + CareerXpMathService.XpPerMissile
                    + ", ground +" + CareerXpMathService.XpPerGround
                    + ", navy +" + CareerXpMathService.XpPerNavy
                    + ", carrier +" + CareerXpMathService.XpPerCarrier
                    + ". Flight time +" + FlightXpCap
                    + " max per match (full at 3h).",
                "2）经验：命中 +" + CareerXpMathService.XpPerHit
                    + "（每机上限 " + CareerXpMathService.HitXpCapPerAircraft + "），摧毁模块 +"
                    + CareerXpMathService.XpPerModule
                    + "，导弹 +" + CareerXpMathService.XpPerMissile
                    + "，地面 +" + CareerXpMathService.XpPerGround
                    + "，海军 +" + CareerXpMathService.XpPerNavy
                    + "，航母 +" + CareerXpMathService.XpPerCarrier
                    + "。飞行时间每局最多 +" + FlightXpCap
                    + "·满额需3小时。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "CDK input is on the Profile tab.",
                "档案页有 CDK 输入口。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "3) Level cost: at least " + XpPerLevelMin
                    + " XP, increases each level (Lv1→2 = " + XpPerLevelMin
                    + ", Lv2→3 = " + (XpPerLevelMin * 2) + ", …).",
                "3）升级消耗：至少 " + XpPerLevelMin
                    + " 经验，逐级递增（1→2 = " + XpPerLevelMin
                    + "，2→3 = " + (XpPerLevelMin * 2) + "，…）。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "4) Stats: PvP / AI / red FTK (friendly). FTK never grants XP.",
                "4）统计：PvP / AI / 红色误伤·友军。误伤永不给经验。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "6) Missile Editor is a standalone program (MissileEditor.exe + MissileEditor.dll), not this Profile panel.",
                "6）导弹编辑器是独立程序（MissileEditor.exe + MissileEditor.dll），不在本档案面板内。"), _bodyStyle);
            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T("Controls", "操控说明"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "Top-right 'Oritasy Profile' button or F8 toggles this panel.",
                "右上角「Oritasy 档案」按钮或 F8 开关此面板。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "Kills save immediately; sessions finalize on leave/menu/quit.",
                "击杀即时保存；离开/回菜单/退出时结算本局。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "Flight analysis / score: plugins/OritasyReplays/; open Analysis from Last 15.",
                "飞行分析/评分：plugins/OritasyReplays/；在「最近15局」打开分析。"), _bodyStyle);
            GUILayout.Label(ModUiLang.T(
                "Flight-score XP: score 0→×1.0, 100→×3.8 (linear); multiplies that match's kill+flight XP only.",
                "飞行评分 XP：0→×1.0，100→×3.8·线性；仅乘算该局击杀+飞行 XP。"), _bodyStyle);
            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T("Language", "语言"), _titleStyle);
            ModUiLang.DrawToggleRow();
            GUILayout.Space(10f);
            GUILayout.Label(ModUiLang.T("Loadout", "挂载设置"), _titleStyle);
            DrawUnrestrictedMountToggle();
        }

        private static void DrawPerformanceSection()
        {
            // Oritasy.PerfMode — reflection keeps WeXon-only builds compiling.
            try
            {
                System.Type t = System.Type.GetType("Oritasy.PerfMode, Oritasy")
                    ?? System.Type.GetType("Oritasy.PerfMode");
                if (t == null)
                {
                    GUILayout.Label(ModUiLang.IsChinese ? "性能" : "Performance", _titleStyle);
                    GUILayout.Label(ModUiLang.IsChinese
                        ? "需要 Oritasy / OritasyAir 包。"
                        : "Requires Oritasy / OritasyAir pack.", _bodyStyle);
                    return;
                }
                MethodInfo draw = t.GetMethod("DrawProfileSection",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (draw != null)
                {
                    draw.Invoke(null, null);
                    return;
                }
            }
            catch { }

            GUILayout.Label(ModUiLang.IsChinese ? "性能" : "Performance", _titleStyle);
            GUILayout.Label(ModUiLang.IsChinese
                ? "性能控制不可用。"
                : "Performance controls unavailable.", _bodyStyle);
        }

        private static void DrawAiBrainSection()
        {
            try
            {
                System.Type t = System.Type.GetType("Oritasy.AiCombatBrain, Oritasy")
                    ?? System.Type.GetType("Oritasy.AiCombatBrain");
                if (t == null)
                {
                    GUILayout.Label(ModUiLang.T("Enhanced AI", "增强 AI"), _titleStyle);
                    GUILayout.Label(ModUiLang.T(
                        "Requires Oritasy / OritasyAir pack.",
                        "需要 Oritasy / OritasyAir 包。"), _bodyStyle);
                    return;
                }
                MethodInfo draw = t.GetMethod("DrawProfileSection",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (draw != null)
                {
                    draw.Invoke(null, null);
                    return;
                }
            }
            catch { }

            GUILayout.Label(ModUiLang.T("Enhanced AI", "增强 AI"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "Enhanced AI controls unavailable.",
                "增强 AI 控制不可用。"), _bodyStyle);
        }

        private static void DrawDynamicMusicBetaSection()
        {
            // Oritasy.DynamicMusic lives in the aircraft / merged pack — reflection keeps WeXon-only builds compiling.
            try
            {
                System.Type t = System.Type.GetType("Oritasy.DynamicMusic, Oritasy")
                    ?? System.Type.GetType("Oritasy.DynamicMusic");
                if (t == null)
                    return;
                MethodInfo draw = t.GetMethod("DrawProfileBetaToggle",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (draw != null)
                {
                    draw.Invoke(null, null);
                    return;
                }
            }
            catch { }

            GUILayout.Label(ModUiLang.T("Experimental (BETA)", "实验功能·BETA"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "Dynamic music requires Oritasy / OritasyAir pack.",
                "动态音乐需要 Oritasy / OritasyAir 包。"), _bodyStyle);
        }

        private static void DrawQolSection()
        {
            try
            {
                System.Type t = System.Type.GetType("Oritasy.EngineStartService, Oritasy")
                    ?? System.Type.GetType("Oritasy.EngineStartService");
                if (t != null)
                {
                    MethodInfo draw = t.GetMethod("DrawProfileToggle",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (draw != null)
                        draw.Invoke(null, null);
                }
            }
            catch { }
            try
            {
                System.Type wear = System.Type.GetType("Oritasy.AirframeWearGui, Oritasy")
                    ?? System.Type.GetType("Oritasy.AirframeWearGui");
                if (wear != null)
                {
                    MethodInfo wearDraw = wear.GetMethod("DrawProfileToggle",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (wearDraw != null)
                        wearDraw.Invoke(null, null);
                }
            }
            catch { }
        }

        private static void DrawFlightAnalysisSection()
        {
            FlightAnalysis.DrawProfileToggle();
        }

        private static string ResolveOritasyDisplayRelease()
        {
            try
            {
                Type t = Type.GetType("Oritasy.PluginInfo, Oritasy")
                    ?? Type.GetType("Oritasy.PluginInfo");
                if (t != null)
                {
                    FieldInfo f = t.GetField("DisplayRelease",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null)
                    {
                        string s = f.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(s))
                            return s;
                    }
                }
            }
            catch { }
            return ModUiLang.EnglishOnlyEdition ? "0.0.9.193D Special Edition" : "0.0.9.193C";
        }

        private static void DrawUnrestrictedMountToggle()
        {
            bool on = false;
            try
            {
                if (Plugin.EnableUnrestricted != null)
                    on = Plugin.EnableUnrestricted.Value;
            }
            catch { }

            GUILayout.BeginHorizontal();
            GUILayout.Label(ModUiLang.T("Unrestricted mounts", "无限制挂载"), GUILayout.Width(140f));
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? ModUiLang.T("ON", "开") : ModUiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                SetUnrestrictedMounts(!on);
                on = !on;
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? ModUiLang.T("  [ON]", "  [开]") : ModUiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? ModUiLang.T(
                        "Player may mount any weapon on any hardpoint (local player only; AI unchanged).",
                        "玩家可任意挂载武器·仅本地玩家不影响AI。")
                    : ModUiLang.T(
                        "Vanilla hardpoint restrictions apply.",
                        "使用原版挂载限制。"),
                _bodyStyle);
        }

        private static void SetUnrestrictedMounts(bool enabled)
        {
            try
            {
                if (Plugin.EnableUnrestricted != null)
                    Plugin.EnableUnrestricted.Value = enabled;
            }
            catch { }

            // Merged Oritasy.dll also has Oritasy.Plugin.UnrestrictedWeapons — keep in sync.
            try
            {
                System.Type t = System.Type.GetType("Oritasy.Plugin, Oritasy")
                    ?? System.Type.GetType("Oritasy.Plugin");
                if (t != null)
                {
                    System.Reflection.FieldInfo f = t.GetField("UnrestrictedWeapons",
                        System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public);
                    if (f != null)
                    {
                        object entry = f.GetValue(null);
                        if (entry != null)
                        {
                            System.Reflection.PropertyInfo val = entry.GetType().GetProperty("Value");
                            if (val != null)
                                val.SetValue(entry, enabled, null);
                        }
                    }
                }
            }
            catch { }
        }

        private static void DrawTopDict(Dictionary<string, int> dict, int max)
        {
            if (dict == null || dict.Count == 0)
            {
                GUILayout.Label(ModUiLang.T("(No data yet)", "暂无数据"), _bodyStyle);
                return;
            }
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(dict);
            list.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            int n = Mathf.Min(max, list.Count);
            for (int i = 0; i < n; i++)
                GUILayout.Label("  " + (i + 1) + ".  " + list[i].Key + "   x" + list[i].Value, _bodyStyle);
        }

        private static string FormatPlaytime(float sec)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(sec));
            int h = s / 3600;
            int m = (s % 3600) / 60;
            int r = s % 60;
            if (ModUiLang.IsChinese)
            {
                if (h > 0)
                    return h + "小时 " + m + "分";
                return m + "分 " + r + "秒";
            }
            if (h > 0)
                return h + "h " + m + "m";
            return m + "m " + r + "s";
        }

        private static string FtkLabel(int count)
        {
            return ModUiLang.T("FTK " + count, "误伤 " + count);
        }

        private static string Yn(bool v)
        {
            return v ? ModUiLang.T("Yes", "是") : ModUiLang.T("No", "否");
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            // Match F2 autopilot menu typography / colors
            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 18;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.normal.textColor = new Color(0.75f, 1f, 0.9f, 1f);

            _bodyStyle = new GUIStyle(GUI.skin.label);
            _bodyStyle.fontSize = 13;
            _bodyStyle.wordWrap = true;
            _bodyStyle.alignment = TextAnchor.MiddleLeft;
            _bodyStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f, 0.95f);

            _warnStyle = new GUIStyle(GUI.skin.label);
            _warnStyle.fontSize = 13;
            _warnStyle.wordWrap = true;
            _warnStyle.alignment = TextAnchor.MiddleLeft;
            _warnStyle.normal.textColor = new Color(1f, 0.85f, 0.3f, 1f);

            _chipStyle = new GUIStyle(GUI.skin.label);
            _chipStyle.fontSize = 12;
            _chipStyle.fontStyle = FontStyle.Bold;
            _chipStyle.alignment = TextAnchor.MiddleRight;
            _chipStyle.normal.textColor = new Color(0.8f, 0.95f, 1f, 0.98f);

            _ftkStyle = new GUIStyle(GUI.skin.label);
            _ftkStyle.fontSize = 13;
            _ftkStyle.fontStyle = FontStyle.Bold;
            _ftkStyle.wordWrap = true;
            _ftkStyle.normal.textColor = new Color(1f, 0.35f, 0.3f, 1f);
            _ftkStyle.alignment = TextAnchor.MiddleLeft;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 12;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.textColor = Color.white;

            _tabOn = _btnStyle;
            _tabOff = _btnStyle;

            _badgeOn = new GUIStyle(GUI.skin.label);
            _badgeOn.fontSize = 13;
            _badgeOn.fontStyle = FontStyle.Bold;
            _badgeOn.normal.textColor = new Color(1f, 0.9f, 0.45f, 1f);

            _badgeOff = new GUIStyle(GUI.skin.label);
            _badgeOff.fontSize = 13;
            _badgeOff.normal.textColor = new Color(0.55f, 0.6f, 0.62f, 1f);
        }

        private static void Load()
        {
            _loaded = true;
            _xp = PlayerPrefs.GetInt(Pref + "xp", 0);
            _prestige = PlayerPrefs.GetInt(Pref + "prestige", 0);
            _playSeconds = PlayerPrefs.GetFloat(Pref + "play", 0f);
            _killsPvp = PlayerPrefs.GetInt(Pref + "pvp", 0);
            _killsAi = PlayerPrefs.GetInt(Pref + "ai", 0);
            _killsFriendly = PlayerPrefs.GetInt(Pref + "ff", 0);
            _killsMissile = PlayerPrefs.GetInt(Pref + "mis", 0);
            _killsGun = PlayerPrefs.GetInt(Pref + "gun", 0);
            _godSlayer = PlayerPrefs.GetInt(Pref + "god", 0);
            _strongKill = PlayerPrefs.GetInt(Pref + "strong", 0);
            _bestStreak = PlayerPrefs.GetInt(Pref + "streak", 0);
            LoadDict(WeaponKills, Pref + "wpn");
            LoadDict(VictimAircraftKills, Pref + "vic");
            LoadDict(PilotAircraftKills, Pref + "plt");
            LoadServerPvp(Pref + "srv");
            LoadRecentGames(Pref + "games15");
            if (TryRecoverDraft())
            {
                // Persist recovered Last-15 / server merge after crash or hard quit
                SaveRecentGames(Pref + "games15");
                SaveServerPvp(Pref + "srv");
                PlayerPrefs.DeleteKey(Pref + "draft");
                PlayerPrefs.Save();
            }
        }

        private static void MarkDirty()
        {
            _dirty = true;
            // Don't postpone forever if many kills arrive — schedule a soft flush window.
            if (_nextSave <= 0f || _nextSave > Time.unscaledTime + 45f)
                _nextSave = Time.unscaledTime + 45f;
        }

        private static void Save(bool flushDisk)
        {
            PlayerPrefs.SetInt(Pref + "xp", _xp);
            PlayerPrefs.SetInt(Pref + "prestige", _prestige);
            PlayerPrefs.SetFloat(Pref + "play", _playSeconds + _playAccum);
            PlayerPrefs.SetInt(Pref + "pvp", _killsPvp);
            PlayerPrefs.SetInt(Pref + "ai", _killsAi);
            PlayerPrefs.SetInt(Pref + "ff", _killsFriendly);
            PlayerPrefs.SetInt(Pref + "mis", _killsMissile);
            PlayerPrefs.SetInt(Pref + "gun", _killsGun);
            PlayerPrefs.SetInt(Pref + "god", _godSlayer);
            PlayerPrefs.SetInt(Pref + "strong", _strongKill);
            PlayerPrefs.SetInt(Pref + "streak", _bestStreak);
            SaveDict(WeaponKills, Pref + "wpn");
            SaveDict(VictimAircraftKills, Pref + "vic");
            SaveDict(PilotAircraftKills, Pref + "plt");
            SaveServerPvp(Pref + "srv");
            SaveRecentGames(Pref + "games15");
            // Live session draft: survives Alt+F4 if OnApplicationQuit is skipped
            if (_activeSession != null)
                PlayerPrefs.SetString(Pref + "draft", EncodeGameSession(_activeSession));
            else
                PlayerPrefs.DeleteKey(Pref + "draft");
            _dirty = false;
            if (flushDisk)
                PlayerPrefs.Save();
        }

        private static string EncodeGameSession(GameSessionRecord g)
        {
            if (g == null)
                return string.Empty;
            StringBuilder sb = new StringBuilder();
            sb.Append(g.StartTicks);
            sb.Append(';');
            sb.Append(SanitizeKey(g.Server));
            sb.Append(';');
            sb.Append(SanitizeKey(g.Mission));
            sb.Append(';');
            sb.Append(g.PvpKills);
            sb.Append(';');
            sb.Append(g.AiKills);
            sb.Append(';');
            sb.Append(g.Deaths);
            sb.Append(';');
            sb.Append(g.BestStreak);
            sb.Append(';');
            sb.Append(g.DurationSec.ToString("0.#", CultureInfo.InvariantCulture));
            sb.Append(';');
            sb.Append(g.Multiplayer ? "1" : "0");
            sb.Append(';');
            sb.Append(g.FriendlyKills);
            return sb.ToString();
        }

        private static GameSessionRecord DecodeGameSession(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            string[] f = raw.Split(';');
            if (f.Length < 8)
                return null;
            GameSessionRecord g = new GameSessionRecord();
            long.TryParse(f[0], out g.StartTicks);
            g.Server = f[1];
            g.Mission = f[2];
            int.TryParse(f[3], out g.PvpKills);
            int.TryParse(f[4], out g.AiKills);
            int.TryParse(f[5], out g.Deaths);
            int.TryParse(f[6], out g.BestStreak);
            float.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out g.DurationSec);
            g.Multiplayer = f.Length > 8 && f[8] == "1";
            if (f.Length > 9)
                int.TryParse(f[9], out g.FriendlyKills);
            return g;
        }

        private static bool TryRecoverDraft()
        {
            string raw = PlayerPrefs.GetString(Pref + "draft", string.Empty);
            if (string.IsNullOrEmpty(raw))
                return false;
            GameSessionRecord g = DecodeGameSession(raw);
            if (g == null)
            {
                PlayerPrefs.DeleteKey(Pref + "draft");
                return false;
            }
            // Skip empty flicker drafts
            if (g.DurationSec < 5f && g.PvpKills == 0 && g.AiKills == 0
                && g.FriendlyKills == 0 && g.Deaths == 0)
            {
                PlayerPrefs.DeleteKey(Pref + "draft");
                return false;
            }
            for (int i = 0; i < RecentGames.Count; i++)
            {
                if (RecentGames[i] != null && RecentGames[i].StartTicks == g.StartTicks)
                {
                    PlayerPrefs.DeleteKey(Pref + "draft");
                    return false;
                }
            }
            RecentGames.Insert(0, g);
            while (RecentGames.Count > MaxRecentGames)
                RecentGames.RemoveAt(RecentGames.Count - 1);
            if (g.Multiplayer || g.PvpKills > 0 || g.FriendlyKills > 0)
                MergeServerRecord(g);
            return true;
        }

        private static void SaveServerPvp(string key)
        {
            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (KeyValuePair<string, ServerPvpRecord> kv in ServerPvp)
            {
                ServerPvpRecord r = kv.Value;
                if (r == null || string.IsNullOrEmpty(r.Server))
                    continue;
                if (!first)
                    sb.Append('|');
                first = false;
                sb.Append(SanitizeKey(r.Server));
                sb.Append('=');
                sb.Append(r.PvpKills);
                sb.Append(',');
                sb.Append(r.Deaths);
                sb.Append(',');
                sb.Append(r.Games);
                sb.Append(',');
                sb.Append(r.AiKills);
                sb.Append(',');
                sb.Append(r.LastTicks);
                sb.Append(',');
                sb.Append(r.FriendlyKills);
            }
            PlayerPrefs.SetString(key, sb.ToString());
        }

        private static void LoadServerPvp(string key)
        {
            ServerPvp.Clear();
            string raw = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return;
            string[] parts = raw.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p))
                    continue;
                int eq = p.IndexOf('=');
                if (eq <= 0)
                    continue;
                string name = p.Substring(0, eq);
                string[] nums = p.Substring(eq + 1).Split(',');
                if (nums.Length < 3)
                    continue;
                ServerPvpRecord r = new ServerPvpRecord();
                r.Server = name;
                int.TryParse(nums[0], out r.PvpKills);
                int.TryParse(nums[1], out r.Deaths);
                int.TryParse(nums[2], out r.Games);
                if (nums.Length > 3)
                    int.TryParse(nums[3], out r.AiKills);
                if (nums.Length > 4)
                    long.TryParse(nums[4], out r.LastTicks);
                if (nums.Length > 5)
                    int.TryParse(nums[5], out r.FriendlyKills);
                ServerPvp[name] = r;
            }
        }

        private static void SaveRecentGames(string key)
        {
            StringBuilder sb = new StringBuilder();
            int n = Mathf.Min(MaxRecentGames, RecentGames.Count);
            for (int i = 0; i < n; i++)
            {
                GameSessionRecord g = RecentGames[i];
                if (g == null)
                    continue;
                if (sb.Length > 0)
                    sb.Append('|');
                sb.Append(EncodeGameSession(g));
            }
            PlayerPrefs.SetString(key, sb.ToString());
        }

        private static void LoadRecentGames(string key)
        {
            RecentGames.Clear();
            string raw = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return;
            string[] parts = raw.Split('|');
            for (int i = 0; i < parts.Length && RecentGames.Count < MaxRecentGames; i++)
            {
                GameSessionRecord g = DecodeGameSession(parts[i]);
                if (g != null)
                    RecentGames.Add(g);
            }
        }

        private static void SaveDict(Dictionary<string, int> dict, string key)
        {
            StringBuilder sb = new StringBuilder();
            if (dict != null)
            {
                bool first = true;
                foreach (KeyValuePair<string, int> kv in dict)
                {
                    if (string.IsNullOrEmpty(kv.Key))
                        continue;
                    if (!first)
                        sb.Append('|');
                    first = false;
                    sb.Append(kv.Key.Replace('|', '/').Replace('=', '-'));
                    sb.Append('=');
                    sb.Append(kv.Value);
                }
            }
            PlayerPrefs.SetString(key, sb.ToString());
        }

        private static void LoadDict(Dictionary<string, int> dict, string key)
        {
            dict.Clear();
            string raw = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return;
            string[] parts = raw.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p))
                    continue;
                int eq = p.IndexOf('=');
                if (eq <= 0)
                    continue;
                string k = p.Substring(0, eq);
                int v;
                if (!int.TryParse(p.Substring(eq + 1), out v))
                    continue;
                dict[k] = v;
            }
        }
    }
}
