using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Extra F6 mystery-pool rewards (30 kinds). Three slots stay hidden;
    /// this only widens ShuffleSlots and applies the extra BoostKind values.
    /// Never offers Hyperion / carrier.
    /// </summary>
    internal static class KillChoiceRewardService
    {
        private const float ThrustMul = 1.25f;
        private const float FuelBurnMul = 0.45f;
        private const float SpeedMul = 1.12f;
        private const float GAdd = 2f;
        private const float BurstSeconds = 60f;
        private const float FuelEconSeconds = 90f;

        private static readonly KillChoiceMenu.BoostKind[] ExtraKinds = new KillChoiceMenu.BoostKind[]
        {
            KillChoiceMenu.BoostKind.Cash5,
            KillChoiceMenu.BoostKind.Cash15,
            KillChoiceMenu.BoostKind.Cash30,
            KillChoiceMenu.BoostKind.RepairNow,
            KillChoiceMenu.BoostKind.RefuelNow,
            KillChoiceMenu.BoostKind.BatteryNow,
            KillChoiceMenu.BoostKind.RearmGuns,
            KillChoiceMenu.BoostKind.RearmStations,
            KillChoiceMenu.BoostKind.RearmCm,
            KillChoiceMenu.BoostKind.ExtinguishNow,
            KillChoiceMenu.BoostKind.EngineHeal,
            KillChoiceMenu.BoostKind.Xp100,
            KillChoiceMenu.BoostKind.Xp250,
            KillChoiceMenu.BoostKind.Xp500,
            KillChoiceMenu.BoostKind.F9Cooldown,
            KillChoiceMenu.BoostKind.ExtraPicks,
            KillChoiceMenu.BoostKind.AirframeFree,
            KillChoiceMenu.BoostKind.AiRestrictHard,
            KillChoiceMenu.BoostKind.AiRestrictLong,
            KillChoiceMenu.BoostKind.RepairOnLand,
            KillChoiceMenu.BoostKind.FreeResupply,
            KillChoiceMenu.BoostKind.ThrustBurst,
            KillChoiceMenu.BoostKind.GBurst,
            KillChoiceMenu.BoostKind.FuelEcon,
            KillChoiceMenu.BoostKind.SpeedBurst,
            KillChoiceMenu.BoostKind.RepairFew,
            KillChoiceMenu.BoostKind.SortieKit,
            KillChoiceMenu.BoostKind.Cash10,
            KillChoiceMenu.BoostKind.Xp350,
            KillChoiceMenu.BoostKind.AiRestrictStack,
            KillChoiceMenu.BoostKind.EngineMaterial
        };

        private static bool _airframeFreePending;
        private static bool _freeResupplyPending;
        private static bool _repairOnLandPending;
        private static bool _sawAirborneForLandRepair;
        private static float _thrustUntil;
        private static float _gUntil;
        private static float _fuelUntil;
        private static float _speedUntil;
        private static float _nextOverlayPush;
        private static bool _kitRepair;
        private static bool _kitFuel;
        private static bool _kitBatt;
        private static bool _kitRearm;
        private static bool _kitGuns;
        private static bool _kitCm;
        private static MethodInfo _grantXpMethod;
        private static FieldInfo _cmStationsField;

        internal static bool AirframeFreePending
        {
            get { return _airframeFreePending; }
        }

        internal static bool HasFreeResupply
        {
            get { return _freeResupplyPending; }
        }

        internal static void ConsumeAirframeFree()
        {
            _airframeFreePending = false;
        }

        internal static void ConsumeFreeResupply()
        {
            _freeResupplyPending = false;
        }

        internal static float ThrustOverlayMul()
        {
            return Time.unscaledTime < _thrustUntil ? ThrustMul : 1f;
        }

        internal static float FuelBurnOverlayMul()
        {
            return Time.unscaledTime < _fuelUntil ? FuelBurnMul : 1f;
        }

        internal static float SpeedOverlayMul()
        {
            return Time.unscaledTime < _speedUntil ? SpeedMul : 1f;
        }

        internal static float GOverlayAdd()
        {
            return Time.unscaledTime < _gUntil ? GAdd : 0f;
        }

        internal static void AddExtraKinds(List<KillChoiceMenu.BoostKind> pool)
        {
            if (pool == null)
                return;
            for (int i = 0; i < ExtraKinds.Length; i++)
                pool.Add(ExtraKinds[i]);
        }

        internal static void ResetMatch()
        {
            _airframeFreePending = false;
            _freeResupplyPending = false;
            _repairOnLandPending = false;
            _sawAirborneForLandRepair = false;
            _thrustUntil = 0f;
            _gUntil = 0f;
            _fuelUntil = 0f;
            _speedUntil = 0f;
            _kitRepair = false;
            _kitFuel = false;
            _kitBatt = false;
            _kitRearm = false;
            _kitGuns = false;
            _kitCm = false;
        }

        internal static void Tick()
        {
            FlushKits();
            TickLandRepair();
            TickOverlays();
        }

        internal static void AppendBuffLines(List<string> parts)
        {
            if (parts == null)
                return;
            if (_airframeFreePending)
                parts.Add(UiLang.T("next airframe free", "下次机体免费"));
            if (_freeResupplyPending)
                parts.Add(UiLang.T("F10 resupply coupon", "F10 补能券"));
            if (_repairOnLandPending)
                parts.Add(UiLang.T("repair on next landing", "下次着陆维修"));
            float now = Time.unscaledTime;
            if (now < _thrustUntil)
                parts.Add(UiLang.T("thrust +" + Remain(_thrustUntil), "推力 +" + Remain(_thrustUntil)));
            if (now < _gUntil)
                parts.Add(UiLang.T("G +" + Remain(_gUntil), "过载 +" + Remain(_gUntil)));
            if (now < _fuelUntil)
                parts.Add(UiLang.T("fuel save " + Remain(_fuelUntil), "省油 " + Remain(_fuelUntil)));
            if (now < _speedUntil)
                parts.Add(UiLang.T("speed +" + Remain(_speedUntil), "极速 +" + Remain(_speedUntil)));
        }

        internal static bool TryApply(KillChoiceMenu.BoostKind kind, out string reveal)
        {
            reveal = null;
            switch (kind)
            {
                case KillChoiceMenu.BoostKind.Cash5:
                    reveal = GrantCash(5f);
                    return true;
                case KillChoiceMenu.BoostKind.Cash10:
                    reveal = GrantCash(10f);
                    return true;
                case KillChoiceMenu.BoostKind.Cash15:
                    reveal = GrantCash(15f);
                    return true;
                case KillChoiceMenu.BoostKind.Cash30:
                    reveal = GrantCash(30f);
                    return true;
                case KillChoiceMenu.BoostKind.RepairNow:
                    reveal = DoRepairNow();
                    return true;
                case KillChoiceMenu.BoostKind.RefuelNow:
                    reveal = DoRefuelNow();
                    return true;
                case KillChoiceMenu.BoostKind.BatteryNow:
                    reveal = DoBatteryNow();
                    return true;
                case KillChoiceMenu.BoostKind.RearmGuns:
                    reveal = DoRearm(true, false);
                    return true;
                case KillChoiceMenu.BoostKind.RearmStations:
                    reveal = DoRearm(false, true);
                    return true;
                case KillChoiceMenu.BoostKind.RearmCm:
                    reveal = DoRearmCm();
                    return true;
                case KillChoiceMenu.BoostKind.ExtinguishNow:
                    reveal = DoExtinguish();
                    return true;
                case KillChoiceMenu.BoostKind.EngineHeal:
                    reveal = DoEngineHeal();
                    return true;
                case KillChoiceMenu.BoostKind.Xp100:
                    reveal = GrantXp(100);
                    return true;
                case KillChoiceMenu.BoostKind.Xp250:
                    reveal = GrantXp(250);
                    return true;
                case KillChoiceMenu.BoostKind.Xp350:
                    reveal = GrantXp(350);
                    return true;
                case KillChoiceMenu.BoostKind.Xp500:
                    reveal = GrantXp(500);
                    return true;
                case KillChoiceMenu.BoostKind.F9Cooldown:
                    KillChoiceMenu.ClearF9CooldownFromReward();
                    reveal = UiLang.T("F9 arsenal cooldown cleared", "F9 支援冷却已清除");
                    return true;
                case KillChoiceMenu.BoostKind.ExtraPicks:
                    KillChoiceMenu.AddPendingChoices(2);
                    reveal = UiLang.T("Bonus mystery draws ×2", "额外神秘抽取 ×2");
                    return true;
                case KillChoiceMenu.BoostKind.AirframeFree:
                    _airframeFreePending = true;
                    reveal = UiLang.T("Next airframe purchase is free", "下次机体购买免费");
                    return true;
                case KillChoiceMenu.BoostKind.AiRestrictHard:
                    KillChoiceMenu.ArmAiRestrict(10, 3);
                    reveal = UiLang.T(
                        "Enemy AI×10 next spawns ≤ your rank−3",
                        "敌方 AI 下 10 次起飞 ≤ 你当前 rank−3");
                    return true;
                case KillChoiceMenu.BoostKind.AiRestrictLong:
                    KillChoiceMenu.ArmAiRestrict(12, 1);
                    reveal = UiLang.T(
                        "Enemy AI×12 next spawns ≤ your rank−1",
                        "敌方 AI 下 12 次起飞 ≤ 你当前 rank−1");
                    return true;
                case KillChoiceMenu.BoostKind.AiRestrictStack:
                    KillChoiceMenu.ArmAiRestrict(8, 2);
                    reveal = UiLang.T(
                        "Enemy AI restrict +8 spawns (≤ rank−2)",
                        "敌方 AI 限机 +8 次（≤ rank−2）");
                    return true;
                case KillChoiceMenu.BoostKind.RepairOnLand:
                    _repairOnLandPending = true;
                    _sawAirborneForLandRepair = false;
                    reveal = UiLang.T("Full repair on next landing", "下次着陆时全修");
                    return true;
                case KillChoiceMenu.BoostKind.FreeResupply:
                    _freeResupplyPending = true;
                    reveal = UiLang.T("Next F10 aerial resupply is free", "下次 F10 空中补能免费");
                    return true;
                case KillChoiceMenu.BoostKind.ThrustBurst:
                    _thrustUntil = Time.unscaledTime + BurstSeconds;
                    PushOverlays();
                    reveal = UiLang.T("Thrust ×1.25 for 60s", "推力 ×1.25，持续 60 秒");
                    return true;
                case KillChoiceMenu.BoostKind.GBurst:
                    _gUntil = Time.unscaledTime + BurstSeconds;
                    PushOverlays();
                    reveal = UiLang.T("Aircraft G +2 for 60s", "机体过载 +2，持续 60 秒");
                    return true;
                case KillChoiceMenu.BoostKind.FuelEcon:
                    _fuelUntil = Time.unscaledTime + FuelEconSeconds;
                    PushOverlays();
                    reveal = UiLang.T("Fuel burn ×0.45 for 90s", "油耗 ×0.45，持续 90 秒");
                    return true;
                case KillChoiceMenu.BoostKind.SpeedBurst:
                    _speedUntil = Time.unscaledTime + BurstSeconds;
                    PushOverlays();
                    reveal = UiLang.T("Max speed ×1.12 for 60s", "极速 ×1.12，持续 60 秒");
                    return true;
                case KillChoiceMenu.BoostKind.RepairFew:
                    reveal = DoRepairFew();
                    return true;
                case KillChoiceMenu.BoostKind.SortieKit:
                    reveal = DoSortieKit();
                    return true;
                case KillChoiceMenu.BoostKind.EngineMaterial:
                    reveal = DoEngineMaterial();
                    return true;
                default:
                    return false;
            }
        }

        private static string Remain(float until)
        {
            int s = Mathf.CeilToInt(until - Time.unscaledTime);
            if (s < 0)
                s = 0;
            return s.ToString() + "s";
        }

        private static Aircraft ResolveLocal()
        {
            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                    return ac;
            }
            catch { }
            return null;
        }

        private static string GrantCash(float millions)
        {
            try
            {
                Player p;
                if (GameManager.GetLocalPlayer(out p) && p != null)
                    p.AddAllocation(millions);
            }
            catch { }
            return UiLang.T(
                "Funds +" + millions.ToString("0") + "M",
                "资金 +" + millions.ToString("0") + "M");
        }

        private static string GrantXp(int amount)
        {
            bool ok = false;
            try
            {
                if (_grantXpMethod == null)
                {
                    Type t = Assembly.GetExecutingAssembly().GetType("WeXon.PlayerCareer");
                    if (t == null)
                        t = Type.GetType("WeXon.PlayerCareer");
                    if (t != null)
                    {
                        _grantXpMethod = t.GetMethod("TryGrantBonusXp",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
                if (_grantXpMethod != null)
                {
                    object r = _grantXpMethod.Invoke(null, new object[] { amount });
                    if (r != null)
                        ok = Convert.ToBoolean(r);
                }
            }
            catch { }
            if (ok)
            {
                return UiLang.T("Career XP +" + amount, "生涯经验 +" + amount);
            }
            return UiLang.T(
                "Career XP +" + amount + " (career off / unavailable)",
                "生涯经验 +" + amount + "（生涯关闭或不可用）");
        }

        private static string DoRepairNow()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                _kitRepair = true;
                return UiLang.T("Full repair armed (next aircraft)", "全修已待命（下一架飞机）");
            }
            int n = ComponentRepair.RepairAllFromOutside(ac);
            return UiLang.T("Repaired " + n + " item(s)", "已维修 " + n + " 项");
        }

        private static string DoRepairFew()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                _kitRepair = true;
                return UiLang.T("Repair armed (next aircraft)", "维修已待命（下一架飞机）");
            }
            int n = ComponentRepair.RepairFewFromOutside(ac, 3);
            return UiLang.T("Spot-repaired " + n + " item(s)", "点修 " + n + " 项");
        }

        private static string DoRefuelNow()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                _kitFuel = true;
                return UiLang.T("Full fuel armed (next aircraft)", "满油已待命（下一架飞机）");
            }
            AerialResupply.FillFuelFromOutside(ac);
            return UiLang.T("Fuel tanks filled", "油箱已加满");
        }

        private static string DoBatteryNow()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                _kitBatt = true;
                return UiLang.T("Battery armed (next aircraft)", "电池已待命（下一架飞机）");
            }
            AerialResupply.FillBatteryFromOutside(ac);
            return UiLang.T("Battery fully charged", "电池已充满");
        }

        private static string DoExtinguish()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
                return UiLang.T("No aircraft — fires skipped", "无飞机 — 未灭火");
            int n = ComponentRepair.ExtinguishFromOutside(ac);
            return UiLang.T("Extinguished " + n + " fire(s)", "已灭火 " + n + " 处");
        }

        private static string DoEngineHeal()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
                return UiLang.T("No aircraft — engines skipped", "无飞机 — 未修引擎");
            int n = ComponentRepair.HealEnginesFromOutside(ac);
            return UiLang.T("Engines restored ×" + n, "引擎已恢复 ×" + n);
        }

        private static string DoEngineMaterial()
        {
            string msg;
            if (AirframeWearService.TryUpgradeMaterial(out msg) && !string.IsNullOrEmpty(msg))
                return msg;
            return GrantCash(5f) + UiLang.T(
                " (engine material already max)",
                "（引擎材料已满级）");
        }

        private static string DoRearm(bool gunsOnly, bool allStations)
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                if (gunsOnly)
                    _kitGuns = true;
                else
                    _kitRearm = true;
                return UiLang.T("Rearm armed (next aircraft)", "补弹已待命（下一架飞机）");
            }
            int n = RearmStations(ac, gunsOnly, allStations);
            if (gunsOnly)
                return UiLang.T("Guns rearmed ×" + n, "机炮已补弹 ×" + n);
            return UiLang.T("Stations rearmed ×" + n, "挂架已补弹 ×" + n);
        }

        private static string DoRearmCm()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                _kitCm = true;
                return UiLang.T("Flares armed (next aircraft)", "干扰弹已待命（下一架飞机）");
            }
            int n = RearmCountermeasures(ac);
            return UiLang.T("Countermeasures refilled ×" + n, "干扰弹已补满 ×" + n);
        }

        private static string DoSortieKit()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                _kitRepair = true;
                _kitFuel = true;
                _kitBatt = true;
                _kitRearm = true;
                _kitCm = true;
                return UiLang.T("Sortie kit armed (next aircraft)", "出击包已待命（下一架飞机）");
            }
            int r = ComponentRepair.RepairAllFromOutside(ac);
            AerialResupply.FillFuelFromOutside(ac);
            AerialResupply.FillBatteryFromOutside(ac);
            int w = RearmStations(ac, false, true);
            int c = RearmCountermeasures(ac);
            return UiLang.T(
                "Sortie kit: repair " + r + " / rearm " + w + " / CM " + c,
                "出击包：维修 " + r + " / 补弹 " + w + " / 干扰弹 " + c);
        }

        private static int RearmStations(Aircraft ac, bool gunsOnly, bool allStations)
        {
            if (ac == null || ac.weaponStations == null)
                return 0;
            int n = 0;
            for (int i = 0; i < ac.weaponStations.Count; i++)
            {
                WeaponStation st = ac.weaponStations[i];
                if (st == null || st.Cargo)
                    continue;
                if (gunsOnly && !StationHasGun(st))
                    continue;
                if (!gunsOnly && !allStations && !StationHasGun(st))
                    continue;
                int ammo = st.FullAmmo;
                if (ammo <= 0)
                    ammo = 999;
                try
                {
                    st.Rearm(ammo);
                    n++;
                }
                catch { }
            }
            return n;
        }

        private static bool StationHasGun(WeaponStation st)
        {
            if (st == null || st.Weapons == null)
                return false;
            for (int i = 0; i < st.Weapons.Count; i++)
            {
                if (st.Weapons[i] is Gun)
                    return true;
            }
            return false;
        }

        private static int RearmCountermeasures(Aircraft ac)
        {
            if (ac == null || ac.countermeasureManager == null)
                return 0;
            CountermeasureManager mgr = ac.countermeasureManager;
            int n = 0;
            try
            {
                if (_cmStationsField == null)
                    _cmStationsField = AccessTools.Field(typeof(CountermeasureManager), "countermeasureStations");
                if (_cmStationsField == null)
                    return 0;
                IList list = _cmStationsField.GetValue(mgr) as IList;
                if (list == null)
                    return 0;
                for (int i = 0; i < list.Count; i++)
                {
                    object st = list[i];
                    if (st == null)
                        continue;
                    Type t = st.GetType();
                    FieldInfo maxF = AccessTools.Field(t, "maxAmmo");
                    FieldInfo ammoF = AccessTools.Field(t, "ammo");
                    if (maxF == null || ammoF == null)
                        continue;
                    object maxObj = maxF.GetValue(st);
                    int max = maxObj != null ? Convert.ToInt32(maxObj) : 0;
                    if (max < 1)
                        max = 24;
                    ammoF.SetValue(st, max);
                    n++;
                }
                try { mgr.UpdateHUD(); }
                catch { }
            }
            catch { }
            return n;
        }

        private static void FlushKits()
        {
            if (!_kitRepair && !_kitFuel && !_kitBatt && !_kitRearm && !_kitGuns && !_kitCm)
                return;
            Aircraft ac = ResolveLocal();
            if (ac == null)
                return;
            if (_kitRepair)
            {
                ComponentRepair.RepairAllFromOutside(ac);
                _kitRepair = false;
            }
            if (_kitFuel)
            {
                AerialResupply.FillFuelFromOutside(ac);
                _kitFuel = false;
            }
            if (_kitBatt)
            {
                AerialResupply.FillBatteryFromOutside(ac);
                _kitBatt = false;
            }
            if (_kitRearm)
            {
                RearmStations(ac, false, true);
                _kitRearm = false;
            }
            if (_kitGuns)
            {
                RearmStations(ac, true, false);
                _kitGuns = false;
            }
            if (_kitCm)
            {
                RearmCountermeasures(ac);
                _kitCm = false;
            }
        }

        private static void TickLandRepair()
        {
            if (!_repairOnLandPending)
                return;
            Aircraft ac = ResolveLocal();
            if (ac == null)
                return;
            bool landed = false;
            try { landed = ac.IsLanded(); }
            catch { }
            if (!landed)
            {
                _sawAirborneForLandRepair = true;
                return;
            }
            if (!_sawAirborneForLandRepair)
                return;
            _repairOnLandPending = false;
            _sawAirborneForLandRepair = false;
            int n = ComponentRepair.RepairAllFromOutside(ac);
            KillChoiceMenu.FlashFromReward(UiLang.T(
                "Landing repair: " + n + " item(s)",
                "着陆维修：" + n + " 项"));
        }

        private static void TickOverlays()
        {
            bool any = Time.unscaledTime < _thrustUntil
                || Time.unscaledTime < _gUntil
                || Time.unscaledTime < _fuelUntil
                || Time.unscaledTime < _speedUntil;
            bool expiredThrust = _thrustUntil > 0f && Time.unscaledTime >= _thrustUntil;
            bool expiredG = _gUntil > 0f && Time.unscaledTime >= _gUntil;
            bool expiredFuel = _fuelUntil > 0f && Time.unscaledTime >= _fuelUntil;
            bool expiredSpeed = _speedUntil > 0f && Time.unscaledTime >= _speedUntil;
            if (expiredThrust || expiredG || expiredFuel || expiredSpeed)
            {
                if (expiredThrust)
                    _thrustUntil = 0f;
                if (expiredG)
                    _gUntil = 0f;
                if (expiredFuel)
                    _fuelUntil = 0f;
                if (expiredSpeed)
                    _speedUntil = 0f;
                PushOverlays();
                KillChoiceMenu.FlashFromReward(UiLang.T("F6 burst expired", "F6 爆发增益已结束"));
                return;
            }
            if (!any)
                return;
            if (Time.unscaledTime < _nextOverlayPush)
                return;
            _nextOverlayPush = Time.unscaledTime + 0.5f;
            PushOverlays();
        }

        private static void PushOverlays()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null)
                return;
            try { FlightEnvelopeService.ApplyLimits(ac); }
            catch { }
            try { AircraftPowerService.ApplyPowerProfile(ac); }
            catch { }
        }
    }
}
