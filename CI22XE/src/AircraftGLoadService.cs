using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield aircraft G-load sensing (0.0.9.60).
    /// Shared by GMeterHud and Oritasy missile-pilot strip.
    /// </summary>
    internal static class AircraftGLoadService
    {
        internal static float ReadSignedG(Aircraft ac)
        {
            if (ac == null)
                return 1f;
            try
            {
                if (ac.pilots != null && ac.pilots.Length > 0 && ac.pilots[0] != null && !ac.pilots[0].dead)
                    return ac.pilots[0].gForce;
            }
            catch { }
            try
            {
                return Vector3.Dot(ac.accel, ac.transform.up);
            }
            catch
            {
                try { return ac.gForce; }
                catch { return 1f; }
            }
        }

        internal static float ResolvePositiveLimit(Aircraft ac)
        {
            if (Plugin.IsUsableXeAircraft(ac))
            {
                try
                {
                    ManeuverProfile p = Plugin.GetOrCreateProfile(ac);
                    if (p != null && p.AircraftG != null)
                        return Mathf.Clamp(p.AircraftG.Value, 4f, 28f);
                }
                catch { }
            }
            try
            {
                AircraftDefinition def = ac != null ? ac.definition as AircraftDefinition : null;
                if (def != null && def.aircraftParameters != null)
                    return Mathf.Clamp(def.aircraftParameters.aircraftGLimit, 4f, 20f);
            }
            catch { }
            return 9f;
        }
    }
}
