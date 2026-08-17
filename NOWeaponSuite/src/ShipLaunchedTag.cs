using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Marker on the ship edition of a missile. Stamped only when a naval launcher
    /// (Ship / turret under Ship, never Aircraft) actually fires it.
    /// Air-launched dual-role munitions must not get this tag.
    /// </summary>
    internal sealed class ShipLaunchedTag : MonoBehaviour
    {
    }
}
