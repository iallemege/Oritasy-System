using System.Diagnostics;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Main-thread frame budget. Unity APIs stay on the game thread.
    /// After a hitch we skip hunt / polish (fonts, music, zh scan) — never skip HUD draws.
    /// </summary>
    internal static class PerfFrameGate
    {
        private static readonly Stopwatch Clock = new Stopwatch();
        private static int _begunFrame = -1;
        private static int _recoverUntil = -1;
        private static float _lastDtMs;
        private static bool _softSkip;
        private static int _hitchRecoveries;

        internal static float LastDtMs
        {
            get { return _lastDtMs; }
        }

        internal static bool Recovering
        {
            get { return Time.frameCount <= _recoverUntil; }
        }

        internal static int HitchRecoveries
        {
            get { return _hitchRecoveries; }
        }

        internal static int PumpLimit
        {
            get { return Recovering ? 2 : 16; }
        }

        /// <summary>Idempotent per Unity frame — first Update (Oritasy or hosted WeXon) wins.</summary>
        internal static void BeginFrame()
        {
            int f = Time.frameCount;
            if (_begunFrame == f)
                return;
            _begunFrame = f;

            _lastDtMs = Time.unscaledDeltaTime * 1000f;
            _softSkip = _lastDtMs >= 20f;
            if (_lastDtMs >= 33.5f)
            {
                _recoverUntil = f + 2;
                _hitchRecoveries++;
            }

            Clock.Reset();
            Clock.Start();
        }

        /// <summary>Fonts / music / zh scan. HUD ticks and OnGUI draws must not use this gate.</summary>
        internal static bool AllowPolish()
        {
            if (Recovering || _softSkip)
                return false;
            float budgetMs = 3.5f;
            if (PerfMode.IsLow)
                budgetMs = 1.6f;
            else if (PerfMode.IsMedOrLower)
                budgetMs = 2.4f;
            return Clock.ElapsedMilliseconds < (long)budgetMs;
        }

        internal static string SnapshotLine()
        {
            return "frameGate dt=" + _lastDtMs.ToString("0.0") + "ms"
                + "  recover=" + (Recovering ? "Y" : "n")
                + "  softSkip=" + (_softSkip ? "Y" : "n")
                + "  recoveries=" + _hitchRecoveries.ToString()
                + "  pump=" + PumpLimit.ToString();
        }
    }
}
