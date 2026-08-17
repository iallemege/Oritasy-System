using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Kh85MT
{
    /// <summary>
    /// TGM-85D Hegemony — radar-only strike (ARM-style).
    /// Manual or auto lock: only units that carry a Radar component.
    /// </summary>
    internal static class Kh85DArm
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> HuntRange;
        internal static ConfigEntry<float> HuntInterval;

        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Missile), "target");
        private static readonly FieldInfo SeekerField = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly Collider[] OverlapBuf = new Collider[64];

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("ArmD", "Enabled", true,
                "TGM-85D Hegemony: only lock units that have a Radar (manual or auto).");
            HuntRange = config.Bind("ArmD", "HuntRange", 14000f,
                "Auto-hunt range (m) for hostile radar emitters when no valid lock.");
            HuntInterval = config.Bind("ArmD", "HuntInterval", 0.35f,
                "Seconds between radar-hunt scans.");
        }

        internal static bool IsEnabled()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static bool IsDVariant(Missile missile)
        {
            return Kh85Util.IsKh85(missile) && Kh85Util.GetVariant(missile) == "D";
        }

        internal static void TryAttach(Missile missile)
        {
            if (missile == null || !IsEnabled() || !IsDVariant(missile))
                return;
            if (missile.GetComponent<Kh85DArmBrain>() != null)
                return;
            try { missile.gameObject.AddComponent<Kh85DArmBrain>(); }
            catch { }
        }

        internal static bool UnitHasRadar(Unit unit)
        {
            if (unit == null)
                return false;
            try
            {
                Radar r = unit.GetComponentInChildren<Radar>(true);
                if (r != null)
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>True if this unit is a valid D lock (hostile + has radar).</summary>
        internal static bool IsValidRadarTarget(Missile self, Unit candidate)
        {
            if (self == null || candidate == null)
                return false;
            if (!UnitHasRadar(candidate))
                return false;
            try
            {
                FactionHQ a = self.NetworkHQ;
                FactionHQ b = candidate.NetworkHQ;
                if (a != null && b != null && a == b)
                    return false;
            }
            catch { }
            return true;
        }

        /// <summary>Filter launch / sticky lock — drop non-radar targets.</summary>
        internal static Unit FilterLaunchTarget(Missile missile, Unit proposed)
        {
            if (!IsEnabled() || !IsDVariant(missile))
                return proposed;
            if (proposed != null && IsValidRadarTarget(missile, proposed))
                return proposed;
            return FindNearestRadar(missile);
        }

        internal static Unit FindNearestRadar(Missile self)
        {
            if (self == null)
                return null;
            float range = HuntRange != null ? HuntRange.Value : 14000f;
#if ORITASY_COMBINED
            try
            {
                float mm = WeXon.Plugin.EffectiveHuntRadius(self);
                if (mm > range)
                    range = mm;
            }
            catch { }
#endif
            int hits = 0;
            try
            {
                hits = Physics.OverlapSphereNonAlloc(self.transform.position, range, OverlapBuf,
                    ~0, QueryTriggerInteraction.Ignore);
            }
            catch { return null; }

            Unit best = null;
            float bestSq = range * range;
            Vector3 pos = self.transform.position;
            for (int i = 0; i < hits; i++)
            {
                Collider c = OverlapBuf[i];
                if (c == null)
                    continue;
                Unit u = null;
                try { u = c.GetComponentInParent<Unit>(); }
                catch { }
                if (u == null || u is Missile)
                    continue;
                if (!IsValidRadarTarget(self, u))
                    continue;
                float sq = (u.transform.position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = u;
                }
            }
            return best;
        }

        internal static void Enforce(Missile missile)
        {
            if (missile == null || !IsEnabled() || !IsDVariant(missile))
                return;

            Unit current = null;
            try
            {
                if (TargetField != null)
                    current = TargetField.GetValue(missile) as Unit;
            }
            catch { }

            if (current != null && IsValidRadarTarget(missile, current))
            {
                // Keep sticky seeker fields warm.
                Kh85Weapon.ApplyTargetLock(missile, current);
                return;
            }

            Unit radar = FindNearestRadar(missile);
            if (radar != null)
            {
                Kh85Weapon.ApplyTargetLock(missile, radar);
                Kh85Weapon.ApplyLeadAim(missile, radar);
                return;
            }

            // No radar available — clear illegal lock so we never chase a dumb target.
            if (current != null)
                ClearTarget(missile);
        }

        private static void ClearTarget(Missile missile)
        {
            try { missile.SetTarget(null); }
            catch { }
            try
            {
                if (SeekerField != null && SeekerTargetField != null)
                {
                    MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
                    if (seeker != null)
                        SeekerTargetField.SetValue(seeker, null);
                }
            }
            catch { }
        }
    }

    public class Kh85DArmBrain : MonoBehaviour
    {
        private Missile _missile;
        private float _nextHunt;

        private void Awake()
        {
            _missile = GetComponent<Missile>();
        }

        private void FixedUpdate()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null || !Kh85DArm.IsEnabled())
                return;
            try
            {
                if (_missile.disabled)
                    return;
                if (_missile.timeSinceSpawn < Kh85Advanced.LoalHuntDelaySec)
                    return;
            }
            catch { }

            float interval = Kh85DArm.HuntInterval != null ? Kh85DArm.HuntInterval.Value : 0.35f;
            if (interval < 0.1f)
                interval = 0.1f;
            if (Time.time < _nextHunt)
                return;
            _nextHunt = Time.time + interval;
            Kh85DArm.Enforce(_missile);
        }
    }
}
