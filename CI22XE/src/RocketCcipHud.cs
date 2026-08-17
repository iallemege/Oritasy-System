using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// First-person / chase rocket CCIP (Continuously Computed Impact Point)
    /// plus optional CEP (Circular Error Probable) dispersion ring.
    /// Ballistics mirror vanilla HUDBombingState + optional motor thrust from Missile prefab.
    /// Yields silently when standalone com.iallemege.rocketccip / com.qiaochen.rocketccip is loaded.
    /// </summary>
    internal static class RocketCcipHud
    {
        internal const string StandaloneGuid = "com.qiaochen.rocketccip";
        internal const string StandaloneGuidNew = "com.iallemege.rocketccip";

        private const int MaxSteps = 220;
        private const float StepDt = 0.05f;
        private const int TerrainMask = -8193;
        private const float MinForwardDot = 0.12f;
        private const int CepSegments = 16;
        private const float CepMinMeters = 3f;
        private const float CepMaxMeters = 400f;
        private const float CepRayleighToCep50 = 1.1774f;
        private const float ComputeHz = 20f;
        private const float ComputeInterval = 1f / ComputeHz;

        private static readonly FieldInfo WeaponMountField =
            AccessTools.Field(typeof(Weapon), "mount");
        private static readonly FieldInfo EjectionField =
            AccessTools.Field(typeof(MissileLauncher), "ejectionVelocity");
        private static readonly FieldInfo CircularErrorField =
            AccessTools.Field(typeof(BallisticMissileGuidance), "circularError");
        private static readonly FieldInfo LaunchTransformsField =
            AccessTools.Field(typeof(MissileLauncher), "launchTransforms");

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _cepEnabled;
        private static ConfigEntry<float> _cepScale;
        private static ConfigEntry<float> _cepBaselineMeters;
        private static ConfigEntry<bool> _cepLabel;
        private static GUIStyle _labelStyle;

        private static bool _hasImpact;
        private static Vector3 _impactWorld;
        private static Vector3 _impactSmoothed;
        private static float _timeOfFlight;
        private static float _cepMeters;
        private static Vector3 _muzzleWorld;
        private static bool _ballisticsReady;
        private static float _cachedDragCoef;
        private static float _cachedFinArea;
        private static float _cachedMass;
        private static float _cachedThrust;
        private static float _cachedBurn;
        private static float _cachedMuzzle;
        private static float _cachedGravMult = 1f;
        private static float _cachedCircularError;
        private static Vector3 _cachedEjection;
        private static WeaponInfo _cachedInfo;
        private static int _cachedStationNumber = -1;

        private static float _nextComputeTime;
        private static string _labelToF = "";
        private static string _labelCep = "";
        private static float _labelToFBucket = -999f;
        private static float _labelCepBucket = -999f;

        private static bool _standaloneChecked;
        private static bool _standalonePresent;

        internal static void Bind(ConfigFile config)
        {
            _enabled = config.Bind("Presentation", "RocketCcip", true,
                "Show rocket CCIP pipper in cockpit / chase / orbit when rockets are selected and ready.");
            _cepEnabled = config.Bind("Presentation", "RocketCcipCep", true,
                "Show CEP (Circular Error Probable) dispersion ring around rocket CCIP pipper.");
            _cepScale = config.Bind("Presentation", "RocketCcipCepScale", 1f,
                "Multiplier applied to estimated / weapon CEP radius (1 = nominal).");
            _cepBaselineMeters = config.Bind("Presentation", "RocketCcipCepBaselineMeters", 8f,
                "Baseline CEP radius in meters added before ToF/range growth (estimate mode).");
            _cepLabel = config.Bind("Presentation", "RocketCcipCepLabel", true,
                "Show small 'CEP ~Xm' label near the dispersion ring.");
        }

        internal static ConfigEntry<bool> Enabled
        {
            get { return _enabled; }
        }

        internal static ConfigEntry<bool> CepEnabled
        {
            get { return _cepEnabled; }
        }

        /// <summary>True when standalone Rocket CCIP plugin is loaded (Oritasy yields).</summary>
        internal static bool StandalonePresent
        {
            get { return IsStandaloneLoaded(); }
        }

        private static bool IsStandaloneLoaded()
        {
            if (_standaloneChecked)
                return _standalonePresent;
            _standaloneChecked = true;
            _standalonePresent = false;
            try
            {
                if (Chainloader.PluginInfos != null
                    && (Chainloader.PluginInfos.ContainsKey(StandaloneGuid)
                        || Chainloader.PluginInfos.ContainsKey(StandaloneGuidNew)))
                    _standalonePresent = true;
            }
            catch { }
            return _standalonePresent;
        }

        internal static void Tick()
        {
            if (IsStandaloneLoaded())
            {
                _hasImpact = false;
                return;
            }
            if (_enabled == null || !_enabled.Value)
            {
                _hasImpact = false;
                return;
            }
            if (MissileCameraHud.ManualActive
                || AircraftManeuverGui.IsOpen
                || OritasyPresentation.BlocksHud)
            {
                _hasImpact = false;
                return;
            }
            if (!IsAircraftFpOrChaseView())
            {
                _hasImpact = false;
                return;
            }

            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            if (ac == null || ac.disabled)
            {
                _hasImpact = false;
                return;
            }

            WeaponStation station = null;
            try
            {
                if (ac.weaponManager != null)
                    station = ac.weaponManager.currentWeaponStation;
            }
            catch { }
            if (station == null || station.WeaponInfo == null
                || !IsRocketStation(station) || station.Ammo <= 0)
            {
                _hasImpact = false;
                return;
            }
            try
            {
                if (station.SafetyIsOn(ac))
                {
                    _hasImpact = false;
                    return;
                }
            }
            catch { }

            // Cheap path between ballistic integrates: keep last impact for screen project.
            float now = Time.unscaledTime;
            if (now < _nextComputeTime)
                return;
            _nextComputeTime = now + ComputeInterval;

            EnsureBallistics(station);
            ComputeImpact(ac, station);
            if (_hasImpact)
            {
                _cepMeters = EstimateCepMeters();
                RefreshLabels();
            }
            else
            {
                _cepMeters = 0f;
            }
        }

        internal static void Draw()
        {
            if (IsStandaloneLoaded())
                return;
            if (!_hasImpact)
                return;
            if (_enabled == null || !_enabled.Value)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            if (MissileCameraHud.ManualActive
                || AircraftManeuverGui.IsOpen
                || OritasyPresentation.BlocksHud)
                return;
            if (!IsAircraftFpOrChaseView())
                return;

            Camera cam = ResolveViewCamera();
            if (cam == null)
                return;

            Vector3 world = _impactSmoothed;
            Vector3 toImpact = world - cam.transform.position;
            if (toImpact.sqrMagnitude < 1f)
                return;
            if (Vector3.Dot(toImpact.normalized, cam.transform.forward) < MinForwardDot)
                return;

            Vector3 sp;
            try { sp = cam.WorldToScreenPoint(world); }
            catch { return; }
            if (sp.z <= 0.05f)
                return;

            float x = UiScaleService.FromScreenX(sp.x);
            float y = UiScaleService.FromScreenYFlipped(sp.y);
            if (x < 4f || x > UiScaleService.Width - 4f || y < 4f || y > UiScaleService.Height - 4f)
                return;

            Color pip = new Color(0.35f, 1f, 0.45f, 0.95f);
            Color line = new Color(0.3f, 0.85f, 0.4f, 0.55f);
            Color cepCol = new Color(0.28f, 0.78f, 0.38f, 0.55f);
            DrawFallLine(cam, x, y, line);
            DrawPipper(x, y, pip);

            bool showCep = _cepEnabled != null && _cepEnabled.Value && _cepMeters > 0.5f;
            if (showCep)
                DrawCepRing(cam, world, _cepMeters, cepCol);

            EnsureStyles();
            Color prev = GUI.color;
            GUI.color = new Color(0.75f, 0.95f, 0.8f, 0.9f);
            GUI.Label(new Rect(x + 14f, y + 10f, 160f, 18f), _labelToF, _labelStyle);

            if (showCep && _cepLabel != null && _cepLabel.Value)
            {
                GUI.color = new Color(0.55f, 0.82f, 0.6f, 0.75f);
                GUI.Label(new Rect(x + 14f, y + 26f, 140f, 16f), _labelCep, _labelStyle);
            }
            GUI.color = prev;
        }

        private static void RefreshLabels()
        {
            float tofBucket = Mathf.Floor(_timeOfFlight * 10f) * 0.1f;
            if (tofBucket != _labelToFBucket)
            {
                _labelToFBucket = tofBucket;
                _labelToF = UiLang.IsChinese
                    ? ("CCIP  飞行时间 " + tofBucket.ToString("0.0") + "秒")
                    : ("CCIP  ToF " + tofBucket.ToString("0.0") + "s");
            }

            float cepBucket;
            if (_cepMeters >= 100f)
                cepBucket = Mathf.Round(_cepMeters);
            else if (_cepMeters >= 10f)
                cepBucket = Mathf.Floor(_cepMeters);
            else
                cepBucket = Mathf.Floor(_cepMeters * 10f) * 0.1f;

            if (cepBucket != _labelCepBucket)
            {
                _labelCepBucket = cepBucket;
                _labelCep = UiLang.IsChinese
                    ? ("CEP ≈" + FormatCepMeters(_cepMeters) + "米")
                    : ("CEP ~" + FormatCepMeters(_cepMeters) + "m");
            }
        }

        private static string FormatCepMeters(float m)
        {
            return RocketCcipMathService.FormatCepMeters(m);
        }

        private static float EstimateCepMeters()
        {
            float scale = _cepScale != null ? Mathf.Clamp(_cepScale.Value, 0.05f, 10f) : 1f;
            float baseline = _cepBaselineMeters != null
                ? Mathf.Clamp(_cepBaselineMeters.Value, 0f, 200f) : 8f;
            return RocketCcipMathService.EstimateCepMeters(
                _cachedCircularError,
                _timeOfFlight,
                _muzzleWorld,
                _impactWorld,
                _cachedMuzzle,
                _cachedBurn,
                _cachedThrust,
                _cachedMass,
                scale,
                baseline);
        }

        private static void DrawCepRing(Camera cam, Vector3 impact, float cepMeters, Color color)
        {
            if (cam == null || cepMeters < 0.5f)
                return;

            float drawCep = cepMeters;
            try
            {
                Vector3 spC = cam.WorldToScreenPoint(impact);
                Vector3 spR = cam.WorldToScreenPoint(impact + Vector3.right * cepMeters);
                if (spC.z > 0.05f && spR.z > 0.05f)
                {
                    float px = Vector2.Distance(
                        new Vector2(UiScaleService.FromScreenX(spC.x), UiScaleService.FromScreenYFlipped(spC.y)),
                        new Vector2(UiScaleService.FromScreenX(spR.x), UiScaleService.FromScreenYFlipped(spR.y)));
                    float maxPx = Mathf.Min(UiScaleService.Width, UiScaleService.Height) * 0.35f;
                    float minPx = 14f;
                    if (px > maxPx && px > 1f)
                        drawCep *= maxPx / px;
                    else if (px < minPx && px > 0.1f)
                        drawCep *= minPx / px;
                }
            }
            catch { }

            Color prev = GUI.color;
            GUI.color = color;
            Vector2 prevPt = Vector2.zero;
            bool hasPrev = false;
            float step = Mathf.PI * 2f / CepSegments;
            for (int i = 0; i <= CepSegments; i++)
            {
                float a = (i % CepSegments) * step;
                Vector3 w = impact + new Vector3(Mathf.Cos(a) * drawCep, 0f, Mathf.Sin(a) * drawCep);
                Vector3 sp;
                try { sp = cam.WorldToScreenPoint(w); }
                catch
                {
                    hasPrev = false;
                    continue;
                }
                if (sp.z <= 0.05f)
                {
                    hasPrev = false;
                    continue;
                }
                Vector2 p = new Vector2(UiScaleService.FromScreenX(sp.x), UiScaleService.FromScreenYFlipped(sp.y));
                if (hasPrev)
                    DrawLine(prevPt.x, prevPt.y, p.x, p.y, 1.25f);
                prevPt = p;
                hasPrev = true;
            }
            GUI.color = prev;
        }

        private static void EnsureBallistics(WeaponStation station)
        {
            if (station == null || station.WeaponInfo == null)
                return;
            if (_ballisticsReady
                && object.ReferenceEquals(_cachedInfo, station.WeaponInfo)
                && _cachedStationNumber == station.Number)
                return;

            _ballisticsReady = false;
            _cachedInfo = station.WeaponInfo;
            _cachedStationNumber = station.Number;
            _cachedDragCoef = Mathf.Max(0f, station.WeaponInfo.dragCoef);
            _cachedFinArea = 0.05f;
            _cachedMass = Mathf.Max(0.5f, station.WeaponInfo.massPerRound);
            _cachedThrust = 0f;
            _cachedBurn = 0f;
            _cachedMuzzle = Mathf.Max(0f, station.WeaponInfo.muzzleVelocity);
            _cachedGravMult = station.WeaponInfo.gravMult > 0.01f ? station.WeaponInfo.gravMult : 1f;
            _cachedCircularError = 0f;
            _cachedEjection = Vector3.zero;

            try
            {
                if (station.Weapons != null && station.Weapons.Count > 0)
                {
                    Weapon w = station.Weapons[0];
                    MissileLauncher launcher = w as MissileLauncher;
                    if (launcher != null && EjectionField != null)
                    {
                        object ej = EjectionField.GetValue(launcher);
                        if (ej is Vector3)
                            _cachedEjection = (Vector3)ej;
                    }

                    Missile missile = null;
                    if (station.WeaponInfo.weaponPrefab != null)
                        missile = station.WeaponInfo.weaponPrefab.GetComponent<Missile>();
                    if (missile == null && launcher != null && launcher.missile != null
                        && launcher.missile.unitPrefab != null)
                    {
                        try { missile = launcher.missile.unitPrefab.GetComponent<Missile>(); }
                        catch { }
                    }
                    if (missile != null)
                    {
                        try { _cachedDragCoef = Mathf.Max(0f, missile.GetDragCoef(Mathf.PI / 360f)); }
                        catch { }
                        try { _cachedFinArea = Mathf.Max(0.01f, missile.GetFinArea()); }
                        catch { }
                        try { _cachedThrust = Mathf.Max(0f, missile.GetThrust()); }
                        catch { }
                        try { _cachedBurn = Mathf.Max(0f, missile.GetThrustDuration()); }
                        catch { }
                        try
                        {
                            if (missile.rb != null && missile.rb.mass > 0.1f)
                                _cachedMass = missile.rb.mass;
                        }
                        catch { }

                        try
                        {
                            BallisticMissileGuidance bmg =
                                missile.GetComponent<BallisticMissileGuidance>();
                            if (bmg == null)
                                bmg = missile.GetComponentInChildren<BallisticMissileGuidance>();
                            if (bmg != null && CircularErrorField != null)
                            {
                                object ce = CircularErrorField.GetValue(bmg);
                                if (ce is float)
                                    _cachedCircularError = Mathf.Max(0f, (float)ce);
                            }
                        }
                        catch { }

                        if (_cachedCircularError < 0.05f)
                            _cachedCircularError = TryReadDispersionField(station.WeaponInfo);
                        if (_cachedCircularError < 0.05f)
                            _cachedCircularError = TryReadDispersionField(missile);
                    }
                }
            }
            catch { }

            if (_cachedFinArea < 0.01f)
                _cachedFinArea = 0.05f;
            if (_cachedMass < 0.5f)
                _cachedMass = 0.5f;
            _ballisticsReady = true;
        }

        private static float TryReadDispersionField(object obj)
        {
            if (obj == null)
                return 0f;
            try
            {
                FieldInfo[] fields = obj.GetType().GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    if (f.FieldType != typeof(float))
                        continue;
                    string n = f.Name;
                    if (n == null)
                        continue;
                    string lower = n.ToLowerInvariant();
                    if (lower.IndexOf("dispersion", StringComparison.Ordinal) < 0
                        && lower.IndexOf("circularerror", StringComparison.Ordinal) < 0
                        && lower.IndexOf("circular_error", StringComparison.Ordinal) < 0
                        && lower != "cep"
                        && lower.IndexOf("inaccuracy", StringComparison.Ordinal) < 0
                        && !(lower.IndexOf("accuracy", StringComparison.Ordinal) >= 0
                            && lower.IndexOf("degrad", StringComparison.Ordinal) < 0))
                        continue;
                    object v = f.GetValue(obj);
                    if (v is float)
                    {
                        float fv = (float)v;
                        if (fv > 0.05f && fv < 5000f)
                            return fv;
                    }
                }
            }
            catch { }
            return 0f;
        }

        private static void ComputeImpact(Aircraft ac, WeaponStation station)
        {
            _hasImpact = false;
            Transform muzzle = ResolveMuzzle(ac, station);
            if (muzzle == null)
                muzzle = ac.transform;

            Vector3 pos = muzzle.position;
            Vector3 fwd = muzzle.forward;
            Vector3 vel = Vector3.zero;
            try
            {
                if (ac.rb != null)
                    vel = ac.rb.velocity;
            }
            catch { }

            vel += muzzle.right * _cachedEjection.x
                + muzzle.up * _cachedEjection.y
                + muzzle.forward * _cachedEjection.z;
            if (_cachedMuzzle > 0.1f)
                vel += fwd * _cachedMuzzle;
            else if (_cachedEjection.sqrMagnitude < 0.01f)
                vel += fwd * 25f;

            vel -= Vector3.up * 9.81f * _cachedGravMult * 0.15f;

            _muzzleWorld = pos;

            float airDensity = 1.2f;
            try { airDensity = LevelInfo.GetAirDensity(pos.y); }
            catch
            {
                try
                {
                    GlobalPosition gp = ac.GlobalPosition();
                    airDensity = LevelInfo.GetAirDensity(gp.y);
                }
                catch { }
            }

            float dragK = 0.5f * _cachedDragCoef * airDensity * _cachedFinArea / _cachedMass;
            float seaY = 0f;
            try { seaY = Datum.LocalSeaY; }
            catch { }

            RocketCcipMathService.BallisticsState state;
            state.Pos = pos;
            state.Vel = vel;
            state.DragK = dragK;
            state.GravMult = _cachedGravMult;
            state.Burn = _cachedBurn;
            state.Thrust = _cachedThrust;
            state.Mass = _cachedMass;
            state.SeaY = seaY;

            RocketCcipMathService.ImpactResult hit = RocketCcipMathService.SimulateImpact(state);
            if (!hit.Hit)
                return;

            _hasImpact = true;
            _impactWorld = hit.Point;
            _timeOfFlight = hit.TimeOfFlight;
            // Smooth with compute-interval dt so lerp stays stable at 20 Hz.
            float smoothDt = ComputeInterval;
            if (_impactSmoothed.sqrMagnitude < 0.01f)
                _impactSmoothed = hit.Point;
            else
                _impactSmoothed = Vector3.Lerp(_impactSmoothed, hit.Point, 1f - Mathf.Exp(-12f * smoothDt));
        }

        private static Transform ResolveMuzzle(Aircraft ac, WeaponStation station)
        {
            try
            {
                if (station.Weapons != null && station.Weapons.Count > 0)
                {
                    Weapon w = station.Weapons[0];
                    if (w != null && w.transform != null)
                    {
                        MissileLauncher launcher = w as MissileLauncher;
                        if (launcher != null && LaunchTransformsField != null)
                        {
                            Transform[] arr = LaunchTransformsField.GetValue(launcher) as Transform[];
                            if (arr != null && arr.Length > 0 && arr[0] != null)
                                return arr[0];
                        }
                        return w.transform;
                    }
                }
            }
            catch { }
            return ac != null ? ac.transform : null;
        }

        internal static bool IsRocketStation(WeaponStation station)
        {
            if (station == null || station.WeaponInfo == null)
                return false;
            WeaponInfo info = station.WeaponInfo;
            if (info.gun || info.bomb || info.glideBomb || info.nuclear
                || info.jammer || info.cargo || info.troops || info.sling || info.energy)
                return false;

            if (NameLooksLikeRocket(info.weaponName)
                || NameLooksLikeRocket(info.shortName))
                return true;

            try
            {
                if (info.weaponPrefab != null
                    && NameLooksLikeRocket(info.weaponPrefab.name))
                    return true;
            }
            catch { }

            try
            {
                if (station.Weapons != null)
                {
                    for (int i = 0; i < station.Weapons.Count; i++)
                    {
                        Weapon w = station.Weapons[i];
                        if (w == null)
                            continue;
                        if (WeaponMountField != null)
                        {
                            WeaponMount mount = WeaponMountField.GetValue(w) as WeaponMount;
                            if (mount != null)
                            {
                                if (NameLooksLikeRocket(mount.jsonKey)
                                    || NameLooksLikeRocket(mount.mountName))
                                    return true;
                            }
                        }
                        MissileLauncher launcher = w as MissileLauncher;
                        if (launcher != null && launcher.missile != null)
                        {
                            MissileDefinition def = launcher.missile;
                            if (NameLooksLikeRocket(def.name)
                                || NameLooksLikeRocket(def.unitName)
                                || NameLooksLikeRocket(def.code)
                                || NameLooksLikeRocket(def.jsonKey))
                                return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (info.missile && info.boresight && !info.laserGuided && !info.overHorizon
                    && info.effectiveness.antiSurface > 0.05f
                    && info.effectiveness.antiAir < 0.05f)
                    return true;
            }
            catch { }

            return false;
        }

        private static bool NameLooksLikeRocket(string s)
        {
            return RocketCcipMathService.NameLooksLikeRocket(s);
        }

        private static bool IsAircraftFpOrChaseView()
        {
            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { }
            if (local == null)
                return false;

            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null)
                {
                    if (object.ReferenceEquals(csm.currentState, csm.cockpitState)
                        || object.ReferenceEquals(csm.currentState, csm.chaseState)
                        || object.ReferenceEquals(csm.currentState, csm.orbitState))
                        return FollowingLocalOrUnset(csm, local);
                }
            }
            catch { }

            try
            {
                CameraMode mode = CameraStateManager.cameraMode;
                if (mode == CameraMode.cockpit || mode == CameraMode.chase || mode == CameraMode.orbit)
                    return true;
            }
            catch { }

            return false;
        }

        private static bool FollowingLocalOrUnset(CameraStateManager csm, Aircraft local)
        {
            if (csm == null || local == null)
                return false;
            try
            {
                Unit follow = csm.followingUnit;
                if (follow == null)
                    return true;
                if (object.ReferenceEquals(follow, local))
                    return true;
                Aircraft fa = follow as Aircraft;
                if (fa != null && object.ReferenceEquals(fa, local))
                    return true;
                if (follow.transform != null
                    && local.transform != null
                    && follow.transform.IsChildOf(local.transform))
                    return true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static Camera ResolveViewCamera()
        {
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null && csm.mainCamera != null && csm.mainCamera.enabled)
                    return csm.mainCamera;
            }
            catch { }
            try
            {
                Camera main = Camera.main;
                if (main != null && main.enabled)
                    return main;
            }
            catch { }
            return null;
        }

        private static void DrawFallLine(Camera cam, float pipX, float pipY, Color color)
        {
            if (cam == null)
                return;
            Vector3 muzzleSp;
            Vector3 startWorld = _muzzleWorld;
            try
            {
                Aircraft ac = null;
                GameManager.GetLocalAircraft(out ac);
                if (ac != null && ac.rb != null && ac.rb.velocity.sqrMagnitude > 1f)
                    startWorld = ac.transform.position + ac.rb.velocity.normalized * 120f;
            }
            catch { }
            try { muzzleSp = cam.WorldToScreenPoint(startWorld); }
            catch { return; }
            if (muzzleSp.z <= 0.05f)
                return;

            float x0 = UiScaleService.FromScreenX(muzzleSp.x);
            float y0 = UiScaleService.FromScreenYFlipped(muzzleSp.y);
            Color prev = GUI.color;
            GUI.color = color;
            DrawLine(x0, y0, pipX, pipY, 1.5f);
            GUI.color = prev;
        }

        private static void DrawPipper(float cx, float cy, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            float arm = 11f;
            float th = 2f;
            float gap = 4f;
            GUI.DrawTexture(new Rect(cx - arm, cy - th * 0.5f, arm - gap, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + gap, cy - th * 0.5f, arm - gap, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy - arm, th, arm - gap), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy + gap, th, arm - gap), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), Texture2D.whiteTexture);
            float r = 9f;
            GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2f, 1.5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - r, cy + r - 1.5f, r * 2f, 1.5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - r, cy - r, 1.5f, r * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + r - 1.5f, cy - r, 1.5f, r * 2f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void DrawLine(float x0, float y0, float x1, float y1, float thickness)
        {
            UiScaleService.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), thickness);
        }

        private static void EnsureStyles()
        {
            if (_labelStyle != null)
                return;
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 12;
            _labelStyle.fontStyle = FontStyle.Bold;
            _labelStyle.normal.textColor = new Color(0.75f, 0.95f, 0.8f, 0.9f);
            _labelStyle.alignment = TextAnchor.UpperLeft;
        }
    }
}
