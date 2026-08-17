using System;

namespace WeXon
{
    /// <summary>
    /// Optional hook for Oritasy missile PiP. Combined Oritasy.dll registers handlers;
    /// WeXon-only builds leave these null (no-op).
    /// </summary>
    internal static class MissileCameraBridge
    {
        /// <summary>Single player-owned missile spawned (bus or GS25).</summary>
        internal static Action<Missile> NotifySpawn;

        /// <summary>After AGM-T bus discard — switch PiP / follow to submunitions.</summary>
        internal static Action<Missile[]> HandoffCluster;

        internal static void TryNotifySpawn(Missile missile)
        {
            Action<Missile> h = NotifySpawn;
            if (h != null && missile != null)
            {
                try { h(missile); }
                catch { }
            }
        }

        internal static void TryHandoffCluster(Missile[] children)
        {
            Action<Missile[]> h = HandoffCluster;
            if (h != null && children != null && children.Length > 0)
            {
                try { h(children); }
                catch { }
            }
        }
    }
}
