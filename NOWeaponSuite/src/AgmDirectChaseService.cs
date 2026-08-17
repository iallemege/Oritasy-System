using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Vanilla AGM-48 / AGM-68 OpticalSeeker loft + jink + terrain-avoid aim above the
    /// lock (LOS to a tank often hits dirt, so SendTargetInfo climbs 0.25 * range and
    /// overflies). Stamp seekers to chase the unit instead of flying past it.
    /// </summary>
    internal static class AgmDirectChaseService
    {
        private static readonly FieldInfo LoftField = AccessTools.Field(typeof(OpticalSeeker), "loftAmount");
        private static readonly FieldInfo TerrainAvoidField = AccessTools.Field(typeof(OpticalSeeker), "terrainAvoidance");
        private static readonly FieldInfo JinkField = AccessTools.Field(typeof(OpticalSeeker), "jinkEvasion");
        private static readonly FieldInfo JinkAmount = AccessTools.Field(typeof(JinkEvasion), "amount");
        private static readonly FieldInfo TopAttackField = AccessTools.Field(typeof(OpticalSeeker), "topAttack");
        private static readonly FieldInfo TopAttackAmount = AccessTools.Field(typeof(TopAttack), "Amount");
        private static readonly FieldInfo HasVisualField = AccessTools.Field(typeof(OpticalSeeker), "hasVisual");

        internal static bool ShouldStamp(Missile missile, MissileSeeker seeker)
        {
            if (missile == null)
                return false;
            try
            {
                if (Plugin.IsGunShellMissile(missile) || Plugin.IsGunShellSeeker(seeker))
                    return false;
                if (Plugin.IsBallisticMissile(missile) || seeker is BallisticMissileGuidance)
                    return false;
                if (Plugin.IsCruiseMissile(missile) || seeker is OpticalSeekerCruiseMissile)
                    return false;
                if (Plugin.IsScannerReconMissile(missile))
                    return false;
                if (Plugin.IsKh85Missile(missile) || Plugin.IsLikelyUnstampedKh85Launch(missile))
                    return false;
                if (AgmTWeapon.HasBusDispenser(missile) || AgmTWeapon.IsAgmTMissile(missile)
                    || AgmTWeapon.IsGs25Submunition(missile))
                    return false;
                if (Aam2CvWeapon.IsAam2CvMissile(missile))
                    return false;
            }
            catch
            {
                return false;
            }

            OpticalSeeker opt = seeker as OpticalSeeker;
            if (opt == null)
            {
                try { opt = missile.GetComponent<OpticalSeeker>(); }
                catch { opt = null; }
            }
            if (opt == null)
                return false;
            return Plugin.IsSurfaceAttackSeeker(opt);
        }

        internal static void Stamp(Missile missile)
        {
            Stamp(missile, null);
        }

        internal static void Stamp(Missile missile, MissileSeeker seeker)
        {
            if (!ShouldStamp(missile, seeker))
                return;

            OpticalSeeker opt = seeker as OpticalSeeker;
            if (opt == null)
            {
                try { opt = missile.GetComponent<OpticalSeeker>(); }
                catch { opt = null; }
            }
            if (opt == null)
            {
                try { opt = missile.GetComponentInChildren<OpticalSeeker>(true); }
                catch { opt = null; }
            }
            if (opt == null)
                return;

            try
            {
                if (LoftField != null)
                    LoftField.SetValue(opt, 0f);
                if (TerrainAvoidField != null)
                    TerrainAvoidField.SetValue(opt, false);

                object jink = JinkField != null ? JinkField.GetValue(opt) : null;
                if (jink != null && JinkAmount != null)
                    JinkAmount.SetValue(jink, 0f);

                object top = TopAttackField != null ? TopAttackField.GetValue(opt) : null;
                if (top != null && TopAttackAmount != null)
                    TopAttackAmount.SetValue(top, 0f);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("AGM direct chase stamp: " + ex.Message);
            }
        }

        /// <summary>True when the optical seeker already sees the lock — vanilla Seek can steer.</summary>
        internal static bool SeekerHasVisual(MissileSeeker seeker)
        {
            if (seeker == null || HasVisualField == null)
                return false;
            try
            {
                object v = HasVisualField.GetValue(seeker);
                return v is bool && (bool)v;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Aim at the unit with a short lead. No energy look-ahead (that overshoots).</summary>
        internal static Vector3 DirectAimPoint(Vector3 missilePos, Vector3 targetPos, Vector3 targetVel)
        {
            float dist = Vector3.Distance(missilePos, targetPos);
            float leadSec = Mathf.Clamp(dist / 450f, 0.05f, 0.35f);
            return targetPos + targetVel * leadSec;
        }
    }
}
