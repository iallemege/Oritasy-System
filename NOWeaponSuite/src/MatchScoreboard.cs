using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using NuclearOption.Networking;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// In-mission Tab board: both factions' air K/D/R plus colored achievement initials.
    /// </summary>
    internal static class MatchScoreboard
    {
        private static float StreakWindow { get { return KillCombatMathService.StreakWindow; } }
        private static float DedupSeconds { get { return KillCombatMathService.DedupSeconds; } }

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _key;
        private static ConfigEntry<bool> _holdToShow;

        private static bool _toggleOpen;
        private static Vector2 _scrollLeft;
        private static Vector2 _scrollRight;
        private static float _nextUiRefresh;
        private static readonly List<FactionBlock> UiBlocks = new List<FactionBlock>(4);
        private static FactionHQ _localHq;

        private static readonly Dictionary<int, PlayerRow> Rows =
            new Dictionary<int, PlayerRow>();
        private static readonly Dictionary<int, float> RecentVictims =
            new Dictionary<int, float>();

        private static GUIStyle _titleStyle;
        private static GUIStyle _headerStyle;
        private static GUIStyle _rowStyle;
        private static GUIStyle _localRowStyle;
        private static GUIStyle _legendStyle;
        private static GUIStyle _badgeStyle;

        private sealed class PlayerRow
        {
            public int Id;
            public string Name;
            public int Kills;
            public int Deaths;
            public int Ftk;
            public int Streak;
            public float StreakUntil;
            public readonly List<string> Badges = new List<string>(8);
            public readonly HashSet<string> BadgeSet =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class FactionBlock
        {
            public string Title;
            public bool Friendly;
            public readonly List<PlayerRow> Players = new List<PlayerRow>(16);
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("MatchScoreboard", "Enabled", true,
                "Hold/toggle Tab in-mission for faction KDR + achievement board.");
            _key = config.Bind("MatchScoreboard", "ToggleKey", KeyCode.Tab,
                "Key to open the match scoreboard.");
            _holdToShow = config.Bind("MatchScoreboard", "HoldToShow", true,
                "True = hold key to show. False = press to toggle.");
        }

        internal static void ClearSession()
        {
            Rows.Clear();
            RecentVictims.Clear();
            UiBlocks.Clear();
            _toggleOpen = false;
            _localHq = null;
        }

        internal static void Tick()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            PruneDedup();

            if (!IsInMission())
            {
                _toggleOpen = false;
                return;
            }

            KeyCode key = _key != null ? _key.Value : KeyCode.Tab;
            bool hold = _holdToShow == null || _holdToShow.Value;
            ScoreboardUiGateService.TickToggle t = ScoreboardUiGateService.ResolveTickToggle(
                true, true, hold, Input.GetKeyDown(key), Input.GetKeyDown(KeyCode.Escape), _toggleOpen);
            if (t == ScoreboardUiGateService.TickToggle.FlipToggle)
                _toggleOpen = !_toggleOpen;
            else if (t == ScoreboardUiGateService.TickToggle.CloseEscape)
                _toggleOpen = false;
        }

        internal static bool IsVisible()
        {
            KeyCode key = _key != null ? _key.Value : KeyCode.Tab;
            ScoreboardUiGateService.Visibility v = ScoreboardUiGateService.ResolveVisibility(
                _enabled != null && _enabled.Value,
                IsInMission(),
                _holdToShow == null || _holdToShow.Value,
                Input.GetKey(key),
                _toggleOpen);
            return ScoreboardUiGateService.IsVisible(v);
        }

        internal static void DrawGui()
        {
            if (!IsVisible())
                return;

            Event e = Event.current;
            if (e != null
                && e.type != EventType.Repaint
                && e.type != EventType.Layout
                && e.type != EventType.ScrollWheel
                && e.type != EventType.MouseDrag
                && e.type != EventType.MouseDown
                && e.type != EventType.MouseUp)
                return;

            EnsureStyles();
            if (Time.unscaledTime >= _nextUiRefresh)
            {
                _nextUiRefresh = Time.unscaledTime + 0.4f;
                RebuildUiBlocks();
            }

            float w = Mathf.Min(1100f, GuiScale.Width * 0.92f);
            float h = Mathf.Min(640f, GuiScale.Height * 0.78f);
            Rect win = new Rect((GuiScale.Width - w) * 0.5f, (GuiScale.Height - h) * 0.12f, w, h);

            Color prev = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.92f);
            GUI.DrawTexture(win, Texture2D.whiteTexture);
            GUI.color = new Color(0.4f, 0.9f, 0.55f, 0.95f);
            GUI.DrawTexture(new Rect(win.x, win.y, win.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(win.x + 14f, win.y + 10f, win.width - 28f, win.height - 20f));
            GUILayout.Label(ModUiLang.T(
                "MATCH BOARD  ·  K / D / R  ·  Achievements",
                "对局计分板  ·  击杀/阵亡/比  ·  成就"), _titleStyle);
            GUILayout.Label(ModUiLang.T(
                "Hold Tab (default). Badges = in-match accolades (initials).",
                "按住 Tab。徽章 = 当局击杀成就缩写。"), _legendStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            float colW = (win.width - 40f) * 0.5f;
            DrawFactionColumn(0, colW);
            GUILayout.Space(8f);
            DrawFactionColumn(1, colW);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            DrawLegend();
            GUILayout.EndArea();
            GUI.color = prev;
        }

        /// <summary>Any attributed air kill (all players). Deduped.</summary>
        internal static void NoteAirKill(Player killer, Aircraft victim)
        {
            NoteKill(killer, victim);
        }

        /// <summary>Any attributed kill — aircraft / vehicle / ship / building. Deduped.</summary>
        internal static void NoteKill(Player killer, Unit victim)
        {
            int vid = 0;
            float now = Time.unscaledTime;
            float until = 0f;
            bool dedup = false;
            if (victim != null)
            {
                vid = victim.GetInstanceID();
                if (RecentVictims.TryGetValue(vid, out until))
                    dedup = ScoreboardUiGateService.IsDedupBlocked(now, until);
            }

            bool friendly = false;
            try
            {
                if (killer != null && victim != null)
                {
                    FactionHQ kh = killer.HQ;
                    FactionHQ vh = Plugin.GetHq(victim);
                    friendly = kh != null && vh != null && object.ReferenceEquals(kh, vh);
                }
            }
            catch { }

            ScoreboardUiGateService.KillPath path = ScoreboardUiGateService.ResolveKillPath(
                _enabled != null && _enabled.Value,
                killer == null,
                victim == null,
                KillAccolades.IsCountableVictim(victim),
                dedup,
                friendly);
            if (path == ScoreboardUiGateService.KillPath.Skip)
                return;

            RecentVictims[vid] = ScoreboardUiGateService.ScheduleDedupUntil(now, DedupSeconds);

            PlayerRow kr = GetOrCreate(killer);
            Aircraft victimAc = victim as Aircraft;
            Player victimPlayer = null;
            try
            {
                if (victimAc != null)
                    victimPlayer = victimAc.Player;
            }
            catch { }
            if (victimPlayer != null)
                GetOrCreate(victimPlayer).Deaths++;

            if (path == ScoreboardUiGateService.KillPath.Friendly)
            {
                kr.Ftk++;
                bool firstFtk = kr.BadgeSet.Add("FTK");
                if (firstFtk)
                    kr.Badges.Add("FTK");
                BroadcastFtkFeed(kr.Name, kr.Ftk);
                return;
            }

            kr.Kills++;
            kr.Streak = KillCombatMathService.AdvanceStreak(kr.Streak, now, ref kr.StreakUntil);

            float gap = 0f;
            if (victimAc != null)
            {
                try
                {
                    Aircraft kAc = killer.Aircraft;
                    float my = kAc != null ? kAc.skill : 0.35f;
                    gap = victimAc.skill - my;
                }
                catch { }
            }

            if (victimAc != null)
            {
                KillCombatMathService.SkillKillKind kind = KillCombatMathService.ClassifySkillGap(
                    true, gap, KillCombatMathService.DefaultStrongGap, KillCombatMathService.DefaultGodGap);
                string skillCode = KillCombatMathService.SkillGapBadgeCode(kind);
                if (skillCode != null)
                    EarnAchievement(kr, skillCode, true);
            }

            string streakCode = KillCombatMathService.StreakBadgeCode(kr.Streak);
            if (streakCode != null)
                EarnAchievement(kr, streakCode, true);

            string missionCode = KillCombatMathService.MissionKillBadgeCode(kr.Kills);
            if (missionCode != null)
                EarnAchievement(kr, missionCode, true);
        }

        /// <summary>Arsenal unlock initials (local) — gold feed once when first earned.</summary>
        internal static void NoteUnlockAchievement(Player player, string unlockKey)
        {
            if (_enabled == null || !_enabled.Value || player == null)
                return;
            string code = ScoreboardBadgeService.UnlockKeyToCode(unlockKey);
            if (code == null)
                return;
            EarnAchievement(GetOrCreate(player), code, true);
        }

        internal static void NoteFromUnits(Unit killerUnit, Unit killedUnit)
        {
            if (killedUnit == null || !KillAccolades.IsCountableVictim(killedUnit))
                return;
            Unit victim = killedUnit;
            Aircraft victimAc = victim as Aircraft;

            Player killerPlayer = null;
            try
            {
                Aircraft kAc = killerUnit as Aircraft;
                if (kAc != null)
                    killerPlayer = kAc.Player;
                else
                {
                    Missile m = killerUnit as Missile;
                    if (m != null && m.owner != null)
                    {
                        Aircraft oa = m.owner as Aircraft;
                        if (oa != null)
                            killerPlayer = oa.Player;
                    }
                }
            }
            catch { }

            if (killerPlayer != null)
                NoteKill(killerPlayer, victim);
            else if (victimAc != null)
            {
                // Still count death if victim was piloted
                try
                {
                    Player vp = victimAc.Player;
                    if (vp != null)
                    {
                        int vid = victim.GetInstanceID();
                        float now = Time.unscaledTime;
                        float until;
                        if (RecentVictims.TryGetValue(vid, out until) && now < until)
                            return;
                        RecentVictims[vid] = now + DedupSeconds;
                        GetOrCreate(vp).Deaths++;
                    }
                }
                catch { }
            }
        }

        private static PlayerRow GetOrCreate(Player p)
        {
            int id = p.GetInstanceID();
            PlayerRow row;
            if (!Rows.TryGetValue(id, out row) || row == null)
            {
                row = new PlayerRow();
                row.Id = id;
                Rows[id] = row;
            }
            try
            {
                string n = p.GetDisplayName(PlayerNameContext.ChatOrLeaderboard);
                if (!string.IsNullOrEmpty(n))
                    row.Name = n;
            }
            catch { }
            if (string.IsNullOrEmpty(row.Name))
                row.Name = "Player";
            return row;
        }

        private static void AddBadgeSilent(PlayerRow row, string code)
        {
            if (row == null || string.IsNullOrEmpty(code))
                return;
            if (!row.BadgeSet.Add(code))
                return;
            row.Badges.Add(code);
        }

        /// <param name="announce">Gold kill-feed when true and badge is new this match.</param>
        private static void EarnAchievement(PlayerRow row, string code, bool announce)
        {
            if (row == null || string.IsNullOrEmpty(code))
                return;
            bool isNew = row.BadgeSet.Add(code);
            if (isNew)
                row.Badges.Add(code);
            // One gold line per first completion this match (no spam on repeats)
            if (announce && isNew)
                BroadcastGoldFeed(row.Name, code);
        }

        private static void BroadcastGoldFeed(string playerName, string code)
        {
            if (string.IsNullOrEmpty(code))
                return;
            if (string.IsNullOrEmpty(playerName))
                playerName = "Player";
            try
            {
                GameplayUI ui = SceneSingleton<GameplayUI>.i;
                if (ui == null)
                    return;
                // Vanilla kill feed supports TMP rich-text colors (join messages use the same).
                string msg = "<color=#FFD700ff>★ "
                    + code + "  " + playerName + "  —  " + BadgeTitle(code)
                    + "</color>";
                ui.KillFeed(msg);
            }
            catch { }
        }

        private static void BroadcastFtkFeed(string playerName, int count)
        {
            if (string.IsNullOrEmpty(playerName))
                playerName = "Player";
            try
            {
                GameplayUI ui = SceneSingleton<GameplayUI>.i;
                if (ui == null)
                    return;
                string ftk = ModUiLang.IsChinese ? "友军击杀" : "Friendly Kill";
                string msg = "<color=#FF3030ff>★ FTK  " + playerName
                    + "  —  " + ftk + "  x" + count + "</color>";
                ui.KillFeed(msg);
            }
            catch { }
        }

        private static string BadgeTitle(string code)
        {
            return ScoreboardBadgeService.BadgeTitle(code, ModUiLang.IsChinese);
        }

        private static void PruneDedup()
        {
            if (RecentVictims.Count == 0)
                return;
            float now = Time.unscaledTime;
            List<int> dead = null;
            foreach (KeyValuePair<int, float> kv in RecentVictims)
            {
                if (now >= kv.Value)
                {
                    if (dead == null)
                        dead = new List<int>(8);
                    dead.Add(kv.Key);
                }
            }
            if (dead == null)
                return;
            for (int i = 0; i < dead.Count; i++)
                RecentVictims.Remove(dead[i]);
        }

        private static void RebuildUiBlocks()
        {
            UiBlocks.Clear();
            _localHq = null;
            try { GameManager.GetLocalHQ(out _localHq); }
            catch { }

            List<FactionHQ> hqs = new List<FactionHQ>(4);
            try
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq != null)
                        hqs.Add(hq);
                }
            }
            catch { }

            // Prefer local faction first, then others
            hqs.Sort(delegate(FactionHQ a, FactionHQ b)
            {
                bool af = object.ReferenceEquals(a, _localHq);
                bool bf = object.ReferenceEquals(b, _localHq);
                if (af == bf)
                    return 0;
                return af ? -1 : 1;
            });

            for (int i = 0; i < hqs.Count; i++)
            {
                FactionHQ hq = hqs[i];
                FactionBlock block = new FactionBlock();
                block.Friendly = object.ReferenceEquals(hq, _localHq);
                try
                {
                    if (hq.faction != null && !string.IsNullOrEmpty(hq.faction.factionExtendedName))
                        block.Title = hq.faction.factionExtendedName;
                    else
                        block.Title = block.Friendly ? "FRIENDLY" : "ENEMY";
                }
                catch
                {
                    block.Title = block.Friendly ? "FRIENDLY" : "ENEMY";
                }
                if (block.Friendly)
                    block.Title = block.Title + "  [YOU]";

                List<Player> players = null;
                try { players = hq.GetPlayers(true); }
                catch { }
                if (players != null)
                {
                    for (int p = 0; p < players.Count; p++)
                    {
                        Player pl = players[p];
                        if (pl == null)
                            continue;
                        block.Players.Add(GetOrCreate(pl));
                    }
                }

                block.Players.Sort(delegate(PlayerRow a, PlayerRow b)
                {
                    int c = b.Kills.CompareTo(a.Kills);
                    if (c != 0)
                        return c;
                    return a.Deaths.CompareTo(b.Deaths);
                });
                UiBlocks.Add(block);
            }

            // Solo / missing registry: still show tracked rows
            if (UiBlocks.Count == 0 && Rows.Count > 0)
            {
                FactionBlock block = new FactionBlock();
                block.Title = "PLAYERS";
                block.Friendly = true;
                foreach (KeyValuePair<int, PlayerRow> kv in Rows)
                    block.Players.Add(kv.Value);
                block.Players.Sort(delegate(PlayerRow a, PlayerRow b)
                {
                    return b.Kills.CompareTo(a.Kills);
                });
                UiBlocks.Add(block);
            }
        }

        private static void DrawFactionColumn(int index, float width)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            if (index >= UiBlocks.Count)
            {
                GUILayout.Label(index == 0 ? "Waiting for factions…" : "", _headerStyle);
                GUILayout.EndVertical();
                return;
            }

            FactionBlock block = UiBlocks[index];
            Color hc = block.Friendly
                ? new Color(0.45f, 1f, 0.65f, 1f)
                : new Color(1f, 0.45f, 0.4f, 1f);
            _headerStyle.normal.textColor = hc;
            GUILayout.Label(block.Title, _headerStyle);
            GUILayout.Label(ModUiLang.T(
                "NAME                  K   D   KDR   BADGES",
                "名称                  杀  亡  比    徽章"), _legendStyle);

            if (index == 0)
                _scrollLeft = GUILayout.BeginScrollView(_scrollLeft, false, true,
                    GUILayout.ExpandHeight(true));
            else
                _scrollRight = GUILayout.BeginScrollView(_scrollRight, false, true,
                    GUILayout.ExpandHeight(true));

            Player local = null;
            try
            {
                Player lp;
                if (GameManager.GetLocalPlayer(out lp))
                    local = lp;
            }
            catch { }
            int localId = local != null ? local.GetInstanceID() : 0;

            if (block.Players.Count == 0)
                GUILayout.Label("(no players)", _legendStyle);

            for (int i = 0; i < block.Players.Count; i++)
            {
                PlayerRow r = block.Players[i];
                if (r == null)
                    continue;
                float kdr = r.Deaths > 0 ? (r.Kills / (float)r.Deaths) : r.Kills;
                string line = (i + 1).ToString()
                    + ". " + PadName(r.Name, 16)
                    + "  " + r.Kills.ToString().PadLeft(2)
                    + "  " + r.Deaths.ToString().PadLeft(2)
                    + "  " + kdr.ToString("0.00").PadLeft(5);
                GUIStyle st = (r.Id == localId) ? _localRowStyle : _rowStyle;
                GUILayout.BeginHorizontal();
                GUILayout.Label(line, st, GUILayout.Width(width * 0.55f));
                DrawBadges(r);
                GUILayout.EndHorizontal();
                if (r.Ftk > 0)
                {
                    _badgeStyle.normal.textColor = BadgeColor("FTK");
                    GUILayout.Label("    FTK x" + r.Ftk, _badgeStyle);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static void DrawBadges(PlayerRow r)
        {
            if (r.Badges.Count == 0)
            {
                GUILayout.Label("-", _legendStyle);
                return;
            }
            for (int i = 0; i < r.Badges.Count; i++)
            {
                string code = r.Badges[i];
                _badgeStyle.normal.textColor = BadgeColor(code);
                GUILayout.Label(code, _badgeStyle, GUILayout.Width(28f));
            }
        }

        private static void DrawLegend()
        {
            GUILayout.BeginHorizontal();
            LegendChip("GS", "God Slayer");
            LegendChip("SF", "Strong Foe");
            LegendChip("DK", "Double");
            LegendChip("TK", "Triple");
            LegendChip("QK", "Quad");
            LegendChip("AC", "Ace");
            LegendChip("AP", "5 Kills");
            LegendChip("BD", "10 Kills");
            LegendChip("CV", "Carrier");
            LegendChip("NU", "Advanced");
            LegendChip("ST", "Strategic");
            LegendChip("FTK", "Friendly");
            GUILayout.EndHorizontal();
        }

        private static void LegendChip(string code, string tip)
        {
            _badgeStyle.normal.textColor = BadgeColor(code);
            GUILayout.Label(code, _badgeStyle, GUILayout.Width(26f));
            GUILayout.Label(tip + "  ", _legendStyle);
        }

        private static Color BadgeColor(string code)
        {
            return ScoreboardBadgeService.BadgeColor(code);
        }

        private static string PadName(string name, int width)
        {
            return ScoreboardBadgeService.PadName(name, width);
        }

        private static bool IsInMission()
        {
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

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 20;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.normal.textColor = new Color(0.85f, 1f, 0.9f, 1f);

            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontSize = 16;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.normal.textColor = Color.white;

            _rowStyle = new GUIStyle(GUI.skin.label);
            _rowStyle.fontSize = 14;
            _rowStyle.normal.textColor = new Color(0.88f, 0.9f, 0.88f, 1f);

            _localRowStyle = new GUIStyle(GUI.skin.label);
            _localRowStyle.fontSize = 14;
            _localRowStyle.fontStyle = FontStyle.Bold;
            _localRowStyle.normal.textColor = new Color(0.95f, 1f, 0.55f, 1f);

            _legendStyle = new GUIStyle(GUI.skin.label);
            _legendStyle.fontSize = 12;
            _legendStyle.normal.textColor = new Color(0.65f, 0.7f, 0.68f, 1f);

            _badgeStyle = new GUIStyle(GUI.skin.label);
            _badgeStyle.fontSize = 13;
            _badgeStyle.fontStyle = FontStyle.Bold;
            _badgeStyle.alignment = TextAnchor.MiddleLeft;
            _badgeStyle.normal.textColor = Color.white;
        }
    }
}
