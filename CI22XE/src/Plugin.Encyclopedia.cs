using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Encyclopedia rename / brand refresh — thin facade over fleet services (0.0.9.63).</summary>
    public partial class Plugin
    {
        internal static Encyclopedia GetEncyclopedia()
        {
            if (CachedEncyclopedia != null)
                return CachedEncyclopedia;
            try
            {
                PropertyInfo p = AccessTools.Property(typeof(Encyclopedia), "i");
                if (p != null)
                {
                    Encyclopedia viaProp = p.GetValue(null, null) as Encyclopedia;
                    if (viaProp != null)
                    {
                        CachedEncyclopedia = viaProp;
                        return viaProp;
                    }
                }
            }
            catch { }
            Encyclopedia[] all = Resources.FindObjectsOfTypeAll<Encyclopedia>();
            if (all == null || all.Length == 0)
                return null;
            CachedEncyclopedia = all[0];
            return all[0];
        }

        /// <summary>Throttle encyclopedia re-brand when browser tabs are clicked.</summary>
        internal static bool AllowEncyclopediaRefresh()
        {
            float next;
            if (!EncyclopediaBrandService.AllowRefresh(Time.unscaledTime, NextEncRefresh, 1.5f, out next))
                return false;
            NextEncRefresh = next;
            return true;
        }

        internal static void EnsureBrandDescription(ref string desc, string brand, string emptyFallback)
        {
            EncyclopediaBrandService.EnsureBrandDescription(ref desc, brand, emptyFallback);
        }

        internal static void EnsurePackDescription(ref string desc)
        {
            EncyclopediaBrandService.StripXeBrandLines(ref desc);
            if (UiLang.IsChinese)
            {
                EncyclopediaBrandService.EnsureBrandDescription(
                    ref desc,
                    EncyclopediaBrandService.XeZhBlurb,
                    EncyclopediaBrandService.XeZhBlurb);
            }
            else
            {
                EncyclopediaBrandService.EnsureBrandDescription(
                    ref desc,
                    PackDescLine,
                    EncyclopediaBrandService.XeEnBlurb);
            }
        }

        internal static string AppendSuffix(string s, string suffix)
        {
            return EncyclopediaBrandService.AppendSuffix(s, suffix);
        }

        /// <summary>Rename + brand every aircraft page in the encyclopedia list.</summary>
        internal static void RefreshEncyclopediaAircraft()
        {
            Encyclopedia enc = GetEncyclopedia();
            if (enc == null || enc.aircraft == null)
                return;
            for (int i = 0; i < enc.aircraft.Count; i++)
            {
                AircraftDefinition def = enc.aircraft[i];
                if (def == null)
                    continue;
                TryRenameDefinition(def);
                // Veyrn lore is written by UnitBrandingService.ApplyBrandFields — do not
                // re-prefix [Oritasy] / short blurb over the full encyclopedia story.
                if (IsXeDefinition(def))
                    EncyclopediaBrandService.UpdateLookup(def);
            }
        }

        internal static void RefreshEncyclopediaShips()
        {
            Encyclopedia enc = GetEncyclopedia();
            if (enc == null || enc.ships == null)
                return;
            for (int i = 0; i < enc.ships.Count; i++)
            {
                ShipDefinition def = enc.ships[i];
                if (def == null)
                    continue;
                TryRenameShipDefinition(def);
                string desc = def.description != null ? def.description : string.Empty;
                EnsureBrandDescription(ref desc, ShipBrand, ShipBrand + " modified naval unit.");
                def.description = desc;
                EncyclopediaBrandService.UpdateLookup(def);
            }
        }

        internal static void RefreshEncyclopediaVehicles()
        {
            Encyclopedia enc = GetEncyclopedia();
            if (enc == null || enc.vehicles == null)
                return;
            for (int i = 0; i < enc.vehicles.Count; i++)
            {
                VehicleDefinition def = enc.vehicles[i];
                if (def == null)
                    continue;
                TryRenameVehicleDefinition(def);
                string desc = def.description != null ? def.description : string.Empty;
                EnsureBrandDescription(ref desc, VehicleBrand, VehicleBrand + " modified ground unit.");
                def.description = desc;
                EncyclopediaBrandService.UpdateLookup(def);
            }
        }

        internal static void RefreshEncyclopediaBuildings()
        {
            Encyclopedia enc = GetEncyclopedia();
            if (enc == null || enc.buildings == null)
                return;
            for (int i = 0; i < enc.buildings.Count; i++)
            {
                BuildingDefinition def = enc.buildings[i];
                if (def == null)
                    continue;
                TryBrandBuildingDefinition(def);
                EncyclopediaBrandService.UpdateLookup(def);
            }
        }

        internal static void TryBrandBuildingDefinition(BuildingDefinition def)
        {
            GroundFleetService.TryBrandBuildingDefinition(def);
        }

        internal static void TryRenameShipDefinition(ShipDefinition def)
        {
            ShipFleetService.TryRenameShipDefinition(def);
        }

        internal static void TryRenameVehicleDefinition(VehicleDefinition def)
        {
            GroundFleetService.TryRenameVehicleDefinition(def);
        }

        internal static void TryBuffShipPropulsion(ShipPropulsion prop)
        {
            ShipFleetService.TryBuffShipPropulsion(prop);
        }

        internal static void TrySetupShip(Ship ship)
        {
            ShipFleetService.TrySetupShip(ship);
        }

        internal static void TrySetupGroundVehicle(GroundVehicle gv)
        {
            GroundFleetService.TrySetupGroundVehicle(gv);
        }

        internal static bool IsVanillaDefinition(AircraftDefinition def)
        {
            return AircraftIdentity.IsVanillaDefinition(def);
        }

        internal static bool IsCoinDefinition(AircraftDefinition def)
        {
            return AircraftIdentity.IsCoinDefinition(def);
        }

        internal static bool IsXeDefinition(AircraftDefinition def)
        {
            return AircraftIdentity.IsXeDefinition(def);
        }

        internal static bool IsCoinAircraft(Aircraft aircraft)
        {
            return AircraftIdentity.IsCoinAircraft(aircraft);
        }

        internal static bool IsXeAircraft(Aircraft aircraft)
        {
            return AircraftIdentity.IsXeAircraft(aircraft);
        }

        internal static bool IsAb4Key(string key)
        {
            return AircraftIdentity.IsAb4(key);
        }

        internal static bool IsVt7Key(string key)
        {
            return AircraftIdentity.IsVt7(key);
        }

        /// <summary>CI-22 / AB-4 / VT-7 get half fuel burn.</summary>
        internal static bool WantsFuelEconomy(Aircraft aircraft)
        {
            return AircraftIdentity.WantsFuelEconomy(aircraft);
        }

        internal static bool IsCi22HardpointSet(HardpointSet hs)
        {
            return hs != null && Ci22HardpointSetIds.Contains(hs.GetHashCode());
        }

        internal static string AppendXe(string s)
        {
            return AircraftIdentity.AppendXe(s);
        }

        internal static void TryRenameDefinition(AircraftDefinition def)
        {
            UnitBrandingService.TryRenameDefinition(def);
        }

        internal static float DefaultThrustMultiplier(string key)
        {
            return AircraftIdentity.DefaultThrustMul(key);
        }

        internal static float GetThrustMultiplier(Aircraft aircraft)
        {
            if (aircraft == null)
                return PowerMultiplier != null ? PowerMultiplier.Value : 1.35f;
            return GetOrCreateProfile(aircraft).ThrustMul.Value;
        }

        internal static float GetFuelBurnMultiplier(Aircraft aircraft)
        {
            if (aircraft == null)
                return 1f;
            return GetOrCreateProfile(aircraft).FuelBurnMul.Value;
        }
    }
}
