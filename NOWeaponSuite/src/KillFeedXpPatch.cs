using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    [HarmonyPatch(typeof(Unit), "RegisterHit")]
    internal static class Patch_CombatKillXp_RegisterHit
    {
        [HarmonyPostfix]
        private static void Postfix(Unit __instance, Unit hitUnit)
        {
            CombatKillXpTracker.OnGunHit(__instance, hitUnit);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeDamage")]
    internal static class Patch_CombatKillXp_TakeDamage
    {
        [HarmonyPostfix]
        private static void Postfix(UnitPart __instance, PersistentID dealerID)
        {
            if (__instance == null)
                return;
            Unit victim = null;
            try { victim = __instance.parentUnit; }
            catch { }
            CombatKillXpTracker.OnDealtDamage(dealerID, victim);
        }
    }

    [HarmonyPatch(typeof(Missile), "PenetrateObject")]
    internal static class Patch_CombatKillXp_Penetrate
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, IDamageable damageable)
        {
            if (damageable == null)
                return;
            Unit victim = null;
            try { victim = damageable.GetUnit(); }
            catch { }
            CombatKillXpTracker.OnLocalMissileTouched(__instance, victim);
        }
    }

    [HarmonyPatch(typeof(Missile), "Detonate")]
    [HarmonyPatch(new System.Type[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    internal static class Patch_CombatKillXp_Detonate
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            if (__instance == null)
                return;
            Unit target = null;
            try
            {
                PersistentID tid = __instance.targetID;
                if (tid.IsValid)
                    tid.TryGetUnit(out target);
            }
            catch { }
            CombatKillXpTracker.OnLocalMissileTouched(__instance, target);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "ApplyDamage")]
    internal static class Patch_CombatKillXp_ApplyDamage
    {
        [HarmonyPrefix]
        private static void Prefix(UnitPart __instance, out float __state)
        {
            __state = 0f;
            if (__instance != null)
                __state = __instance.hitPoints;
        }

        [HarmonyPostfix]
        private static void Postfix(UnitPart __instance, float __state)
        {
            if (__instance == null)
                return;
            if (__state > 0f && __instance.hitPoints <= 0f)
                CombatKillXpTracker.OnPartDestroyed(__instance);
        }
    }

    [HarmonyPatch(typeof(ShipPart), "ApplyDamage")]
    internal static class Patch_CombatKillXp_ShipApplyDamage
    {
        [HarmonyPrefix]
        private static void Prefix(ShipPart __instance, out float __state)
        {
            __state = 0f;
            if (__instance != null)
                __state = __instance.hitPoints;
        }

        [HarmonyPostfix]
        private static void Postfix(ShipPart __instance, float __state)
        {
            if (__instance == null)
                return;
            if (__state > 0f && __instance.hitPoints <= 0f)
                CombatKillXpTracker.OnPartDestroyed(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "SpawnFragments")]
    internal static class Patch_CombatKillXp_SpawnFragments
    {
        [HarmonyPrefix]
        private static void Prefix(UnitPart __instance)
        {
            CombatKillXpTracker.OnPartDestroyed(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "Detach")]
    internal static class Patch_CombatKillXp_Detach
    {
        [HarmonyPostfix]
        private static void Postfix(UnitPart __instance)
        {
            CombatKillXpTracker.OnPartDestroyed(__instance);
        }
    }

    [HarmonyPatch(typeof(MessageManager), "UserCode_RpcKillMessage_635947223")]
    internal static class Patch_CombatKillXp_RpcKill
    {
        [HarmonyPrefix]
        private static void Prefix(PersistentID killerID, PersistentID killedID, KillType killedType)
        {
            CombatKillXpTracker.BeginFeed(killerID, killedID, killedType);
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            CombatKillXpTracker.EndFeed();
        }
    }

    [HarmonyPatch(typeof(GameplayUI), "KillFeed")]
    internal static class Patch_CombatKillXp_KillFeed
    {
        [HarmonyPrefix]
        private static void Prefix(ref string message)
        {
            CombatKillXpTracker.TryAppendFeedXp(ref message);
        }
    }
}
