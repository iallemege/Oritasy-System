using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Moray Class: tiny sail hitbox at runtime, void player-only helipad,
    /// encyclopedia registration for TST-48.
    /// </summary>
    internal static class MorayRuntime
    {
        internal const string ShipKey = "Moray1";
        internal const string TorpKey = "MorayTorp1";
        internal const string HangarName = "MorayHelipad";
        internal const string HitBoxName = "MorayHitBox";

        internal static bool IsMoray(Unit unit)
        {
            if (unit == null || unit.definition == null)
                return false;
            return unit.definition.jsonKey == ShipKey;
        }

        internal static bool IsPlayerPad(Hangar hangar)
        {
            if (hangar == null)
                return false;
            try
            {
                if (hangar.name == HangarName)
                    return true;
            }
            catch { }
            Ship ship = null;
            try { ship = hangar.GetComponentInParent<Ship>(); }
            catch { }
            return IsMoray(ship);
        }

        internal static void AfterLoad(Encyclopedia enc)
        {
            if (enc == null)
                return;
            MissileDefinition torp = FindMissile(TorpKey);
            if (torp != null && enc.missiles != null)
            {
                bool have = false;
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    if (enc.missiles[i] != null && enc.missiles[i].jsonKey == TorpKey)
                    {
                        have = true;
                        break;
                    }
                }
                if (!have)
                    enc.missiles.Add(torp);
                if (Encyclopedia.Lookup != null)
                    Encyclopedia.Lookup[TorpKey] = torp;
            }
            ShipDefinition ship = FindShip(ShipKey);
            if (ship != null && enc.ships != null)
            {
                bool have = false;
                for (int i = 0; i < enc.ships.Count; i++)
                {
                    if (enc.ships[i] != null && enc.ships[i].jsonKey == ShipKey)
                    {
                        have = true;
                        break;
                    }
                }
                if (!have)
                    enc.ships.Add(ship);
                if (Encyclopedia.Lookup != null)
                    Encyclopedia.Lookup[ShipKey] = ship;
            }
            WeaponMount mount = FindMount("MorayTorp1_single");
            if (mount != null && enc.weaponMounts != null)
            {
                bool have = false;
                for (int i = 0; i < enc.weaponMounts.Count; i++)
                {
                    if (enc.weaponMounts[i] != null && enc.weaponMounts[i].jsonKey == "MorayTorp1_single")
                    {
                        have = true;
                        break;
                    }
                }
                if (!have)
                    enc.weaponMounts.Add(mount);
                if (Encyclopedia.WeaponLookup != null)
                    Encyclopedia.WeaponLookup["MorayTorp1_single"] = mount;
            }
        }

        internal static void TrySetupShip(Ship ship)
        {
            if (!IsMoray(ship))
                return;
            ShrinkColliders(ship.gameObject);
            EnsureHitBox(ship.gameObject);
        }

        internal static Vector3 PlayerPadPos(Hangar hangar, Transform spawn, out float yaw)
        {
            yaw = 0f;
            Transform t = spawn;
            if (t == null && hangar != null)
            {
                try { t = hangar.GetSpawnTransform(); }
                catch { t = null; }
            }
            if (t == null && hangar != null)
                t = hangar.transform;
            if (t == null)
                return Vector3.zero;
            yaw = t.eulerAngles.y;
            return t.position;
        }

        static void ShrinkColliders(GameObject root)
        {
            Collider[] cs = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == null)
                    continue;
                if (cs[i].gameObject.name == HitBoxName)
                {
                    cs[i].enabled = true;
                    continue;
                }
                cs[i].enabled = false;
            }
        }

        static void EnsureHitBox(GameObject root)
        {
            Transform t = root.transform.Find(HitBoxName);
            GameObject hit;
            if (t != null)
                hit = t.gameObject;
            else
            {
                hit = new GameObject(HitBoxName);
                hit.transform.SetParent(root.transform, false);
                hit.transform.localPosition = new Vector3(0f, 2.35f, 6.5f);
                hit.layer = root.layer;
            }
            BoxCollider box = hit.GetComponent<BoxCollider>();
            if (box == null)
                box = hit.AddComponent<BoxCollider>();
            box.enabled = true;
            box.size = new Vector3(1.5f, 2.0f, 2.2f);
            box.center = Vector3.zero;
        }

        static MissileDefinition FindMissile(string key)
        {
            MissileDefinition[] all = Resources.FindObjectsOfTypeAll<MissileDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].jsonKey == key)
                    return all[i];
            }
            return null;
        }

        static ShipDefinition FindShip(string key)
        {
            ShipDefinition[] all = Resources.FindObjectsOfTypeAll<ShipDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].jsonKey == key)
                    return all[i];
            }
            return null;
        }

        static WeaponMount FindMount(string key)
        {
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].jsonKey == key)
                    return all[i];
            }
            return null;
        }
    }
}
