using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Radar / ECM overhaul ported from RadarWarfareOverhaul (features 1–6).
    /// Numbers are 2× that mod; cone / notch angles are +15°.
    /// No generator buff, no extra RWR HUD.
    /// </summary>
    internal static class RadarWarfare
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> JammerRefRange;
        internal static ConfigEntry<float> JammerMaxIntensity;
        internal static ConfigEntry<float> JammerConeHalfAngle;
        internal static ConfigEntry<float> JammerLobeMinFactor;
        internal static ConfigEntry<float> JammerPowerDraw;
        internal static ConfigEntry<float> JammerBaseIntensity;
        internal static ConfigEntry<float> DeceptionFactor;
        internal static ConfigEntry<bool> AutoDeception;
        internal static ConfigEntry<float> DeceptionRangeKm;
        internal static ConfigEntry<bool> FriendlyEcmExempt;
        internal static ConfigEntry<float> ChaffLifetime;
        internal static ConfigEntry<float> ChaffEffectiveTime;
        internal static ConfigEntry<float> ChaffDazzlePower;
        internal static ConfigEntry<float> InsDriftRate;
        internal static ConfigEntry<float> InsDriftLostMult;
        internal static ConfigEntry<float> SarhMemoryDrift;
        internal static ConfigEntry<float> DatalinkErrorGrowth;
        internal static ConfigEntry<bool> PodAreaJam;
        internal static ConfigEntry<float> PodConeHalfAngle;
        internal static ConfigEntry<float> PodMaxRangeKm;
        internal static ConfigEntry<bool> PodJamMissiles;
        internal static ConfigEntry<float> PodJamMissileRangeKm;
        internal static ConfigEntry<float> ArhEcmBlindThreshold;
        internal static ConfigEntry<bool> ArhHomeOnJamFromEcm;

        internal const float InsAlignAngle = 25f;
        internal const float InsTrackErrorCap = 0.4f;
        internal const float DatalinkAccurateM = 4000f;
        internal const float DatalinkErrorCapM = 3000f;
        internal const float LobeFadeAngle = 45f;

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            Enabled = config.Bind("RadarWarfare", "Enabled", true,
                "J/S ECM, hold-to-jam, short-window chaff, ARH HOJ, INS/datalink drift, pod area jam.");
            JammerRefRange = config.Bind("RadarWarfare", "JammerRefRange", 16000f,
                "J/S reference range (m). Burn-through is easier inside this range.");
            JammerMaxIntensity = config.Bind("RadarWarfare", "JammerMaxIntensity", 7f,
                "Cap on aircraft ecmIntensity.");
            JammerConeHalfAngle = config.Bind("RadarWarfare", "JammerConeHalfAngle", 85f,
                "Self-protection jammer tail-lobe half angle (deg). 0 = omni.");
            JammerLobeMinFactor = config.Bind("RadarWarfare", "JammerLobeMinFactor", 0.30f,
                "Residual jam outside the main lobe (0–1).");
            JammerPowerDraw = config.Bind("RadarWarfare", "JammerPowerDraw", 50f,
                "Continuous jammer power draw per frame.");
            JammerBaseIntensity = config.Bind("RadarWarfare", "JammerBaseIntensity", 4f,
                "Noise-mode ecmIntensity at full power.");
            DeceptionFactor = config.Bind("RadarWarfare", "DeceptionFactor", 4.4f,
                "Deception-mode intensity multiplier vs noise.");
            AutoDeception = config.Bind("RadarWarfare", "AutoDeception", true,
                "Switch to deception when a radar missile is inside DeceptionRangeKm.");
            DeceptionRangeKm = config.Bind("RadarWarfare", "DeceptionRangeKm", 28f,
                "Auto-deception trigger range (km).");
            FriendlyEcmExempt = config.Bind("RadarWarfare", "FriendlyEcmExempt", true,
                "Friendly radars / seekers ignore own-side ECM.");
            ChaffLifetime = config.Bind("RadarWarfare", "ChaffLifetime", 24f,
                "Chaff object lifetime (s).");
            ChaffEffectiveTime = config.Bind("RadarWarfare", "ChaffEffectiveTime", 4.4f,
                "Doppler-valid dazzle window after launch (s).");
            ChaffDazzlePower = config.Bind("RadarWarfare", "ChaffDazzlePower", 3.2f,
                "Chaff dazzle multiplier.");
            InsDriftRate = config.Bind("RadarWarfare", "InsDriftRate", 0.024f,
                "INS error accumulation per second.");
            InsDriftLostMult = config.Bind("RadarWarfare", "InsDriftLostMult", 5f,
                "INS drift multiplier when datalink / track is lost.");
            SarhMemoryDrift = config.Bind("RadarWarfare", "SarhMemoryDrift", 70f,
                "SARH coast-memory wander (m/s).");
            DatalinkErrorGrowth = config.Bind("RadarWarfare", "DatalinkErrorGrowth", 24f,
                "ARH midcourse datalink error growth (m/s), scaled by target ECM.");
            PodAreaJam = config.Bind("RadarWarfare", "PodAreaJam", true,
                "Jamming pod suppresses enemy radars in a forward cone.");
            PodConeHalfAngle = config.Bind("RadarWarfare", "PodConeHalfAngle", 70f,
                "Pod area-jam cone half angle (deg).");
            PodMaxRangeKm = config.Bind("RadarWarfare", "PodMaxRangeKm", 44f,
                "Pod area-jam range (km).");
            PodJamMissiles = config.Bind("RadarWarfare", "PodJamMissiles", true,
                "Pod also jams inbound radar missiles (HOJ still works).");
            PodJamMissileRangeKm = config.Bind("RadarWarfare", "PodJamMissileRangeKm", 50f,
                "Pod vs inbound missile range (km).");
            ArhEcmBlindThreshold = config.Bind("RadarWarfare", "ArhEcmBlindThreshold", 2f,
                "Target ecmIntensity that blinds an ARH seeker.");
            ArhHomeOnJamFromEcm = config.Bind("RadarWarfare", "ArhHomeOnJamFromEcm", true,
                "Self-protection ECM can trip ARH home-on-jam.");
            JammerQte.Bind(config);
        }

        internal static bool On()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static float F(ConfigEntry<float> entry, float fallback)
        {
            if (entry == null)
                return fallback;
            return entry.Value;
        }

        internal static bool B(ConfigEntry<bool> entry, bool fallback)
        {
            if (entry == null)
                return fallback;
            return entry.Value;
        }

        internal static float SuppressSignal(float signal, float ecm, float dist)
        {
            if (signal <= 0f || ecm <= 0.001f)
                return signal;
            ecm = Mathf.Min(ecm, F(JammerMaxIntensity, 7f));
            float ratio = dist / Mathf.Max(F(JammerRefRange, 16000f), 100f);
            float js = ecm * ratio * ratio;
            return signal / (1f + js);
        }

        internal static float DirectionalFactor(Vector3 aircraftForward, Vector3 fromAircraftToListener)
        {
            float half = F(JammerConeHalfAngle, 85f);
            if (half <= 0f)
                return 1f;
            if (fromAircraftToListener.sqrMagnitude < 0.0001f)
                return 1f;
            float angle = Vector3.Angle(-aircraftForward, fromAircraftToListener.normalized);
            float floor = Mathf.Clamp01(F(JammerLobeMinFactor, 0.30f));
            if (angle <= half)
                return 1f;
            float fade = Mathf.Max(LobeFadeAngle, half * 0.5f);
            float t = Mathf.Clamp01(1f - (angle - half) / fade);
            float s = t * t * (3f - 2f * t);
            return Mathf.Lerp(floor, 1f, s);
        }

        internal static bool SameHq(Unit a, Unit b)
        {
            if (a == null || b == null)
                return false;
            try
            {
                FactionHQ ha = a.NetworkHQ;
                FactionHQ hb = b.NetworkHQ;
                return ha != null && hb != null && object.ReferenceEquals(ha, hb);
            }
            catch
            {
                return false;
            }
        }

        internal static void JamUnit(Unit target, Unit source, float amount)
        {
            if (target == null || amount <= 0f)
                return;
            try
            {
                Unit.JamEventArgs args = default(Unit.JamEventArgs);
                args.jamAmount = amount;
                args.jammingUnit = source;
                target.Jam(args);
            }
            catch { }
        }

        internal static float ChaffDazzle(Missile missile, Unit target, RadarChaff chaff, float jamTol, float maxRange)
        {
            if (missile == null || target == null || chaff == null)
                return 0f;
            RwChaffData data = chaff.GetComponent<RwChaffData>();
            float age = 0f;
            if (data != null)
                age = Time.timeSinceLevelLoad - data.launchedAt;
            else
                return 0f;
            float window = Mathf.Max(F(ChaffEffectiveTime, 4.4f), 0.1f);
            if (age >= window)
                return 0f;
            float timeFactor = Mathf.Clamp01(1f - age / window);
            float dist = FastMath.Distance(missile.transform.position, target.transform.position);
            float rangeFactor = Mathf.Clamp01(1f - dist / Mathf.Max(maxRange, 1f));
            Vector3 toTarget = FastMath.NormalizedDirection(missile.transform.position, target.transform.position);
            Vector3 toChaff = FastMath.NormalizedDirection(missile.transform.position, chaff.transform.position);
            float ang = Mathf.Clamp01(1f - Mathf.Abs(Vector3.Dot(toTarget, toChaff)));
            return rangeFactor * ang * timeFactor / (1f + jamTol) * F(ChaffDazzlePower, 3.2f);
        }
    }

    internal enum RwEcmMode
    {
        Noise = 0,
        Deception = 1
    }

    internal class RwChaffData : MonoBehaviour
    {
        internal float launchedAt;
        internal Vector3 launchVelocity;
    }

    internal class RwContinuousJammer : MonoBehaviour
    {
        internal Aircraft aircraft;
        internal bool active;
        internal RwEcmMode mode;
        internal float currentEcm;
        internal PowerSupply powerSupply;
        private float _nextThreatAt;
        private bool _cachedThreat;

        internal static RwContinuousJammer Get(Aircraft ac)
        {
            if (ac == null)
                return null;
            return ac.GetComponent<RwContinuousJammer>();
        }

        internal static RwContinuousJammer Ensure(Aircraft ac)
        {
            if (ac == null)
                return null;
            RwContinuousJammer jam = ac.GetComponent<RwContinuousJammer>();
            if (jam == null)
                jam = ac.gameObject.AddComponent<RwContinuousJammer>();
            return jam;
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            if (aircraft != null)
                powerSupply = aircraft.GetPowerSupply();
        }

        internal void Activate()
        {
            if (aircraft == null || aircraft.disabled || active)
                return;
            active = true;
            mode = RwEcmMode.Noise;
        }

        internal void Deactivate()
        {
            if (!active)
                return;
            active = false;
            if (aircraft != null)
                aircraft.AddECMIntensity(-currentEcm);
            currentEcm = 0f;
        }

        internal void SetHold(bool holding)
        {
            if (holding)
                Activate();
            else
                Deactivate();
        }

        private void Update()
        {
            if (!active)
                return;
            if (aircraft == null || aircraft.disabled)
            {
                Deactivate();
                return;
            }
            if (powerSupply == null)
                powerSupply = aircraft.GetPowerSupply();
            if (powerSupply == null)
            {
                Deactivate();
                return;
            }
            float need = RadarWarfare.F(RadarWarfare.JammerPowerDraw, 50f);
            float got = powerSupply.DrawPower(need);
            float ratio = got / Mathf.Max(need, 0.1f);
            if (ratio < 0.05f)
            {
                Deactivate();
                return;
            }
            if (RadarWarfare.B(RadarWarfare.AutoDeception, true))
            {
                bool threat = HasMissileThreat(RadarWarfare.F(RadarWarfare.DeceptionRangeKm, 28f) * 1000f);
                if (threat && mode != RwEcmMode.Deception)
                    mode = RwEcmMode.Deception;
                else if (!threat && mode != RwEcmMode.Noise)
                    mode = RwEcmMode.Noise;
            }
            float factor = 1f;
            if (mode == RwEcmMode.Deception)
                factor = Mathf.Max(RadarWarfare.F(RadarWarfare.DeceptionFactor, 4.4f), 1f);
            float want = Mathf.Min(
                RadarWarfare.F(RadarWarfare.JammerBaseIntensity, 4f) * ratio * factor,
                RadarWarfare.F(RadarWarfare.JammerMaxIntensity, 7f));
            float delta = want - currentEcm;
            if (Mathf.Abs(delta) > 0.0005f)
            {
                aircraft.AddECMIntensity(delta);
                currentEcm = want;
            }
        }

        private bool HasMissileThreat(float range)
        {
            float now = Time.unscaledTime;
            if (now < _nextThreatAt)
                return _cachedThreat;
            _nextThreatAt = now + 0.2f;
            _cachedThreat = false;
            try
            {
                MissileWarning warn = aircraft.GetMissileWarningSystem();
                if (warn == null || warn.knownMissiles == null || warn.knownMissiles.Count == 0)
                    return false;
                for (int i = 0; i < warn.knownMissiles.Count; i++)
                {
                    Missile m = warn.knownMissiles[i];
                    if (m == null || m.disabled)
                        continue;
                    if (FastMath.Distance(GlobalPositionExtensions.GlobalPosition(m),
                            GlobalPositionExtensions.GlobalPosition(aircraft)) < range)
                    {
                        _cachedThreat = true;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void OnDestroy()
        {
            if (active && aircraft != null)
                aircraft.AddECMIntensity(-currentEcm);
            currentEcm = 0f;
            active = false;
        }
    }

    internal static class RwField
    {
        internal static FieldInfo Get(Type type, string name)
        {
            return AccessTools.Field(type, name);
        }

        internal static T Read<T>(FieldInfo field, object obj)
        {
            if (field == null || obj == null)
                return default(T);
            try
            {
                object v = field.GetValue(obj);
                if (v is T)
                    return (T)v;
            }
            catch { }
            return default(T);
        }

        internal static void Write<T>(FieldInfo field, object obj, T value)
        {
            if (field == null || obj == null)
                return;
            try { field.SetValue(obj, value); }
            catch { }
        }
    }
}
