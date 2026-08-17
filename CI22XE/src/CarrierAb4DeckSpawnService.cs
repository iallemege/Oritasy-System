using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Carrier deck hangars (non-elevator) can spawn AB-4. Vanilla only lists
    /// it on elevator pads, so selecting AB-4 always came up the lift.
    /// Hyperion hangar_F is a helo lift with elevator=false — never inject or
    /// spawn AB-4 there.
    /// </summary>
    internal static class CarrierAb4DeckSpawnService
    {
        private static readonly FieldInfo AvailableAircraftField =
            AccessTools.Field(typeof(Hangar), "availableAircraft");
        private static readonly FieldInfo ElevatorField =
            AccessTools.Field(typeof(Hangar), "elevator");
        private static readonly FieldInfo WaitForOpenField =
            AccessTools.Field(typeof(Hangar), "waitForOpenBeforeSpawn");
        private static readonly FieldInfo DoorsField =
            AccessTools.Field(typeof(Hangar), "doors");
        private static readonly HashSet<int> Injected = new HashSet<int>();
        private static AircraftDefinition _ab4;

        internal static void EnsureDeckAb4(Hangar hangar)
        {
            if (hangar == null || AvailableAircraftField == null)
                return;
            if (!Plugin.IsRuntimeInstance(hangar))
                return;
            int id = hangar.GetInstanceID();
            if (Injected.Contains(id))
                return;
            if (IsHyperionHeloElevator(hangar))
            {
                StripAb4(hangar);
                Injected.Add(id);
                return;
            }
            if (IsElevator(hangar) || !IsCarrierHangar(hangar))
            {
                Injected.Add(id);
                return;
            }

            AircraftDefinition[] list = null;
            try { list = AvailableAircraftField.GetValue(hangar) as AircraftDefinition[]; }
            catch { list = null; }
            if (list == null)
            {
                Injected.Add(id);
                return;
            }
            if (ContainsAb4(list))
            {
                Injected.Add(id);
                return;
            }

            AircraftDefinition ab4 = FindAb4();
            if (ab4 == null)
                return;

            AircraftDefinition[] next = new AircraftDefinition[list.Length + 1];
            Array.Copy(list, next, list.Length);
            next[list.Length] = ab4;
            try { AvailableAircraftField.SetValue(hangar, next); }
            catch { return; }
            Injected.Add(id);
        }

        internal static bool IsAb4Definition(AircraftDefinition def)
        {
            if (def == null)
                return false;
            return AircraftIdentity.IsAb4(def.jsonKey)
                || AircraftIdentity.IsAb4(def.code)
                || AircraftIdentity.IsAb4(def.unitName)
                || AircraftIdentity.IsAb4(def.name);
        }

        internal static bool IsElevator(Hangar hangar)
        {
            if (hangar == null || ElevatorField == null)
                return false;
            try { return (bool)ElevatorField.GetValue(hangar); }
            catch { return false; }
        }

        internal static bool IsCarrierHangar(Hangar hangar)
        {
            if (hangar == null)
                return false;
            try
            {
                Unit u = hangar.attachedUnit;
                Ship ship = u as Ship;
                if (AirbaseLocator.IsCarrierShip(ship))
                    return true;
            }
            catch { }

            try
            {
                Airbase ab = hangar.GetComponentInParent<Airbase>();
                if (AirbaseLocator.IsCarrierAirbase(ab))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsHyperionHangar(Hangar hangar)
        {
            if (hangar == null)
                return false;
            try
            {
                Unit u = hangar.attachedUnit;
                Ship ship = u as Ship;
                if (AirbaseLocator.IsHyperionShip(ship))
                    return true;
            }
            catch { }

            try
            {
                Airbase ab = hangar.GetComponentInParent<Airbase>();
                if (AirbaseLocator.IsHyperionAirbase(ab))
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Hyperion hangar_F: lift doors, UH-90/SAH-46 only, elevator flag is off.
        /// </summary>
        internal static bool IsHyperionHeloElevator(Hangar hangar)
        {
            if (hangar == null || !IsHyperionHangar(hangar))
                return false;
            if (!HasLift(hangar))
                return false;
            if (!HangarHasRotorcraft(hangar))
                return false;
            if (HangarHasJet(hangar))
                return false;
            return true;
        }

        internal static bool HasLift(Hangar hangar)
        {
            if (hangar == null)
                return false;
            if (IsElevator(hangar))
                return true;
            if (WaitForOpenField != null)
            {
                try
                {
                    if ((bool)WaitForOpenField.GetValue(hangar))
                        return true;
                }
                catch { }
            }
            if (DoorsField != null)
            {
                try
                {
                    Array doors = DoorsField.GetValue(hangar) as Array;
                    if (doors != null && doors.Length > 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

        internal static AircraftDefinition[] WithoutAb4(AircraftDefinition[] list)
        {
            if (list == null || list.Length == 0)
                return list;
            int keep = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (!IsAb4Definition(list[i]))
                    keep++;
            }
            if (keep == list.Length)
                return list;
            AircraftDefinition[] next = new AircraftDefinition[keep];
            int w = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (IsAb4Definition(list[i]))
                    continue;
                next[w] = list[i];
                w++;
            }
            return next;
        }

        private static void StripAb4(Hangar hangar)
        {
            if (hangar == null || AvailableAircraftField == null)
                return;
            AircraftDefinition[] list = null;
            try { list = AvailableAircraftField.GetValue(hangar) as AircraftDefinition[]; }
            catch { list = null; }
            AircraftDefinition[] next = WithoutAb4(list);
            if (next == list)
                return;
            try { AvailableAircraftField.SetValue(hangar, next); }
            catch { }
        }

        private static bool HangarHasRotorcraft(Hangar hangar)
        {
            AircraftDefinition[] list = ReadAvailable(hangar);
            if (list == null)
                return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (IsRotorcraftDefinition(list[i]))
                    return true;
            }
            return false;
        }

        private static bool HangarHasJet(Hangar hangar)
        {
            AircraftDefinition[] list = ReadAvailable(hangar);
            if (list == null)
                return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (IsJetDefinition(list[i]))
                    return true;
            }
            return false;
        }

        private static AircraftDefinition[] ReadAvailable(Hangar hangar)
        {
            if (hangar == null || AvailableAircraftField == null)
                return null;
            try { return AvailableAircraftField.GetValue(hangar) as AircraftDefinition[]; }
            catch { return null; }
        }

        private static bool IsRotorcraftDefinition(AircraftDefinition def)
        {
            if (def == null)
                return false;
            return AircraftIdentity.IsUh90(def.jsonKey)
                || AircraftIdentity.IsUh90(def.code)
                || AircraftIdentity.IsUh90(def.unitName)
                || AircraftIdentity.IsUh90(def.name)
                || AircraftIdentity.IsSah46(def.jsonKey)
                || AircraftIdentity.IsSah46(def.code)
                || AircraftIdentity.IsSah46(def.unitName)
                || AircraftIdentity.IsSah46(def.name);
        }

        private static bool IsJetDefinition(AircraftDefinition def)
        {
            if (def == null || IsAb4Definition(def))
                return false;
            return IsJetKey(def.jsonKey)
                || IsJetKey(def.code)
                || IsJetKey(def.unitName)
                || IsJetKey(def.name);
        }

        private static bool IsJetKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return AircraftIdentity.IsCi22(key)
                || AircraftIdentity.IsTa30(key)
                || AircraftIdentity.IsFs12(key)
                || AircraftIdentity.IsFs20(key)
                || AircraftIdentity.IsSfb(key)
                || AircraftIdentity.IsEw25(key)
                || AircraftIdentity.IsKr67(key)
                || AircraftIdentity.IsA19(key);
        }

        private static bool ContainsAb4(AircraftDefinition[] list)
        {
            if (list == null)
                return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (IsAb4Definition(list[i]))
                    return true;
            }
            return false;
        }

        private static AircraftDefinition FindAb4()
        {
            if (_ab4 != null)
                return _ab4;
            try
            {
                Encyclopedia enc = Encyclopedia.i;
                if (enc != null && enc.aircraft != null)
                {
                    for (int i = 0; i < enc.aircraft.Count; i++)
                    {
                        AircraftDefinition d = enc.aircraft[i];
                        if (IsAb4Definition(d))
                        {
                            _ab4 = d;
                            return _ab4;
                        }
                    }
                }
            }
            catch { }

            try
            {
                AircraftDefinition[] all = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (IsAb4Definition(all[i]))
                        {
                            _ab4 = all[i];
                            return _ab4;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    [HarmonyPatch(typeof(Hangar), "GetAvailableAircraft")]
    internal static class Patch_Hangar_Ab4DeckAvailable
    {
        [HarmonyPrefix]
        private static void Prefix(Hangar __instance)
        {
            CarrierAb4DeckSpawnService.EnsureDeckAb4(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(Hangar __instance, ref AircraftDefinition[] __result)
        {
            if (!CarrierAb4DeckSpawnService.IsHyperionHeloElevator(__instance))
                return;
            __result = CarrierAb4DeckSpawnService.WithoutAb4(__result);
        }
    }

    [HarmonyPatch(typeof(Hangar), "CanSpawnAircraft")]
    internal static class Patch_Hangar_Ab4DeckCanSpawn
    {
        [HarmonyPrefix]
        private static void Prefix(Hangar __instance)
        {
            CarrierAb4DeckSpawnService.EnsureDeckAb4(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(Hangar __instance, AircraftDefinition definition, ref bool __result)
        {
            if (!__result)
                return;
            if (!CarrierAb4DeckSpawnService.IsAb4Definition(definition))
                return;
            if (CarrierAb4DeckSpawnService.IsHyperionHeloElevator(__instance))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Airbase), "TrySpawnAircraft")]
    internal static class Patch_Airbase_Ab4PreferDeck
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Airbase __instance,
            Player player,
            AircraftDefinition definition,
            LiveryKey livery,
            Loadout loadout,
            float fuelLevel,
            ref Airbase.TrySpawnResult __result)
        {
            if (__instance == null || definition == null)
                return true;
            if (!CarrierAb4DeckSpawnService.IsAb4Definition(definition))
                return true;
            if (!AirbaseLocator.IsCarrierAirbase(__instance))
                return true;

            List<Hangar> hangars = null;
            try { hangars = __instance.hangars; }
            catch { hangars = null; }
            if (hangars == null)
                return true;

            if (TrySpawnFiltered(hangars, player, definition, livery, loadout, fuelLevel, true, ref __result))
                return false;
            if (TrySpawnFiltered(hangars, player, definition, livery, loadout, fuelLevel, false, ref __result))
                return false;
            return true;
        }

        private static bool TrySpawnFiltered(
            List<Hangar> hangars,
            Player player,
            AircraftDefinition definition,
            LiveryKey livery,
            Loadout loadout,
            float fuelLevel,
            bool openDeckOnly,
            ref Airbase.TrySpawnResult __result)
        {
            for (int i = 0; i < hangars.Count; i++)
            {
                Hangar h = hangars[i];
                if (h == null)
                    continue;
                if (CarrierAb4DeckSpawnService.IsHyperionHeloElevator(h))
                    continue;
                if (CarrierAb4DeckSpawnService.IsElevator(h))
                    continue;
                if (openDeckOnly && CarrierAb4DeckSpawnService.HasLift(h))
                    continue;
                CarrierAb4DeckSpawnService.EnsureDeckAb4(h);
                Airbase.TrySpawnResult result = default(Airbase.TrySpawnResult);
                try { result = h.TrySpawnAircraft(player, definition, livery, loadout, fuelLevel); }
                catch { continue; }
                if (result.Allowed)
                {
                    __result = result;
                    return true;
                }
            }
            return false;
        }
    }
}
