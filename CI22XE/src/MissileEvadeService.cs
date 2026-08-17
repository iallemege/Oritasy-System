using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield missile evade TEST overlay (0.0.9.59). IR dump / radar notch.
    /// </summary>
    internal static class MissileEvadeService
    {
        internal static void Reset()
        {
            PlayerAutopilot._evadeWasActive = false;
            PlayerAutopilot._evadeReactAt = -1f;
            PlayerAutopilot._cmHoldUntil = -1f;
            PlayerAutopilot._nextCmAt = 0f;
            PlayerAutopilot._evadeSign = 1;
            PlayerAutopilot._evadeBeam = Vector3.right;
        }

        internal static void TickCountermeasurePulse(Aircraft ac)
        {
            if (ac == null || PlayerAutopilot._cmHoldUntil < 0f)
                return;
            if (Time.unscaledTime < PlayerAutopilot._cmHoldUntil)
                return;
            PlayerAutopilot._cmHoldUntil = -1f;
            StopCountermeasures(ac);
        }

        internal static void StopCountermeasures(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                if (ac.countermeasureManager != null)
                    ac.Countermeasures(false, ac.countermeasureManager.activeIndex);
            }
            catch { }
        }

        private static void PulseCountermeasures(Aircraft ac, float holdSec)
        {
            if (ac == null)
                return;
            float now = Time.unscaledTime;
            if (now < PlayerAutopilot._nextCmAt)
                return;
            AiCombatEvadeService.DumpFlares(ac);
            PlayerAutopilot._cmHoldUntil = now + Mathf.Clamp(holdSec, 0.15f, 1.2f);
            PlayerAutopilot._nextCmAt = now + 0.55f;
        }

        /// <summary>
        /// TEST overlay: when missile warning is live, override the selected AP mode with
        /// AI-style IR dump or radar notch. Returns true if evade owns this tick.
        /// </summary>
        internal static bool TryApply(Aircraft ac)
        {
            Autopilot ap = ac.autopilot;
            ControlInputs inputs = ac.GetInputs();
            if (ap == null || inputs == null)
                return false;

            Missile threat = null;
            if (!TryResolveIncomingMissile(ac, out threat) || threat == null)
            {
                if (PlayerAutopilot._evadeWasActive)
                {
                    PlayerAutopilot._evadeWasActive = false;
                    PlayerAutopilot._evadeReactAt = -1f;
                }
                return false;
            }

            float now = Time.unscaledTime;
            if (!PlayerAutopilot._evadeWasActive)
            {
                PlayerAutopilot._evadeWasActive = true;
                // Fast reaction (aligned with snappier AI / guardian).
                PlayerAutopilot._evadeReactAt = now + UnityEngine.Random.Range(0.04f, 0.16f);
                PlayerAutopilot._evadeSign = UnityEngine.Random.value < 0.5f ? -1 : 1;
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Autopilot EVADE TEST: threat");
            }

            if (PlayerAutopilot._evadeReactAt > 0f && now < PlayerAutopilot._evadeReactAt)
            {
                PlayerAutopilot._status = "EVADE  react…";
                return false; // keep flying selected mode until reaction time
            }

            // Do not ChooseCountermeasure — that selects the unused chaff station.
            bool ir = AiCombatEvadeService.IsIrThreat(threat);

            if (ir)
            {
                ApplyEvadeIr(ac, ap, inputs, threat);
                PlayerAutopilot._status = "EVADE IR  " + MissileShortName(threat);
            }
            else
            {
                ApplyEvadeRadar(ac, ap, inputs, threat);
                PlayerAutopilot._status = "EVADE RADAR  " + MissileShortName(threat);
            }
            return true;
        }

        private static bool TryResolveIncomingMissile(Aircraft ac, out Missile threat)
        {
            threat = null;
            try
            {
                MissileWarning mw = ac.GetMissileWarningSystem();
                if (mw == null)
                    return false;

                Missile nearest = null;
                if (mw.TryGetNearestIncoming(out nearest) && nearest != null && IsMissileAlive(nearest))
                {
                    threat = nearest;
                    return true;
                }

                if (mw.knownMissiles != null && mw.knownMissiles.Count > 0)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < mw.knownMissiles.Count; i++)
                    {
                        Missile m = mw.knownMissiles[i];
                        if (!IsMissileAlive(m))
                            continue;
                        float d = Vector3.Distance(ac.transform.position, m.transform.position);
                        if (d < best)
                        {
                            best = d;
                            threat = m;
                        }
                    }
                    return threat != null;
                }

                return mw.IsWarning() && threat != null;
            }
            catch { return false; }
        }

        private static bool IsMissileAlive(Missile m)
        {
            if (m == null)
                return false;
            try
            {
                if (m.disabled)
                    return false;
            }
            catch { }
            try
            {
                return m.gameObject != null && m.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        private static string MissileShortName(Missile m)
        {
            try
            {
                if (m != null && m.definition != null && !string.IsNullOrEmpty(m.definition.code))
                    return m.definition.code;
                if (m != null && m.definition != null && !string.IsNullOrEmpty(m.definition.unitName))
                    return m.definition.unitName;
            }
            catch { }
            return "MSL";
        }

        private static void ApplyEvadeIr(Aircraft ac, Autopilot ap, ControlInputs inputs, Missile threat)
        {
            // AI IR: idle throttle + dump flares; keep a shallow beam away from the missile.
            inputs.throttle = 0f;
            inputs.brake = 0f;
            PulseCountermeasures(ac, 0.45f);

            Vector3 away = FlatAwayFromMissile(ac, threat);
            Vector3 aim = ac.transform.position + away * 6000f;
            float hold = Mathf.Max(250f, PlayerAutopilot._holdAgl > 50f ? PlayerAutopilot._holdAgl : 400f);
            try { hold = Mathf.Max(hold, ac.radarAlt); }
            catch { }
            PlayerAutopilot.AutoAimAny(ap, aim.ToGlobalPosition(), true, false, false, 0.95f,
                AutopilotAim.EvadeBank, true, hold, Vector3.zero);
        }

        private static void ApplyEvadeRadar(Aircraft ac, Autopilot ap, ControlInputs inputs, Missile threat)
        {
            Vector3 acPos = ac.transform.position;
            Vector3 mPos = threat.transform.position;
            Vector3 toMsl = mPos - acPos;
            float dist = toMsl.magnitude;
            Vector3 closing = Vector3.zero;
            try
            {
                if (threat.rb != null && ac.rb != null)
                    closing = threat.rb.velocity - ac.rb.velocity;
            }
            catch { }
            float approach = Vector3.Dot(toMsl.sqrMagnitude > 1f ? toMsl.normalized : Vector3.forward, closing);
            float impactT = dist / Mathf.Max(1f, approach);

            // Beam / notch: prefer Missile.GetEvasionPoint when available (vanilla AI).
            Vector3 beam = Vector3.zero;
            try
            {
                GlobalPosition evadePt = threat.GetEvasionPoint();
                Vector3 evadeLocal = evadePt.ToLocalPosition();
                Vector3 toEvade = evadeLocal - acPos;
                Vector3 acVel = ac.rb != null ? ac.rb.velocity : ac.transform.forward;
                beam = Vector3.Cross(toEvade, acVel);
                if (beam.sqrMagnitude < 0.01f)
                    beam = Vector3.Cross(toEvade, Vector3.up);
                beam.y = 0f;
                if (beam.sqrMagnitude > 0.01f)
                    beam.Normalize();
                if (Vector3.Dot(beam, ac.transform.forward) < 0f)
                    beam = -beam;
            }
            catch
            {
                beam = FlatAwayFromMissile(ac, threat);
                // Pure notch: perpendicular to missile LOS.
                Vector3 los = toMsl;
                los.y = 0f;
                if (los.sqrMagnitude > 0.01f)
                {
                    beam = Vector3.Cross(Vector3.up, los.normalized) * PlayerAutopilot._evadeSign;
                    if (beam.sqrMagnitude > 0.01f)
                        beam.Normalize();
                }
            }
            if (beam.sqrMagnitude < 0.01f)
                beam = FlatAwayFromMissile(ac, threat);
            PlayerAutopilot._evadeBeam = beam;

            float hold = 10f; // AI radar evade drops targetHeight to ~10m terrain follow
            try { hold = Mathf.Clamp(ac.radarAlt * 0.35f, 40f, 180f); }
            catch { hold = 80f; }

            Vector3 aim = acPos + beam * AutopilotAim.LookAheadM;
            // Blend slightly toward current track when impact is still far.
            if (impactT > 7f)
            {
                Vector3 track = ac.transform.forward;
                track.y = 0f;
                if (track.sqrMagnitude > 0.01f)
                    track.Normalize();
                aim = Vector3.Lerp(acPos + track * AutopilotAim.LookAheadM, aim, 0.55f);
            }

            PlayerAutopilot.AutoAimAny(ap, aim.ToGlobalPosition(), true, false, false, 1f,
                AutopilotAim.EvadeBank, true, hold, Vector3.zero);
            inputs.throttle = 1f;
            inputs.brake = 0f;
            // Radar: kinematic beam only. No chaff on the aircraft.
        }

        private static Vector3 FlatAwayFromMissile(Aircraft ac, Missile threat)
        {
            Vector3 away = ac.transform.position - threat.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = ac.transform.forward;
                away.y = 0f;
            }
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.forward;
            return away.normalized;
        }
    
    }
}
