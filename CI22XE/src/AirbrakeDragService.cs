using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Double Airbrake.dragAmount once per instance (vanilla FixedUpdate force
    /// is dragAmount × density × v²).
    /// </summary>
    internal static class AirbrakeDragService
    {
        internal const float DragMul = 2f;
        private static readonly FieldInfo DragAmountField =
            AccessTools.Field(typeof(Airbrake), "dragAmount");
        private static readonly HashSet<int> Touched = new HashSet<int>();

        internal static void Apply(Airbrake brake)
        {
            if (brake == null || DragAmountField == null)
                return;
            if (!Plugin.IsRuntimeInstance(brake))
                return;
            int id = brake.GetInstanceID();
            if (Touched.Contains(id))
                return;
            float d = 0f;
            try
            {
                object raw = DragAmountField.GetValue(brake);
                if (raw == null)
                    return;
                d = (float)raw;
            }
            catch { return; }
            if (d <= 0f)
            {
                Touched.Add(id);
                return;
            }
            try { DragAmountField.SetValue(brake, d * DragMul); }
            catch { return; }
            Touched.Add(id);
        }
    }

    [HarmonyPatch(typeof(Airbrake), "Start")]
    internal static class Patch_Airbrake_Start
    {
        private static void Postfix(Airbrake __instance)
        {
            AirbrakeDragService.Apply(__instance);
        }
    }
}
