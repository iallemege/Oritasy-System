using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>MIG-15S-style 1 kt suicide charge. Infantry: Backspace.</summary>
    internal static class InfantryKamikaze
    {
        private const float YieldKt = 1f;
        private const float DemolishRadius = 15f;

        private static readonly System.Reflection.FieldInfo ShockYieldField =
            AccessTools.Field(typeof(Shockwave), "yieldKilotons");

        private static GameObject _nukeFx;
        private static bool _fxTried;
        private static bool _used;

        internal static void Reset()
        {
            _used = false;
        }

        internal static void Tick()
        {
            if (!InfantryModeService.Active || _used)
                return;
            if (InfantryHud.BlocksLook)
                return;
            if (!Input.GetKeyDown(KeyCode.Backspace))
                return;
            Detonate();
        }

        private static void Detonate()
        {
            _used = true;
            Vector3 pos = InfantryModeService.EyePos;
            if (InfantryModeService.PawnRoot != null)
                pos = InfantryModeService.PawnRoot.position;
            PersistentID owner = PersistentID.None;
            Unit linked = InfantryModeService.LinkedUnit();
            if (linked != null)
            {
                try { owner = linked.persistentID; }
                catch { }
            }
            DemolishAround(pos);
            SpawnOneKt(pos, owner);
            InfantryModeService.KillFromKamikaze();
        }

        private static void SpawnOneKt(Vector3 pos, PersistentID owner)
        {
            GameObject prefab = ResolveNukeFx();
            if (prefab == null)
            {
                InfantryFx.Blast(pos);
                return;
            }
            GameObject fx = null;
            try { fx = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity); }
            catch { fx = null; }
            if (fx == null)
            {
                InfantryFx.Blast(pos);
                return;
            }
            try
            {
                MushroomCloud[] clouds = fx.GetComponentsInChildren<MushroomCloud>(true);
                if (clouds != null)
                {
                    for (int i = 0; i < clouds.Length; i++)
                    {
                        if (clouds[i] != null)
                            clouds[i].yield = YieldKt;
                    }
                }
            }
            catch { }
            if (ShockYieldField == null)
                return;
            try
            {
                Shockwave[] waves = fx.GetComponentsInChildren<Shockwave>(true);
                if (waves == null)
                    return;
                for (int i = 0; i < waves.Length; i++)
                {
                    if (waves[i] == null)
                        continue;
                    waves[i].enabled = true;
                    ShockYieldField.SetValue(waves[i], YieldKt);
                    try { waves[i].SetOwner(owner, YieldKt); }
                    catch { }
                }
            }
            catch { }
        }

        private static void DemolishAround(Vector3 pos)
        {
            Collider[] hits = null;
            try { hits = Physics.OverlapSphere(pos, DemolishRadius); }
            catch { hits = null; }
            if (hits == null)
                return;
            PersistentID none = PersistentID.None;
            Transform pawn = InfantryModeService.PawnRoot;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i];
                if (col == null)
                    continue;
                if (pawn != null && col.transform.IsChildOf(pawn))
                    continue;
                MapBuilding mb = null;
                try { mb = col.GetComponentInParent<MapBuilding>(); }
                catch { }
                if (mb != null)
                {
                    try { mb.TakeDamage(0f, 0f, 1f, 0f, 1e9f, none); }
                    catch { }
                    try { mb.ApplyDamage(0f, 0f, 0f, 1e9f); }
                    catch { }
                }
                Unit u = null;
                try { u = col.GetComponentInParent<Unit>(); }
                catch { }
                if (u == null || u.disabled)
                    continue;
                UnitPart[] parts = null;
                try { parts = u.GetComponentsInChildren<UnitPart>(true); }
                catch { }
                if (parts != null)
                {
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (parts[p] == null)
                            continue;
                        try { parts[p].TakeDamage(0f, 0f, 1f, 0f, 1e9f, none); }
                        catch { }
                    }
                }
                try { u.DisableUnit(); }
                catch { }
            }
        }

        private static GameObject ResolveNukeFx()
        {
            if (_nukeFx != null || _fxTried)
                return _nukeFx;
            _fxTried = true;
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            if (all == null)
                return null;
            GameObject sceneHit = null;
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null)
                    continue;
                string n = go.name;
                if (n != "explosion_1kt" && n != "explosion_1kt(Clone)"
                    && n != "explosion_10kt" && n != "explosion_5kt"
                    && n != "explosion_20kt")
                    continue;
                if (go.GetComponentInChildren<MushroomCloud>(true) == null)
                    continue;
                if (!go.scene.IsValid())
                {
                    _nukeFx = go;
                    if (n.IndexOf("1kt") >= 0)
                        return _nukeFx;
                }
                else if (sceneHit == null)
                    sceneHit = go;
            }
            if (_nukeFx == null)
                _nukeFx = sceneHit;
            return _nukeFx;
        }
    }
}
