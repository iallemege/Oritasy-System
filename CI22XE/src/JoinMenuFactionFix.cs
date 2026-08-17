using System;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Join-menu (and aircraft-select) uGUI must receive clicks. IMGUI chips and
    /// a pre-assigned HQ from SaveData both block picking BDF/PALA after hosting.
    /// </summary>
    internal static class JoinMenuFactionFix
    {
        private static readonly FieldInfo JoinMenuField =
            AccessTools.Field(typeof(GameplayUI), "joinMenu");
        private static readonly MethodInfo HqSetter =
            AccessTools.PropertySetter(typeof(Player), "HQ");
        private static readonly MethodInfo GetAuthData =
            AccessTools.Method(typeof(BasePlayer), "GetAuthData");

        internal static bool SelectionUiOpen()
        {
            try
            {
                return CursorManager.GetFlag(CursorFlags.SelectionMenu);
            }
            catch
            {
                return false;
            }
        }

        internal static bool JoinMenuOpen()
        {
            if (!SelectionUiOpen())
                return false;
            try
            {
                GameplayUI ui = SceneSingleton<GameplayUI>.i;
                if (ui == null || JoinMenuField == null)
                    return false;
                GameObject go = JoinMenuField.GetValue(ui) as GameObject;
                return go != null && go.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        internal static void ClearHqForJoinPick(Player player)
        {
            if (player == null)
                return;
            FactionHQ cur = null;
            try { cur = player.HQ; }
            catch { }
            if (cur == null)
                return;
            try { cur.RemovePlayer(player); }
            catch { }
            try
            {
                if (HqSetter != null)
                    HqSetter.Invoke(player, new object[] { null });
            }
            catch { }
            ClearSavedFaction(player);
        }

        private static void ClearSavedFaction(Player player)
        {
            if (GetAuthData == null)
                return;
            try
            {
                object auth = GetAuthData.Invoke(player, null);
                if (auth == null)
                    return;
                FieldInfo saveField = AccessTools.Field(auth.GetType(), "SaveData");
                if (saveField == null)
                    return;
                SavedPlayerData data = saveField.GetValue(auth) as SavedPlayerData;
                if (data != null)
                    data.Faction = null;
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Player_ValidateFactionChange_JoinMenu
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo two = AccessTools.Method(typeof(Player), "ValidateFactionChange",
                new Type[] { typeof(FactionHQ), typeof(bool) });
            if (two != null)
                return two;
            return AccessTools.Method(typeof(Player), "ValidateFactionChange",
                new Type[] { typeof(FactionHQ) });
        }

        [HarmonyPrefix]
        private static void Prefix(Player __instance, FactionHQ newHQ)
        {
            if (!JoinMenuFactionFix.JoinMenuOpen())
                return;
            if (__instance == null || newHQ == null)
                return;
            FactionHQ cur = null;
            try { cur = __instance.HQ; }
            catch { }
            if (cur == null || object.ReferenceEquals(cur, newHQ))
                return;
            JoinMenuFactionFix.ClearHqForJoinPick(__instance);
        }
    }
}
