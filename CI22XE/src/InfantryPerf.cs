using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Infantry ray masks. Does not change QualitySettings, walk collision, or animations.
    /// Sight rays skip FX / UI layers so LOS is cheaper in dense scenes; Default (aircraft) stays on.
    /// </summary>
    internal static class InfantryPerf
    {
        // TagManager: Default=0 Cockpit=3 Water=4 Statics=6 Ships=11 CockpitAndExternal=14
        internal static readonly int SightMask =
            (1 << 0) | (1 << 3) | (1 << 4) | (1 << 6) | (1 << 11) | (1 << 14);

        internal static void ApplyWalkCollider(CharacterController cc)
        {
            if (cc == null)
                return;
            try
            {
                cc.minMoveDistance = 0.002f;
            }
            catch { }
        }
    }
}
