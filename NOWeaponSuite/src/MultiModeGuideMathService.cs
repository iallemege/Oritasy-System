using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield MultiMode GuideTo / soft-retarget / terminal / seeker-warm math (0.0.9.85).
    /// MultiModeBrain owns seeker reflection writes and SetAimpoint.
    /// </summary>
    internal static class MultiModeGuideMathService
    {
        internal const float SoftRetargetDefaultSec = 2.5f;
        internal const float PlayerSoftRetargetCapSec = 1.25f;
        internal const float FinBoostIntervalSec = 0.35f;
        internal const float MinHuntIntervalSec = 0.25f;
        internal const float DefaultMinGuideSpeedMps = 90f;

        internal enum GuidePath
        {
            Skip = 0,
            CoastSticky = 1,
            SyncLockOnly = 2,
            FullAim = 3
        }

        internal static GuidePath ResolveGuidePath(bool ejectedPilot, bool stickyOnly, bool deferKh85GuideTo)
        {
            if (ejectedPilot)
                return stickyOnly ? GuidePath.CoastSticky : GuidePath.Skip;
            if (deferKh85GuideTo)
                return GuidePath.SyncLockOnly;
            return GuidePath.FullAim;
        }

        internal static bool NeedsFullSeekerWarm(int targetId, int warmTargetId)
        {
            return targetId == 0 || targetId != warmTargetId;
        }

        internal static bool NeedsFinEnsure(bool stickyOnly, float now, float nextFinBoostAt)
        {
            return !stickyOnly || now >= nextFinBoostAt;
        }

        internal static float ScheduleNextFinBoost(float now)
        {
            return now + FinBoostIntervalSec;
        }

        /// <summary>
        /// Player re-lock used to skip this window and snap 90–180° — that stalled the airframe.
        /// Player/sticky still get a window, just a shorter one so it does not look like a loft-away.
        /// </summary>
        internal static float ScheduleSoftRetargetUntil(float now, float softSeconds, bool playerDesignated)
        {
            if (softSeconds <= 0f)
                return 0f;
            if (playerDesignated)
                softSeconds = Mathf.Min(softSeconds, PlayerSoftRetargetCapSec);
            return now + softSeconds;
        }

        internal static bool IsSoftRetargetWindow(
            bool playerDesignated,
            bool stickyOnly,
            float now,
            float softRetargetUntil)
        {
            // Args kept so call sites stay explicit: player/sticky used to be excluded here.
            if (playerDesignated || stickyOnly)
                return now < softRetargetUntil;
            return now < softRetargetUntil;
        }

        /// <summary>True when lock id changed, or we just came off coast / lost-lock.</summary>
        internal static bool ShouldOpenSoftRetarget(bool newTargetId, bool needsRelockSoft)
        {
            return newTargetId || needsRelockSoft;
        }

        internal static float ScheduleNextHunt(float now, float searchInterval)
        {
            return now + Mathf.Max(MinHuntIntervalSec, searchInterval);
        }

        internal static float MaxOffBoresightDeg(float speedMps, bool softRetarget)
        {
            return MaxOffBoresightDeg(speedMps, softRetarget, 0f);
        }

        /// <summary>
        /// Commanded off-boresight vs current velocity. High-speed 85° snaps were stalling on re-lock.
        /// Large aspect tightens further so the missile turns with energy instead of a max-G yank.
        /// </summary>
        internal static float MaxOffBoresightDeg(float speedMps, bool softRetarget, float aspectDeg)
        {
            float maxDeg;
            if (speedMps < 80f)
                maxDeg = 8f;
            else if (speedMps < 150f)
                maxDeg = Mathf.Lerp(8f, 22f, (speedMps - 80f) / 70f);
            else if (speedMps < 280f)
                maxDeg = Mathf.Lerp(22f, 38f, (speedMps - 150f) / 130f);
            else if (speedMps < 450f)
                maxDeg = Mathf.Lerp(38f, 48f, (speedMps - 280f) / 170f);
            else
                maxDeg = 50f;

            if (aspectDeg > 55f)
                maxDeg *= Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(55f, 120f, aspectDeg));
            if (softRetarget)
                maxDeg *= 0.55f;
            return Mathf.Clamp(maxDeg, 6f, 52f);
        }

        /// <summary>
        /// Direct chase only when already nearly aligned and energetic. Player/sticky re-lock
        /// must never skip the clamp — that was the stall.
        /// </summary>
        internal static bool AllowDirectChase(
            bool energyAware,
            bool terminal,
            float speedMps,
            float minGuideSpeedMps,
            float aspectDeg)
        {
            if (!energyAware)
                return true;
            if (!terminal)
                return false;
            if (speedMps < minGuideSpeedMps * 1.35f)
                return false;
            return aspectDeg <= 28f;
        }

        internal static Vector3 LeadPosition(Vector3 targetPos, Vector3 targetVel, float dist, float speed)
        {
            float tGo = Mathf.Clamp(dist / Mathf.Max(speed, 50f), 0.12f, 6f);
            return targetPos + targetVel * (tGo * 0.7f);
        }

        /// <summary>
        /// Collision-course lead with lag-pursuit fallback. Far intercept leads behind the
        /// missile (or 90° abeam) used to command a 180° pull on re-lock.
        /// </summary>
        internal static Vector3 LeadPosition(
            Vector3 missilePos,
            Vector3 missileFwd,
            float missileSpeed,
            Vector3 targetPos,
            Vector3 targetVel)
        {
            Vector3 toTgt = targetPos - missilePos;
            float dist = toTgt.magnitude;
            if (dist < 1f)
                return targetPos;

            Vector3 los = toTgt / dist;
            Vector3 fwd = missileFwd.sqrMagnitude > 0.01f ? missileFwd.normalized : los;
            float closing = Vector3.Dot(los, fwd * missileSpeed - targetVel);
            float tGo = closing > 25f
                ? dist / closing
                : dist / Mathf.Max(missileSpeed, 50f);
            tGo = Mathf.Clamp(tGo, 0.12f, 6f);

            float aspect = Vector3.Angle(fwd, los);
            float leadK = aspect > 55f || Vector3.Dot(fwd, los) < 0.15f
                ? 0.18f
                : 0.72f;
            return targetPos + targetVel * (tGo * leadK);
        }

        internal static float AspectDeg(Vector3 forward, Vector3 fromPos, Vector3 toPos)
        {
            Vector3 d = toPos - fromPos;
            if (d.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f)
                return 0f;
            return Vector3.Angle(forward, d);
        }

        /// <summary>Pure aim point in scene space (convert with ToGlobalPosition at call site).</summary>
        internal static Vector3 ComputeAimPoint(
            Vector3 missilePos,
            Vector3 forward,
            float speed,
            Vector3 leadPos,
            bool directChase,
            float minGuideSpeedMps,
            bool softRetarget)
        {
            return ComputeAimPoint(
                missilePos, forward, speed, leadPos, directChase,
                minGuideSpeedMps, softRetarget, Vector3.zero, 0.02f);
        }

        internal static Vector3 ComputeAimPoint(
            Vector3 missilePos,
            Vector3 forward,
            float speed,
            Vector3 leadPos,
            bool directChase,
            float minGuideSpeedMps,
            bool softRetarget,
            Vector3 lastCmdDir,
            float dt)
        {
            Vector3 desired = leadPos - missilePos;
            if (desired.sqrMagnitude < 0.01f)
                desired = forward;
            desired.Normalize();

            if (directChase)
                return leadPos;

            if (speed < minGuideSpeedMps)
            {
                Vector3 coast = Vector3.Slerp(forward, desired, 0.12f);
                if (coast.sqrMagnitude < 0.01f)
                    coast = forward;
                coast.Normalize();
                float lookSlow = Mathf.Clamp(speed * 2.5f, 250f, 1200f);
                Vector3 aim = missilePos + coast * lookSlow;
                // Mild loft only while recovering from a large off-axis — not when already tracking
                // (that looked like "locked but flies away").
                if (Vector3.Angle(forward, desired) > 25f && aim.y < missilePos.y + 20f)
                    aim.y = missilePos.y + 30f;
                return aim;
            }

            float ang = Vector3.Angle(forward, desired);
            float maxDeg = MaxOffBoresightDeg(speed, softRetarget, ang);
            Vector3 dir = desired;
            if (ang > maxDeg)
                dir = Vector3.RotateTowards(forward, desired, maxDeg * Mathf.Deg2Rad, 0f);
            dir.Normalize();

            if (lastCmdDir.sqrMagnitude > 0.01f && dt > 0.001f)
            {
                float maxDps = speed < 80f
                    ? 28f
                    : Mathf.Lerp(45f, 95f, Mathf.Clamp01((speed - 80f) / 320f));
                if (softRetarget)
                    maxDps *= 0.65f;
                float maxStep = maxDps * Mathf.Clamp(dt, 0.016f, 0.25f);
                float slewAng = Vector3.Angle(lastCmdDir, dir);
                if (slewAng > maxStep)
                    dir = Vector3.RotateTowards(lastCmdDir, dir, maxStep * Mathf.Deg2Rad, 0f);
            }

            float dist = Vector3.Distance(missilePos, leadPos);
            float look = Mathf.Clamp(speed * 2.0f, 400f, Mathf.Min(Mathf.Max(dist, 400f), 3500f));
            Vector3 aim2 = missilePos + dir.normalized * look;
            if (softRetarget && aim2.y < missilePos.y - 80f)
                aim2.y = Mathf.Lerp(aim2.y, missilePos.y, 0.35f);
            return aim2;
        }

        /// <summary>Cap gLimit during re-lock / low energy so vanilla Steering cannot dump speed.</summary>
        internal static float CapGLimitForEnergy(
            float currentG,
            float originalG,
            float speedMps,
            float minGuideSpeedMps,
            bool softRetarget,
            float aspectDeg)
        {
            // Unlimited / TBM: do not touch. Healthy intercept: do not undo EnsureFinsAndArm.
            if (originalG <= 0.01f)
                return currentG;
            float src = currentG > 0.01f ? currentG : originalG;
            if (speedMps < minGuideSpeedMps)
                return Mathf.Min(src, 8f);
            if (softRetarget || aspectDeg > 55f)
                return Mathf.Min(src, 14f);
            if (aspectDeg > 35f && speedMps < minGuideSpeedMps * 1.8f)
                return Mathf.Min(src, 18f);
            return currentG;
        }

        internal static bool WantTerminal(
            bool hasTarget,
            float distanceToTarget,
            float terminalRange,
            float speedMps,
            float minGuideSpeedMps,
            float now,
            float softRetargetUntil)
        {
            if (!hasTarget)
                return false;
            if (distanceToTarget > terminalRange)
                return false;
            if (speedMps < minGuideSpeedMps * 1.2f || now < softRetargetUntil)
                return false;
            return true;
        }

        /// <summary>Skip duplicate GuideTo SetAimpoint when already synced this FixedUpdate.</summary>
        internal static bool AimAlreadySyncedThisFrame(int aimSyncedFrame, int currentFrame)
        {
            return aimSyncedFrame == currentFrame;
        }

        /// <summary>Ship PD / VLS: wait for energy before inflating fins / G.</summary>
        internal static bool ShipFinBoostOk(bool shipLaunch, float ageSec, float speedMps)
        {
            return !shipLaunch || ageSec >= 1.2f || speedMps >= 90f;
        }

        internal static float WantFinArea(float finArea, bool shipBoostOk, bool energyOk)
        {
            float wantFin = finArea;
            if (shipBoostOk && energyOk && finArea >= 0.05f && finArea < 0.25f)
                wantFin = Mathf.Max(finArea, 0.35f);
            return wantFin;
        }

        internal static bool ShouldWriteCurrentFin(bool shipBoostOk, float wantFin, float currentFin)
        {
            return shipBoostOk && wantFin > 0.01f && currentFin < wantFin * 0.9f;
        }

        internal static bool ShouldRaiseGLimit(bool shipBoostOk, bool energyOk, float gLimit)
        {
            return shipBoostOk && energyOk && gLimit > 0.01f && gLimit < 5f;
        }

        internal static float FinDeployDelaySec(bool shipLaunch)
        {
            return shipLaunch ? 0.45f : 0.15f;
        }

        internal static bool EnergyOkForFinBoost(float speedMps, float minGuideSpeedMps)
        {
            float min = minGuideSpeedMps > 0f ? minGuideSpeedMps : DefaultMinGuideSpeedMps;
            return speedMps >= min;
        }
    }
}
