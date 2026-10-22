using System;
using System.Collections.Generic;
using System.Reflection;
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
            "SAH-46", "SAH46", "SAH-64", "SAH64", "Alkyon"
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
            return ContainsAny(key, "SAH-46", "SAH46", "SAH-64", "SAH64",
                "RAH-46", "RAH46", "RAH-64", "RAH64", "Chicane");
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
            if (string.IsNullOrEmpty(key))
                return false;
            // VT-7 is also a trainer. Never let the Compass jsonKey stem steal it.
            if (IsVt7(key))
                return false;
            if (ContainsAny(key, "T/A-30", "TA-30", "TA30", "T-A-30", "Compass"))
                return true;
            // Vanilla T/A-30 Compass jsonKey is exactly "trainer".
            return FirstTokenEquals(key, "trainer");
        }

        private static bool FirstTokenEquals(string key, string token)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(token))
                return false;
            int i = 0;
            while (i < key.Length && char.IsWhiteSpace(key[i]))
                i++;
            int n = 0;
            while (i + n < key.Length && !char.IsWhiteSpace(key[i + n]))
                n++;
            if (n != token.Length)
                return false;
            return string.Compare(key, i, token, 0, n, StringComparison.OrdinalIgnoreCase) == 0;
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

        private static readonly Dictionary<int, bool> BiaGoCache = new Dictionary<int, bool>(64);
        private static readonly Dictionary<int, bool> IcarusAcCache = new Dictionary<int, bool>(128);
        private static Type _biaMfdType;
        private static Type _biaRadarType;
        private static Type _biaAirframeType;
        private static FieldInfo _biaCompleteField;
        private static bool _biaTypesTried;
        private static int _localFrame = -1;
        private static Aircraft _localAc;

        /// <summary>One GameManager.GetLocalAircraft per rendered frame (shared by Harmony prefixes).</summary>
        internal static Aircraft CachedLocal()
        {
            int frame = Time.frameCount;
            if (frame == _localFrame)
                return _localAc;
            _localFrame = frame;
            _localAc = null;
            try { GameManager.GetLocalAircraft(out _localAc); }
            catch { _localAc = null; }
            return _localAc;
        }

        /// <summary>Drop FixedUpdate caches when an airframe is destroyed.</summary>
        internal static void ForgetAircraft(Aircraft aircraft)
        {
            if (aircraft == null)
                return;
            try
            {
                int id = aircraft.GetInstanceID();
                IcarusAcCache.Remove(id);
                GameObject go = aircraft.gameObject;
                if (go != null)
                    BiaGoCache.Remove(go.GetInstanceID());
            }
            catch { }
        }

        private static void EnsureBiaTypes()
        {
            if (_biaTypesTried)
                return;
            _biaTypesTried = true;
            try
            {
                _biaMfdType = Type.GetType("BIA.BIAMfd, BIA.Runtime");
                _biaRadarType = Type.GetType("BIA.BIARadar, BIA.Runtime");
                _biaAirframeType = Type.GetType("BIA.BIAAirframe, BIA.Runtime");
                if (_biaAirframeType != null)
                    _biaCompleteField = _biaAirframeType.GetField("completeAirframe",
                        BindingFlags.Instance | BindingFlags.Public);
            }
            catch { }
        }

        internal static Component FindBiaMfd(GameObject root)
        {
            if (root == null)
                return null;
            EnsureBiaTypes();
            if (_biaMfdType == null)
                return null;
            try { return root.GetComponentInChildren(_biaMfdType, true); }
            catch { return null; }
        }

        /// <summary>
        /// Official Blueprinter hangar J 35 Draken (jsonKey SaabJ35). Not the BIA clone.
        /// </summary>
        internal static bool IsOfficialHangarDraken(string key)
        {
            if (string.IsNullOrEmpty(key) || IsDrakenBiaClone(key))
                return false;
            return string.Equals(key, "SaabJ35", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "J-35", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Icarus .baas copy with BIAMfd (jsonKey SaabJ35BIA).</summary>
        internal static bool IsDrakenBiaClone(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return string.Equals(key, "SaabJ35BIA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "J-35B", StringComparison.OrdinalIgnoreCase)
                || ContainsAny(key, "J35BIA", "J-35BIA");
        }

        /// <summary>
        /// Finished sit-on-wheels BIA airframe (BIAAirframe.completeAirframe).
        /// Cube templates omit the component. Official .nobp SaabJ35 is not BIA.
        /// </summary>
        internal static bool IsCompleteBiaAirframe(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            GameObject go = null;
            try { go = aircraft.gameObject; }
            catch { }
            if (IsCompleteBiaAirframe(go))
                return true;
            return IsDrakenBiaClone(GetKey(aircraft));
        }

        internal static bool IsCompleteBiaAirframe(GameObject root)
        {
            if (root == null)
                return false;
            EnsureBiaTypes();
            if (_biaAirframeType == null)
                return false;
            try
            {
                Component c = root.GetComponent(_biaAirframeType);
                if (c == null)
                    c = root.GetComponentInChildren(_biaAirframeType, true);
                if (c == null)
                    return false;
                if (_biaCompleteField == null)
                    return true;
                object v = _biaCompleteField.GetValue(c);
                if (v is bool)
                    return (bool)v;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>BIA avionics on a live airframe (BIAMfd / BIARadar). Does not need Icarus.</summary>
        internal static bool HasBiaAvionics(GameObject root)
        {
            if (root == null)
                return false;
            int id = 0;
            try { id = root.GetInstanceID(); }
            catch { return false; }
            bool cached;
            if (BiaGoCache.TryGetValue(id, out cached))
                return cached;
            cached = ScanBiaAvionics(root);
            BiaGoCache[id] = cached;
            return cached;
        }

        private static bool ScanBiaAvionics(GameObject root)
        {
            EnsureBiaTypes();
            try
            {
                if (_biaMfdType != null && root.GetComponentInChildren(_biaMfdType, true) != null)
                    return true;
                if (_biaRadarType != null && root.GetComponentInChildren(_biaRadarType, true) != null)
                    return true;
            }
            catch { }
            if (_biaMfdType != null || _biaRadarType != null)
                return false;
            // BIA.Runtime not loaded — one hierarchy walk, then cached.
            MonoBehaviour[] all = null;
            try { all = root.GetComponentsInChildren<MonoBehaviour>(true); }
            catch { return false; }
            if (all == null)
                return false;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                string n = all[i].GetType().Name;
                if (n == "BIAMfd" || n == "BIARadar" || n == "BIACountermeasures" || n == "BIAAirbrake")
                    return true;
            }
            return false;
        }

        internal static bool IsBiaAircraft(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            GameObject go = null;
            try { go = aircraft.gameObject; }
            catch { }
            if (HasBiaAvionics(go))
                return true;
            AircraftDefinition def = TryGetDefinition(aircraft);
            if (def != null && HasBiaAvionics(def.unitPrefab))
                return true;
            return IsDrakenBiaClone(GetKey(aircraft));
        }

        /// <summary>Cube Wyvern / XE J-35 only. Official SaabJ35.nobp is not this.</summary>
        internal static bool IsJ35WyvernKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (IsOfficialHangarDraken(key) || IsDrakenBiaClone(key))
                return false;
            if (string.Equals(key, "J35XE_Wyvern", StringComparison.Ordinal)
                || string.Equals(key, "J35_Draken", StringComparison.Ordinal))
                return true;
            if (ContainsAny(key, "J-35XE", "J35XE"))
                return true;
            return ContainsAny(key, "J-35", "J35")
                && ContainsAny(key, "Draken", "Wyvern", "飞龙");
        }

        /// <summary>
        /// Wheel-plane sit. Official hangar Draken + BIA clone. Cube Wyvern snaps.
        /// </summary>
        internal static bool IsSaabJ35Sit(string key)
        {
            return IsOfficialHangarDraken(key) || IsDrakenBiaClone(key);
        }

        /// <summary>Icarus/BIA airframe that is not a vanilla XE reskin.</summary>
        internal static bool IsIcarusOnlyDefinition(AircraftDefinition def)
        {
            if (def == null || !IsIcarusDefinition(def))
                return false;
            return !IsVanillaDefinition(def);
        }

        /// <summary>BIA airframe. Icarus AircraftDetail is only a hangar loader, not identity.</summary>
        internal static bool IsIcarusDefinition(AircraftDefinition def)
        {
            if (def == null)
                return false;
            if (def.unitPrefab != null && HasBiaAvionics(def.unitPrefab))
                return true;
            if (IsOfficialHangarDraken(def.jsonKey))
                return false;
            if (IsJ35WyvernKey(def.jsonKey) || IsDrakenBiaClone(def.jsonKey))
                return true;
            return IcarusAircraftRegistry.Contains(def);
        }

        internal static bool IsIcarusAircraft(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            int id = 0;
            try { id = aircraft.GetInstanceID(); }
            catch { return false; }
            bool cached;
            if (IcarusAcCache.TryGetValue(id, out cached))
                return cached;
            cached = false;
            if (IsBiaAircraft(aircraft))
                cached = true;
            else
            {
                AircraftDefinition def = TryGetDefinition(aircraft);
                if (def != null && IsIcarusDefinition(def))
                    cached = true;
                else
                {
                    string key = GetKey(aircraft);
                    cached = IcarusAircraftRegistry.ContainsKey(key);
                }
            }
            IcarusAcCache[id] = cached;
            return cached;
        }

        /// <summary>Icarus/BIA plane that is not a vanilla fleet reskin.</summary>
        internal static bool IsIcarusOnly(Aircraft aircraft)
        {
            if (!IsIcarusAircraft(aircraft))
                return false;
            return !IsKnownFleet(GetKey(aircraft));
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
            // Icarus / BIA hangar jets are not the XE fleet. XE FBW/thrust
            // rewrite fights the Draken Delta SAS and feels like reversed sticks.
            if (IsIcarusOnly(aircraft) || IsCompleteBiaAirframe(aircraft)
                || IsDrakenBiaClone(GetKey(aircraft))
                || IsOfficialHangarDraken(GetKey(aircraft)))
                return false;
            if (IsCoinAircraft(aircraft))
                return true;
            AircraftDefinition def = TryGetDefinition(aircraft);
            if (def != null)
            {
                if (IsXeDefinition(def))
                    return true;
            }
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
            if (IsVl49(key))
                return 6.5f;
            if (IsKr67(key))
                return 3.2f;
            if (!IsKnownFleet(key)
                && (IsJ35WyvernKey(key) || IcarusAircraftRegistry.ContainsKey(key)))
                return 1f;
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
