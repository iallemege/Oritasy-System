using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Strip vanilla building pierceArmor / armorTier so modular omni-6 is the only plate.
    /// Covers Unit buildings (hangars, SAM, factories) and MapBuilding scenery.
    /// </summary>
    internal static class BuildingResistStripService
    {
        internal static void StripArmor(ArmorProperties ap)
        {
            if (ap == null)
                return;
            ap.pierceArmor = 0f;
            ap.blastArmor = 0f;
            ap.fireArmor = 0f;
            ap.pierceTolerance = 1f;
            ap.blastTolerance = 1f;
            ap.fireTolerance = 1f;
            ap.overpressureLimit = 0f;
        }

        internal static void StripParts(GameObject go)
        {
            if (go == null)
                return;
            UnitPart[] parts = go.GetComponentsInChildren<UnitPart>(true);
            if (parts == null)
                return;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null)
                    continue;
                StripArmor(parts[i].GetArmorProperties());
            }
        }

        internal static void StripUnit(Unit u)
        {
            if (u == null)
                return;
            if (u.definition != null)
                u.definition.armorTier = 0f;
            StripParts(u.gameObject);
        }

        internal static void StripBuilding(Building b)
        {
            StripUnit(b);
        }

        internal static void StripMapBuilding(MapBuilding mb)
        {
            if (mb == null)
                return;
            StripArmor(mb.GetArmorProperties());
        }

        internal static void StripDefinition(BuildingDefinition def)
        {
            if (def == null)
                return;
            def.armorTier = 0f;
            if (def.unitPrefab != null)
                StripParts(def.unitPrefab);
        }

        internal static void StripLiveWorld()
        {
            Building[] buildings = Resources.FindObjectsOfTypeAll<Building>();
            for (int i = 0; i < buildings.Length; i++)
            {
                if (buildings[i] == null)
                    continue;
                StripBuilding(buildings[i]);
            }

            MapBuilding[] maps = Resources.FindObjectsOfTypeAll<MapBuilding>();
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] == null)
                    continue;
                StripMapBuilding(maps[i]);
            }
        }
    }

    [HarmonyPatch(typeof(Building), "Awake")]
    internal static class Patch_Building_Awake_StripResist
    {
        [HarmonyPostfix]
        private static void Postfix(Building __instance)
        {
            BuildingResistStripService.StripBuilding(__instance);
        }
    }

    [HarmonyPatch(typeof(MapBuilding), "AssignBuildingSet")]
    internal static class Patch_MapBuilding_AssignBuildingSet_StripResist
    {
        [HarmonyPostfix]
        private static void Postfix(MapBuilding __instance)
        {
            BuildingResistStripService.StripMapBuilding(__instance);
        }
    }

    [HarmonyPatch(typeof(MapBuilding), "TakeDamage")]
    internal static class Patch_MapBuilding_TakeDamage_StripResist
    {
        [HarmonyPrefix]
        private static void Prefix(
            MapBuilding __instance,
            ref float pierceDamage,
            ref float blastDamage,
            ref float fireDamage,
            ref float impactDamage,
            PersistentID dealerID)
        {
            BuildingResistStripService.StripMapBuilding(__instance);
            ModularArmorService.FilterMapBuilding(
                __instance, ref pierceDamage, ref blastDamage, ref fireDamage, ref impactDamage, dealerID);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeDamage")]
    internal static class Patch_UnitPart_TakeDamage_StripBuildingArmor
    {
        [HarmonyPrefix]
        private static void Prefix(UnitPart __instance)
        {
            if (__instance == null)
                return;
            Unit parent = null;
            try { parent = __instance.parentUnit; }
            catch { return; }
            if (!(parent is Building))
                return;
            BuildingResistStripService.StripArmor(__instance.GetArmorProperties());
        }
    }
}
