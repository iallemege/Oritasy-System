using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Ship-edition missile identity, isolated from aircraft LOAL / MultiMode.
    ///
    /// Two editions:
    /// - Air: Aircraft owner, no tag. Full MultiMode / LOAL.
    /// - Ship: naval launcher fire stamp, or naval-exclusive munition (RAM-45 / R9).
    ///   Vanilla seeker, VLS SlowChecks window, no overpen look-ahead.
    ///
    /// Never uses transform.root (carrier aircraft parenting false-positives).
    /// </summary>
    internal static class ShipMissileService
    {
        private static readonly FieldInfo InfoField = AccessTools.Field(typeof(Missile), "info");
        private const float PendingFireSec = 1.5f;
        private static float _pendingUntil;
        private static int _pendingOwnerId;

        internal static bool IsAircraftSide(Unit unit)
        {
            if (unit == null)
                return false;
            if (unit is Aircraft)
                return true;
            try { return unit.GetComponentInParent<Aircraft>() != null; }
            catch { return false; }
        }

        /// <summary>Ship hull or a turret under a Ship. Aircraft on a carrier is not this.</summary>
        internal static bool IsNavalLauncher(Unit unit)
        {
            if (unit == null || IsAircraftSide(unit))
                return false;
            if (unit is Ship)
                return true;
            try { return unit.GetComponentInParent<Ship>() != null; }
            catch { return false; }
        }

        /// <summary>RAM-45 / StratoLance R9 — never an aircraft pylon munition.</summary>
        internal static bool IsNavalExclusiveMunition(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.definition != null)
                {
                    if (MissileClassifyGateService.IsSamRadarKey(missile.definition.jsonKey))
                        return true;
                    if (MissileClassifyGateService.LooksLikeRam45Name(missile.definition.unitName))
                        return true;
                }
            }
            catch { }
            try
            {
                if (MissileClassifyGateService.LooksLikeRam45Name(missile.name))
                    return true;
            }
            catch { }
            try
            {
                WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                if (IsNavalExclusiveWeaponInfo(info))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsNavalExclusiveWeaponInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            string blob = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty));
            if (MissileClassifyGateService.LooksLikeNavalExclusiveName(blob))
                return true;
            try
            {
                if (info.weaponPrefab == null)
                    return false;
                Missile m = info.weaponPrefab.GetComponent<Missile>();
                if (m == null)
                    m = info.weaponPrefab.GetComponentInChildren<Missile>(true);
                if (m == null)
                    return false;
                if (m.definition != null)
                {
                    if (MissileClassifyGateService.IsSamRadarKey(m.definition.jsonKey))
                        return true;
                    if (MissileClassifyGateService.LooksLikeNavalExclusiveName(m.definition.unitName))
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool IsNavalExclusiveMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsNavalExclusiveWeaponInfo(mount.info))
                return true;
            if (MissileClassifyGateService.LooksLikeNavalExclusiveName(mount.jsonKey))
                return true;
            if (MissileClassifyGateService.LooksLikeNavalExclusiveName(mount.mountName))
                return true;
            if (MissileClassifyGateService.LooksLikeNavalExclusiveName(mount.name))
                return true;
            return false;
        }

        internal static void NoteFire(Unit owner)
        {
            if (!IsNavalLauncher(owner))
                return;
            _pendingUntil = Time.unscaledTime + PendingFireSec;
            try { _pendingOwnerId = owner.GetInstanceID(); }
            catch { _pendingOwnerId = 0; }
        }

        internal static void Stamp(Missile missile, Unit spawnOwner)
        {
            if (missile == null)
                return;
            try
            {
                if (missile.GetComponent<ShipLaunchedTag>() != null)
                    return;
            }
            catch { }

            Unit owner = spawnOwner != null ? spawnOwner : null;
            if (owner == null)
            {
                try { owner = missile.owner; }
                catch { }
            }

            if (IsAircraftSide(owner))
                return;

            bool naval = IsNavalLauncher(owner);
            // Pending ship fire must not stamp aircraft / unknown air munitions.
            if (!naval && owner == null && Time.unscaledTime <= _pendingUntil && _pendingOwnerId != 0
                && IsNavalExclusiveMunition(missile))
                naval = true;
            if (!naval && IsNavalExclusiveMunition(missile) && !IsAircraftSide(owner))
                naval = true;
            if (!naval)
                return;

            Plugin.TryAddBehaviour<ShipLaunchedTag>(missile.gameObject);
        }

        /// <summary>True only for the ship edition. Aircraft shots are always false.</summary>
        internal static bool IsShipEdition(Missile missile)
        {
            if (missile == null)
                return false;
            Unit owner = null;
            try { owner = missile.owner; }
            catch { }
            if (IsAircraftSide(owner))
                return false;
            try
            {
                if (missile.GetComponent<ShipLaunchedTag>() != null)
                    return true;
            }
            catch { }
            return IsNavalExclusiveMunition(missile);
        }

        /// <summary>VLS SARH (RAM-45 / R9): delayed motor + pitch-over. Not ship-fired ARH.</summary>
        internal static bool IsVlsEdition(Missile missile)
        {
            if (!IsShipEdition(missile))
                return false;
            if (IsNavalExclusiveMunition(missile))
                return true;
            try { return missile.GetComponent<SARHSeeker>() != null; }
            catch { return false; }
        }
    }
}
