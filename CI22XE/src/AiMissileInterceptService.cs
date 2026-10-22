using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Opposing AI fires AAMs at missiles the local player launched. AI-vs-AI
    /// shots are left alone (evade / vanilla). Seekers still need WeXon IFF
    /// to allow missile-on-missile SetTarget for those shots.
    /// </summary>
    internal static class AiMissileInterceptService
    {
        internal const float MaxRangeM = 16000f;
        internal const float MinRangeM = 220f;
        internal const float FireConeDeg = 32f;
        internal const float ScanInterval = 0.12f;
        internal const float SpawnBindSec = 2.2f;

        private static readonly List<Missile> PlayerShots = new List<Missile>(24);
        private static readonly FieldInfo MissileTargetField =
            AccessTools.Field(typeof(Missile), "target");
        private static float _nextScan;
        private static Unit _pendingOwner;
        private static Unit _pendingTgt;
        private static float _pendingAt;

        internal static bool IsOn()
        {
            return AiCombatBrain.InterceptPlayerMissilesOn();
        }

        internal static bool IsPlayerFired(Missile m)
        {
            if (m == null)
                return false;
            try
            {
                if (m.disabled)
                    return false;
            }
            catch { return false; }

            Unit owner = null;
            try { owner = m.owner; }
            catch { owner = null; }
            if (owner == null)
            {
                try
                {
                    Unit resolved;
                    if (UnitRegistry.TryGetUnit(m.ownerID, out resolved))
                        owner = resolved;
                }
                catch { }
            }
            Aircraft ac = owner as Aircraft;
            if (ac == null && owner != null)
            {
                try { ac = owner.GetComponentInParent<Aircraft>(); }
                catch { ac = null; }
            }
            if (ac == null)
                return false;
            Player p = null;
            try { p = ac.Player; }
            catch { p = null; }
            if (p == null)
                return false;
            return Plugin.IsLocalHumanPlayer(p);
        }

        internal static bool ShooterIsLocalPlayer(Unit shooter)
        {
            if (shooter == null)
                return false;
            Aircraft ac = shooter as Aircraft;
            if (ac == null)
            {
                try { ac = shooter.GetComponentInParent<Aircraft>(); }
                catch { ac = null; }
            }
            if (ac == null)
                return false;
            Player p = null;
            try { p = ac.Player; }
            catch { p = null; }
            if (p == null)
                return false;
            return Plugin.IsLocalHumanPlayer(p);
        }

        internal static void Refresh()
        {
            if (Time.time < _nextScan)
                return;
            _nextScan = Time.time + ScanInterval;
            PlayerShots.Clear();
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { all = null; }
            if (all == null)
                return;
            for (int i = 0; i < all.Count; i++)
            {
                Missile m = all[i] as Missile;
                if (m == null)
                    continue;
                if (!IsPlayerFired(m))
                    continue;
                PlayerShots.Add(m);
            }
        }

        internal static Missile Pick(Aircraft ac)
        {
            if (ac == null || !IsOn())
                return null;
            bool a2a, a2g, gun;
            AiCombatMathService.ScanLoadout(ac, out a2a, out a2g, out gun);
            if (!a2a)
                return null;
            Refresh();
            Missile best = null;
            float bestScore = 0f;
            for (int i = 0; i < PlayerShots.Count; i++)
            {
                Missile m = PlayerShots[i];
                if (!AiCombatMathService.ValidTarget(m, ac))
                    continue;
                if (SameHq(ac, m) || SameHq(ac, MissileOwner(m)))
                    continue;
                float score = Score(ac, m);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            if (bestScore < 800f)
                return null;
            return best;
        }

        internal static bool TryPick(Aircraft ac, out Unit intercept, out float score)
        {
            intercept = null;
            score = -1f;
            Missile m = Pick(ac);
            if (m == null)
                return false;
            intercept = m;
            score = Score(ac, m);
            return true;
        }

        internal static Unit MissileOwner(Missile m)
        {
            if (m == null)
                return null;
            Unit owner = null;
            try { owner = m.owner; }
            catch { owner = null; }
            if (owner != null)
                return owner;
            try
            {
                Unit resolved;
                if (UnitRegistry.TryGetUnit(m.ownerID, out resolved))
                    return resolved;
            }
            catch { }
            return null;
        }

        internal static bool SameHq(Unit a, Unit b)
        {
            if (a == null || b == null)
                return false;
            try
            {
                if (a.NetworkHQ == null || b.NetworkHQ == null)
                    return false;
                return object.ReferenceEquals(a.NetworkHQ, b.NetworkHQ);
            }
            catch { return false; }
        }

        internal static float Score(Aircraft ac, Missile m)
        {
            if (ac == null || m == null)
                return -1f;
            Vector3 myPos = ac.transform.position;
            Vector3 mPos = m.transform.position;
            float dist = Vector3.Distance(myPos, mPos);
            if (dist > MaxRangeM || dist < MinRangeM * 0.45f)
                return -1f;

            Vector3 mVel = Vector3.zero;
            try
            {
                if (m.rb != null)
                    mVel = m.rb.velocity;
            }
            catch { }

            Vector3 toMe = myPos - mPos;
            float closing = 0f;
            if (toMe.sqrMagnitude > 1f)
                closing = Vector3.Dot(mVel, toMe.normalized);

            bool incoming = closing > 60f;
            bool aimedAtUs = MissileAimedAt(m, ac);
            bool near = dist < 9000f;
            if (!incoming && !aimedAtUs && !near)
                return -1f;

            float score = 50000f - dist;
            if (incoming)
                score += 35000f;
            if (aimedAtUs)
                score += 55000f;
            if (dist < 4000f)
                score += 8000f;
            return score;
        }

        internal static bool MissileAimedAt(Missile m, Unit victim)
        {
            if (m == null || victim == null)
                return false;
            try
            {
                if (m.targetID == victim.persistentID)
                    return true;
            }
            catch { }
            try
            {
                Unit tgt = null;
                FieldInfoTgt(m, out tgt);
                if (tgt != null && object.ReferenceEquals(tgt, victim))
                    return true;
            }
            catch { }
            return false;
        }

        private static void FieldInfoTgt(Missile m, out Unit tgt)
        {
            tgt = null;
            if (MissileTargetField == null || m == null)
                return;
            try { tgt = MissileTargetField.GetValue(m) as Unit; }
            catch { tgt = null; }
        }

        internal static bool IncomingAt(Unit analyzer, Missile m)
        {
            if (analyzer == null || m == null)
                return false;
            if (MissileAimedAt(m, analyzer))
                return true;
            try
            {
                Vector3 toMe = analyzer.transform.position - m.transform.position;
                Vector3 vel = m.rb != null ? m.rb.velocity : Vector3.zero;
                if (toMe.sqrMagnitude < 1f)
                    return true;
                return Vector3.Dot(vel, toMe.normalized) > 80f;
            }
            catch { return false; }
        }

        internal static void NotePending(Unit owner, Unit tgt)
        {
            if (owner == null || tgt == null)
                return;
            _pendingOwner = owner;
            _pendingTgt = tgt;
            _pendingAt = Time.time;
        }

        internal static void OnSpawned(Missile shot, Unit owner)
        {
            if (shot == null || _pendingTgt == null)
                return;
            if (Time.time - _pendingAt > SpawnBindSec)
                return;
            bool ours = false;
            if (owner != null && object.ReferenceEquals(owner, _pendingOwner))
                ours = true;
            if (!ours && _pendingOwner != null)
            {
                try
                {
                    if (shot.owner != null
                        && object.ReferenceEquals(shot.owner, _pendingOwner))
                        ours = true;
                }
                catch { }
            }
            if (!ours)
                return;
            Missile tgt = _pendingTgt as Missile;
            if (tgt == null || tgt.disabled)
                return;
            try { shot.SetTarget(tgt); }
            catch { }
            try { shot.SetProxyFuse(tgt.transform, tgt.rb); }
            catch { }
            try { shot.Arm(); }
            catch { }
            _pendingTgt = null;
            _pendingOwner = null;
        }
    }

    [HarmonyPatch(typeof(CombatAI), "AnalyzeTarget")]
    internal static class Patch_AnalyzeTarget_PlayerMissileOnly
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            WeaponStation weaponStation,
            Unit analyzer,
            TrackingInfo trackingInfo,
            ref OpportunityThreat __result)
        {
            if (trackingInfo == null)
                return;
            Unit unit = null;
            try
            {
                if (!trackingInfo.TryGetUnit(out unit) || unit == null)
                    return;
            }
            catch { return; }
            Missile m = unit as Missile;
            if (m == null)
                return;
            if (!AiMissileInterceptService.IsOn())
                return;
            if (!AiMissileInterceptService.IsPlayerFired(m)
                || AiMissileInterceptService.SameHq(analyzer, m)
                || AiMissileInterceptService.SameHq(analyzer, AiMissileInterceptService.MissileOwner(m)))
            {
                __result = new OpportunityThreat(0f, __result.threat);
                return;
            }
            if (weaponStation == null || weaponStation.WeaponInfo == null
                || !AiCombatMathService.WeaponFitsTarget(weaponStation.WeaponInfo, m))
            {
                __result = new OpportunityThreat(0f, __result.threat);
                return;
            }
            float dist = 8000f;
            try
            {
                if (analyzer != null)
                    dist = Vector3.Distance(analyzer.transform.position, m.transform.position);
            }
            catch { }
            float opp = 2.4f * Mathf.Clamp01(1f - dist / 18000f);
            if (opp < 0.55f)
                opp = 0.55f;
            if (AiMissileInterceptService.IncomingAt(analyzer, m))
                opp *= 2.2f;
            if (__result.opportunity > opp)
                opp = __result.opportunity;
            __result = new OpportunityThreat(opp, Mathf.Max(__result.threat, opp));
        }
    }

    [HarmonyPatch(typeof(Spawner))]
    internal static class Patch_Spawner_BindIntercept
    {
        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[]
        {
            typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion),
            typeof(Vector3), typeof(Unit), typeof(Unit)
        })]
        private static void PostfixDef(Missile __result, Unit owner)
        {
            AiMissileInterceptService.OnSpawned(__result, owner);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[]
        {
            typeof(GameObject), typeof(Vector3), typeof(Quaternion),
            typeof(Vector3), typeof(Unit), typeof(Unit)
        })]
        private static void PostfixGo(Missile __result, Unit owner)
        {
            AiMissileInterceptService.OnSpawned(__result, owner);
        }
    }

    [HarmonyPatch(typeof(CombatAI), "InterceptViability")]
    internal static class Patch_InterceptViability_PlayerMissileOnly
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Unit target, Unit analyzer, ref float __result)
        {
            Missile m = target as Missile;
            if (m == null)
                return;
            if (!AiMissileInterceptService.IsOn())
                return;
            if (!AiMissileInterceptService.IsPlayerFired(m)
                || AiMissileInterceptService.SameHq(analyzer, m)
                || AiMissileInterceptService.SameHq(analyzer, AiMissileInterceptService.MissileOwner(m)))
            {
                __result = 0f;
                return;
            }
            if (__result < 0.35f)
                __result = 0.65f;
        }
    }

    [HarmonyPatch(typeof(CombatAI), "LookForMissileTargets")]
    internal static class Patch_LookForMissileTargets_PlayerOnly
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Aircraft aircraft, List<Unit> outTargets)
        {
            if (outTargets == null || !AiMissileInterceptService.IsOn())
                return;
            for (int i = outTargets.Count - 1; i >= 0; i--)
            {
                Missile m = outTargets[i] as Missile;
                if (m == null)
                    continue;
                if (!AiMissileInterceptService.IsPlayerFired(m)
                    || AiMissileInterceptService.SameHq(aircraft, m)
                    || AiMissileInterceptService.SameHq(aircraft, AiMissileInterceptService.MissileOwner(m)))
                    outTargets.RemoveAt(i);
            }
        }
    }
}
