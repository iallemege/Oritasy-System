using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Crash guardian + terrain auto-off</summary>
    internal static partial class BeginnerAssist
    {
        private static void EvaluateSafety(Aircraft ac, bool apEngagedOnlyDepartures)
        {
            float now = Time.unscaledTime;
            if (now < _nextGuardianAt)
                return;
            if (_guardianHoldActive)
                return;
            // Landing AP + guardian STRAIGHT fight → departure / spin near the deck.
            // Also bail if a stale hold is somehow still marked active during LAND.
            if (PlayerAutopilot.IsLandingMode)
            {
                if (_guardianHoldActive || _boostCaptured)
                    YieldToAutopilotLand();
                return;
            }

            AirframeTune tune = ResolveTune(ac);
            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            float sink = 0f;
            try
            {
                if (ac.rb != null)
                    sink = -ac.rb.velocity.y;
            }
            catch { }
            float aoa = ReadAoA(ac);
            float speed = Mathf.Max(1f, ac.speed);
            float yawRate = ReadYawRateDeg(ac);
            float rollRate = ReadRollRateDeg(ac);
            float corner = ResolveCorner(ac);
            bool fallingLeaf = IsFallingLeaf(aoa, yawRate, rollRate, speed, corner, tune);

            float cd = _guardianCooldown != null ? Mathf.Max(0.8f, _guardianCooldown.Value) : 2.5f;
            float handback = _guardianHandback != null ? Mathf.Max(0.8f, _guardianHandback.Value) : 2f;
            CrashThreatClassifier.Result threat = CrashThreatClassifier.Classify(
                ac,
                _guardianOn,
                _terrainOn,
                apEngagedOnlyDepartures,
                tune.SpinAgl,
                tune.SpinYawRate,
                tune.StallAoa,
                tune.PostStallAgl,
                tune.SinkTrigger,
                corner,
                ResolveTakeoffSpd(ac),
                cd,
                handback,
                IsInvertedThreat(ac, tune),
                fallingLeaf,
                aoa,
                yawRate,
                rollRate,
                ralt,
                sink,
                speed);

            if (threat.Kind == CrashThreatClassifier.Kind.None)
                return;

            GuardianKind kind = GuardianKind.None;
            switch (threat.Kind)
            {
                case CrashThreatClassifier.Kind.Spin: kind = GuardianKind.Spin; break;
                case CrashThreatClassifier.Kind.InvertDive: kind = GuardianKind.InvertDive; break;
                case CrashThreatClassifier.Kind.PostStall: kind = GuardianKind.PostStall; break;
                case CrashThreatClassifier.Kind.Terrain: kind = GuardianKind.Terrain; break;
            }

            float holdAgl = threat.HoldAgl;
            float spd = threat.HoldSpeed;
            float holdSec = threat.HoldSeconds;
            string reason = threat.Reason;

            // Block re-entry only while holding; EndGuardianHold sets a short re-arm delay.
            _nextGuardianAt = now + holdSec + 0.15f;
            _guardianHoldActive = true;
            _guardianKind = kind;
            _guardianHandbackAt = now + holdSec;
            _guardianHadUserAp = PlayerAutopilot.IsEngaged;
            _guardianSavedMode = PlayerAutopilot.PeekModeOrdinal();
            CaptureGuardianBaselines(ac);
            ApplyGuardianLimitBoost(ac, kind != GuardianKind.Terrain);
            PlayerAutopilot.EngageStraightHold(holdAgl, spd);
            Flash("GUARDIAN  " + (reason != null ? reason : "PULL-UP")
                + "  [" + (tune.Key != null ? tune.Key : "?") + "]"
                + " (" + holdSec.ToString("0.0") + "s)");
        }

        /// <summary>
        /// Falling-leaf: post-stall wing rock — alternating roll with yaw, common when
        /// engine thrust returns after a thin-air flameout / thrust collapse.
        /// </summary>
        private static bool IsFallingLeaf(float aoa, float yawRate, float rollRate,
            float speed, float corner, AirframeTune tune)
        {
            if (aoa < tune.StallAoa * 0.4f)
                return false;
            if (speed > corner * 1.4f)
                return false;
            float absYaw = Mathf.Abs(yawRate);
            float absRoll = Mathf.Abs(rollRate);
            // Rocking wings + some yaw, or strong roll oscillation alone at high AoA.
            return (absRoll >= 32f && absYaw >= tune.SpinYawRate * 0.28f)
                || (absRoll >= 55f && aoa >= tune.StallAoa * 0.48f)
                || (absYaw >= tune.SpinYawRate * 0.62f && absRoll >= 20f && aoa >= tune.StallAoa * 0.42f);
        }

        private static void EndGuardianHold(Aircraft ac, bool flashHandback)
        {
            if (!_guardianHoldActive && !_boostCaptured)
                return;
            _guardianHoldActive = false;
            _guardianHandbackAt = -1f;
            _guardianKind = GuardianKind.None;
            float cd = _guardianCooldown != null ? Mathf.Max(0.8f, _guardianCooldown.Value) : 2.5f;
            // Short re-arm so a lingering departure can be caught immediately.
            _nextGuardianAt = Time.unscaledTime + Mathf.Min(cd, 1.2f);
            RestoreGuardianBaselines(ac);
            if (_guardianHadUserAp && PlayerAutopilot.IsEngaged)
            {
                // Keep F2 AP — restore prior mode (LAND/ORBIT/…) instead of killing it.
                PlayerAutopilot.RestoreModeAfterGuardian(_guardianSavedMode);
                if (flashHandback)
                    Flash("GUARDIAN  →  AP");
            }
            else if (flashHandback && PlayerAutopilot.IsEngaged)
            {
                PlayerAutopilot.DisengageFromOutside(true);
                Flash("GUARDIAN  →  PILOT");
            }
            else if (PlayerAutopilot.IsEngaged && ac == null)
                PlayerAutopilot.DisengageFromOutside(false);
            _guardianHadUserAp = false;
        }

        private static float InvertGroundAglBase()
        {
            float v = _invertGroundAgl != null ? _invertGroundAgl.Value : 190f;
            return Mathf.Clamp(v, 80f, 400f);
        }

        private static AirframeTune ResolveTune(Aircraft ac)
        {
            AirframeTune t;
            t.Key = Plugin.GetAircraftKey(ac);
            t.StovlNozzle = Plugin.IsStovlNozzleAircraft(ac);
            float bas = InvertGroundAglBase(); // default 190
            float mul = 1f;
            float stallAoa = 26f;
            float spinYaw = 55f;
            float postAgl = 900f;
            float spinAgl = 1200f;
            float sinkTrig = 26f;

            if (Plugin.IsSfbKey(t.Key) || Plugin.IsKr67Key(t.Key))
            {
                mul = 1.2f;
                stallAoa = 24f;
                spinYaw = 50f;
                sinkTrig = 30f;
            }
            else if (Plugin.IsFs12Key(t.Key))
            {
                mul = 1.12f;
                stallAoa = 25f;
                spinYaw = 52f;
            }
            else if (Plugin.IsFs20Key(t.Key))
            {
                mul = 1.05f;
                stallAoa = 27f;
                spinYaw = 58f;
                sinkTrig = 22f; // STOVL can arrest earlier with nozzles
            }
            else if (Plugin.IsEw25Key(t.Key))
            {
                mul = 1.08f;
                stallAoa = 28f;
                spinYaw = 50f;
                sinkTrig = 20f;
            }
            else if (Plugin.IsVt7Key(t.Key)
                || (t.Key != null && t.Key.IndexOf("Vagrant", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                mul = 1.0f;
                stallAoa = 30f;
                spinYaw = 60f;
                sinkTrig = 18f;
                postAgl = 700f;
            }
            else if (Plugin.IsUh90Key(t.Key)
                || (t.Key != null && t.Key.IndexOf("Chicane", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                mul = 0.7f;
                stallAoa = 35f;
                spinYaw = 70f;
                postAgl = 500f;
                spinAgl = 600f;
                sinkTrig = 14f;
            }
            else if (t.Key != null && (t.Key.IndexOf("Compass", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Key.IndexOf("T/A-30", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Key.IndexOf("TA-30", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                mul = 0.92f;
                stallAoa = 28f;
            }
            else if (Plugin.IsA19Key(t.Key))
            {
                mul = 1.15f;
                stallAoa = 23f;
                sinkTrig = 28f;
            }

            // Scale with this airframe's corner / landing speeds (each jet different).
            float corner = ResolveCorner(ac);
            float land = 70f;
            try
            {
                AircraftParameters p = ac.GetAircraftParameters();
                if (p != null)
                {
                    land = Mathf.Max(40f, p.landingSpeed);
                    if (p.verticalLanding)
                        mul = Mathf.Max(mul, 1f);
                }
            }
            catch { }
            float dyn = Mathf.Clamp(corner / 120f, 0.85f, 1.35f);
            t.InvertAgl = Mathf.Clamp(bas * mul * dyn, 100f, 320f);
            t.StallAoa = stallAoa;
            t.SpinYawRate = spinYaw;
            t.PostStallAgl = postAgl;
            t.SpinAgl = spinAgl;
            t.SinkTrigger = Mathf.Max(sinkTrig, land * 0.28f);
            return t;
        }

        internal static float ResolveCorner(Aircraft ac)
        {
            try
            {
                AircraftParameters p = ac.GetAircraftParameters();
                if (p != null && p.cornerSpeed > 10f)
                    return p.cornerSpeed;
            }
            catch { }
            return 120f;
        }

        private static float ResolveTakeoffSpd(Aircraft ac)
        {
            try
            {
                AircraftParameters p = ac.GetAircraftParameters();
                if (p != null && p.takeoffSpeed > 10f)
                    return p.takeoffSpeed;
            }
            catch { }
            return 70f;
        }

        private static bool IsInverted(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                // Belly toward sky (~101°+ bank). Old <0.2 treated ~78° combat banks as inverted.
                return Vector3.Dot(ac.transform.up, Vector3.up) < -0.2f;
            }
            catch { return false; }
        }

        /// <summary>
        /// Truly inverted with a dive / ground threat. Steep upright banks and low-alt BFM
        /// must not trigger invert-dive recovery (pitch-push while banked → crash).
        /// </summary>
        private static bool IsInvertedThreat(Aircraft ac, AirframeTune tune)
        {
            if (!IsInverted(ac))
                return false;
            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { return false; }
            if (ralt <= 1f)
                return false;

            float sink = 0f;
            try
            {
                if (ac.rb != null)
                    sink = -ac.rb.velocity.y;
            }
            catch { }
            float noseDown = 0f;
            try { noseDown = -ac.transform.forward.y; }
            catch { }
            float tti = ralt > 0.5f && sink > 2f ? ralt / sink : 999f;
            float upDot = 0f;
            try { upDot = Vector3.Dot(ac.transform.up, Vector3.up); }
            catch { }

            // Below ~35m only intervene if deeply inverted — otherwise terrain/sink paths own it.
            if (ralt < 35f && upDot > -0.55f)
                return false;

            // Look-ahead: steeper / faster dive raises the trigger ceiling (timelier).
            float diveMargin = Mathf.Clamp(sink * 2.2f + Mathf.Max(0f, noseDown) * 100f, 0f, 120f);
            float triggerAgl = tune.InvertAgl + diveMargin;

            if (ralt > triggerAgl)
                return false;

            // Require real dive evidence — do not fire on altitude alone while knife-edge / rolling.
            return (noseDown > 0.28f && sink > 10f)
                || (noseDown > 0.22f && tti < 4.5f)
                || (sink > 22f && tti < 3.8f)
                || (upDot < -0.55f && (noseDown > 0.18f || sink > 14f));
        }

        private static bool IsGuardianRecovered(Aircraft ac)
        {
            if (ac == null || !_guardianHoldActive)
                return false;
            AirframeTune tune = ResolveTune(ac);
            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            float aoa = ReadAoA(ac);
            float yaw = ReadYawRateDeg(ac);
            float sink = 0f;
            try
            {
                if (ac.rb != null)
                    sink = -ac.rb.velocity.y;
            }
            catch { }
            float noseDown = 0f;
            try { noseDown = -ac.transform.forward.y; }
            catch { }

            if (_guardianKind == GuardianKind.Spin)
            {
                float roll = ReadRollRateDeg(ac);
                // High-alt falling leaf: also require wing rock to calm before handback.
                return Mathf.Abs(yaw) < 25f && Mathf.Abs(roll) < 35f
                    && aoa < tune.StallAoa * 0.7f && sink < 40f;
            }
            if (_guardianKind == GuardianKind.PostStall)
                return aoa < tune.StallAoa * 0.55f && !IsInverted(ac)
                    && Mathf.Abs(ReadRollRateDeg(ac)) < 40f;
            if (_guardianKind == GuardianKind.InvertDive)
            {
                // Hand back once upright and not diving — do not require a climb above InvertAgl
                // (that kept false invert recoveries stuck pushing into the dirt at low AGL).
                return !IsInverted(ac) && sink < 18f && noseDown < 0.2f && ralt > 20f;
            }
            return false;
        }

        internal static float ReadYawRateDeg(Aircraft ac)
        {
            try
            {
                if (ac.rb == null)
                    return 0f;
                Vector3 local = ac.transform.InverseTransformDirection(ac.rb.angularVelocity);
                return local.y * Mathf.Rad2Deg;
            }
            catch { return 0f; }
        }

        internal static float ReadRollRateDeg(Aircraft ac)
        {
            try
            {
                if (ac.rb == null)
                    return 0f;
                Vector3 local = ac.transform.InverseTransformDirection(ac.rb.angularVelocity);
                return local.z * Mathf.Rad2Deg;
            }
            catch { return 0f; }
        }

        private static void CaptureGuardianBaselines(Aircraft ac)
        {
            GuardianRecoveryService.CaptureBaselines(ac);
        }

        private static void ApplyGuardianLimitBoost(Aircraft ac, bool inverted)
        {
            GuardianRecoveryService.ApplyLimitBoost(ac, inverted);
        }

        private static void RestoreGuardianBaselines(Aircraft ac)
        {
            GuardianRecoveryService.RestoreBaselines(ac);
        }

        /// <summary>
        /// Inverted near terrain: skyward aim + push (bunt) then roll upright.
        /// Stick-back while inverted pulls into the ground.
        /// </summary>
        private static void ApplyInvertedPullUp(Aircraft ac, AirframeTune tune)
        {
            GuardianRecoveryService.ApplyInvertedPullUp(ac, tune);
        }

        /// <summary>过失速改出: unload AoA, wings level, power as needed.</summary>
        private static void ApplyPostStallRecovery(Aircraft ac, AirframeTune tune)
        {
            GuardianRecoveryService.ApplyPostStallRecovery(ac, tune);
        }

        /// <summary>尾旋改出: opposite rudder, stick forward, ailerons neutral.</summary>
        private static void ApplySpinRecovery(Aircraft ac, AirframeTune tune)
        {
            GuardianRecoveryService.ApplySpinRecovery(ac, tune);
        }

        /// <summary>
        /// EW-25 / VT-7 / FS-20: vector nozzles (customAxis1 → hover/lift) + full thrust
        /// to arrest sink. When inverted, belly nozzles point skyward — still useful.
        /// Yields entirely while F2 LAND AP owns the aircraft.
        /// </summary>
        private static void ApplyStovlNozzleThrust(Aircraft ac, bool climb)
        {
            GuardianRecoveryService.ApplyStovlNozzleThrust(ac, climb);
        }

        private static Vector3 FlatTrack(Aircraft ac)
        {
            return LandingMath.FlatTrackDir(ac);
        }

        private static void AutoAim(Aircraft ac, Vector3 aimLocal, float holdAgl, bool followTerrain)
        {
            Autopilot ap = ac.autopilot;
            if (ap == null)
                return;
            AutopilotAim.AutoAim(ap, aimLocal.ToGlobalPosition(), true, false, false, 0.95f, 180f,
                followTerrain, holdAgl, Vector3.zero);
        }

        private static float ReadAoA(Aircraft ac)
        {
            try
            {
                Vector3 vel = ac.rb != null ? ac.rb.velocity : Vector3.zero;
                if (vel.sqrMagnitude < 1f)
                    return 0f;
                Vector3 local = ac.transform.InverseTransformDirection(vel.normalized);
                return Mathf.Atan2(local.y, local.z) * -Mathf.Rad2Deg;
            }
            catch { return 0f; }
        }
    }
}
