using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Gear-down: nearest enemy land airbase (AAA/SAM and aircraft on/near the
    /// field) holds fire at the local aircraft.
    /// Land there: defect to that faction, keep 10% funds, spend 90% on the
    /// original faction (ground convoys first, leftover as faction funds).
    /// Vanilla SetFaction refuses mid-match switches — host uses RemovePlayer
    /// + ServerApplyFaction. Solo / listen-host only for the defect step.
    /// </summary>
    internal static class AirportDefection
    {
        private const float KeepFraction = 0.10f;
        private const float ScanInterval = 0.25f;
        /// <summary>Gear down: enemies of that field within this range of the player hold fire.</summary>
        private const float HoldFireRangeM = 6000f;
        /// <summary>Show defectable-airfield marker with gear up or down.</summary>
        private const float DisplayRangeM = 15000f;

        private static readonly MethodInfo UnitSetHq =
            AccessTools.Method(typeof(Unit), "SetHQ", new Type[] { typeof(FactionHQ) });
        private static readonly PropertyInfo UnitNetworkHq =
            AccessTools.Property(typeof(Unit), "NetworkHQ");
        private static readonly MethodInfo ServerApplyFaction =
            AccessTools.Method(typeof(Player), "ServerApplyFaction", new Type[] { typeof(FactionHQ) });
        private static readonly MethodInfo PlayerHqChanged =
            AccessTools.Method(typeof(Player), "HQChanged", Type.EmptyTypes);
        private static readonly MethodInfo ObjectiveInitList =
            AccessTools.Method(typeof(ObjectiveInfoList), "InitializeObjectiveList");
        private static readonly FieldInfo ObjectiveInitFlag =
            AccessTools.Field(typeof(ObjectiveInfoList), "objectiveInitialized");
        private static readonly FieldInfo ObjectiveEntries =
            AccessTools.Field(typeof(ObjectiveInfoList), "listObjectives");
        private static readonly FieldInfo CombatHudMarkers =
            AccessTools.Field(typeof(CombatHUD), "markers");
        private static readonly MethodInfo HudSetFactionColor =
            AccessTools.Method(typeof(HUDUnitMarker), "SetFactionColor");
        private static readonly MethodInfo HudSetFactionIcon =
            AccessTools.Method(typeof(HUDUnitMarker), "SetFactionIcon");
        private static readonly MethodInfo HudUpdateColor =
            AccessTools.Method(typeof(HUDUnitMarker), "UpdateColor");
        private static readonly FieldInfo GameplayHomeAirbase =
            AccessTools.Field(typeof(GameplayUI), "homeAirbase");

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _holdFire;
        private static ConfigEntry<bool> _defectOnLand;

        private static float _nextScan;
        private static Airbase _enemyBase;
        private static FactionHQ _enemyHq;
        private static Vector3 _enemyCenter;
        private static float _landRadiusM;
        private static bool _gearDown;
        private static bool _holdActive;
        private static bool _showMarker;
        private static bool _insideFriendly;
        private static bool _nearEnemy;
        private static bool _landedInside;
        private static bool _defectedThisSit;
        private static bool _hostBlocked;
        private static string _hudSuccessName = "";
        private static FactionHQ _betrayedHq;
        private static readonly List<FactionHQ> _betrayedHqs = new List<FactionHQ>(4);
        private static int _defectCount;
        private static string _defectFieldName = "";
        private static string _traitorFeedText = "";
        private static int _traitorFeedLeft;
        private static float _nextTraitorFeed;
        private static GUIStyle _chipStyle;
        private static GUIStyle _markStyle;

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("AirportDefection", "Enabled", true,
                "Nearest enemy land airport: marker within 15 km (gear up or down). Gear down within 6 km: that faction holds fire. Land there to defect (keep 10%, 90% funds original faction).");
            _holdFire = config.Bind("AirportDefection", "HoldFireOnGearDown", true,
                "With landing gear down, enemies of that airfield within 6 km of you will not fire.");
            _defectOnLand = config.Bind("AirportDefection", "DefectOnLanding", true,
                "Landing at that enemy airport defects you to their faction (host/listen-server).");
        }

        internal static void Tick()
        {
            PumpTraitorKillFeed();
            if (_enabled != null && !_enabled.Value)
            {
                ClearScan();
                return;
            }

            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            if (ac == null)
            {
                ClearScan();
                _defectedThisSit = false;
                _hudSuccessName = "";
                _hostBlocked = false;
                ClearBetrayalMemory();
                return;
            }
            if (LocalPilotUnavailable(ac))
            {
                _holdActive = false;
                _showMarker = false;
                _nearEnemy = false;
                _landedInside = false;
                _hudSuccessName = "";
                return;
            }

            _gearDown = GearIsDown(ac);
            bool landed = false;
            try { landed = ac.IsLanded(); }
            catch
            {
                try { landed = ac.radarAlt < 2.5f && ac.speed < 18f; }
                catch { }
            }

            if (!landed)
            {
                float ralt = 0f;
                try { ralt = ac.radarAlt; }
                catch { }
                if (ralt > 25f)
                {
                    _defectedThisSit = false;
                    _hudSuccessName = "";
                    _hostBlocked = false;
                }
            }

            if (Time.unscaledTime >= _nextScan || _enemyBase == null)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                RefreshNearestEnemy(ac);
            }

            if (_enemyBase == null || _enemyHq == null)
            {
                _holdActive = false;
                _showMarker = false;
                _nearEnemy = false;
                _landedInside = false;
                return;
            }

            Vector3 pos = ac.transform.position;
            float horiz = Horizontal(_enemyCenter, pos);
            _showMarker = !_insideFriendly && horiz <= DisplayRangeM;
            _nearEnemy = !_insideFriendly && horiz <= HoldFireRangeM;
            _holdActive = (_holdFire == null || _holdFire.Value)
                && _gearDown && _nearEnemy;
            _landedInside = landed && horiz <= _landRadiusM;
            if (_landedInside && _gearDown)
                TryDefect(ac);
        }

        internal static void Draw()
        {
            if (!Plugin.AllowThirdPersonUi)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            if (_enabled != null && !_enabled.Value)
                return;

            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { }
            if (LocalPilotUnavailable(local))
                return;

            EnsureStyles();
            if (!string.IsNullOrEmpty(_hudSuccessName))
            {
                DrawChip(UiLang.T(
                    "Defected at " + _hudSuccessName,
                    "于" + _hudSuccessName + "叛变成功"),
                    new Color(1f, 0.82f, 0.28f, 0.95f));
                return;
            }
            if (_showMarker && _enemyBase != null && !_insideFriendly)
            {
                string name = AirbaseDisplayName(_enemyBase);
                string state;
                if (_hostBlocked)
                    state = UiLang.T("host only", "需房主");
                else if (_landedInside)
                    state = UiLang.T("defecting", "叛变中");
                else
                    state = UiLang.T("ready", "可叛变");
                Color accent = _landedInside
                    ? new Color(1f, 0.82f, 0.28f, 0.95f)
                    : new Color(1f, 0.78f, 0.18f, 0.95f);
                DrawChip(name + "  |  " + state, accent);
                DrawWorldMarker(state, accent);
            }
        }

        internal static bool ShouldHoldFire(Turret turret, Unit target)
        {
            if (turret == null)
                return false;
            Unit shooter = null;
            try { shooter = turret.GetAttachedUnit(); }
            catch { }
            if (shooter == null)
            {
                try { shooter = turret.GetComponentInParent<Unit>(); }
                catch { }
            }
            return ShouldHoldFire(shooter, target);
        }

        internal static bool ShouldHoldFire(Unit shooter, Unit target)
        {
            if (!_holdActive || shooter == null || target == null)
                return false;
            if (_enemyHq == null || _enemyBase == null)
                return false;

            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { }
            if (local == null || !object.ReferenceEquals(target, local))
                return false;

            if (object.ReferenceEquals(shooter, local))
                return false;

            FactionHQ shq = null;
            try { shq = shooter.NetworkHQ; }
            catch { }
            if (shq == null || !object.ReferenceEquals(shq, _enemyHq))
                return false;

            Vector3 p;
            try { p = shooter.transform.position; }
            catch { return false; }
            Vector3 lp;
            try { lp = local.transform.position; }
            catch { return false; }
            return Horizontal(lp, p) <= HoldFireRangeM;
        }

        /// <summary>Vanilla / extra combat FSM: drop lock on the local jet while the field holds fire.</summary>
        internal static void ClearHeldCombatTarget(PilotBaseState state, FieldInfo aircraftField, FieldInfo targetField)
        {
            if (state == null || aircraftField == null || targetField == null)
                return;
            Aircraft ac = null;
            Unit t = null;
            try { ac = aircraftField.GetValue(state) as Aircraft; }
            catch { }
            try { t = targetField.GetValue(state) as Unit; }
            catch { }
            if (ac == null || t == null)
                return;
            if (!ShouldHoldFire(ac, t))
                return;
            try { targetField.SetValue(state, null); }
            catch { }
        }

        private static void TryDefect(Aircraft ac)
        {
            if (_defectOnLand != null && !_defectOnLand.Value)
                return;
            if (_defectedThisSit || ac == null || _enemyHq == null)
                return;

            Player player = null;
            try { GameManager.GetLocalPlayer(out player); }
            catch { }
            if (player == null || !Plugin.IsLocalHumanPlayer(player))
                return;

            FactionHQ oldHq = null;
            try { oldHq = player.HQ; }
            catch { }
            if (oldHq == null)
            {
                try { oldHq = ac.NetworkHQ; }
                catch { }
            }
            if (oldHq == null || SameFactionHq(oldHq, _enemyHq))
                return;

            bool host = false;
            try { host = player.IsServer; }
            catch { }
            if (!host)
            {
                try
                {
                    NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                    host = nm != null && nm.Server != null && nm.Server.Active;
                }
                catch { }
            }
            if (!host)
            {
                _hostBlocked = true;
                _defectedThisSit = true;
                return;
            }

            _defectedThisSit = true;
            float total = 0f;
            try { total = player.Allocation; }
            catch { }
            if (total < 0f)
                total = 0f;
            float keep = total * KeepFraction;
            int convoys = SpendOnOriginalFaction(player, oldHq, total - keep);

            try { player.SetAllocation(0f); }
            catch { }

            try { oldHq.RemovePlayer(player); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("AirportDefection RemovePlayer: " + ex.Message);
            }

            try
            {
                if (ServerApplyFaction != null)
                    ServerApplyFaction.Invoke(player, new object[] { _enemyHq });
                else
                    player.SetFaction(_enemyHq, true);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("AirportDefection ServerApplyFaction: " + ex.Message);
                try { player.SetFaction(_enemyHq, true); }
                catch { }
            }

            try { player.SetAllocation(keep); }
            catch
            {
                try { player.AddAllocation(keep); }
                catch { }
            }

            ApplyUnitHq(ac, _enemyHq);
            RefreshLocalFactionView(player, ac, _enemyHq, _enemyBase);
            _showMarker = false;
            _holdActive = false;
            _nearEnemy = false;
            _nextScan = 0f;

            RecordBetrayedHq(oldHq);
            _betrayedHq = oldHq;
            _defectCount++;
            _defectFieldName = AirbaseDisplayName(_enemyBase);
            _hudSuccessName = _defectFieldName;
            QueueTraitorKillFeed();
            AceRadioChatter.NotifyDefected();
            string dest = FactionName(_enemyHq);
            string src = FactionName(oldHq);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("AirportDefection: " + src + " -> " + dest
                    + " keep=" + keep.ToString("0.0") + " convoys=" + convoys.ToString()
                    + " at " + _hudSuccessName);
        }

        internal static bool TraitorRadioActive
        {
            get { return _defectCount > 0; }
        }

        internal static int DefectHopCount
        {
            get { return _defectCount; }
        }

        internal static string DefectFieldName
        {
            get { return _defectFieldName; }
        }

        internal static bool IsBetrayedFaction(Unit unit)
        {
            return IsHqBetrayed(UnitHq(unit));
        }

        internal static bool IsLastLeftFaction(Unit unit)
        {
            return SameFactionHq(UnitHq(unit), _betrayedHq);
        }

        /// <summary>
        /// 0 none, 1 first hostile, 2 switched again (hostile), 3 came back (ally).
        /// </summary>
        internal static int TraitorTone(Unit unit)
        {
            if (unit == null || _defectCount <= 0 || !IsBetrayedFaction(unit))
                return 0;
            bool ally = SameFactionHq(UnitHq(unit), LocalHq());
            if (ally)
                return 3;
            if (_defectCount <= 1)
                return 1;
            return 2;
        }

        private static void RecordBetrayedHq(FactionHQ hq)
        {
            if (hq == null || IsHqBetrayed(hq))
                return;
            _betrayedHqs.Add(hq);
        }

        private static bool IsHqBetrayed(FactionHQ hq)
        {
            if (hq == null)
                return false;
            if (SameFactionHq(hq, _betrayedHq))
                return true;
            for (int i = 0; i < _betrayedHqs.Count; i++)
            {
                if (SameFactionHq(hq, _betrayedHqs[i]))
                    return true;
            }
            return false;
        }

        private static void ClearBetrayalMemory()
        {
            _betrayedHq = null;
            _betrayedHqs.Clear();
            _defectCount = 0;
            _defectFieldName = "";
            _traitorFeedText = "";
        }

        private static FactionHQ UnitHq(Unit unit)
        {
            if (unit == null)
                return null;
            FactionHQ hq = null;
            try { hq = unit.NetworkHQ; }
            catch { }
            if (hq == null)
            {
                try { hq = unit.MapHQ; }
                catch { }
            }
            return hq;
        }

        private static FactionHQ LocalHq()
        {
            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            if (ac != null)
            {
                FactionHQ hq = UnitHq(ac);
                if (hq != null)
                    return hq;
            }
            FactionHQ local = null;
            try { GameManager.GetLocalHQ(out local); }
            catch { }
            return local;
        }

        private static void QueueTraitorKillFeed()
        {
            if (_defectCount >= 2)
                _traitorFeedText = "<color=#8B0000ff><b>又叛变了！</b></color>";
            else
                _traitorFeedText = "<color=#8B0000ff><b>叛徒！</b></color>";
            _traitorFeedLeft = 3;
            _nextTraitorFeed = 0f;
            PumpTraitorKillFeed();
        }

        private static void PumpTraitorKillFeed()
        {
            if (_traitorFeedLeft <= 0)
                return;
            if (string.IsNullOrEmpty(_traitorFeedText))
                return;
            if (Time.unscaledTime < _nextTraitorFeed)
                return;
            try
            {
                GameplayUI ui = SceneSingleton<GameplayUI>.i;
                if (ui != null)
                    ui.KillFeed(_traitorFeedText);
            }
            catch { }
            _traitorFeedLeft--;
            _nextTraitorFeed = Time.unscaledTime + 0.12f;
        }

        private static int SpendOnOriginalFaction(Player player, FactionHQ oldHq, float budget)
        {
            int bought = 0;
            if (player == null || oldHq == null || budget < 0.05f)
                return 0;

            List<Faction.ConvoyGroup> groups = null;
            try
            {
                if (oldHq.faction != null)
                    groups = oldHq.faction.GetConvoyGroups();
            }
            catch { }

            if (groups != null && groups.Count > 0)
            {
                List<int> order = new List<int>(groups.Count);
                for (int i = 0; i < groups.Count; i++)
                    order.Add(i);
                order.Sort(delegate(int a, int b)
                {
                    float ca = SafeCost(groups, a);
                    float cb = SafeCost(groups, b);
                    if (ca < cb)
                        return 1;
                    if (ca > cb)
                        return -1;
                    return 0;
                });

                bool progressed = true;
                while (progressed)
                {
                    progressed = false;
                    for (int n = 0; n < order.Count; n++)
                    {
                        int idx = order[n];
                        float cost = SafeCost(groups, idx);
                        if (cost < 0.05f || cost > budget + 0.001f)
                            continue;
                        float have = 0f;
                        try { have = player.Allocation; }
                        catch { }
                        if (have + 0.001f < cost)
                            continue;
                        if (ConvoyOnCooldown(oldHq, idx))
                            continue;
                        try
                        {
                            player.CmdPurchaseConvoy(idx);
                            budget -= cost;
                            bought++;
                            progressed = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (Plugin.Log != null)
                                Plugin.Log.LogWarning("AirportDefection convoy: " + ex.Message);
                        }
                    }
                }
            }

            float leftover = budget;
            float haveNow = 0f;
            try { haveNow = player.Allocation; }
            catch { }
            float keepFloor = haveNow - leftover;
            if (keepFloor < 0f)
                keepFloor = 0f;
            if (leftover > 0.05f && haveNow > keepFloor + 0.05f)
            {
                float donate = haveNow - keepFloor;
                if (donate > leftover)
                    donate = leftover;
                try
                {
                    player.AddAllocation(-donate);
                    oldHq.AddBonusFunds(donate);
                }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("AirportDefection funds: " + ex.Message);
                }
            }
            return bought;
        }

        private static bool ConvoyOnCooldown(FactionHQ hq, int index)
        {
            if (hq == null || index < 0 || index > 255)
                return false;
            try
            {
                float delay = hq.CmdGetDelaySpawnConvoy((byte)index);
                return delay > 0.5f;
            }
            catch
            {
                return false;
            }
        }

        private static float SafeCost(List<Faction.ConvoyGroup> groups, int i)
        {
            if (groups == null || i < 0 || i >= groups.Count || groups[i] == null)
                return 0f;
            try { return groups[i].GetCost(); }
            catch { return 0f; }
        }

        private static void ApplyUnitHq(Unit unit, FactionHQ hq)
        {
            if (unit == null || hq == null)
                return;
            if (UnitSetHq != null)
            {
                try { UnitSetHq.Invoke(unit, new object[] { hq }); return; }
                catch { }
            }
            if (UnitNetworkHq != null && UnitNetworkHq.CanWrite)
            {
                try { UnitNetworkHq.SetValue(unit, hq, null); }
                catch { }
            }
        }

        /// <summary>
        /// Vanilla join binds the map / objectives / IFF to DynamicMap.HQ once
        /// (Aircraft.OnStartClient). Mid-match ServerApplyFaction does not.
        /// </summary>
        private static void RefreshLocalFactionView(Player player, Aircraft ac, FactionHQ hq, Airbase home)
        {
            if (hq == null)
                return;

            if (PlayerHqChanged != null && player != null)
            {
                try { PlayerHqChanged.Invoke(player, null); }
                catch { }
            }

            try
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null)
                    map.SetFaction(hq);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("AirportDefection SetFaction map: " + ex.Message);
            }

            RebuildObjectiveList();
            RecolorHudMarkers();
            PersistPlayerSave(player);

            if (home != null && GameplayHomeAirbase != null)
            {
                try
                {
                    GameplayUI ui = SceneSingleton<GameplayUI>.i;
                    if (ui != null)
                        GameplayHomeAirbase.SetValue(ui, home);
                }
                catch { }
            }
        }

        private static void RebuildObjectiveList()
        {
            ObjectiveInfoList list = null;
            try { list = SceneSingleton<ObjectiveInfoList>.i; }
            catch { }
            if (list == null)
                return;

            if (ObjectiveEntries != null)
            {
                try
                {
                    System.Collections.IList entries = ObjectiveEntries.GetValue(list) as System.Collections.IList;
                    if (entries != null)
                    {
                        for (int i = entries.Count - 1; i >= 0; i--)
                        {
                            ObjectiveInfoList_ObjEntry entry = entries[i] as ObjectiveInfoList_ObjEntry;
                            if (entry != null)
                            {
                                try { UnityEngine.Object.Destroy(entry.gameObject); }
                                catch { }
                            }
                        }
                        entries.Clear();
                    }
                }
                catch { }
            }

            if (ObjectiveInitFlag != null)
            {
                try { ObjectiveInitFlag.SetValue(list, false); }
                catch { }
            }
            if (ObjectiveInitList != null)
            {
                try { ObjectiveInitList.Invoke(list, null); }
                catch { }
            }
        }

        private static void RecolorHudMarkers()
        {
            CombatHUD hud = null;
            try { hud = SceneSingleton<CombatHUD>.i; }
            catch { }
            if (hud == null || CombatHudMarkers == null)
                return;
            System.Collections.IList markers = null;
            try { markers = CombatHudMarkers.GetValue(hud) as System.Collections.IList; }
            catch { }
            if (markers == null)
                return;
            object[] none = null;
            for (int i = 0; i < markers.Count; i++)
            {
                HUDUnitMarker mark = markers[i] as HUDUnitMarker;
                if (mark == null)
                    continue;
                try
                {
                    if (HudSetFactionColor != null)
                        HudSetFactionColor.Invoke(mark, none);
                    if (HudSetFactionIcon != null)
                        HudSetFactionIcon.Invoke(mark, none);
                    if (HudUpdateColor != null)
                        HudUpdateColor.Invoke(mark, none);
                }
                catch { }
            }
        }

        private static void PersistPlayerSave(Player player)
        {
            if (player == null)
                return;
            try
            {
                MethodInfo getAuth = AccessTools.Method(typeof(BasePlayer), "GetAuthData");
                if (getAuth == null)
                    return;
                object auth = getAuth.Invoke(player, null);
                if (auth == null)
                    return;
                FieldInfo saveField = AccessTools.Field(auth.GetType(), "SaveData");
                if (saveField == null)
                    return;
                SavedPlayerData data = saveField.GetValue(auth) as SavedPlayerData;
                if (data != null)
                    data.Save(player);
            }
            catch { }
        }

        private static bool SameFactionHq(FactionHQ a, FactionHQ b)
        {
            if (a == null || b == null)
                return false;
            if (object.ReferenceEquals(a, b))
                return true;
            try
            {
                if (a.faction != null && b.faction != null)
                {
                    if (object.ReferenceEquals(a.faction, b.faction))
                        return true;
                    string na = a.faction.factionName;
                    string nb = b.faction.factionName;
                    if (!string.IsNullOrEmpty(na) && !string.IsNullOrEmpty(nb)
                        && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void RefreshNearestEnemy(Aircraft ac)
        {
            _enemyBase = null;
            _enemyHq = null;
            _insideFriendly = false;
            _landRadiusM = 800f;
            FactionHQ myHq = null;
            try { myHq = ac.NetworkHQ; }
            catch { }
            if (myHq == null)
            {
                try { GameManager.GetLocalHQ(out myHq); }
                catch { }
            }
            if (myHq == null)
            {
                try
                {
                    Player p;
                    GameManager.GetLocalPlayer(out p);
                    if (p != null)
                        myHq = p.HQ;
                }
                catch { }
            }

            Airbase[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Airbase>(); }
            catch { }
            if (all == null)
                return;

            Vector3 from = ac.transform.position;
            float best = float.MaxValue;
            Airbase pick = null;
            for (int i = 0; i < all.Length; i++)
            {
                Airbase ab = all[i];
                if (ab == null)
                    continue;
                try
                {
                    if (ab.disabled)
                        continue;
                }
                catch { }
                try
                {
                    if (ab.AttachedAirbase)
                        continue;
                }
                catch { }
                if (AirbaseLocator.IsCarrierAirbase(ab))
                    continue;
                FactionHQ hq = null;
                try { hq = ab.CurrentHQ; }
                catch { }
                if (hq == null)
                    continue;
                Vector3 c = CenterOf(ab);
                float d = Horizontal(from, c);
                if (myHq != null && SameFactionHq(hq, myHq))
                {
                    float landR = ResolveLandRadius(ab);
                    if (landR < 800f)
                        landR = 800f;
                    if (d <= landR * 1.25f)
                        _insideFriendly = true;
                    continue;
                }
                if (d < best)
                {
                    best = d;
                    pick = ab;
                    _enemyHq = hq;
                    _enemyCenter = c;
                }
            }
            _enemyBase = pick;
            if (pick != null)
                _landRadiusM = ResolveLandRadius(pick);
        }

        private static float ResolveLandRadius(Airbase ab)
        {
            float r = 0f;
            try { r = ab.GetRadius(); }
            catch { }
            try
            {
                ICapturable cap = ab as ICapturable;
                if (cap != null && cap.CaptureRange > r)
                    r = cap.CaptureRange;
            }
            catch { }
            if (r < 400f)
                r = 400f;
            if (r > 8000f)
                r = 8000f;
            return r;
        }

        private static Vector3 CenterOf(Airbase ab)
        {
            try
            {
                if (ab.center != null)
                    return ab.center.position;
            }
            catch { }
            try { return ab.transform.position; }
            catch { return Vector3.zero; }
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Wreck / eject / spectator: GetLocalAircraft often still returns the hull.
        /// </summary>
        private static bool LocalPilotUnavailable(Aircraft ac)
        {
            if (ac == null)
                return true;
            try
            {
                if (ac.disabled)
                    return true;
            }
            catch { }
            try
            {
                if (ac.pilots != null && ac.pilots.Length > 0)
                {
                    Pilot pl = ac.pilots[0];
                    if (pl != null && (pl.dead || pl.ejected))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool GearIsDown(Aircraft ac)
        {
            try
            {
                if (ac.gearDeployed)
                    return true;
            }
            catch { }
            try
            {
                return ac.gearState != LandingGear.GearState.LockedRetracted;
            }
            catch { }
            return false;
        }

        private static string AirbaseDisplayName(Airbase ab)
        {
            string name = null;
            try { name = AirbaseLocator.FormatAirbaseName(ab, false); }
            catch { }
            if (string.IsNullOrEmpty(name))
                return "?";
            return name;
        }

        private static string FactionName(FactionHQ hq)
        {
            try
            {
                if (hq != null && hq.faction != null && !string.IsNullOrEmpty(hq.faction.factionName))
                    return hq.faction.factionName;
            }
            catch { }
            return "?";
        }

        private static void ClearScan()
        {
            _holdActive = false;
            _showMarker = false;
            _gearDown = false;
            _insideFriendly = false;
            _nearEnemy = false;
            _landedInside = false;
            _enemyBase = null;
            _enemyHq = null;
        }

        private static void DrawChip(string line, Color accent)
        {
            float w = 420f;
            Rect chip = new Rect((UiScaleService.Width - w) * 0.5f, 42f, w, 26f);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(chip.x + 8f, chip.y, chip.width - 16f, chip.height), line, _chipStyle);
            GUI.color = prev;
        }

        private static void DrawWorldMarker(string state, Color gold)
        {
            Camera cam = ResolveViewCamera();
            if (cam == null)
                return;

            Vector3 world = _enemyCenter;
            world.y += 28f;
            Vector3 sp;
            try { sp = cam.WorldToScreenPoint(world); }
            catch { return; }

            float x = UiScaleService.FromScreenX(sp.x);
            float y = UiScaleService.FromScreenYFlipped(sp.y);
            bool behind = sp.z <= 0.08f;
            if (behind)
            {
                x = UiScaleService.Width - x;
                y = UiScaleService.Height - y;
            }

            float m = 40f;
            float maxX = UiScaleService.Width - m;
            float maxY = UiScaleService.Height - 56f;
            if (maxX < m)
                maxX = m;
            if (maxY < m)
                maxY = m;
            bool clamped = x < m || x > maxX || y < m || y > maxY || behind;
            x = Mathf.Clamp(x, m, maxX);
            y = Mathf.Clamp(y, m, maxY);

            float pulse = 0.78f + 0.22f * Mathf.PingPong(Time.unscaledTime * 2.2f, 1f);
            float size = clamped ? 16f : 20f;
            Color prev = GUI.color;

            GUI.color = new Color(gold.r, gold.g, gold.b, 0.22f * pulse);
            UiScaleService.DrawRotatedQuad(new Vector2(x, y), size + 14f, 45f);

            GUI.color = new Color(gold.r, gold.g, gold.b, 0.95f * pulse);
            UiScaleService.DrawRotatedQuad(new Vector2(x, y), size, 45f);

            GUI.color = new Color(1f, 0.95f, 0.65f, 0.95f);
            UiScaleService.DrawRotatedQuad(new Vector2(x, y), size * 0.42f, 45f);

            GUI.color = new Color(gold.r, gold.g, gold.b, 0.9f * pulse);
            GUI.DrawTexture(new Rect(x - 1.5f, y + size * 0.35f, 3f, 10f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 5f, y + size * 0.35f + 8f, 10f, 3f), Texture2D.whiteTexture);

            if (_markStyle != null)
            {
                _markStyle.normal.textColor = new Color(1f, 0.9f, 0.45f, 0.95f);
                GUI.color = Color.white;
                GUI.Label(new Rect(x - 70f, y + size * 0.35f + 14f, 140f, 20f), state, _markStyle);
            }
            GUI.color = prev;
        }

        private static Camera ResolveViewCamera()
        {
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null && csm.mainCamera != null && csm.mainCamera.enabled)
                    return csm.mainCamera;
            }
            catch { }
            try
            {
                Camera main = Camera.main;
                if (main != null && main.enabled)
                    return main;
            }
            catch { }
            return null;
        }

        private static void EnsureStyles()
        {
            if (_chipStyle != null)
                return;
            _chipStyle = new GUIStyle(GUI.skin.label);
            _chipStyle.alignment = TextAnchor.MiddleCenter;
            _chipStyle.fontSize = 13;
            _chipStyle.fontStyle = FontStyle.Bold;
            _chipStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.95f);
            _markStyle = new GUIStyle(GUI.skin.label);
            _markStyle.alignment = TextAnchor.UpperCenter;
            _markStyle.fontSize = 12;
            _markStyle.fontStyle = FontStyle.Bold;
            _markStyle.normal.textColor = new Color(1f, 0.9f, 0.45f, 0.95f);
        }
    }

    [HarmonyPatch(typeof(AIPilotCombatModes), "FixedUpdateState")]
    internal static class Patch_AIPilotCombat_AirportHold
    {
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");
        private static readonly FieldInfo TargetField =
            AccessTools.Field(typeof(AIPilotCombatModes), "currentTarget");

        private static void Postfix(AIPilotCombatModes __instance)
        {
            AirportDefection.ClearHeldCombatTarget(__instance, AircraftField, TargetField);
        }
    }

    [HarmonyPatch(typeof(AIHeloCombatState), "FixedUpdateState")]
    internal static class Patch_AIHeloCombat_AirportHold
    {
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");
        private static readonly FieldInfo TargetField =
            AccessTools.Field(typeof(AIHeloCombatState), "currentTarget");

        private static void Postfix(AIHeloCombatState __instance)
        {
            AirportDefection.ClearHeldCombatTarget(__instance, AircraftField, TargetField);
        }
    }

    [HarmonyPatch(typeof(Turret), "AssessTargetPriority")]
    internal static class Patch_Turret_AssessTargetPriority_AirportHold
    {
        private static bool Prefix(Turret __instance, Unit targetCandidate)
        {
            if (AirportDefection.ShouldHoldFire(__instance, targetCandidate))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Turret), "ChooseTarget")]
    internal static class Patch_Turret_ChooseTarget_AirportHold
    {
        private static void Postfix(Turret __instance)
        {
            if (__instance == null)
                return;
            Unit t = null;
            try { t = __instance.GetTarget(); }
            catch { }
            if (t == null)
                return;
            if (!AirportDefection.ShouldHoldFire(__instance, t))
                return;
            try { __instance.SetTargetFromController(null); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Turret), "SetTargetFromController")]
    internal static class Patch_Turret_SetTargetFromController_AirportHold
    {
        private static bool Prefix(Turret __instance, Unit target)
        {
            if (target == null)
                return true;
            if (AirportDefection.ShouldHoldFire(__instance, target))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(MissileLauncher), "Fire")]
    internal static class Patch_MissileLauncher_Fire_AirportHold
    {
        private static bool Prefix(Unit owner, Unit target)
        {
            if (AirportDefection.ShouldHoldFire(owner, target))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Weapon), "Fire", new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    internal static class Patch_Weapon_Fire_AirportHold
    {
        private static bool Prefix(Unit owner, Unit target)
        {
            if (AirportDefection.ShouldHoldFire(owner, target))
                return false;
            return true;
        }
    }
}
