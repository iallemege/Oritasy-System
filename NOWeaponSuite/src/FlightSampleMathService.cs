using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield flight-sample classifiers + landing-note FSM (0.0.9.87).
    /// FlightAnalysis owns CSV I/O, UI, and accumulator state.
    /// </summary>
    internal static class FlightSampleMathService
    {
        internal const float HighDeflAbs = 0.7f;
        internal const float FullThrMin = 0.95f;
        internal const float IdleThrMax = 0.05f;
        internal const float InvertedBankDeg = 90f;
        internal const float NoeAglMinM = 8f;
        internal const float NoeAglMaxM = 90f;
        internal const float NoeMinSpdMps = 80f;
        internal const float HighGAbs = 4.5f;
        internal const float HighGBankDeg = 40f;
        internal const float SmoothnessJitterScale = 220f;
        internal const float SoftLandSinkMps = 2.5f;
        internal const float SoftLandSpdMps = 80f;
        internal const float AcceptLandSinkMps = 5f;
        internal const float FirmLandSinkMps = 9f;

        internal enum LandingGrade
        {
            None = 0,
            Soft = 1,
            Acceptable = 2,
            Firm = 3,
            Hard = 4
        }

        internal static bool IsHighDeflection(float pitchIn, float rollIn, float yawIn)
        {
            return Mathf.Abs(pitchIn) >= HighDeflAbs
                || Mathf.Abs(rollIn) >= HighDeflAbs
                || Mathf.Abs(yawIn) >= HighDeflAbs;
        }

        internal static bool IsFullThrottle(float thr01)
        {
            return thr01 >= FullThrMin;
        }

        internal static bool IsIdleThrottle(float thr01)
        {
            return thr01 <= IdleThrMax;
        }

        internal static bool IsInverted(float bankAbsDeg)
        {
            return bankAbsDeg > InvertedBankDeg;
        }

        internal static bool IsNoe(bool airborne, float raltM, float spdMps)
        {
            if (!airborne)
                return false;
            return raltM >= NoeAglMinM && raltM <= NoeAglMaxM && spdMps >= NoeMinSpdMps;
        }

        internal static bool IsHighGTurn(bool airborne, float absG, float bankAbsDeg)
        {
            if (!airborne)
                return false;
            return absG >= HighGAbs && bankAbsDeg >= HighGBankDeg;
        }

        internal static bool ShouldMarkTakeoff(bool prevAirborne, bool airborne)
        {
            return !prevAirborne && airborne;
        }

        internal static bool ShouldMarkLanding(bool prevAirborne, bool landed, bool hadAirborne)
        {
            return prevAirborne && landed && hadAirborne;
        }

        internal static float Pct(int count, int total)
        {
            int n = total > 0 ? total : 1;
            return 100f * count / n;
        }

        internal static float Rms(double sumSq, int count)
        {
            int n = count > 0 ? count : 1;
            return Mathf.Sqrt((float)(sumSq / n));
        }

        internal static float SmoothnessFromJitter(double jitterSumSq, int jitterSamples)
        {
            float jitterRms = 0f;
            if (jitterSamples > 0)
                jitterRms = Mathf.Sqrt((float)(jitterSumSq / jitterSamples));
            return Mathf.Clamp(100f - jitterRms * SmoothnessJitterScale, 0f, 100f);
        }

        internal static LandingGrade ClassifyLanding(bool valid, float sinkMps, float spdMps)
        {
            if (!valid)
                return LandingGrade.None;
            if (sinkMps < SoftLandSinkMps && spdMps < SoftLandSpdMps)
                return LandingGrade.Soft;
            if (sinkMps < AcceptLandSinkMps)
                return LandingGrade.Acceptable;
            if (sinkMps < FirmLandSinkMps)
                return LandingGrade.Firm;
            return LandingGrade.Hard;
        }

        /// <summary>English note stored on AnalysisResult / disk.</summary>
        internal static string LandingNoteEn(LandingGrade grade)
        {
            switch (grade)
            {
                case LandingGrade.Soft:
                    return "Soft touchdown.";
                case LandingGrade.Acceptable:
                    return "Acceptable landing.";
                case LandingGrade.Firm:
                    return "Firm landing — reduce sink rate.";
                case LandingGrade.Hard:
                    return "Hard landing — high sink rate at contact.";
                default:
                    return "No touchdown detected.";
            }
        }

        internal static string LandingNoteLocalized(LandingGrade grade, bool zh)
        {
            if (!zh)
                return LandingNoteEn(grade);
            switch (grade)
            {
                case LandingGrade.Soft:
                    return "柔和接地";
                case LandingGrade.Acceptable:
                    return "着陆尚可";
                case LandingGrade.Firm:
                    return "着陆偏重，降低下沉率";
                case LandingGrade.Hard:
                    return "硬着陆，接地时下沉率过高";
                default:
                    return "未检测到着陆";
            }
        }
    }
}
