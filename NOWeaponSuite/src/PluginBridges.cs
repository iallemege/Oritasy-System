using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Satellite APIs expected by AgmT / overpen / proximity after Plugin.cs
    /// was restored from an older snapshot. Same partial type as Plugin.
    /// </summary>
    public partial class Plugin
    {
        internal static ConfigEntry<bool> EnableMissileOverpen;
        internal static ConfigEntry<float> OverpenMinAge;
        internal static ConfigEntry<float> OverpenSphereRadius;
        internal static ConfigEntry<int> OverpenMaxHits;
        internal static ConfigEntry<float> OverpenKineticScale;
        internal static ConfigEntry<float> OverpenSpeedKeep;

        internal static void StartHosted(BaseUnityPlugin host, ManualLogSource log, ConfigFile config)
        {
            if (host == null || config == null)
                return;
            if (Log == null)
                Log = log != null ? log : BepInEx.Logging.Logger.CreateLogSource(PluginInfo.Name);
            else if (log != null)
                Log = log;
            RunInit(config);
        }

        internal static void HostedTick()
        {
            if (OritasyMenuIdle())
            {
                PlayerCareer.Tick();
                return;
            }
            RunUpdateBody();
            try { AgmTDispenser.PumpAll(); }
            catch { }
            try { AgmTSubBrain.PumpAll(); }
            catch { }
        }

        internal static void DrawHostedGui()
        {
            RunGuiBody();
        }

        internal static void BindOverpen(ConfigFile config)
        {
            if (config == null)
                return;
            EnableMissileOverpen = config.Bind("Missile", "EnableMissileOverpen", true,
                "Kinetic overpen when high-speed missiles tunnel through thin colliders.");
            OverpenMinAge = config.Bind("Missile", "OverpenMinAge",
                MissileOverpenMathService.DefaultMinAge, "Seconds after spawn before overpen is armed.");
            OverpenSphereRadius = config.Bind("Missile", "OverpenSphereRadius",
                MissileOverpenMathService.DefaultSphereRadius, "SphereCast radius (m).");
            OverpenMaxHits = config.Bind("Missile", "OverpenMaxHits",
                MissileOverpenMathService.DefaultMaxHits, "Max kinetic hits before forced detonate.");
            OverpenKineticScale = config.Bind("Missile", "OverpenKineticScale",
                MissileOverpenMathService.DefaultKineticScale, "mass * speed * scale.");
            OverpenSpeedKeep = config.Bind("Missile", "OverpenSpeedKeep",
                MissileOverpenMathService.DefaultSpeedKeep, "Speed kept after a non-fatal overpen (0-1).");
        }

        internal static T TryAddBehaviour<T>(GameObject go) where T : MonoBehaviour
        {
            if (go == null)
                return null;
            try
            {
                T existing = go.GetComponent<T>();
                if (existing != null)
                    return existing;
                return go.AddComponent<T>();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("AddComponent " + typeof(T).Name + ": " + ex.Message);
                return null;
            }
        }

        internal static void ApplyAam2CvNukeWarhead(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                float y = Aam2CvWeapon.NukeYield5kt;
                if (BlastYieldField != null)
                    BlastYieldField.SetValue(missile, y);
                if (PierceDamage != null && PierceDamage.Value >= 0f && PierceField != null)
                    PierceField.SetValue(missile, PierceDamage.Value);
                ApplyWarheadFx(missile);
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("ApplyAam2CvNukeWarhead: " + ex.Message);
            }
        }

        internal static bool IsRocketOrUnguidedMissile(Missile missile)
        {
            if (missile == null)
                return false;
            string jsonKey = null;
            string unitName = null;
            string defName = null;
            try
            {
                if (missile.definition != null)
                {
                    jsonKey = missile.definition.jsonKey;
                    unitName = missile.definition.unitName;
                    defName = missile.definition.name;
                }
            }
            catch { }
            return MissileClassifyGateService.IsRocketOrUnguidedMissile(
                missile.name, jsonKey, unitName, defName);
        }

        internal static bool IsScannerReconMissile(Missile missile)
        {
            if (missile == null)
                return false;
            if (MissileClassifyGateService.IsScannerReconName(missile.name))
                return true;
            try
            {
                if (missile.definition != null)
                {
                    if (MissileClassifyGateService.IsScannerReconName(missile.definition.jsonKey))
                        return true;
                    if (MissileClassifyGateService.IsScannerReconName(missile.definition.unitName))
                        return true;
                    if (MissileClassifyGateService.IsScannerReconName(missile.definition.name))
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool IsSamRadarFamilyMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.definition != null
                    && MissileClassifyGateService.IsSamRadarKey(missile.definition.jsonKey))
                    return true;
            }
            catch { }
            try
            {
                if (MissileClassifyGateService.LooksLikeNavalExclusiveName(missile.name))
                    return true;
                if (missile.definition != null
                    && MissileClassifyGateService.LooksLikeNavalExclusiveName(missile.definition.unitName))
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Pack missiles (TGM-85 / ACM-119 / AAM-2CV) identified by jsonKey/name only.
        /// Do not call AgmTWeapon here — VeyrnWeapons used to Harmony-skip those
        /// and MultiMode then GuideTo'd a still-AGM donor (vanish).
        /// </summary>
        internal static bool LooksLikePackMissile(Missile missile)
        {
            if (missile == null)
                return false;
            return LooksLikeKh85Missile(missile)
                || LooksLikeAgmTMissile(missile)
                || LooksLikeAam2CvMissile(missile);
        }

        internal static bool LooksLikeKh85Missile(Missile missile)
        {
            return LooksLikeKh85Blob(MissileIdentityBlob(missile));
        }

        internal static bool LooksLikeAgmTMissile(Missile missile)
        {
            return LooksLikeAgmTBlob(MissileIdentityBlob(missile));
        }

        internal static bool LooksLikeAam2CvMissile(Missile missile)
        {
            return LooksLikeAam2CvBlob(MissileIdentityBlob(missile));
        }

        private static string MissileIdentityBlob(Missile missile)
        {
            if (missile == null)
                return string.Empty;
            string blob = missile.name != null ? missile.name : string.Empty;
            try
            {
                if (missile.definition != null)
                {
                    if (!string.IsNullOrEmpty(missile.definition.jsonKey))
                        blob = blob + " " + missile.definition.jsonKey;
                    if (!string.IsNullOrEmpty(missile.definition.unitName))
                        blob = blob + " " + missile.definition.unitName;
                    if (!string.IsNullOrEmpty(missile.definition.name))
                        blob = blob + " " + missile.definition.name;
                }
            }
            catch { }
            try
            {
                WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                if (info != null)
                {
                    if (!string.IsNullOrEmpty(info.weaponName))
                        blob = blob + " " + info.weaponName;
                    if (!string.IsNullOrEmpty(info.shortName))
                        blob = blob + " " + info.shortName;
                    if (!string.IsNullOrEmpty(info.name))
                        blob = blob + " " + info.name;
                }
            }
            catch { }
            try
            {
                string n = missile.NetworkunitName;
                if (!string.IsNullOrEmpty(n))
                    blob = blob + " " + n;
            }
            catch { }
            return blob;
        }

        internal static bool LooksLikeKh85Blob(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return false;
            return blob.IndexOf("Kh85MT", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("TGM-85", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("TGM85", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("Shardfall", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool LooksLikeAgmTBlob(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return false;
            return blob.IndexOf("ACM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("ACM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("ACNM_118", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AGM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AGM_T", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool LooksLikeAam2CvBlob(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return false;
            return blob.IndexOf("AAM2CV", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AAM-2CV", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AAM_2CV", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool LooksLikeVeyrnPackBlob(string blob)
        {
            return LooksLikeKh85Blob(blob) || LooksLikeAgmTBlob(blob) || LooksLikeAam2CvBlob(blob);
        }

        internal static bool LooksLikePulsarBlob(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return false;
            return blob.IndexOf("Pulse250", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AAM-250", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AAM_250", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool LooksLikeForeignPackBlob(string blob)
        {
            return LooksLikeVeyrnPackBlob(blob) || LooksLikePulsarBlob(blob);
        }

        internal static string WeaponInfoBlob(WeaponInfo info)
        {
            if (info == null)
                return string.Empty;
            return ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty));
        }

        internal static bool LooksLikeForeignPackInfo(WeaponInfo info)
        {
            return LooksLikeForeignPackBlob(WeaponInfoBlob(info));
        }

        internal static bool LooksLikeForeignPackMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            string blob = ((mount.jsonKey != null ? mount.jsonKey : string.Empty) + " "
                + (mount.name != null ? mount.name : string.Empty) + " "
                + (mount.mountName != null ? mount.mountName : string.Empty));
            if (LooksLikeForeignPackBlob(blob))
                return true;
            return LooksLikeForeignPackInfo(mount.info);
        }

        internal static bool LooksLikeForeignPackDef(MissileDefinition def)
        {
            if (def == null)
                return false;
            string blob = ((def.jsonKey != null ? def.jsonKey : string.Empty) + " "
                + (def.unitName != null ? def.unitName : string.Empty) + " "
                + (def.name != null ? def.name : string.Empty));
            return LooksLikeForeignPackBlob(blob);
        }

        internal static bool IsKh85Family(Missile missile)
        {
            return LooksLikeKh85Missile(missile);
        }

        internal static bool IsTgm85CNuclearMissile(Missile missile)
        {
            if (!LooksLikeKh85Missile(missile))
                return false;
            try
            {
                string blob = MissileIdentityBlob(missile);
                if (blob.IndexOf("10kt", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("TGM-85C", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("Shardfall", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("Kh85MTC", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }
            return true;
        }

        internal static Unit ResolveHardLockTarget(Missile missile)
        {
            return ResolveDesignatedTarget(missile);
        }

        internal static bool IsAgmTEngageTarget(Missile missile, Unit candidate)
        {
            if (missile == null || candidate == null)
                return false;
            if (object.ReferenceEquals(candidate, missile)
                || object.ReferenceEquals(candidate, missile.owner))
                return false;
            if (IsEjectedPilotUnit(candidate))
                return false;
            if (!IsUnitAlive(candidate))
                return false;
            if (candidate is Scenery || candidate is Missile)
                return false;
            if (IsJunkHuntTarget(candidate))
                return false;
            return IsAllowedTarget(ResolveShooterSide(missile), candidate);
        }

        internal static float EffectiveHuntRadius(Missile missile)
        {
            float configured = MadModeSearchRadius != null
                ? MadModeSearchRadius.Value
                : HuntRangeGateService.FloorM;
            if (missile != null && AgmTWeapon.IsAgmTMissile(missile) && AgmTSearchRadius != null)
                configured = AgmTSearchRadius.Value;
            return HuntRangeGateService.ResolveHuntRadiusM(configured, 0f);
        }

        internal static bool IsJunkHuntTarget(Unit unit)
        {
            if (unit == null)
                return true;
            string blob = unit.name != null ? unit.name : string.Empty;
            try
            {
                if (unit.definition != null)
                {
                    if (!string.IsNullOrEmpty(unit.definition.unitName))
                        blob = blob + " " + unit.definition.unitName;
                    if (!string.IsNullOrEmpty(unit.definition.jsonKey))
                        blob = blob + " " + unit.definition.jsonKey;
                    if (!string.IsNullOrEmpty(unit.definition.name))
                        blob = blob + " " + unit.definition.name;
                }
            }
            catch { }

            int buildingType = -1;
            if (unit is Building)
            {
                try
                {
                    FieldInfo bt = AccessTools.Field(unit.GetType(), "buildingType");
                    if (bt != null)
                        buildingType = Convert.ToInt32(bt.GetValue(unit));
                }
                catch { buildingType = -1; }
            }
            float valueM = -1f;
            try
            {
                if (unit.definition != null)
                    valueM = unit.definition.value;
            }
            catch { }
            if (unit is Building)
                return HuntJunkGateService.IsJunkHuntBuilding(buildingType, valueM, blob);
            return HuntJunkGateService.IsJunkHuntName(blob);
        }
    }
}
