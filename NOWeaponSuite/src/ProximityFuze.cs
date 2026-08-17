using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Yield-scaled proximity fuze for Oritasy/WeXon missiles.
    ///
    /// Vanilla: MissileSeeker.proximityFuse (often false on clones) → SetProxyFuse →
    /// DetectCollisions → ProxyFuse.ConditionsMet uses range = speed * 0.25s (not yield).
    ///
    /// This module:
    /// 1) Ensures ProxyFuse is armed once the warhead is armed (no seeker / steer patches)
    /// 2) Replaces ConditionsMet range with cube-root(yield) scaling (same exponent as Shockwave)
    /// 3) Light FixedUpdate fallback Detonate if proxy was never wired
    ///
    /// Does not patch Steering, Seek, PID, or aimpoint paths.
    /// </summary>
    internal static class YieldProximityFuze
    {
        private static readonly Type ProxyFuseType = AccessTools.Inner(typeof(Missile), "ProxyFuse");
        private static readonly FieldInfo ProxyMissileTransform =
            ProxyFuseType != null ? AccessTools.Field(ProxyFuseType, "missileTransform") : null;
        private static readonly FieldInfo ProxyMissileRb =
            ProxyFuseType != null ? AccessTools.Field(ProxyFuseType, "missileRB") : null;
        private static readonly FieldInfo ProxyTargetTransform =
            ProxyFuseType != null ? AccessTools.Field(ProxyFuseType, "targetTransform") : null;
        private static readonly FieldInfo ProxyTargetRb =
            ProxyFuseType != null ? AccessTools.Field(ProxyFuseType, "targetRB") : null;
        private static readonly FieldInfo BlastYieldField = AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly FieldInfo ProxyFuseField = AccessTools.Field(typeof(Missile), "proxyFuse");

        internal static bool Enabled
        {
            get
            {
                return Plugin.EnableYieldProximityFuze != null && Plugin.EnableYieldProximityFuze.Value;
            }
        }

        /// <summary>
        /// Proximity trigger radius (m) from warhead blastYield.
        /// Matches Shockwave cube-root scaling: R = RefRange * (yield / RefYield)^(1/3) * Scale, clamped.
        /// </summary>
        internal static float RangeFromYield(float blastYield)
        {
            float refY = Plugin.ProximityRefYield != null ? Plugin.ProximityRefYield.Value : 25f;
            float refR = Plugin.ProximityRefRangeM != null ? Plugin.ProximityRefRangeM.Value : 30f;
            float scale = Plugin.ProximityScale != null ? Plugin.ProximityScale.Value : 1f;
            float minM = Plugin.ProximityMinM != null ? Plugin.ProximityMinM.Value : 8f;
            float maxM = Plugin.ProximityMaxM != null ? Plugin.ProximityMaxM.Value : 250f;
            return ProximityFuzeMathService.RangeFromYield(blastYield, refY, refR, scale, minM, maxM);
        }

        internal static float GetMissileYield(Missile missile)
        {
            if (missile == null || BlastYieldField == null)
                return 0f;
            try
            {
                return (float)BlastYieldField.GetValue(missile);
            }
            catch
            {
                return 0f;
            }
        }

        internal static bool ShouldHandle(Missile missile)
        {
            if (!Enabled || missile == null || missile.disabled)
                return false;
            // Cheap first: conventional HE never needs our yield prox (every-FU path).
            float yield = GetMissileYield(missile);
            float refY = Plugin.ProximityRefYield != null ? Plugin.ProximityRefYield.Value : 25f;
            float minYield = Mathf.Max(200f, refY * 4f);
            if (yield < minYield && !Plugin.IsNukeVariantMissile(missile))
                return false;
            try
            {
                if (!missile.IsServer)
                    return false;
            }
            catch
            {
                return false;
            }
            if (Plugin.IsGunShellMissile(missile) || Plugin.IsMotorlessProjectile(missile))
                return false;
            if (AgmTDispenser.IsSafeDiscard(missile))
                return false;
            // Cruise: keep impact / terrain fuse; yield airburst would spoil sea-skimmers.
            if (Plugin.IsCruiseMissile(missile))
                return false;
            try
            {
                if (!missile.IsArmed())
                    return false;
            }
            catch
            {
                return false;
            }
            float ageMin = Plugin.ProximityMinAge != null ? Plugin.ProximityMinAge.Value : 0.4f;
            try
            {
                if (missile.timeSinceSpawn < ageMin)
                    return false;
            }
            catch { }
            return true;
        }

        internal static Unit ResolveFuzeTarget(Missile missile)
        {
            if (missile == null)
                return null;
            try
            {
                Unit t = Plugin.ResolveDesignatedTarget(missile);
                if (t != null && Plugin.IsUnitAlive(t))
                    return t;
            }
            catch { }
            try
            {
                PersistentID tid = missile.targetID;
                Unit u;
                if (tid.IsValid && UnitRegistry.TryGetUnit(tid, out u) && Plugin.IsUnitAlive(u))
                    return u;
            }
            catch { }
            return null;
        }

        /// <summary>Wire vanilla ProxyFuse to current target so DetectCollisions can fire it.</summary>
        internal static void EnsureProxyWired(Missile missile)
        {
            if (!ShouldHandle(missile))
                return;
            Unit tgt = ResolveFuzeTarget(missile);
            if (tgt == null)
                return;
            try
            {
                Transform part = null;
                try { part = tgt.GetRandomPart(); }
                catch { }
                Transform tt = part != null ? part : tgt.transform;
                Rigidbody trb = null;
                try { trb = tgt.rb; }
                catch { }
                if (tt == null)
                    return;
                missile.SetProxyFuse(tt, trb);
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogWarning("YieldProximity EnsureProxy: " + ex.Message);
            }
        }

        /// <summary>
        /// Yield-scaled ConditionsMet (mirrors vanilla CPA snap + closing check).
        /// </summary>
        internal static bool ConditionsMetYield(object proxy, Vector3 targetVelocity, float missileSpeed)
        {
            if (proxy == null || ProxyTargetTransform == null || ProxyMissileTransform == null)
                return false;

            Transform targetTransform = ProxyTargetTransform.GetValue(proxy) as Transform;
            Transform missileTransform = ProxyMissileTransform.GetValue(proxy) as Transform;
            if (targetTransform == null || missileTransform == null)
                return false;

            Missile missile = missileTransform.GetComponentInParent<Missile>();
            if (missile == null || !ShouldHandle(missile))
                return false;

            float yield = GetMissileYield(missile);
            float range = RangeFromYield(yield);
            float trigger = ProximityFuzeMathService.TriggerRange(range, missileSpeed);

            Vector3 tPos = targetTransform.position;
            Vector3 mPos = missileTransform.position;
            if ((tPos - mPos).sqrMagnitude > trigger * trigger)
                return false;

            Vector3 tgtVel = targetVelocity;
            Rigidbody trb = ProxyTargetRb != null ? ProxyTargetRb.GetValue(proxy) as Rigidbody : null;
            if (trb != null)
                tgtVel = trb.velocity;

            Rigidbody mrb = ProxyMissileRb != null ? ProxyMissileRb.GetValue(proxy) as Rigidbody : null;
            if (mrb == null)
                return false;

            // Vanilla ProxyFuse.ConditionsMet geometry (CPA pass), yield only replaces speed*0.25.
            Vector3 toMissile = mPos - tPos;
            Vector3 relVel = mrb.velocity - tgtVel;
            bool passedCpa = ProximityFuzeMathService.PassedCpa(toMissile, relVel, Time.fixedDeltaTime);

            bool requireClosing = Plugin.ProximityRequireClosing == null
                || Plugin.ProximityRequireClosing.Value;
            if (!passedCpa)
            {
                if (requireClosing
                    && !ProximityFuzeMathService.InsideTightBubble(toMissile.sqrMagnitude, trigger))
                    return false;
            }
            else
            {
                Vector3 cpa = ProximityFuzeMathService.CpaSnapPosition(mPos, toMissile, relVel);
                missileTransform.position = cpa;
                mrb.MovePosition(cpa);
            }

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
            {
                Plugin.Log.LogInfo("YieldProx boom " + missile.name
                    + " yield=" + yield.ToString("0")
                    + " R=" + trigger.ToString("0.0")
                    + " d=" + toMissile.magnitude.ToString("0.0"));
            }
            return true;
        }

        private static readonly Dictionary<int, float> NextFallbackAt = new Dictionary<int, float>(32);
        private const float FallbackIntervalSec = 0.25f;

        /// <summary>
        /// Fallback when ProxyFuse was never created (seeker never called SetProxyFuse).
        /// Called from Missile.FixedUpdate Postfix but early-outs unless nuke/high-yield,
        /// and at most 4 Hz when active.
        /// </summary>
        internal static void TickFallback(Missile missile)
        {
            if (!ShouldHandle(missile))
                return;

            int id = missile.GetInstanceID();
            float now = Time.unscaledTime;
            float next;
            if (NextFallbackAt.TryGetValue(id, out next) && now < next)
                return;
            NextFallbackAt[id] = now + FallbackIntervalSec;

            // Prefer vanilla DetectCollisions path next frame.
            EnsureProxyWired(missile);

            object proxy = ProxyFuseField != null ? ProxyFuseField.GetValue(missile) : null;
            if (proxy != null)
                return; // DetectCollisions owns the trigger

            Unit tgt = ResolveFuzeTarget(missile);
            if (tgt == null)
                return;

            float yield = GetMissileYield(missile);
            float range = RangeFromYield(yield);
            Vector3 mPos = missile.transform.position;
            Vector3 tPos = tgt.transform.position;
            Vector3 delta = mPos - tPos;
            if (delta.sqrMagnitude > range * range)
                return;

            Vector3 mVel = missile.rb != null ? missile.rb.velocity : Vector3.zero;
            Vector3 tVel = tgt.rb != null ? tgt.rb.velocity : Vector3.zero;
            Vector3 rel = mVel - tVel;
            bool requireClosing = Plugin.ProximityRequireClosing == null
                || Plugin.ProximityRequireClosing.Value;
            if (requireClosing && Vector3.Dot(rel, delta) > 0f)
            {
                // Opening — only fire if already very close.
                float half = range * 0.35f;
                if (delta.sqrMagnitude > half * half)
                    return;
            }

            try
            {
                Vector3 n = mVel.sqrMagnitude > 1f ? mVel.normalized : Vector3.forward;
                missile.Detonate(n, false, false);
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogInfo("YieldProx fallback Detonate " + missile.name
                        + " R=" + range.ToString("0.0"));
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("YieldProx fallback: " + ex.Message);
            }
        }
    }

    /// <summary>Replace vanilla speed*0.25 proximity gate with yield-scaled range.</summary>
    [HarmonyPatch]
    internal static class Patch_ProxyFuse_ConditionsMet
    {
        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodBase TargetMethod()
        {
            Type t = AccessTools.Inner(typeof(Missile), "ProxyFuse");
            if (t == null)
                return null;
            return AccessTools.Method(t, "ConditionsMet", new Type[] { typeof(Vector3), typeof(float) });
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance, Vector3 targetVelocity, float missileSpeed, ref bool __result)
        {
            if (!YieldProximityFuze.Enabled)
                return true;
            if (__instance == null)
                return true;

            // If this proxy belongs to a missile we refuse to handle, leave vanilla alone.
            try
            {
                FieldInfo mt = AccessTools.Field(__instance.GetType(), "missileTransform");
                Transform tr = mt != null ? mt.GetValue(__instance) as Transform : null;
                Missile m = tr != null ? tr.GetComponentInParent<Missile>() : null;
                if (m != null && !YieldProximityFuze.ShouldHandle(m))
                    return true;
            }
            catch { }

            __result = YieldProximityFuze.ConditionsMetYield(__instance, targetVelocity, missileSpeed);
            return false;
        }
    }

    [HarmonyPatch(typeof(MissileSeeker), "Initialize")]
    internal static class Patch_Seeker_Initialize_YieldProx
    {
        [HarmonyPostfix]
        private static void Postfix(MissileSeeker __instance, Unit target)
        {
            if (!YieldProximityFuze.Enabled || __instance == null)
                return;
            if (Plugin.IsGunShellSeeker(__instance))
                return;
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return;
            // Target arg may be null on LOAL; ResolveFuzeTarget picks designated / hunt.
            YieldProximityFuze.EnsureProxyWired(missile);
        }
    }

    [HarmonyPatch(typeof(Missile), "SetTarget")]
    internal static class Patch_Missile_SetTarget_YieldProx
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, Unit target)
        {
            if (!YieldProximityFuze.Enabled || __instance == null || target == null)
                return;
            YieldProximityFuze.EnsureProxyWired(__instance);
        }
    }
}
