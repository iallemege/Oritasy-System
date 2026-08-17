using System.Collections.Generic;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Per-missile gun/cruise/ballistic flags for Seek / SetTarget hot paths.
    /// Uncached classify does GetComponent(InChildren) — must not run every physics frame.
    /// Flags: bit0=resolved, bit1=gunShell, bit2=ballistic, bit3=cruise.
    /// </summary>
    internal static class MissileFlightClassCache
    {
        private static readonly Dictionary<int, byte> FlagsById = new Dictionary<int, byte>(256);
        private static float _nextPrune;

        internal const byte Resolved = 1;
        internal const byte GunShell = 2;
        internal const byte Ballistic = 4;
        internal const byte Cruise = 8;

        internal static bool TryGet(int instanceId, out byte flags)
        {
            return FlagsById.TryGetValue(instanceId, out flags) && (flags & Resolved) != 0;
        }

        internal static void Set(int instanceId, byte flags)
        {
            FlagsById[instanceId] = (byte)(flags | Resolved);
            MaybePrune();
        }

        internal static void Clear()
        {
            FlagsById.Clear();
        }

        private static void MaybePrune()
        {
            if (Time.unscaledTime < _nextPrune)
                return;
            _nextPrune = Time.unscaledTime + 45f;
            if (FlagsById.Count > 384)
                FlagsById.Clear();
        }
    }
}
