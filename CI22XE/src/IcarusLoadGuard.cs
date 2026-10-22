using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Icarus Plugin.Update waits on Encyclopedia.i then LoadBundles() on the same
    /// frame. That sync AssetBundle.LoadFromFile runs while MainMenu still awaits
    /// GameAssets / Rewired / NetworkManager and deadlocks the loading overlay.
    /// Console spam: "Waiting for prefabs to load...".
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_Icarus_PluginUpdate
    {
        private static Type _pluginType;
        private static FieldInfo _done;
        private static MethodInfo _collect;
        private static MethodInfo _load;
        private static bool _started;

        private static bool Prepare()
        {
            _pluginType = Type.GetType("IcarusArmories.Plugin, IcarusArmories");
            if (_pluginType == null)
                return false;
            _done = AccessTools.Field(_pluginType, "done");
            _collect = AccessTools.Method(_pluginType, "CollectHangars");
            Type importer = Type.GetType("IcarusArmories.AssetBundleImporter, IcarusArmories");
            _load = importer != null ? AccessTools.Method(importer, "LoadBundles") : null;
            return _done != null && _load != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(_pluginType, "Update");
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            if (__instance == null)
                return false;
            if (_started)
                return false;
            try
            {
                if (_done != null && (bool)_done.GetValue(__instance))
                    return false;
            }
            catch { return false; }

            if (MainMenu.State != MainMenu.LoadingState.Loaded)
                return false;
            if (Encyclopedia.i == null)
                return false;

            _started = true;
            try { _done.SetValue(__instance, true); }
            catch { }
            // Oritasy loads *.baas itself. Leave Icarus inert if the DLL is still present.
            return false;
        }

        private static IEnumerator ImportNextFrame(object inst)
        {
            yield return null;
            ImportNow(inst);
        }

        private static void ImportNow(object inst)
        {
            try
            {
                if (_collect != null)
                    _collect.Invoke(inst, null);
                if (_load != null)
                    _load.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException != null ? ex.InnerException : ex;
                if (Plugin.Log != null)
                    Plugin.Log.LogError("Icarus bundle import: " + inner.Message);
            }
        }
    }
}
