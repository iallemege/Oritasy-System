using System;
using System.Reflection;
using HarmonyLib;
using Mirage;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Stamp Mirage _prefabHash on BIA / baas aircraft only. Scene objects
    /// (HQ, LevelInfo) must keep hash 0 and use sceneId.
    /// AssetBundle Instantiate still ships hash 0, so Spawn / SendSpawnMessage
    /// must copy the registered prefab hash onto the live identity.
    /// </summary>
    internal static class BaasNetworkFixer
    {
        private static readonly FieldInfo PrefabHashField =
            AccessTools.Field(typeof(NetworkIdentity), "_prefabHash");

        internal static void EnsureDefinition(AircraftDefinition def)
        {
            if (def == null)
                return;
            try { EnsurePrefab(def.unitPrefab); }
            catch { }
        }

        internal static void EnsurePrefab(GameObject prefab)
        {
            if (prefab == null || !LooksLikeBaasRoot(prefab))
                return;
            NetworkIdentity[] ids = null;
            try { ids = prefab.GetComponentsInChildren<NetworkIdentity>(true); }
            catch { ids = null; }
            if (ids == null || ids.Length == 0)
                return;
            int hash = StableHash(prefab.name);
            for (int i = 0; i < ids.Length; i++)
            {
                if (ReadHash(ids[i]) != 0)
                    continue;
                WriteHash(ids[i], hash, false);
            }
        }

        internal static void EnsureIdentity(NetworkIdentity identity)
        {
            if (!IsBaasTarget(identity))
                return;
            int hash = ReadHash(identity);
            if (hash == 0)
            {
                hash = HashFromAircraftPrefab(identity);
                if (hash == 0)
                {
                    string n = identity.gameObject != null ? identity.gameObject.name : null;
                    hash = StableHash(n);
                }
            }
            WriteHash(identity, hash, true);
        }

        internal static int ReadHash(NetworkIdentity identity)
        {
            if (identity == null)
                return 0;
            try
            {
                if (PrefabHashField != null)
                    return (int)PrefabHashField.GetValue(identity);
            }
            catch { }
            try { return identity.PrefabHash; }
            catch { return 0; }
        }

        internal static bool IsBaasTarget(NetworkIdentity identity)
        {
            if (identity == null || identity.gameObject == null)
                return false;
            if (IsSceneSingletonName(identity.gameObject.name))
                return false;
            if (LooksLikeBaasRoot(identity.gameObject))
                return true;
            Aircraft ac = null;
            try { ac = identity.GetComponent<Aircraft>(); }
            catch { ac = null; }
            if (ac == null)
                return false;
            return AircraftIdentity.IsBiaAircraft(ac)
                || IcarusAircraftRegistry.Contains(ac.definition as AircraftDefinition);
        }

        private static bool IsSceneSingletonName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.IndexOf("HQ", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("LevelInfo", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("NetworkManager", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.StartsWith("Player", StringComparison.OrdinalIgnoreCase)
                && name.IndexOf("J35", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
            return false;
        }

        private static bool LooksLikeBaasRoot(GameObject go)
        {
            if (go == null)
                return false;
            string n = go.name;
            if (!string.IsNullOrEmpty(n)
                && (n.IndexOf("J35", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Draken", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Wyvern", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("飞龙", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            try
            {
                if (go.GetComponent<Aircraft>() != null && AircraftIdentity.HasBiaAvionics(go))
                    return true;
            }
            catch { }
            return false;
        }

        private static int HashFromAircraftPrefab(NetworkIdentity identity)
        {
            Aircraft ac = null;
            try { ac = identity.GetComponent<Aircraft>(); }
            catch { ac = null; }
            if (ac == null || ac.definition == null || ac.definition.unitPrefab == null)
                return 0;
            GameObject prefab = ac.definition.unitPrefab;
            NetworkIdentity src = null;
            try { src = prefab.GetComponent<NetworkIdentity>(); }
            catch { src = null; }
            int hash = ReadHash(src);
            if (hash != 0)
                return hash;
            EnsurePrefab(prefab);
            return ReadHash(src);
        }

        private static void WriteHash(NetworkIdentity identity, int hash, bool forceProperty)
        {
            if (identity == null)
                return;
            int before = ReadHash(identity);
            if (before != 0)
                return;
            if (hash == 0)
                hash = 1;
            try
            {
                if (PrefabHashField != null)
                    PrefabHashField.SetValue(identity, hash);
            }
            catch { }
            if (forceProperty || before == 0)
            {
                try { identity.PrefabHash = hash; }
                catch { }
            }
            if (before == 0 && Plugin.Log != null)
            {
                string n = identity.gameObject != null ? identity.gameObject.name : "?";
                Plugin.Log.LogInfo("Baas prefabHash " + n + "=" + hash);
            }
        }

        internal static int StableHash(string name)
        {
            name = CleanName(name);
            int hash = Fnv1a32(name);
            if (hash == 0)
                hash = 1;
            return hash;
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "aircraft";
            if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7).TrimEnd();
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            if (p > 0)
                name = name.Substring(0, p);
            if (string.IsNullOrEmpty(name))
                return "aircraft";
            return name;
        }

        internal static int Fnv1a32(string text)
        {
            int hash = -2128831035;
            if (string.IsNullOrEmpty(text))
                return hash;
            for (int i = 0; i < text.Length; i++)
                hash = (hash ^ text[i]) * 16777619;
            return hash;
        }
    }

    [HarmonyPatch(typeof(ClientObjectManager), "RegisterPrefab", new Type[] { typeof(NetworkIdentity) })]
    internal static class Patch_Baas_RegisterPrefab
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(NetworkIdentity identity)
        {
            BaasNetworkFixer.EnsureIdentity(identity);
        }
    }

    [HarmonyPatch(typeof(ClientObjectManager), "RegisterPrefab", new Type[] { typeof(NetworkIdentity), typeof(int) })]
    internal static class Patch_Baas_RegisterPrefabHash
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(NetworkIdentity identity, ref int newPrefabHash)
        {
            BaasNetworkFixer.EnsureIdentity(identity);
            if (newPrefabHash == 0 && BaasNetworkFixer.IsBaasTarget(identity))
                newPrefabHash = BaasNetworkFixer.ReadHash(identity);
        }
    }

    [HarmonyPatch(typeof(ServerObjectManager), "Spawn", new Type[] { typeof(NetworkIdentity) })]
    internal static class Patch_Baas_ServerSpawn
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(NetworkIdentity identity)
        {
            BaasNetworkFixer.EnsureIdentity(identity);
        }
    }

    [HarmonyPatch(typeof(ServerObjectManager), "Spawn", new Type[] { typeof(NetworkIdentity), typeof(INetworkPlayer) })]
    internal static class Patch_Baas_ServerSpawnOwner
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(NetworkIdentity identity)
        {
            BaasNetworkFixer.EnsureIdentity(identity);
        }
    }

    [HarmonyPatch(typeof(ServerObjectManager), "Spawn", new Type[] { typeof(NetworkIdentity), typeof(int), typeof(INetworkPlayer) })]
    internal static class Patch_Baas_ServerSpawnHash
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(NetworkIdentity identity, ref int prefabHash)
        {
            BaasNetworkFixer.EnsureIdentity(identity);
            if (prefabHash == 0 && BaasNetworkFixer.IsBaasTarget(identity))
                prefabHash = BaasNetworkFixer.ReadHash(identity);
        }
    }

    [HarmonyPatch(typeof(ServerObjectManager), "SendSpawnMessage")]
    internal static class Patch_Baas_SendSpawnMessage
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(NetworkIdentity identity)
        {
            BaasNetworkFixer.EnsureIdentity(identity);
        }
    }

    [HarmonyPatch(typeof(Hangar), "TrySpawnAircraft")]
    internal static class Patch_Baas_HangarTrySpawn
    {
        [HarmonyPrefix]
        private static void Prefix(AircraftDefinition definition)
        {
            if (definition == null)
                return;
            BaasNetworkFixer.EnsureDefinition(definition);
            if (Plugin.Log != null && AircraftIdentity.IsJ35WyvernKey(definition.jsonKey))
                Plugin.Log.LogInfo("J35 hangar TrySpawn " + definition.jsonKey);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(AircraftDefinition definition, Exception __exception)
        {
            if (__exception != null && Plugin.Log != null)
            {
                string key = definition != null ? definition.jsonKey : "?";
                Plugin.Log.LogError("Hangar TrySpawn " + key + ": " + __exception);
            }
            return __exception;
        }
    }
}
