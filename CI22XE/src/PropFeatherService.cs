using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Prop aircraft out of fuel: auto-feather, cut remaining drag 12%,
    /// then shut the engine down. After refuel: unfeather to normal pitch
    /// and prompt the player to start the engine manually.
    /// </summary>
    internal static class PropFeatherService
    {
        private const float DragCut = 0.12f;
        private const float NormalPitchT = 0.28f;

        private static readonly FieldInfo FeatherMode =
            AccessTools.Field(typeof(ConstantSpeedProp), "featherMode");
        private static readonly FieldInfo FeatherIfLost =
            AccessTools.Field(typeof(ConstantSpeedProp), "featherIfPowerLost");
        private static readonly FieldInfo BladeMax =
            AccessTools.Field(typeof(ConstantSpeedProp), "bladeMaxPitch");
        private static readonly FieldInfo BladeMin =
            AccessTools.Field(typeof(ConstantSpeedProp), "bladeMinPitch");
        private static readonly FieldInfo PitchRate =
            AccessTools.Field(typeof(ConstantSpeedProp), "pitchRate");
        private static readonly FieldInfo PropAircraft =
            AccessTools.Field(typeof(ConstantSpeedProp), "aircraft");
        private static readonly FieldInfo PropPart =
            AccessTools.Field(typeof(ConstantSpeedProp), "unitPart");
        private static readonly FieldInfo PropForceTorque =
            AccessTools.Field(typeof(ConstantSpeedProp), "forceAndTorque");

        private static readonly HashSet<int> Feathered = new HashSet<int>();

        internal static bool IsPropAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                return ac.GetComponentInChildren<ConstantSpeedProp>(true) != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool OutOfFuel(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                List<FuelTank> tanks = ac.GetFuelTanks();
                if (tanks == null || tanks.Count == 0)
                    return false;
                return ac.GetFuelQuantity() <= 0.01f;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldFeather(Aircraft ac)
        {
            return IsPropAircraft(ac) && OutOfFuel(ac);
        }

        internal static Aircraft ReadAircraft(ConstantSpeedProp prop)
        {
            if (prop == null || PropAircraft == null)
                return null;
            try { return PropAircraft.GetValue(prop) as Aircraft; }
            catch { return null; }
        }

        internal static void BeginFeather(ConstantSpeedProp prop, Aircraft ac)
        {
            if (prop == null || ac == null)
                return;
            if (FeatherIfLost != null)
            {
                try { FeatherIfLost.SetValue(prop, true); }
                catch { }
            }
            if (FeatherMode != null)
            {
                try { FeatherMode.SetValue(prop, true); }
                catch { }
            }
            if (Feathered.Add(ac.GetInstanceID()))
            {
                ShutdownEngine(ac);
                ReportLocal(ac,
                    UiLang.T("Auto-switched to feathering", "自动切换到顺桨"),
                    3f);
            }
        }

        internal static void EndFeather(ConstantSpeedProp prop)
        {
            if (prop == null)
                return;
            if (FeatherMode != null)
            {
                try { FeatherMode.SetValue(prop, false); }
                catch { }
            }
            SnapNormalPitch(prop);
        }

        internal static void DriveFeatherPitch(ConstantSpeedProp prop)
        {
            if (prop == null || BladeMax == null || PitchRate == null)
                return;
            try
            {
                float maxP = (float)BladeMax.GetValue(prop);
                float rate = (float)PitchRate.GetValue(prop);
                prop.PropPitch = Mathf.MoveTowards(
                    prop.PropPitch, maxP, rate * Time.fixedDeltaTime);
            }
            catch { }
        }

        internal static void CutPropDrag(ConstantSpeedProp prop, Aircraft ac)
        {
            if (prop == null || ac == null || PropForceTorque == null)
                return;
            if (ac.rb == null || ac.rb.velocity.sqrMagnitude < 1f)
                return;
            ForceAndTorque ft;
            try { ft = (ForceAndTorque)PropForceTorque.GetValue(prop); }
            catch { return; }
            Vector3 vdir = ac.rb.velocity.normalized;
            float along = Vector3.Dot(ft.force, vdir);
            if (along >= 0f)
                return;
            UnitPart part = null;
            if (PropPart != null)
            {
                try { part = PropPart.GetValue(prop) as UnitPart; }
                catch { part = null; }
            }
            Rigidbody rb = part != null && part.rb != null ? part.rb : ac.rb;
            if (rb == null)
                return;
            rb.AddForce(-along * DragCut * vdir);
        }

        internal static void OnFueled(Aircraft ac, ConstantSpeedProp prop)
        {
            if (ac == null)
                return;
            if (!Feathered.Remove(ac.GetInstanceID()))
                return;
            ConstantSpeedProp[] props = ac.GetComponentsInChildren<ConstantSpeedProp>(true);
            if (props != null && props.Length > 0)
            {
                for (int i = 0; i < props.Length; i++)
                    EndFeather(props[i]);
            }
            else
                EndFeather(prop);
            ReportLocal(ac,
                UiLang.T(
                    "Start engine manually. Auto-switched to normal pitch",
                    "手动开启引擎，已自动切换回正桨"),
                5f);
        }

        private static void SnapNormalPitch(ConstantSpeedProp prop)
        {
            if (prop == null || BladeMax == null || BladeMin == null)
                return;
            try
            {
                float maxP = (float)BladeMax.GetValue(prop);
                float minP = (float)BladeMin.GetValue(prop);
                prop.PropPitch = Mathf.Lerp(minP, maxP, NormalPitchT);
            }
            catch { }
        }

        private static void ShutdownEngine(Aircraft ac)
        {
            if (ac == null || !ac.Ignition)
                return;
            if (!IsLocalHudAircraft(ac))
                return;
            try
            {
                if (ac.IsServer)
                    ac.NetworkIgnition = false;
                else
                    ac.CmdToggleIgnition();
            }
            catch
            {
                try { ac.NetworkIgnition = false; }
                catch { ac.Ignition = false; }
            }
        }

        private static bool IsLocalHudAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                Aircraft local;
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return false;
                if (!object.ReferenceEquals(local, ac))
                    return false;
                if (SceneSingleton<CombatHUD>.i == null
                    || SceneSingleton<CombatHUD>.i.aircraft != ac)
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReportLocal(Aircraft ac, string text, float seconds)
        {
            if (ac == null || string.IsNullOrEmpty(text))
                return;
            if (!IsLocalHudAircraft(ac))
                return;
            try
            {
                if (SceneSingleton<AircraftActionsReport>.i == null)
                    return;
                SceneSingleton<AircraftActionsReport>.i.ReportText(text, seconds);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ConstantSpeedProp), "FixedUpdate")]
    internal static class Patch_Prop_FuelFeather
    {
        [HarmonyPrefix]
        private static void Prefix(ConstantSpeedProp __instance)
        {
            Aircraft ac = PropFeatherService.ReadAircraft(__instance);
            if (!PropFeatherService.ShouldFeather(ac))
            {
                PropFeatherService.OnFueled(ac, __instance);
                return;
            }
            PropFeatherService.BeginFeather(__instance, ac);
        }

        [HarmonyPostfix]
        private static void Postfix(ConstantSpeedProp __instance)
        {
            Aircraft ac = PropFeatherService.ReadAircraft(__instance);
            if (!PropFeatherService.ShouldFeather(ac))
                return;
            PropFeatherService.BeginFeather(__instance, ac);
            PropFeatherService.DriveFeatherPitch(__instance);
            PropFeatherService.CutPropDrag(__instance, ac);
        }
    }

    [HarmonyPatch(typeof(ConstantSpeedProp), "AutoPropPitch")]
    internal static class Patch_Prop_FuelFeatherPitch
    {
        [HarmonyPostfix]
        private static void Postfix(ConstantSpeedProp __instance)
        {
            Aircraft ac = PropFeatherService.ReadAircraft(__instance);
            if (!PropFeatherService.ShouldFeather(ac))
                return;
            PropFeatherService.DriveFeatherPitch(__instance);
        }
    }
}
