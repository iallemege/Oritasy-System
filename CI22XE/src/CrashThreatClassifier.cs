using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield crash-threat classifier (0.0.9.57).
    /// Pure sensing → kind/reason/hold targets; BeginnerAssist still owns engage/handback.
    /// </summary>
    internal static class CrashThreatClassifier
    {
        internal enum Kind
        {
            None = 0,
            InvertDive = 1,
            PostStall = 2,
            Spin = 3,
            Terrain = 4
        }

        internal struct Result
        {
            public Kind Kind;
            public string Reason;
            public float HoldAgl;
            public float HoldSpeed;
            public float HoldSeconds;
        }

        internal static Result Classify(
            Aircraft ac,
            bool guardianOn,
            bool terrainOn,
            bool apEngagedOnlyDepartures,
            float spinAgl,
            float spinYawRate,
            float stallAoa,
            float postStallAgl,
            float sinkTrigger,
            float corner,
            float takeoffSpd,
            float guardianCooldownSec,
            float guardianHandbackSec,
            bool invertedThreat,
            bool fallingLeaf,
            float aoa,
            float yawRate,
            float rollRate,
            float ralt,
            float sink,
            float speed)
        {
            Result r = new Result();
            r.Kind = Kind.None;
            r.Reason = null;

            float tti = ralt > 0.5f && sink > 2f ? ralt / sink : 999f;
            bool highAlt = ralt >= 2500f;

            if (guardianOn)
            {
                if (ralt > 1f
                    && (ralt < spinAgl || highAlt || fallingLeaf)
                    && (Mathf.Abs(yawRate) >= spinYawRate * (fallingLeaf ? 0.45f : 0.82f)
                        || fallingLeaf)
                    && aoa > stallAoa * (fallingLeaf ? 0.38f : 0.55f)
                    && speed < corner * (highAlt ? 1.4f : 1.2f))
                {
                    r.Kind = Kind.Spin;
                    r.Reason = fallingLeaf ? "FALLING LEAF" : (highAlt ? "HIGH-ALT SPIN" : "SPIN");
                }
                else if (!apEngagedOnlyDepartures && invertedThreat)
                {
                    r.Kind = Kind.InvertDive;
                    r.Reason = "INVERT DIVE";
                }
                else if (aoa >= stallAoa * (highAlt ? 0.75f : 0.9f)
                    && ralt > 12f
                    && (ralt < postStallAgl || highAlt)
                    && speed < corner * (highAlt ? 1.1f : 1.0f))
                {
                    r.Kind = Kind.PostStall;
                    r.Reason = highAlt ? "HIGH-ALT STALL" : "POST-STALL";
                }
                else if (!apEngagedOnlyDepartures && ralt < 200f && sink > sinkTrigger * 0.88f && ralt > 1f)
                {
                    r.Kind = Kind.Terrain;
                    r.Reason = "HIGH SINK";
                }
                else if (!apEngagedOnlyDepartures && ralt < 140f && tti < 5.2f)
                {
                    r.Kind = Kind.Terrain;
                    r.Reason = "TERRAIN CLOSURE";
                }
            }

            if (r.Kind == Kind.None && terrainOn && !apEngagedOnlyDepartures)
            {
                if (ralt < 100f && sink > 10f && ralt > 1f)
                {
                    r.Kind = Kind.Terrain;
                    r.Reason = "TERRAIN";
                }
                else if (ralt < 70f && tti < 6.5f && speed > 45f)
                {
                    r.Kind = Kind.Terrain;
                    r.Reason = "LOW ALT";
                }
            }

            if (r.Kind == Kind.None)
                return r;

            if (r.Kind == Kind.Spin || r.Kind == Kind.PostStall)
            {
                if (highAlt)
                    r.HoldAgl = Mathf.Clamp(ralt - 250f, 1500f, ralt);
                else
                    r.HoldAgl = Mathf.Max(500f, ralt + 200f);
            }
            else
                r.HoldAgl = Mathf.Max(500f, ralt + 350f);

            r.HoldSpeed = Mathf.Max(corner, takeoffSpd * 1.2f);
            if (highAlt && (r.Kind == Kind.Spin || r.Kind == Kind.PostStall))
                r.HoldSpeed = Mathf.Max(r.HoldSpeed, corner * 1.15f);

            float holdSec = Mathf.Max(0.8f, guardianHandbackSec);
            if (r.Kind == Kind.InvertDive || r.Kind == Kind.Spin)
                holdSec = Mathf.Max(holdSec, highAlt ? 4f : 2.8f);
            else if (r.Kind == Kind.PostStall)
                holdSec = Mathf.Max(holdSec, highAlt ? 3.2f : 2.2f);
            r.HoldSeconds = holdSec;
            return r;
        }
    }
}
