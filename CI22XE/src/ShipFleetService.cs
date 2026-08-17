using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield naval fleet branding + propulsion buffs (0.0.9.63).
    /// </summary>
    internal static class ShipFleetService
    {
        internal static void TryRenameShipDefinition(ShipDefinition def)
        {
            if (def == null)
                return;
            if (!Plugin.Touched.AddShipDef(def.GetInstanceID()))
                return;

            def.unitName = EncyclopediaBrandService.AppendSuffix(def.unitName, Plugin.ShipSuffix);
            def.code = EncyclopediaBrandService.AppendSuffix(def.code, Plugin.ShipSuffix);
            if (def.shipInfo != null && def.shipInfo.topSpeed > 0f
                && Plugin.ShipPowerMultiplier != null && Plugin.ShipPowerMultiplier.Value > 0f)
                def.shipInfo.topSpeed *= Plugin.ShipPowerMultiplier.Value;

            string desc = def.description != null ? def.description : string.Empty;
            EncyclopediaBrandService.EnsureBrandDescription(
                ref desc, Plugin.ShipBrand, Plugin.ShipBrand + " modified naval unit.");
            def.description = desc;

            EncyclopediaBrandService.UpdateLookup(def);

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("Ship NE rename: " + def.jsonKey + " -> " + def.unitName);
        }

        internal static void TryBuffShipPropulsion(ShipPropulsion prop)
        {
            if (prop == null || !Plugin.IsRuntimeInstance(prop))
                return;
            if (!Plugin.Touched.AddShipPropulsion(prop.GetInstanceID()))
                return;

            float mul = Plugin.ShipPowerMultiplier != null ? Plugin.ShipPowerMultiplier.Value : 1.5f;
            if (mul <= 0f || Mathf.Abs(mul - 1f) < 0.0001f)
                return;

            try
            {
                if (EngineReflection.ShipThrust != null)
                {
                    float t = (float)EngineReflection.ShipThrust.GetValue(prop);
                    EngineReflection.ShipThrust.SetValue(prop, t * mul);
                }
                if (EngineReflection.ShipSteerThrust != null)
                {
                    float s = (float)EngineReflection.ShipSteerThrust.GetValue(prop);
                    EngineReflection.ShipSteerThrust.SetValue(prop, s * mul);
                }
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogWarning("Ship propulsion buff: " + ex.Message);
            }
        }

        internal static void TrySetupShip(Ship ship)
        {
            if (ship == null || !Plugin.IsRuntimeInstance(ship))
                return;

            ShipDefinition def = ship.definition as ShipDefinition;
            if (def != null)
                TryRenameShipDefinition(def);

            ShipPropulsion[] props = ship.GetComponentsInChildren<ShipPropulsion>(true);
            for (int i = 0; i < props.Length; i++)
                TryBuffShipPropulsion(props[i]);

            AaaHitService.TryBuffHost(ship);
        }
    }
}
