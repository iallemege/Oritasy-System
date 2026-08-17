using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Kh85MT
{
    /// <summary>
    /// TGM-85B Torch onboard ECM: continuously jam nearby hostile aircraft radars/avionics.
    /// </summary>
    internal static class Kh85BEcm
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> JamAmount;
        internal static ConfigEntry<float> Radius;
        internal static ConfigEntry<float> PulseInterval;
        internal static ConfigEntry<float> ArmDelay;
        internal static ConfigEntry<float> EcmIntensity;

        private static readonly Collider[] OverlapBuf = new Collider[64];

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("EcmB", "Enabled", true,
                "TGM-85B: onboard ECM that jams nearby hostile aircraft.");
            JamAmount = config.Bind("EcmB", "JamAmount", 0.4f,
                "Jam amount applied per pulse to each nearby hostile aircraft (0–1 scale).");
            Radius = config.Bind("EcmB", "Radius", 6000f,
                "ECM radius (m) for jamming nearby aircraft.");
            PulseInterval = config.Bind("EcmB", "PulseInterval", 0.2f,
                "Seconds between ECM jam pulses.");
            ArmDelay = config.Bind("EcmB", "ArmDelay", 0.6f,
                "Seconds after launch before B ECM starts.");
            EcmIntensity = config.Bind("EcmB", "EcmIntensity", 1.2f,
                "Extra GetECMIntensity on the B missile itself while ECM is running.");
        }

        internal static bool IsEnabled()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static bool IsBVariant(Missile missile)
        {
            return Kh85Util.IsKh85(missile) && Kh85Util.GetVariant(missile) == "B";
        }

        internal static void TryAttach(Missile missile)
        {
            if (missile == null || !IsEnabled() || !IsBVariant(missile))
                return;
            if (missile.GetComponent<Kh85BEcmBrain>() != null)
                return;
            try { missile.gameObject.AddComponent<Kh85BEcmBrain>(); }
            catch { }
        }

        internal static float ActiveEcmBonus(Missile missile)
        {
            if (!IsEnabled() || !IsBVariant(missile))
                return 0f;
            Kh85BEcmBrain brain = missile.GetComponent<Kh85BEcmBrain>();
            if (brain == null || !brain.IsEcmActive())
                return 0f;
            return EcmIntensity != null ? EcmIntensity.Value : 1.2f;
        }

        internal static void Pulse(Missile self)
        {
            if (self == null)
                return;

            float jamAmt = JamAmount != null ? JamAmount.Value : 0.4f;
            if (jamAmt < 0.05f)
                jamAmt = 0.05f;
            float radius = Radius != null ? Radius.Value : 6000f;
            if (radius < 200f)
                radius = 200f;

            int hits = 0;
            try
            {
                hits = Physics.OverlapSphereNonAlloc(self.transform.position, radius, OverlapBuf,
                    ~0, QueryTriggerInteraction.Ignore);
            }
            catch { return; }

            HashSet<int> seen = null;
            for (int i = 0; i < hits; i++)
            {
                Collider c = OverlapBuf[i];
                if (c == null)
                    continue;
                Aircraft ac = null;
                try { ac = c.GetComponentInParent<Aircraft>(); }
                catch { }
                if (ac == null)
                    continue;
                if (seen == null)
                    seen = new HashSet<int>();
                if (!seen.Add(ac.GetInstanceID()))
                    continue;
                if (!IsHostile(self, ac))
                    continue;

                ApplyJam(ac, self, jamAmt);
            }
        }

        private static void ApplyJam(Unit victim, Unit jammer, float amount)
        {
            if (victim == null || jammer == null)
                return;
            try
            {
                Unit.JamEventArgs args = default(Unit.JamEventArgs);
                args.jammingUnit = jammer;
                args.jamAmount = amount;
                victim.Jam(args);
            }
            catch { }
        }

        private static bool IsHostile(Unit self, Unit other)
        {
            if (self == null || other == null)
                return false;
            try
            {
                FactionHQ a = self.NetworkHQ;
                FactionHQ b = other.NetworkHQ;
                if (a != null && b != null && a == b)
                    return false;
            }
            catch { }
            return true;
        }
    }

    public class Kh85BEcmBrain : MonoBehaviour
    {
        private Missile _missile;
        private float _nextPulse;
        private bool _armed;

        private void Awake()
        {
            _missile = GetComponent<Missile>();
        }

        internal bool IsEcmActive()
        {
            return _armed && Kh85BEcm.IsEnabled();
        }

        private void FixedUpdate()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null || !Kh85BEcm.IsEnabled())
                return;
            try
            {
                if (_missile.disabled)
                    return;
            }
            catch { }

            float arm = Kh85BEcm.ArmDelay != null ? Kh85BEcm.ArmDelay.Value : 0.6f;
            try
            {
                if (_missile.timeSinceSpawn < arm)
                    return;
            }
            catch { }

            _armed = true;
            float interval = Kh85BEcm.PulseInterval != null ? Kh85BEcm.PulseInterval.Value : 0.2f;
            if (interval < 0.05f)
                interval = 0.05f;
            if (Time.time < _nextPulse)
                return;
            _nextPulse = Time.time + interval;
            Kh85BEcm.Pulse(_missile);
        }
    }

    [HarmonyPatch(typeof(Missile), "GetECMIntensity")]
    internal static class Patch_Kh85B_GetECMIntensity
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref float __result)
        {
            float bonus = Kh85BEcm.ActiveEcmBonus(__instance);
            if (bonus > 0f)
                __result += bonus;
        }
    }
}
