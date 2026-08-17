using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    internal static class NukeResist
    {
        /// <summary>
        /// Aircraft = Full baseline (aircraft factors).
        /// Building = half resist when BuildingHalfResist (2× damage).
        /// Ship = triple resist when NavalTripleResist (1/3 damage); else half (2×).
        /// Vehicle = half resist (2× damage).
        /// </summary>
        internal enum Tier
        {
            None = 0,
            Aircraft = 1,
            Building = 2,
            Ship = 3,
            Vehicle = 4
        }

        internal static readonly Type InfluencedObjectType =
            AccessTools.Inner(typeof(Shockwave), "InfluencedObject");
        internal static readonly FieldInfo InfluencedDamageableField =
            InfluencedObjectType != null ? AccessTools.Field(InfluencedObjectType, "damageable") : null;

        internal static Aircraft GetAircraft(Component c)
        {
            if (c == null)
                return null;
            Aircraft ac = c as Aircraft;
            if (ac != null)
                return ac;
            try { return c.GetComponentInParent<Aircraft>(); }
            catch { return null; }
        }

        internal static Unit GetUnit(Component c)
        {
            if (c == null)
                return null;
            Unit u = c as Unit;
            if (u != null)
                return u;
            try { return c.GetComponentInParent<Unit>(); }
            catch { return null; }
        }

        internal static Unit GetUnitFromDamageable(IDamageable dmg)
        {
            if (dmg == null)
                return null;
            try
            {
                Unit u = dmg.GetUnit();
                if (u != null)
                    return u;
            }
            catch { }
            return GetUnit(dmg as Component);
        }

        internal static Aircraft GetAircraftFromDamageable(IDamageable dmg)
        {
            Unit u = GetUnitFromDamageable(dmg);
            if (u is Aircraft)
                return (Aircraft)u;
            if (u != null)
            {
                try { return u.GetComponentInParent<Aircraft>(); }
                catch { }
            }
            return GetAircraft(dmg as Component);
        }

        internal static Tier GetTier(Unit u)
        {
            if (!Plugin.NukeShockResist.Value || u == null)
                return Tier.None;
            if (u is Aircraft)
                return Tier.Aircraft;
            if (u is Building)
                return Tier.Building;
            if (u is Ship)
                return Tier.Ship;
            if (u is GroundVehicle)
                return Tier.Vehicle;
            return Tier.None;
        }

        internal static Tier GetTier(Component c)
        {
            return GetTier(GetUnit(c));
        }

        internal static Tier GetTier(IDamageable dmg)
        {
            return GetTier(GetUnitFromDamageable(dmg));
        }

        internal static bool IsProtectedUnit(Unit u)
        {
            return GetTier(u) != Tier.None;
        }

        internal static bool IsProtectedAircraft(Aircraft ac)
        {
            return GetTier(ac) == Tier.Aircraft;
        }

        internal static bool IsProtected(Component c)
        {
            return GetTier(c) != Tier.None;
        }

        internal static bool IsProtectedDamageable(IDamageable dmg)
        {
            return GetTier(dmg) != Tier.None;
        }

        private static bool BuildingHalf()
        {
            return Plugin.BuildingHalfResist != null && Plugin.BuildingHalfResist.Value;
        }

        private static bool NavalTriple()
        {
            return Plugin.NavalTripleResist != null && Plugin.NavalTripleResist.Value;
        }

        private static float ShockFactorFor(Unit u, Tier tier)
        {
            return NukeResistMathService.ShockFactor(
                tier,
                Plugin.NukeAircraftShockFactor.Value,
                Plugin.NukeShockFactor.Value,
                BuildingHalf(),
                NavalTriple());
        }

        private static float BlastFactorFor(Unit u, Tier tier)
        {
            return NukeResistMathService.BlastFactor(
                tier,
                Plugin.NukeAircraftBlastFactor.Value,
                Plugin.NukeBlastFactor.Value,
                BuildingHalf(),
                NavalTriple());
        }

        internal static void ScaleShock(Unit u, ref float a, ref float b)
        {
            Tier tier = GetTier(u);
            if (tier == Tier.None)
                return;
            float f = ShockFactorFor(u, tier);
            a *= f;
            b *= f;
        }

        internal static void ScaleShock(Component c, ref float a, ref float b)
        {
            ScaleShock(GetUnit(c), ref a, ref b);
        }

        internal static void ScaleBlastDamage(Unit u, ref float blastDamage)
        {
            Tier tier = GetTier(u);
            if (tier == Tier.None)
                return;
            if (!NukeResistMathService.BlastAboveThreshold(blastDamage, Plugin.NukeBlastThreshold.Value))
                return;
            blastDamage *= BlastFactorFor(u, tier);
        }

        internal static void ScaleBlastDamage(Component c, ref float blastDamage)
        {
            ScaleBlastDamage(GetUnit(c), ref blastDamage);
        }

        internal static void ScaleShockwaveHit(Unit u, ref float overpressure, ref float blastYield)
        {
            Tier tier = GetTier(u);
            if (tier == Tier.None)
                return;
            overpressure *= ShockFactorFor(u, tier);
            blastYield *= BlastFactorFor(u, tier);
        }

        internal static void ScaleShockwaveHit(IDamageable dmg, ref float overpressure, ref float blastYield)
        {
            ScaleShockwaveHit(GetUnitFromDamageable(dmg), ref overpressure, ref blastYield);
        }
    }
}
