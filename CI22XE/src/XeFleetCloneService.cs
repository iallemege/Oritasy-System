using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Clone vanilla AircraftDefinition (+ aircraftParameters) for each XE brand,
    /// bake envelope onto the clone only, then swap encyclopedia / hangar refs so
    /// the game uses clones. Vanilla ScriptableObjects stay vanilla.
    /// Pattern mirrors Kh85MT / WeXon ammo mount clones (Instantiate + Lookup / IndexLookup).
    /// </summary>
    internal static class XeFleetCloneService
    {
        private static readonly Dictionary<int, AircraftDefinition> VanillaIdToClone =
            new Dictionary<int, AircraftDefinition>();
        private static readonly Dictionary<int, AircraftDefinition> CloneIdToVanilla =
            new Dictionary<int, AircraftDefinition>();
        private static readonly HashSet<int> CloneIds = new HashSet<int>();
        private static readonly FieldInfo UnitDefinitionField = AccessTools.Field(typeof(Unit), "definition");
        private static readonly HashSet<int> CloneParamIds = new HashSet<int>();
        private static readonly HashSet<string> LoggedKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> HangarTouched = new HashSet<int>();

        private static readonly FieldInfo AvailableAircraftField =
            AccessTools.Field(typeof(Hangar), "availableAircraft");

        internal static bool IsCloneParams(AircraftParameters p)
        {
            return p != null && CloneParamIds.Contains(p.GetInstanceID());
        }

        internal static bool IsClone(AircraftDefinition def)
        {
            return def != null && CloneIds.Contains(def.GetInstanceID());
        }

        internal static bool TryGetClone(AircraftDefinition vanilla, out AircraftDefinition clone)
        {
            clone = null;
            if (vanilla == null)
                return false;
            if (IsClone(vanilla))
            {
                clone = vanilla;
                return true;
            }
            return VanillaIdToClone.TryGetValue(vanilla.GetInstanceID(), out clone) && clone != null;
        }

        /// <summary>Resolve the live XE definition (clone) for branding / UI paths.</summary>
        internal static AircraftDefinition ResolveLive(AircraftDefinition def)
        {
            if (def == null)
                return null;
            if (IsClone(def))
                return def;
            AircraftDefinition clone;
            if (TryGetClone(def, out clone))
                return clone;
            return def;
        }

        /// <summary>One-shot: clone every XE-brandable vanilla def, register, swap hangars.</summary>
        internal static void EnsureAll()
        {
            Encyclopedia enc = Plugin.GetEncyclopedia();

            // Prefer encyclopedia list (authoritative hangar/UI source), then Resources sweep.
            if (enc != null && enc.aircraft != null)
            {
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition src = enc.aircraft[i];
                    AircraftDefinition clone = EnsureClone(src, enc);
                    if (clone != null && !object.ReferenceEquals(src, clone))
                        enc.aircraft[i] = clone;
                }
            }

            AircraftDefinition[] all = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
            for (int i = 0; i < all.Length; i++)
                EnsureClone(all[i], enc);

            ReplaceAllHangarLists();
        }

        internal static AircraftDefinition EnsureClone(AircraftDefinition src, Encyclopedia enc)
        {
            if (src == null)
                return null;
            if (IsClone(src))
                return src;

            // Already cloned this vanilla instance?
            AircraftDefinition existing;
            if (VanillaIdToClone.TryGetValue(src.GetInstanceID(), out existing) && existing != null)
                return existing;

            // Skip defs that are not XE fleet targets.
            if (!AircraftIdentity.IsXeDefinition(src))
                return src;
            UnitBrandingService.XeBrand brand;
            if (!UnitBrandingService.TryResolveBrand(src, out brand))
                return src;

            AircraftDefinition clone = UnityEngine.Object.Instantiate(src);
            string srcName = src.name != null ? src.name : "AircraftDefinition";
            clone.name = "OritasyXe_" + srcName;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // Keep jsonKey identical so Lookup / loadouts / saves resolve to the clone.
            if (!string.IsNullOrEmpty(src.jsonKey))
                clone.jsonKey = src.jsonKey;

            AircraftParameters paramClone = null;
            if (src.aircraftParameters != null)
            {
                paramClone = UnityEngine.Object.Instantiate(src.aircraftParameters);
                paramClone.name = "OritasyXe_params_" + (src.aircraftParameters.name != null
                    ? src.aircraftParameters.name
                    : "AircraftParameters");
                paramClone.hideFlags = HideFlags.DontUnloadUnusedAsset;
                clone.aircraftParameters = paramClone;
                CloneParamIds.Add(paramClone.GetInstanceID());
            }

            UnitBrandingService.ApplyBrandFields(clone, brand);
            BakeEnvelopeOntoClone(clone);

            int vid = src.GetInstanceID();
            int cid = clone.GetInstanceID();
            VanillaIdToClone[vid] = clone;
            CloneIdToVanilla[cid] = src;
            CloneIds.Add(cid);

            RegisterInEncyclopedia(enc, clone, src);
            GameZhLocalizer.NoteBrandedAircraft(clone);

            string key = !string.IsNullOrEmpty(clone.jsonKey) ? clone.jsonKey : clone.name;
            if (LoggedKeys.Add(key) && Plugin.Log != null)
                Plugin.Log.LogInfo("XE clone: " + key + " -> " + clone.unitName);

            return clone;
        }

        private static void BakeEnvelopeOntoClone(AircraftDefinition clone)
        {
            if (clone == null || clone.aircraftParameters == null)
                return;

            // Prefer branded code/unitName (FS-12) over jsonKey (Fighter1).
            // Baking Fighter1 envelope then applying FS-12 FBW PitchMul=2.2 causes PIO.
            string key = null;
            string codeKey = !string.IsNullOrEmpty(clone.code) ? AircraftIdentity.StripXe(clone.code) : null;
            string nameKey = !string.IsNullOrEmpty(clone.unitName) ? AircraftIdentity.StripXe(clone.unitName) : null;
            string jsonKey = !string.IsNullOrEmpty(clone.jsonKey) ? AircraftIdentity.StripXe(clone.jsonKey) : null;
            if (AircraftIdentity.IsKnownFleet(codeKey))
                key = codeKey;
            else if (AircraftIdentity.IsKnownFleet(nameKey))
                key = nameKey;
            else if (!string.IsNullOrEmpty(jsonKey))
                key = jsonKey;
            else if (!string.IsNullOrEmpty(codeKey))
                key = codeKey;
            else if (!string.IsNullOrEmpty(nameKey))
                key = nameKey;
            if (string.IsNullOrEmpty(key))
                return;

            ManeuverProfile profile = null;
            if (Plugin.Profiles != null)
                Plugin.Profiles.TryGetValue(key, out profile);
            if (profile == null)
                return;

            float gAir = profile.AircraftG.Value;
            float maxSpd = profile.MaxSpeed.Value;
            float corner = Mathf.Min(profile.CornerSpeed.Value, maxSpd);
            AircraftParameters ap = clone.aircraftParameters;
            ap.aircraftGLimit = gAir;
            ap.maxSpeed = maxSpd;
            ap.cornerSpeed = corner;
            ap.approachSpeed = profile.ApproachSpeed.Value;
            ap.landingSpeed = profile.LandingSpeed.Value;
            ap.takeoffSpeed = profile.TakeoffSpeed.Value;
            ap.turningRadius = profile.TurningRadius.Value;
            if (!string.IsNullOrEmpty(clone.unitName))
                ap.aircraftName = clone.unitName;

            // aircraftInfo is not a UnityEngine.Object — only mutate maxSpeed when present.
            try
            {
                if (clone.aircraftInfo != null)
                    clone.aircraftInfo.maxSpeed = maxSpd;
            }
            catch { }
        }

        private static void RegisterInEncyclopedia(
            Encyclopedia enc, AircraftDefinition clone, AircraftDefinition vanilla)
        {
            if (clone == null)
                return;

            if (Encyclopedia.Lookup != null && !string.IsNullOrEmpty(clone.jsonKey))
                Encyclopedia.Lookup[clone.jsonKey] = clone;

            if (enc != null && enc.aircraft != null)
            {
                bool replaced = false;
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition cur = enc.aircraft[i];
                    if (cur == null)
                        continue;
                    if (object.ReferenceEquals(cur, vanilla)
                        || object.ReferenceEquals(cur, clone)
                        || (!string.IsNullOrEmpty(clone.jsonKey)
                            && string.Equals(cur.jsonKey, clone.jsonKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        enc.aircraft[i] = clone;
                        replaced = true;
                    }
                }
                if (!replaced)
                    enc.aircraft.Add(clone);
            }

            RegisterNetworkLookup(enc, clone);
        }

        private static void RegisterNetworkLookup(Encyclopedia enc, AircraftDefinition clone)
        {
            if (enc == null || clone == null || enc.IndexLookup == null)
                return;
            try
            {
                INetworkDefinition nd = clone as INetworkDefinition;
                if (nd == null)
                    return;
                int idx = enc.IndexLookup.IndexOf(nd);
                if (idx >= 0)
                {
                    nd.LookupIndex = idx;
                    return;
                }
                enc.IndexLookup.Add(nd);
                nd.LookupIndex = enc.IndexLookup.Count - 1;
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("XE IndexLookup: " + ex.Message);
            }
        }

        internal static void ReplaceAllHangarLists()
        {
            if (AvailableAircraftField == null)
                return;
            Hangar[] hangars = Resources.FindObjectsOfTypeAll<Hangar>();
            for (int i = 0; i < hangars.Length; i++)
                ReplaceHangarList(hangars[i]);
        }

        internal static void ReplaceHangarList(Hangar hangar)
        {
            if (hangar == null || AvailableAircraftField == null)
                return;
            if (!Plugin.IsRuntimeInstance(hangar) && hangar.gameObject != null)
            {
                // Still rewrite prefab/scene hangar templates so first open sees clones.
            }

            AircraftDefinition[] list = null;
            try { list = AvailableAircraftField.GetValue(hangar) as AircraftDefinition[]; }
            catch { return; }
            if (list == null || list.Length == 0)
                return;

            bool changed = false;
            AircraftDefinition[] next = new AircraftDefinition[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                AircraftDefinition d = list[i];
                AircraftDefinition live = ResolveLive(d);
                // If still vanilla brandable, try ensure now.
                if (live != null && !IsClone(live) && AircraftIdentity.IsXeDefinition(live))
                {
                    AircraftDefinition c = EnsureClone(live, Plugin.GetEncyclopedia());
                    if (c != null)
                        live = c;
                }
                next[i] = live != null ? live : d;
                if (!object.ReferenceEquals(next[i], d))
                    changed = true;
            }
            if (!changed)
                return;
            try { AvailableAircraftField.SetValue(hangar, next); }
            catch { }
            HangarTouched.Add(hangar.GetInstanceID());
        }

        /// <summary>True when def params are safe to mutate (clone only).</summary>
        
        /// <summary>
        /// Hangar/encyclopedia may still hand out vanilla defs at spawn.
        /// Swap live aircraft onto the XE clone and point ControlsFilter.aircraftParameters
        /// at the cloned params so FBW G/corner and aero envelope stay one object.
        /// Mismatch here (high PitchMul on FBW + vanilla CfParams) produces high-speed PIO.
        /// </summary>
        internal static AircraftDefinition BindAircraftToClone(Aircraft aircraft)
        {
            if (aircraft == null)
                return null;

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            if (def == null)
                return null;

            Encyclopedia enc = null;
            try { enc = Plugin.GetEncyclopedia(); } catch { }

            AircraftDefinition live = ResolveLive(def);
            if (live == null)
                live = def;

            if (!IsClone(live) && AircraftIdentity.IsXeDefinition(live))
            {
                AircraftDefinition c = EnsureClone(live, enc);
                if (c != null)
                    live = c;
            }

            if (IsClone(live) && !ReferenceEquals(aircraft.definition, live))
            {
                // Aircraft.definition is a get-only property; write Unit.definition field.
                try
                {
                    if (UnitDefinitionField != null)
                        UnitDefinitionField.SetValue(aircraft, live);
                }
                catch { }
            }

            // Always retarget CfParams onto the clone params when we have them.
            // This is the critical path for CI-22XE: even if definition assign is ignored,
            // FBW must share the same AircraftParameters object we bake/mutate.
            if (IsClone(live) && live.aircraftParameters != null)
            {
                try
                {
                    ControlsFilter filter = FlightEnvelopeService.GetControlsFilter(aircraft);
                    if (filter != null && EngineReflection.CfParams != null)
                    {
                        AircraftParameters cur = EngineReflection.CfParams.GetValue(filter) as AircraftParameters;
                        if (!ReferenceEquals(cur, live.aircraftParameters))
                            EngineReflection.CfParams.SetValue(filter, live.aircraftParameters);
                    }
                }
                catch { }
            }

            return live;
        }
        internal static bool CanMutateDefinitionParams(AircraftDefinition def)
        {
            return IsClone(def);
        }
    }

    /// <summary>Keep hangar availableAircraft pointing at XE clones on every query.</summary>
    [HarmonyPatch(typeof(Hangar), "GetAvailableAircraft")]
    internal static class Patch_Hangar_XeFleetClones
    {
        private static void Prefix(Hangar __instance)
        {
            XeFleetCloneService.ReplaceHangarList(__instance);
        }
    }
}
