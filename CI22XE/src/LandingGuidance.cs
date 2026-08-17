using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield landing guidance (0.0.9.58+): base join, CV 五边, VTOL pad, winged final.
    /// Owns approach geometry / AutoAim; energy via LandingEnergyLaws; phase math via LandingFinalMathService;
    /// acquisition via AirbaseLocator.
    /// </summary>
    internal static class LandingGuidance
    {
        internal static void Apply(Aircraft ac, Autopilot ap, ControlInputs inputs,
            AircraftParameters parms, float landSpd, float turnR, float cruise)
        {
            PlayerAutopilot.AirframeCondition af = PlayerAutopilot.SampleAirframe(ac);
            float baseLand = landSpd;
            landSpd = PlayerAutopilot.EffectiveLandSpeed(baseLand, af);
            // STOVL pad ops keep higher jet-borne margin. LAND CV is winged — do not inflate
            // approach speed into an ~85 m/s deck dump (live 24C: oscillate 20–45 AGL then slam).
            bool stovl = false;
            try
            {
                stovl = Plugin.IsStovlNozzleAircraft(ac)
                    || (parms != null && parms.verticalLanding);
            }
            catch { }
            landSpd *= LandingFinalMathService.LandSpeedEnergyMul(
                stovl, PlayerAutopilot.IsLandCarrierMode);
            float sinkScale = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(af.Severity));
            float bankScale = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(af.Severity));
            float dmgApBoost = af.Severity * 180f;

            // LAND CV: never keep a land-only airbase; re-scan carriers periodically.
            if (PlayerAutopilot.IsLandCarrierMode
                && (PlayerAutopilot._landBase == null || PlayerAutopilot._landBase.disabled || !PlayerAutopilot.IsCarrierAirbase(PlayerAutopilot._landBase)
                    || Time.unscaledTime >= PlayerAutopilot._cvNextCarrierRescan))
            {
                if (PlayerAutopilot._landBase == null || PlayerAutopilot._landBase.disabled || !PlayerAutopilot.IsCarrierAirbase(PlayerAutopilot._landBase))
                {
                    PlayerAutopilot.ResolveLand(ac, true);
                    PlayerAutopilot._cvNextCarrierRescan = Time.unscaledTime + 2.5f;
                }
                else
                    PlayerAutopilot._cvNextCarrierRescan = Time.unscaledTime + 8f;
            }
            else if (PlayerAutopilot._landBase == null || PlayerAutopilot._landBase.disabled)
            {
                PlayerAutopilot.ResolveLand(ac, PlayerAutopilot.IsLandCarrierMode);
            }

            if (PlayerAutopilot._landBase == null)
            {
                PlayerAutopilot._status = PlayerAutopilot.IsLandCarrierMode
                    ? (PlayerAutopilot.UiZh() ? "未检测到航母" : "NO CARRIER")
                    : (PlayerAutopilot.UiZh() ? "未检测到机场" : "NO AIRBASE");
                Vector3 hold = AutopilotAim.GroundTrackAim(ac, 12000f);
                PlayerAutopilot.AutoAimAny(ap, hold.ToGlobalPosition(), true, false, false, 0.95f,
                    AutopilotAim.CruiseBank, true,
                    Mathf.Max(600f, PlayerAutopilot._holdAgl), Vector3.zero);
                inputs.throttle = 0.45f;
                if (PlayerAutopilot.IsLandCarrierMode)
                    ForceCarrierNormalFlight(inputs);
                return;
            }

            if (!PlayerAutopilot._hasRunway)
            {
                try
                {
                    RunwayQuery q = PlayerAutopilot.IsLandCarrierMode
                        ? PlayerAutopilot.BuildCarrierLandQuery(ac, parms)
                        : PlayerAutopilot.BuildLandQuery(ac, parms);
                    Airbase.Runway.RunwayUsage? usage = PlayerAutopilot._landBase.RequestLanding(ac, q);
                    // Carrier decks are short — strict takeoff-distance MinSize often rejects (27C).
                    if (!usage.HasValue && PlayerAutopilot.IsLandCarrierMode)
                    {
                        RunwayQuery loose = PlayerAutopilot.BuildCarrierLandQuery(ac, parms);
                        loose.MinSize = 10f;
                        loose.LandingSpeed = 0f;
                        loose.TailHook = false;
                        usage = PlayerAutopilot._landBase.RequestLanding(ac, loose);
                    }
                    if (usage.HasValue)
                    {
                        PlayerAutopilot._runway = usage.Value;
                        PlayerAutopilot._hasRunway = true;
                        PlayerAutopilot._reachedApproach = false;
                        PlayerAutopilot._vtolHovering = false;
                        PlayerAutopilot._cvLeg = PlayerAutopilot.CvPatternLeg.None;
                        PlayerAutopilot._cvLegWatch = PlayerAutopilot.CvPatternLeg.None;
                    }
                }
                catch { }
            }

            string baseName = PlayerAutopilot.FormatAirbaseName(PlayerAutopilot._landBase, PlayerAutopilot.IsLandCarrierMode);

            if (PlayerAutopilot._hasRunway && PlayerAutopilot._runway.Runway != null)
            {
                // Installed game: glideslope helpers live on RunwayUsage (not Runway).
                Transform touch = null;
                try { touch = PlayerAutopilot._runway.GetStart(); }
                catch
                {
                    try { touch = PlayerAutopilot._runway.Reverse ? PlayerAutopilot._runway.Runway.End : PlayerAutopilot._runway.Runway.Start; }
                    catch { }
                }
                Vector3 touchPos = touch != null ? touch.position : PlayerAutopilot._landBase.transform.position;
                float dist = Vector3.Distance(ac.transform.position, touchPos);

                // LAND BASE / pad: STOVL + verticalLanding use Hover settle.
                // LAND CV: always winged stern-line approach (FS-20 etc.) — no sustained Hover/VTOL.
                bool useVtolPad = (stovl || (parms != null && parms.verticalLanding))
                    && !PlayerAutopilot.IsLandCarrierMode;
                if (useVtolPad)
                {
                    ApplyVtolLanding(ac, ap, inputs, parms, cruise, af, baseName, touchPos, dist);
                    return;
                }
                if (PlayerAutopilot.IsLandCarrierMode)
                    PlayerAutopilot._vtolHovering = false;

                if (!PlayerAutopilot._reachedApproach)
                {
                    Vector3 dir = PlayerAutopilot._runway.GetDirection();
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                        dir.Normalize();
                    else
                        dir = Vector3.forward;

                    float alongAp, latAp;
                    LandingMath.RunwayAlongLateral(ac.transform.position, touchPos, dir, out alongAp, out latAp);

                    float raltAp = 0f;
                    try { raltAp = ac.radarAlt; }
                    catch { }
                    float sinkAp = 0f;
                    try { if (ac.rb != null) sinkAp = -ac.rb.velocity.y; }
                    catch { }
                    float corner = parms != null ? parms.cornerSpeed : 120f;

                    // LAND CV: rectangular traffic pattern (五边) from actual runway heading.
                    if (PlayerAutopilot.IsLandCarrierMode)
                    {
                        ApplyCarrierTrafficPattern(ac, ap, inputs, parms, landSpd, turnR, cruise,
                            af, bankScale, dmgApBoost, baseName, touchPos, dir, alongAp, latAp,
                            raltAp, sinkAp, dist, corner);
                        return;
                    }

                    // LAND BASE: align runway early, then ride configurable ILS path.
                    Vector3 aimBase;
                    float absLatAp = Mathf.Abs(latAp);
                    bool ilsJoin = IlsApproachMathService.ShouldTrackIls(alongAp, latAp, false)
                        && absLatAp < 650f && alongAp > 200f;
                    if (alongAp < -80f || absLatAp > 650f)
                    {
                        // Rejoin stern + cut lateral hard so we square up earlier.
                        float sternDist = Mathf.Max(turnR * 1.15f, 2200f);
                        aimBase = touchPos - dir * sternDist;
                        if (absLatAp > 40f)
                            aimBase += Vector3.Cross(Vector3.up, dir).normalized
                                * Mathf.Sign(latAp) * Mathf.Min(absLatAp * 0.35f, 180f);
                        ilsJoin = false;
                    }
                    else if (ilsJoin)
                    {
                        aimBase = IlsApproachMathService.CorrectedGlideAim(
                            touchPos, dir, alongAp, latAp);
                    }
                    else
                    {
                        // Pre-ILS: aim centerline stern so heading lines up before capture.
                        float sternDist = Mathf.Clamp(alongAp * 0.55f, 1200f, 3200f);
                        aimBase = touchPos - dir * sternDist;
                        if (absLatAp > 30f)
                            aimBase += Vector3.Cross(Vector3.up, dir).normalized
                                * Mathf.Sign(latAp) * Mathf.Min(absLatAp * 0.4f, 160f);
                    }

                    float apAltBase = LandingFinalMathService.ResolveJoinApAlt(
                        turnR, dmgApBoost, ac.speed, landSpd, alongAp, latAp, raltAp, af.Severity);
                    if (ilsJoin)
                    {
                        float ilsAgl = IlsApproachMathService.ResolveIlsAltHold(
                            alongAp, raltAp, false, false, alongAp < 1800f);
                        apAltBase = ilsAgl;
                        aimBase.y = touchPos.y + apAltBase;
                    }
                    else
                    {
                        float floorYBase = touchPos.y + apAltBase;
                        float softCeilBase = ac.transform.position.y + (raltAp > apAltBase + 80f ? 8f : 40f);
                        float aimYBase = Mathf.Max(floorYBase, touchPos.y + apAltBase * 0.85f);
                        if (raltAp > apAltBase + 80f)
                            aimYBase = Mathf.Min(aimYBase, ac.transform.position.y - 60f);
                        aimBase.y = Mathf.Min(softCeilBase, Mathf.Max(aimYBase, touchPos.y + 80f));
                    }
                    // More bank while laterally offset — straighten onto runway sooner.
                    float apBankBase = (absLatAp > 120f ? 150f : 120f) * bankScale;
                    PlayerAutopilot.AutoAimAny(ap, aimBase.ToGlobalPosition(), true, false, false, 0.95f, apBankBase,
                        !ilsJoin, apAltBase, Vector3.zero);

                    if (LandingFinalMathService.ShouldCaptureApproach(
                        alongAp, latAp, raltAp, apAltBase, ac.speed, landSpd, sinkAp, af.Severity,
                        PlayerAutopilot._reachedApproach))
                        PlayerAutopilot._reachedApproach = true;
                    float farCapBase = Mathf.Min(corner * 0.78f, landSpd * 1.95f);
                    float nearTgtBase = landSpd * 1.34f;
                    float tBase = Mathf.Clamp01((dist - 1600f) / 14000f);
                    float tgtBase = Mathf.Min(Mathf.Lerp(nearTgtBase, farCapBase, tBase),
                        landSpd * 1.42f + dist * 0.005f);
                    ApplySoftLandEnergy(inputs, ac, tgtBase, cruise, landSpd, false, false, raltAp);
                    if (LandingFinalMathService.ShouldDropGearEarly(
                        dist, alongAp, raltAp, ac.speed, landSpd, false, false))
                        TryDropGear(ac);
                    PlayerAutopilot._status = (alongAp < -80f ? "REJOIN  " : (ilsJoin ? "ILS  " : "APPROACH  "))
                        + baseName
                        + "  along=" + alongAp.ToString("0")
                        + "  lat=" + latAp.ToString("0")
                        + "  agl=" + raltAp.ToString("0") + "→" + apAltBase.ToString("0")
                        + "  " + (ac.speed * 3.6f).ToString("0") + "→" + (tgtBase * 3.6f).ToString("0")
                        + PlayerAutopilot.AirframeStatusTag(af);
                    return;
                }

                float alongDot = 0f;
                try
                {
                    Vector3 windRel = ac.rb.velocity;
                    alongDot = Vector3.Dot(ac.transform.forward, windRel);
                }
                catch { alongDot = ac.speed; }

                // Match vanilla AIPilotShortLandingState: keep glide aim 200–700m ahead.
                // Old formula (dist - 100) went negative on short final → aim behind → hard left bank / circle.
                float lookAhead = Mathf.Min(dist / Mathf.Max(alongDot, 5f), 30f);
                float glideDist = Mathf.Lerp(200f, 700f, Mathf.Clamp01(dist * 0.0002f));

                Vector3 rwyDir = Vector3.forward;
                try
                {
                    rwyDir = PlayerAutopilot._runway.GetDirection();
                    rwyDir.y = 0f;
                    if (rwyDir.sqrMagnitude > 0.01f)
                        rwyDir.Normalize();
                    else
                        rwyDir = Vector3.forward;
                }
                catch { rwyDir = Vector3.forward; }

                // Along-track: + = still astern (good), - = past threshold (overshot).
                float alongTrack, latTrack;
                LandingMath.RunwayAlongLateral(ac.transform.position, touchPos, rwyDir, out alongTrack, out latTrack);
                float ralt = 0f;
                try { ralt = ac.radarAlt; }
                catch { }

                // Wheels down: stop AutoAim pitch fights so handback can complete.
                bool landedWing = false;
                try { landedWing = ac.IsLanded(); }
                catch { landedWing = ralt < 1.5f && ac.speed < 12f; }
                if (LandingFinalMathService.IsWheelsDownRollout(landedWing, ralt, ac.speed))
                {
                    inputs.throttle = 0f;
                    inputs.brake = 1f;
                    if (PlayerAutopilot.IsLandCarrierMode)
                        ForceCarrierNormalFlight(inputs);
                    else
                        ApplyStovlWingedCarrierNozzles(inputs, ac, stovl, ralt, true, true);
                    PlayerAutopilot._status = "ROLLOUT  " + baseName + "  DOWN"
                        + "  " + GameUnitDisplayService.Speed(ac.speed)
                        + PlayerAutopilot.AirframeStatusTag(af);
                    return;
                }

                // Overshot / abeam too close / heading crossed → go around (CV: 五边复飞).
                Vector3 trackSf = LandingMath.FlatTrackDir(ac);
                float hdgSf = Vector3.Dot(trackSf, rwyDir);
                if (LandingFinalMathService.ShouldBreakOff(
                    alongTrack, latTrack, dist, hdgSf, PlayerAutopilot.IsLandCarrierMode))
                {
                    PlayerAutopilot._reachedApproach = false;
                    PlayerAutopilot._vtolHovering = false;
                    if (PlayerAutopilot.IsLandCarrierMode)
                    {
                        // Climb-out then pattern — never hover / park abeam.
                        ForceCarrierNormalFlight(inputs);
                        if (Mathf.Abs(latTrack) > 40f)
                            PlayerAutopilot._cvPatSide = Mathf.Sign(latTrack);
                        PlayerAutopilot._cvLeg = alongTrack < -80f ? PlayerAutopilot.CvPatternLeg.Upwind : PlayerAutopilot.CvPatternLeg.Downwind;
                        ApplyCarrierTrafficPattern(ac, ap, inputs, parms, landSpd, turnR, cruise,
                            af, bankScale, dmgApBoost, baseName, touchPos, rwyDir, alongTrack,
                            latTrack, ralt, 0f, dist,
                            parms != null ? parms.cornerSpeed : 120f);
                        return;
                    }
                    Vector3 rightSf = Vector3.Cross(Vector3.up, rwyDir);
                    if (rightSf.sqrMagnitude < 0.01f)
                        rightSf = Vector3.right;
                    rightSf.Normalize();
                    float sideSf = Mathf.Abs(latTrack) > 40f ? Mathf.Sign(latTrack) : 1f;
                    float sternLong = Mathf.Max(turnR * 1.2f, 2200f);
                    Vector3 stern = touchPos - rwyDir * sternLong + rightSf * (sideSf * 250f);
                    stern.y = Mathf.Max(ac.transform.position.y, touchPos.y + 700f);
                    PlayerAutopilot.AutoAimAny(ap, stern.ToGlobalPosition(), true, false, false, 0.9f,
                        120f * bankScale, true, Mathf.Max(500f, ralt * 0.5f), Vector3.zero);
                    ApplySoftLandEnergy(inputs, ac, landSpd * 1.4f, cruise, landSpd, false, false,
                        ralt);
                    ApplyStovlWingedCarrierNozzles(inputs, ac, stovl, ralt, false, false);
                    PlayerAutopilot._status = "REJOIN  " + baseName
                        + "  along=" + alongTrack.ToString("0")
                        + "  lat=" + latTrack.ToString("0");
                    return;
                }

                LandingFinalMathService.FinalPhases phases = LandingFinalMathService.ResolveFinalPhases(
                    dist, ralt, alongTrack, ac.speed, landSpd, PlayerAutopilot.IsLandCarrierMode);
                bool align = phases.Align;
                bool shortFinal = phases.ShortFinal;
                bool flare = phases.Flare;
                bool rollout = phases.Rollout;

                // Cap bank like vanilla (135 approach / much less on short final) so final does not knife-edge left.
                // Damaged / asymmetric aero: keep wings flatter.
                // CV: keep decisive bank until truly lined up — 25C parked at 35° bank + axis1=1.
                float bank = LandingFinalMathService.ResolveBankCap(
                    phases, latTrack, hdgSf, bankScale, PlayerAutopilot.IsLandCarrierMode);

                bool useIls = IlsApproachMathService.ShouldTrackIls(
                    alongTrack, latTrack, rollout);
                Vector3 finalAim;
                if (rollout || alongTrack < 0f)
                {
                    // Past / over threshold: track runway end, do not chase a glideslope behind the jet.
                    Transform rwyEnd = null;
                    try { rwyEnd = PlayerAutopilot._runway.GetEnd(); }
                    catch { }
                    if (rwyEnd != null)
                        finalAim = rwyEnd.position;
                    else
                        finalAim = touchPos + rwyDir * 900f;
                    finalAim.y = touchPos.y;
                    useIls = false;
                }
                else if (useIls)
                {
                    // Fly simulated ILS: 3° glideslope + localizer centerline.
                    finalAim = IlsApproachMathService.CorrectedGlideAim(
                        touchPos, rwyDir, alongTrack, latTrack);
                    float shallowAgl = LandingFinalMathService.ResolveShallowAgl(
                        alongTrack, ralt, af.Severity, PlayerAutopilot.IsLandCarrierMode, stovl, flare);
                    float floorY = touchPos.y + shallowAgl;
                    if (finalAim.y < floorY)
                        finalAim.y = floorY;
                }
                else
                {
                    finalAim = PlayerAutopilot._runway.GetGlideslopeAimpoint(ac, glideDist, lookAhead);
                    float shallowAgl = LandingFinalMathService.ResolveShallowAgl(
                        alongTrack, ralt, af.Severity, PlayerAutopilot.IsLandCarrierMode, stovl, flare);
                    float floorY = touchPos.y + shallowAgl;
                    if (finalAim.y < floorY)
                        finalAim.y = floorY;
                }

                // Soften hard sink: lift aim when vertical rate would slam the gear.
                float sink = 0f;
                try { if (ac.rb != null) sink = -ac.rb.velocity.y; }
                catch { }
                // Target sink ≈ V * tan(2.8°) — keep well under structural slam rates.
                // Reduce further when parts are missing (weaker structure / less lift).
                float maxSink = LandingFinalMathService.ResolveMaxSink(
                    ac.speed, ralt, sinkScale, af.Severity, flare, PlayerAutopilot.IsLandCarrierMode);
                bool sinkHot = !rollout && sink > maxSink;
                if (sinkHot)
                {
                    finalAim.y += LandingFinalMathService.ResolveSinkLift(
                        sink, maxSink, ralt, PlayerAutopilot.IsLandCarrierMode, shortFinal);
                }

                // Speed-safe = near approach speed (not stall-edge). Low+slow is never "safe".
                // LAND BASE flare must be allowed near landSpd — old 1.12–1.16 floor fought the flare
                // via stallThreat → altHold=ralt+55 → runaway pitch / never settle.
                float safeLo = LandingFinalMathService.ResolveSafeLo(
                    landSpd, PlayerAutopilot.IsLandCarrierMode, flare, shortFinal);
                bool speedSafe = ac.speed <= landSpd * 1.55f && ac.speed >= safeLo;
                bool energySafe = speedSafe && sink <= maxSink + 2f;
                float stallMul = LandingFinalMathService.ResolveStallMul(
                    PlayerAutopilot.IsLandCarrierMode, flare, shortFinal);
                bool stallThreat = !rollout && ac.speed < landSpd * stallMul;
                bool ignoreCol = (flare || rollout) && (energySafe || flare) && !(stallThreat && ac.speed < landSpd * 0.82f);
                bool followTerrain = (!align && !flare) || (!energySafe && !flare && !shortFinal) || sinkHot;
                float altHold = shortFinal
                    ? 0.075f * Mathf.Max(dist, 180f)
                    : 0.08f * dist;
                bool trueStall = stallThreat && ac.speed < landSpd * (
                    PlayerAutopilot.IsLandCarrierMode ? 0.82f : 0.9f);
                if ((!energySafe || sinkHot || trueStall) && !flare)
                {
                    float holdBoost = Mathf.Max(ralt + 55f, Mathf.Min(ralt * 1.1f + 40f, 380f));
                    // Short final / low: never command a big climb-away hold (BASE + CV).
                    if ((shortFinal || ralt < 220f) && alongTrack > 0f)
                        holdBoost = Mathf.Min(holdBoost, Mathf.Max(ralt + 12f, 28f));
                    altHold = Mathf.Max(altHold, holdBoost);
                }
                if (flare && !rollout)
                    altHold = Mathf.Max(12f, Mathf.Min(altHold, Mathf.Max(10f, ralt * 0.65f)));
                // ILS / CV / BASE: path owns AGL — do not let mild stallThreat steal this.
                if (useIls && !trueStall)
                {
                    followTerrain = false;
                    altHold = IlsApproachMathService.ResolveIlsAltHold(
                        alongTrack, ralt, PlayerAutopilot.IsLandCarrierMode, flare, shortFinal);
                }
                else if (PlayerAutopilot.IsLandCarrierMode && !trueStall)
                {
                    followTerrain = false;
                    if (flare)
                        altHold = Mathf.Clamp(ralt * 0.45f, 4f, 18f);
                    else if (shortFinal || alongTrack < 2200f)
                        altHold = Mathf.Clamp(alongTrack * 0.042f, 10f, 85f);
                    else
                        altHold = Mathf.Clamp(alongTrack * 0.048f, 40f, 160f);
                }
                else if (!PlayerAutopilot.IsLandCarrierMode && !trueStall && (shortFinal || flare || align)
                    && alongTrack > 0f && Mathf.Abs(latTrack) < 320f)
                {
                    followTerrain = false;
                    if (flare)
                        altHold = Mathf.Clamp(ralt * 0.5f, 6f, 24f);
                    else if (shortFinal)
                        altHold = Mathf.Clamp(alongTrack * IlsApproachMathService.GlideTan, 16f, 110f);
                    else
                        altHold = Mathf.Clamp(alongTrack * IlsApproachMathService.GlideTan, 40f, 180f);
                }

                PlayerAutopilot.AutoAimAny(ap, finalAim.ToGlobalPosition(), true, ignoreCol,
                    (align || flare) && (energySafe || flare) && !trueStall,
                    trueStall ? 0.68f : (energySafe || flare || shortFinal ? 0.9f : 0.76f), bank,
                    followTerrain || (!energySafe && !rollout && !flare && !shortFinal
                        && !PlayerAutopilot.IsLandCarrierMode), altHold,
                    Vector3.zero);

                // CV short final must bleed below ~70 m/s — 1.26× landSpd × 1.1 was ~85 m/s slam.
                float tgtSpd = LandingFinalMathService.ResolveTargetSpeed(
                    landSpd, dist, ac.speed, ralt, phases, PlayerAutopilot.IsLandCarrierMode,
                    stallThreat, sinkHot);
                ApplySoftLandEnergy(inputs, ac, tgtSpd, cruise, landSpd, flare, rollout, ralt);
                if (PlayerAutopilot.IsLandCarrierMode)
                    ForceCarrierNormalFlight(inputs);
                else
                    ApplyStovlWingedCarrierNozzles(inputs, ac, stovl, ralt, flare, rollout);

                if (LandingFinalMathService.ShouldDropGearEarly(
                    dist, alongTrack, ralt, ac.speed, landSpd, shortFinal, flare))
                    TryDropGear(ac);
                string phaseTag = useIls
                    ? (PlayerAutopilot.IsLandCarrierMode ? "CV ILS " : "ILS ")
                    : (PlayerAutopilot.IsLandCarrierMode ? "CV WING " : "BASE ");
                PlayerAutopilot._status = phaseTag
                    + baseName + "  " + dist.ToString("0") + "m"
                    + "  agl=" + ralt.ToString("0") + "→" + altHold.ToString("0")
                    + "  ax1=" + (inputs != null ? inputs.customAxis1.ToString("0.00") : "?")
                    + "  " + (ac.speed * 3.6f).ToString("0") + "→" + (tgtSpd * 3.6f).ToString("0")
                    + PlayerAutopilot.AirframeStatusTag(af);
            }
            else
            {
                Vector3 c = PlayerAutopilot._landBase.center != null
                    ? PlayerAutopilot._landBase.center.position
                    : PlayerAutopilot._landBase.transform.position;
                // No runway yet: pad VTOL only for LAND BASE. CV keeps winged VECTOR.
                bool useVtolPadNoRwy = (stovl || (parms != null && parms.verticalLanding))
                    && !PlayerAutopilot.IsLandCarrierMode;
                if (useVtolPadNoRwy)
                {
                    ApplyVtolLanding(ac, ap, inputs, parms, cruise, af, baseName, c,
                        Vector3.Distance(ac.transform.position, c));
                    return;
                }
                if (PlayerAutopilot.IsLandCarrierMode)
                    PlayerAutopilot._vtolHovering = false;
                Vector3 to = c - ac.transform.position;
                to.y = 0f;
                Vector3 aim = c - (to.sqrMagnitude > 1f ? to.normalized * (turnR * 2.5f) : Vector3.forward * 2000f);
                aim.y = c.y + 500f + dmgApBoost;
                PlayerAutopilot.AutoAimAny(ap, aim.ToGlobalPosition(), true, false, false, 0.95f, 135f * bankScale, true,
                    500f + dmgApBoost, Vector3.zero);
                float vecTgt = Mathf.Min(
                    parms != null ? parms.cornerSpeed * 0.7f : 110f,
                    landSpd * 1.55f);
                float raltVec = 0f;
                try { raltVec = ac.radarAlt; }
                catch { }
                ApplySoftLandEnergy(inputs, ac, vecTgt, cruise, landSpd, false, false, raltVec);
                if (PlayerAutopilot.IsLandCarrierMode)
                    ForceCarrierNormalFlight(inputs);
                else
                    ApplyStovlWingedCarrierNozzles(inputs, ac, stovl, raltVec, false, false);
                PlayerAutopilot._status = "VECTOR  " + baseName + PlayerAutopilot.AirframeStatusTag(af);
            }
        }

        /// <summary>
        /// LAND CV rectangular traffic pattern (五边) keyed to runway GetDirection().
        /// Upwind → Crosswind → Downwind → Base → Final → CV WING (short final).
        /// Overshoot → Upwind/Downwind go-around (never hover / level forever at ~100 m).
        /// </summary>
        private static void ApplyCarrierTrafficPattern(Aircraft ac, Autopilot ap,
            ControlInputs inputs, AircraftParameters parms, float landSpd, float turnR,
            float cruise, PlayerAutopilot.AirframeCondition af, float bankScale, float dmgApBoost,
            string baseName, Vector3 touchPos, Vector3 rwyDir, float along, float lat,
            float ralt, float sink, float dist, float corner)
        {
            if (ac == null || ap == null)
                return;

            ForceCarrierNormalFlight(inputs);
            BeginnerAssist.YieldToAutopilotLand();

            Vector3 dir = rwyDir;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                dir.Normalize();
            else
                dir = Vector3.forward;

            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;
            right.Normalize();

            Vector3 track = LandingMath.FlatTrackDir(ac);
            float hdgDot = Vector3.Dot(track, dir);
            float hdgDown = Vector3.Dot(track, -dir);
            float absLat = Mathf.Abs(lat);

            LandingCvPatternGateService.Geometry g = LandingCvPatternGateService.ComputeGeometry(
                turnR, dmgApBoost);

            float side = LandingCvPatternGateService.ResolvePatternSide(lat, PlayerAutopilot._cvPatSide);
            PlayerAutopilot._cvPatSide = side;

            if (PlayerAutopilot._cvLeg == PlayerAutopilot.CvPatternLeg.None)
            {
                PlayerAutopilot._cvLeg = LandingCvPatternGateService.PickEntryLeg(
                    along, lat, hdgDot, g.SternDist, g.LatOff);
                if (absLat > 80f)
                    PlayerAutopilot._cvPatSide = Mathf.Sign(lat);
            }

            float crossDot = Vector3.Dot(track, right * side);
            PlayerAutopilot.CvPatternLeg advanced = LandingCvPatternGateService.AdvanceLeg(
                PlayerAutopilot._cvLeg, along, absLat, dist, hdgDot, hdgDown, crossDot, g);
            if (advanced == PlayerAutopilot.CvPatternLeg.Upwind
                && LandingCvPatternGateService.ShouldHardGoAround(along, absLat, dist))
                PlayerAutopilot._reachedApproach = false;
            PlayerAutopilot._cvLeg = advanced;

            // Stuck circling watchdog: same leg, level altitude, no speed bleed → force next leg.
            if (PlayerAutopilot._cvLeg != PlayerAutopilot._cvLegWatch)
            {
                PlayerAutopilot._cvLegWatch = PlayerAutopilot._cvLeg;
                PlayerAutopilot._cvLegWatchSince = Time.unscaledTime;
            }
            else if (LandingCvPatternGateService.ShouldWatchdogForce(
                PlayerAutopilot._cvLeg, PlayerAutopilot._cvLegWatch,
                PlayerAutopilot._cvLegWatchSince, Time.unscaledTime, Mathf.Abs(sink)))
            {
                PlayerAutopilot._cvLeg = LandingCvPatternGateService.WatchdogNext(PlayerAutopilot._cvLeg);
                PlayerAutopilot._cvLegWatch = PlayerAutopilot._cvLeg;
                PlayerAutopilot._cvLegWatchSince = Time.unscaledTime;
            }

            LandingCvPatternGateService.LegAim legAim = LandingCvPatternGateService.ResolveLegAim(
                PlayerAutopilot._cvLeg, touchPos, dir, right, side, along, lat, absLat, ralt,
                hdgDot, landSpd, corner, bankScale, ac.speed, g);

            Vector3 aim = legAim.AimFlat;
            float apAlt = legAim.ApAlt;
            float apBank = legAim.ApBank;
            float tgtSpd = legAim.TgtSpd;
            bool followTerrain = legAim.FollowTerrain;
            string phaseTag = legAim.PhaseTag;
            if (legAim.CaptureWing)
                PlayerAutopilot._reachedApproach = true;

            // CV Final / Wing: ride ILS localizer + 3° (deck-flat) path.
            bool cvIls = (PlayerAutopilot._cvLeg == PlayerAutopilot.CvPatternLeg.Final
                    || legAim.CaptureWing)
                && IlsApproachMathService.ShouldTrackIls(along, lat, false);
            if (cvIls)
            {
                aim = IlsApproachMathService.CorrectedGlideAim(touchPos, dir, along, lat);
                apAlt = IlsApproachMathService.ResolveIlsAltHold(
                    along, ralt, true, false, along < 1600f);
                followTerrain = false;
                phaseTag = legAim.CaptureWing ? "CV ILS WING" : "CV ILS";
                if (absLat > 80f)
                    apBank = Mathf.Max(apBank, 160f * bankScale);
            }
            else
            {
                aim.y = LandingCvPatternGateService.ResolveAimY(
                    PlayerAutopilot._cvLeg, ac.transform.position, touchPos, apAlt, aim.y);
            }

            PlayerAutopilot.AutoAimAny(ap, aim.ToGlobalPosition(), true, false, false, 0.95f, apBank,
                followTerrain, apAlt, Vector3.zero);
            ApplySoftLandEnergy(inputs, ac, tgtSpd, cruise, landSpd, false, false, ralt);
            ForceCarrierNormalFlight(inputs);

            if (LandingCvPatternGateService.ShouldDropGear(PlayerAutopilot._cvLeg, dist, ralt))
                TryDropGear(ac);

            PlayerAutopilot._status = phaseTag + "  " + baseName
                + "  along=" + along.ToString("0")
                + "  lat=" + lat.ToString("0")
                + "  hdg=" + hdgDot.ToString("0.00")
                + "  agl=" + ralt.ToString("0") + "→" + apAlt.ToString("0")
                + "  ax1=0"
                + "  " + (ac.speed * 3.6f).ToString("0") + "→" + (tgtSpd * 3.6f).ToString("0")
                + PlayerAutopilot.AirframeStatusTag(af);
        }

        private static void PickCvEntryLeg(float along, float lat, float hdgDot,
            float sternDist, float latOff)
        {
            if (Mathf.Abs(lat) > 80f)
                PlayerAutopilot._cvPatSide = Mathf.Sign(lat);
            PlayerAutopilot._cvLeg = LandingCvPatternGateService.PickEntryLeg(
                along, lat, hdgDot, sternDist, latOff);
        }

        /// <summary>
        /// LAND CV: snap nozzles forward + clear Hover. Live 25C held customAxis1=1.0 for ~300s
        /// (VTOL_TRANSITION / thr vertical / AGL oscillate) and never reached the deck.
        /// </summary>
        internal static void ForceCarrierNormalFlight(ControlInputs inputs)
        {
            PlayerAutopilot._vtolHovering = false;
            if (inputs == null)
                return;
            inputs.customAxis1 = 0f;
        }

        /// <summary>
        /// STOVL / verticalLanding: stern-line ingress → Hover over pad → vertical descent.
        /// Carrier: always approach from the stern, never from the beam; overshoot → rejoin.
        /// </summary>
        private static void ApplyVtolLanding(Aircraft ac, Autopilot ap, ControlInputs inputs,
            AircraftParameters parms, float cruise, PlayerAutopilot.AirframeCondition af, string baseName,
            Vector3 touchPos, float dist3)
        {
            if (ac == null || ap == null || inputs == null)
                return;

            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            float sink = 0f;
            try { if (ac.rb != null) sink = -ac.rb.velocity.y; }
            catch { }

            Vector3 rwyDir = Vector3.forward;
            try
            {
                if (PlayerAutopilot._hasRunway && PlayerAutopilot._runway.Runway != null)
                {
                    rwyDir = PlayerAutopilot._runway.GetDirection();
                    rwyDir.y = 0f;
                    if (rwyDir.sqrMagnitude > 0.01f)
                        rwyDir.Normalize();
                }
            }
            catch { rwyDir = Vector3.forward; }

            float along, lateral;
            LandingMath.RunwayAlongLateral(ac.transform.position, touchPos, rwyDir, out along, out lateral);
            float absLat = Mathf.Abs(lateral);
            float horiz = Vector3.Distance(
                new Vector3(ac.transform.position.x, 0f, ac.transform.position.z),
                new Vector3(touchPos.x, 0f, touchPos.z));

            LandingVtolPadGateService.Geometry g = LandingVtolPadGateService.ComputeGeometry(
                PlayerAutopilot.IsLandCarrierMode);
            Vector3 pad = touchPos + rwyDir * g.PadAlong;
            Vector3 sternGate = touchPos - rwyDir * g.SternLong;
            Vector3 finalGate = touchPos - rwyDir * g.FinalLong;

            if (LandingVtolPadGateService.ShouldDropGear(horiz, ralt))
                TryDropGear(ac);

            bool landed = false;
            try { landed = ac.IsLanded(); }
            catch { landed = ralt < 1.5f && ac.speed < 12f; }

            if (LandingVtolPadGateService.IsPadDown(landed, ralt, ac.speed))
            {
                inputs.throttle = 0f;
                inputs.brake = 1f;
                inputs.customAxis1 = 1f;
                try { ap.Hover(pad.ToGlobalPosition(), 0.5f, rwyDir); }
                catch { }
                PlayerAutopilot._status = (PlayerAutopilot.IsLandCarrierMode ? "CV VTOL " : "VTOL ")
                    + baseName + "  DOWN"
                    + PlayerAutopilot.AirframeStatusTag(af);
                return;
            }

            float dt = Time.deltaTime;
            if (dt < 0.001f || dt > 0.2f)
                dt = 0.02f;

            LandingVtolPadGateService.Phase phase = LandingVtolPadGateService.ResolvePhase(
                along, absLat, horiz, ralt, g);
            if (phase.Overshot)
            {
                PlayerAutopilot._vtolHovering = false;
                float holdGa = g.GoAroundHoldAgl;
                Vector3 rejoin = sternGate;
                if (absLat > 40f)
                    rejoin += Vector3.Cross(Vector3.up, rwyDir).normalized * Mathf.Sign(lateral) * 200f;
                rejoin.y = touchPos.y;
                Vector3 aimGa = rejoin;
                aimGa.y = touchPos.y + holdGa;
                PlayerAutopilot.AutoAimAny(ap, aimGa.ToGlobalPosition(), true, false, false, 0.9f, 120f,
                    true, holdGa, Vector3.zero);
                inputs.customAxis1 = Mathf.MoveTowards(inputs.customAxis1, 0f, dt * 0.8f);
                inputs.throttle = Mathf.Clamp(cruise * 0.7f, 0.35f, 0.85f);
                inputs.brake = ac.speed > 120f ? 0.25f : 0f;
                PlayerAutopilot._status = "VTOL REJOIN  " + baseName
                    + "  along=" + along.ToString("0")
                    + "  lat=" + lateral.ToString("0")
                    + PlayerAutopilot.AirframeStatusTag(af);
                return;
            }

            if (!PlayerAutopilot._vtolHovering)
            {
                if (LandingVtolPadGateService.ShouldCaptureHover(
                    phase, ac.speed, ralt, horiz, along, absLat))
                    PlayerAutopilot._vtolHovering = true;
            }
            if (PlayerAutopilot._vtolHovering
                && LandingVtolPadGateService.ShouldReleaseHover(ralt, phase.Overshot, absLat))
                PlayerAutopilot._vtolHovering = false;

            float hold = LandingVtolPadGateService.SoftenHoldForSink(
                g.HoldAgl, ralt, sink, PlayerAutopilot._vtolHovering);

            if (!PlayerAutopilot._vtolHovering)
            {
                bool needStern = phase.NeedStern;
                Vector3 gate = needStern ? sternGate : finalGate;
                Vector3 aim = gate;
                aim.y = touchPos.y + hold;

                PlayerAutopilot.AutoAimAny(ap, aim.ToGlobalPosition(), true, false, false, 0.92f,
                    needStern ? 120f : 70f, true, hold, Vector3.zero);

                float axisTgt = LandingVtolPadGateService.IngressAxisTarget(
                    ralt, phase.NearDeck, phase.ShortStern, along, g.FinalLong, absLat);
                inputs.customAxis1 = Mathf.MoveTowards(inputs.customAxis1, axisTgt, dt * 0.85f);

                float tgtSpd = LandingVtolPadGateService.IngressTargetSpeed(needStern, horiz, along);
                float err = ac.speed - tgtSpd;
                inputs.throttle = LandingVtolPadGateService.IngressThrottle(
                    tgtSpd, ac.speed, cruise, ralt, hold, sink, err);
                inputs.brake = LandingVtolPadGateService.IngressBrake(err);

                PlayerAutopilot._status = (needStern ? "VTOL STERN  " : "VTOL IN  ") + baseName
                    + "  along=" + along.ToString("0")
                    + "  lat=" + lateral.ToString("0")
                    + "  agl=" + ralt.ToString("0")
                    + "  " + GameUnitDisplayService.Speed(ac.speed)
                    + PlayerAutopilot.AirframeStatusTag(af);
                return;
            }

            float targetAgl = LandingVtolPadGateService.ResolveHoverTargetAgl(along, absLat, ralt);
            Vector3 hoverAim = (along > 40f) ? (touchPos - rwyDir * Mathf.Min(along * 0.4f, 200f)) : pad;
            try { ap.Hover(hoverAim.ToGlobalPosition(), targetAgl, rwyDir); }
            catch
            {
                Vector3 aimH = hoverAim;
                aimH.y = touchPos.y + targetAgl;
                PlayerAutopilot.AutoAimAny(ap, aimH.ToGlobalPosition(), true, true, true, 0.7f, 25f,
                    false, targetAgl, Vector3.zero);
            }

            float axisHover = LandingVtolPadGateService.HoverAxisTarget(ralt, phase.NearDeck);
            inputs.customAxis1 = Mathf.MoveTowards(inputs.customAxis1, axisHover, dt * 1.2f);
            inputs.throttle = LandingVtolPadGateService.HoverThrottle(sink, ralt);
            inputs.brake = LandingVtolPadGateService.HoverBrake(ac.speed, along);

            PlayerAutopilot._status = (PlayerAutopilot.IsLandCarrierMode ? "CV VTOL " : "VTOL ")
                + baseName + "  along=" + along.ToString("0")
                + "  agl=" + ralt.ToString("0") + "→" + targetAgl.ToString("0")
                + "  ax1=" + inputs.customAxis1.ToString("0.00")
                + "  " + GameUnitDisplayService.Speed(ac.speed)
                + PlayerAutopilot.AirframeStatusTag(af);
        }

        /// <summary>
        /// LAND CV winged STOVL (FS-20): nozzles stay forward for the entire approach.
        /// 25C brief near-deck lift still left axis1 stuck high when something else fought it;
        /// 26C hard-zeros unless essentially stopped on deck.
        /// </summary>
        private static void ApplyStovlWingedCarrierNozzles(ControlInputs inputs, Aircraft ac,
            bool stovl, float ralt, bool flare, bool rollout)
        {
            if (inputs == null || ac == null)
                return;
            LandingStovlNozzleGateService.Path path = LandingStovlNozzleGateService.Resolve(
                PlayerAutopilot.IsLandCarrierMode, stovl, rollout, ralt, ac.speed);
            if (path == LandingStovlNozzleGateService.Path.Skip)
                return;
            if (path == LandingStovlNozzleGateService.Path.WhisperCap)
            {
                inputs.customAxis1 = Mathf.Min(
                    inputs.customAxis1, LandingStovlNozzleGateService.WhisperAxisCap);
                return;
            }
            inputs.customAxis1 = 0f;
        }

        /// <summary>
        /// Bleed excess energy without collapsing below approach speed (stall → crash).
        /// </summary>
        private static void ApplySoftLandEnergy(ControlInputs inputs, Aircraft ac, float tgtSpd,
            float cruise, float landSpd, bool flare, bool rollout, float ralt)
        {
            LandingEnergyLaws.ApplySoftLandEnergy(inputs, ac, tgtSpd, cruise, landSpd,
                flare, rollout, ralt, PlayerAutopilot.IsLandCarrierMode);
        }

        private static void TryDropGear(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                if (ac.gearState == LandingGear.GearState.LockedRetracted)
                    ac.SetGear(true);
            }
            catch { }
        }
    }
}
