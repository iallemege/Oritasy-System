using System.Globalization;
using System.Text;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield flight-score math (0.0.9.60).
    /// Pure scoring / XP mul / grade; FlightAnalysis owns recording + UI.
    /// </summary>
    internal static class FlightScoreMathService
    {
        internal const float HardXpMulCap = 3.8f;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static void ScoreFlight(FlightAnalysis.AnalysisResult r)
        {
            if (r == null)
                return;

            float score = 70f;
            score += (r.Smoothness - 50f) * 0.35f;
            if (r.HighDeflPct > 40f)
                score -= (r.HighDeflPct - 40f) * 0.25f;
            if (r.Crash)
                score -= 40f;
            if (r.LandingValid)
            {
                if (r.LandSink < 2.5f) score += 12f;
                else if (r.LandSink < 5f) score += 6f;
                else if (r.LandSink < 9f) score -= 4f;
                else score -= 15f;
            }
            if (r.MaxAbsG > 12f)
                score -= 5f;

            float bonus = 0f;
            StringBuilder mnotes = new StringBuilder();
            if (r.NoePct >= 8f)
            {
                float b = Mathf.Clamp(r.NoePct * 0.35f, 0f, 12f);
                bonus += b;
                mnotes.Append(string.Format(Inv,
                    "NOE / terrain hug +{0:0} ({1:0.0}% airborne low+fast). ",
                    b, r.NoePct));
            }
            if (r.HighGTurnPct >= 4f)
            {
                float b = Mathf.Clamp(r.HighGTurnPct * 0.4f, 0f, 10f);
                bonus += b;
                mnotes.Append(string.Format(Inv,
                    "Sustained high-G turns +{0:0} ({1:0.0}%). ",
                    b, r.HighGTurnPct));
            }
            if (!r.Crash && r.InvertedPct >= 2f && r.InvertedPct <= 35f)
            {
                float b = Mathf.Clamp(r.InvertedPct * 0.25f, 0f, 6f);
                bonus += b;
                mnotes.Append(string.Format(Inv,
                    "Inverted flight +{0:0} ({1:0.0}%). ",
                    b, r.InvertedPct));
            }
            if (r.LandingValid && r.LandSink < 2.5f && r.LandSpd < 80f)
            {
                bonus += 3f;
                mnotes.Append("Smooth landing +3. ");
            }
            if (r.Smoothness >= 75f && r.DurationSec >= 45f)
            {
                bonus += 2f;
                mnotes.Append("Smooth stick work +2. ");
            }
            r.ManeuverBonus = Mathf.Clamp(Mathf.RoundToInt(bonus), 0, 25);
            score += r.ManeuverBonus;
            r.ManeuverNotes = mnotes.Length > 0
                ? mnotes.ToString().Trim()
                : "No maneuver bonuses.";

            r.Score = Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);
            r.Grade = GradeFromScore(r.Score);

            StringBuilder tips = new StringBuilder();
            if (r.Smoothness < 55f)
                tips.Append("Smooth stick inputs — reduce high-frequency jitter. ");
            if (r.HighDeflPct > 45f)
                tips.Append("Less full-deflection time; plan earlier, smaller corrections. ");
            if (r.LandingValid && r.LandSink >= 5f)
                tips.Append("Flare earlier; lower sink rate before touchdown. ");
            if (r.Crash)
                tips.Append("Aircraft was disabled / heavily damaged. ");
            if (r.NoePct < 5f && r.DurationSec >= 60f && !r.Crash)
                tips.Append("Try nap-of-the-earth runs for maneuver bonus. ");
            if (tips.Length == 0)
                tips.Append("Solid flight — keep practicing pattern work and smooth landings.");
            r.Tips = tips.ToString().Trim();
        }

        internal static string GradeFromScore(int score)
        {
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }

        internal static float XpMultiplierForScore(int score, bool enabled, float maxMulConfig)
        {
            if (!enabled)
                return 1f;
            float maxMul = maxMulConfig;
            if (maxMul < 1f) maxMul = 1f;
            if (maxMul > HardXpMulCap) maxMul = HardXpMulCap;
            float t = Mathf.Clamp01(score / 100f);
            float mul = 1f + (maxMul - 1f) * t;
            if (mul < 1f) mul = 1f;
            if (mul > HardXpMulCap) mul = HardXpMulCap;
            return mul;
        }

        internal static string FormatXpMulLabel(float mul, bool chinese)
        {
            if (mul <= 1.001f)
                return chinese ? "经验 ×1.0" : "XP ×1.0 (flight score)";
            return chinese
                ? ("经验 ×" + mul.ToString("0.0", Inv))
                : ("XP ×" + mul.ToString("0.0", Inv) + " (flight score)");
        }
    }
}
