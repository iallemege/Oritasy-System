using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Marks F9 orbital drops. After the 20 km handoff, HighAlt freeze must not
    /// slam them down again (that + vanilla TBM loft is the in-place loop).
    /// </summary>
    public sealed class F9DropMark : MonoBehaviour
    {
        internal bool IsTbm;
        internal bool Released;

        internal void Configure(bool tbm)
        {
            IsTbm = tbm;
            Released = false;
        }

        private void FixedUpdate()
        {
            if (!IsTbm || !Released)
                return;
            Missile missile = null;
            try { missile = GetComponent<Missile>(); }
            catch { }
            if (missile == null)
                return;
            F9TbmDive.SteerReleased(missile);
        }

        internal static F9DropMark Find(Missile missile)
        {
            if (missile == null)
                return null;
            try { return missile.GetComponent<F9DropMark>(); }
            catch { return null; }
        }

        internal static bool Has(Missile missile)
        {
            return Find(missile) != null;
        }

        internal static bool HasTbm(Missile missile)
        {
            F9DropMark mark = Find(missile);
            return mark != null && mark.IsTbm;
        }

        internal static bool IsReleased(Missile missile)
        {
            F9DropMark mark = Find(missile);
            return mark != null && mark.Released;
        }

        internal static void MarkReleased(Missile missile)
        {
            F9DropMark mark = Find(missile);
            if (mark != null)
                mark.Released = true;
        }
    }

    /// <summary>
    /// F9 TBM: replace BallisticMissileGuidance loft with a downward dive.
    /// Vanilla SetTrajectory aims 45–90° up while the engine is on.
    /// </summary>
    internal static class F9TbmDive
    {
        private static readonly FieldInfo KnownPosField =
            AccessTools.Field(typeof(BallisticMissileGuidance), "knownPos");
        private static readonly FieldInfo KnownVelField =
            AccessTools.Field(typeof(BallisticMissileGuidance), "knownVel");
        private static readonly FieldInfo SeekerTargetField =
            AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo GLimitField =
            AccessTools.Field(typeof(Missile), "gLimit");

        internal static bool TryReplaceTrajectory(BallisticMissileGuidance guidance)
        {
            if (guidance == null)
                return false;
            Missile missile = null;
            try
            {
                if (Plugin.SeekerMissileField != null)
                    missile = Plugin.SeekerMissileField.GetValue(guidance) as Missile;
            }
            catch { }
            if (missile == null)
            {
                try { missile = guidance.GetComponent<Missile>(); }
                catch { }
            }
            if (!F9DropMark.HasTbm(missile))
                return false;

            ApplyGLimit(missile);
            if (missile.transform != null
                && missile.transform.position.y >= HighAltMissileFreeze.FreezeAboveM)
                return true;
            ApplyDive(guidance, missile);
            return true;
        }

        internal static void SteerReleased(Missile missile)
        {
            if (missile == null || missile.disabled)
                return;
            ApplyGLimit(missile);
            BallisticMissileGuidance guidance = null;
            try { guidance = missile.GetComponent<BallisticMissileGuidance>(); }
            catch { }
            if (guidance == null)
            {
                try { guidance = missile.GetComponentInChildren<BallisticMissileGuidance>(); }
                catch { }
            }
            ApplyDive(guidance, missile);
        }

        private static void ApplyDive(BallisticMissileGuidance guidance, Missile missile)
        {
            if (missile == null)
                return;
            Vector3 tpos;
            Vector3 tvel;
            if (!TryResolveAim(guidance, missile, out tpos, out tvel))
                return;
            Vector3 mpos = missile.transform.position;
            Vector3 aim = StrategicArsenalMathService.F9TbmDiveAim(mpos, tpos);
            try { missile.SetAimpoint(aim.ToGlobalPosition(), tvel); }
            catch { }
        }

        private static bool TryResolveAim(
            BallisticMissileGuidance guidance,
            Missile missile,
            out Vector3 tpos,
            out Vector3 tvel)
        {
            tpos = Vector3.zero;
            tvel = Vector3.zero;
            Unit target = null;
            try
            {
                if (SeekerTargetField != null)
                    target = SeekerTargetField.GetValue(guidance) as Unit;
            }
            catch { }
            if (target == null)
            {
                try
                {
                    PersistentID tid = missile.targetID;
                    Unit u;
                    if (tid.IsValid && UnitRegistry.TryGetUnit(tid, out u))
                        target = u;
                }
                catch { }
            }
            if (target != null && Plugin.IsUnitAlive(target))
            {
                try { tpos = target.transform.position; }
                catch { return false; }
                try
                {
                    if (target.rb != null)
                        tvel = Vector3.ClampMagnitude(target.rb.velocity, 20f);
                }
                catch { }
                return true;
            }
            if (guidance == null || KnownPosField == null)
                return false;
            try
            {
                object raw = KnownPosField.GetValue(guidance);
                if (raw is GlobalPosition)
                {
                    GlobalPosition gp = (GlobalPosition)raw;
                    tpos = gp.ToLocalPosition();
                }
                else
                    return false;
            }
            catch { return false; }
            try
            {
                if (KnownVelField != null)
                    tvel = (Vector3)KnownVelField.GetValue(guidance);
            }
            catch { }
            return true;
        }

        internal static void ApplyGLimit(Missile missile)
        {
            if (missile == null || GLimitField == null)
                return;
            try { GLimitField.SetValue(missile, StrategicArsenalMathService.F9TbmGLimit); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(BallisticMissileGuidance), "SetTrajectory")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Bmg_F9Dive
    {
        [HarmonyPrefix]
        private static bool Prefix(BallisticMissileGuidance __instance)
        {
            return !F9TbmDive.TryReplaceTrajectory(__instance);
        }
    }
}
