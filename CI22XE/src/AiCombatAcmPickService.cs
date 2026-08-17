using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield ACM geometry picker (0.0.9.80).
    /// Pure range/speed/aspect gates — AiCombatBrain owns RNG side, duration, StartManeuver.
    /// </summary>
    internal static class AiCombatAcmPickService
    {
        internal enum Script
        {
            None = 0,
            JTurn = 1,
            HighYoYo = 2,
            LowYoYo = 3,
            LagPursuit = 4,
            LeadTurn = 5,
            Scissors = 6,
            EnergyExtend = 7,
            ZoomClimb = 8
        }

        /// <summary>
        /// First matching energy/ACM script from geometry. Returns None if no rule fires.
        /// </summary>
        internal static Script Pick(
            float dist,
            float angle,
            float aspect,
            float mySpd,
            float tgtSpd,
            float corner,
            float ralt,
            float breakDist,
            float skill)
        {
            return Pick(dist, angle, aspect, mySpd, tgtSpd, corner, ralt, breakDist, skill, 0f);
        }

        internal static Script Pick(
            float dist,
            float angle,
            float aspect,
            float mySpd,
            float tgtSpd,
            float corner,
            float ralt,
            float breakDist,
            float skill,
            float dAlt)
        {
            // Overshoot / too close nose-on → J-turn
            if (dist < breakDist * 1.35f && angle < 55f && mySpd > corner * 0.85f)
                return Script.JTurn;

            // Vertical only when truly separated AND not already in a gun fight.
            // 280 m used to force ZoomClimb/YoYo and killed flat dogfights.
            bool closeFight = dist < 2800f;
            if (!closeFight && Mathf.Abs(dAlt) > 1200f)
            {
                if (dAlt > 1200f && ralt < 4500f)
                    return Script.ZoomClimb;
                if (dAlt < -1200f && ralt > 260f)
                    return Script.LowYoYo;
                return Script.HighYoYo;
            }

            // Fast + poor angle off → High yo-yo (bleed closure, keep energy)
            if (dist < 2800f && dist > 400f && mySpd > tgtSpd * 1.08f && angle > 25f && ralt > 350f)
                return Script.HighYoYo;

            // Slow / below corner in a turn fight → Low yo-yo
            if (dist < 2200f && mySpd < corner * 0.92f && angle > 30f && ralt > 280f)
                return Script.LowYoYo;

            // Behind but not in gun/missile cone → lag pursuit
            if (aspect > 110f && angle > 18f && dist < 3500f && dist > 300f)
                return Script.LagPursuit;

            // Head-on / high aspect merge → lead turn
            if (aspect < 50f && angle < 50f && dist > 800f && dist < 6000f)
                return Script.LeadTurn;

            // Flat scissors when slow turning fight
            if (dist < 1600f && angle > 40f && mySpd < corner * 1.15f && skill > 0.4f)
                return Script.Scissors;

            // Energy extend when hot and close
            if (dist < breakDist * 1.1f && mySpd > corner && ralt > 200f)
                return Script.EnergyExtend;

            // Zoom climb when fast with room — not during a merge/turn fight.
            if (mySpd > corner * 1.25f && ralt < 1200f && dist > 4000f && angle < 25f && skill > 0.5f)
                return Script.ZoomClimb;

            return Script.None;
        }

        /// <summary>Base duration range (lo, hi) before tier AcmDuration scale.</summary>
        internal static void DurationRange(Script script, out float lo, out float hi)
        {
            switch (script)
            {
                case Script.JTurn:
                    lo = 2.6f; hi = 1.55f; return;
                case Script.HighYoYo:
                    lo = 2.8f; hi = 1.7f; return;
                case Script.LowYoYo:
                    lo = 2.4f; hi = 1.5f; return;
                case Script.LagPursuit:
                    lo = 2.1f; hi = 1.2f; return;
                case Script.LeadTurn:
                    lo = 2.0f; hi = 1.15f; return;
                case Script.Scissors:
                    lo = 2.6f; hi = 1.55f; return;
                case Script.EnergyExtend:
                    lo = 2.3f; hi = 1.35f; return;
                case Script.ZoomClimb:
                    lo = 2.1f; hi = 1.2f; return;
                default:
                    lo = 1.5f; hi = 1.0f; return;
            }
        }
    }
}
