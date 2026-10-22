using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// Vanilla: each Hangar has a baked availableAircraft list; Airbase.AddHangar
    /// registers that hangar. Icarus: FindObjectsOfTypeAll Hangar, match Object.name
    /// to HangersName (hangar_med / revetment1 / shelter1 / Helipad), then append
    /// the imported AircraftDefinition to Hangar.availableAircraft.
    /// Icarus Dictionary.Add keeps only the first hangar per type (usually the
    /// prefab, before mission AddHangar). Replay Icarus's write onto every hangar
    /// with that prefab name, including Unity "name (1)" instances.
    /// </summary>
    internal static class IcarusHangarSpreadService
    {
        private static readonly FieldInfo AvailableAircraftField =
            AccessTools.Field(typeof(Hangar), "availableAircraft");
        private static readonly FieldInfo SelectionField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "aircraftSelection");

        private static readonly List<Record> Records = new List<Record>();
        private static readonly HashSet<int> SpreadHangars = new HashSet<int>();
        private static bool _icarusPatched;
        private static bool _logged;
        private static IDictionary _hangersName;
        private static bool _hangersNameTried;
        private static bool _detailsCaptured;
        private static bool _detailTypeTried;
        private static Type _aircraftDetailType;

        private sealed class Record
        {
            public AircraftDefinition Def;
            public string[] PrefabNames;
        }

        internal static void Register(AircraftDefinition def, string[] prefabNames)
        {
            if (def == null)
                return;
            Record rec = new Record();
            rec.Def = def;
            rec.PrefabNames = prefabNames != null && prefabNames.Length > 0
                ? prefabNames
                : DefaultLandPrefabs();
            Upsert(rec);
            SpreadHangars.Clear();
            SpreadToMatchingHangars(rec);
        }

        internal static void TryPatchIcarus(Harmony harmony)
        {
            if (_icarusPatched || harmony == null)
                return;
            _icarusPatched = true;
            Type importer = null;
            try { importer = Type.GetType("IcarusArmories.AssetBundleImporter, IcarusArmories"); }
            catch { importer = null; }
            if (importer == null)
                return;
            try
            {
                MethodInfo import = AccessTools.DeclaredMethod(importer, "ImportAircraft");
                if (import != null)
                    harmony.Patch(import, null, new HarmonyMethod(typeof(IcarusHangarSpreadService), "IcarusImportPostfix"));
                MethodInfo add = AccessTools.DeclaredMethod(importer, "AddAircraftToHangers");
                if (add != null)
                    harmony.Patch(add, new HarmonyMethod(typeof(IcarusHangarSpreadService), "IcarusAddPrefix"), null);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Icarus hangar patch: " + ex.Message);
            }
            CaptureFromDetails();
        }

        private static void IcarusImportPostfix(object __0)
        {
            Record rec = RecordFromDetail(__0);
            if (rec == null || rec.Def == null)
                return;
            IcarusAircraftRegistry.RegisterNetwork(rec.Def);
            Upsert(rec);
            SpreadHangars.Clear();
            SpreadToMatchingHangars(rec);
        }

        private static bool IcarusAddPrefix(AircraftDefinition __0, object __1)
        {
            if (__0 == null)
                return false;
            Record rec = new Record();
            rec.Def = __0;
            IcarusAircraftRegistry.RegisterNetwork(rec.Def);
            rec.PrefabNames = PrefabNamesFromHangars(__1);
            Upsert(rec);
            SpreadHangars.Clear();
            SpreadToMatchingHangars(rec);
            return false;
        }

        internal static void EnsureOnHangar(Hangar hangar)
        {
            if (hangar == null || AvailableAircraftField == null)
                return;
            if (MorayRuntime.IsPlayerPad(hangar))
                return;
            int id = hangar.GetInstanceID();
            if (SpreadHangars.Contains(id))
                return;
            if (Records.Count == 0)
                CaptureFromDetails();
            if (Records.Count == 0)
            {
                SpreadHangars.Add(id);
                return;
            }
            for (int i = 0; i < Records.Count; i++)
            {
                Record rec = Records[i];
                if (rec == null || rec.Def == null)
                    continue;
                if (!IcarusNameMatches(hangar, rec.PrefabNames))
                    continue;
                AppendToHangar(hangar, rec.Def);
            }
            SpreadHangars.Add(id);
        }

        internal static void MergeInto(List<AircraftDefinition> dest, Airbase airbase)
        {
            if (dest == null || airbase == null)
                return;
            List<Hangar> hangars = null;
            try { hangars = airbase.hangars; }
            catch { hangars = null; }
            if (hangars == null)
                return;
            for (int i = 0; i < hangars.Count; i++)
            {
                Hangar h = hangars[i];
                if (h == null)
                    continue;
                EnsureOnHangar(h);
                AircraftDefinition[] arr = AvailableAircraftField != null
                    ? AvailableAircraftField.GetValue(h) as AircraftDefinition[]
                    : null;
                if (arr == null)
                    continue;
                for (int a = 0; a < arr.Length; a++)
                {
                    if (arr[a] == null)
                        continue;
                    AircraftDefinition def = EncyclopediaDef(arr[a]);
                    if (ListContains(dest, def))
                        continue;
                    dest.Add(def);
                }
            }
        }

        internal static void InjectMenu(AircraftSelectionMenu menu, Airbase airbase)
        {
            if (menu == null || airbase == null || SelectionField == null)
                return;
            List<AircraftDefinition> sel = SelectionField.GetValue(menu) as List<AircraftDefinition>;
            if (sel == null)
            {
                sel = new List<AircraftDefinition>(8);
                SelectionField.SetValue(menu, sel);
            }
            MergeInto(sel, airbase);
        }

        internal static bool MenuContains(AircraftSelectionMenu menu, AircraftDefinition definition)
        {
            if (menu == null || definition == null || SelectionField == null)
                return false;
            List<AircraftDefinition> sel = SelectionField.GetValue(menu) as List<AircraftDefinition>;
            return ListContains(sel, definition);
        }

        internal static bool HangarListContains(Hangar hangar, AircraftDefinition definition)
        {
            if (hangar == null || definition == null || AvailableAircraftField == null)
                return false;
            AircraftDefinition[] arr = AvailableAircraftField.GetValue(hangar) as AircraftDefinition[];
            if (arr == null)
                return false;
            string key = definition.jsonKey;
            for (int i = 0; i < arr.Length; i++)
            {
                AircraftDefinition cur = arr[i];
                if (cur == null)
                    continue;
                if (object.ReferenceEquals(cur, definition))
                    return true;
                if (!string.IsNullOrEmpty(key) && string.Equals(cur.jsonKey, key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static bool IsRecorded(AircraftDefinition def)
        {
            if (def == null)
                return false;
            if (Records.Count == 0)
                CaptureFromDetails();
            string key = def.jsonKey;
            for (int i = 0; i < Records.Count; i++)
            {
                Record rec = Records[i];
                if (rec == null || rec.Def == null)
                    continue;
                if (object.ReferenceEquals(rec.Def, def))
                    return true;
                if (!string.IsNullOrEmpty(key) && string.Equals(rec.Def.jsonKey, key, StringComparison.Ordinal))
                    return true;
            }
            return IcarusAircraftRegistry.Contains(def);
        }

        private static void SpreadToMatchingHangars(Record rec)
        {
            if (rec == null || rec.Def == null)
                return;
            Hangar[] all = null;
            try { all = Resources.FindObjectsOfTypeAll<Hangar>(); }
            catch { all = null; }
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                Hangar h = all[i];
                if (h == null)
                    continue;
                if (IsRubble(h.name))
                    continue;
                if (!IcarusNameMatches(h, rec.PrefabNames))
                    continue;
                AppendToHangar(h, rec.Def);
            }
        }

        private static void AppendToHangar(Hangar hangar, AircraftDefinition def)
        {
            if (hangar == null || def == null || AvailableAircraftField == null)
                return;
            AircraftDefinition use = EncyclopediaDef(def);
            AircraftDefinition[] cur = AvailableAircraftField.GetValue(hangar) as AircraftDefinition[];
            if (ArrayContains(cur, use))
                return;
            int n = cur != null ? cur.Length : 0;
            AircraftDefinition[] next = new AircraftDefinition[n + 1];
            if (n > 0)
                Array.Copy(cur, next, n);
            next[n] = use;
            AvailableAircraftField.SetValue(hangar, next);
            if (!_logged && Plugin.Log != null)
            {
                _logged = true;
                string key = use.jsonKey != null ? use.jsonKey : use.name;
                Plugin.Log.LogInfo("Icarus hangar spread: " + key + " onto " + hangar.name);
            }
        }

        private static bool ArrayContains(AircraftDefinition[] arr, AircraftDefinition def)
        {
            if (arr == null || def == null)
                return false;
            string key = def.jsonKey;
            for (int i = 0; i < arr.Length; i++)
            {
                AircraftDefinition cur = arr[i];
                if (cur == null)
                    continue;
                if (object.ReferenceEquals(cur, def))
                    return true;
                if (!string.IsNullOrEmpty(key) && string.Equals(cur.jsonKey, key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Icarus CollectHangars: HangarTypeByPrefabName.ContainsKey(hangar.name).</summary>
        private static bool IcarusNameMatches(Hangar hangar, string[] prefabs)
        {
            if (hangar == null || prefabs == null || prefabs.Length == 0)
                return false;
            string name = hangar.name;
            if (string.IsNullOrEmpty(name) || IsRubble(name))
                return false;
            for (int i = 0; i < prefabs.Length; i++)
            {
                string p = prefabs[i];
                if (string.IsNullOrEmpty(p))
                    continue;
                if (string.Equals(name, p, StringComparison.Ordinal))
                    return true;
                if (name.Length > p.Length && name.StartsWith(p, StringComparison.Ordinal)
                    && (name[p.Length] == ' ' || name[p.Length] == '('))
                    return true;
            }
            return false;
        }

        private static bool IsRubble(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string n = name.ToLowerInvariant();
            return n.IndexOf("rubble", StringComparison.Ordinal) >= 0
                || n.IndexOf("destroy", StringComparison.Ordinal) >= 0
                || n.IndexOf("wreck", StringComparison.Ordinal) >= 0;
        }

        private static void Upsert(Record rec)
        {
            if (rec == null || rec.Def == null)
                return;
            rec.Def = EncyclopediaDef(rec.Def);
            if (rec.PrefabNames == null || rec.PrefabNames.Length == 0)
                rec.PrefabNames = DefaultLandPrefabs();
            string key = rec.Def.jsonKey;
            for (int i = 0; i < Records.Count; i++)
            {
                Record cur = Records[i];
                if (cur == null || cur.Def == null)
                    continue;
                if (object.ReferenceEquals(cur.Def, rec.Def)
                    || (!string.IsNullOrEmpty(key) && string.Equals(cur.Def.jsonKey, key, StringComparison.Ordinal)))
                {
                    Records[i] = rec;
                    return;
                }
            }
            Records.Add(rec);
        }

        private static Record RecordFromDetail(object detail)
        {
            if (detail == null)
                return null;
            Type t = detail.GetType();
            FieldInfo defField = t.GetField("aircraftDefinition", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo hangField = t.GetField("availableHangars", BindingFlags.Instance | BindingFlags.Public);
            if (defField == null || hangField == null)
                return null;
            AircraftDefinition def = defField.GetValue(detail) as AircraftDefinition;
            if (def == null)
                return null;
            Record rec = new Record();
            rec.Def = def;
            rec.PrefabNames = PrefabNamesFromHangars(hangField.GetValue(detail));
            return rec;
        }

        private static void CaptureFromDetails()
        {
            if (_detailsCaptured)
                return;
            _detailsCaptured = true;
            Type detailType = ResolveAircraftDetailType();
            if (detailType == null)
                return;
            UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(detailType);
            if (found == null)
                return;
            for (int i = 0; i < found.Length; i++)
            {
                Record rec = RecordFromDetail(found[i]);
                if (rec != null)
                {
                    IcarusAircraftRegistry.RegisterNetwork(rec.Def);
                    Upsert(rec);
                }
            }
        }

        private static string[] PrefabNamesFromHangars(object hangars)
        {
            IEnumerable e = hangars as IEnumerable;
            if (e == null)
                return DefaultLandPrefabs();
            IDictionary map = GetHangersName();
            List<string> names = new List<string>(4);
            foreach (object item in e)
            {
                if (item == null)
                    continue;
                string n = null;
                if (map != null)
                {
                    try
                    {
                        if (map.Contains(item))
                            n = map[item] as string;
                    }
                    catch { n = null; }
                }
                if (string.IsNullOrEmpty(n))
                {
                    int idx = 0;
                    try { idx = Convert.ToInt32(item); }
                    catch { continue; }
                    if (idx > 0 && idx < IcarusAircraftRegistry.PrefabByHangarType.Length)
                        n = IcarusAircraftRegistry.PrefabByHangarType[idx];
                }
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
            if (names.Count == 0)
                return DefaultLandPrefabs();
            return names.ToArray();
        }

        private static string[] DefaultLandPrefabs()
        {
            return new string[] { "hangar_med", "revetment1", "shelter1", "Helipad" };
        }

        private static Type ResolveAircraftDetailType()
        {
            if (_detailTypeTried)
                return _aircraftDetailType;
            _detailTypeTried = true;
            try
            {
                _aircraftDetailType = Type.GetType("IcarusArmories.AircraftDetail, IcarusArmories");
            }
            catch
            {
                _aircraftDetailType = null;
            }
            return _aircraftDetailType;
        }

        private static IDictionary GetHangersName()
        {
            if (_hangersNameTried)
                return _hangersName;
            _hangersNameTried = true;
            Type plugin = null;
            try { plugin = Type.GetType("IcarusArmories.Plugin, IcarusArmories"); }
            catch { plugin = null; }
            if (plugin == null)
                return null;
            FieldInfo f = plugin.GetField("HangersName", BindingFlags.Static | BindingFlags.Public);
            if (f == null)
                return null;
            _hangersName = f.GetValue(null) as IDictionary;
            return _hangersName;
        }

        private static AircraftDefinition EncyclopediaDef(AircraftDefinition def)
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

        internal static bool ListContains(List<AircraftDefinition> list, AircraftDefinition def)
        {
            if (list == null || def == null)
                return false;
            string key = def.jsonKey;
            for (int i = 0; i < list.Count; i++)
            {
                AircraftDefinition cur = list[i];
                if (cur == null)
                    continue;
                if (object.ReferenceEquals(cur, def))
                    return true;
                if (!string.IsNullOrEmpty(key) && string.Equals(cur.jsonKey, key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Airbase), "AddHangar")]
    internal static class Patch_Airbase_IcarusAddHangar
    {
        [HarmonyPostfix]
        private static void Postfix(Hangar hangar)
        {
            IcarusHangarSpreadService.EnsureOnHangar(hangar);
        }
    }

    [HarmonyPatch(typeof(Hangar), "GetAvailableAircraft")]
    internal static class Patch_Hangar_IcarusSpreadAvailable
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Hangar __instance, ref AircraftDefinition[] __result)
        {
            IcarusHangarSpreadService.EnsureOnHangar(__instance);
            if (__instance == null || HangarAircraftField == null)
                return;
            AircraftDefinition[] field = HangarAircraftField.GetValue(__instance) as AircraftDefinition[];
            if (field != null && field.Length > 0)
                __result = field;
        }

        private static readonly FieldInfo HangarAircraftField =
            AccessTools.Field(typeof(Hangar), "availableAircraft");
    }

    [HarmonyPatch(typeof(Hangar), "CanSpawnAircraft")]
    internal static class Patch_Hangar_IcarusSpreadCanSpawn
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static void Prefix(Hangar __instance)
        {
            IcarusHangarSpreadService.EnsureOnHangar(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Hangar __instance, AircraftDefinition definition, ref bool __result)
        {
            if (__result || definition == null || __instance == null)
                return;
            if (!IcarusHangarSpreadService.IsRecorded(definition))
                return;
            IcarusHangarSpreadService.EnsureOnHangar(__instance);
            if (IcarusHangarSpreadService.HangarListContains(__instance, definition))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Airbase), "GetAvailableAircraft")]
    internal static class Patch_Airbase_IcarusSpreadAvailable
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Airbase __instance, List<AircraftDefinition> __result)
        {
            if (__instance == null)
                return;
            try
            {
                if (__instance.disabled)
                    return;
            }
            catch { }
            IcarusHangarSpreadService.MergeInto(__result, __instance);
        }
    }

    [HarmonyPatch(typeof(AircraftSelectionMenu), "Refresh")]
    internal static class Patch_SelMenu_IcarusSpread
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AircraftSelectionMenu __instance, Airbase airbase)
        {
            IcarusHangarSpreadService.InjectMenu(__instance, airbase);
        }
    }

    [HarmonyPatch(typeof(AircraftSelectionMenu), "SpawnPreview")]
    internal static class Patch_SelMenu_IcarusPreviewDef
    {
        private static readonly FieldInfo SelectedTypeField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "selectedType");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AircraftSelectionMenu __instance)
        {
            if (__instance == null || SelectedTypeField == null)
                return;
            AircraftDefinition cur = SelectedTypeField.GetValue(__instance) as AircraftDefinition;
            IcarusAircraftRegistry.RegisterNetwork(cur);
        }
    }

    [HarmonyPatch(typeof(AircraftSelectionMenu), "Update")]
    internal static class Patch_SelMenu_IcarusFlyButton
    {
        private static readonly FieldInfo SelectedTypeField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "selectedType");
        private static readonly FieldInfo LocalPlayerField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "localPlayer");
        private static readonly FieldInfo FlyButtonField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "flyButton");
        private static readonly FieldInfo LoadoutUnaffordableField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "loadoutUnaffordable");
        private static readonly FieldInfo InsufficientWarheadsField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "insufficientWarheads");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AircraftSelectionMenu __instance)
        {
            if (__instance == null)
                return;
            Button fly = FlyButtonField != null
                ? FlyButtonField.GetValue(__instance) as Button
                : null;
            if (fly == null || fly.interactable)
                return;
            AircraftDefinition selected = SelectedTypeField != null
                ? SelectedTypeField.GetValue(__instance) as AircraftDefinition
                : null;
            Player player = LocalPlayerField != null
                ? LocalPlayerField.GetValue(__instance) as Player
                : null;
            if (selected == null || player == null)
                return;
            if (!player.OwnsAirframe(selected, true))
                return;
            bool blocked = false;
            try
            {
                Image unaff = LoadoutUnaffordableField != null
                    ? LoadoutUnaffordableField.GetValue(__instance) as Image
                    : null;
                Image warheads = InsufficientWarheadsField != null
                    ? InsufficientWarheadsField.GetValue(__instance) as Image
                    : null;
                if (unaff != null && unaff.enabled)
                    blocked = true;
                if (warheads != null && warheads.enabled)
                    blocked = true;
            }
            catch { }
            fly.interactable = !blocked;
        }
    }

    [HarmonyPatch(typeof(AircraftSelectionMenu), "CanFlyAircraft")]
    internal static class Patch_SelMenu_IcarusCanFly
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AircraftSelectionMenu __instance, AircraftDefinition definition, ref bool __result)
        {
            if (__result || definition == null || __instance == null)
                return;
            if (IcarusHangarSpreadService.MenuContains(__instance, definition))
            {
                __result = true;
                return;
            }
            if (!IcarusHangarSpreadService.IsRecorded(definition))
                return;
            Airbase airbase = null;
            FieldInfo f = AccessTools.Field(typeof(AircraftSelectionMenu), "airbase");
            if (f != null)
                airbase = f.GetValue(__instance) as Airbase;
            if (airbase == null)
                return;
            IcarusHangarSpreadService.InjectMenu(__instance, airbase);
            if (IcarusHangarSpreadService.MenuContains(__instance, definition))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Player), "OwnsAirframe")]
    internal static class Patch_Player_IcarusOwnsAirframe
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, AircraftDefinition aircraftDef, bool includeReserved, ref bool __result)
        {
            if (__result || __instance == null || aircraftDef == null)
                return;
            try
            {
                foreach (OwnedAirframe owned in __instance.OwnedAirframes)
                {
                    if (!IcarusAircraftRegistry.SameAirframe(owned.Definition, aircraftDef))
                        continue;
                    if (includeReserved || !owned.Reserved)
                    {
                        __result = true;
                        return;
                    }
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Player), "OwnedAirframeTypeCount")]
    internal static class Patch_Player_IcarusOwnedCount
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, AircraftDefinition aircraftDef, bool includeReserved, ref int __result)
        {
            if (__instance == null || aircraftDef == null)
                return;
            if (__result > 0
                && !IcarusAircraftRegistry.Contains(aircraftDef)
                && !IcarusHangarSpreadService.IsRecorded(aircraftDef))
                return;
            int n = 0;
            try
            {
                foreach (OwnedAirframe owned in __instance.OwnedAirframes)
                {
                    if (!IcarusAircraftRegistry.SameAirframe(owned.Definition, aircraftDef))
                        continue;
                    if (includeReserved || !owned.Reserved)
                        n++;
                }
            }
            catch { return; }
            if (n > __result)
                __result = n;
        }
    }

    [HarmonyPatch(typeof(Player), "FlyOwnedAirframe")]
    internal static class Patch_Player_IcarusFlyOwned
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance, ref AircraftDefinition airframe)
        {
            if (__instance == null || airframe == null)
                return;
            try
            {
                foreach (OwnedAirframe owned in __instance.OwnedAirframes)
                {
                    if (!IcarusAircraftRegistry.SameAirframe(owned.Definition, airframe))
                        continue;
                    if (owned.Definition != null)
                        airframe = owned.Definition;
                    return;
                }
            }
            catch { }
            airframe = IcarusAircraftRegistry.Resolve(airframe);
        }
    }

    [HarmonyPatch(typeof(AircraftInventoryMenu), "BuyAirframe")]
    internal static class Patch_InvMenu_IcarusBuy
    {
        [HarmonyPrefix]
        private static void Prefix(AircraftInventoryMenu __instance)
        {
            IcarusAircraftRegistry.EnsureScanned();
            IcarusAircraftRegistry.RegisterAllNetwork();
            if (__instance == null)
                return;
            FieldInfo f = AccessTools.Field(typeof(AircraftInventoryMenu), "selectedType");
            if (f == null)
                return;
            AircraftDefinition cur = f.GetValue(__instance) as AircraftDefinition;
            IcarusAircraftRegistry.RegisterNetwork(cur);
        }
    }

    [HarmonyPatch(typeof(Airbase), "CanSpawnAircraft")]
    internal static class Patch_Airbase_IcarusCanSpawn
    {
        [HarmonyPostfix]
        private static void Postfix(Airbase __instance, AircraftDefinition definition, ref bool __result)
        {
            if (__result || __instance == null || definition == null)
                return;
            if (!IcarusHangarSpreadService.IsRecorded(definition)
                && !IcarusAircraftRegistry.Contains(definition))
                return;
            List<Hangar> hangars = null;
            try { hangars = __instance.hangars; }
            catch { hangars = null; }
            if (hangars == null)
                return;
            for (int i = 0; i < hangars.Count; i++)
            {
                Hangar h = hangars[i];
                if (h == null)
                    continue;
                try
                {
                    if (h.Disabled)
                        continue;
                }
                catch { }
                IcarusHangarSpreadService.EnsureOnHangar(h);
                if (IcarusHangarSpreadService.HangarListContains(h, definition))
                {
                    __result = true;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Player), "CmdPurchaseAirframe")]
    internal static class Patch_Player_IcarusCmdPurchase
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AircraftDefinition aircraftDef)
        {
            IcarusAircraftRegistry.RegisterNetwork(aircraftDef);
        }
    }

    [HarmonyPatch]
    internal static class Patch_Player_IcarusUserCodePurchase
    {
        private static bool Prepare()
        {
            return FindUserCode() != null;
        }

        private static MethodBase TargetMethod()
        {
            return FindUserCode();
        }

        private static MethodBase FindUserCode()
        {
            try
            {
                MethodInfo[] methods = typeof(Player).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m != null && m.Name != null
                        && m.Name.IndexOf("UserCode_CmdPurchaseAirframe", StringComparison.Ordinal) >= 0)
                        return m;
                }
            }
            catch { }
            return null;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AircraftDefinition aircraftDef)
        {
            IcarusAircraftRegistry.RegisterNetwork(aircraftDef);
        }
    }

    [HarmonyPatch(typeof(Player), "CreditAirframe")]
    internal static class Patch_Player_IcarusCreditAirframe
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AircraftDefinition aircraftDef)
        {
            IcarusAircraftRegistry.RegisterNetwork(aircraftDef);
        }
    }

    [HarmonyPatch(typeof(DefinitionWriters), "WriteAircraftDefinition")]
    internal static class Patch_Writers_IcarusAircraftDef
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AircraftDefinition definition)
        {
            if (definition == null)
                return;
            if (!IcarusAircraftRegistry.Contains(definition)
                && !IcarusHangarSpreadService.IsRecorded(definition))
                return;
            IcarusAircraftRegistry.RegisterNetwork(definition);
        }
    }
}
