using System.Collections.Generic;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// LOAL salvo pick: prefer higher UnitDefinition.value and cap how many
    /// missiles may lock one cheap target in the same frame / salvo.
    /// </summary>
    internal static class HuntSalvoGateService
    {
        internal const float ValueWeight = 6000f;
        internal const float ExtraLockPenalty = 20000f;
        internal const float OverCapPenalty = 500000f;

        private static int _frame = -1;
        private static readonly Dictionary<int, int> Pending = new Dictionary<int, int>(32);

        internal static int MaxLoalLocks(float valueM)
        {
            if (valueM < 12f)
                return 1;
            if (valueM < 25f)
                return 2;
            if (valueM < 45f)
                return 3;
            if (valueM < 80f)
                return 4;
            return 6;
        }

        internal static float HuntScore(float valueM, float distM, int existingLocks, int maxLocks)
        {
            float dist = distM < 1f ? 1f : distM;
            if (maxLocks < 1)
                maxLocks = 1;
            float score = valueM * ValueWeight - dist - existingLocks * ExtraLockPenalty;
            if (existingLocks >= maxLocks)
                score -= OverCapPenalty;
            return score;
        }

        internal static int PendingLocks(int unitId)
        {
            BeginFrame();
            int n;
            if (Pending.TryGetValue(unitId, out n))
                return n;
            return 0;
        }

        internal static void NotePick(int unitId)
        {
            if (unitId == 0)
                return;
            BeginFrame();
            int n;
            Pending.TryGetValue(unitId, out n);
            Pending[unitId] = n + 1;
        }

        private static void BeginFrame()
        {
            int f = Time.frameCount;
            if (f == _frame)
                return;
            _frame = f;
            Pending.Clear();
        }
    }
}
