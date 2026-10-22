using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// J-35 gear visual is the combined FBX piece mesh_165. It cannot be
    /// split onto the three LandingGear hinges, so G hides that mesh.
    /// Placeholder wheel/strut boxes stay invisible.
    /// </summary>
    [HarmonyPatch(typeof(LandingGear), "FixedUpdate")]
    internal static class Patch_J35_GearVisual
    {
        private const string JsonKey = "J35XE_Wyvern";
        private const string JsonKeyLegacy = "J35_Draken";
        private const string GearMesh = "mesh_165";
        private const byte KindSkip = 1;
        private const byte KindJ35 = 2;

        private sealed class State
        {
            public Renderer gearMesh;
            public bool hidPlaceholders;
        }

        private static readonly Dictionary<int, State> States = new Dictionary<int, State>();
        private static readonly Dictionary<int, byte> GearKind = new Dictionary<int, byte>(256);
        private static readonly FieldInfo GearAircraft =
            AccessTools.Field(typeof(LandingGear), "aircraft");

        internal static void ForgetAircraft(Aircraft ac)
        {
            if (ac == null)
                return;
            try { States.Remove(ac.GetInstanceID()); }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix(LandingGear __instance)
        {
            if (__instance == null)
                return;
            int gid = __instance.GetInstanceID();
            byte kind;
            if (GearKind.TryGetValue(gid, out kind) && kind == KindSkip)
                return;

            Aircraft ac = AircraftOf(__instance);
            if (kind != KindJ35)
            {
                if (ac == null)
                    return;
                if (ac.definition == null)
                {
                    GearKind[gid] = KindSkip;
                    return;
                }
                string key = ac.definition.jsonKey;
                if (!AircraftIdentity.IsJ35WyvernKey(key)
                    && !AircraftIdentity.IsCompleteBiaAirframe(ac))
                {
                    GearKind[gid] = KindSkip;
                    if (GearKind.Count > 512)
                        PruneGearKind();
                    return;
                }
                GearKind[gid] = KindJ35;
            }
            if (ac == null)
                ac = AircraftOf(__instance);
            if (ac == null)
                return;

            int id = ac.GetInstanceID();
            State st;
            if (!States.TryGetValue(id, out st) || st == null)
            {
                st = new State();
                States[id] = st;
                if (States.Count > 32)
                    Prune();
            }

            if (!st.hidPlaceholders)
            {
                HidePlaceholders(ac.transform);
                st.hidPlaceholders = true;
            }

            if (st.gearMesh == null)
                st.gearMesh = FindNamedRenderer(ac.transform, GearMesh);

            bool show = true;
            try
            {
                int gearSt = (int)ac.gearState;
                show = gearSt != 1 && gearSt != 3;
            }
            catch { }

            if (st.gearMesh != null && st.gearMesh.enabled != show)
                st.gearMesh.enabled = show;
        }

        private static Aircraft AircraftOf(LandingGear gear)
        {
            if (gear == null)
                return null;
            if (GearAircraft != null)
            {
                try
                {
                    Aircraft ac = GearAircraft.GetValue(gear) as Aircraft;
                    if (ac != null)
                        return ac;
                }
                catch { }
            }
            try { return gear.GetComponentInParent<Aircraft>(); }
            catch { return null; }
        }

        private static void PruneGearKind()
        {
            GearKind.Clear();
        }

        private static void Prune()
        {
            List<int> dead = new List<int>();
            Dictionary<int, State>.Enumerator e = States.GetEnumerator();
            while (e.MoveNext())
            {
                State st = e.Current.Value;
                if (st == null || st.gearMesh == null)
                    dead.Add(e.Current.Key);
            }
            for (int i = 0; i < dead.Count; i++)
                States.Remove(dead[i]);
        }

        private static void HidePlaceholders(Transform root)
        {
            if (root == null)
                return;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null)
                    continue;
                string n = t.name;
                if (n != "wheel" && n != "strut")
                    continue;
                MeshRenderer mr = t.GetComponent<MeshRenderer>();
                if (mr != null)
                    mr.enabled = false;
            }
        }

        private static Renderer FindNamedRenderer(Transform root, string name)
        {
            if (root == null)
                return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t.name != name)
                    continue;
                Renderer r = t.GetComponent<Renderer>();
                if (r != null)
                    return r;
            }
            return null;
        }
    }
}
