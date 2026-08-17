using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Soft AI combat tuning: skill/bravery floors, targeting opportunity, intercept
    /// viability, faster missile react. Never touches local human pilots.
    /// </summary>
    internal static class AiCombatSmarts
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _opposingOnly;
        private static ConfigEntry<float> _skillBonus;
        private static ConfigEntry<float> _braveryBonus;
        private static ConfigEntry<float> _minSkill;
        private static ConfigEntry<float> _opportunityMul;
        private static ConfigEntry<float> _interceptMul;
        private static ConfigEntry<float> _missileReactScale;
        private static ConfigEntry<bool> _preferPlayerTarget;

        private static FieldInfo _missileReactTimeField;
        private static FieldInfo _aircraftField;
        private static bool _fieldsResolved;

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("AiSmarts", "Enabled", false,
                "Legacy soft AI tweaks. Off by default — use AiBrain (Career Profile) full combat brain instead.");
            _opposingOnly = config.Bind("AiSmarts", "OpposingOnly", true,
                "If true, only boost AI opposing the local player's faction (friendly AI stay stock).");
            _skillBonus = config.Bind("AiSmarts", "SkillBonus", 0.12f,
                "Added to AI aircraft.skill (0-1 scale). Mild default.");
            _braveryBonus = config.Bind("AiSmarts", "BraveryBonus", 0.10f,
                "Added to AI aircraft.bravery (0-1 scale).");
            _minSkill = config.Bind("AiSmarts", "MinSkill", 0.38f,
                "Floor for boosted AI skill after bonus.");
            _opportunityMul = config.Bind("AiSmarts", "OpportunityMul", 1.12f,
                "Multiply CombatAI.AnalyzeTarget opportunity for boosted AI.");
            _interceptMul = config.Bind("AiSmarts", "InterceptMul", 1.10f,
                "Multiply CombatAI.InterceptViability for boosted AI.");
            _missileReactScale = config.Bind("AiSmarts", "MissileReactScale", 0.45f,
                "Scale AI missileReactTime after alert (lower = reacts sooner). Applied to all non-player AI.");
            _preferPlayerTarget = config.Bind("AiSmarts", "PreferPlayerTarget", true,
                "When ranking missile targets, prefer the local player's aircraft.");
            // Migrate older slow default
            if (_missileReactScale != null && Mathf.Abs(_missileReactScale.Value - 0.72f) < 0.01f)
                _missileReactScale.Value = 0.45f;
        }

        private static bool Enabled
        {
            get
            {
                // Full brain already replaces FixedUpdate — skip soft stacking.
                if (_enabled == null || !_enabled.Value)
                    return false;
                return true;
            }
        }

        /// <summary>Missile-react scaling is always on for AI (independent of legacy AiSmarts).</summary>
        private static bool MissileReactEnabled
        {
            get { return _missileReactScale != null; }
        }

        private static void ResolveFields()
        {
            if (_fieldsResolved)
                return;
            _fieldsResolved = true;
            try
            {
                _missileReactTimeField = AccessTools.Field(typeof(AIPilotCombatModes), "missileReactTime");
                _aircraftField = AccessTools.Field(typeof(PilotBaseState), "aircraft");
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("AiSmarts field resolve: " + ex.Message);
            }
        }

        private static bool IsLocalHumanAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.Player != null && Plugin.IsLocalHumanPlayer(ac.Player))
                    return true;
            }
            catch { }
            try
            {
                if (ac.pilots != null)
                {
                    for (int i = 0; i < ac.pilots.Length; i++)
                    {
                        Pilot p = ac.pilots[i];
                        if (p != null && p.playerControlled)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsOpposingLocal(Aircraft ac)
        {
            if (ac == null)
                return false;
            if (_opposingOnly == null || !_opposingOnly.Value)
                return true;
            try
            {
                Aircraft local;
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return true;
                FactionHQ myHq = local.NetworkHQ;
                FactionHQ theirHq = ac.NetworkHQ;
                if (myHq == null || theirHq == null)
                    return true;
                return !object.ReferenceEquals(myHq, theirHq);
            }
            catch { return true; }
        }

        internal static bool ShouldBoost(Aircraft ac)
        {
            if (!Enabled || ac == null)
                return false;
            if (IsLocalHumanAircraft(ac))
                return false;
            return IsOpposingLocal(ac);
        }

        private static void ApplySkillBravery(Aircraft ac)
        {
            if (!ShouldBoost(ac))
                return;
            float skillAdd = _skillBonus != null ? _skillBonus.Value : 0.12f;
            float bravAdd = _braveryBonus != null ? _braveryBonus.Value : 0.10f;
            float minSk = _minSkill != null ? _minSkill.Value : 0.38f;
            AiCombatMathService.ApplySkillBonus(ac, skillAdd, bravAdd, minSk);
        }

        private static Aircraft AircraftFromCombat(AIPilotCombatModes state)
        {
            ResolveFields();
            if (state == null || _aircraftField == null)
                return null;
            try { return _aircraftField.GetValue(state) as Aircraft; }
            catch { return null; }
        }

        private static void ScaleMissileReact(AIPilotCombatModes state)
        {
            ResolveFields();
            if (state == null || _missileReactTimeField == null)
                return;
            float scale = _missileReactScale != null ? _missileReactScale.Value : 0.45f;
            try
            {
                float t = (float)_missileReactTimeField.GetValue(state);
                float scaled = AiCombatMathService.ScaleMissileReactTime(t, scale);
                if (scaled != t)
                    _missileReactTimeField.SetValue(state, scaled);
            }
            catch { }
        }

        private static bool IsNonPlayerAi(Aircraft ac)
        {
            return ac != null && !IsLocalHumanAircraft(ac);
        }

        private static void PreferLocalPlayerInList(List<Unit> outTargets)
        {
            if (_preferPlayerTarget == null || !_preferPlayerTarget.Value)
                return;
            if (outTargets == null || outTargets.Count < 2)
                return;
            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { }
            if (local == null)
                return;
            int idx = -1;
            for (int i = 0; i < outTargets.Count; i++)
            {
                if (object.ReferenceEquals(outTargets[i], local))
                {
                    idx = i;
                    break;
                }
            }
            if (idx <= 0)
                return;
            Unit tmp = outTargets[0];
            outTargets[0] = outTargets[idx];
            outTargets[idx] = tmp;
        }

        // ——— Harmony ———

        [HarmonyPatch(typeof(Spawner), "SpawnAircraft")]
        private static class Patch_SpawnAircraft_AiSmarts
        {
            [HarmonyPostfix]
            private static void Postfix(Aircraft __result)
            {
                if (__result == null || !Enabled)
                    return;
                // ShouldBoost skips local human via Aircraft.Player / playerControlled.
                ApplySkillBravery(__result);
            }
        }

        [HarmonyPatch(typeof(AIPilotCombatModes), "EnterState")]
        private static class Patch_CombatEnter_AiSmarts
        {
            [HarmonyPostfix]
            private static void Postfix(AIPilotCombatModes __instance)
            {
                Aircraft ac = AircraftFromCombat(__instance);
                ApplySkillBravery(ac);
            }
        }

        [HarmonyPatch(typeof(CombatAI), "AnalyzeTarget")]
        private static class Patch_AnalyzeTarget_AiSmarts
        {
            [HarmonyPostfix]
            private static void Postfix(ref OpportunityThreat __result, Unit analyzer)
            {
                Aircraft ac = analyzer as Aircraft;
                if (!ShouldBoost(ac))
                    return;
                float mul = _opportunityMul != null ? Mathf.Clamp(_opportunityMul.Value, 1f, 1.5f) : 1.12f;
                if (mul <= 1.001f)
                    return;
                try
                {
                    __result = new OpportunityThreat(__result.opportunity * mul, __result.threat);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(CombatAI), "InterceptViability")]
        private static class Patch_Intercept_AiSmarts
        {
            [HarmonyPostfix]
            private static void Postfix(ref float __result, Unit analyzer)
            {
                Aircraft ac = analyzer as Aircraft;
                if (!ShouldBoost(ac))
                    return;
                float mul = _interceptMul != null ? Mathf.Clamp(_interceptMul.Value, 1f, 1.5f) : 1.10f;
                if (mul > 1.001f)
                    __result *= mul;
            }
        }

        [HarmonyPatch(typeof(CombatAI), "LookForMissileTargets")]
        private static class Patch_MissileTargets_AiSmarts
        {
            [HarmonyPostfix]
            private static void Postfix(Aircraft aircraft, List<Unit> outTargets)
            {
                if (!ShouldBoost(aircraft))
                    return;
                PreferLocalPlayerInList(outTargets);
            }
        }

        [HarmonyPatch(typeof(AIPilotCombatModes), "AICombat_OnMissileAlert")]
        private static class Patch_MissileAlert_AiSmarts
        {
            [HarmonyPostfix]
            private static void Postfix(AIPilotCombatModes __instance)
            {
                if (!MissileReactEnabled)
                    return;
                Aircraft ac = AircraftFromCombat(__instance);
                if (!IsNonPlayerAi(ac))
                    return;
                ScaleMissileReact(__instance);
            }
        }

        [HarmonyPatch(typeof(AIPilotCombatModes), "AICombat_OnRadarWarning")]
        private static class Patch_RadarWarn_AiSmarts
        {
            [HarmonyPostfix]
            private static void Postfix(AIPilotCombatModes __instance)
            {
                if (!MissileReactEnabled)
                    return;
                Aircraft ac = AircraftFromCombat(__instance);
                if (!IsNonPlayerAi(ac))
                    return;
                ScaleMissileReact(__instance);
            }
        }

    }
}
