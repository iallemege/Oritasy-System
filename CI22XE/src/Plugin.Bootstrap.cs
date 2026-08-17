using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Runtime checks and one-shot ApplyAll scan</summary>
    public partial class Plugin
    {
        internal static bool IsRuntimeInstance(Component c)
        {
            if (c == null || c.gameObject == null)
                return false;
            try { return c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }
            catch { return false; }
        }

        private static bool ApplyAllDone;

        internal static void ApplyAll()
        {
            // Encyclopedia AfterLoad fires multiple patches — only pay the full scan once
            if (ApplyAllDone)
                return;

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            AircraftDefinition[] defs = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
            for (int i = 0; i < defs.Length; i++)
                TryRenameDefinition(defs[i]);

            ShipDefinition[] ships = Resources.FindObjectsOfTypeAll<ShipDefinition>();
            for (int i = 0; i < ships.Length; i++)
                TryRenameShipDefinition(ships[i]);

            VehicleDefinition[] vehicles = Resources.FindObjectsOfTypeAll<VehicleDefinition>();
            for (int i = 0; i < vehicles.Length; i++)
                TryRenameVehicleDefinition(vehicles[i]);

            BuildingDefinition[] buildings = Resources.FindObjectsOfTypeAll<BuildingDefinition>();
            for (int i = 0; i < buildings.Length; i++)
                TryBrandBuildingDefinition(buildings[i]);

            RefreshEncyclopediaAircraft();
            RefreshEncyclopediaShips();
            RefreshEncyclopediaVehicles();
            RefreshEncyclopediaBuildings();

            ArtilleryAaService.Apply();

            Aircraft[] aircraft = Resources.FindObjectsOfTypeAll<Aircraft>();
            for (int i = 0; i < aircraft.Length; i++)
            {
                if (aircraft[i] != null && IsRuntimeInstance(aircraft[i]))
                    TrySetupAircraft(aircraft[i]);
            }

            Ship[] shipUnits = Resources.FindObjectsOfTypeAll<Ship>();
            for (int i = 0; i < shipUnits.Length; i++)
            {
                if (shipUnits[i] != null && IsRuntimeInstance(shipUnits[i]))
                    TrySetupShip(shipUnits[i]);
            }

            GroundVehicle[] gvs = Resources.FindObjectsOfTypeAll<GroundVehicle>();
            for (int i = 0; i < gvs.Length; i++)
            {
                if (gvs[i] != null && IsRuntimeInstance(gvs[i]))
                    TrySetupGroundVehicle(gvs[i]);
            }

            ApplyAllDone = true;
            try { GameZhLocalizer.NotifyEncyclopediaLoaded(); }
            catch { }

            PerfProbeService.Accrue("Oritasy.ApplyAll",
                System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }
    }
}
