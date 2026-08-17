using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// F6 kill-choice: rewards at air-kill thresholds k(k+1) → 2, 6, 12, 20…
    /// Mystery pick of core buffs + locked F9 support (no carrier) + 30 extra pool kinds.
    /// Unlocked advanced F9 may re-enter the pool from reward 3 onward.
    /// Draw Next: first two when stacked; never on the 3rd; after 3rd only when stacked.
    /// </summary>
    internal static class KillChoiceMenu
    {
        private const float AirframePayFraction = 0.2f; // −80% → pay 20%
        private const int AiRestrictCount = 6;
        /// <summary>DRAW NEXT for rewards 1–2 when stacked; blocked exactly at reward 3.</summary>
        private const int MaxDrawNextRewardIndex = 2;
        /// <summary>Unlocked advanced F9 may re-enter the mystery pool from this reward index.</summary>
        private const int MinRewardForUnlockedAdvanced = 3;

        internal enum BoostKind
        {
            AirframeDiscount = 0,
            FreeMissiles = 1,
            AiRankRestrict = 2,
            /// <summary>F9 locked: nuclear cruise / single nuke TBM (KillAccolades.advanced).</summary>
            F9Advanced = 3,
            /// <summary>F9 locked: strategic salvos (KillAccolades.strategic). Never carrier.</summary>
            F9Strategic = 4,
            Cash5 = 5,
            Cash15 = 6,
            Cash30 = 7,
            RepairNow = 8,
            RefuelNow = 9,
            BatteryNow = 10,
            RearmGuns = 11,
            RearmStations = 12,
            RearmCm = 13,
            ExtinguishNow = 14,
            EngineHeal = 15,
            Xp100 = 16,
            Xp250 = 17,
            Xp500 = 18,
            F9Cooldown = 19,
            ExtraPicks = 20,
            AirframeFree = 21,
            AiRestrictHard = 22,
            AiRestrictLong = 23,
            RepairOnLand = 24,
            FreeResupply = 25,
            ThrustBurst = 26,
            GBurst = 27,
            FuelEcon = 28,
            SpeedBurst = 29,
            RepairFew = 30,
            SortieKit = 31,
            Cash10 = 32,
            Xp350 = 33,
            AiRestrictStack = 34,
            EngineMaterial = 35
        }

        private const string F9UnlockAdvanced = "advanced";
        private const string F9UnlockStrategic = "strategic";

        private static ConfigEntry<KeyCode> _menuKey;
        private static bool _menuOpen;
        private static int _missionAirKills;
        /// <summary>How many rewards have been earned this match (1→@2, 2→@6, 3→@12…).</summary>
        private static int _rewardsEarned;
        private static int _pendingChoices;
        private static bool _airframeDiscountPending;
        private static bool _freeMissilesPending;
        private static bool _freeMissilesSpawnActive;
        private static int _aiRestrictRemaining;
        private static int _aiMaxRank = -1;
        private static readonly HashSet<int> RecentVictimIds = new HashSet<int>();
        private static float _recentClearAt;
        private static string _status = "";
        private static float _statusUntil;
        /// <summary>Three mystery slots drawn from core boosts + locked F9 support (no carrier).</summary>
        private static readonly BoostKind[] _slotOrder = new BoostKind[3];
        private static string _lastReveal = "";
        private static bool _showReveal;
        /// <summary>Local mirror of match F9 unlocks (strategic never re-enters once owned).</summary>
        private static bool _f9AdvancedOwned;
        private static bool _f9StrategicOwned;
        private static Type _killAccoladesType;
        private static MethodInfo _hasUnlockMethod;
        private static MethodInfo _grantUnlockMethod;
        private static MethodInfo _describeUnlockMethod;
        private static MethodInfo _clearArsenalCooldownMethod;

        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _chipStyle;
        private static GUIStyle _optStyle;
        private static bool _cursorHeld;

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static bool AirframeDiscountPending
        {
            get { return _airframeDiscountPending || KillChoiceRewardService.AirframeFreePending; }
        }

        internal static bool FreeMissilesPending
        {
            get { return _freeMissilesPending; }
        }

        internal static void Bind(ConfigFile config)
        {
            _menuKey = config.Bind("KillChoice", "MenuKey", KeyCode.F6,
                "Open kill-choice menu (rewards at 2 / 6 / 12 / 20… air kills).");
        }

        /// <summary>Absolute kill count for the next reward: 1→2, 2→6, 3→12 (k(k+1)).</summary>
        private static int NextRewardKillThreshold()
        {
            int k = _rewardsEarned + 1;
            return k * (k + 1);
        }

        internal static void ResetMatch()
        {
            _missionAirKills = 0;
            _rewardsEarned = 0;
            _pendingChoices = 0;
            _airframeDiscountPending = false;
            _freeMissilesPending = false;
            _freeMissilesSpawnActive = false;
            _aiRestrictRemaining = 0;
            _aiMaxRank = -1;
            _lastReveal = "";
            _showReveal = false;
            _f9AdvancedOwned = false;
            _f9StrategicOwned = false;
            RecentVictimIds.Clear();
            KillChoiceRewardService.ResetMatch();
            CloseMenu();
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        internal static void Tick()
        {
            KillChoiceRewardService.Tick();
            if (Time.unscaledTime >= _recentClearAt && RecentVictimIds.Count > 0)
                RecentVictimIds.Clear();

            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F6;
            if (menu != KeyCode.None && Input.GetKeyDown(menu))
            {
                if (_menuOpen)
                    CloseMenu();
                else
                    OpenMenu();
            }
            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseMenu();
        }

        internal static void DrawGui()
        {
            EnsureStyles();
            if (Plugin.AllowThirdPersonUi)
                DrawCornerHint();
            if (!_menuOpen)
                return;
            HoldCursor();
            DrawMenu();
        }

        /// <summary>Called from Harmony after a local air kill is credited.</summary>
        internal static void NoteLocalAirKill(Player killer, Unit victim)
        {
            if (killer == null || victim == null)
                return;
            if (!(victim is Aircraft))
                return;
            if (!Plugin.IsLocalHumanPlayer(killer))
                return;

            int vid = victim.GetInstanceID();
            if (RecentVictimIds.Contains(vid))
                return;
            RecentVictimIds.Add(vid);
            _recentClearAt = Time.unscaledTime + 2.5f;

            // Friendly fire skip
            try
            {
                FactionHQ myHq = killer.HQ;
                FactionHQ vicHq = null;
                try { vicHq = victim.NetworkHQ; }
                catch { }
                if (vicHq == null)
                {
                    try { vicHq = victim.MapHQ; }
                    catch { }
                }
                if (myHq != null && vicHq != null && object.ReferenceEquals(myHq, vicHq))
                    return;
            }
            catch { }

            _missionAirKills++;
            bool earned = false;
            while (_missionAirKills >= NextRewardKillThreshold())
            {
                _pendingChoices++;
                _rewardsEarned++;
                earned = true;
            }
            if (earned)
            {
                SetStatus(UiLang.T(
                    "Kill reward ready — press F6 (" + _pendingChoices + ")",
                    "击杀奖励就绪 — 按 F6（" + _pendingChoices + "）"));
                if (!_menuOpen)
                    OpenMenu();
            }
        }

        private static void OpenMenu()
        {
            if (MissileCameraHud.ManualActive)
                return;
            if (AircraftManeuverGui.IsOpen)
                AircraftManeuverGui.Close();
            if (PlayerAutopilot.MenuOpen)
                PlayerAutopilot.CloseMenuFromOutside();
            if (AerialResupply.MenuOpen)
                AerialResupply.CloseMenuFromOutside();
            if (WarThunderRwrHud.LayoutMenuOpen)
                WarThunderRwrHud.CloseLayoutMenuFromOutside();
            if (BeginnerAssist.MenuOpen)
                BeginnerAssist.CloseMenuFromOutside();
            if (IlsSettingsMenu.MenuOpen)
                IlsSettingsMenu.CloseMenuFromOutside();
            if (PrivateMessageMenu.MenuOpen)
                PrivateMessageMenu.CloseMenuFromOutside();
            if (HostFundMenu.MenuOpen)
                HostFundMenu.CloseMenuFromOutside();
            AirframeWearGui.CloseFromOutside();
            PlayerAutopilot.CloseWeXonSupportMenu();
            ShuffleSlots();
            // Fresh open: hide previous reveal until a new pick (unless no pending — show last).
            if (_pendingChoices > 0)
                _showReveal = false;
            _menuOpen = true;
            CaptureCursor();
        }

        private static void CloseMenu()
        {
            _menuOpen = false;
            ReleaseCursor();
        }

        private static void ShuffleSlots()
        {
            SyncF9OwnedFromWeXon();

            // Pool: three core boosts + F9 support (no carrier) + 30 extra kinds.
            // Locked advanced/strategic always eligible; unlocked advanced only from reward 3+.
            List<BoostKind> pool = new List<BoostKind>(40);
            pool.Add(BoostKind.AirframeDiscount);
            pool.Add(BoostKind.FreeMissiles);
            pool.Add(BoostKind.AiRankRestrict);
            if (CanOfferF9AdvancedInPool())
                pool.Add(BoostKind.F9Advanced);
            if (IsF9StillLocked(F9UnlockStrategic))
                pool.Add(BoostKind.F9Strategic);
            KillChoiceRewardService.AddExtraKinds(pool);

            // Fisher–Yates on pool, then take first 3 for the mystery slots.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                BoostKind tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }
            for (int s = 0; s < 3; s++)
            {
                if (s < pool.Count)
                    _slotOrder[s] = pool[s];
                else
                    _slotOrder[s] = BoostKind.AirframeDiscount;
            }
        }

        private static bool CanOfferF9AdvancedInPool()
        {
            if (IsF9StillLocked(F9UnlockAdvanced))
                return true;
            // Already unlocked: only from the 3rd reward onward.
            return _rewardsEarned >= MinRewardForUnlockedAdvanced;
        }

        private static bool IsF9StillLocked(string key)
        {
            if (string.Equals(key, F9UnlockAdvanced, StringComparison.OrdinalIgnoreCase))
            {
                if (_f9AdvancedOwned)
                    return false;
            }
            else if (string.Equals(key, F9UnlockStrategic, StringComparison.OrdinalIgnoreCase))
            {
                if (_f9StrategicOwned)
                    return false;
            }
            else
                return false;

            if (QueryF9HasUnlock(key))
            {
                MarkF9Owned(key);
                return false;
            }
            return true;
        }

        private static void MarkF9Owned(string key)
        {
            if (string.Equals(key, F9UnlockAdvanced, StringComparison.OrdinalIgnoreCase))
                _f9AdvancedOwned = true;
            else if (string.Equals(key, F9UnlockStrategic, StringComparison.OrdinalIgnoreCase))
                _f9StrategicOwned = true;
        }

        private static void SyncF9OwnedFromWeXon()
        {
            if (QueryF9HasUnlock(F9UnlockAdvanced))
                _f9AdvancedOwned = true;
            if (QueryF9HasUnlock(F9UnlockStrategic))
                _f9StrategicOwned = true;
        }

        private static void EnsureKillAccoladesReflection()
        {
            if (_killAccoladesType != null)
                return;
            try
            {
                _killAccoladesType = Assembly.GetExecutingAssembly().GetType("WeXon.KillAccolades");
                if (_killAccoladesType == null)
                {
                    Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < asms.Length; i++)
                    {
                        try
                        {
                            _killAccoladesType = asms[i].GetType("WeXon.KillAccolades");
                            if (_killAccoladesType != null)
                                break;
                        }
                        catch { }
                    }
                }
                if (_killAccoladesType == null)
                    return;
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _hasUnlockMethod = _killAccoladesType.GetMethod("HasUnlock", flags);
                _grantUnlockMethod = _killAccoladesType.GetMethod("TryGrantUnlockFromOutside", flags);
                _describeUnlockMethod = _killAccoladesType.GetMethod("DescribeUnlockForUi", flags);
            }
            catch
            {
                _killAccoladesType = null;
            }
        }

        private static bool QueryF9HasUnlock(string key)
        {
            EnsureKillAccoladesReflection();
            if (_hasUnlockMethod == null)
                return false;
            try
            {
                object r = _hasUnlockMethod.Invoke(null, new object[] { key });
                if (r == null)
                    return false;
                return Convert.ToBoolean(r);
            }
            catch
            {
                return false;
            }
        }

        private static string QueryF9Describe(string key)
        {
            EnsureKillAccoladesReflection();
            if (_describeUnlockMethod == null)
                return null;
            try
            {
                return _describeUnlockMethod.Invoke(null, new object[] { key }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static void TryClearF9ArsenalCooldown()
        {
            try
            {
                if (_clearArsenalCooldownMethod == null)
                {
                    Type t = Assembly.GetExecutingAssembly().GetType("WeXon.StrategicArsenal");
                    if (t == null)
                    {
                        Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                        for (int i = 0; i < asms.Length; i++)
                        {
                            try
                            {
                                t = asms[i].GetType("WeXon.StrategicArsenal");
                                if (t != null)
                                    break;
                            }
                            catch { }
                        }
                    }
                    if (t != null)
                    {
                        _clearArsenalCooldownMethod = t.GetMethod("ClearCooldownFromOutside",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
                if (_clearArsenalCooldownMethod != null)
                    _clearArsenalCooldownMethod.Invoke(null, null);
            }
            catch { }
        }

        private static bool TryGrantF9Unlock(string key, out string describe)
        {
            describe = null;
            // Already owned → never grant / never treat as a fresh unlock.
            if (!IsF9StillLocked(key))
                return false;

            EnsureKillAccoladesReflection();
            if (_grantUnlockMethod == null)
                return false;
            try
            {
                object ok = _grantUnlockMethod.Invoke(null, new object[] { key });
                if (ok == null || !Convert.ToBoolean(ok))
                    return false;
                MarkF9Owned(key);
                describe = QueryF9Describe(key);
                if (string.IsNullOrEmpty(describe))
                    describe = key;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DrawCornerHint()
        {
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            Player local = null;
            try { GameManager.GetLocalPlayer(out local); }
            catch { }
            if (local == null)
                return;

            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF6);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            bool ready = _pendingChoices > 0;
            GUI.color = _menuOpen
                ? new Color(0.95f, 0.8f, 0.35f, 0.95f)
                : (ready
                    ? new Color(1f, 0.55f, 0.25f, 0.95f)
                    : new Color(0.7f, 0.85f, 0.55f, 0.9f));
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string line;
            if (_menuOpen)
                line = UiLang.T("F6 BOOST  |  OPEN", "F6 击杀奖励  |  已打开");
            else if (ready)
                line = UiLang.T("F6 BOOST  |  READY x" + _pendingChoices,
                    "F6 击杀奖励  |  可选 x" + _pendingChoices);
            else
                line = UiLang.T(
                    "F6 BOOST  |  " + _missionAirKills + "/" + NextRewardKillThreshold(),
                    "F6 击杀奖励  |  " + _missionAirKills + "/" + NextRewardKillThreshold());
            _chipStyle.normal.textColor = new Color(0.9f, 0.98f, 0.85f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipStyle);
            GUI.color = prev;
        }

        private static void DrawMenu()
        {
            Rect box = AssistMenuLayoutService.KillChoiceMenuRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.7f, 0.35f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 10f, box.width - 32f, 24f),
                UiLang.T("KILL BOOST  (F6)", "击杀奖励（F6）"), _titleStyle);

            string progress = UiLang.T(
                "Air kills " + _missionAirKills + "  ·  next at " + NextRewardKillThreshold()
                    + "  ·  pending " + _pendingChoices,
                "空中击杀 " + _missionAirKills + "  ·  下次 " + NextRewardKillThreshold()
                    + "  ·  待选 " + _pendingChoices);
            GUI.Label(new Rect(box.x + 16f, box.y + 36f, box.width - 32f, 28f), progress, _labelStyle);

            float y = box.y + 70f;
            float bw = box.width - 32f;
            float bh = 56f;

            // After a pick: reveal what was chosen (options stay hidden until then).
            if (_showReveal && !string.IsNullOrEmpty(_lastReveal))
            {
                GUI.color = new Color(0.12f, 0.2f, 0.12f, 0.95f);
                GUI.DrawTexture(new Rect(box.x + 16f, y, bw, 72f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(box.x + 24f, y + 8f, bw - 16f, 22f),
                    UiLang.T("YOU UNLOCKED", "你获得了"), _titleStyle);
                GUI.Label(new Rect(box.x + 24f, y + 34f, bw - 16f, 32f), _lastReveal, _labelStyle);
                y += 84f;
            }

            // Mystery picks — content hidden; order reshuffled every OpenMenu.
            if (_pendingChoices > 0 && !_showReveal)
            {
                GUI.Label(new Rect(box.x + 16f, y, bw, 28f),
                    UiLang.T("Pick one mystery boost (order is random each open).",
                        "选择一个神秘奖励（每次打开顺序随机）。"),
                    _labelStyle);
                y += 32f;

                string[] letters = { "A", "B", "C" };
                for (int i = 0; i < 3; i++)
                {
                    string mystery = UiLang.T(
                        "???  BOOST " + letters[i],
                        "???  奖励 " + letters[i]);
                    int captured = i;
                    DrawOption(new Rect(box.x + 16f, y, bw, bh), mystery, true, delegate
                    {
                        PickSlot(captured);
                    });
                    y += bh + 8f;
                }
            }
            else if (_pendingChoices > 0 && _showReveal && CanShowDrawNext())
            {
                if (GUI.Button(new Rect(box.x + 16f, y, bw, 36f),
                    UiLang.T("DRAW NEXT (" + _pendingChoices + " left)",
                        "抽取下一个（剩余 " + _pendingChoices + "）"), _btnStyle))
                {
                    _showReveal = false;
                    ShuffleSlots();
                }
                y += 44f;
            }
            else if (_pendingChoices > 0 && _showReveal && !CanShowDrawNext())
            {
                GUI.Label(new Rect(box.x + 16f, y, bw, 40f),
                    UiLang.T("Close and reopen F6 to claim remaining picks.",
                        "关闭后重新打开 F6 以领取剩余奖励。"),
                    _labelStyle);
                y += 44f;
            }
            else if (!_showReveal)
            {
                GUI.Label(new Rect(box.x + 16f, y, bw, 40f),
                    UiLang.T("No pending choice. Shoot down aircraft to earn the next boost.",
                        "当前无待选奖励。击落敌机以解锁下一次。"),
                    _labelStyle);
                y += 44f;
            }

            // Only show armed buffs after at least one reveal this match (or when armed).
            string buffs = BuildActiveBuffsText();
            if (!string.IsNullOrEmpty(buffs))
                GUI.Label(new Rect(box.x + 16f, box.y + box.height - 96f, bw - 110f, 36f),
                    buffs, _labelStyle);

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
                GUI.Label(new Rect(box.x + 16f, box.y + box.height - 70f, bw - 110f, 24f),
                    _status, _labelStyle);

            if (GUI.Button(new Rect(box.x + box.width - 116f, box.y + box.height - 48f, 100f, 32f),
                UiLang.T("CLOSE", "关闭"), _btnStyle))
                CloseMenu();

            GUI.color = prev;
        }

        private static void DrawOption(Rect r, string label, bool enabled, Action onPick)
        {
            Color prev = GUI.color;
            GUI.color = enabled
                ? new Color(0.18f, 0.22f, 0.16f, 0.95f)
                : new Color(0.1f, 0.1f, 0.12f, 0.7f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.enabled = enabled;
            if (GUI.Button(r, label, _optStyle) && enabled && onPick != null)
                onPick();
            GUI.enabled = true;
        }

        private static string BuildActiveBuffsText()
        {
            List<string> parts = new List<string>(8);
            if (_airframeDiscountPending)
                parts.Add(UiLang.T("discount armed", "机体折扣待用"));
            if (_freeMissilesPending)
                parts.Add(UiLang.T("free missiles armed", "免费导弹待用"));
            if (_aiRestrictRemaining > 0)
                parts.Add(UiLang.T(
                    "AI restrict " + _aiRestrictRemaining + "× ≤r" + _aiMaxRank,
                    "AI 限机 " + _aiRestrictRemaining + "× ≤r" + _aiMaxRank));
            KillChoiceRewardService.AppendBuffLines(parts);
            if (parts.Count == 0)
                return null;
            return UiLang.T("Active: ", "已生效：") + string.Join(" · ", parts.ToArray());
        }

        private static void ConsumePending()
        {
            if (_pendingChoices > 0)
                _pendingChoices--;
        }

        /// <summary>
        /// DRAW NEXT when pending remains after a pick:
        /// - rewards 1–2: yes if stacked unclaimed
        /// - reward 3 exactly: never (close/reopen)
        /// - after 3rd: only if still stacking unclaimed
        /// </summary>
        private static bool CanShowDrawNext()
        {
            if (_pendingChoices <= 0)
                return false;
            if (_rewardsEarned <= MaxDrawNextRewardIndex)
                return true;
            if (_rewardsEarned == MinRewardForUnlockedAdvanced)
                return false;
            return true;
        }

        private static void PickSlot(int slotIndex)
        {
            if (_pendingChoices <= 0 || _showReveal)
                return;
            if (slotIndex < 0 || slotIndex >= _slotOrder.Length)
                return;
            ApplyBoost(_slotOrder[slotIndex]);
        }

        private static void ApplyBoost(BoostKind kind)
        {
            if (_pendingChoices <= 0)
                return;
            ConsumePending();

            if (kind == BoostKind.AirframeDiscount)
            {
                _airframeDiscountPending = true;
                _lastReveal = UiLang.T(
                    "Airframe sale −80% (next buy pays 20%)",
                    "机体折扣 −80%（下次购买只付 20%）");
            }
            else if (kind == BoostKind.FreeMissiles)
            {
                _freeMissilesPending = true;
                _lastReveal = UiLang.T(
                    "Free missiles on next flight",
                    "下次飞行导弹全部免费");
            }
            else if (kind == BoostKind.AiRankRestrict)
            {
                int rank = PeekPlayerAirframeRank();
                int max = rank - 2;
                if (max < 0)
                    max = 0;
                _aiRestrictRemaining = AiRestrictCount;
                _aiMaxRank = max;
                _lastReveal = UiLang.T(
                    "Enemy AI×" + AiRestrictCount + " next spawns ≤ rank " + max
                        + " (your rank−2)",
                    "敌方 AI 下 " + AiRestrictCount + " 次起飞 ≤ rank " + max
                        + "（你当前机体 rank−2）");
            }
            else if (kind == BoostKind.F9Advanced || kind == BoostKind.F9Strategic)
            {
                string key = kind == BoostKind.F9Advanced
                    ? F9UnlockAdvanced
                    : F9UnlockStrategic;

                // Unlocked advanced from reward 3+: refresh F9 arsenal cooldown (valid late prize).
                if (kind == BoostKind.F9Advanced
                    && !IsF9StillLocked(key)
                    && _rewardsEarned >= MinRewardForUnlockedAdvanced)
                {
                    MarkF9Owned(key);
                    TryClearF9ArsenalCooldown();
                    string ownedDesc = QueryF9Describe(key);
                    if (string.IsNullOrEmpty(ownedDesc))
                        ownedDesc = key;
                    _lastReveal = UiLang.T(
                        "F9 advanced ready: " + ownedDesc + " (cooldown cleared)",
                        "高级 F9 支援就绪：" + ownedDesc + "（冷却已清除）");
                }
                else if (!IsF9StillLocked(key))
                {
                    // Strategic (or advanced before reward 3) should not be in pool — refund.
                    _pendingChoices++; // undo ConsumePending
                    MarkF9Owned(key);
                    ShuffleSlots();
                    _showReveal = false;
                    SetStatus(UiLang.T(
                        "That F9 support is already unlocked — pick again.",
                        "该 F9 支援已解锁 — 请重新选择。"));
                    return;
                }
                else
                {
                    string desc;
                    if (TryGrantF9Unlock(key, out desc))
                    {
                        _lastReveal = UiLang.T(
                            "F9 support unlocked: " + desc,
                            "F9 支援已解锁：" + desc);
                    }
                    else
                    {
                        _airframeDiscountPending = true;
                        _lastReveal = UiLang.T(
                            "F9 unlock unavailable — airframe −80% instead.",
                            "F9 解锁不可用 — 改为机体 −80%。");
                    }
                }
            }
            else
            {
                string extra;
                if (KillChoiceRewardService.TryApply(kind, out extra)
                    && !string.IsNullOrEmpty(extra))
                {
                    _lastReveal = extra;
                }
                else
                {
                    _airframeDiscountPending = true;
                    _lastReveal = UiLang.T(
                        "Unknown boost — airframe −80% instead.",
                        "未知奖励 — 改为机体 −80%。");
                }
            }

            _showReveal = true;
            SetStatus(_lastReveal);
        }

        internal static void FlashFromReward(string s)
        {
            SetStatus(s);
        }

        internal static void AddPendingChoices(int n)
        {
            if (n <= 0)
                return;
            _pendingChoices += n;
        }

        internal static void ArmAiRestrict(int count, int rankMinus)
        {
            if (count <= 0)
                return;
            int rank = PeekPlayerAirframeRank();
            int max = rank - rankMinus;
            if (max < 0)
                max = 0;
            if (_aiRestrictRemaining > 0)
            {
                _aiRestrictRemaining += count;
                if (max < _aiMaxRank)
                    _aiMaxRank = max;
            }
            else
            {
                _aiRestrictRemaining = count;
                _aiMaxRank = max;
            }
        }

        internal static void ClearF9CooldownFromReward()
        {
            TryClearF9ArsenalCooldown();
        }

        private static int PeekPlayerAirframeRank()
        {
            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                {
                    AircraftDefinition def = ac.definition as AircraftDefinition;
                    if (def != null && def.aircraftParameters != null)
                        return def.aircraftParameters.rankRequired;
                }
            }
            catch { }
            return 0;
        }

        private static void SetStatus(string s)
        {
            _status = s ?? "";
            _statusUntil = Time.unscaledTime + 4f;
        }

        // ——— effect helpers used by Harmony ———

        private static float AirframeDealPayFraction()
        {
            if (KillChoiceRewardService.AirframeFreePending)
                return 0f;
            return AirframePayFraction;
        }

        private static void ConsumeAirframeDeal()
        {
            if (KillChoiceRewardService.AirframeFreePending)
                KillChoiceRewardService.ConsumeAirframeFree();
            else
                _airframeDiscountPending = false;
        }

        internal static bool TryApplyDiscountedPurchase(Player player, AircraftDefinition aircraftDef)
        {
            if (!AirframeDiscountPending || player == null || aircraftDef == null)
                return false;
            if (!Plugin.IsLocalHumanPlayer(player))
                return false;
            float full = 0f;
            try { full = aircraftDef.value; }
            catch { return false; }
            float frac = AirframeDealPayFraction();
            float cost = full * frac;
            if (cost < 0f)
                cost = 0f;
            try
            {
                if (player.OwnsAirframe(aircraftDef, true))
                    return false;
            }
            catch { }

            if (player.Allocation + 0.001f < cost)
                return false;
            try
            {
                if (cost > 0f)
                    player.AddAllocation(-cost);
                player.CreditAirframe(aircraftDef, 1, false);
                ConsumeAirframeDeal();
                if (frac <= 0.001f)
                {
                    SetStatus(UiLang.T("Airframe bought free.", "机体已免费购买。"));
                }
                else
                {
                    SetStatus(UiLang.T(
                        "Airframe bought at 20% (" + cost.ToString("0.00") + "M).",
                        "机体已按 20% 购买（" + cost.ToString("0.00") + "M）。"));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldZeroLoadoutValue(WeaponManager wm)
        {
            if (!_freeMissilesPending && !_freeMissilesSpawnActive)
                return false;
            return IsLocalWeaponManager(wm);
        }

        internal static void OnSpawnWeaponsBegin(WeaponManager wm)
        {
            if (_freeMissilesPending && IsLocalWeaponManager(wm))
                _freeMissilesSpawnActive = true;
        }

        internal static void OnSpawnWeaponsEnd(WeaponManager wm)
        {
            if (!_freeMissilesSpawnActive)
                return;
            if (!IsLocalWeaponManager(wm))
                return;
            _freeMissilesSpawnActive = false;
            _freeMissilesPending = false;
            SetStatus(UiLang.T("Free-missile sortie consumed.", "免费导弹出击已消耗。"));
        }

        internal static bool ShouldRewriteEnemyAiSpawn(Airbase airbase, Player player)
        {
            if (_aiRestrictRemaining <= 0 || player != null || airbase == null)
                return false;
            try
            {
                FactionHQ localHq = null;
                GameManager.GetLocalHQ(out localHq);
                FactionHQ spawnHq = airbase.CurrentHQ;
                if (localHq == null || spawnHq == null)
                    return false;
                return !object.ReferenceEquals(localHq, spawnHq);
            }
            catch
            {
                return false;
            }
        }

        internal static AircraftDefinition RewriteAiDefinition(AircraftDefinition proposed, Airbase airbase)
        {
            if (_aiRestrictRemaining <= 0)
                return proposed;
            int max = _aiMaxRank;
            if (proposed != null && proposed.aircraftParameters != null
                && proposed.aircraftParameters.rankRequired <= max)
                return proposed;

            AircraftDefinition pick = PickRestrictedAirframe(max, airbase, proposed);
            return pick != null ? pick : proposed;
        }

        internal static void OnAiSpawnAttempt(bool allowed)
        {
            if (!allowed || _aiRestrictRemaining <= 0)
                return;
            _aiRestrictRemaining--;
            if (_aiRestrictRemaining <= 0)
                SetStatus(UiLang.T("Enemy AI rank restrict finished.", "敌方 AI 限机已结束。"));
        }

        private static FieldInfo _wmAircraftField;

        private static Aircraft ReadWeaponManagerAircraft(WeaponManager wm)
        {
            if (wm == null)
                return null;
            try
            {
                if (_wmAircraftField == null)
                    _wmAircraftField = AccessTools.Field(typeof(WeaponManager), "aircraft");
                if (_wmAircraftField == null)
                    return null;
                return _wmAircraftField.GetValue(wm) as Aircraft;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsLocalWeaponManager(WeaponManager wm)
        {
            if (wm == null)
                return false;
            try
            {
                Aircraft ac = ReadWeaponManagerAircraft(wm);
                if (ac == null)
                    return false;
                Player p = ac.Player;
                return Plugin.IsLocalHumanPlayer(p);
            }
            catch
            {
                return false;
            }
        }

        private static AircraftDefinition PickRestrictedAirframe(int maxRank, Airbase airbase, AircraftDefinition prefer)
        {
            List<AircraftDefinition> exact = new List<AircraftDefinition>(16);
            List<AircraftDefinition> under = new List<AircraftDefinition>(32);
            try
            {
                Encyclopedia enc = Encyclopedia.i;
                if (enc == null || enc.aircraft == null)
                    return prefer;
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition d = enc.aircraft[i];
                    if (d == null || d.aircraftParameters == null)
                        continue;
                    int r = d.aircraftParameters.rankRequired;
                    if (r > maxRank)
                        continue;
                    bool can = true;
                    try
                    {
                        if (airbase != null)
                            can = airbase.CanSpawnAircraft(d);
                    }
                    catch { }
                    if (!can)
                        continue;
                    if (r == maxRank)
                        exact.Add(d);
                    else
                        under.Add(d);
                }
            }
            catch { }

            if (exact.Count > 0)
                return exact[UnityEngine.Random.Range(0, exact.Count)];
            if (under.Count > 0)
                return under[UnityEngine.Random.Range(0, under.Count)];
            return prefer;
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _titleStyle.normal.textColor = new Color(1f, 0.92f, 0.7f, 1f);
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            _labelStyle.normal.textColor = new Color(0.8f, 0.88f, 0.78f, 0.95f);
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            _chipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            _optStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
        }

        private static void CaptureCursor()
        {
            if (_cursorHeld)
                return;
            OritasyCursor.Hold();
            _cursorHeld = true;
        }

        private static void HoldCursor()
        {
            if (!_cursorHeld)
                CaptureCursor();
            OritasyCursor.Pulse();
        }

        private static void ReleaseCursor()
        {
            if (!_cursorHeld)
                return;
            OritasyCursor.Release();
            _cursorHeld = false;
        }

        // ——— Harmony ———

        [HarmonyPatch(typeof(FactionHQ), "ReportKillAction")]
        private static class ReportKillPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player player, Unit target)
            {
                try { NoteLocalAirKill(player, target); }
                catch { }
            }
        }

        [HarmonyPatch]
        private static class PurchaseAirframePatch
        {
            private static bool Prepare()
            {
                return FindPlayerUserCode("UserCode_CmdPurchaseAirframe") != null;
            }

            private static MethodBase TargetMethod()
            {
                return FindPlayerUserCode("UserCode_CmdPurchaseAirframe");
            }

            [HarmonyPrefix]
            private static bool Prefix(Player __instance, AircraftDefinition aircraftDef)
            {
                try
                {
                    if (TryApplyDiscountedPurchase(__instance, aircraftDef))
                        return false;
                }
                catch { }
                return true;
            }
        }

        [HarmonyPatch(typeof(AircraftInventoryMenu), "BuyAirframe")]
        private static class BuyAirframeUiPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                AircraftInventoryMenu __instance,
                AircraftDefinition ___selectedType,
                Player ___localPlayer)
            {
                if (!AirframeDiscountPending || __instance == null)
                    return true;
                try
                {
                    AircraftDefinition def = ___selectedType;
                    Player local = ___localPlayer;
                    if (def == null || local == null)
                        return true;
                    float cost = def.value * AirframeDealPayFraction();
                    if (local.Allocation + 0.001f < cost)
                        return true;
                    local.CmdPurchaseAirframe(def);
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(AircraftInventoryMenu), "UpdateBuyButton")]
        private static class UpdateBuyButtonPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                AircraftInventoryMenu __instance,
                Button ___buyButton,
                AircraftDefinition ___selectedType,
                Player ___localPlayer)
            {
                if (!AirframeDiscountPending || __instance == null || ___buyButton == null)
                    return;
                try
                {
                    AircraftDefinition def = ___selectedType;
                    Player local = ___localPlayer;
                    if (def == null || local == null)
                        return;
                    float cost = def.value * AirframeDealPayFraction();
                    if (local.Allocation + 0.001f >= cost)
                        ___buyButton.interactable = true;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(WeaponManager), "GetCurrentValue")]
        private static class GetCurrentValuePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(WeaponManager __instance, bool includeCargo, ref float __result)
            {
                if (!ShouldZeroLoadoutValue(__instance))
                    return true;
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
        private static class SpawnWeaponsPatch
        {
            [HarmonyPrefix]
            private static void Prefix(WeaponManager __instance)
            {
                OnSpawnWeaponsBegin(__instance);
            }

            [HarmonyPostfix]
            private static void Postfix(WeaponManager __instance)
            {
                OnSpawnWeaponsEnd(__instance);
            }
        }

        [HarmonyPatch(typeof(Airbase), "TrySpawnAircraft")]
        private static class AirbaseAiSpawnPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Airbase __instance, Player player, ref AircraftDefinition definition, ref bool __state)
            {
                __state = false;
                if (!ShouldRewriteEnemyAiSpawn(__instance, player))
                    return;
                definition = RewriteAiDefinition(definition, __instance);
                __state = true;
            }

            [HarmonyPostfix]
            private static void Postfix(Airbase.TrySpawnResult __result, bool __state)
            {
                if (__state && __result.Allowed)
                    OnAiSpawnAttempt(true);
            }
        }

        private static MethodBase FindPlayerUserCode(string nameContains)
        {
            try
            {
                MethodInfo[] methods = typeof(Player).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m != null && m.Name != null
                        && m.Name.IndexOf(nameContains, StringComparison.Ordinal) >= 0)
                        return m;
                }
            }
            catch { }
            return null;
        }
    }
}
