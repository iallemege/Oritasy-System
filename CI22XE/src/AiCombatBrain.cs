using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Full replacement combat brain for AI aircraft (5 tiers).
    /// Level is chosen on the Career Profile / Stats page (PlayerPrefs + config).
    /// Prefixes AIPilotCombatModes / AIHeloCombatState FixedUpdateState.
    /// </summary>
    internal static class AiCombatBrain
    {
        internal const string PrefKey = "Oritasy.AiBrain.Level";
        internal const string PrefEnabledKey = "Oritasy.AiBrain.Enabled";
        internal const int MinLevel = 1;
        internal const int MaxLevel = 5;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<int> _level;
        private static ConfigEntry<bool> _opposingOnly;
        private static ConfigEntry<bool> _affectHelo;

        private static FieldInfo _aircraftField;
        private static bool _fieldsResolved;

        private static readonly Dictionary<int, BrainState> States = new Dictionary<int, BrainState>(64);
        private static readonly List<WeaponStation> StationScratch = new List<WeaponStation>(16);
        private static float _nextPruneAt;
        private static readonly System.Random Rng = new System.Random();

        private enum Mode
        {
            Hunt = 0,
            Attack = 1,
            BreakOff = 2,
            Evade = 3,
            Cruise = 4,
            Acm = 5
        }

        /// <summary>Energy / ACM scripts driven through AutoAim + stick overlays.</summary>
        private enum Maneuver
        {
            None = 0,
            JTurn = 1,
            HighYoYo = 2,
            LowYoYo = 3,
            LagPursuit = 4,
            LeadTurn = 5,
            Scissors = 6,
            EnergyExtend = 7,
            ZoomClimb = 8
        }

        private struct Tier
        {
            public float Skill;
            public float Bravery;
            public float Effort;
            public float AimJitter;
            public float FireDelay;
            public float MissileMax;
            public float MissileMin;
            public float GunMax;
            public float MissileCone;
            public float GunCone;
            public float BreakDist;
            public float RetargetSec;
            public float CmChance;
            public float EvadeSec;
            public float ThrottleAttack;
            public float ThrottleCruise;
            public bool PreferPlayer;
            public float LeadGain;
            public float AcmChance;
            public float AcmStick;
            /// <summary>Seconds between ACM re-evaluations (lower = snappier).</summary>
            public float AcmRefresh;
            /// <summary>Scale on scripted maneuver length (lower = shorter commits).</summary>
            public float AcmDuration;
        }

        private class BrainState
        {
            public Unit Target;
            public Mode Mode;
            public float NextRetargetAt;
            public float NextFireAt;
            public float BreakUntil;
            public float EvadeUntil;
            public float NextCmAt;
            public Vector3 EvadeOffset;
            public float ErrorSeed;
            public Maneuver Maneuver;
            public float ManeuverUntil;
            public float ManeuverStarted;
            public float ManeuverSide;
            public float NextAcmPickAt;
            public float CornerSpeed;
            public Vector3 Intercept;
            public bool HasIntercept;
            public bool EvadeIs39;
            public float EvadeBeamSign;
            public float EvadeStarted;
            public float HuntSide;
            public AiCombatEngagementService.HuntApproachKind HuntKind;
        }

        private static readonly Tier[] Tiers = new Tier[]
        {
            // Timing fields tightened (~0.65×) so state-machine reacts closer to physics rate.
            // 1 Cadet — rare / clumsy ACM
            new Tier
            {
                Skill = 0.32f, Bravery = 0.32f, Effort = 0.75f, AimJitter = 36f, FireDelay = 0.85f,
                MissileMax = 7500f, MissileMin = 1400f, GunMax = 850f, MissileCone = 20f, GunCone = 14f,
                BreakDist = 1100f, RetargetSec = 1.9f, CmChance = 0.40f, EvadeSec = 3.4f,
                ThrottleAttack = 0.85f, ThrottleCruise = 0.7f, PreferPlayer = false, LeadGain = 0.55f,
                AcmChance = 0.32f, AcmStick = 0.48f, AcmRefresh = 0.55f, AcmDuration = 0.62f
            },
            // 2 Regular
            new Tier
            {
                Skill = 0.50f, Bravery = 0.52f, Effort = 0.90f, AimJitter = 16f, FireDelay = 0.48f,
                MissileMax = 12000f, MissileMin = 900f, GunMax = 1200f, MissileCone = 14f, GunCone = 9f,
                BreakDist = 800f, RetargetSec = 1.25f, CmChance = 0.65f, EvadeSec = 2.3f,
                ThrottleAttack = 0.95f, ThrottleCruise = 0.75f, PreferPlayer = true, LeadGain = 0.78f,
                AcmChance = 0.58f, AcmStick = 0.68f, AcmRefresh = 0.38f, AcmDuration = 0.55f
            },
            // 3 Veteran (default)
            new Tier
            {
                Skill = 0.68f, Bravery = 0.72f, Effort = 0.96f, AimJitter = 8f, FireDelay = 0.24f,
                MissileMax = 17000f, MissileMin = 650f, GunMax = 1650f, MissileCone = 11f, GunCone = 7f,
                BreakDist = 580f, RetargetSec = 0.85f, CmChance = 0.88f, EvadeSec = 1.55f,
                ThrottleAttack = 1f, ThrottleCruise = 0.8f, PreferPlayer = true, LeadGain = 0.95f,
                AcmChance = 0.82f, AcmStick = 0.85f, AcmRefresh = 0.24f, AcmDuration = 0.48f
            },
            // 4 Elite
            new Tier
            {
                Skill = 0.85f, Bravery = 0.90f, Effort = 0.99f, AimJitter = 4f, FireDelay = 0.12f,
                MissileMax = 22000f, MissileMin = 450f, GunMax = 2100f, MissileCone = 9f, GunCone = 5.5f,
                BreakDist = 420f, RetargetSec = 0.55f, CmChance = 0.96f, EvadeSec = 1.05f,
                ThrottleAttack = 1f, ThrottleCruise = 0.85f, PreferPlayer = true, LeadGain = 1.08f,
                AcmChance = 0.93f, AcmStick = 0.94f, AcmRefresh = 0.16f, AcmDuration = 0.42f
            },
            // 5 Ace
            new Tier
            {
                Skill = 0.98f, Bravery = 1.0f, Effort = 1.0f, AimJitter = 1.5f, FireDelay = 0.05f,
                MissileMax = 30000f, MissileMin = 280f, GunMax = 2600f, MissileCone = 7f, GunCone = 4f,
                BreakDist = 320f, RetargetSec = 0.35f, CmChance = 1.0f, EvadeSec = 0.7f,
                ThrottleAttack = 1f, ThrottleCruise = 0.9f, PreferPlayer = true, LeadGain = 1.18f,
                AcmChance = 0.98f, AcmStick = 1.0f, AcmRefresh = 0.10f, AcmDuration = 0.35f
            }
        };

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("AiBrain", "Enabled", true,
                "Replace vanilla AI combat with the Oritasy brain. Toggle on Career Profile.");
            _level = config.Bind("AiBrain", "Level", 3,
                "AI combat brain tier 1-5 (Cadet…Ace). Also set from Career Profile.");
            _opposingOnly = config.Bind("AiBrain", "OpposingOnly", true,
                "If true, only rewrite AI opposing the local player's faction.");
            _affectHelo = config.Bind("AiBrain", "AffectHelo", true,
                "Also replace helicopter combat AI.");

            bool en = _enabled.Value;
            try
            {
                if (PlayerPrefs.HasKey(PrefEnabledKey))
                    en = PlayerPrefs.GetInt(PrefEnabledKey, en ? 1 : 0) != 0;
            }
            catch { }
            ApplyEnabled(en, false);

            int pref = _level.Value;
            try { pref = PlayerPrefs.GetInt(PrefKey, pref); }
            catch { }
            ApplyLevel(pref, false);
        }

        internal static bool IsEnabled()
        {
            return _enabled != null && _enabled.Value;
        }

        internal static void SetEnabled(bool on)
        {
            ApplyEnabled(on, true);
        }

        private static void ApplyEnabled(bool on, bool savePrefs)
        {
            if (_enabled != null)
                _enabled.Value = on;
            if (savePrefs)
            {
                try
                {
                    PlayerPrefs.SetInt(PrefEnabledKey, on ? 1 : 0);
                    PlayerPrefs.Save();
                }
                catch { }
            }
        }

        internal static int GetLevel()
        {
            int v = _level != null ? _level.Value : 3;
            if (v < MinLevel)
                return MinLevel;
            if (v > MaxLevel)
                return MaxLevel;
            return v;
        }

        internal static void SetLevel(int level)
        {
            ApplyLevel(level, true);
        }

        private static void ApplyLevel(int level, bool savePrefs)
        {
            level = AiCombatMathService.ClampLevel(level, MinLevel, MaxLevel);
            if (_level != null)
                _level.Value = level;
            if (savePrefs)
            {
                try
                {
                    PlayerPrefs.SetInt(PrefKey, level);
                    PlayerPrefs.Save();
                }
                catch { }
            }
        }

        internal static string GetLevelName(int level)
        {
            switch (level)
            {
                case 1: return ModUiName(true, "新兵", "Cadet");
                case 2: return ModUiName(true, "常规", "Regular");
                case 3: return ModUiName(true, "老兵", "Veteran");
                case 4: return ModUiName(true, "精锐", "Elite");
                case 5: return ModUiName(true, "王牌", "Ace");
                default: return "?";
            }
        }

        private static string ModUiName(bool preferZh, string zh, string en)
        {
            if (IsEnglishOnlyEdition())
                return en;
            // Career ModUiLang lives in WeXon — detect Chinese via PlayerPrefs used by ModUiLang if present.
            try
            {
                // ModUiLang PrefKey "WeXon.UiLang": 0=EN, 1=ZH
                if (PlayerPrefs.HasKey("WeXon.UiLang"))
                    return PlayerPrefs.GetInt("WeXon.UiLang", 0) != 0 ? zh : en;
            }
            catch { }
            return en;
        }

        private static bool IsEnglishOnlyEdition()
        {
            try
            {
                if (PluginInfo.EnglishOnlyEdition)
                    return true;
            }
            catch { }
            return false;
        }

        internal static string GetLevelBlurb(int level)
        {
            bool zh = false;
            if (IsEnglishOnlyEdition())
                zh = false;
            else
            {
                try
                {
                    if (PlayerPrefs.HasKey("WeXon.UiLang"))
                        zh = PlayerPrefs.GetInt("WeXon.UiLang", 0) != 0;
                }
                catch { }
            }
            switch (level)
            {
                case 1:
                    return zh
                        ? "反应慢、开火晚、容易拉脱，适合轻松对战。"
                        : "Slow react, late shots, breaks off early.";
                case 2:
                    return zh
                        ? "接近原版偏强：会导弹/航炮切换，中等压迫。"
                        : "Solid baseline missile/gun switching.";
                case 3:
                    return zh
                        ? "主动占位、J转/悠悠球等能量机动、更优先盯玩家。"
                        : "Presses harder with energy ACM; prefers the player.";
                case 4:
                    return zh
                        ? "高威胁：J转、高低悠悠球、剪刀机动、快射强硬规避。"
                        : "High threat: J-turns, yo-yos, scissors, fast fire.";
                case 5:
                    return zh
                        ? "极致能量战：频繁J转/拉起再攻、近距炮与远距弹。"
                        : "Max energy fight: frequent J-turns/zooms, guns + long missiles.";
                default:
                    return "";
            }
        }

        /// <summary>Career Profile → Enhanced AI master switch + 1–5 skill tiers.</summary>
        internal static void DrawProfileSection()
        {
            GUILayout.Label(UiLang.T("Enhanced AI", "增强 AI"), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("Combat brain", "作战大脑"), GUILayout.Width(140f));
            Color prev = GUI.backgroundColor;
            bool on = IsEnabled();
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

            if (!on)
            {
                GUILayout.Label(UiLang.T(
                    "OFF: vanilla AI. Turn on to replace opposing (or all) AI combat with ACM / energy fighting.",
                    "关：使用原版 AI。打开后用能量战 / 格斗大脑替换敌方（或全部）AI。"),
                    GUILayout.ExpandWidth(true));
                return;
            }

            int lvl = GetLevel();
            GUILayout.BeginHorizontal();
            DrawLevelButton(1, lvl);
            DrawLevelButton(2, lvl);
            DrawLevelButton(3, lvl);
            DrawLevelButton(4, lvl);
            DrawLevelButton(5, lvl);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                GetLevelName(lvl) + "  —  " + GetLevelBlurb(lvl),
                GUILayout.ExpandWidth(true));

            if (_opposingOnly != null)
            {
                bool opp = _opposingOnly.Value;
                bool wantOpp = GUILayout.Toggle(opp,
                    UiLang.T(" Opposing AI only (friendly stay vanilla)", " 仅敌方 AI（友军保持原版）"));
                if (wantOpp != opp)
                    _opposingOnly.Value = wantOpp;
            }
            if (_affectHelo != null)
            {
                bool helo = _affectHelo.Value;
                bool wantHelo = GUILayout.Toggle(helo,
                    UiLang.T(" Include helicopters", " 包含直升机"));
                if (wantHelo != helo)
                    _affectHelo.Value = wantHelo;
            }
        }

        private static void DrawLevelButton(int level, int current)
        {
            bool on = level == current;
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? new Color(0.35f, 0.7f, 0.45f) : Color.white;
            string label = GetLevelName(level);
            if (GUILayout.Button(label, GUILayout.Width(72f), GUILayout.Height(26f)))
                SetLevel(level);
            GUI.backgroundColor = prev;
        }

        private static Tier CurrentTier()
        {
            int i = GetLevel() - 1;
            if (i < 0)
                i = 0;
            if (i >= Tiers.Length)
                i = Tiers.Length - 1;
            return Tiers[i];
        }

        private static void ResolveFields()
        {
            if (_fieldsResolved)
                return;
            _fieldsResolved = true;
            try { _aircraftField = AccessTools.Field(typeof(PilotBaseState), "aircraft"); }
            catch { }
        }

        private static Aircraft AircraftOf(PilotBaseState state)
        {
            ResolveFields();
            if (state == null || _aircraftField == null)
                return null;
            try { return _aircraftField.GetValue(state) as Aircraft; }
            catch { return null; }
        }

        private static bool IsLocalHuman(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.Player != null && Plugin.IsLocalHumanPlayer(ac.Player))
                    return true;
            }
            catch { }
            try
            {
                if (ac.pilots != null)
                {
                    for (int i = 0; i < ac.pilots.Length; i++)
                    {
                        Pilot p = ac.pilots[i];
                        if (p != null && p.playerControlled)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsOpposingLocal(Aircraft ac)
        {
            if (_opposingOnly == null || !_opposingOnly.Value)
                return true;
            try
            {
                Aircraft local;
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return true;
                FactionHQ my = local.NetworkHQ;
                FactionHQ their = ac.NetworkHQ;
                if (my == null || their == null)
                    return true;
                return !object.ReferenceEquals(my, their);
            }
            catch { return true; }
        }

        internal static bool ShouldTakeover(Pilot pilot, Aircraft ac)
        {
            if (!IsEnabled())
                return false;
            if (pilot == null || ac == null)
                return false;
            if (pilot.playerControlled)
                return false;
            if (IsLocalHuman(ac))
                return false;
            if (ac.disabled)
                return false;
            return IsOpposingLocal(ac);
        }

        private static BrainState GetState(Aircraft ac)
        {
            int id = ac.GetInstanceID();
            BrainState s;
            if (!States.TryGetValue(id, out s) || s == null)
            {
                s = new BrainState();
                s.Mode = Mode.Cruise;
                s.ErrorSeed = (float)(Rng.NextDouble() * 6.28);
                States[id] = s;
            }
            return s;
        }

        private static void PruneStates()
        {
            float now = Time.unscaledTime;
            if (now < _nextPruneAt)
                return;
            _nextPruneAt = now + 20f;
            if (States.Count > 120)
                States.Clear();
        }

        private static bool ValidTarget(Unit tgt, Aircraft self)
        {
            if (!AiCombatMathService.ValidTarget(tgt, self))
                return false;
            if (AirportDefection.ShouldHoldFire(self, tgt))
                return false;
            return true;
        }

        private static void ApplyTierStats(Aircraft ac, Tier t)
        {
            AiCombatMathService.ApplySkillFloor(ac, t.Skill, t.Bravery);
            try
            {
                if (!ac.flightAssist)
                    ac.SetFlightAssist(true);
            }
            catch { }
        }

        private static void Retarget(Aircraft ac, Pilot pilot, BrainState s, Tier t, bool helo)
        {
            s.NextRetargetAt = Time.time + t.RetargetSec;
            Unit prev = s.Target;
            Unit best = null;
            float bestScore = -1f;

            bool a2a, a2g, gun;
            AiCombatMathService.ScanLoadout(ac, out a2a, out a2g, out gun);

            bool warn = false;
            try
            {
                MissileWarning mw = ac.GetMissileWarningSystem();
                if (mw != null)
                    warn = mw.IsWarning();
            }
            catch { }

            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { }

            float nearestAir = 99999f;
            bool canHuntAir = a2a || gun;

            try
            {
                List<Aircraft> all = UnitRegistry.allAircraft;
                if (all != null && canHuntAir)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Aircraft other = all[i];
                        if (!ValidTarget(other, ac))
                            continue;
                        float dist = Vector3.Distance(ac.transform.position, other.transform.position);
                        if (dist < nearestAir)
                            nearestAir = dist;
                        // Guns only: do not chase BVR jets with empty AAM rails.
                        if (!a2a && dist > 4500f)
                            continue;
                        float score = AiCombatMathService.ScoreAirTarget(other, ac, local, t.PreferPlayer);
                        if (!a2a && gun)
                            score -= 8000f;
                        if (a2a && dist < t.MissileMax * 1.15f)
                            score += 5000f;
                        if (warn)
                            score += 10000f;
                        if (helo && dist > 2800f)
                            score -= 14000f;
                        if (prev != null && object.ReferenceEquals(prev, other))
                            score += 6500f;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = other;
                        }
                    }
                }
            }
            catch { }

            try
            {
                StationScratch.Clear();
                if (ac.weaponStations != null)
                {
                    for (int i = 0; i < ac.weaponStations.Count; i++)
                    {
                        WeaponStation st = ac.weaponStations[i];
                        if (st != null && st.Ammo > 0 && st.WeaponInfo != null)
                            StationScratch.Add(st);
                    }
                }
                if (StationScratch.Count > 0)
                {
                    CombatAI.TargetSearchResults r = CombatAI.ChooseHQTarget(ac, t.Bravery, StationScratch);
                    if (r.target != null && ValidTarget(r.target, ac))
                    {
                        WeaponInfo hqW = null;
                        try
                        {
                            if (r.chosenWeaponStation != null)
                                hqW = r.chosenWeaponStation.WeaponInfo;
                        }
                        catch { }
                        bool fit = hqW != null
                            ? AiCombatMathService.WeaponFitsTarget(hqW, r.target)
                            : ((r.target is Aircraft) ? (a2a || gun) : a2g);
                        if (fit)
                        {
                            float score = (r.target is Aircraft)
                                ? AiCombatMathService.ScoreAirTarget(r.target as Aircraft, ac, local, t.PreferPlayer)
                                : AiCombatMathService.ScoreGroundTarget(r.target, ac);
                            score += 4000f;
                            if (helo && !(r.target is Aircraft))
                                score += 5000f;
                            if (helo && r.target is Aircraft)
                                score -= 8000f;
                            if (prev != null && object.ReferenceEquals(prev, r.target))
                                score += 6500f;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = r.target;
                            }
                        }
                    }
                }
            }
            catch { }

            bool considerGround = a2g || helo;
            if (considerGround && (helo || !a2a || nearestAir > 7000f || !warn))
            {
                try
                {
                    List<Unit> units = UnitRegistry.allUnits;
                    if (units != null)
                    {
                        for (int i = 0; i < units.Count; i++)
                        {
                            Unit u = units[i];
                            if (!ValidTarget(u, ac))
                                continue;
                            if (u is Aircraft)
                                continue;
                            float score = AiCombatMathService.ScoreGroundTarget(u, ac);
                            if (score < 0f)
                                continue;
                            if (a2g)
                                score += 3500f;
                            if (helo)
                                score += 5000f;
                            if (a2a && nearestAir < 6000f)
                                score -= 9000f;
                            if (warn)
                                score -= 12000f;
                            if (prev != null && object.ReferenceEquals(prev, u))
                                score += 6500f;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = u;
                            }
                        }
                    }
                }
                catch { }
            }

            s.Target = best;
            if (best != prev)
            {
                s.HasIntercept = false;
                s.HuntSide = 0f;
                s.HuntKind = AiCombatEngagementService.HuntApproachKind.LeadChase;
            }
            if (best != null && pilot != null)
            {
                try { pilot.SetPrimaryTarget(best); }
                catch { }
            }
        }

        private static WeaponStation PickStation(Aircraft ac, Unit tgt, Tier t, out bool isGun, out bool isMissile)
        {
            isGun = false;
            isMissile = false;
            if (ac == null || ac.weaponStations == null || tgt == null)
                return null;
            bool wantAir = tgt is Aircraft;
            WeaponStation bestMis = null;
            WeaponStation bestGun = null;
            WeaponStation bestAg = null;
            float bestMisR = -1f;
            for (int i = 0; i < ac.weaponStations.Count; i++)
            {
                WeaponStation st = ac.weaponStations[i];
                if (st == null || st.Ammo <= 0 || st.WeaponInfo == null)
                    continue;
                WeaponInfo w = st.WeaponInfo;
                if (!AiCombatMathService.WeaponFitsTarget(w, tgt))
                    continue;
                AiCombatMathService.WeaponRole role = AiCombatMathService.ClassifyWeapon(w);
                if (role == AiCombatMathService.WeaponRole.Gun)
                {
                    if (bestGun == null)
                        bestGun = st;
                    continue;
                }
                if (wantAir)
                {
                    float reach = Mathf.Max(w.maxSpeed * 8f, t.MissileMax * 0.5f);
                    if (reach > bestMisR)
                    {
                        bestMisR = reach;
                        bestMis = st;
                    }
                }
                else
                {
                    if (bestAg == null || w.bomb || w.glideBomb)
                        bestAg = st;
                }
            }

            if (wantAir)
            {
                if (bestMis != null)
                {
                    isMissile = true;
                    return bestMis;
                }
                if (bestGun != null)
                {
                    isGun = true;
                    return bestGun;
                }
            }
            else
            {
                if (bestAg != null)
                {
                    isMissile = bestAg.WeaponInfo != null
                        && (bestAg.WeaponInfo.missile || bestAg.WeaponInfo.bomb || bestAg.WeaponInfo.glideBomb);
                    return bestAg;
                }
                if (bestGun != null)
                {
                    isGun = true;
                    return bestGun;
                }
            }
            return null;
        }

        private static void TryFire(Aircraft ac, Pilot pilot, WeaponStation st, Unit tgt, bool isGun, Tier t, BrainState s)
        {
            if (ac == null || st == null || tgt == null)
                return;
            try
            {
                if (st.WeaponInfo != null && !AiCombatMathService.WeaponFitsTarget(st.WeaponInfo, tgt))
                    return;
            }
            catch { return; }
            float now = Time.time;
            if (now < s.NextFireAt)
                return;
            float interval = 0.35f;
            try
            {
                if (st.WeaponInfo != null && st.WeaponInfo.fireInterval > 0.05f)
                    interval = st.WeaponInfo.fireInterval;
            }
            catch { }
            s.NextFireAt = AiCombatEngagementService.ScheduleNextFireAt(now, interval, t.FireDelay);

            try
            {
                WeaponManager wm = ac.weaponManager;
                if (wm != null)
                    wm.SetActiveStation(st.Number);
            }
            catch { }

            try { pilot.SetPrimaryTarget(tgt); }
            catch { }

            try
            {
                if (isGun)
                {
                    if (ac.weaponManager != null)
                        ac.weaponManager.FireGuns();
                    else if (pilot != null)
                        pilot.Fire();
                }
                else
                {
                    st.Fire(ac, tgt);
                }
            }
            catch
            {
                // Do not fall back to pilot.Fire() — that shoots the currently selected
                // station, which may be the wrong role (AAM at tanks / AGM at jets).
            }
        }

        private static bool TryEvadeHelo(Aircraft ac, BrainState s, Tier t, ControlInputs inputs,
            bool warn, Missile near, bool hasNear, float now)
        {
            bool ir = hasNear && AiCombatEvadeService.IsIrThreat(near);
            if (!AiCombatEvadeService.ThreatActive(warn, hasNear))
            {
                if (!AiCombatEvadeService.IsEvading(now, s.EvadeUntil))
                    return false;
            }
            else
            {
                s.EvadeIs39 = false;
                s.EvadeUntil = AiCombatEvadeService.ScheduleHeloNoeUntil(now);
                s.EvadeOffset = AiCombatEvadeService.MakeHeloNoeAim(ac, near) - ac.transform.position;
            }

            if (!AiCombatEvadeService.IsEvading(now, s.EvadeUntil))
                return false;

            if (ir && AiCombatEvadeService.ShouldDeployCm(
                    now, s.NextCmAt, t.CmChance, (float)Rng.NextDouble()))
            {
                s.NextCmAt = AiCombatEvadeService.ScheduleNextCmAt(now, t.Skill);
                AiCombatEvadeService.DumpFlares(ac);
            }

            s.Mode = Mode.Evade;
            Vector3 aim = ac.transform.position + s.EvadeOffset;
            AutoAim(ac, aim, AiCombatEvadeService.ResolveHeloNoeAgl(ac), t.Effort, Vector3.zero, true,
                AutopilotAim.EvadeBank);
            if (inputs != null)
            {
                inputs.throttle = AiCombatEvadeService.ResolveHeloEvadeThrottle(ac);
                inputs.brake = 0f;
            }
            return true;
        }

        private static bool TryEvade(Aircraft ac, BrainState s, Tier t, ControlInputs inputs, bool helo)
        {
            float now = Time.time;
            bool warn = false;
            Missile near = null;
            try
            {
                MissileWarning mw = ac.GetMissileWarningSystem();
                if (mw != null)
                {
                    warn = mw.IsWarning();
                    mw.TryGetNearestIncoming(out near);
                }
            }
            catch { }

            bool hasNear = near != null;
            if (AiCombatEvadeService.ShouldIdle(warn, hasNear, now, s.EvadeUntil))
            {
                s.EvadeIs39 = false;
                return false;
            }

            if (helo)
                return TryEvadeHelo(ac, s, t, inputs, warn, near, hasNear, now);

            float mDist = AiCombatEvadeService.MissileDistance(ac, near);
            float tti = AiCombatEvadeService.MissileImpactSec(ac, near);
            float tgtDist = 99999f;
            if (s.Target != null)
                tgtDist = distSafe(ac, s.Target);
            bool ir = hasNear && AiCombatEvadeService.IsIrThreat(near);
            // 3-9 is a radar notch (no chaff on the jet). IR always dumps flares and turns.
            bool use39 = hasNear && !ir && AiCombatEvadeService.IsLongRange39(mDist, tti, tgtDist);
            bool climbout = AiCombatEvadeService.IsClimbout(SafeRalt(ac), SpeedOf(ac));
            if (!AiCombatEvadeService.AllowEvadeCommit(climbout, hasNear, tti, mDist))
            {
                s.EvadeIs39 = false;
                s.EvadeUntil = 0f;
                return false;
            }

            if (AiCombatEvadeService.ThreatActive(warn, hasNear))
            {
                if (AiCombatEvadeService.NeedsNewEvadeWindow(now, s.EvadeUntil))
                {
                    s.EvadeIs39 = use39;
                    s.EvadeStarted = now;
                    if (use39)
                    {
                        Vector3 threat = AiCombatEvadeService.ThreatNotchPoint(ac, near);
                        s.EvadeBeamSign = AiCombatEvadeService.Pick39Side(
                            ac.transform.forward, ac.transform.position, threat);
                        s.EvadeUntil = AiCombatEvadeService.Schedule39Until(now, t.Skill);
                    }
                    else
                    {
                        s.EvadeBeamSign = (Rng.NextDouble() < 0.5) ? -1f : 1f;
                        s.EvadeUntil = AiCombatEvadeService.ScheduleDiveZoomUntil(
                            now, t.Skill, SafeRalt(ac));
                    }
                }

                if (s.EvadeIs39 && hasNear)
                {
                    Vector3 threat = AiCombatEvadeService.ThreatNotchPoint(ac, near);
                    if (s.EvadeBeamSign == 0f)
                    {
                        s.EvadeBeamSign = AiCombatEvadeService.Pick39Side(
                            ac.transform.forward, ac.transform.position, threat);
                    }
                    s.EvadeOffset = AiCombatEvadeService.Make39Offset(
                        ac.transform.position, ac.transform.forward, threat,
                        s.EvadeBeamSign, t.Skill, climbout);
                }
                else
                {
                    bool dive = !climbout && AiCombatEvadeService.DiveZoomDivePhase(
                        s.EvadeStarted, s.EvadeUntil, now, SafeRalt(ac));
                    s.EvadeOffset = AiCombatEvadeService.MakeDiveZoomOffset(
                        dive, ac.transform.forward, s.EvadeBeamSign, t.Skill, climbout);
                }

                // Flares only — radar missiles get the beam, not a fake chaff dump.
                bool dump = ir && AiCombatEvadeService.ShouldDeployCm(
                    now, s.NextCmAt, t.CmChance, (float)Rng.NextDouble());
                if (dump)
                {
                    s.NextCmAt = AiCombatEvadeService.ScheduleNextCmAt(now, t.Skill);
                    AiCombatEvadeService.DumpFlares(ac);
                }
            }

            if (!AiCombatEvadeService.IsEvading(now, s.EvadeUntil))
            {
                s.EvadeIs39 = false;
                return false;
            }

            s.Mode = Mode.Evade;
            Vector3 aim = ac.transform.position + s.EvadeOffset;
            float hold = 200f;
            AutoAim(ac, aim, hold, t.Effort, Vector3.zero, true, AutopilotAim.EvadeBank);
            if (inputs != null)
                inputs.throttle = 1f;
            return true;
        }

        private static float SafeRalt(Aircraft ac)
        {
            return AiCombatMathService.SafeRalt(ac);
        }

        private static float SpeedOf(Aircraft ac)
        {
            if (ac == null)
                return 0f;
            try { return ac.speed; }
            catch { return 0f; }
        }

        private static Vector3 TargetVel(Unit tgt)
        {
            return AiCombatMathService.TargetVel(tgt);
        }

        private static void AutoAim(Aircraft ac, Vector3 worldAim, float altHold, float effort,
            Vector3 tgtVel, bool followTerrain, float bankAllowed)
        {
            if (ac == null || ac.autopilot == null)
                return;
            try
            {
                Vector3 pos = ac.transform.position;
                Autopilot ap = ac.autopilot;
                AutopilotHelo heloAp = ap as AutopilotHelo;
                if (heloAp != null)
                {
                    worldAim.y = pos.y;
                    worldAim = AutopilotAim.LookAhead(pos, worldAim, AutopilotAim.LookAheadM);
                    worldAim = SoftMapClampAim(ac, worldAim);
                    float minHold = AiCombatEvadeService.ResolveHeloMinAgl(ac);
                    altHold = Mathf.Clamp(altHold, minHold, 420f);
                    Vector3 dir = worldAim - pos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.01f)
                        dir = ac.transform.forward;
                    heloAp.AutoAim(worldAim.ToGlobalPosition(), altHold, dir.normalized, Vector3.zero, true);
                    return;
                }

                // Never feed AutoAim a close intercept — lateral jitter becomes a bank PIO.
                worldAim = AutopilotAim.LookAhead(pos, worldAim, AutopilotAim.LookAheadM);
                worldAim = SoftMapClampAim(ac, worldAim);
                // Hold AGL from the aim point, not current altitude (stops flat turns vs high/low bandits).
                float ralt = SafeRalt(ac);
                altHold = Mathf.Max(120f, ralt + (worldAim.y - pos.y));
                GlobalPosition dest = worldAim.ToGlobalPosition();
                AutopilotPlane plane = ap as AutopilotPlane;
                if (plane != null)
                {
                    // tgtVel already baked into intercept; passing it again makes dest wander.
                    plane.AutoAim(dest, true, false, false, effort, bankAllowed, followTerrain, altHold,
                        Vector3.zero);
                    return;
                }
                Vector3 aimDir = ac.transform.forward;
                ap.AutoAim(dest, altHold, aimDir, Vector3.zero, followTerrain);
            }
            catch { }
        }

        /// <summary>Keep AI from chasing / cruising off the playable map disk.</summary>
        private static bool TryGetMapHalfExtent(out float half)
        {
            half = 0f;
            try
            {
                LevelInfo li = NetworkSceneSingleton<LevelInfo>.i;
                if (li != null)
                {
                    if (li.mapSize > 500f)
                    {
                        half = li.mapSize * 0.5f;
                        return true;
                    }
                    MapSettings ms = li.LoadedMapSettings;
                    if (ms != null)
                    {
                        half = Mathf.Max(ms.MapSize.x, ms.MapSize.y) * 0.5f;
                        return half > 500f;
                    }
                }
            }
            catch { }
            return false;
        }

        private static Vector3 SoftMapClampAim(Aircraft ac, Vector3 aim)
        {
            float half;
            if (!TryGetMapHalfExtent(out half))
                return aim;
            return AiCombatMathService.SoftMapClampAim(ac, aim, half);
        }

        private static float ResolveCorner(Aircraft ac, BrainState s)
        {
            if (s.CornerSpeed > 10f)
                return s.CornerSpeed;
            float c = 120f;
            try
            {
                AircraftParameters p = ac.GetAircraftParameters();
                if (p != null && p.cornerSpeed > 10f)
                    c = p.cornerSpeed;
            }
            catch { }
            s.CornerSpeed = c;
            return c;
        }

        private static void StartManeuver(BrainState s, Maneuver m, float duration, float side, Tier t)
        {
            float dur = AiCombatEngagementService.ManeuverDuration(duration, t.AcmDuration);
            s.Maneuver = m;
            s.ManeuverStarted = Time.time;
            s.ManeuverUntil = Time.time + dur;
            s.ManeuverSide = side >= 0f ? 1f : -1f;
            s.Mode = Mode.Acm;
            // Allow re-pick shortly before the script ends (snappy chaining).
            s.NextAcmPickAt = AiCombatEngagementService.NextAcmPickAfterManeuver(
                s.ManeuverUntil, dur, t.Skill);
        }

        private static void ClearManeuver(BrainState s)
        {
            s.Maneuver = Maneuver.None;
            s.ManeuverUntil = 0f;
        }

        private static float AcmDuration(Tier t, float lo, float hi)
        {
            return Mathf.Lerp(lo, hi, t.Skill);
        }

        private static Vector3 FlatSide(Vector3 forward, float side)
        {
            Vector3 f = forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f)
                f = Vector3.forward;
            f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
            return right * side;
        }

        /// <summary>
        /// Choose ACM from geometry / energy. Higher tiers pick J-turns and yo-yos more often.
        /// </summary>
        private static void MaybePickManeuver(Aircraft ac, Unit tgt, BrainState s, Tier t,
            float dist, float angle, bool helo)
        {
            if (helo || !(tgt is Aircraft))
                return;
            float now = Time.time;

            // Mid-script refresh: after ~40% of a maneuver, allow chaining a new one.
            if (s.Maneuver != Maneuver.None && now < s.ManeuverUntil)
            {
                float span = Mathf.Max(0.2f, s.ManeuverUntil - s.ManeuverStarted);
                float u = (now - s.ManeuverStarted) / span;
                if (!AiCombatEngagementService.AllowAcmMidScriptRefresh(
                        u, t.AcmChance, (float)Rng.NextDouble()))
                    return;
            }
            else if (now < s.NextAcmPickAt)
                return;

            float refresh = AiCombatEngagementService.AcmRefreshForRange(t.AcmRefresh, dist);
            s.NextAcmPickAt = now + refresh;

            if (!AiCombatEngagementService.AllowAcmPick(t.AcmChance, (float)Rng.NextDouble()))
                return;

            float mySpd = Mathf.Max(1f, ac.speed);
            float tgtSpd = Mathf.Max(1f, TargetVel(tgt).magnitude);
            float corner = ResolveCorner(ac, s);
            float ralt = SafeRalt(ac);
            float side = (Rng.NextDouble() < 0.5) ? -1f : 1f;
            float dAlt = 0f;
            try { dAlt = tgt.transform.position.y - ac.transform.position.y; }
            catch { }
            // Aspect: are we pointing at them vs them pointing at us
            float aspect = 180f;
            try
            {
                Vector3 fromTgt = ac.transform.position - tgt.transform.position;
                aspect = Vector3.Angle(tgt.transform.forward, fromTgt);
            }
            catch { }

            AiCombatAcmPickService.Script pick = AiCombatAcmPickService.Pick(
                dist, angle, aspect, mySpd, tgtSpd, corner, ralt, t.BreakDist, t.Skill, dAlt);
            if (pick == AiCombatAcmPickService.Script.None)
                return;

            Maneuver m = (Maneuver)(int)pick;
            float lo, hi;
            AiCombatAcmPickService.DurationRange(pick, out lo, out hi);
            StartManeuver(s, m, AcmDuration(t, lo, hi), side, t);
        }

        /// <summary>Execute active ACM. Returns true if this tick is owned by the maneuver.</summary>
        private static bool RunManeuver(Aircraft ac, Unit tgt, BrainState s, Tier t, ControlInputs inputs)
        {
            if (s.Maneuver == Maneuver.None)
                return false;
            float now = Time.time;
            if (now >= s.ManeuverUntil || !ValidTarget(tgt, ac))
            {
                ClearManeuver(s);
                return false;
            }

            float u = Mathf.Clamp01((now - s.ManeuverStarted) / Mathf.Max(0.2f, s.ManeuverUntil - s.ManeuverStarted));
            Vector3 myPos = ac.transform.position;
            Vector3 fwd = ac.transform.forward;
            Vector3 tgtPos = tgt.transform.position;
            Vector3 vel = TargetVel(tgt);
            Vector3 side = FlatSide(fwd, s.ManeuverSide);
            float corner = ResolveCorner(ac, s);
            float ralt = SafeRalt(ac);
            float effort = Mathf.Clamp01(t.Effort + 0.05f);
            Vector3 tgtFwd = Vector3.forward;
            try { tgtFwd = tgt.transform.forward; }
            catch { }

            AiCombatAcmRunService.Output sample;
            if (!AiCombatAcmRunService.Evaluate(
                    (AiCombatAcmPickService.Script)(int)s.Maneuver,
                    u,
                    now - s.ManeuverStarted,
                    myPos, fwd, tgtPos, tgtFwd, vel, side,
                    distSafe(ac, tgt),
                    ac.speed,
                    corner,
                    ralt,
                    SafeRaltUnit(tgt),
                    t.AcmStick,
                    t.ThrottleAttack,
                    s.ManeuverSide,
                    out sample)
                || sample.UnknownScript)
            {
                ClearManeuver(s);
                return false;
            }

            if (sample.ApplyStick)
                StickOverlay(inputs, sample.StickPitch, sample.StickRoll, sample.StickYaw);

            AutoAim(ac, sample.Aim, sample.Hold, effort, vel, true, AutopilotAim.AcmBank);
            if (inputs != null)
                inputs.throttle = sample.Throttle;
            s.Mode = Mode.Acm;
            return true;
        }

        private static float distSafe(Aircraft ac, Unit tgt)
        {
            try { return Vector3.Distance(ac.transform.position, tgt.transform.position); }
            catch { return 1000f; }
        }

        private static float SafeRaltUnit(Unit u)
        {
            Aircraft a = u as Aircraft;
            if (a != null)
                return SafeRalt(a);
            return 0f;
        }

        private static void StickOverlay(ControlInputs inputs, float pitch, float roll, float yaw)
        {
            if (inputs == null)
                return;
            inputs.pitch = Mathf.Clamp(pitch, -1f, 1f);
            inputs.roll = Mathf.Clamp(roll, -1f, 1f);
            inputs.yaw = Mathf.Clamp(yaw, -1f, 1f);
        }

        private static bool IsHeloAirframe(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.autopilot is AutopilotHelo)
                    return true;
            }
            catch { }
            try
            {
                string key = AircraftIdentity.GetKey(ac);
                return AircraftIdentity.IsRotorcraft(key);
            }
            catch { return false; }
        }

        private static void Tick(PilotBaseState state, Pilot pilot, bool helo)
        {
            Aircraft ac = AircraftOf(state);
            if (ac == null && pilot != null)
            {
                try { ac = pilot.aircraft; }
                catch { }
            }
            if (!helo && IsHeloAirframe(ac))
                helo = true;
            if (!ShouldTakeover(pilot, ac))
                return;

            PruneStates();
            Tier t = CurrentTier();
            ApplyTierStats(ac, t);
            BrainState s = GetState(ac);
            ControlInputs inputs = null;
            try { inputs = ac.GetInputs(); }
            catch { }

            if (TryEvade(ac, s, t, inputs, helo))
            {
                ClearManeuver(s);
                return;
            }

            float now = Time.time;
            if (now >= s.NextRetargetAt || !ValidTarget(s.Target, ac))
                Retarget(ac, pilot, s, t, helo);

            bool climbout = !helo && AiCombatEvadeService.IsClimbout(SafeRalt(ac), SpeedOf(ac));

            if (!ValidTarget(s.Target, ac))
            {
                ClearManeuver(s);
                s.Mode = Mode.Cruise;
                Vector3 cruise = AutopilotAim.GroundTrackAim(ac, 12000f);
                // If already near map edge with no target, cruise inward instead of outbound.
                float half;
                if (TryGetMapHalfExtent(out half))
                {
                    Vector3 p = ac.transform.position;
                    float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                    float frac = half > 1f ? r / half : 0f;
                    if (AiCombatEngagementService.PreferInwardCruise(frac))
                    {
                        Vector3 inward = new Vector3(-p.x, 0f, -p.z).normalized;
                        cruise = p + inward * 12000f;
                        cruise.y = p.y;
                    }
                }
                float cruiseHold = helo
                    ? AiCombatEvadeService.ResolveHeloCruiseAgl(ac)
                    : (climbout
                        ? Mathf.Max(AiCombatEvadeService.ClimboutAglM + 200f, SafeRalt(ac) + 250f)
                        : Mathf.Max(400f, SafeRalt(ac)));
                if (climbout)
                    cruise = ac.transform.position + ac.transform.forward * AiCombatEvadeService.ClimboutAimFwdM
                        + Vector3.up * AiCombatEvadeService.ClimboutAimUpM;
                AutoAim(ac, cruise, cruiseHold, t.Effort * 0.85f, Vector3.zero, true,
                    AutopilotAim.CruiseBank);
                if (inputs != null)
                {
                    if (climbout)
                    {
                        inputs.throttle = 1f;
                        inputs.brake = 0f;
                    }
                    else
                    {
                        float frac2 = 0f;
                        float half2;
                        if (TryGetMapHalfExtent(out half2))
                        {
                            Vector3 p2 = ac.transform.position;
                            float r2 = Mathf.Sqrt(p2.x * p2.x + p2.z * p2.z);
                            frac2 = half2 > 1f ? r2 / half2 : 0f;
                        }
                        inputs.throttle = AiCombatEngagementService.CruiseThrottle(t.ThrottleCruise, frac2);
                    }
                }
                return;
            }

            if (climbout)
            {
                ClearManeuver(s);
                s.Mode = Mode.Hunt;
                Vector3 climbAim = ac.transform.position
                    + ac.transform.forward * AiCombatEvadeService.ClimboutAimFwdM
                    + Vector3.up * AiCombatEvadeService.ClimboutAimUpM;
                AutoAim(ac, climbAim,
                    Mathf.Max(AiCombatEvadeService.ClimboutAglM + 200f, SafeRalt(ac) + 250f),
                    t.Effort, Vector3.zero, true, AutopilotAim.HuntBank);
                if (inputs != null)
                {
                    inputs.throttle = 1f;
                    inputs.brake = 0f;
                }
                return;
            }

            Unit tgt = s.Target;
            Vector3 myPos = ac.transform.position;
            Vector3 tgtPos = tgt.transform.position;
            Vector3 vel = TargetVel(tgt);
            float dist = Vector3.Distance(myPos, tgtPos);
            Vector3 toTgt = tgtPos - myPos;
            float angle = 180f;
            try { angle = Vector3.Angle(ac.transform.forward, toTgt); }
            catch { }

            // Active / newly picked energy ACM owns the tick
            MaybePickManeuver(ac, tgt, s, t, dist, angle, helo);
            if (RunManeuver(ac, tgt, s, t, inputs))
                return;

            bool isGun;
            bool isMissile;
            WeaponStation st = PickStation(ac, tgt, t, out isGun, out isMissile);

            if (now < s.BreakUntil)
            {
                s.Mode = Mode.BreakOff;
                if (helo)
                {
                    Vector3 sideH = FlatSide(ac.transform.forward, s.ManeuverSide != 0f ? s.ManeuverSide : 1f);
                    Vector3 awayH = myPos + ac.transform.forward * 1800f + sideH * 900f;
                    AutoAim(ac, awayH, AiCombatEvadeService.ResolveHeloCruiseAgl(ac), t.Effort, Vector3.zero, true,
                        AutopilotAim.BreakBank);
                    if (inputs != null)
                        inputs.throttle = 0.85f;
                    return;
                }
                // Energy break: dive-extend then zoom instead of flat run
                Vector3 away;
                if (AiCombatEngagementService.BreakOffDivePhase(s.BreakUntil, now))
                {
                    away = myPos + ac.transform.forward * 2200f
                        + FlatSide(ac.transform.forward, s.ManeuverSide != 0f ? s.ManeuverSide : 1f) * 800f
                        - Vector3.up * 280f;
                    AutoAim(ac, away, Mathf.Max(140f, SafeRalt(ac) * 0.5f), t.Effort, Vector3.zero, true,
                        AutopilotAim.BreakBank);
                }
                else
                {
                    away = myPos + Vector3.up * 1200f - toTgt.normalized * 800f;
                    AutoAim(ac, away, SafeRalt(ac) + 500f, t.Effort, Vector3.zero, true,
                        AutopilotAim.BreakBank);
                }
                if (inputs != null)
                    inputs.throttle = 1f;
                return;
            }

            if (AiCombatEngagementService.ShouldBreakOff(dist, angle, t.BreakDist))
            {
                // Prefer J-turn / extend ACM over plain break at higher tiers
                if (AiCombatEngagementService.PreferAcmOverBreak(t.AcmChance, (float)Rng.NextDouble()))
                {
                    float side = (Rng.NextDouble() < 0.5) ? -1f : 1f;
                    if (ac.speed > ResolveCorner(ac, s) * 0.9f)
                        StartManeuver(s, Maneuver.JTurn, AcmDuration(t, 2.4f, 1.45f), side, t);
                    else
                        StartManeuver(s, Maneuver.EnergyExtend, AcmDuration(t, 2.2f, 1.35f), side, t);
                    if (RunManeuver(ac, tgt, s, t, inputs))
                        return;
                }
                s.BreakUntil = AiCombatEngagementService.BreakOffUntil(now, t.Skill);
                s.Mode = Mode.BreakOff;
                return;
            }

            float jitter = t.AimJitter;
            float seed = s.ErrorSeed + now * 0.7f;
            Vector3 err = new Vector3(Mathf.Sin(seed) * jitter, Mathf.Cos(seed * 1.3f) * jitter * 0.45f, Mathf.Sin(seed * 0.6f) * jitter);
            Vector3 myVel = AiCombatMathService.OwnVel(ac);
            Vector3 fwd = ac.transform.forward;
            bool dogfight = tgt is Aircraft && dist < 4800f;
            bool guns = isGun && dist < t.GunMax * 1.35f;
            Vector3 rawInt = AiCombatMathService.ComputeAirIntercept(
                myPos, myVel, ac.speed, fwd, tgtPos, vel, dogfight, guns, t.Skill, t.LeadGain);
            float dt = Time.fixedDeltaTime > 0.001f ? Time.fixedDeltaTime : Time.deltaTime;
            s.Intercept = AiCombatMathService.FollowIntercept(
                s.Intercept, s.HasIntercept, rawInt, dt, t.Skill);
            s.HasIntercept = true;
            Vector3 aimPos = s.Intercept + err;
            Vector3 toInt = s.Intercept - myPos;
            float intFwd = 1f;
            if (toInt.sqrMagnitude > 1f)
                intFwd = Vector3.Dot(fwd, toInt.normalized);

            float hold = helo
                ? AiCombatEvadeService.ResolveHeloAttackAgl(ac)
                : Mathf.Max(200f, SafeRalt(ac));
            if (!helo && tgt is Aircraft)
                hold = Mathf.Max(hold, SafeRalt(ac) * 0.35f + 150f);

            bool canShoot = AiCombatEngagementService.CanShoot(
                st != null, isGun, isMissile, dist, angle,
                t.GunMax, t.GunCone, t.MissileMax, t.MissileMin, t.MissileCone);

            if (canShoot)
            {
                s.Mode = Mode.Attack;
                AutoAim(ac, aimPos, hold, t.Effort, vel, !(tgt is Aircraft), AutopilotAim.AttackBank);
                if (inputs != null)
                    inputs.throttle = t.ThrottleAttack;
                TryFire(ac, pilot, st, tgt, isGun, t, s);
            }
            else
            {
                s.Mode = Mode.Hunt;
                Vector3 approach = aimPos;
                float corner = ResolveCorner(ac, s);
                float dAltHunt = tgtPos.y - myPos.y;
                AiCombatEngagementService.HuntApproachKind hunt;
                if (tgt is Aircraft && Mathf.Abs(dAltHunt) > 1200f && dist > 2800f)
                    hunt = AiCombatEngagementService.HuntApproachKind.FarChase;
                else
                    hunt = AiCombatEngagementService.ClassifyHuntApproach(
                        dist, angle, isMissile, t.MissileMax, t.MissileMin, tgt is Aircraft, intFwd,
                        s.HuntKind);
                s.HuntKind = hunt;
                if (hunt == AiCombatEngagementService.HuntApproachKind.FarChase)
                    approach = s.Intercept;
                else if (hunt == AiCombatEngagementService.HuntApproachKind.TooCloseSide)
                {
                    if (s.HuntSide == 0f)
                        s.HuntSide = Vector3.Dot(ac.transform.right, toTgt) >= 0f ? 1f : -1f;
                    Vector3 sideV = FlatSide(ac.transform.forward, s.HuntSide);
                    // Ahead + offset, not 90° abeam (pure beam + AutoAim = continuous roll).
                    approach = myPos + ac.transform.forward * 4500f + sideV * 900f + Vector3.up * 180f;
                }
                else if (hunt == AiCombatEngagementService.HuntApproachKind.LagLine)
                {
                    Vector3 tgtFwd = tgt.transform.forward;
                    approach = tgtPos - tgtFwd * Mathf.Clamp(dist * 0.35f, 400f, 1400f);
                    s.HuntSide = 0f;
                }
                else
                    s.HuntSide = 0f;
                AutoAim(ac, approach, hold, t.Effort, vel, true, AutopilotAim.HuntBank);
                if (inputs != null)
                {
                    inputs.throttle = AiCombatEngagementService.HuntThrottle(
                        dist, ac.speed, corner, angle, t.ThrottleAttack);
                }
            }
        }

        // ——— Harmony ———

        [HarmonyPatch(typeof(AIPilotCombatModes), "FixedUpdateState")]
        private static class Patch_FixedWingBrain
        {
            [HarmonyPrefix]
            private static bool Prefix(AIPilotCombatModes __instance, Pilot pilot)
            {
                Aircraft ac = AircraftOf(__instance);
                if (!ShouldTakeover(pilot, ac))
                    return true;
                try { Tick(__instance, pilot, false); }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("AiBrain: " + ex.Message);
                    return true;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(AIHeloCombatState), "FixedUpdateState")]
        private static class Patch_HeloBrain
        {
            [HarmonyPrefix]
            private static bool Prefix(AIHeloCombatState __instance, Pilot pilot)
            {
                if (_affectHelo != null && !_affectHelo.Value)
                    return true;
                Aircraft ac = AircraftOf(__instance);
                if (!ShouldTakeover(pilot, ac))
                    return true;
                try { Tick(__instance, pilot, true); }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("AiBrain helo: " + ex.Message);
                    return true;
                }
                return false;
            }
        }
    }
}
