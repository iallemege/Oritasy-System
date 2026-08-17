namespace WeXon
{
    /// <summary>
    /// Greenfield flight recorder start/stop/sample + auto-score open gates (0.0.9.92).
    /// FlightAnalysis owns CSV I/O, UI chrome, and aircraft probes.
    /// </summary>
    internal static class FlightRecordGateService
    {
        internal const float MinSampleHz = 2f;
        internal const float MaxSampleHz = 12f;
        internal const int MinSamplesForAnalyze = 4;

        internal enum RecordAction
        {
            None = 0,
            Start = 1,
            StopAnalyze = 2
        }

        internal enum AutoScorePath
        {
            Wait = 0,
            MarkHadPlayer = 1,
            Trigger = 2
        }

        internal static RecordAction ResolveRecordAction(bool ownsRecording, bool hasAircraft, bool recording)
        {
            if (!ownsRecording)
                return RecordAction.None;
            if (hasAircraft && !recording)
                return RecordAction.Start;
            if (!hasAircraft && recording)
                return RecordAction.StopAnalyze;
            return RecordAction.None;
        }

        internal static bool ShouldAnalyzeOnStop(bool analyzeRequested, bool autoAnalyze, int sampleCount)
        {
            return analyzeRequested
                && autoAnalyze
                && sampleCount >= MinSamplesForAnalyze;
        }

        internal static float ClampSampleHz(float configuredHz, float perfCapHz)
        {
            float hz = configuredHz;
            if (perfCapHz > 0f && hz > perfCapHz)
                hz = perfCapHz;
            if (hz < MinSampleHz)
                hz = MinSampleHz;
            if (hz > MaxSampleHz)
                hz = MaxSampleHz;
            return hz;
        }

        internal static float ScheduleNextSample(float now, float hz)
        {
            float h = hz > 0.01f ? hz : MinSampleHz;
            return now + 1f / h;
        }

        internal static bool SampleDue(float now, float nextSampleAt)
        {
            return now >= nextSampleAt;
        }

        /// <summary>
        /// Auto-open after crash/death/leave once enough samples and sortie had airborne
        /// (or hard crash/death without airborne).
        /// </summary>
        internal static AutoScorePath ResolveAutoScore(
            bool alreadyOpened,
            int sampleCount,
            bool hasAircraft,
            bool crash,
            bool dead,
            bool left,
            bool hadAirborne)
        {
            if (alreadyOpened || sampleCount < MinSamplesForAnalyze)
                return hasAircraft ? AutoScorePath.MarkHadPlayer : AutoScorePath.Wait;
            if (!crash && !dead && !left)
                return AutoScorePath.Wait;
            if (!hadAirborne && !crash && !dead)
                return AutoScorePath.Wait;
            return AutoScorePath.Trigger;
        }
    }
}
