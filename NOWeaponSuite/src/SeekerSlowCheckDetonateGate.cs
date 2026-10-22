namespace WeXon
{
    /// <summary>
    /// SlowChecks must run (SetTangible, loft, SARH radar). Only the Detonate
    /// inside SlowChecks is gated — vanilla selfDestructAtSpeed is ~150–270 m/s
    /// and airbursts coasting shots after burnout.
    /// </summary>
    internal static class SeekerSlowCheckDetonateGate
    {
        private static int _depth;

        internal static void Enter()
        {
            _depth++;
        }

        internal static void Exit()
        {
            if (_depth > 0)
                _depth--;
        }

        internal static bool InSlowChecks
        {
            get { return _depth > 0; }
        }
    }
}
