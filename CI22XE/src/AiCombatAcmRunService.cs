using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield ACM run trajectories (0.0.9.81): aim / hold / throttle / stick by script phase.
    /// AiCombatBrain owns AutoAim, StickOverlay application, and state lifetime.
    /// </summary>
    internal static class AiCombatAcmRunService
    {
        internal struct Output
        {
            public Vector3 Aim;
            public float Hold;
            public float Throttle;
            public float StickPitch;
            public float StickRoll;
            public float StickYaw;
            public bool ApplyStick;
            public bool UnknownScript;
        }

        /// <summary>
        /// Compute ACM control sample for progress u in [0,1]. Returns false if script is None.
        /// </summary>
        internal static bool Evaluate(
            AiCombatAcmPickService.Script script,
            float u,
            float elapsed,
            Vector3 myPos,
            Vector3 fwd,
            Vector3 tgtPos,
            Vector3 tgtFwd,
            Vector3 tgtVel,
            Vector3 side,
            float dist,
            float mySpd,
            float corner,
            float ralt,
            float tgtRalt,
            float acmStick,
            float throttleAttack,
            float maneuverSide,
            out Output o)
        {
            o = new Output();
            o.Aim = tgtPos;
            o.Hold = Mathf.Max(180f, ralt);
            o.Throttle = throttleAttack;
            o.ApplyStick = false;
            o.UnknownScript = false;

            if (script == AiCombatAcmPickService.Script.None)
                return false;

            float stick = acmStick;
            switch (script)
            {
                case AiCombatAcmPickService.Script.JTurn:
                    EvalJTurn(u, myPos, fwd, tgtPos, tgtVel, side, ralt, tgtRalt, mySpd, corner,
                        stick, maneuverSide, ref o);
                    break;
                case AiCombatAcmPickService.Script.HighYoYo:
                    EvalHighYoYo(u, myPos, fwd, tgtPos, tgtVel, side, ralt, mySpd, stick, maneuverSide, ref o);
                    break;
                case AiCombatAcmPickService.Script.LowYoYo:
                    EvalLowYoYo(u, myPos, fwd, tgtPos, tgtVel, side, ralt, mySpd, stick, maneuverSide, ref o);
                    break;
                case AiCombatAcmPickService.Script.LagPursuit:
                    EvalLag(myPos, tgtPos, tgtFwd, side, dist, mySpd, corner, ralt, stick, maneuverSide, ref o);
                    break;
                case AiCombatAcmPickService.Script.LeadTurn:
                    o.Aim = AiCombatMathService.ComputeAirIntercept(
                        myPos, fwd * mySpd, mySpd, fwd, tgtPos, tgtVel,
                        true, false, 0.8f, 0.9f);
                    o.Hold = Mathf.Max(180f, ralt + (tgtPos.y - myPos.y));
                    o.Throttle = 1f;
                    SetStick(ref o, stick * 0.7f, maneuverSide * 0.7f, 0f);
                    break;
                case AiCombatAcmPickService.Script.Scissors:
                    EvalScissors(elapsed, myPos, fwd, side, mySpd, corner, ralt, stick, maneuverSide, ref o);
                    break;
                case AiCombatAcmPickService.Script.EnergyExtend:
                    EvalEnergyExtend(u, myPos, fwd, side, ralt, stick, maneuverSide, ref o);
                    break;
                case AiCombatAcmPickService.Script.ZoomClimb:
                    o.Aim = myPos + Vector3.up * 2200f + fwd * 400f + side * 250f;
                    o.Hold = ralt + 1100f;
                    o.Throttle = 1f;
                    SetStick(ref o, stick, 0f, 0f);
                    break;
                default:
                    o.UnknownScript = true;
                    return false;
            }

            ApplyTerrainFloor(script, myPos, ralt, stick, ref o);
            return true;
        }

        private static void SetStick(ref Output o, float pitch, float roll, float yaw)
        {
            o.StickPitch = pitch;
            o.StickRoll = roll;
            o.StickYaw = yaw;
            o.ApplyStick = true;
        }

        private static void EvalJTurn(
            float u, Vector3 myPos, Vector3 fwd, Vector3 tgtPos, Vector3 vel, Vector3 side,
            float ralt, float tgtRalt, float mySpd, float corner, float stick, float maneuverSide,
            ref Output o)
        {
            if (u < 0.28f)
            {
                o.Aim = myPos + fwd * 400f + Vector3.up * 1600f + side * 200f;
                o.Hold = ralt + 700f;
                o.Throttle = 0.55f;
                SetStick(ref o, stick, maneuverSide * 0.35f, 0f);
            }
            else if (u < 0.72f)
            {
                o.Aim = myPos - fwd * 900f + side * 2200f + Vector3.up * 400f;
                o.Hold = ralt + 250f;
                o.Throttle = Mathf.Clamp01(mySpd / Mathf.Max(40f, corner) * 0.7f);
                SetStick(ref o, stick * 0.85f, maneuverSide, 0.15f * maneuverSide);
            }
            else
            {
                o.Aim = AiCombatMathService.ComputeAirIntercept(
                    myPos, fwd * mySpd, mySpd, fwd, tgtPos, vel, true, false, 0.75f, 0.9f);
                o.Hold = Mathf.Max(ralt, tgtRalt + 80f);
                o.Throttle = 1f;
                SetStick(ref o, stick * 0.6f, maneuverSide * 0.4f, 0f);
            }
        }

        private static void EvalHighYoYo(
            float u, Vector3 myPos, Vector3 fwd, Vector3 tgtPos, Vector3 vel, Vector3 side,
            float ralt, float mySpd, float stick, float maneuverSide, ref Output o)
        {
            if (u < 0.45f)
            {
                o.Aim = myPos + fwd * 600f + Vector3.up * 1400f + side * 400f;
                o.Hold = ralt + 900f;
                o.Throttle = 0.75f;
                SetStick(ref o, stick * 0.9f, maneuverSide * 0.5f, 0f);
            }
            else
            {
                o.Aim = AiCombatMathService.ComputeAirIntercept(
                    myPos, fwd * mySpd, mySpd, fwd, tgtPos, vel, true, false, 0.75f, 0.9f);
                o.Hold = Mathf.Max(220f, ralt * 0.6f);
                o.Throttle = 0.9f;
                SetStick(ref o, stick * 0.5f, -maneuverSide * 0.35f, 0f);
            }
        }

        private static void EvalLowYoYo(
            float u, Vector3 myPos, Vector3 fwd, Vector3 tgtPos, Vector3 vel, Vector3 side,
            float ralt, float mySpd, float stick, float maneuverSide, ref Output o)
        {
            if (u < 0.4f)
            {
                o.Aim = myPos + fwd * 800f + side * 500f - Vector3.up * 500f;
                o.Hold = Mathf.Max(160f, ralt * 0.45f);
                o.Throttle = 1f;
                SetStick(ref o, -stick * 0.35f, maneuverSide * 0.55f, 0f);
            }
            else
            {
                o.Aim = AiCombatMathService.ComputeAirIntercept(
                    myPos, fwd * mySpd, mySpd, fwd, tgtPos, vel, true, false, 0.75f, 0.9f);
                o.Hold = ralt + 350f;
                o.Throttle = 0.85f;
                SetStick(ref o, stick * 0.75f, maneuverSide * 0.4f, 0f);
            }
        }

        private static void EvalLag(
            Vector3 myPos, Vector3 tgtPos, Vector3 tgtFwd, Vector3 side, float dist,
            float mySpd, float corner, float ralt, float stick, float maneuverSide, ref Output o)
        {
            o.Aim = tgtPos - tgtFwd * Mathf.Clamp(dist * 0.35f, 250f, 900f) + side * 180f;
            o.Hold = Mathf.Max(180f, ralt + (tgtPos.y - myPos.y));
            o.Throttle = Mathf.Clamp(mySpd < corner ? 1f : 0.7f, 0.55f, 1f);
            SetStick(ref o, stick * 0.4f, maneuverSide * 0.25f, 0f);
        }

        private static void EvalScissors(
            float elapsed, Vector3 myPos, Vector3 fwd, Vector3 side,
            float mySpd, float corner, float ralt, float stick, float maneuverSide, ref Output o)
        {
            float weave = Mathf.Sin(elapsed * 3.2f);
            o.Aim = myPos + fwd * 500f + side * (1400f * weave) + Vector3.up * (200f * Mathf.Abs(weave));
            o.Hold = Mathf.Max(180f, ralt);
            o.Throttle = mySpd > corner * 1.1f ? 0.5f : 0.95f;
            SetStick(ref o, stick * 0.55f * Mathf.Sign(weave + 0.01f), maneuverSide * stick * 0.85f, 0f);
        }

        private static void EvalEnergyExtend(
            float u, Vector3 myPos, Vector3 fwd, Vector3 side, float ralt,
            float stick, float maneuverSide, ref Output o)
        {
            if (u < 0.55f)
            {
                o.Aim = myPos + fwd * 2500f + side * 600f - Vector3.up * 350f;
                o.Hold = Mathf.Max(150f, ralt * 0.5f);
                o.Throttle = 1f;
                SetStick(ref o, -0.2f * stick, maneuverSide * 0.3f, 0f);
            }
            else
            {
                o.Aim = myPos + Vector3.up * 1800f + side * 400f;
                o.Hold = ralt + 800f;
                o.Throttle = 1f;
                SetStick(ref o, stick, maneuverSide * 0.2f, 0f);
            }
        }

        private static void ApplyTerrainFloor(
            AiCombatAcmPickService.Script script,
            Vector3 myPos,
            float ralt,
            float stick,
            ref Output o)
        {
            o.Hold = Mathf.Max(o.Hold, 120f);
            if (ralt >= 140f)
                return;
            if (script == AiCombatAcmPickService.Script.LowYoYo
                || script == AiCombatAcmPickService.Script.EnergyExtend)
                return;
            o.Aim.y = myPos.y + 800f;
            o.Hold = ralt + 500f;
            SetStick(ref o, stick, 0f, 0f);
        }
    }
}
