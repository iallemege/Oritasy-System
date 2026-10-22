using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    [HarmonyPatch(typeof(Aircraft), "GetRadarReturn")]
    internal static class Patch_Aircraft_GetRadarReturn_Rw
    {
        private static bool Prefix(
            Aircraft __instance,
            Vector3 source,
            Radar radar,
            Unit emitter,
            float dist,
            float clutter,
            RadarParams radarParams,
            bool triggerWarning,
            ref float __result)
        {
            if (!RadarWarfare.On())
                return true;
            float ecm = 0f;
            try { ecm = __instance.GetECMIntensity(); }
            catch { }
            // No ECM: vanilla GetRadarReturn. Harmony still runs, but skip a second
            // GetSignalStrength + directional math on every radar ping.
            if (ecm <= 0.001f)
                return true;
            Vector3 dir = FastMath.NormalizedDirection(source, __instance.transform.position);
            if (__instance.IsServer && triggerWarning)
                __instance.RpcGetRadarWarning(emitter);
            float raw = radarParams.GetSignalStrength(dir, dist, __instance.rb, __instance.RCS, clutter, 0f);
            if (RadarWarfare.B(RadarWarfare.FriendlyEcmExempt, true) && RadarWarfare.SameHq(emitter, __instance))
            {
                __result = raw;
                return false;
            }
            Vector3 toListener = source - __instance.transform.position;
            ecm *= RadarWarfare.DirectionalFactor(__instance.transform.forward, toListener);
            __result = RadarWarfare.SuppressSignal(raw, ecm, dist);
            return false;
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "GetRadarReturn")]
    internal static class Patch_ARHSeeker_GetRadarReturn_Rw
    {
        private static readonly FieldInfo FIsJammed = RwField.Get(typeof(ARHSeeker), "isJammed");
        private static readonly FieldInfo FTarget = RwField.Get(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo FJamAcc = RwField.Get(typeof(ARHSeeker), "jamAccumulation");
        private static readonly FieldInfo FJamTol = RwField.Get(typeof(ARHSeeker), "jamTolerance");

        private static void Prefix(ARHSeeker __instance)
        {
            if (!RadarWarfare.On())
                return;
            Unit target = RwField.Read<Unit>(FTarget, __instance);
            Aircraft ac = target as Aircraft;
            float ecm = 0f;
            if (ac != null)
                ecm = ac.GetECMIntensity();
            float jamAcc = RwField.Read<float>(FJamAcc, __instance);
            float jamTol = RwField.Read<float>(FJamTol, __instance);
            bool jammed = jamAcc > jamTol;
            if (ecm >= RadarWarfare.F(RadarWarfare.ArhEcmBlindThreshold, 2f)
                && RadarWarfare.B(RadarWarfare.ArhHomeOnJamFromEcm, true))
                jammed = true;
            RwField.Write<bool>(FIsJammed, __instance, jammed);
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "DatalinkMode")]
    internal static class Patch_ARHSeeker_Datalink_Rw
    {
        private static readonly FieldInfo FMissile = RwField.Get(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo FTarget = RwField.Get(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo FError = RwField.Get(typeof(ARHSeeker), "positionalErrorVector");

        private static void Postfix(ARHSeeker __instance)
        {
            if (!RadarWarfare.On())
                return;
            Missile missile = RwField.Read<Missile>(FMissile, __instance);
            Unit target = RwField.Read<Unit>(FTarget, __instance);
            if (missile == null || missile.NetworkHQ == null || target == null)
                return;
            bool accurate = false;
            try { accurate = missile.NetworkHQ.IsTargetPositionAccurate(target, RadarWarfare.DatalinkAccurateM); }
            catch { }
            Aircraft ac = target as Aircraft;
            float ecm = ac != null ? ac.GetECMIntensity() : 0f;
            Vector3 err = RwField.Read<Vector3>(FError, __instance);
            if (accurate && ecm < RadarWarfare.F(RadarWarfare.ArhEcmBlindThreshold, 2f))
                err *= Mathf.Max(0f, 1f - Time.deltaTime);
            else
            {
                float grow = RadarWarfare.F(RadarWarfare.DatalinkErrorGrowth, 24f) * Time.deltaTime * (1f + ecm);
                err += UnityEngine.Random.insideUnitSphere * grow;
            }
            if (err.magnitude > RadarWarfare.DatalinkErrorCapM)
                err = err.normalized * RadarWarfare.DatalinkErrorCapM;
            RwField.Write<Vector3>(FError, __instance, err);
        }
    }

    [HarmonyPatch(typeof(RadarChaff), "LaunchChaff")]
    internal static class Patch_RadarChaff_Launch_Rw
    {
        private static readonly FieldInfo FChaffTime = RwField.Get(typeof(RadarChaff), "chaffTime");

        private static void Postfix(RadarChaff __instance, Aircraft aircraft, Transform launchPoint, Vector3 launchVelocity)
        {
            if (!RadarWarfare.On() || __instance == null)
                return;
            RwField.Write<float>(FChaffTime, __instance, RadarWarfare.F(RadarWarfare.ChaffLifetime, 24f));
            RwChaffData data = __instance.GetComponent<RwChaffData>();
            if (data == null)
                data = __instance.gameObject.AddComponent<RwChaffData>();
            data.launchedAt = Time.timeSinceLevelLoad;
            data.launchVelocity = launchVelocity;
        }
    }

    [HarmonyPatch(typeof(SARHSeeker), "SARHSeeker_OnChaff")]
    internal static class Patch_SARH_OnChaff_Rw
    {
        private static readonly FieldInfo FMissile = RwField.Get(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo FTarget = RwField.Get(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo FParams = RwField.Get(typeof(SARHSeeker), "radarParams");
        private static readonly FieldInfo FJamAcc = RwField.Get(typeof(SARHSeeker), "jamAccumulation");
        private static readonly FieldInfo FJamTol = RwField.Get(typeof(SARHSeeker), "jamTolerance");

        private static bool Prefix(SARHSeeker __instance, RadarChaff source)
        {
            if (!RadarWarfare.On())
                return true;
            ApplyChaff(__instance, source, FMissile, FTarget, FParams, FJamAcc, FJamTol);
            return false;
        }

        internal static void ApplyChaff(
            object seeker,
            RadarChaff source,
            FieldInfo fMissile,
            FieldInfo fTarget,
            FieldInfo fParams,
            FieldInfo fAcc,
            FieldInfo fTol)
        {
            Missile missile = RwField.Read<Missile>(fMissile, seeker);
            Unit target = RwField.Read<Unit>(fTarget, seeker);
            if (missile == null || target == null || source == null)
                return;
            try
            {
                PersistentID tid = missile.targetID;
                if (tid.NotValid || (int)missile.seekerMode != 1)
                    return;
            }
            catch
            {
                return;
            }
            RadarParams rp = RwField.Read<RadarParams>(fParams, seeker);
            float dazzle = RadarWarfare.ChaffDazzle(missile, target, source, RwField.Read<float>(fTol, seeker), rp.maxRange);
            if (dazzle <= 0f)
                return;
            float acc = Mathf.Clamp01(RwField.Read<float>(fAcc, seeker) + dazzle);
            RwField.Write<float>(fAcc, seeker, acc);
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "ARHSeeker_OnChaff")]
    internal static class Patch_ARH_OnChaff_Rw
    {
        private static readonly FieldInfo FMissile = RwField.Get(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo FTarget = RwField.Get(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo FParams = RwField.Get(typeof(ARHSeeker), "radarParameters");
        private static readonly FieldInfo FJamAcc = RwField.Get(typeof(ARHSeeker), "jamAccumulation");
        private static readonly FieldInfo FJamTol = RwField.Get(typeof(ARHSeeker), "jamTolerance");

        private static bool Prefix(ARHSeeker __instance, RadarChaff source)
        {
            if (!RadarWarfare.On())
                return true;
            Patch_SARH_OnChaff_Rw.ApplyChaff(__instance, source, FMissile, FTarget, FParams, FJamAcc, FJamTol);
            return false;
        }
    }

    [HarmonyPatch(typeof(InertialSeekerShell), "SlowChecks")]
    internal static class Patch_Inertial_SlowChecks_Rw
    {
        private static readonly FieldInfo FMissile = RwField.Get(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo FTarget = RwField.Get(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo FUseDl = RwField.Get(typeof(InertialSeekerShell), "useDatalink");
        private static readonly FieldInfo FUseLaser = RwField.Get(typeof(InertialSeekerShell), "useLaser");
        private static readonly FieldInfo FWind = RwField.Get(typeof(InertialSeekerShell), "measuredWind");
        private static readonly FieldInfo FKnownPos = RwField.Get(typeof(InertialSeekerShell), "knownPos");
        private static readonly FieldInfo FError = RwField.Get(typeof(InertialSeekerShell), "errorAccumulation");
        private static readonly FieldInfo FTtt = RwField.Get(typeof(InertialSeekerShell), "timeToTarget");
        private static readonly FieldInfo FTraj = RwField.Get(typeof(InertialSeekerShell), "trajectoryError");

        private static bool Prefix(InertialSeekerShell __instance)
        {
            if (!RadarWarfare.On())
                return true;
            Missile missile = RwField.Read<Missile>(FMissile, __instance);
            if (missile == null)
                return false;
            if (missile.disabled)
                return false;
            if (missile.speed < 1f && missile.IsArmed())
                missile.Detonate(missile.rb.velocity, false, true);
            if (!missile.IsTangible() && missile.timeSinceSpawn > 0.9f)
                missile.SetTangible(true);
            try
            {
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                if (level != null)
                    RwField.Write<Vector3>(FWind, __instance, level.GetWind());
            }
            catch { }
            missile.UpdateRadarAlt();
            GlobalPosition known = RwField.Read<GlobalPosition>(FKnownPos, __instance);
            Vector3 delta = known - GlobalPositionExtensions.GlobalPosition(missile);
            float fall = Kinematics.FallTime(-delta.y, missile.rb.velocity.y);
            Vector3 horiz = new Vector3(delta.x, 0f, delta.z);
            float horizMag = horiz.magnitude;
            Vector3 velH = new Vector3(missile.rb.velocity.x, 0f, missile.rb.velocity.z);
            float close = Vector3.Dot(horiz.normalized, velH);
            float ttt = Mathf.Max(horizMag, 10f) / Mathf.Max(close, 10f);
            RwField.Write<float>(FTtt, __instance, ttt);
            RwField.Write<float>(FTraj, __instance, Mathf.Clamp(ttt / Mathf.Max(fall, 0.01f), 0.5f, 1.5f));
            float rate = RadarWarfare.F(RadarWarfare.InsDriftRate, 0.024f) * Time.deltaTime;
            Unit target = RwField.Read<Unit>(FTarget, __instance);
            bool tracked = false;
            bool useDl = RwField.Read<bool>(FUseDl, __instance);
            bool useLaser = RwField.Read<bool>(FUseLaser, __instance);
            if (useDl && missile.NetworkHQ != null && target != null)
            {
                try { tracked = missile.NetworkHQ.IsTargetBeingTracked(target); }
                catch { }
            }
            if (!tracked && !useLaser)
                rate *= Mathf.Max(RadarWarfare.F(RadarWarfare.InsDriftLostMult, 5f), 1f);
            float acc = Mathf.Clamp01(RwField.Read<float>(FError, __instance) + rate);
            if (Vector3.Angle(missile.transform.forward, delta) < RadarWarfare.InsAlignAngle)
            {
                if (tracked)
                    acc = Mathf.Min(acc, RadarWarfare.InsTrackErrorCap);
                if (useLaser && missile.NetworkHQ != null && target != null)
                {
                    try
                    {
                        if (missile.NetworkHQ.IsTargetLased(target))
                            acc = 0f;
                    }
                    catch { }
                }
            }
            RwField.Write<float>(FError, __instance, acc);
            return false;
        }
    }

    [HarmonyPatch(typeof(SARHSeeker), "SearchMode")]
    internal static class Patch_SARH_SearchMode_Rw
    {
        private static readonly FieldInfo FKnownPos = RwField.Get(typeof(SARHSeeker), "knownPos");
        private static readonly FieldInfo FKnownVel = RwField.Get(typeof(SARHSeeker), "knownVel");

        private static void Postfix(SARHSeeker __instance)
        {
            if (!RadarWarfare.On())
                return;
            float drift = RadarWarfare.F(RadarWarfare.SarhMemoryDrift, 70f);
            if (drift > 0f)
            {
                GlobalPosition pos = RwField.Read<GlobalPosition>(FKnownPos, __instance);
                pos += UnityEngine.Random.insideUnitSphere * drift * Time.fixedDeltaTime;
                RwField.Write<GlobalPosition>(FKnownPos, __instance, pos);
            }
            Vector3 vel = RwField.Read<Vector3>(FKnownVel, __instance);
            vel *= Mathf.Max(0f, 1f - 1.5f * Time.fixedDeltaTime);
            RwField.Write<Vector3>(FKnownVel, __instance, vel);
        }
    }

    [HarmonyPatch(typeof(JammingPod), "FixedUpdate")]
    internal static class Patch_JammingPod_FixedUpdate_Rw
    {
        private const int MaxArea = 8;
        private const int MaxMissiles = 8;
        private static readonly FieldInfo FTarget = RwField.Get(typeof(Weapon), "currentTarget");
        private static readonly FieldInfo FPower = RwField.Get(typeof(JammingPod), "power");
        private static readonly FieldInfo FFalloff = RwField.Get(typeof(JammingPod), "rangeFalloff");
        private static readonly FieldInfo FDir = RwField.Get(typeof(JammingPod), "directionTransform");
        private static readonly FieldInfo FSupply = RwField.Get(typeof(JammingPod), "powerSupply");
        private static readonly FieldInfo FLast = RwField.Get(typeof(JammingPod), "lastJammingTick");
        private static readonly FieldInfo FRewardAmt = RwField.Get(typeof(JammingPod), "rewardAmount");
        private static readonly FieldInfo FRewardCnt = RwField.Get(typeof(JammingPod), "rewardCount");
        private static readonly FieldInfo FRewardTh = RwField.Get(typeof(JammingPod), "rewardThreshold");

        private static bool Prefix(JammingPod __instance)
        {
            if (!RadarWarfare.On())
                return true;
            Unit owner = __instance.attachedUnit;
            Unit locked = RwField.Read<Unit>(FTarget, __instance);
            PowerSupply supply = RwField.Read<PowerSupply>(FSupply, __instance);
            Transform dirT = RwField.Read<Transform>(FDir, __instance);
            AnimationCurve falloff = RwField.Read<AnimationCurve>(FFalloff, __instance);
            float power = RwField.Read<float>(FPower, __instance);
            Vector3 aim = locked != null
                ? locked.transform.position - __instance.transform.position
                : __instance.transform.forward;
            if (aim.sqrMagnitude < 0.0001f)
                aim = __instance.transform.forward;
            if (dirT != null)
            {
                if (aim.sqrMagnitude < 0.0001f)
                    aim = dirT.rotation * Vector3.forward;
                dirT.rotation = Quaternion.LookRotation(aim);
            }
            float powerRatio = 1f;
            if (supply != null)
                powerRatio = supply.DrawPower(power) / Mathf.Max(power, 0.1f);
            if (powerRatio <= 0.05f)
                return false;
            if (owner == null)
                return false;
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                if (nm == null || nm.Server == null || !nm.Server.Active)
                    return false;
            }
            catch
            {
                return false;
            }
            float last = RwField.Read<float>(FLast, __instance);
            if (Time.timeSinceLevelLoad - last < 0.2f)
                return false;
            RwField.Write<float>(FLast, __instance, Time.timeSinceLevelLoad);
            Vector3 origin = __instance.transform.position;
            if (locked != null && owner.NetworkHQ != null)
            {
                bool tracked = false;
                try { tracked = owner.NetworkHQ.IsTargetBeingTracked(locked); }
                catch { }
                if (tracked && !Physics.Linecast(origin, locked.transform.position, PhysicsLayers.StaticsMask))
                {
                    float amt = 0f;
                    if (falloff != null)
                        amt = falloff.Evaluate(Vector3.Distance(origin, locked.transform.position)) * powerRatio;
                    RadarWarfare.JamUnit(locked, owner, amt);
                }
            }
            if (RadarWarfare.B(RadarWarfare.PodAreaJam, true) && owner.NetworkHQ != null)
                AreaJam(__instance, owner, origin, aim, falloff, powerRatio);
            if (RadarWarfare.B(RadarWarfare.PodJamMissiles, true) && owner.NetworkHQ != null)
                MissileJam(owner, origin, falloff, powerRatio);
            TryReward(__instance, owner, locked, powerRatio);
            return false;
        }

        private static void AreaJam(
            JammingPod pod,
            Unit owner,
            Vector3 origin,
            Vector3 aim,
            AnimationCurve falloff,
            float powerRatio)
        {
            float range = RadarWarfare.F(RadarWarfare.PodMaxRangeKm, 44f) * 1000f;
            float half = RadarWarfare.F(RadarWarfare.PodConeHalfAngle, 70f);
            int n = 0;
            try
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null || object.ReferenceEquals(hq, owner.NetworkHQ))
                        continue;
                    if (hq.factionRadarReturn == null)
                        continue;
                    for (int i = 0; i < hq.factionRadarReturn.Count && n < MaxArea; i++)
                    {
                        PersistentID pid = hq.factionRadarReturn[i];
                        Unit unit;
                        if (!UnitRegistry.TryGetUnit(pid, out unit) || unit == null || unit.disabled)
                            continue;
                        if (unit.radar == null)
                            continue;
                        if (FastMath.OutOfRange(
                            GlobalPositionExtensions.GlobalPosition(unit),
                            GlobalPositionExtensions.GlobalPosition(owner),
                            range))
                            continue;
                        Vector3 to = unit.transform.position - origin;
                        if (half < 180f && Vector3.Angle(aim, to) > half)
                            continue;
                        if (Physics.Linecast(origin, unit.transform.position, PhysicsLayers.StaticsMask))
                            continue;
                        float amt = 0f;
                        if (falloff != null)
                            amt = falloff.Evaluate(to.magnitude) * powerRatio;
                        RadarWarfare.JamUnit(unit, owner, amt);
                        n++;
                    }
                    if (n >= MaxArea)
                        break;
                }
            }
            catch { }
        }

        private static void MissileJam(Unit owner, Vector3 origin, AnimationCurve falloff, float powerRatio)
        {
            float range = RadarWarfare.F(RadarWarfare.PodJamMissileRangeKm, 50f) * 1000f;
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { return; }
            if (all == null)
                return;
            int n = 0;
            for (int i = 0; i < all.Count && n < MaxMissiles; i++)
            {
                Missile missile = all[i] as Missile;
                if (missile == null || missile.disabled)
                    continue;
                try
                {
                    PersistentID tid = missile.targetID;
                    if (tid.NotValid)
                        continue;
                    Unit tgt;
                    if (!UnitRegistry.TryGetUnit(tid, out tgt) || tgt == null)
                        continue;
                    if (!RadarWarfare.SameHq(tgt, owner))
                        continue;
                    if (FastMath.OutOfRange(
                        GlobalPositionExtensions.GlobalPosition(missile),
                        GlobalPositionExtensions.GlobalPosition(owner),
                        range))
                        continue;
                    if (Physics.Linecast(origin, missile.transform.position, PhysicsLayers.StaticsMask))
                        continue;
                    float amt = 0f;
                    if (falloff != null)
                        amt = falloff.Evaluate(Vector3.Distance(origin, missile.transform.position)) * powerRatio;
                    RadarWarfare.JamUnit(missile, owner, amt);
                    n++;
                }
                catch { }
            }
        }

        private static void TryReward(JammingPod pod, Unit owner, Unit locked, float powerRatio)
        {
            Aircraft ac = owner as Aircraft;
            if (ac == null || locked == null)
                return;
            try
            {
                if (ac.Player == null || !locked.HasRadarEmission())
                    return;
                if (locked.NetworkHQ == null || RadarWarfare.SameHq(locked, owner))
                    return;
            }
            catch
            {
                return;
            }
            float count = RwField.Read<float>(FRewardCnt, pod) + powerRatio * 0.2f;
            float amount = RwField.Read<float>(FRewardAmt, pod);
            float value = 1f;
            try
            {
                if (locked.definition != null)
                    value = locked.definition.value;
            }
            catch { }
            amount += 0.0001f * powerRatio * Mathf.Sqrt(Mathf.Max(value, 0f));
            float thresh = RwField.Read<float>(FRewardTh, pod);
            if (thresh <= 0f)
                thresh = 60f;
            if (count > thresh)
            {
                try { owner.NetworkHQ.ReportJammingAction(ac.Player, locked, amount); }
                catch { }
                count = 0f;
                amount = 0f;
            }
            RwField.Write<float>(FRewardCnt, pod, count);
            RwField.Write<float>(FRewardAmt, pod, amount);
        }
    }

    [HarmonyPatch(typeof(RadarJammer), "Fire")]
    internal static class Patch_RadarJammer_Fire_Rw
    {
        private static bool Prefix(RadarJammer __instance)
        {
            if (!RadarWarfare.On())
                return true;
            Aircraft ac = __instance.aircraft;
            if (ac == null)
                return true;
            if (JammerQte.On())
            {
                if (JammerQte.TryBegin(ac))
                    return false;
            }
            else if (DirjamBridge.IsActiveFor(ac) || DirjamBridge.ShouldSuppressHold(ac))
            {
                return false;
            }
            RwContinuousJammer jam = RwContinuousJammer.Ensure(ac);
            if (jam != null)
                jam.Activate();
            return false;
        }
    }

    [HarmonyPatch(typeof(RadarJammer), "Update")]
    internal static class Patch_RadarJammer_Update_Rw
    {
        private static bool Prefix(RadarJammer __instance)
        {
            if (!RadarWarfare.On())
                return true;
            if (JammerQte.IsActiveFor(__instance.aircraft)
                || DirjamBridge.IsActiveFor(__instance.aircraft))
                return false;
            RwContinuousJammer jam = RwContinuousJammer.Get(__instance.aircraft);
            if (jam != null && jam.active)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Countermeasures", new Type[] { typeof(bool), typeof(byte) })]
    internal static class Patch_Aircraft_Countermeasures_Rw
    {
        private static bool Prefix(Aircraft __instance, bool active)
        {
            if (!active || __instance == null || !RadarWarfare.On())
                return true;
            if (!IsRadarJammerSelected(__instance))
                return true;
            if (JammerQte.On() && JammerQte.TryBegin(__instance))
                return false;
            return true;
        }

        private static void Postfix(Aircraft __instance, bool active)
        {
            if (!RadarWarfare.On() || __instance == null)
                return;
            if (!IsRadarJammerSelected(__instance))
                return;
            if (JammerQte.ShouldSuppressHold(__instance) || DirjamBridge.ShouldSuppressHold(__instance))
                return;
            RwContinuousJammer jam = RwContinuousJammer.Get(__instance);
            if (jam != null)
                jam.SetHold(active);
        }

        private static bool IsRadarJammerSelected(Aircraft aircraft)
        {
            try
            {
                if (aircraft.countermeasureManager == null)
                    return false;
                Countermeasure cm = aircraft.countermeasureManager.GetActiveCountermeasure();
                return cm is RadarJammer;
            }
            catch
            {
                return false;
            }
        }
    }
}
