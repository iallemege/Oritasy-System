using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield ground vehicle + building encyclopedia brands / speed buffs (0.0.9.63).
    /// </summary>
    internal static class GroundFleetService
    {
        internal static void TryRenameVehicleDefinition(VehicleDefinition def)
        {
            if (def == null)
                return;
            if (!Plugin.Touched.AddVehicleDef(def.GetInstanceID()))
                return;

            def.unitName = EncyclopediaBrandService.AppendSuffix(def.unitName, Plugin.VehicleSuffix);
            def.code = EncyclopediaBrandService.AppendSuffix(def.code, Plugin.VehicleSuffix);

            string desc = def.description != null ? def.description : string.Empty;
            EncyclopediaBrandService.EnsureBrandDescription(
                ref desc, Plugin.VehicleBrand, Plugin.VehicleBrand + " modified ground unit.");
            def.description = desc;

            EncyclopediaBrandService.UpdateLookup(def);

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("Vehicle TE rename: " + def.jsonKey + " -> " + def.unitName);
        }

        internal static void TryBrandBuildingDefinition(BuildingDefinition def)
        {
            if (def == null)
                return;
            if (!Plugin.Touched.AddBuildingDef(def.GetInstanceID()))
                return;

            string desc = def.description != null ? def.description : string.Empty;
            EncyclopediaBrandService.EnsureBrandDescription(
                ref desc, Plugin.BuildingBrand, Plugin.BuildingBrand + " modified structure.");
            def.description = desc;

            EncyclopediaBrandService.UpdateLookup(def);

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("Building Bexur brand: " + def.jsonKey);
        }

        internal static void TrySetupGroundVehicle(GroundVehicle gv)
        {
            if (gv == null || !Plugin.IsRuntimeInstance(gv))
                return;
            if (!Plugin.Touched.AddGroundVehicle(gv.GetInstanceID()))
                return;

            VehicleDefinition def = gv.definition as VehicleDefinition;
            if (def != null)
                TryRenameVehicleDefinition(def);

            AaaHitService.TryBuffHost(gv);
            ArtilleryAaService.TryBuffHost(gv);

            VehicleCookoffService.BoostIgnition(gv);

            float mul = Plugin.VehiclePowerMultiplier != null ? Plugin.VehiclePowerMultiplier.Value : 1.1f;
            if (mul <= 0f || Mathf.Abs(mul - 1f) < 0.0001f)
                return;

            try
            {
                if (EngineReflection.GvAccel != null)
                {
                    float a = (float)EngineReflection.GvAccel.GetValue(gv);
                    EngineReflection.GvAccel.SetValue(gv, a * mul);
                }
                if (EngineReflection.GvTopOnroad != null)
                {
                    float on = (float)EngineReflection.GvTopOnroad.GetValue(gv);
                    EngineReflection.GvTopOnroad.SetValue(gv, on * mul);
                }
                if (EngineReflection.GvTopOffroad != null)
                {
                    float off = (float)EngineReflection.GvTopOffroad.GetValue(gv);
                    EngineReflection.GvTopOffroad.SetValue(gv, off * mul);
                }
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogWarning("Ground vehicle buff: " + ex.Message);
            }
        }
    }
}
