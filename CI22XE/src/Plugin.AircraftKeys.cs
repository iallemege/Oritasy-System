using System;

namespace Oritasy
{
    /// <summary>Thin facade — identity lives in AircraftIdentity (0.0.9.56 greenfield).</summary>
    public partial class Plugin
    {
        internal static string StripXeSuffix(string s)
        {
            return AircraftIdentity.StripXe(s);
        }

        internal static string GetAircraftKey(Aircraft aircraft)
        {
            return AircraftIdentity.GetKey(aircraft);
        }

        internal static bool IsFs12Key(string key)
        {
            return AircraftIdentity.IsFs12(key);
        }

        internal static bool IsFs20Key(string key)
        {
            return AircraftIdentity.IsFs20(key);
        }

        internal static bool IsStovlNozzleAircraft(Aircraft aircraft)
        {
            return AircraftIdentity.IsStovlNozzle(aircraft);
        }

        internal static bool IsSfbKey(string key)
        {
            return AircraftIdentity.IsSfb(key);
        }

        internal static bool IsEw25Key(string key)
        {
            return AircraftIdentity.IsEw25(key);
        }

        internal static bool IsKr67Key(string key)
        {
            return AircraftIdentity.IsKr67(key);
        }

        internal static bool IsUh90Key(string key)
        {
            return AircraftIdentity.IsUh90(key);
        }

        internal static bool IsA19Key(string key)
        {
            return AircraftIdentity.IsA19(key);
        }
    }
}
