using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Vanilla T/A-30 ControlSurface.Awake always reads visibleMesh.transform.
    /// BIA fast template left visibleMesh null (physics-only), so hangar
    /// SpawnPreview throws and Fly never becomes interactable.
    /// </summary>
    [HarmonyPatch(typeof(ControlSurface), "Awake")]
    internal static class Patch_Bia_ControlSurfaceAwake
    {
        private static readonly FieldInfo VisibleMeshField =
            AccessTools.Field(typeof(ControlSurface), "visibleMesh");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ControlSurface __instance)
        {
            EnsureVisibleMesh(__instance);
        }

        internal static void EnsureVisibleMesh(ControlSurface instance)
        {
            if (instance == null || VisibleMeshField == null)
                return;
            GameObject vis = VisibleMeshField.GetValue(instance) as GameObject;
            Transform child = instance.transform.Find("visible");
            // Never rotate the AeroPart root. That inverts lift vs stick on
            // sit-on-wheels Draken copies (visibleMesh was the wing itself).
            if (child != null && (vis == null || vis == instance.gameObject))
            {
                VisibleMeshField.SetValue(instance, child.gameObject);
                return;
            }
            if (vis != null)
                return;
            Aircraft ac = null;
            try { ac = instance.GetComponentInParent<Aircraft>(); }
            catch { }
            if (AircraftIdentity.IsCompleteBiaAirframe(ac))
                return;
            VisibleMeshField.SetValue(instance, instance.gameObject);
        }
    }

    /// <summary>
    /// Vanilla Aircraft.SetComplexPhysics casts every partLookup entry to AeroPart.
    /// BIA cockpit is UnitPart only, so preview dies before selectedType is assigned.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), "SetComplexPhysics")]
    internal static class Patch_Bia_SetComplexPhysics
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Aircraft __instance)
        {
            if (__instance == null || __instance.partLookup == null)
                return true;
            if (AircraftIdentity.IsCompleteBiaAirframe(__instance))
                return true;
            BiaSpawnGuards.EnsureEngineAeroPart(__instance);
            bool needsSkip = false;
            List<UnitPart> parts = __instance.partLookup;
            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart p = parts[i];
                if (p != null && !(p is AeroPart))
                {
                    needsSkip = true;
                    break;
                }
            }
            if (!needsSkip)
                return true;

            Rigidbody rb = __instance.rb;
            Vector3 zero = Vector3.zero;
            for (int i = 0; i < parts.Count; i++)
            {
                AeroPart aero = parts[i] as AeroPart;
                if (aero == null)
                    continue;
                Vector3 vel = zero;
                if (rb != null)
                    vel = rb.GetPointVelocity(aero.transform.position);
                aero.CreateRB(vel, zero);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                AeroPart aero = parts[i] as AeroPart;
                if (aero != null)
                    aero.CreateJoints();
            }
            __instance.simplePhysics = false;
            if (rb != null)
            {
                rb.ResetCenterOfMass();
                if (!rb.isKinematic)
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TurbineEngine), "OnEnable")]
    internal static class Patch_Bia_TurbineOnEnable
    {
        private static readonly FieldInfo IrField =
            AccessTools.Field(typeof(TurbineEngine), "IRSourceTransform");
        private static readonly FieldInfo CritField =
            AccessTools.Field(typeof(TurbineEngine), "criticalParts");
        private static AudioClip _silent;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TurbineEngine __instance)
        {
            if (__instance == null)
                return;
            AudioSource src = __instance.GetComponent<AudioSource>();
            if (src == null)
            {
                src = __instance.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.spatialBlend = 1f;
            }
            if (src.clip == null)
            {
                if (_silent == null)
                    _silent = AudioClip.Create("bia_engine_silent", 1024, 1, 44100, false);
                src.clip = _silent;
            }
            if (IrField != null && IrField.GetValue(__instance) == null)
                IrField.SetValue(__instance, __instance.transform);
            if (CritField != null && CritField.GetValue(__instance) == null)
                CritField.SetValue(__instance, new UnitPart[0]);
            if (__instance.aircraft != null && __instance.aircraft.engineStates == null)
                __instance.aircraft.engineStates = new List<IEngine>();
        }
    }

    /// <summary>
    /// Inventory SetSelectedType only highlights the button after SpawnPreview
    /// returns. Assign selectedType first so a later NRE still leaves Fly green.
    /// </summary>
    [HarmonyPatch(typeof(AircraftSelectionMenu), "SetSelectedType")]
    internal static class Patch_Bia_SetSelectedType
    {
        private static readonly FieldInfo SelectedTypeField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "selectedType");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AircraftSelectionMenu __instance, AircraftDefinition definition)
        {
            if (__instance == null || definition == null || SelectedTypeField == null)
                return;
            SelectedTypeField.SetValue(__instance, definition);
            IcarusAircraftRegistry.RegisterNetwork(definition);
        }
    }

    /// <summary>
    /// Swallow SpawnPreview NREs so AircraftInventoryMenu.Refresh still runs
    /// (selection outline) and selectedType stays assigned (Fly).
    /// </summary>
    [HarmonyPatch(typeof(AircraftSelectionMenu), "SpawnPreview")]
    internal static class Patch_Bia_SpawnPreview
    {
        private static readonly FieldInfo SelectedTypeField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "selectedType");
        private static readonly FieldInfo SelectionField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "aircraftSelection");
        private static readonly FieldInfo IndexField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "selectionIndex");

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.First)]
        private static Exception Finalizer(AircraftSelectionMenu __instance, Exception __exception)
        {
            if (__exception == null || __instance == null)
                return null;
            try
            {
                AircraftDefinition def = SelectedTypeField != null
                    ? SelectedTypeField.GetValue(__instance) as AircraftDefinition
                    : null;
                if (def == null && SelectionField != null && IndexField != null)
                {
                    List<AircraftDefinition> list =
                        SelectionField.GetValue(__instance) as List<AircraftDefinition>;
                    int idx = (int)IndexField.GetValue(__instance);
                    if (list != null && idx >= 0 && idx < list.Count)
                        def = list[idx];
                    if (def != null && SelectedTypeField != null)
                        SelectedTypeField.SetValue(__instance, def);
                }
                IcarusAircraftRegistry.RegisterNetwork(def);
            }
            catch { }
            return null;
        }
    }

    /// <summary>
    /// Vanilla Update always calls previewAircraft.GetInputs with no null check.
    /// A failed SpawnPreview then bricks the whole hangar UI every frame.
    /// </summary>
    [HarmonyPatch(typeof(AircraftSelectionMenu), "Update")]
    internal static class Patch_Bia_SelMenuUpdate
    {
        private static readonly FieldInfo PreviewField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "previewAircraft");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AircraftSelectionMenu __instance)
        {
            if (__instance == null || PreviewField == null)
                return true;
            Aircraft preview = PreviewField.GetValue(__instance) as Aircraft;
            return preview != null;
        }
    }

    /// <summary>
    /// Hangar Fly can succeed while OnStartClient still throws (null
    /// CountermeasureManager.aircraft, empty loadouts[1], missing StatusDisplay).
    /// That leaves the local player in free-cam with Oritasy ground HUD drawn.
    /// </summary>
    internal static class BiaSpawnGuards
    {
        private static readonly FieldInfo CmAircraftField =
            AccessTools.Field(typeof(CountermeasureManager), "aircraft");
        private static FieldInfo PlayerInputField;
        private static MethodInfo GetButtonDownMethod;
        private static MethodInfo GetButtonTimedPressUpMethod;

        internal static void Prepare(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                BaasNetworkFixer.EnsureDefinition(ac.definition as AircraftDefinition);
                BaasNetworkFixer.EnsurePrefab(ac.gameObject);
            }
            catch { }
            if (AircraftIdentity.IsCompleteBiaAirframe(ac))
            {
                FillHudPrefabs(ac);
                StripCubeFlightHacks(ac);
                IgnoreSelfCollisions(ac);
                SuppressBiaImguiHud(ac);
                BiaChaseCameras.EnsureAircraft(ac);
                return;
            }
            WireCountermeasures(ac);
            PadLoadouts(ac);
            FillHudPrefabs(ac);
            StripTrainerHud(ac);
            FillTrainerWeapons(ac);
            EnsureEngineAeroPart(ac);
            EnsurePowerSupplyAudio(ac);
            try
            {
                if (ac.definition != null)
                {
                    ac.definition.IsObstacle = false;
                    if (ac.definition.spawnOffset.y < 0.05f)
                        ac.definition.spawnOffset = new Vector3(0f, 0.22f, 0f);
                }
            }
            catch { }
            FillBiaFuel(ac);
            PlaceCockpitAtNose(ac);
            RegisterJetEngine(ac);
            EnsureLivePhysics(ac);
            EnsurePowerPlant(ac);
            EnsureDefaultLoadout(ac);
            SuppressBiaImguiHud(ac);
            IgnoreSelfCollisions(ac);
            BiaChaseCameras.EnsureAircraft(ac);
            try
            {
                if (ac.loadout != null && ac.loadout.weapons == null)
                    ac.loadout.weapons = new List<WeaponMount>();
            }
            catch { }
        }

        internal static void AfterPhysics(Aircraft ac)
        {
            IgnoreSelfCollisions(ac);
            if (AircraftIdentity.IsCompleteBiaAirframe(ac))
                return;
            WeldFuselage(ac);
            RegisterJetEngine(ac);
            FillBiaFuel(ac);
            PlaceCockpitAtNose(ac);
            EnsureLivePhysics(ac);
            EnsurePowerPlant(ac);
            EnsureDefaultLoadout(ac);
            SettleIcarusSpawn(ac);
        }

        internal static void AttachLocalCamera(Aircraft ac)
        {
            try
            {
                if (ac == null || ac.Player == null || !ac.Player.IsLocalPlayer)
                    return;
                CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
                if (cam == null)
                    return;
                cam.SetFollowingUnit(ac);
                if (ac.rb != null)
                    cam.followingRB = ac.rb;
                try { ac.SetLocalSim(true); }
                catch { }
                if (cam.cockpitState != null)
                    cam.SwitchState(cam.cockpitState);
                FlightHud.EnableCanvas(true);
                if (SceneSingleton<FlightHud>.i != null)
                    SceneSingleton<FlightHud>.i.SetAircraft(ac);
            }
            catch { }
        }

        internal static bool ButtonDown(string action)
        {
            object pi = PlayerInput();
            if (pi == null)
                return false;
            if (GetButtonDownMethod == null)
            {
                try
                {
                    GetButtonDownMethod = pi.GetType().GetMethod("GetButtonDown",
                        new Type[] { typeof(string) });
                }
                catch { return false; }
            }
            if (GetButtonDownMethod == null)
                return false;
            try
            {
                object r = GetButtonDownMethod.Invoke(pi, new object[] { action });
                return r is bool && (bool)r;
            }
            catch
            {
                return false;
            }
        }

        internal static bool WantsSwitchView()
        {
            if (ButtonDown("Switch View") || ButtonTimedPressUp("Switch View"))
                return true;
            try
            {
                return Input.GetKeyDown(KeyCode.L);
            }
            catch
            {
                return false;
            }
        }

        internal static bool ButtonTimedPressUp(string action)
        {
            object pi = PlayerInput();
            if (pi == null)
                return false;
            if (GetButtonTimedPressUpMethod == null)
            {
                try
                {
                    GetButtonTimedPressUpMethod = pi.GetType().GetMethod("GetButtonTimedPressUp",
                        new Type[] { typeof(string), typeof(float), typeof(float) });
                }
                catch { return false; }
            }
            if (GetButtonTimedPressUpMethod == null)
                return false;
            float delay = 0.25f;
            try { delay = PlayerSettings.clickDelay; }
            catch { }
            try
            {
                object r = GetButtonTimedPressUpMethod.Invoke(pi, new object[] { action, 0f, delay });
                return r is bool && (bool)r;
            }
            catch
            {
                return false;
            }
        }

        private static object PlayerInput()
        {
            if (PlayerInputField == null)
            {
                try
                {
                    PlayerInputField = typeof(GameManager).GetField("playerInput",
                        BindingFlags.Public | BindingFlags.Static);
                }
                catch { return null; }
            }
            if (PlayerInputField == null)
                return null;
            try { return PlayerInputField.GetValue(null); }
            catch { return null; }
        }

        internal static void WireCountermeasures(Aircraft ac)
        {
            if (ac == null || ac.countermeasureManager == null || CmAircraftField == null)
                return;
            if (CmAircraftField.GetValue(ac.countermeasureManager) == null)
                CmAircraftField.SetValue(ac.countermeasureManager, ac);
        }

        internal static void EnsureEngineAeroPart(Aircraft ac)
        {
            if (ac == null)
                return;
            Turbojet jet = null;
            try { jet = ac.GetComponentInChildren<Turbojet>(true); }
            catch { return; }
            if (jet == null)
                return;
            AeroPart part = null;
            try
            {
                Transform fuseT = ac.transform.Find("fuselage");
                if (fuseT != null)
                    part = fuseT.GetComponent<AeroPart>();
                if (part == null)
                    part = jet.GetComponentInParent<AeroPart>();
            }
            catch { }
            if (part == null)
                return;
            if (part.parentUnit == null)
                part.parentUnit = ac;
            FieldInfo critParts = AccessTools.Field(typeof(Turbojet), "criticalParts");
            if (critParts != null)
            {
                try { critParts.SetValue(jet, new UnitPart[] { part }); }
                catch { }
            }
            FieldInfo nozzlePart = AccessTools.Field(typeof(JetNozzle), "part");
            if (nozzlePart != null)
            {
                try
                {
                    JetNozzle[] nozzles = ac.GetComponentsInChildren<JetNozzle>(true);
                    if (nozzles != null)
                    {
                        for (int i = 0; i < nozzles.Length; i++)
                        {
                            if (nozzles[i] != null)
                                nozzlePart.SetValue(nozzles[i], part);
                        }
                    }
                }
                catch { }
            }
            FieldInfo hasFuel = AccessTools.Field(typeof(Turbojet), "hasFuel");
            if (hasFuel != null)
            {
                try { hasFuel.SetValue(jet, true); }
                catch { }
            }
            if (ac.rb != null && part.rb == null)
            {
                try { part.rb = ac.rb; }
                catch { }
            }
        }

        internal static void RegisterJetEngine(Aircraft ac)
        {
            if (ac == null)
                return;
            Turbojet jet = null;
            try { jet = ac.GetComponentInChildren<Turbojet>(true); }
            catch { return; }
            if (jet == null)
                return;
            try
            {
                if (ac.engineStates == null)
                    ac.engineStates = new List<IEngine>();
                if (!ac.engineStates.Contains(jet))
                    ac.engineStates.Add(jet);
            }
            catch { }
            try
            {
                if (ac.engines == null)
                    ac.engines = new List<IEngine>();
                if (!ac.engines.Contains(jet))
                    ac.engines.Add(jet);
            }
            catch { }
        }

        /// <summary>
        /// Keep vanilla FlightHud on complete airframes. Do not disable the
        /// clickable BIAMfd overlay — Home still toggles it.
        /// </summary>
        internal static void SuppressBiaImguiHud(Aircraft ac)
        {
            if (ac == null)
                return;
            Component mb = AircraftIdentity.FindBiaMfd(ac.gameObject);
            if (mb == null)
                return;
            Type t = mb.GetType();
            try
            {
                FieldInfo hud = t.GetField("showHud", BindingFlags.Instance | BindingFlags.Public);
                if (hud != null)
                    hud.SetValue(mb, false);
            }
            catch { }
        }

        internal static void WeldFuselage(Aircraft ac)
        {
            // BIA parts share aircraft.rb. Extra FixedJoints chatter and shake the mesh.
        }

        internal static void PlaceCockpitAtNose(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac))
                return;
            if (AircraftIdentity.IsJ35WyvernKey(AircraftIdentity.GetKey(ac))
                || AircraftIdentity.IsCompleteBiaAirframe(ac))
                return;
            Transform view = ac.cockpitViewPoint;
            if (view == null)
                return;
            try
            {
                view.SetParent(ac.transform, false);
                view.localPosition = NoseViewLocal(ac);
                view.localRotation = Quaternion.identity;
                view.localScale = Vector3.one;
            }
            catch { }
        }

        internal static Vector3 NoseViewLocal(Aircraft ac)
        {
            float y = 1.35f;
            float z = 8.45f;
            try
            {
                if (TryVisualNose(ac, out y, out z))
                    return new Vector3(0f, y, z);
                if (ac != null && ac.definition != null)
                {
                    float len = ac.definition.length;
                    if (len > 4f)
                        z = len * 0.52f + 0.7f;
                    float h = ac.definition.height;
                    if (h > 1f)
                        y = Mathf.Clamp(h * 0.38f, 1.1f, 1.85f);
                }
            }
            catch { }
            return new Vector3(0f, y, z);
        }

        private static bool TryVisualNose(Aircraft ac, out float y, out float z)
        {
            y = 1.35f;
            z = 8.45f;
            if (ac == null)
                return false;
            Renderer[] rs = null;
            try { rs = ac.GetComponentsInChildren<Renderer>(true); }
            catch { return false; }
            if (rs == null || rs.Length == 0)
                return false;
            float maxZ = float.MinValue;
            float maxY = float.MinValue;
            bool any = false;
            Vector3 e;
            Vector3 c;
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled)
                    continue;
                Bounds b = r.bounds;
                c = b.center;
                e = b.extents;
                int ix;
                int iy;
                int iz;
                for (ix = -1; ix <= 1; ix += 2)
                {
                    for (iy = -1; iy <= 1; iy += 2)
                    {
                        for (iz = -1; iz <= 1; iz += 2)
                        {
                            Vector3 world = c + new Vector3(e.x * ix, e.y * iy, e.z * iz);
                            Vector3 loc = ac.transform.InverseTransformPoint(world);
                            if (loc.z > maxZ)
                                maxZ = loc.z;
                            if (loc.y > maxY)
                                maxY = loc.y;
                            any = true;
                        }
                    }
                }
            }
            if (!any || maxZ < 1f)
                return false;
            z = maxZ + 0.75f;
            y = Mathf.Clamp(maxY * 0.42f, 1.1f, 1.9f);
            return true;
        }

        internal static void EnsureLivePhysics(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac))
                return;
            if (AircraftIdentity.IsCompleteBiaAirframe(ac))
            {
                try
                {
                    if (ac.airDensity < 0.3f)
                        ac.airDensity = 1.225f;
                }
                catch { }
                try
                {
                    Aircraft local = AircraftIdentity.CachedLocal();
                    if (local != null && object.ReferenceEquals(local, ac))
                        ac.SetLocalSim(true);
                }
                catch { }
                return;
            }
            try
            {
                if (ac.cockpit != null && ac.rb != null && ac.cockpit.rb == null)
                    ac.cockpit.rb = ac.rb;
            }
            catch { }
            try
            {
                if (ac.airDensity < 0.3f)
                    ac.airDensity = 1.225f;
            }
            catch { }
            try
            {
                Aircraft local = AircraftIdentity.CachedLocal();
                if (local != null && object.ReferenceEquals(local, ac))
                    ac.SetLocalSim(true);
            }
            catch { }
            int id = ac.GetInstanceID();
            if (PhysicsWired.Contains(id))
                return;
            EnsureEngineAeroPart(ac);
            RegisterJetEngine(ac);
            FillBiaFuel(ac);
            PhysicsWired.Add(id);
        }

        internal static bool IsPhysicsWired(Aircraft ac)
        {
            if (ac == null)
                return false;
            try { return PhysicsWired.Contains(ac.GetInstanceID()); }
            catch { return false; }
        }

        internal static void Forget(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                int id = ac.GetInstanceID();
                PhysicsWired.Remove(id);
                SpawnGraceUntil.Remove(id);
                SpawnSettled.Remove(id);
            }
            catch { }
        }

        internal static void FillBiaFuel(Aircraft ac)
        {
            if (ac == null)
                return;
            FuelTank[] tanks = null;
            try { tanks = ac.GetComponentsInChildren<FuelTank>(true); }
            catch { return; }
            if (tanks == null || tanks.Length == 0)
                return;
            FieldInfo capField = AccessTools.Field(typeof(FuelTank), "fuelCapacity");
            FieldInfo partField = AccessTools.Field(typeof(FuelTank), "part");
            FieldInfo acCapField = AccessTools.Field(typeof(Aircraft), "fuelCapacity");
            UnitPart fuse = null;
            try
            {
                Transform ft = ac.transform.Find("fuselage");
                if (ft != null)
                    fuse = ft.GetComponent<AeroPart>();
            }
            catch { }
            List<FuelTank> registered = null;
            try { registered = ac.GetFuelTanks(); }
            catch { }
            float cap = 0f;
            for (int i = 0; i < tanks.Length; i++)
            {
                FuelTank tank = tanks[i];
                if (tank == null)
                    continue;
                try
                {
                    if (partField != null && fuse != null && partField.GetValue(tank) == null)
                        partField.SetValue(tank, fuse);
                    if (registered == null || !registered.Contains(tank))
                    {
                        ac.RegisterFuelTank(tank);
                        if (registered != null)
                            registered.Add(tank);
                    }
                    if (capField != null)
                    {
                        object v = capField.GetValue(tank);
                        if (v is float)
                            cap += (float)v;
                    }
                    tank.Refuel(1f);
                }
                catch { }
            }
            if (cap > 1f && acCapField != null)
            {
                try { acCapField.SetValue(ac, cap); }
                catch { }
            }
            Turbojet[] jets = null;
            try { jets = ac.GetComponentsInChildren<Turbojet>(true); }
            catch { return; }
            if (jets == null)
                return;
            FieldInfo hasFuel = AccessTools.Field(typeof(Turbojet), "hasFuel");
            if (hasFuel == null)
                return;
            for (int i = 0; i < jets.Length; i++)
            {
                if (jets[i] == null)
                    continue;
                try { hasFuel.SetValue(jets[i], true); }
                catch { }
            }
        }

        internal static void EnsurePowerSupplyAudio(Aircraft ac)
        {
            if (ac == null)
                return;
            PowerSupply ps = null;
            try { ps = ac.GetComponent<PowerSupply>(); }
            catch { return; }
            if (ps == null)
                return;
            FieldInfo srcField = AccessTools.Field(typeof(PowerSupply), "source");
            if (srcField == null)
                return;
            AudioSource src = null;
            try { src = srcField.GetValue(ps) as AudioSource; }
            catch { return; }
            if (src != null)
                return;
            try
            {
                src = ps.gameObject.GetComponent<AudioSource>();
                if (src == null)
                    src = ps.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                srcField.SetValue(ps, src);
            }
            catch { }
        }

        internal static void EnsureFollow(CameraStateManager cam)
        {
            if (cam == null)
                return;
            Aircraft follow = cam.followingUnit as Aircraft;
            if (follow != null)
            {
                if (follow.rb != null && cam.followingRB == null)
                    cam.followingRB = follow.rb;
                return;
            }
            Aircraft ac = AircraftIdentity.CachedLocal();
            if (ac == null)
                return;
            if (!IsIcarus(ac))
                return;
            try { cam.SetFollowingUnit(ac); }
            catch { }
            try
            {
                if (ac.rb != null)
                    cam.followingRB = ac.rb;
            }
            catch { }
        }

        internal static void EnsurePowerPlant(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac))
                return;
            PowerSupply ps = null;
            try { ps = ac.GetPowerSupply(); }
            catch { }
            if (ps == null)
            {
                try { ps = ac.GetComponent<PowerSupply>(); }
                catch { }
            }
            if (ps == null)
                return;
            FieldInfo maxCharge = AccessTools.Field(typeof(PowerSupply), "maxCharge");
            FieldInfo maxPower = AccessTools.Field(typeof(PowerSupply), "maxPower");
            FieldInfo chargePer = AccessTools.Field(typeof(PowerSupply), "chargePerRPM");
            FieldInfo curve = AccessTools.Field(typeof(PowerSupply), "supplyAtCharge");
            FieldInfo engines = AccessTools.Field(typeof(PowerSupply), "engineInterfaces");
            FieldInfo sources = AccessTools.Field(typeof(PowerSupply), "powerSources");
            if (maxCharge != null)
            {
                try
                {
                    float cur = (float)maxCharge.GetValue(ps);
                    if (cur < 350f)
                        maxCharge.SetValue(ps, 400f);
                }
                catch { }
            }
            if (maxPower != null)
            {
                try
                {
                    float cur = (float)maxPower.GetValue(ps);
                    if (cur < 50f)
                        maxPower.SetValue(ps, 60f);
                }
                catch { }
            }
            if (chargePer != null)
            {
                try
                {
                    float cur = (float)chargePer.GetValue(ps);
                    if (cur < 0.0025f)
                        chargePer.SetValue(ps, 0.003f);
                }
                catch { }
            }
            if (curve != null)
            {
                try
                {
                    AnimationCurve c = curve.GetValue(ps) as AnimationCurve;
                    if (c == null || c.length < 2)
                    {
                        AnimationCurve n = new AnimationCurve();
                        n.AddKey(0f, 0f);
                        n.AddKey(0.7f, 1f);
                        curve.SetValue(ps, n);
                    }
                }
                catch { }
            }
            Turbojet jet = null;
            try { jet = ac.GetComponentInChildren<Turbojet>(true); }
            catch { }
            if (sources != null && jet != null)
            {
                try
                {
                    GameObject[] arr = sources.GetValue(ps) as GameObject[];
                    if (arr == null || arr.Length == 0 || arr[0] == null)
                        sources.SetValue(ps, new GameObject[] { jet.gameObject });
                }
                catch { }
            }
            if (engines != null && jet != null)
            {
                try
                {
                    IEngine[] arr = engines.GetValue(ps) as IEngine[];
                    if (arr == null || arr.Length == 0 || arr[0] == null)
                        engines.SetValue(ps, new IEngine[] { jet });
                }
                catch { }
            }
            EnsurePowerSupplyAudio(ac);
        }

        internal static void EnsureDefaultLoadout(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac) || ac.weaponManager == null)
                return;
            HardpointSet[] sets = ac.weaponManager.hardpointSets;
            if (sets == null || sets.Length == 0)
                return;
            if (ac.loadout == null)
                ac.loadout = new Loadout();
            if (ac.loadout.weapons == null)
                ac.loadout.weapons = new List<WeaponMount>();
            List<WeaponMount> weapons = ac.loadout.weapons;
            bool any = false;
            int i;
            for (i = 0; i < weapons.Count; i++)
            {
                if (weapons[i] != null)
                    any = true;
            }
            if (any)
                return;
            while (weapons.Count < sets.Length)
                weapons.Add(null);
            for (i = 0; i < sets.Length; i++)
            {
                if (weapons[i] != null)
                    continue;
                weapons[i] = FirstMount(sets[i]);
            }
        }

        private static WeaponMount FirstMount(HardpointSet set)
        {
            if (set == null || set.weaponOptions == null)
                return null;
            int i;
            for (i = 0; i < set.weaponOptions.Count; i++)
            {
                if (set.weaponOptions[i] != null)
                    return set.weaponOptions[i];
            }
            return null;
        }

        /// <summary>
        /// Chase / orbit need followingRB. Keep the pivot on the airframe
        /// so vanilla chase offsets sit outside the hull.
        /// </summary>
        internal static void ParentCamToAirframe(CameraStateManager cam, Aircraft ac)
        {
            if (cam == null || ac == null || !IsIcarus(ac))
                return;
            try
            {
                if (ac.rb != null)
                    cam.followingRB = ac.rb;
            }
            catch { }
        }

        internal static bool IsIcarusFuselage(AeroPart part)
        {
            return IsBiaSharedRbPart(part);
        }

        internal static bool IsBiaSharedRbPart(AeroPart part)
        {
            if (part == null)
                return false;
            Aircraft ac = part.parentUnit as Aircraft;
            if (ac == null)
            {
                try { ac = part.GetComponentInParent<Aircraft>(); }
                catch { }
            }
            return IsIcarus(ac);
        }

        private static readonly Dictionary<int, float> SpawnGraceUntil = new Dictionary<int, float>();
        internal static bool HasAnySpawnGrace { get { return SpawnGraceUntil.Count > 0; } }
        private static readonly HashSet<int> SpawnSettled = new HashSet<int>();
        private static readonly HashSet<int> PhysicsWired = new HashSet<int>();
        private static readonly RaycastHit[] ProbeHits = new RaycastHit[24];

        internal static void RecalcIcarusRadarAlt(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac) || ac.definition == null)
                return;
            if (AircraftIdentity.IsCompleteBiaAirframe(ac))
                return;
            float prev = 0f;
            try { prev = ac.radarAlt; }
            catch { }
            if (prev > 80f && ((Time.frameCount + ac.GetInstanceID()) & 7) != 0)
                return;
            float probe = 3.2f;
            float dist;
            if (!ProbeGround(ac, ac.transform.position + Vector3.up * probe, 80f, out dist))
                return;
            float agl = dist - probe;
            float offset = 0f;
            try { offset = ac.definition.spawnOffset.y; }
            catch { }
            float alt = agl - offset;
            if (alt < 0f)
                alt = 0f;
            try { ac.radarAlt = alt; }
            catch { }
        }

        internal static void SettleIcarusSpawn(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac))
                return;
            if (AircraftIdentity.IsCompleteBiaAirframe(ac))
            {
                try
                {
                    if (ac.airDensity < 0.3f)
                        ac.airDensity = 1.225f;
                }
                catch { }
                try
                {
                    Aircraft local = AircraftIdentity.CachedLocal();
                    if (local != null && object.ReferenceEquals(local, ac))
                        ac.SetLocalSim(true);
                }
                catch { }
                return;
            }
            int id = ac.GetInstanceID();
            if (SpawnSettled.Contains(id))
                return;
            RecalcIcarusRadarAlt(ac);
            bool hangar = false;
            try { hangar = ac.NetworkspawningHangar != null; }
            catch { }
            float speed = 0f;
            try { speed = ac.rb != null ? ac.rb.velocity.magnitude : ac.speed; }
            catch { }
            float alt = 0f;
            try { alt = ac.radarAlt; }
            catch { }
            bool ground = hangar;
            if (!ground && speed < 12f && alt < 25f)
                ground = true;
            if (!ground)
                return;
            try { ac.SetGear(true); }
            catch { }
            bool live = false;
            try { live = ac.Ignition; }
            catch { }
            if (!live && !EngineStartService.ApuRunning)
            {
                try
                {
                    ControlInputs inputs = ac.GetInputs();
                    if (inputs != null)
                        inputs.throttle = 0f;
                }
                catch { }
            }
            SnapIcarusToPad(ac);
            FillBiaFuel(ac);
            PlaceCockpitAtNose(ac);
            EnsureLivePhysics(ac);
            if (ac.rb != null && !live)
            {
                try
                {
                    ac.rb.velocity = Vector3.zero;
                    ac.rb.angularVelocity = Vector3.zero;
                }
                catch { }
            }
            RecalcIcarusRadarAlt(ac);
            SpawnGraceUntil[id] = Time.time + 1.2f;
            SpawnSettled.Add(id);
        }

        internal static void TickSpawnGrace(Aircraft ac)
        {
            if (ac == null || ac.rb == null)
                return;
            // Most aircraft never enter spawn-grace — check the dict first (cheap).
            int id = ac.GetInstanceID();
            float until;
            if (!SpawnGraceUntil.TryGetValue(id, out until))
                return;
            try
            {
                if (AircraftIdentity.IsSaabJ35Sit(AircraftIdentity.GetKey(ac))
                    || AircraftIdentity.IsCompleteBiaAirframe(ac))
                {
                    SpawnGraceUntil.Remove(id);
                    return;
                }
            }
            catch { }
            bool live = false;
            try { live = ac.Ignition; }
            catch { }
            if (live || Time.time > until)
            {
                SpawnGraceUntil.Remove(id);
                return;
            }
            try
            {
                float alt = 0f;
                try { alt = ac.radarAlt; }
                catch { }
                Vector3 v = ac.rb.velocity;
                if (alt > 2.5f)
                    return;
                if (v.y > 1.5f)
                    v.y = 0f;
                ac.rb.velocity = v;
                ac.rb.angularVelocity = Vector3.zero;
            }
            catch { }
        }

        private static void SnapIcarusToPad(Aircraft ac)
        {
            if (ac == null || ac.transform == null)
                return;
            try
            {
                if (AircraftIdentity.IsSaabJ35Sit(AircraftIdentity.GetKey(ac))
                    || AircraftIdentity.IsCompleteBiaAirframe(ac))
                    return;
            }
            catch { }
            Hangar hangar = null;
            try { hangar = ac.NetworkspawningHangar; }
            catch { }
            if (hangar != null)
            {
                Transform spawn = null;
                try { spawn = hangar.GetSpawnTransform(); }
                catch { }
                if (spawn == null)
                    return;
                float offset = 0.22f;
                try
                {
                    if (ac.definition != null && ac.definition.spawnOffset.y > 0.05f)
                        offset = ac.definition.spawnOffset.y;
                }
                catch { }
                Vector3 p = ac.transform.position;
                p.y = spawn.position.y + offset;
                try { ac.transform.position = p; }
                catch { }
                if (ac.rb != null)
                {
                    try { ac.rb.position = p; }
                    catch { }
                }
                return;
            }
            float dist;
            Vector3 start = ac.transform.position + Vector3.up * 6f;
            if (!ProbeGround(ac, start, 40f, out dist))
                return;
            if (dist < 0.05f || dist > 18f)
                return;
            float pad = 0.22f;
            try
            {
                if (ac.definition != null && ac.definition.spawnOffset.y > 0.05f)
                    pad = ac.definition.spawnOffset.y;
            }
            catch { }
            float wantY = start.y - dist + pad;
            Vector3 pos = ac.transform.position;
            if (Mathf.Abs(wantY - pos.y) > 12f)
                return;
            pos.y = wantY;
            try { ac.transform.position = pos; }
            catch { }
            if (ac.rb != null)
            {
                try { ac.rb.position = pos; }
                catch { }
            }
        }

        private static bool ProbeGround(Aircraft ac, Vector3 start, float maxDist, out float distance)
        {
            distance = -1f;
            int n = 0;
            try
            {
                n = Physics.RaycastNonAlloc(start, Vector3.down, ProbeHits, maxDist, ~0,
                    QueryTriggerInteraction.Ignore);
            }
            catch
            {
                return false;
            }
            if (n <= 0)
                return false;
            float best = maxDist + 1f;
            bool found = false;
            int lim = n < ProbeHits.Length ? n : ProbeHits.Length;
            for (int i = 0; i < lim; i++)
            {
                RaycastHit hit = ProbeHits[i];
                if (hit.collider == null)
                    continue;
                if (IsOwnCollider(ac, hit.collider))
                    continue;
                if (hit.distance < 0.02f)
                    continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    found = true;
                }
            }
            if (!found)
                return false;
            distance = best;
            return true;
        }

        private static bool IsOwnCollider(Aircraft ac, Collider c)
        {
            if (ac == null || c == null)
                return false;
            try
            {
                Transform t = c.transform;
                if (t == ac.transform || t.IsChildOf(ac.transform))
                    return true;
            }
            catch { }
            try
            {
                UnitPart part = c.GetComponentInParent<UnitPart>();
                if (part != null && part.parentUnit == ac)
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsIcarus(Aircraft ac)
        {
            return AircraftIdentity.IsIcarusAircraft(ac);
        }

        internal static void IgnoreSelfCollisions(Aircraft ac)
        {
            if (ac == null)
                return;
            List<Collider> list = new List<Collider>(64);
            try
            {
                if (ac.partLookup != null)
                {
                    for (int i = 0; i < ac.partLookup.Count; i++)
                    {
                        UnitPart part = ac.partLookup[i];
                        if (part == null)
                            continue;
                        Collider[] onPart = part.GetComponentsInChildren<Collider>(true);
                        if (onPart == null)
                            continue;
                        for (int c = 0; c < onPart.Length; c++)
                        {
                            if (onPart[c] != null)
                                list.Add(onPart[c]);
                        }
                    }
                }
            }
            catch { }
            try
            {
                Collider[] extra = ac.GetComponentsInChildren<Collider>(true);
                if (extra != null)
                {
                    for (int i = 0; i < extra.Length; i++)
                    {
                        if (extra[i] != null && !list.Contains(extra[i]))
                            list.Add(extra[i]);
                    }
                }
            }
            catch { }
            if (list.Count < 2)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                Collider a = list[i];
                if (a == null)
                    continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    Collider b = list[j];
                    if (b == null)
                        continue;
                    try { Physics.IgnoreCollision(a, b, true); }
                    catch { }
                }
            }
        }

        private static void PadLoadouts(Aircraft ac)
        {
            try
            {
                AircraftDefinition def = ac.definition as AircraftDefinition;
                if (def == null || def.aircraftParameters == null)
                    return;
                AircraftParameters p = def.aircraftParameters;
                if (p.loadouts == null)
                    p.loadouts = new List<NuclearOption.SavedMission.Loadout>();
                while (p.loadouts.Count < 2)
                {
                    NuclearOption.SavedMission.Loadout empty = new NuclearOption.SavedMission.Loadout();
                    empty.weapons = new List<WeaponMount>();
                    p.loadouts.Add(empty);
                }
            }
            catch { }
        }

        private static void StripTrainerHud(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac))
                return;
            if (AircraftIdentity.IsJ35WyvernKey(AircraftIdentity.GetKey(ac))
                || AircraftIdentity.IsCompleteBiaAirframe(ac))
                return;
            if (!AircraftIdentity.HasBiaAvionics(ac.gameObject))
            {
                AircraftDefinition def = AircraftIdentity.TryGetDefinition(ac);
                if (def == null || !AircraftIdentity.HasBiaAvionics(def.unitPrefab))
                    return;
            }
            try
            {
                AircraftParameters p = ac.GetAircraftParameters();
                if (p == null)
                    return;
                p.StatusDisplay = null;
                p.HUDExtras = null;
            }
            catch { }
        }

        private static void StripCubeFlightHacks(Aircraft ac)
        {
            if (ac == null)
                return;
            DestroyBehavioursNamed(ac, "MagicTorqueController");
            DestroyBehavioursNamed(ac, "Repulsorlift");
        }

        private static void DestroyBehavioursNamed(Aircraft ac, string typeName)
        {
            if (ac == null || string.IsNullOrEmpty(typeName))
                return;
            MonoBehaviour[] all = null;
            try { all = ac.GetComponentsInChildren<MonoBehaviour>(true); }
            catch { return; }
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i];
                if (mb == null)
                    continue;
                try
                {
                    if (mb.GetType().Name != typeName)
                        continue;
                    UnityEngine.Object.Destroy(mb);
                }
                catch { }
            }
        }

        private static void FillHudPrefabs(Aircraft ac)
        {
            try
            {
                if (IsIcarus(ac)
                    && !AircraftIdentity.IsJ35WyvernKey(AircraftIdentity.GetKey(ac))
                    && !AircraftIdentity.IsCompleteBiaAirframe(ac))
                    return;
                AircraftParameters p = ac.GetAircraftParameters();
                if (p == null)
                    return;
                if (p.StatusDisplay != null && p.HUDExtras != null
                    && !AircraftIdentity.IsCompleteBiaAirframe(ac))
                    return;
                AircraftDefinition trainer = null;
                UnitDefinition unit = null;
                if (Encyclopedia.Lookup != null
                    && Encyclopedia.Lookup.TryGetValue("trainer", out unit))
                    trainer = unit as AircraftDefinition;
                if (trainer == null)
                    trainer = Encyclopedia.i != null ? FindTrainer(Encyclopedia.i) : null;
                AircraftParameters src = trainer != null ? trainer.aircraftParameters : null;
                if (src == null)
                    return;
                if (p.StatusDisplay == null || AircraftIdentity.IsCompleteBiaAirframe(ac))
                    p.StatusDisplay = src.StatusDisplay;
                if (p.HUDExtras == null || AircraftIdentity.IsCompleteBiaAirframe(ac))
                    p.HUDExtras = src.HUDExtras;
            }
            catch { }
        }

        private static AircraftDefinition FindTrainer(Encyclopedia enc)
        {
            if (enc == null || enc.aircraft == null)
                return null;
            for (int i = 0; i < enc.aircraft.Count; i++)
            {
                AircraftDefinition d = enc.aircraft[i];
                if (d != null && string.Equals(d.jsonKey, "trainer", StringComparison.Ordinal))
                    return d;
            }
            return null;
        }

        private static void FillTrainerWeapons(Aircraft ac)
        {
            if (ac == null || !IsIcarus(ac) || ac.weaponManager == null)
                return;
            if (AircraftIdentity.IsJ35WyvernKey(AircraftIdentity.GetKey(ac))
                || AircraftIdentity.IsCompleteBiaAirframe(ac))
                return;
            HardpointSet[] dest = ac.weaponManager.hardpointSets;
            if (dest == null || dest.Length == 0)
                return;
            if (CountLiveMounts(dest) >= 8)
                return;
            List<WeaponMount> catalog = LoadTrainerCatalog();
            if (catalog == null || catalog.Count < 2)
                return;
            for (int i = 0; i < dest.Length; i++)
            {
                HardpointSet set = dest[i];
                if (set == null)
                    continue;
                if (CountLiveInSet(set) >= 3)
                    continue;
                set.weaponOptions = new List<WeaponMount>(catalog);
            }
        }

        private static int CountLiveMounts(HardpointSet[] sets)
        {
            int n = 0;
            if (sets == null)
                return 0;
            for (int i = 0; i < sets.Length; i++)
                n += CountLiveInSet(sets[i]);
            return n;
        }

        private static int CountLiveInSet(HardpointSet set)
        {
            if (set == null || set.weaponOptions == null)
                return 0;
            int n = 0;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                if (set.weaponOptions[i] != null)
                    n++;
            }
            return n;
        }

        private static List<WeaponMount> LoadTrainerCatalog()
        {
            try
            {
                AircraftDefinition trainer = null;
                UnitDefinition unit = null;
                if (Encyclopedia.Lookup != null
                    && Encyclopedia.Lookup.TryGetValue("trainer", out unit))
                    trainer = unit as AircraftDefinition;
                if (trainer == null && Encyclopedia.i != null)
                    trainer = FindTrainer(Encyclopedia.i);
                if (trainer == null || trainer.unitPrefab == null)
                    return null;
                WeaponManager wm = trainer.unitPrefab.GetComponentInChildren<WeaponManager>(true);
                if (wm == null || wm.hardpointSets == null)
                    return null;
                List<WeaponMount> best = null;
                int bestN = 0;
                for (int i = 0; i < wm.hardpointSets.Length; i++)
                {
                    HardpointSet set = wm.hardpointSets[i];
                    int n = CountLiveInSet(set);
                    if (n > bestN)
                    {
                        bestN = n;
                        best = set.weaponOptions;
                    }
                }
                if (best == null)
                    return null;
                return new List<WeaponMount>(best);
            }
            catch
            {
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(Aircraft), "OnStartClient")]
    internal static class Patch_Bia_OnStartClient
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Aircraft __instance)
        {
            BiaSpawnGuards.Prepare(__instance);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.First)]
        private static Exception Finalizer(Aircraft __instance, Exception __exception)
        {
            BiaSpawnGuards.SettleIcarusSpawn(__instance);
            if (__exception != null)
                BiaSpawnGuards.AttachLocalCamera(__instance);
            return null;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "SetupLocalPlayerAndUI")]
    internal static class Patch_Bia_SetupLocalPlayerAndUI
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Aircraft __instance)
        {
            BiaSpawnGuards.Prepare(__instance);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.First)]
        private static Exception Finalizer(Aircraft __instance, Exception __exception)
        {
            BiaSpawnGuards.SettleIcarusSpawn(__instance);
            BiaSpawnGuards.AttachLocalCamera(__instance);
            return null;
        }
    }

    [HarmonyPatch(typeof(LoadoutSelector), "AssignAircraft")]
    internal static class Patch_Bia_LoadoutAssign
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Aircraft aircraft)
        {
            BiaSpawnGuards.Prepare(aircraft);
        }
    }

    /// <summary>
    /// Fast-template jets have no cockpit interior. Vanilla then disables
    /// every exterior renderer, so the camera sits in empty space and orbit
    /// looks like the plane vanished.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), "SetCockpitRenderers")]
    internal static class Patch_Bia_SetCockpitRenderers
    {
        private static readonly FieldInfo CockpitRenderersField =
            AccessTools.Field(typeof(Aircraft), "cockpitRenderers");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Aircraft __instance, bool enabled)
        {
            if (__instance == null || CockpitRenderersField == null)
                return true;
            Renderer[] interior = CockpitRenderersField.GetValue(__instance) as Renderer[];
            if (interior != null && interior.Length > 0)
                return true;
            try
            {
                if (__instance.engines != null)
                {
                    for (int i = 0; i < __instance.engines.Count; i++)
                    {
                        IEngine engine = __instance.engines[i];
                        if (engine != null)
                            engine.SetInteriorSounds(enabled);
                    }
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// Vanilla FlightHud.Update bails if cockpit.rb is still null, so the
    /// pitch ladder / compass never draw. Fall back to Aircraft.CockpitRB().
    /// </summary>
    [HarmonyPatch(typeof(FlightHud), "SetAircraft")]
    internal static class Patch_Bia_FlightHudSetAircraft
    {
        private static readonly FieldInfo CockpitRbField =
            AccessTools.Field(typeof(FlightHud), "cockpitRB");
        private static readonly FieldInfo CockpitTfField =
            AccessTools.Field(typeof(FlightHud), "cockpitTransform");
        private static readonly FieldInfo InputsField =
            AccessTools.Field(typeof(FlightHud), "inputs");

        [HarmonyPostfix]
        private static void Postfix(FlightHud __instance, Aircraft aircraft)
        {
            if (__instance == null || aircraft == null)
                return;
            try
            {
                if (CockpitRbField != null)
                {
                    Rigidbody rb = CockpitRbField.GetValue(__instance) as Rigidbody;
                    if (rb == null)
                    {
                        rb = aircraft.CockpitRB();
                        if (rb == null)
                            rb = aircraft.rb;
                        CockpitRbField.SetValue(__instance, rb);
                    }
                }
                if (CockpitTfField != null && CockpitTfField.GetValue(__instance) == null
                    && aircraft.cockpit != null)
                    CockpitTfField.SetValue(__instance, aircraft.cockpit.transform);
                if (InputsField != null && InputsField.GetValue(__instance) == null)
                    InputsField.SetValue(__instance, aircraft.GetInputs());
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Aircraft), "SetComplexPhysics")]
    internal static class Patch_Bia_IgnoreSelfCollisions
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Aircraft __instance)
        {
            BiaSpawnGuards.AfterPhysics(__instance);
        }
    }

    /// <summary>
    /// Fast-template AeroParts ship with joints: []. Vanilla CreateRB still
    /// unparents them, so wings/engine fall off the visual on spawn.
    /// </summary>
    [HarmonyPatch(typeof(AeroPart), "CreateJoints")]
    internal static class Patch_Bia_CreateJoints
    {
        private static readonly FieldInfo JointsField =
            AccessTools.Field(typeof(AeroPart), "joints");
        private static readonly FieldInfo AttachField =
            AccessTools.Field(typeof(UnitPart), "attachInfo");
        private static FieldInfo AttachParentField;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AeroPart __instance)
        {
            if (BiaSpawnGuards.IsBiaSharedRbPart(__instance))
                return false;
            if (__instance == null || JointsField == null)
                return true;
            PartJoint[] joints = null;
            try { joints = JointsField.GetValue(__instance) as PartJoint[]; }
            catch { return true; }
            if (joints != null && joints.Length > 0)
                return true;
            UnitPart parent = ReadAttachParent(__instance);
            if (parent == null)
                return true;
            PartJoint j = new PartJoint();
            j.connectedPart = parent;
            j.breakForce = 200000f;
            j.breakTorque = 200000f;
            j.solverIterations = 6;
            try { JointsField.SetValue(__instance, new PartJoint[] { j }); }
            catch { }
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(AeroPart __instance)
        {
            if (__instance == null)
                return;
            if (BiaSpawnGuards.IsBiaSharedRbPart(__instance))
                return;
            Aircraft ac = __instance.parentUnit as Aircraft;
            if (ac == null || __instance.rb == null || __instance.rb == ac.rb)
                return;
            FixedJoint[] existing = null;
            try { existing = __instance.GetComponents<FixedJoint>(); }
            catch { return; }
            if (existing != null && existing.Length > 0)
                return;
            UnitPart parent = ReadAttachParent(__instance);
            if (parent == null || parent.rb == null)
                return;
            try
            {
                FixedJoint fj = __instance.gameObject.AddComponent<FixedJoint>();
                fj.connectedBody = parent.rb;
                fj.breakForce = 2000000f;
                fj.breakTorque = 2000000f;
                fj.enableCollision = false;
            }
            catch { }
        }

        private static UnitPart ReadAttachParent(AeroPart part)
        {
            if (part == null || AttachField == null)
                return null;
            object info = null;
            try { info = AttachField.GetValue(part); }
            catch { return null; }
            if (info == null)
                return null;
            if (AttachParentField == null)
                AttachParentField = AccessTools.Field(info.GetType(), "parentPart");
            if (AttachParentField == null)
                return null;
            try { return AttachParentField.GetValue(info) as UnitPart; }
            catch { return null; }
        }
    }

    /// <summary>
    /// Trainer StatusDisplay overlays default to yellow (damaged). J-35 part
    /// names never match cockpit_F / engine_L, so every tile stays "destroyed".
    /// </summary>
    [HarmonyPatch(typeof(StatusDisplay), "Initialize")]
    internal static class Patch_Bia_StatusDisplayInit
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(StatusDisplay __instance, Aircraft aircraft)
        {
            if (aircraft == null || !BiaSpawnGuards.IsIcarus(aircraft))
                return true;
            if (AircraftIdentity.IsCompleteBiaAirframe(aircraft))
                return true;
            try
            {
                if (__instance != null)
                    __instance.gameObject.SetActive(false);
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraCockpitState), "EnterState")]
    internal static class Patch_Bia_CockpitEnter
    {
        private static readonly FieldInfo CockpitRenderersField =
            AccessTools.Field(typeof(Aircraft), "cockpitRenderers");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CameraStateManager cam)
        {
            if (cam == null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            BiaSpawnGuards.EnsureFollow(cam);
            BiaSpawnGuards.EnsureLivePhysics(ac);
            BiaSpawnGuards.PlaceCockpitAtNose(ac);
        }

        [HarmonyPostfix]
        private static void Postfix(CameraStateManager cam)
        {
            if (cam == null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            try { CameraStateManager.cameraMode = CameraMode.cockpit; }
            catch { }
            if (ac != null && ac.cockpitViewPoint != null)
            {
                try
                {
                    if (cam.cameraPivot != null
                        && cam.cameraPivot.parent != ac.cockpitViewPoint)
                    {
                        cam.cameraPivot.SetParent(ac.cockpitViewPoint);
                        cam.cameraPivot.localPosition = Vector3.zero;
                        cam.cameraPivot.localRotation = Quaternion.identity;
                    }
                    if (cam.transform.parent != cam.cameraPivot)
                    {
                        cam.transform.SetParent(cam.cameraPivot);
                        cam.transform.localPosition = Vector3.zero;
                        cam.transform.localRotation = Quaternion.identity;
                    }
                }
                catch { }
            }
            if (cam.mainCamera == null)
                return;
            Renderer[] interior = null;
            if (CockpitRenderersField != null && ac != null)
            {
                try { interior = CockpitRenderersField.GetValue(ac) as Renderer[]; }
                catch { }
            }
            if (interior != null && interior.Length > 0)
                return;
            try
            {
                cam.mainCamera.nearClipPlane = 0.2f;
            }
            catch { }
            try
            {
                if (cam.cockpitCamRender != null)
                    cam.cockpitCamRender.enabled = true;
            }
            catch { }
            try { FlightHud.EnableCanvas(true); }
            catch { }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            return null;
        }
    }

    [HarmonyPatch(typeof(CameraChaseState), "Initialize")]
    internal static class Patch_Bia_ChaseInit
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            BiaChaseCameras.EnsureEncyclopedia();
        }
    }

    [HarmonyPatch(typeof(CameraChaseState), "EnterState")]
    internal static class Patch_Bia_ChaseEnter
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CameraStateManager cam)
        {
            if (cam == null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            BiaChaseCameras.EnsureAircraft(ac);
            BiaSpawnGuards.EnsureLivePhysics(ac);
            try
            {
                if (ac != null && ac.rb != null)
                    cam.followingRB = ac.rb;
            }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix(CameraStateManager cam)
        {
            if (cam == null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            BiaSpawnGuards.ParentCamToAirframe(cam, ac);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(CameraStateManager cam, Exception __exception)
        {
            if (__exception != null)
            {
                Aircraft ac = cam != null ? cam.followingUnit as Aircraft : null;
                BiaSpawnGuards.ParentCamToAirframe(cam, ac);
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(CameraChaseState), "CheckInput")]
    internal static class Patch_Bia_ChaseInput
    {
        [HarmonyPrefix]
        private static void Prefix(CameraStateManager cam)
        {
            if (cam == null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            BiaChaseCameras.EnsureAircraft(ac);
        }
    }

    internal static class BiaChaseCameras
    {
        private static readonly FieldInfo DictField =
            AccessTools.Field(typeof(CameraChaseState), "AircraftCameraPos");
        private static readonly Type PosType =
            typeof(CameraChaseState).GetNestedType("CameraPos", BindingFlags.NonPublic);
        private static readonly Type PovType = PosType != null
            ? PosType.GetNestedType("POV", BindingFlags.Public | BindingFlags.NonPublic)
            : null;

        internal static void EnsureAircraft(Aircraft ac)
        {
            if (ac == null)
                return;
            AircraftDefinition def = ac.definition as AircraftDefinition;
            Ensure(def);
            try
            {
                Ensure(IcarusAircraftRegistry.EncyclopediaInstance(def));
            }
            catch { }
        }

        internal static void EnsureEncyclopedia()
        {
            try
            {
                Encyclopedia enc = Encyclopedia.i;
                if (enc == null || enc.aircraft == null)
                    return;
                for (int i = 0; i < enc.aircraft.Count; i++)
                    Ensure(enc.aircraft[i]);
            }
            catch { }
        }

        internal static void Ensure(AircraftDefinition def)
        {
            if (def == null || DictField == null || PosType == null || PovType == null)
                return;
            CameraStateManager csm = null;
            try { csm = SceneSingleton<CameraStateManager>.i; }
            catch { return; }
            if (csm == null || csm.chaseState == null)
                return;
            IDictionary map = DictField.GetValue(csm.chaseState) as IDictionary;
            if (map == null)
                return;
            if (map.Contains(def))
                return;
            object pos = BuildPos(def);
            if (pos == null)
                return;
            try { map[def] = pos; }
            catch { }
        }

        private static object BuildPos(AircraftDefinition item)
        {
            float num = Mathf.Max(item.length, item.width * 0.7f)
                + Mathf.Max(item.width, item.length * 0.7f);
            if (num < 8f)
                num = 8f;
            object value = Activator.CreateInstance(PosType);
            SetPov(value, "back", new Vector3(0f, 0.1f * num, -num), Vector3.forward);
            SetPov(value, "front", new Vector3(0f, 0f, num), -Vector3.forward);
            SetPov(value, "top", new Vector3(0f, num, 0f), -Vector3.up + 0.1f * Vector3.forward);
            SetPov(value, "bottom", new Vector3(0f, -num, 0f), Vector3.up + 0.1f * Vector3.forward);
            SetPov(value, "belly", new Vector3(0f, -0.1f * num, -0.25f * num), Vector3.forward);
            SetPov(value, "tail", new Vector3(0f, 0.1f * num, -0.25f * num), Vector3.forward);
            SetPov(value, "wingL", new Vector3(-num, 0f, 0f), Vector3.right);
            SetPov(value, "wingR", new Vector3(num, 0f, 0f), -Vector3.right);
            SetPov(value, "wingRootL", new Vector3(-0.1f * num, 0.05f * num, 0f), Vector3.forward);
            SetPov(value, "wingRootR", new Vector3(0.1f * num, 0.05f * num, 0f), Vector3.forward);
            return value;
        }

        private static void SetPov(object camPos, string field, Vector3 pos, Vector3 ori)
        {
            FieldInfo f = AccessTools.Field(PosType, field);
            if (f == null || camPos == null)
                return;
            object pov = Activator.CreateInstance(PovType);
            FieldInfo p = AccessTools.Field(PovType, "position");
            FieldInfo o = AccessTools.Field(PovType, "orientation");
            if (p != null)
                p.SetValue(pov, pos);
            if (o != null)
                o.SetValue(pov, ori);
            f.SetValue(camPos, pov);
        }
    }

    [HarmonyPatch(typeof(AeroPart), "CreateRB")]
    internal static class Patch_Bia_FuselageCreateRB
    {
        private static readonly FieldInfo SimplePhysicsField =
            AccessTools.Field(typeof(AeroPart), "simplePhysics");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AeroPart __instance)
        {
            if (!BiaSpawnGuards.IsIcarusFuselage(__instance))
                return true;
            Aircraft ac = __instance.parentUnit as Aircraft;
            if (ac == null || ac.rb == null)
                return true;
            try
            {
                __instance.rb = ac.rb;
                if (SimplePhysicsField != null)
                    SimplePhysicsField.SetValue(__instance, false);
            }
            catch
            {
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Turbojet), "Awake")]
    internal static class Patch_Bia_TurbojetAwake
    {
        private static readonly FieldInfo CritField =
            AccessTools.Field(typeof(Turbojet), "criticalParts");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Turbojet __instance)
        {
            if (__instance == null)
                return;
            Aircraft ac = null;
            try { ac = __instance.GetComponentInParent<Aircraft>(); }
            catch { }
            AeroPart part = null;
            try { part = __instance.GetComponent<AeroPart>(); }
            catch { }
            if (part != null && part.parentUnit == null && ac != null)
                part.parentUnit = ac;
            if (CritField == null)
                return;
            UnitPart[] crit = null;
            try { crit = CritField.GetValue(__instance) as UnitPart[]; }
            catch { }
            if (crit == null || crit.Length == 0 || crit[0] == null)
            {
                if (part == null)
                    return;
                try { CritField.SetValue(__instance, new UnitPart[] { part }); }
                catch { }
                crit = new UnitPart[] { part };
            }
            if (crit[0] != null && crit[0].parentUnit == null && ac != null)
                crit[0].parentUnit = ac;
            if (ac != null)
            {
                try
                {
                    if (ac.engineStates == null)
                        ac.engineStates = new List<IEngine>();
                    if (ac.engines == null)
                        ac.engines = new List<IEngine>();
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(JetNozzle), "Awake")]
    internal static class Patch_Bia_JetNozzleAwake
    {
        private static readonly FieldInfo PartField =
            AccessTools.Field(typeof(JetNozzle), "part");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(JetNozzle __instance)
        {
            if (__instance == null || PartField == null)
                return;
            UnitPart part = null;
            try { part = PartField.GetValue(__instance) as UnitPart; }
            catch { }
            if (part == null)
            {
                try
                {
                    part = __instance.GetComponentInParent<AeroPart>();
                    if (part != null)
                        PartField.SetValue(__instance, part);
                }
                catch { }
            }
            if (part != null && part.parentUnit == null)
            {
                Aircraft ac = null;
                try { ac = __instance.GetComponentInParent<Aircraft>(); }
                catch { }
                if (ac != null)
                    part.parentUnit = ac;
            }
        }
    }

    [HarmonyPatch(typeof(CameraCockpitState), "UpdateState")]
    internal static class Patch_Bia_CockpitUpdate
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CameraStateManager cam)
        {
            BiaSpawnGuards.EnsureFollow(cam);
        }
    }

    [HarmonyPatch(typeof(CameraControlUI), "SetCamCockpit")]
    internal static class Patch_Bia_CamMenuCockpit
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { BiaSpawnGuards.EnsureFollow(SceneSingleton<CameraStateManager>.i); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CameraControlUI), "SetCamOrbit")]
    internal static class Patch_Bia_CamMenuOrbit
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { BiaSpawnGuards.EnsureFollow(SceneSingleton<CameraStateManager>.i); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CameraControlUI), "SetCamChase")]
    internal static class Patch_Bia_CamMenuChase
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { BiaSpawnGuards.EnsureFollow(SceneSingleton<CameraStateManager>.i); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CameraControlUI), "SetCamFlyby")]
    internal static class Patch_Bia_CamMenuFlyby
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { BiaSpawnGuards.EnsureFollow(SceneSingleton<CameraStateManager>.i); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "SpawnWeapons")]
    internal static class Patch_Bia_SpawnWeapons
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(WeaponManager __instance)
        {
            if (__instance == null)
                return;
            Aircraft ac = null;
            try { ac = __instance.GetComponent<Aircraft>(); }
            catch { }
            BiaSpawnGuards.EnsureDefaultLoadout(ac);
        }
    }

    [HarmonyPatch(typeof(Turbojet), "FixedUpdate")]
    internal static class Patch_Bia_TurbojetThrust
    {
        private static readonly FieldInfo HasFuelField =
            AccessTools.Field(typeof(Turbojet), "hasFuel");
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(Turbojet), "aircraft");
        private static readonly FieldInfo InputsField =
            AccessTools.Field(typeof(Turbojet), "controlInputs");
        private static readonly FieldInfo ThrustField =
            AccessTools.Field(typeof(Turbojet), "thrust");

        [HarmonyPrefix]
        private static void Prefix(Turbojet __instance)
        {
            if (__instance == null)
                return;
            Aircraft ac = null;
            try { ac = AircraftField != null ? AircraftField.GetValue(__instance) as Aircraft : null; }
            catch { }
            if (ac == null)
            {
                try { ac = __instance.GetComponentInParent<Aircraft>(); }
                catch { }
                if (AircraftField != null && ac != null)
                {
                    try { AircraftField.SetValue(__instance, ac); }
                    catch { }
                }
            }
            if (!BiaSpawnGuards.IsIcarus(ac))
                return;
            if (!BiaSpawnGuards.IsPhysicsWired(ac))
                BiaSpawnGuards.EnsureLivePhysics(ac);
            if (InputsField != null)
            {
                try
                {
                    if (InputsField.GetValue(__instance) == null)
                        InputsField.SetValue(__instance, ac.GetInputs());
                }
                catch { }
            }
            if (HasFuelField != null)
            {
                try
                {
                    bool fuel = (bool)HasFuelField.GetValue(__instance);
                    if (!fuel)
                        HasFuelField.SetValue(__instance, true);
                }
                catch { }
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Turbojet __instance)
        {
            if (__instance == null || ThrustField == null)
                return;
            Aircraft ac = null;
            try { ac = AircraftField != null ? AircraftField.GetValue(__instance) as Aircraft : null; }
            catch { }
            if (!BiaSpawnGuards.IsIcarus(ac) || ac.rb == null)
                return;
            bool ign = false;
            try { ign = ac.Ignition; }
            catch { return; }
            if (!ign)
                return;
            float thrust = 0f;
            try { thrust = (float)ThrustField.GetValue(__instance); }
            catch { return; }
            bool localSim = false;
            try { localSim = ac.LocalSim; }
            catch { }
            if (localSim && thrust > 1f)
                return;
            float throttle = 0f;
            try
            {
                ControlInputs inputs = ac.GetInputs();
                if (inputs != null)
                    throttle = Mathf.Clamp01(inputs.throttle);
            }
            catch { }
            if (throttle < 0.04f)
                return;
            float maxT = 0f;
            try { maxT = __instance.maxThrust; }
            catch { }
            if (maxT < 100f)
                maxT = 31600f;
            try
            {
                ac.rb.AddForce(ac.transform.forward * maxT * throttle);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(JetNozzle), "Thrust")]
    internal static class Patch_Bia_JetNozzleThrust
    {
        private static readonly FieldInfo PartField =
            AccessTools.Field(typeof(JetNozzle), "part");
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(JetNozzle), "aircraft");
        private static readonly FieldInfo XfField =
            AccessTools.Field(typeof(JetNozzle), "thrustTransform");

        [HarmonyPrefix]
        private static void Prefix(JetNozzle __instance)
        {
            if (__instance == null)
                return;
            UnitPart part = null;
            try { part = PartField != null ? PartField.GetValue(__instance) as UnitPart : null; }
            catch { }
            Aircraft ac = null;
            try { ac = AircraftField != null ? AircraftField.GetValue(__instance) as Aircraft : null; }
            catch { }
            if (ac == null)
            {
                try { ac = __instance.GetComponentInParent<Aircraft>(); }
                catch { }
                if (AircraftField != null && ac != null)
                {
                    try { AircraftField.SetValue(__instance, ac); }
                    catch { }
                }
            }
            if (!BiaSpawnGuards.IsIcarus(ac))
                return;
            if (part != null && part.rb == null && ac.rb != null)
            {
                try { part.rb = ac.rb; }
                catch { }
            }
            if (XfField != null)
            {
                try
                {
                    if (XfField.GetValue(__instance) == null)
                        XfField.SetValue(__instance, __instance.transform);
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(CameraOrbitState), "EnterState")]
    internal static class Patch_Bia_OrbitEnter
    {
        [HarmonyPrefix]
        private static void Prefix(CameraStateManager cam)
        {
            BiaSpawnGuards.EnsureFollow(cam);
            if (cam == null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            if (ac == null || ac.rb == null)
                return;
            try { cam.followingRB = ac.rb; }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix(CameraStateManager cam)
        {
            if (cam == null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            BiaSpawnGuards.ParentCamToAirframe(cam, ac);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(CameraStateManager cam, Exception __exception)
        {
            if (__exception != null)
            {
                Aircraft ac = cam != null ? cam.followingUnit as Aircraft : null;
                BiaSpawnGuards.ParentCamToAirframe(cam, ac);
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(CameraOrbitState), "UpdateState")]
    internal static class Patch_Bia_OrbitSwitchView
    {
        [HarmonyPrefix]
        private static void Prefix(CameraStateManager cam)
        {
            if (cam == null)
                return;
            if (cam.followingRB == null)
            {
                Aircraft follow = cam.followingUnit as Aircraft;
                if (follow != null && follow.rb != null)
                {
                    try { cam.followingRB = follow.rb; }
                    catch { }
                }
            }
        }

        [HarmonyPostfix]
        private static void Postfix(CameraStateManager cam)
        {
            if (cam == null || cam.currentState != cam.orbitState)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            if (!BiaSpawnGuards.IsIcarus(ac))
                return;
            try
            {
                if (BiaSpawnGuards.ButtonDown("Center") && cam.chaseState != null)
                    cam.SwitchState(cam.chaseState);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CameraChaseState), "UpdateState")]
    internal static class Patch_Bia_ChaseFollowRb
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CameraStateManager cam)
        {
            if (cam == null || cam.followingRB != null)
                return;
            Aircraft ac = cam.followingUnit as Aircraft;
            if (ac == null || ac.rb == null)
                return;
            try { cam.followingRB = ac.rb; }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Aircraft), "SpawnedInPosition")]
    internal static class Patch_Bia_SpawnedInPosition
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            BiaSpawnGuards.RecalcIcarusRadarAlt(__instance);
            BiaSpawnGuards.SettleIcarusSpawn(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "CheckRadarAlt")]
    internal static class Patch_Bia_CheckRadarAlt
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            BiaSpawnGuards.RecalcIcarusRadarAlt(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    internal static class Patch_Bia_SpawnGrace
    {
        // Hot path: every Aircraft in the mission hits this. Keep it tiny.
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Aircraft __instance)
        {
            if (__instance == null)
                return;
            // Skip vanilla fleet — Icarus/BIA spawn wiring only.
            if (!BiaSpawnGuards.IsIcarus(__instance))
                return;
            BiaSpawnGuards.EnsureLivePhysics(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            if (__instance == null)
                return;
            // Grace dict empty + not local: zero work (baseline fleet).
            if (!BiaSpawnGuards.HasAnySpawnGrace)
            {
                Aircraft loc = AircraftIdentity.CachedLocal();
                if (loc == null || !object.ReferenceEquals(loc, __instance))
                    return;
                EngineStartService.ApplyApuThrust(__instance);
                ReverseThrustService.ApplyAirframeReverse(__instance);
                return;
            }
            BiaSpawnGuards.TickSpawnGrace(__instance);
            Aircraft local = AircraftIdentity.CachedLocal();
            if (local == null || !object.ReferenceEquals(local, __instance))
                return;
            EngineStartService.ApplyApuThrust(__instance);
            ReverseThrustService.ApplyAirframeReverse(__instance);
        }
    }

    [HarmonyPatch(typeof(LandingGear), "Awake")]
    internal static class Patch_Bia_LandingGearAwake
    {
        private static readonly FieldInfo ColliderField =
            AccessTools.Field(typeof(LandingGear), "gearCollider");
        private static readonly FieldInfo TravelField =
            AccessTools.Field(typeof(LandingGear), "suspensionTravel");
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(LandingGear), "aircraft");
        private static readonly FieldInfo AttachedField =
            AccessTools.Field(typeof(LandingGear), "attachedPart");
        private static readonly FieldInfo ContactAreaField =
            AccessTools.Field(typeof(LandingGear), "contactArea");
        private static readonly FieldInfo RollingField =
            AccessTools.Field(typeof(LandingGear), "rollingResistance");
        private static readonly FieldInfo AlignField =
            AccessTools.Field(typeof(LandingGear), "aligningStrength");

        private static bool IsBiaGear(LandingGear gear)
        {
            if (gear == null)
                return false;
            Aircraft ac = null;
            try { ac = gear.GetComponentInParent<Aircraft>(); }
            catch { ac = null; }
            if (ac == null && AircraftField != null)
            {
                try { ac = AircraftField.GetValue(gear) as Aircraft; }
                catch { ac = null; }
            }
            return BiaSpawnGuards.IsIcarus(ac) && !AircraftIdentity.IsCompleteBiaAirframe(ac);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(LandingGear __instance)
        {
            if (__instance == null || !IsBiaGear(__instance))
                return;
            Aircraft ac = null;
            try { ac = __instance.GetComponentInParent<Aircraft>(); }
            catch { }
            if (AircraftField != null && AircraftField.GetValue(__instance) == null && ac != null)
                AircraftField.SetValue(__instance, ac);
            if (AttachedField != null && AttachedField.GetValue(__instance) == null)
            {
                AeroPart part = __instance.GetComponentInParent<AeroPart>();
                if (part != null)
                    AttachedField.SetValue(__instance, part);
            }
            if (TravelField != null)
            {
                try
                {
                    float travel = (float)TravelField.GetValue(__instance);
                    if (travel < 0.45f)
                        TravelField.SetValue(__instance, 0.65f);
                }
                catch { }
            }
            if (ContactAreaField != null)
            {
                try
                {
                    float area = (float)ContactAreaField.GetValue(__instance);
                    if (area < 0.005f)
                        ContactAreaField.SetValue(__instance, 0.02f);
                }
                catch { }
            }
            if (RollingField != null)
            {
                try
                {
                    float roll = (float)RollingField.GetValue(__instance);
                    if (roll < 0.001f)
                        RollingField.SetValue(__instance, 0.02f);
                }
                catch { }
            }
            if (AlignField != null)
            {
                try
                {
                    float align = (float)AlignField.GetValue(__instance);
                    if (align < 0.5f)
                        AlignField.SetValue(__instance, 5f);
                }
                catch { }
            }
        }

        [HarmonyPostfix]
        private static void Postfix(LandingGear __instance)
        {
            if (__instance == null || !IsBiaGear(__instance))
                return;
            if (ColliderField != null && ColliderField.GetValue(__instance) == null)
            {
                try
                {
                    SphereCollider sc = __instance.GetComponent<SphereCollider>();
                    if (sc == null)
                        sc = __instance.gameObject.AddComponent<SphereCollider>();
                    sc.radius = 0.22f;
                    sc.isTrigger = false;
                    sc.enabled = true;
                    ColliderField.SetValue(__instance, sc);
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(LandingGear), "BreakWheel")]
    internal static class Patch_Bia_LandingGearBreak
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(LandingGear __instance)
        {
            if (__instance == null)
                return false;
            try
            {
                FieldInfo dust = AccessTools.Field(typeof(LandingGear), "dust");
                FieldInfo col = AccessTools.Field(typeof(LandingGear), "gearCollider");
                if (dust != null && dust.GetValue(__instance) == null)
                    return false;
                if (col != null && col.GetValue(__instance) == null)
                    return false;
            }
            catch
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(LandingGear), "FixedUpdate")]
    internal static class Patch_Bia_LandingGearFixedUpdate
    {
        private static readonly FieldInfo ColliderField =
            AccessTools.Field(typeof(LandingGear), "gearCollider");
        private static readonly HashSet<int> GearOk = new HashSet<int>();

        // Prefix instead of Finalizer: Harmony Finalizer wraps every gear tick in
        // try/catch and tanks large missions. Skip vanilla FU only when a BIA
        // wheel has no collider (the NRE Awake already tries to fill).
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(LandingGear __instance)
        {
            if (__instance == null)
                return false;
            int id = __instance.GetInstanceID();
            if (GearOk.Contains(id))
                return true;
            if (ColliderField != null)
            {
                try
                {
                    if (ColliderField.GetValue(__instance) != null)
                    {
                        GearOk.Add(id);
                        return true;
                    }
                }
                catch { }
            }
            Aircraft ac = null;
            try { ac = __instance.GetComponentInParent<Aircraft>(); }
            catch { ac = null; }
            if (AircraftIdentity.IsCompleteBiaAirframe(ac))
            {
                GearOk.Add(id);
                return true;
            }
            return !BiaSpawnGuards.IsIcarus(ac);
        }
    }

    /// <summary>
    /// Oritasy baas import nulls afterburner materials. Vanilla Run NREs
    /// every FixedUpdate and kills JetNozzle.Thrust (tumble / hangar abort).
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_Bia_AfterburnerRun
    {
        private static FieldInfo FlameField;
        private static FieldInfo GlowField;

        private static MethodBase TargetMethod()
        {
            Type inner = AccessTools.Inner(typeof(JetNozzle), "Afterburner");
            if (inner == null)
                return null;
            FlameField = AccessTools.Field(inner, "flameRenderer");
            GlowField = AccessTools.Field(inner, "nozzleGlowRenderer");
            return AccessTools.Method(inner, "Run",
                new Type[] { typeof(float), typeof(float), typeof(float) });
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(object __instance)
        {
            if (__instance == null)
                return false;
            try
            {
                Renderer flame = FlameField != null
                    ? FlameField.GetValue(__instance) as Renderer : null;
                Renderer glow = GlowField != null
                    ? GlowField.GetValue(__instance) as Renderer : null;
                if (flame == null)
                    return false;
                if (flame.sharedMaterial == null)
                    return false;
                if (glow != null && glow.sharedMaterial == null)
                    return false;
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}
