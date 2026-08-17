using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield aircraft identity (keys, XE/vanilla/coin, STOVL).
    /// Written from scratch for 0.0.9.56 — behavior matched to prior fleet rules.
    /// </summary>
    internal static class AircraftIdentity
    {
        private static readonly string[] VanillaMarkers = new string[]
        {
            "COIN", "CI-22", "CI22",
            "SFB-81", "SFB81",
            "FS-12", "FS12", "FS-20", "FS20",
            "VL-49", "VL49",
            "KR-67", "KR67",
            "EW-25", "EW25",
            "UH-90", "UH90",
            "A-19", "A19", "NOTA-10", "NOTA10",
            "Compass", "Chicane", "Darkreach", "Revoker", "Dynamo",
            "Cricket", "Medusa", "Ifrit", "Vortex", "Atlas", "Tarantula",
            "Ibis", "Vagrant", "Brawler", "Warthog", "Liberator",
            "AB-4", "AB4", "VT-7", "VT7",
            "T/A-30", "TA-30", "TA30", "T-A-30",
            "SAH-46", "SAH46", "Alkyon"
        };

        internal static string StripXe(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            string t = s.Trim();
            if (t.Length > 2 && t.EndsWith("XE", StringComparison.OrdinalIgnoreCase))
                return t.Substring(0, t.Length - 2);
            return t;
        }

        internal static string AppendXe(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            string t = s.TrimEnd();
            if (t.EndsWith("XE", StringComparison.OrdinalIgnoreCase))
                return t;
            return t + "XE";
        }

        internal static AircraftDefinition TryGetDefinition(Aircraft aircraft)
        {
            if (aircraft == null)
                return null;
            AircraftDefinition def = null;
            try
            {
                Unit u = aircraft;
                if (u != null)
                    def = u.definition as AircraftDefinition;
            }
            catch { }
            if (def == null)
            {
                try { def = aircraft.definition as AircraftDefinition; }
                catch { }
            }
            return def;
        }

        internal static string GetKey(Aircraft aircraft)
        {
            if (aircraft == null)
                return "unknown";
            string defKey = null;
            AircraftDefinition def = TryGetDefinition(aircraft);
            if (def != null)
            {
                if (!string.IsNullOrEmpty(def.jsonKey))
                    defKey = StripXe(def.jsonKey);
                else if (!string.IsNullOrEmpty(def.code))
                    defKey = StripXe(def.code);
                else if (!string.IsNullOrEmpty(def.unitName))
                    defKey = StripXe(def.unitName);
            }
            string nameKey = StripXe(aircraft.name != null ? aircraft.name : "");
            if (IsKnownFleet(defKey))
                return defKey;
            if (IsKnownFleet(nameKey))
                return nameKey;
            if (def != null)
            {
                if (IsKnownFleet(def.code))
                    return StripXe(def.code);
                if (IsKnownFleet(def.unitName))
                    return StripXe(def.unitName);
                if (IsKnownFleet(def.name))
                    return StripXe(def.name);
            }
            if (!string.IsNullOrEmpty(defKey))
                return defKey;
            if (!string.IsNullOrEmpty(nameKey))
                return nameKey;
            return "unknown";
        }

        private static bool TextHasMarker(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            for (int i = 0; i < VanillaMarkers.Length; i++)
            {
                if (text.IndexOf(VanillaMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        internal static bool ContainsAny(string hay, params string[] needles)
        {
            if (string.IsNullOrEmpty(hay) || needles == null)
                return false;
            for (int i = 0; i < needles.Length; i++)
            {
                if (!string.IsNullOrEmpty(needles[i])
                    && hay.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        internal static bool IsFs12(string key)
        {
            return ContainsAny(key, "FS-12", "FS12", "Revoker");
        }

        internal static bool IsFs20(string key)
        {
            return ContainsAny(key, "FS-20", "FS20", "Vortex");
        }

        internal static bool IsSfb(string key)
        {
            return ContainsAny(key, "SFB", "Darkreach");
        }

        internal static bool IsEw25(string key)
        {
            return ContainsAny(key, "EW-25", "EW25", "Medusa");
        }

        internal static bool IsKr67(string key)
        {
            return ContainsAny(key, "KR-67", "KR67", "Ifrit");
        }

        internal static bool IsUh90(string key)
        {
            return ContainsAny(key, "UH-90", "UH90", "Ibis", "King Cobra", "KingCobra");
        }

        internal static bool IsSah46(string key)
        {
            return ContainsAny(key, "SAH-46", "SAH46", "RAH-46", "RAH46", "Chicane");
        }

        internal static bool IsRotorcraft(string key)
        {
            return IsUh90(key) || IsSah46(key);
        }

        internal static bool IsVl49(string key)
        {
            return ContainsAny(key, "VL-49", "VL49", "Tarantula", "Bird-Eating", "QuadVTOL");
        }

        internal static bool IsA19(string key)
        {
            return ContainsAny(key, "A-19", "A19", "NOTA-10", "NOTA10", "Brawler", "Warthog");
        }

        internal static bool IsAb4(string key)
        {
            return ContainsAny(key, "AB-4", "AB4", "Alkyon", "Alkyone");
        }

        internal static bool IsTa30(string key)
        {
            return ContainsAny(key, "T/A-30", "TA-30", "TA30", "T-A-30", "Compass", "trainer");
        }

        internal static bool HasFleetAfterburner(string key)
        {
            return IsAb4(key) || IsKr67(key) || IsFs12(key) || IsFs20(key);
        }

        internal static bool IsKnownFleet(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return IsFs12(key) || IsFs20(key) || IsSfb(key) || IsEw25(key)
                || IsKr67(key) || IsUh90(key) || IsSah46(key) || IsVl49(key)
                || IsA19(key) || IsAb4(key) || IsTa30(key) || IsVt7(key)
                || IsCi22(key);
        }

        internal static bool IsVt7(string key)
        {
            return ContainsAny(key, "VT-7", "VT7", "Vagrant");
        }

        internal static bool IsCi22(string key)
        {
            return ContainsAny(key, "CI-22", "CI22", "Cricket", "COIN");
        }

        internal static bool IsVanillaDefinition(AircraftDefinition def)
        {
            if (def == null)
                return false;
            return TextHasMarker(def.jsonKey)
                || TextHasMarker(def.code)
                || TextHasMarker(def.unitName)
                || TextHasMarker(def.name);
        }

        internal static bool IsCoinDefinition(AircraftDefinition def)
        {
            if (def == null)
                return false;
            string key = def.jsonKey != null ? def.jsonKey : string.Empty;
            string code = def.code != null ? def.code : string.Empty;
            string name = def.unitName != null ? def.unitName : string.Empty;
            string asset = def.name != null ? def.name : string.Empty;
            if (key.Equals("COIN", StringComparison.OrdinalIgnoreCase))
                return true;
            if (code.IndexOf("CI-22", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("CI22", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("CI-22", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (asset.IndexOf("CI-22", StringComparison.OrdinalIgnoreCase) >= 0
                || asset.IndexOf("COIN", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool IsXeDefinition(AircraftDefinition def)
        {
            if (def == null)
                return false;
            if (Plugin.AffectAllAircraft != null && Plugin.AffectAllAircraft.Value)
                return true;
            return IsVanillaDefinition(def);
        }

        internal static bool IsCoinAircraft(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            if (def != null && IsCoinDefinition(def))
                return true;
            string n = aircraft.name != null ? aircraft.name : string.Empty;
            return n.IndexOf("COIN", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("CI-22", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsXeAircraft(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            if (IsCoinAircraft(aircraft))
                return true;
            AircraftDefinition def = TryGetDefinition(aircraft);
            if (def != null)
                return IsXeDefinition(def);
            if (Plugin.AffectAllAircraft != null && Plugin.AffectAllAircraft.Value)
                return true;
            string n = aircraft.name != null ? aircraft.name : string.Empty;
            return TextHasMarker(n);
        }

        internal static bool IsStovlNozzle(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            string key = GetKey(aircraft);
            return IsEw25(key) || IsVt7(key) || IsFs20(key)
                || ContainsAny(key, "Vagrant", "Medusa");
        }

        /// <summary>CI-22 / AB-4 / VT-7 get half fuel burn.</summary>
        internal static bool WantsFuelEconomy(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            if (IsCoinAircraft(aircraft))
                return true;
            string key = GetKey(aircraft);
            return IsAb4(key) || IsVt7(key);
        }

        internal static float DefaultThrustMul(string key)
        {
            if (IsSfb(key))
                return 8f;
            if (IsAb4(key))
                return 6f;
            if (IsA19(key))
                return 6.2f;
            if (IsEw25(key))
                return 4.34f;
            if (IsUh90(key))
                return 4f;
            if (IsKr67(key))
                return 3.2f;
            return Plugin.PowerMultiplier != null ? Plugin.PowerMultiplier.Value : 2.7f;
        }

        internal static bool IsOnXe(Component c)
        {
            if (c == null || !Plugin.IsRuntimeInstance(c))
                return false;
            Aircraft ac = c.GetComponentInParent<Aircraft>();
            return ac != null && IsXeAircraft(ac);
        }

        internal static bool IsOnCoin(Component c)
        {
            if (c == null || !Plugin.IsRuntimeInstance(c))
                return false;
            Aircraft ac = c.GetComponentInParent<Aircraft>();
            return ac != null && IsCoinAircraft(ac);
        }
    }
}
