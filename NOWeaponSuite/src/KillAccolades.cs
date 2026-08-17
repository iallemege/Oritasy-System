using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// War Thunder-style air-kill accolades (mid-bottom HUD) + match-scoped unlocks
    /// for StrategicArsenal / Support 支援 (extra carrier / advanced weapons).
    /// Unlocks reset when leaving or entering a mission — not career-persistent.
    /// </summary>
    internal static class KillAccolades
    {
        internal const string UnlockCarrier = "carrier";
        internal const string UnlockAdvanced = "advanced";
        internal const string UnlockStrategic = "strategic";

        private const string PrefPrefix = "WeXon.Accolade.";
        private const string MatchScopedPref = "WeXon.Accolade.MatchScoped111";
        private static float StreakWindow { get { return KillCombatMathService.StreakWindow; } }

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _persistUnlocks;
        private static ConfigEntry<float> _strongGap;
        private static ConfigEntry<float> _godGap;

        private static readonly HashSet<string> SessionUnlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<AccoladeFlash> Queue = new Queue<AccoladeFlash>();

        private static AccoladeFlash _current;
        private static GUIStyle _titleStyle;
        private static GUIStyle _subStyle;

        private static int _missionAirKills;
        private static int _streak;
        private static float _streakUntil;
        private static readonly HashSet<int> RecentVictimIds = new HashSet<int>();
        private static float _recentClearAt;

        private sealed class AccoladeFlash
        {
            public string Title;
            public string Sub;
            public Color Color;
            public float Until;
            public float Born;
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("KillAccolades", "Enabled", true,
                "Show air-kill accolades (mid-bottom) and unlock Support / 支援 arsenal perks for the current match.");
            // Legacy: formerly persisted across matches. 111C+ is always match-scoped.
            _persistUnlocks = config.Bind("KillAccolades", "PersistUnlocks", false,
                "Deprecated. Killstreak unlocks are match-only; this is forced false.");
            _strongGap = config.Bind("KillAccolades", "StrongEnemySkillGap", 0.15f,
                "Victim.skill - your.skill for Strong Foe.");
            _godGap = config.Bind("KillAccolades", "GodSlayerSkillGap", 0.35f,
                "Victim.skill - your.skill for God Slayer.");

            ModUiLang.EnsureLoaded();
            MigrateToMatchScopedUnlocks();
        }

        /// <summary>Clear streak / unlocks at mission start or when leaving a match.</summary>
        internal static void ClearMatchUnlocks()
        {
            SessionUnlocks.Clear();
            _missionAirKills = 0;
            _streak = 0;
            _streakUntil = 0f;
            RecentVictimIds.Clear();
            Queue.Clear();
            _current = null;
            ClearPersistedUnlockPrefs();
            TryResetOritasyKillChoice();
        }

        private static void TryResetOritasyKillChoice()
        {
            try
            {
                Type t = Type.GetType("Oritasy.KillChoiceMenu, Oritasy")
                    ?? Type.GetType("Oritasy.KillChoiceMenu");
                if (t == null)
                    return;
                MethodInfo m = t.GetMethod("ResetMatch",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                    m.Invoke(null, null);
            }
            catch { }
        }

        private static void MigrateToMatchScopedUnlocks()
        {
            try
            {
                if (_persistUnlocks != null && _persistUnlocks.Value)
                    _persistUnlocks.Value = false;
            }
            catch { }

            if (PlayerPrefs.GetInt(MatchScopedPref, 0) == 0)
            {
                ClearPersistedUnlockPrefs();
                SessionUnlocks.Clear();
                PlayerPrefs.SetInt(MatchScopedPref, 1);
            }
            else
            {
                // Never reload career unlocks into a new process — match-only.
                ClearPersistedUnlockPrefs();
                SessionUnlocks.Clear();
            }
        }

        private static void ClearPersistedUnlockPrefs()
        {
            try
            {
                PlayerPrefs.DeleteKey(PrefPrefix + UnlockCarrier);
                PlayerPrefs.DeleteKey(PrefPrefix + UnlockAdvanced);
                PlayerPrefs.DeleteKey(PrefPrefix + UnlockStrategic);
            }
            catch { }
        }

        internal static void Tick()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            if (Time.unscaledTime >= _recentClearAt && RecentVictimIds.Count > 0)
                RecentVictimIds.Clear();

            if (_current != null && Time.unscaledTime >= _current.Until)
                _current = null;
            if (_current == null && Queue.Count > 0)
                _current = Queue.Dequeue();
        }

        internal static void DrawGui()
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (_current == null || string.IsNullOrEmpty(_current.Title))
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            EnsureStyles();
            float life = Mathf.Clamp01((_current.Until - Time.unscaledTime) / 3.2f);
            float fadeIn = Mathf.Clamp01((Time.unscaledTime - _current.Born) / 0.18f);
            float alpha = Mathf.Min(fadeIn, life);
            // Slight rise from bottom-center
            float y = GuiScale.Height * 0.72f - (1f - life) * 18f;
            float w = Mathf.Min(820f, GuiScale.Width * 0.78f);
            Rect titleR = new Rect((GuiScale.Width - w) * 0.5f, y, w, 42f);
            Rect subR = new Rect(titleR.x, titleR.yMax + 2f, w, 28f);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f * alpha);
            GUI.DrawTexture(new Rect(titleR.x - 10f, titleR.y - 6f, w + 20f, 78f), Texture2D.whiteTexture);

            Color c = _current.Color;
            c.a = alpha;
            _titleStyle.normal.textColor = c;
            GUI.color = Color.white;
            GUI.Label(titleR, _current.Title, _titleStyle);

            _subStyle.normal.textColor = new Color(0.92f, 0.92f, 0.88f, alpha);
            if (!string.IsNullOrEmpty(_current.Sub))
                GUI.Label(subR, _current.Sub, _subStyle);

            GUI.color = prev;
        }

        internal static bool HasUnlock(string key)
        {
            // Memory-only for the current match (cleared on mission enter/leave).
            if (string.IsNullOrEmpty(key))
                return true;
            return SessionUnlocks.Contains(key);
        }

        /// <summary>
        /// F6 mystery boost: grant a match-scoped F9 arsenal unlock.
        /// Carrier is never granted here.
        /// </summary>
        internal static bool TryGrantUnlockFromOutside(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (string.Equals(key, UnlockCarrier, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(key, UnlockAdvanced, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, UnlockStrategic, StringComparison.OrdinalIgnoreCase))
                return false;
            if (HasUnlock(key))
                return false;
            GrantUnlock(key, null);
            return true;
        }

        internal static string DescribeUnlockForUi(string key)
        {
            return DescribeUnlock(key);
        }

        internal static string UnlockHint(string key)
        {
            if (string.IsNullOrEmpty(key) || HasUnlock(key))
                return null;
            bool zh = ModUiLang.IsChinese;
            if (string.Equals(key, UnlockCarrier, StringComparison.OrdinalIgnoreCase))
                return zh ? "需要：神挡杀神 或 空中王牌" : "Need: God Slayer or Flying Ace";
            if (string.Equals(key, UnlockAdvanced, StringComparison.OrdinalIgnoreCase))
                return zh ? "需要：强敌击破 / 三杀 / 神挡杀神" : "Need: Strong Foe / Triple / God Slayer";
            if (string.Equals(key, UnlockStrategic, StringComparison.OrdinalIgnoreCase))
                return zh ? "需要：战场主宰 或 神挡杀神+王牌" : "Need: Battlefield Dominator or God Slayer+Ace";
            return zh ? "需要击杀成就" : "Need kill accolades";
        }

        internal static void ProcessAirKill(Player killerPlayer, Aircraft victim)
        {
            ProcessKill(killerPlayer, victim);
        }

        /// <summary>Local player kill of air / ground / ship / building (not missiles).</summary>
        internal static void ProcessKill(Player killerPlayer, Unit victim)
        {
            int vid = victim != null ? victim.GetInstanceID() : 0;
            bool dedup = victim != null && RecentVictimIds.Contains(vid);

            Aircraft localAc = null;
            try { GameManager.GetLocalAircraft(out localAc); }
            catch { }
            if (localAc == null)
            {
                try { localAc = killerPlayer != null ? killerPlayer.Aircraft : null; }
                catch { }
            }

            Aircraft victimAc = victim as Aircraft;
            bool air = victimAc != null;

            bool friendly = false;
            if (localAc != null && victim != null && Plugin.IsSameFaction(localAc, victim))
                friendly = true;
            else
            {
                try
                {
                    FactionHQ myHq = killerPlayer != null ? killerPlayer.HQ : null;
                    FactionHQ vicHq = Plugin.GetHq(victim);
                    friendly = myHq != null && vicHq != null && object.ReferenceEquals(myHq, vicHq);
                }
                catch { }
            }

            KillAccoladeProcessGateService.Path path = KillAccoladeProcessGateService.Resolve(
                _enabled != null && _enabled.Value,
                victim == null,
                IsCountableVictim(victim),
                killerPlayer != null && Plugin.IsLocalHumanPlayer(killerPlayer),
                dedup,
                friendly);
            if (path == KillAccoladeProcessGateService.Path.Skip)
                return;

            if (victim != null)
            {
                RecentVictimIds.Add(vid);
                _recentClearAt = Time.unscaledTime + KillCombatMathService.DedupSeconds;
            }

            if (path == KillAccoladeProcessGateService.Path.FriendlyOnly)
            {
                try { PlayerCareer.RecordFriendlyKill(victim, localAc); }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("PlayerCareer friendly: " + ex.Message);
                }
                return;
            }

            float mySkill = localAc != null ? localAc.skill : 0.35f;
            float theirSkill = air ? victimAc.skill : 0f;
            float gap = theirSkill - mySkill;
            float strongGap = _strongGap != null ? _strongGap.Value : KillCombatMathService.DefaultStrongGap;
            float godGap = _godGap != null ? _godGap.Value : KillCombatMathService.DefaultGodGap;

            _missionAirKills++;
            float now = Time.unscaledTime;
            _streak = KillCombatMathService.AdvanceStreak(_streak, now, ref _streakUntil);

            string victimName = ResolveUnitName(victim);
            try { CombatKillXpTracker.NoteUnitDestroyed(victim); }
            catch { }
            try
            {
                string xpTail = CombatKillXpTracker.FormatPeek(victim);
                if (!string.IsNullOrEmpty(xpTail))
                    victimName = victimName + xpTail;
            }
            catch { }
            List<string> unlockedNow = new List<string>();
            bool zh = ModUiLang.IsChinese;

            KillCombatMathService.SkillKillKind kind = KillCombatMathService.ClassifySkillGap(
                air, gap, strongGap, godGap);

            // Primary skill-gap accolade (air only — ground/ships use destroy wording)
            if (kind == KillCombatMathService.SkillKillKind.GodSlayer)
            {
                Enqueue(
                    zh ? "神挡杀神" : "GOD SLAYER",
                    zh
                        ? (victimName + "  技能差 +" + gap.ToString("0.00"))
                        : (victimName + "  skill gap +" + gap.ToString("0.00")),
                    KillCombatMathService.FlashColor(kind));
            }
            else if (kind == KillCombatMathService.SkillKillKind.StrongFoe)
            {
                Enqueue(
                    zh ? "强敌击破" : "STRONG FOE",
                    zh
                        ? (victimName + "  技能差 +" + gap.ToString("0.00"))
                        : (victimName + "  skill gap +" + gap.ToString("0.00")),
                    KillCombatMathService.FlashColor(kind));
            }
            else if (kind == KillCombatMathService.SkillKillKind.EasyKill)
            {
                Enqueue(zh ? "轻松击落" : "EASY KILL", victimName,
                    KillCombatMathService.FlashColor(kind));
            }
            else if (kind == KillCombatMathService.SkillKillKind.Kill)
            {
                Enqueue(zh ? "击落" : "KILL", victimName,
                    KillCombatMathService.FlashColor(kind));
            }
            else
            {
                Enqueue(zh ? "击毁" : "DESTROYED", victimName,
                    KillCombatMathService.FlashColor(kind));
            }

            // Streak accolades
            if (_streak == 2)
                Enqueue(zh ? "双杀" : "DOUBLE KILL",
                    zh ? "连续击落" : "Streak", new Color(1f, 0.85f, 0.35f, 1f));
            else if (_streak == 3)
            {
                Enqueue(zh ? "三杀" : "TRIPLE KILL",
                    zh ? "连续击落" : "Streak", new Color(1f, 0.65f, 0.2f, 1f));
            }
            else if (_streak == 4)
                Enqueue(zh ? "四杀" : "QUAD KILL",
                    zh ? "势不可挡" : "Unstoppable", new Color(1f, 0.45f, 0.2f, 1f));
            else if (_streak >= 5)
            {
                Enqueue(zh ? "空中王牌" : "FLYING ACE",
                    zh ? (_streak.ToString() + " 连杀") : (_streak.ToString() + " streak"),
                    new Color(1f, 0.25f, 0.15f, 1f));
            }

            // Mission totals
            if (_missionAirKills == 5)
            {
                Enqueue(zh ? "空战先锋" : "AIR PIONEER",
                    zh ? "本局 5 杀" : "5 kills this match",
                    new Color(0.85f, 1f, 0.55f, 1f));
            }
            if (_missionAirKills == 10)
            {
                Enqueue(zh ? "战场主宰" : "BATTLEFIELD DOMINATOR",
                    zh ? "本局 10 杀" : "10 kills this match",
                    new Color(1f, 0.4f, 0.75f, 1f));
            }

            KillAccoladeUnlockMathService.UnlockFlags flags =
                KillAccoladeUnlockMathService.FromSkillKind(kind)
                | KillAccoladeUnlockMathService.FromStreak(_streak)
                | KillAccoladeUnlockMathService.FromMissionKills(_missionAirKills);
            bool willCarrier = HasUnlock(UnlockCarrier)
                || (flags & KillAccoladeUnlockMathService.UnlockFlags.Carrier) != 0;
            bool willAdvanced = HasUnlock(UnlockAdvanced)
                || (flags & KillAccoladeUnlockMathService.UnlockFlags.Advanced) != 0;
            flags |= KillAccoladeUnlockMathService.FromCombo(willCarrier, willAdvanced, _missionAirKills);
            var unlockKeys = new List<string>(4);
            KillAccoladeUnlockMathService.AppendKeys(flags, unlockKeys);
            for (int u = 0; u < unlockKeys.Count; u++)
                GrantUnlock(unlockKeys[u], unlockedNow);

            for (int i = 0; i < unlockedNow.Count; i++)
                Enqueue(zh ? "解锁" : "UNLOCKED", DescribeUnlock(unlockedNow[i]),
                    new Color(0.45f, 1f, 0.65f, 1f));

            try
            {
                string weapon = PlayerCareer.GuessWeapon(localAc);
                PlayerCareer.RecordKill(victim, localAc, weapon, gap, _streak);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("PlayerCareer record: " + ex.Message);
            }

            if (Plugin.Log != null && Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("KillAccolade kill streak=" + _streak
                    + " gap=" + gap.ToString("0.00") + " total=" + _missionAirKills
                    + " type=" + victim.GetType().Name);
        }

        internal static bool IsCountableVictim(Unit u)
        {
            return KillCombatMathService.IsCountableVictim(u);
        }

        private static void GrantUnlock(string key, List<string> report)
        {
            if (string.IsNullOrEmpty(key) || HasUnlock(key))
                return;
            // Match-scoped only — never write PlayerPrefs (survives only this mission).
            SessionUnlocks.Add(key);
            if (report != null)
                report.Add(key);

            // Gold kill-feed + Tab board initials for newly earned arsenal unlocks
            try
            {
                Player local;
                if (GameManager.GetLocalPlayer(out local) && local != null)
                    MatchScoreboard.NoteUnlockAchievement(local, key);
            }
            catch { }
        }

        private static string DescribeUnlock(string key)
        {
            bool zh = ModUiLang.IsChinese;
            if (string.Equals(key, UnlockCarrier, StringComparison.OrdinalIgnoreCase))
                return zh ? "可购买额外友军航母" : "Extra allied carrier purchase";
            if (string.Equals(key, UnlockAdvanced, StringComparison.OrdinalIgnoreCase))
                return zh ? "可使用核巡航 / 核TBM" : "Nuclear cruise / TBM unlocked";
            if (string.Equals(key, UnlockStrategic, StringComparison.OrdinalIgnoreCase))
                return zh ? "可使用战略级齐射" : "Strategic salvos unlocked";
            return key;
        }

        private static string ResolveName(Aircraft ac)
        {
            return ResolveUnitName(ac);
        }

        private static string ResolveUnitName(Unit u)
        {
            if (u == null)
                return ModUiLang.IsChinese ? "目标" : "Target";
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
                return ModUiLang.IsChinese ? "敌机" : "Hostile aircraft";
            if (u is Ship)
                return ModUiLang.IsChinese ? "敌舰" : "Hostile ship";
            if (u is GroundVehicle)
                return ModUiLang.IsChinese ? "地面单位" : "Ground unit";
            if (u is Building)
                return ModUiLang.IsChinese ? "建筑" : "Building";
            return ModUiLang.IsChinese ? "目标" : "Target";
        }

        private static void Enqueue(string title, string sub, Color color)
        {
            AccoladeFlash flash = new AccoladeFlash();
            flash.Title = title;
            flash.Sub = sub;
            flash.Color = color;
            flash.Born = Time.unscaledTime;
            flash.Until = Time.unscaledTime + 3.4f;
            // If nothing on screen, show immediately; else queue
            if (_current == null)
                _current = flash;
            else
                Queue.Enqueue(flash);
        }

        private static void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label);
                _titleStyle.fontSize = 36;
                _titleStyle.fontStyle = FontStyle.Bold;
                _titleStyle.alignment = TextAnchor.MiddleCenter;
                _titleStyle.wordWrap = false;
            }
            if (_subStyle == null)
            {
                _subStyle = new GUIStyle(GUI.skin.label);
                _subStyle.fontSize = 18;
                _subStyle.alignment = TextAnchor.MiddleCenter;
                _subStyle.wordWrap = false;
            }
        }

        internal static void TryProcessFromUnits(Unit killerUnit, Unit killedUnit)
        {
            if (killedUnit == null || !IsCountableVictim(killedUnit))
                return;

            Player local;
            if (!GameManager.GetLocalPlayer(out local) || local == null)
                return;

            // Killer is local aircraft, or missile/unit owned by local side that we flew
            bool mine = false;
            Aircraft localAc = null;
            try { GameManager.GetLocalAircraft(out localAc); }
            catch { }

            if (localAc != null && object.ReferenceEquals(killerUnit, localAc))
                mine = true;
            else if (killerUnit != null)
            {
                try
                {
                    Missile m = killerUnit as Missile;
                    if (m != null && localAc != null && object.ReferenceEquals(m.owner, localAc))
                        mine = true;
                    else if (m != null && m.owner != null)
                    {
                        Aircraft ownerAc = m.owner as Aircraft;
                        if (ownerAc != null && ownerAc.Player != null
                            && Plugin.IsLocalHumanPlayer(ownerAc.Player))
                            mine = true;
                    }
                }
                catch { }
            }

            // ReportKillAction path already has Player — this is RpcKill fallback
            if (!mine)
            {
                // Still accept if killer persistent matches local aircraft
                if (localAc != null && killerUnit != null
                    && object.ReferenceEquals(Plugin.GetHq(killerUnit), Plugin.GetHq(localAc))
                    && killerUnit is Aircraft
                    && object.ReferenceEquals(killerUnit, localAc))
                    mine = true;
            }
            if (!mine)
                return;

            ProcessKill(local, killedUnit);
        }
    }

    [HarmonyPatch(typeof(FactionHQ), "ReportKillAction")]
    internal static class Patch_FactionHQ_ReportKillAction
    {
        [HarmonyPostfix]
        private static void Postfix(Player player, Unit target, float factor)
        {
            if (player == null || target == null)
                return;
            if (!KillAccolades.IsCountableVictim(target))
                return;
            MatchScoreboard.NoteKill(player, target);
            KillAccolades.ProcessKill(player, target);
        }
    }

    [HarmonyPatch(typeof(MessageManager), "UserCode_RpcKillMessage_635947223")]
    internal static class Patch_MessageManager_RpcKillMessage
    {
        [HarmonyPostfix]
        private static void Postfix(PersistentID killerID, PersistentID killedID, KillType killedType)
        {
            if (killedType != KillType.Aircraft
                && killedType != KillType.Vehicle
                && killedType != KillType.Building
                && killedType != KillType.Ship)
                return;
            Unit killer = null;
            Unit killed = null;
            try { killerID.TryGetUnit(out killer); }
            catch { }
            try { killedID.TryGetUnit(out killed); }
            catch { }
            MatchScoreboard.NoteFromUnits(killer, killed);
            KillAccolades.TryProcessFromUnits(killer, killed);
        }
    }
}
