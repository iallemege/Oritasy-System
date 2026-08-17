using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield STOVL / VTOL pad approach FSM (0.0.9.97).
    /// LandingGuidance owns AutoAim/Hover, gear, and input field writes.
    /// </summary>
    internal static class LandingVtolPadGateService
    {
        internal struct Geometry
        {
            public float SternLong;
            public float FinalLong;
            public float PadAlong;
            public float HoldAgl;
            public float GoAroundHoldAgl;
        }

        internal struct Phase
        {
            public bool Overshot;
            public bool OnSternLine;
            public bool ShortStern;
            public bool NearDeck;
            public bool NeedStern;
        }

        internal static Geometry ComputeGeometry(bool carrier)
        {
            Geometry g;
            g.SternLong = carrier ? 2200f : 1600f;
            g.FinalLong = carrier ? 550f : 450f;
            g.PadAlong = carrier ? 35f : 55f;
            g.HoldAgl = carrier ? 140f : 160f;
            g.GoAroundHoldAgl = carrier ? 160f : 180f;
            return g;
        }

        internal static bool IsPadDown(bool landedFlag, float ralt, float speed)
        {
            return landedFlag || (ralt < 2f && speed < 15f);
        }

        internal static bool ShouldDropGear(float horiz, float ralt)
        {
            return horiz < 5000f || ralt < 450f;
        }

        internal static Phase ResolvePhase(
            float along,
            float absLat,
            float horiz,
            float ralt,
            Geometry g)
        {
            Phase p;
            p.Overshot = along < -120f
                || (along < 80f && absLat > 180f && horiz < 900f)
                || (along < -40f && horiz < 500f);
            p.OnSternLine = along > 80f && along < g.SternLong + 400f && absLat < 220f;
            p.ShortStern = along > 60f && along < g.FinalLong + 80f && absLat < 90f;
            p.NearDeck = horiz < 350f && ralt < 45f && along > -80f && along < 450f && absLat < 120f;
            p.NeedStern = !p.OnSternLine || along > g.SternLong * 0.85f || absLat > 120f || ralt > 250f;
            return p;
        }

        internal static bool ShouldCaptureHover(
            Phase p, float speed, float ralt, float horiz, float along, float absLat)
        {
            return (p.ShortStern && speed < 90f && ralt < 220f && absLat < 80f)
                || p.NearDeck
                || (ralt < 40f && horiz < 500f && along > -40f && absLat < 100f && speed < 100f);
        }

        internal static bool ShouldReleaseHover(float ralt, bool overshot, float absLat)
        {
            return ralt > 300f || overshot || absLat > 150f;
        }

        internal static float SoftenHoldForSink(float hold, float ralt, float sink, bool hovering)
        {
            if (!hovering && ralt < 350f && sink > 25f)
                return Mathf.Max(hold, ralt + Mathf.Clamp((sink - 25f) * 2.5f, 20f, 180f));
            return hold;
        }

        internal static float IngressAxisTarget(float ralt, bool nearDeck, bool shortStern, float along, float finalLong, float absLat)
        {
            if (ralt < 40f || nearDeck)
                return 1f;
            if (shortStern && ralt < 200f)
                return 0.7f;
            if (along < finalLong + 200f && ralt < 250f && absLat < 120f)
                return 0.35f;
            return 0f;
        }

        internal static float IngressTargetSpeed(bool needStern, float horiz, float along)
        {
            if (needStern)
                return horiz > 2500f ? 150f : 110f;
            return along > 800f ? 85f : 55f;
        }

        internal static float IngressThrottle(
            float tgtSpd, float speed, float cruise, float ralt, float hold, float sink, float err)
        {
            float thr = Mathf.Clamp((tgtSpd + 6f - speed) * 0.07f, 0.15f, cruise * 0.8f);
            if (ralt > hold + 40f && sink < 2f)
                thr = Mathf.Min(thr, 0.22f);
            if (ralt > hold + 40f && sink < -1f)
                thr = Mathf.Min(thr, 0.12f);
            if (sink > 40f && ralt < 300f)
                thr = Mathf.Max(thr, 0.55f);
            if (sink > 80f && ralt < 250f)
                thr = Mathf.Max(thr, 0.85f);
            if (err > 30f)
                thr = Mathf.Min(thr, 0.2f);
            return thr;
        }

        internal static float IngressBrake(float err)
        {
            return err > 25f ? Mathf.Clamp01((err - 25f) * 0.025f) : 0f;
        }

        internal static float ResolveHoverTargetAgl(float along, float absLat, float ralt)
        {
            float targetAgl;
            if (along > 180f || absLat > 50f)
                targetAgl = Mathf.Clamp(Mathf.Min(ralt, 80f), 18f, 90f);
            else if (ralt > 50f)
                targetAgl = Mathf.Max(18f, ralt - 12f);
            else if (ralt > 28f)
                targetAgl = Mathf.Max(8f, ralt - 8f);
            else if (ralt > 12f)
                targetAgl = Mathf.Max(2.5f, ralt * 0.45f);
            else if (ralt > 4f)
                targetAgl = Mathf.Max(1.2f, ralt - 2f);
            else
                targetAgl = 0.8f;
            return Mathf.Min(targetAgl, Mathf.Max(0.8f, ralt - 0.5f));
        }

        internal static float HoverAxisTarget(float ralt, bool nearDeck)
        {
            return (ralt < 45f || nearDeck) ? 1f : 0.85f;
        }

        internal static float HoverThrottle(float sink, float ralt)
        {
            float thrH = 0.55f;
            if (sink > 3f)
                thrH = 0.75f;
            if (sink > 6f)
                thrH = 0.92f;
            if (sink > 10f)
                thrH = 1f;
            if (sink < 1.2f && ralt > 6f && ralt < 40f)
                thrH = Mathf.Lerp(0.28f, 0.48f, Mathf.Clamp01((ralt - 6f) / 30f));
            if (sink < -1f)
                thrH = 0.35f;
            return Mathf.Clamp(thrH, 0.22f, 1f);
        }

        internal static float HoverBrake(float speed, float along)
        {
            return (speed > 30f && along < 180f)
                ? Mathf.Clamp01((speed - 30f) * 0.045f) : 0f;
        }
    }
}
