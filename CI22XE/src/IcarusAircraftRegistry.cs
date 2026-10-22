using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// BIA / Icarus .baas aircraft loaded as AircraftDetail. Hangar spread and
    /// Oritasy identity both use this list. Not vanilla XE branding.
    /// </summary>
    internal static class IcarusAircraftRegistry
    {
        internal static readonly string[] PrefabByHangarType = new string[]
        {
            "",
            "revetment1",
            "Helipad",
            "hangar_med",
            "shelter1",
            "Hull_hangarFloor",
            "hangar_F",
            "hangar_R1",
            "hangar_R2",
            "hangar_R3",
            "hangar_F",
            "hangar_M",
            "hangar_R1",
            "hangar_R2",
            "hangar_R3"
        };

        private static readonly string[] DefaultLandPrefabs = new string[]
        {
            "hangar_med",
            "revetment1",
            "shelter1",
            "Helipad"
        };

        internal sealed class Entry
        {
            public AircraftDefinition Def;
            public string[] PrefabNames;
        }

        private static readonly List<Entry> EntriesInternal = new List<Entry>();
        private static readonly HashSet<int> Ids = new HashSet<int>();
        private static readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);
        private static int _scannedFrame = -1;
        private static float _nextScanAt;
        private static bool _scanReady;
        private static int _foundDetails;
        private static Type _detailType;
        private static bool _detailTypeTried;
        private static FieldInfo _defField;
        private static FieldInfo _hangarsField;

        internal static List<Entry> Entries
        {
            get
            {
                EnsureScanned();
                return EntriesInternal;
            }
        }

        internal static bool Contains(AircraftDefinition def)
        {
            if (def == null)
                return false;
            EnsureScanned();
            if (Ids.Contains(def.GetInstanceID()))
                return true;
            string key = def.jsonKey;
            return !string.IsNullOrEmpty(key) && Keys.Contains(key);
        }

        internal static bool ContainsKey(string jsonKey)
        {
            if (string.IsNullOrEmpty(jsonKey))
                return false;
            EnsureScanned();
            return Keys.Contains(jsonKey);
        }

        internal static AircraftDefinition EncyclopediaInstance(AircraftDefinition def)
        {
            if (def == null)
                return null;
            try
            {
                Encyclopedia enc = Encyclopedia.i;
                if (enc == null || enc.aircraft == null)
                    return def;
                string key = def.jsonKey;
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition cur = enc.aircraft[i];
                    if (cur == null)
                        continue;
                    if (object.ReferenceEquals(cur, def))
                        return cur;
                    if (!string.IsNullOrEmpty(key)
                        && string.Equals(cur.jsonKey, key, StringComparison.Ordinal))
                        return cur;
                }
            }
            catch { }
            return def;
        }

        internal static string[] HangarsFor(AircraftDefinition def)
        {
            if (def == null)
                return null;
            EnsureScanned();
            string key = def.jsonKey;
            for (int i = 0; i < EntriesInternal.Count; i++)
            {
                Entry ent = EntriesInternal[i];
                if (ent == null || ent.Def == null)
                    continue;
                if (object.ReferenceEquals(ent.Def, def))
                    return ent.PrefabNames;
                if (!string.IsNullOrEmpty(key)
                    && string.Equals(ent.Def.jsonKey, key, StringComparison.Ordinal))
                    return ent.PrefabNames;
            }
            return null;
        }

        internal static void AddDefinition(AircraftDefinition def)
        {
            if (def == null)
                return;
            Ids.Add(def.GetInstanceID());
            if (!string.IsNullOrEmpty(def.jsonKey))
                Keys.Add(def.jsonKey);
            for (int i = 0; i < EntriesInternal.Count; i++)
            {
                Entry cur = EntriesInternal[i];
                if (cur == null || cur.Def == null)
                    continue;
                if (object.ReferenceEquals(cur.Def, def))
                    return;
                if (!string.IsNullOrEmpty(def.jsonKey)
                    && string.Equals(cur.Def.jsonKey, def.jsonKey, StringComparison.Ordinal))
                {
                    cur.Def = def;
                    return;
                }
            }
            Entry ent = new Entry();
            ent.Def = def;
            ent.PrefabNames = DefaultLandPrefabs;
            EntriesInternal.Add(ent);
        }

        internal static void MarkDirty()
        {
            _scanReady = false;
            _nextScanAt = 0f;
        }

        internal static void EnsureScanned()
        {
            if (_scanReady)
                return;
            _nextScanAt = Time.unscaledTime + 30f;
            _scannedFrame = Time.frameCount;
            if (!EnsureDetailType())
            {
                RemapEncyclopediaInstances();
                RegisterAllNetwork();
                _scanReady = true;
                return;
            }
            _foundDetails = CountDetails();
            ScanDetails();
            RemapEncyclopediaInstances();
            RegisterAllNetwork();
            _scanReady = true;
        }

        internal static AircraftDefinition Resolve(AircraftDefinition def)
        {
            AircraftDefinition enc = EncyclopediaInstance(def);
            RegisterNetwork(enc != null ? enc : def);
            return enc != null ? enc : def;
        }

        internal static bool SameAirframe(AircraftDefinition a, AircraftDefinition b)
        {
            if (a == null || b == null)
                return false;
            if (object.ReferenceEquals(a, b))
                return true;
            try
            {
                if (a == b)
                    return true;
            }
            catch { }
            string ka = a.jsonKey;
            string kb = b.jsonKey;
            if (!string.IsNullOrEmpty(ka) && string.Equals(ka, kb, StringComparison.Ordinal))
                return true;
            try
            {
                if (a.unitPrefab != null && b.unitPrefab != null && a.unitPrefab == b.unitPrefab)
                    return true;
            }
            catch { }
            return false;
        }

        internal static void RegisterAllNetwork()
        {
            for (int i = 0; i < EntriesInternal.Count; i++)
            {
                Entry ent = EntriesInternal[i];
                if (ent != null)
                    RegisterNetwork(ent.Def);
            }
            try
            {
                Encyclopedia enc = Encyclopedia.i;
                if (enc == null || enc.aircraft == null)
                    return;
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition def = enc.aircraft[i];
                    if (def == null || string.IsNullOrEmpty(def.jsonKey))
                        continue;
                    if (Keys.Contains(def.jsonKey))
                        RegisterNetwork(def);
                }
            }
            catch { }
        }

        internal static void RegisterNetwork(AircraftDefinition def)
        {
            if (def == null)
                return;
            BaasNetworkFixer.EnsureDefinition(def);
            try
            {
                if (!string.IsNullOrEmpty(def.jsonKey) && Encyclopedia.Lookup != null)
                    Encyclopedia.Lookup[def.jsonKey] = def;
            }
            catch { }
            Encyclopedia enc = null;
            try { enc = Encyclopedia.i; }
            catch { enc = null; }
            if (enc == null || enc.IndexLookup == null)
                return;
            INetworkDefinition nd = def;
            try
            {
                int? existing = nd.LookupIndex;
                if (existing.HasValue
                    && existing.Value >= 0
                    && existing.Value < enc.IndexLookup.Count
                    && object.ReferenceEquals(enc.IndexLookup[existing.Value], nd))
                    return;
                int idx = enc.IndexLookup.IndexOf(nd);
                if (idx >= 0)
                {
                    nd.LookupIndex = idx;
                    return;
                }
                enc.IndexLookup.Add(nd);
                nd.LookupIndex = enc.IndexLookup.Count - 1;
            }
            catch { }
        }

        private static Type FindDetailType()
        {
            try
            {
                return Type.GetType("IcarusArmories.AircraftDetail, IcarusArmories");
            }
            catch
            {
                return null;
            }
        }

        private static bool EnsureDetailType()
        {
            if (_detailType != null)
                return true;
            if (_detailTypeTried)
                return false;
            _detailTypeTried = true;
            _detailType = FindDetailType();
            if (_detailType == null)
                return false;
            _defField = _detailType.GetField("aircraftDefinition", BindingFlags.Instance | BindingFlags.Public);
            _hangarsField = _detailType.GetField("availableHangars", BindingFlags.Instance | BindingFlags.Public);
            if (_defField == null || _hangarsField == null)
            {
                _detailType = null;
                return false;
            }
            return true;
        }

        private static int CountDetails()
        {
            if (!EnsureDetailType())
                return 0;
            try
            {
                UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(_detailType);
                return found != null ? found.Length : 0;
            }
            catch { return 0; }
        }

        private static void ScanDetails()
        {
            if (!EnsureDetailType())
                return;

            UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(_detailType);
            if (found == null || found.Length == 0)
                return;
            EntriesInternal.Clear();
            Ids.Clear();
            Keys.Clear();
            for (int i = 0; i < found.Length; i++)
            {
                UnityEngine.Object obj = found[i];
                if (obj == null)
                    continue;
                AircraftDefinition def = ReadDefinition(obj);
                if (def == null)
                    continue;
                object hangars = _hangarsField.GetValue(obj);
                string[] names = PrefabNamesFromHangars(hangars);
                if (names == null || names.Length == 0)
                    names = DefaultLandPrefabs;
                Entry ent = new Entry();
                ent.Def = def;
                ent.PrefabNames = names;
                EntriesInternal.Add(ent);
                Ids.Add(def.GetInstanceID());
                if (!string.IsNullOrEmpty(def.jsonKey))
                    Keys.Add(def.jsonKey);
            }
        }

        private static void RemapEncyclopediaInstances()
        {
            for (int i = 0; i < EntriesInternal.Count; i++)
            {
                Entry ent = EntriesInternal[i];
                if (ent == null || ent.Def == null)
                    continue;
                AircraftDefinition enc = EncyclopediaInstance(ent.Def);
                if (enc == null || object.ReferenceEquals(enc, ent.Def))
                    continue;
                ent.Def = enc;
                Ids.Add(enc.GetInstanceID());
                if (!string.IsNullOrEmpty(enc.jsonKey))
                    Keys.Add(enc.jsonKey);
            }
        }

        private static AircraftDefinition ReadDefinition(UnityEngine.Object detail)
        {
            object raw = null;
            try { raw = _defField.GetValue(detail); }
            catch { raw = null; }
            AircraftDefinition def = raw as AircraftDefinition;
            if (def != null)
                return def;
            UnityEngine.Object obj = raw as UnityEngine.Object;
            def = obj as AircraftDefinition;
            if (def != null)
                return def;
            return FindEncyclopediaByDetailName(detail);
        }

        private static AircraftDefinition FindEncyclopediaByDetailName(UnityEngine.Object detail)
        {
            if (detail == null)
                return null;
            string name = detail.name;
            if (string.IsNullOrEmpty(name))
                return null;
            if (name.EndsWith("_detail", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 7);
            try
            {
                Encyclopedia enc = Encyclopedia.i;
                if (enc == null || enc.aircraft == null)
                    return null;
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition cur = enc.aircraft[i];
                    if (cur == null)
                        continue;
                    if (string.Equals(cur.jsonKey, name, StringComparison.Ordinal))
                        return cur;
                    if (string.Equals(cur.name, name, StringComparison.OrdinalIgnoreCase))
                        return cur;
                }
            }
            catch { }
            return null;
        }

        private static string[] PrefabNamesFromHangars(object hangars)
        {
            IEnumerable e = hangars as IEnumerable;
            if (e == null)
                return null;
            List<string> names = new List<string>(4);
            foreach (object item in e)
            {
                if (item == null)
                    continue;
                int idx = 0;
                try { idx = Convert.ToInt32(item); }
                catch { continue; }
                if (idx <= 0 || idx >= PrefabByHangarType.Length)
                    continue;
                string n = PrefabByHangarType[idx];
                if (string.IsNullOrEmpty(n))
                    continue;
                bool dup = false;
                for (int i = 0; i < names.Count; i++)
                {
                    if (string.Equals(names[i], n, StringComparison.OrdinalIgnoreCase))
                    {
                        dup = true;
                        break;
                    }
                }
                if (!dup)
                    names.Add(n);
            }
            return names.ToArray();
        }
    }
}
