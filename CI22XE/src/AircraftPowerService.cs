using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield engine / fuel power service.
    /// Owns baselines + absolute F1 re-apply. Written from scratch for 0.0.9.56.
    /// </summary>
    internal static class AircraftPowerService
    {
        private static readonly Dictionary<int, Plugin.PowerApplyState> PowerState =
            new Dictionary<int, Plugin.PowerApplyState>();
        private static readonly Dictionary<int, float> ThrustBaseline = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> FuelBurnBaseline = new Dictionary<int, float>();
        private static readonly HashSet<int> LiveXeIds = new HashSet<int>();

        internal static void RegisterLiveXe(Aircraft aircraft)
        {
            if (aircraft == null)
                return;
            int id = aircraft.GetInstanceID();
            if (!LiveXeIds.Add(id))
                return;
            Plugin.LiveXeAircraft.Add(aircraft);
        }

        internal static void UnregisterLiveXe(Aircraft aircraft)
        {
            if (aircraft == null)
                return;
            int id = aircraft.GetInstanceID();
            if (!LiveXeIds.Remove(id))
                return;
            PowerState.Remove(id);
            AirframeStrengthService.Unregister(aircraft);
            // Unity recycles InstanceIDs — drop Touched + baselines or the next aircraft
            // reusing this id can skip thrust buffs and poison T/W.
            ClearComponentTouchesAndBaselines(aircraft);
            Plugin.Touched.RemoveAircraft(id);
            for (int i = Plugin.LiveXeAircraft.Count - 1; i >= 0; i--)
            {
                Aircraft a = Plugin.LiveXeAircraft[i];
                if (a == null || a.GetInstanceID() == id)
                    Plugin.LiveXeAircraft.RemoveAt(i);
            }
        }

        private static void ClearComponentTouchesAndBaselines(Aircraft aircraft)
        {
            if (aircraft == null)
                return;
            TurbineEngine[] engines = aircraft.GetComponentsInChildren<TurbineEngine>(true);
            for (int i = 0; i < engines.Length; i++)
            {
                if (engines[i] == null) continue;
                int cid = engines[i].GetInstanceID();
                Plugin.Touched.RemoveEngine(cid);
                ThrustBaseline.Remove(cid);
                FuelBurnBaseline.Remove(cid);
            }
            ConstantSpeedProp[] props = aircraft.GetComponentsInChildren<ConstantSpeedProp>(true);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i] == null) continue;
                int cid = props[i].GetInstanceID();
                Plugin.Touched.RemoveProp(cid);
                ThrustBaseline.Remove(cid);
            }
            PropFan[] propFans = aircraft.GetComponentsInChildren<PropFan>(true);
            for (int i = 0; i < propFans.Length; i++)
            {
                if (propFans[i] == null) continue;
                int cid = propFans[i].GetInstanceID();
                Plugin.Touched.RemovePropFan(cid);
                ThrustBaseline.Remove(cid);
            }
            RotorShaft[] rotors = aircraft.GetComponentsInChildren<RotorShaft>(true);
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null) continue;
                int cid = rotors[i].GetInstanceID();
                Plugin.Touched.RemoveRotor(cid);
                ThrustBaseline.Remove(cid);
                ThrustBaseline.Remove(cid ^ 0x400000);
            }
            DuctedFan[] ducted = aircraft.GetComponentsInChildren<DuctedFan>(true);
            for (int i = 0; i < ducted.Length; i++)
            {
                if (ducted[i] == null) continue;
                int cid = ducted[i].GetInstanceID();
                Plugin.Touched.RemoveDucted(cid);
                ThrustBaseline.Remove(cid);
                ThrustBaseline.Remove(cid ^ 0x100000);
                ThrustBaseline.Remove(cid ^ 0x200000);
            }
            Turbofan[] fans = aircraft.GetComponentsInChildren<Turbofan>(true);
            for (int i = 0; i < fans.Length; i++)
            {
                if (fans[i] == null) continue;
                int cid = fans[i].GetInstanceID();
                Plugin.Touched.RemoveTurbofan(cid);
                ThrustBaseline.Remove(cid);
                FuelBurnBaseline.Remove(cid);
            }
            Turbojet[] jets = aircraft.GetComponentsInChildren<Turbojet>(true);
            for (int i = 0; i < jets.Length; i++)
            {
                if (jets[i] == null) continue;
                int cid = jets[i].GetInstanceID();
                Plugin.Touched.RemoveTurbojet(cid);
                ThrustBaseline.Remove(cid);
                FuelBurnBaseline.Remove(cid);
            }
            LandingGear[] gears = aircraft.GetComponentsInChildren<LandingGear>(true);
            for (int i = 0; i < gears.Length; i++)
            {
                if (gears[i] == null) continue;
                Plugin.Touched.RemoveGear(gears[i].GetInstanceID());
            }
            FuelTank[] tanks = aircraft.GetComponentsInChildren<FuelTank>(true);
            for (int i = 0; i < tanks.Length; i++)
            {
                if (tanks[i] == null) continue;
                Plugin.Touched.RemoveTank(tanks[i].GetInstanceID());
            }
        }

        internal static void TrySetupAircraft(Aircraft aircraft)
        {
            if (!AircraftIdentity.IsXeAircraft(aircraft) || !Plugin.IsRuntimeInstance(aircraft))
                return;

            RegisterLiveXe(aircraft);
            bool first = Plugin.Touched.AddAircraft(aircraft.GetInstanceID());
            bool coin = AircraftIdentity.IsCoinAircraft(aircraft);
            ManeuverProfile profile = FlightEnvelopeService.GetOrCreateProfile(aircraft);
            float fuelCapMul = profile.FuelCapMul.Value;

            if (first && EngineReflection.AircraftFuelCapacity != null
                && Mathf.Abs(fuelCapMul - 1f) > 0.0001f)
            {
                try
                {
                    float cap = (float)EngineReflection.AircraftFuelCapacity.GetValue(aircraft);
                    if (cap > 0f)
                        EngineReflection.AircraftFuelCapacity.SetValue(aircraft, cap * fuelCapMul);
                }
                catch { }
            }

            if (coin && aircraft.weaponManager != null)
                Plugin.RegisterHardpointSets(aircraft.weaponManager);

            if (first)
            {
                BuffAllEnginesOn(aircraft);

                if (coin)
                {
                    LandingGear[] gears = aircraft.GetComponentsInChildren<LandingGear>(true);
                    for (int i = 0; i < gears.Length; i++)
                        TryBuffGear(gears[i]);
                }

                FuelTank[] tanks = aircraft.GetComponentsInChildren<FuelTank>(true);
                for (int i = 0; i < tanks.Length; i++)
                    TryBuffFuel(tanks[i], fuelCapMul);

                // Joint / impact durability ×N — never changes mass / rb.mass.
                AirframeStrengthService.TryBuff(aircraft);

                InitPowerState(aircraft, profile);
            }

            FlightEnvelopeService.ApplyLimits(aircraft);
            ApplyPowerProfile(aircraft);
        }

        private static void InitPowerState(Aircraft aircraft, ManeuverProfile profile)
        {
            if (aircraft == null || profile == null)
                return;
            PowerState[aircraft.GetInstanceID()] = new Plugin.PowerApplyState
            {
                Thrust = profile.ThrustMul.Value,
                FuelBurn = profile.FuelBurnMul.Value,
                FuelCap = profile.FuelCapMul.Value
            };
        }

        internal static void ApplyPowerProfile(Aircraft aircraft)
        {
            if (aircraft == null || !AircraftIdentity.IsXeAircraft(aircraft))
                return;
            ManeuverProfile profile = FlightEnvelopeService.GetOrCreateProfile(aircraft);
            int id = aircraft.GetInstanceID();
            Plugin.PowerApplyState st;
            // prev* is the last multiplier WE applied. Never invent DefaultThrustMul here —
            // that poisoned baselines (vanilla/default → under-thrust) when InstanceIDs recycled.
            float prevThrust = 1f;
            float prevBurn = 1f;
            float prevCap = 1f;
            if (PowerState.TryGetValue(id, out st) && st != null)
            {
                prevThrust = Mathf.Max(0.05f, st.Thrust);
                prevBurn = Mathf.Max(0.05f, st.FuelBurn);
                prevCap = Mathf.Max(0.05f, st.FuelCap);
            }
            else
            {
                st = new Plugin.PowerApplyState();
                PowerState[id] = st;
            }

            float thrustTarget = Mathf.Max(0.05f, profile.ThrustMul.Value)
                * KillChoiceRewardService.ThrustOverlayMul();
            float burnTarget = Mathf.Max(0.05f, profile.FuelBurnMul.Value)
                * KillChoiceRewardService.FuelBurnOverlayMul();
            float capTarget = Mathf.Max(0.05f, profile.FuelCapMul.Value);

            ApplyAbsoluteEngineThrust(aircraft, thrustTarget, prevThrust);
            ApplyAbsoluteEngineFuelBurn(aircraft, burnTarget, prevBurn);

            float capScale = capTarget / prevCap;
            if (Mathf.Abs(capScale - 1f) > 0.0005f)
                ScaleFuelCapacity(aircraft, capScale);

            st.Thrust = thrustTarget;
            st.FuelBurn = burnTarget;
            st.FuelCap = capTarget;
        }

        private static void CaptureThrustBaseline(int componentId, float vanillaValue)
        {
            if (componentId == 0 || vanillaValue <= 0f)
                return;
            if (!ThrustBaseline.ContainsKey(componentId))
                ThrustBaseline[componentId] = vanillaValue;
        }

        private static void CaptureFuelBurnBaseline(int componentId, float vanillaValue)
        {
            if (componentId == 0 || vanillaValue <= 0f)
                return;
            if (!FuelBurnBaseline.ContainsKey(componentId))
                FuelBurnBaseline[componentId] = vanillaValue;
        }

        private static void SetScaledField(FieldInfo field, object target, int componentId, float mul)
        {
            if (field == null || target == null)
                return;
            try
            {
                float baseline;
                if (!ThrustBaseline.TryGetValue(componentId, out baseline) || baseline <= 0f)
                {
                    baseline = (float)field.GetValue(target);
                    CaptureThrustBaseline(componentId, baseline);
                }
                field.SetValue(target, baseline * Mathf.Max(0.05f, mul));
            }
            catch { }
        }

        private static void ApplyAbsoluteEngineThrust(Aircraft aircraft, float mul, float prevMul)
        {
            if (aircraft == null)
                return;
            mul = Mathf.Max(0.05f, mul);
            prevMul = Mathf.Max(0.05f, prevMul);

            TurbineEngine[] engines = aircraft.GetComponentsInChildren<TurbineEngine>(true);
            for (int i = 0; i < engines.Length; i++)
            {
                TurbineEngine e = engines[i];
                if (e == null)
                    continue;
                int cid = e.GetInstanceID();
                float b;
                if (!ThrustBaseline.TryGetValue(cid, out b) || b <= 0f)
                {
                    // Infer vanilla from current / previously applied mul (1 if unknown).
                    float inferred = e.maxPower / Mathf.Max(0.05f, prevMul);
                    CaptureThrustBaseline(cid, inferred > 0.01f ? inferred : e.maxPower);
                    b = ThrustBaseline[cid];
                }
                e.maxPower = b * mul;
            }

            ConstantSpeedProp[] props = aircraft.GetComponentsInChildren<ConstantSpeedProp>(true);
            for (int i = 0; i < props.Length; i++)
                if (props[i] != null)
                    SetScaledField(EngineReflection.PropNominalPower, props[i], props[i].GetInstanceID(), mul);

            PropFan[] propFans = aircraft.GetComponentsInChildren<PropFan>(true);
            for (int i = 0; i < propFans.Length; i++)
                if (propFans[i] != null)
                    SetScaledField(EngineReflection.PropFanNominalPower, propFans[i], propFans[i].GetInstanceID(), mul);

            RotorShaft[] rotors = aircraft.GetComponentsInChildren<RotorShaft>(true);
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null)
                    continue;
                int rid = rotors[i].GetInstanceID();
                SetScaledField(EngineReflection.RotorNominalPower, rotors[i], rid, mul);
                SetScaledField(EngineReflection.RotorTorqueLimit, rotors[i], rid ^ 0x400000, mul);
            }

            DuctedFan[] ducted = aircraft.GetComponentsInChildren<DuctedFan>(true);
            for (int i = 0; i < ducted.Length; i++)
            {
                if (ducted[i] == null)
                    continue;
                int cid = ducted[i].GetInstanceID();
                SetScaledField(EngineReflection.DuctedMaxThrust, ducted[i], cid, mul);
                SetScaledField(EngineReflection.DuctedMaxPower, ducted[i], cid ^ 0x100000, mul);
                SetScaledField(EngineReflection.DuctedNominalPower, ducted[i], cid ^ 0x200000, mul);
            }

            Turbofan[] fans = aircraft.GetComponentsInChildren<Turbofan>(true);
            for (int i = 0; i < fans.Length; i++)
                if (fans[i] != null)
                    SetScaledField(EngineReflection.TurbofanThrust, fans[i], fans[i].GetInstanceID(), mul);

            Turbojet[] jets = aircraft.GetComponentsInChildren<Turbojet>(true);
            for (int i = 0; i < jets.Length; i++)
                if (jets[i] != null)
                    SetScaledField(EngineReflection.TurbojetThrust, jets[i], jets[i].GetInstanceID(), mul);
        }

        private static void ApplyAbsoluteEngineFuelBurn(Aircraft aircraft, float mul, float prevMul)
        {
            if (aircraft == null)
                return;
            mul = Mathf.Max(0.05f, mul);
            prevMul = Mathf.Max(0.05f, prevMul);

            TurbineEngine[] engines = aircraft.GetComponentsInChildren<TurbineEngine>(true);
            for (int i = 0; i < engines.Length; i++)
            {
                if (engines[i] == null || EngineReflection.TurbineMaxFuel == null)
                    continue;
                int cid = engines[i].GetInstanceID();
                try
                {
                    float cur = (float)EngineReflection.TurbineMaxFuel.GetValue(engines[i]);
                    float b;
                    if (!FuelBurnBaseline.TryGetValue(cid, out b) || b <= 0f)
                    {
                        float inferred = cur / Mathf.Max(0.05f, prevMul);
                        CaptureFuelBurnBaseline(cid, inferred > 0.01f ? inferred : cur);
                        b = FuelBurnBaseline[cid];
                    }
                    EngineReflection.TurbineMaxFuel.SetValue(engines[i], b * mul);
                }
                catch { }
            }

            Turbofan[] fans = aircraft.GetComponentsInChildren<Turbofan>(true);
            for (int i = 0; i < fans.Length; i++)
            {
                if (fans[i] == null || EngineReflection.TurbofanFuel == null)
                    continue;
                int cid = fans[i].GetInstanceID();
                try
                {
                    float cur = (float)EngineReflection.TurbofanFuel.GetValue(fans[i]);
                    float b;
                    if (!FuelBurnBaseline.TryGetValue(cid, out b) || b <= 0f)
                    {
                        CaptureFuelBurnBaseline(cid, cur / prevMul);
                        b = FuelBurnBaseline[cid];
                    }
                    EngineReflection.TurbofanFuel.SetValue(fans[i], b * mul);
                }
                catch { }
            }

            Turbojet[] jets = aircraft.GetComponentsInChildren<Turbojet>(true);
            for (int i = 0; i < jets.Length; i++)
            {
                if (jets[i] == null || EngineReflection.TurbojetFuel == null)
                    continue;
                int cid = jets[i].GetInstanceID();
                try
                {
                    float cur = (float)EngineReflection.TurbojetFuel.GetValue(jets[i]);
                    float b;
                    if (!FuelBurnBaseline.TryGetValue(cid, out b) || b <= 0f)
                    {
                        CaptureFuelBurnBaseline(cid, cur / prevMul);
                        b = FuelBurnBaseline[cid];
                    }
                    EngineReflection.TurbojetFuel.SetValue(jets[i], b * mul);
                }
                catch { }
            }
        }

        private static void ScaleFuelCapacity(Aircraft aircraft, float scale)
        {
            if (aircraft == null || Mathf.Approximately(scale, 1f))
                return;

            if (EngineReflection.AircraftFuelCapacity != null)
            {
                try
                {
                    float cap = (float)EngineReflection.AircraftFuelCapacity.GetValue(aircraft);
                    if (cap > 0f)
                        EngineReflection.AircraftFuelCapacity.SetValue(aircraft, cap * scale);
                }
                catch { }
            }

            if (EngineReflection.TankCapacity == null)
                return;
            FuelTank[] tanks = aircraft.GetComponentsInChildren<FuelTank>(true);
            for (int i = 0; i < tanks.Length; i++)
            {
                FuelTank tank = tanks[i];
                if (tank == null)
                    continue;
                try
                {
                    float cap = (float)EngineReflection.TankCapacity.GetValue(tank);
                    float ratio = cap > 0.01f ? Mathf.Clamp01(tank.fuelMass / cap) : 1f;
                    float newCap = cap * scale;
                    EngineReflection.TankCapacity.SetValue(tank, newCap);
                    tank.fuelMass = newCap * ratio;
                }
                catch { }
            }
        }

        private static float ThrustMulFrom(Component c)
        {
            if (c == null)
                return Plugin.PowerMultiplier != null ? Plugin.PowerMultiplier.Value : 1.35f;
            Aircraft ac = c.GetComponentInParent<Aircraft>();
            if (ac == null)
                return Plugin.PowerMultiplier != null ? Plugin.PowerMultiplier.Value : 1.35f;
            return FlightEnvelopeService.GetOrCreateProfile(ac).ThrustMul.Value;
        }

        private static float FuelBurnMulFrom(Component c)
        {
            if (c == null)
                return 1f;
            Aircraft ac = c.GetComponentInParent<Aircraft>();
            if (ac == null)
                return 1f;
            return FlightEnvelopeService.GetOrCreateProfile(ac).FuelBurnMul.Value;
        }

        internal static void TryBuffEngine(TurbineEngine engine)
        {
            if (engine == null || !AircraftIdentity.IsOnXe(engine))
                return;
            int id = engine.GetInstanceID();
            if (!Plugin.Touched.AddEngine(id))
                return;

            CaptureThrustBaseline(id, engine.maxPower);
            float m = ThrustMulFrom(engine);
            engine.maxPower = ThrustBaseline[id] * m;
            if (EngineReflection.TurbineMaxFuel != null)
            {
                try
                {
                    float fuel = (float)EngineReflection.TurbineMaxFuel.GetValue(engine);
                    CaptureFuelBurnBaseline(id, fuel);
                    EngineReflection.TurbineMaxFuel.SetValue(engine, FuelBurnBaseline[id] * FuelBurnMulFrom(engine));
                }
                catch { }
            }
        }

        internal static void TryBuffProp(ConstantSpeedProp prop)
        {
            if (prop == null || !AircraftIdentity.IsOnXe(prop) || EngineReflection.PropNominalPower == null)
                return;
            int id = prop.GetInstanceID();
            if (!Plugin.Touched.AddProp(id))
                return;
            try
            {
                float p = (float)EngineReflection.PropNominalPower.GetValue(prop);
                CaptureThrustBaseline(id, p);
                EngineReflection.PropNominalPower.SetValue(prop, ThrustBaseline[id] * ThrustMulFrom(prop));
            }
            catch { }
        }

        internal static void TryBuffPropFan(PropFan fan)
        {
            if (fan == null || !AircraftIdentity.IsOnXe(fan) || EngineReflection.PropFanNominalPower == null)
                return;
            int id = fan.GetInstanceID();
            if (!Plugin.Touched.AddPropFan(id))
                return;
            try
            {
                float p = (float)EngineReflection.PropFanNominalPower.GetValue(fan);
                CaptureThrustBaseline(id, p);
                EngineReflection.PropFanNominalPower.SetValue(fan, ThrustBaseline[id] * ThrustMulFrom(fan));
            }
            catch { }
        }

        internal static void TryBuffRotor(RotorShaft rotor)
        {
            if (rotor == null || !AircraftIdentity.IsOnXe(rotor) || EngineReflection.RotorNominalPower == null)
                return;
            int id = rotor.GetInstanceID();
            if (!Plugin.Touched.AddRotor(id))
                return;
            try
            {
                float p = (float)EngineReflection.RotorNominalPower.GetValue(rotor);
                CaptureThrustBaseline(id, p);
                float mul = ThrustMulFrom(rotor);
                EngineReflection.RotorNominalPower.SetValue(rotor, ThrustBaseline[id] * mul);
                if (EngineReflection.RotorTorqueLimit != null)
                {
                    int tid = id ^ 0x400000;
                    float tq = (float)EngineReflection.RotorTorqueLimit.GetValue(rotor);
                    CaptureThrustBaseline(tid, tq);
                    EngineReflection.RotorTorqueLimit.SetValue(rotor, ThrustBaseline[tid] * mul);
                }
            }
            catch { }
        }

        internal static void TryBuffDucted(DuctedFan fan)
        {
            if (fan == null || !AircraftIdentity.IsOnXe(fan))
                return;
            int id = fan.GetInstanceID();
            if (!Plugin.Touched.AddDucted(id))
                return;
            float m = ThrustMulFrom(fan);
            SetScaledField(EngineReflection.DuctedMaxThrust, fan, id, m);
            SetScaledField(EngineReflection.DuctedMaxPower, fan, id ^ 0x100000, m);
            SetScaledField(EngineReflection.DuctedNominalPower, fan, id ^ 0x200000, m);
        }

        internal static void TryBuffTurbofan(Turbofan fan)
        {
            if (fan == null || !AircraftIdentity.IsOnXe(fan) || EngineReflection.TurbofanThrust == null)
                return;
            int id = fan.GetInstanceID();
            if (!Plugin.Touched.AddTurbofan(id))
                return;
            try
            {
                float t = (float)EngineReflection.TurbofanThrust.GetValue(fan);
                CaptureThrustBaseline(id, t);
                EngineReflection.TurbofanThrust.SetValue(fan, ThrustBaseline[id] * ThrustMulFrom(fan));
                if (EngineReflection.TurbofanFuel != null)
                {
                    float fuel = (float)EngineReflection.TurbofanFuel.GetValue(fan);
                    CaptureFuelBurnBaseline(id, fuel);
                    EngineReflection.TurbofanFuel.SetValue(fan, FuelBurnBaseline[id] * FuelBurnMulFrom(fan));
                }
            }
            catch { }
        }

        internal static void TryBuffTurbojet(Turbojet jet)
        {
            if (jet == null || !AircraftIdentity.IsOnXe(jet) || EngineReflection.TurbojetThrust == null)
                return;
            int id = jet.GetInstanceID();
            if (!Plugin.Touched.AddTurbojet(id))
                return;
            try
            {
                float t = (float)EngineReflection.TurbojetThrust.GetValue(jet);
                CaptureThrustBaseline(id, t);
                EngineReflection.TurbojetThrust.SetValue(jet, ThrustBaseline[id] * ThrustMulFrom(jet));
                if (EngineReflection.TurbojetFuel != null)
                {
                    float fuel = (float)EngineReflection.TurbojetFuel.GetValue(jet);
                    CaptureFuelBurnBaseline(id, fuel);
                    EngineReflection.TurbojetFuel.SetValue(jet, FuelBurnBaseline[id] * FuelBurnMulFrom(jet));
                }
            }
            catch { }
        }

        internal static void TryBuffGear(LandingGear gear)
        {
            if (gear == null || !AircraftIdentity.IsOnCoin(gear))
                return;
            if (!Plugin.Touched.AddGear(gear.GetInstanceID()))
                return;

            float m = Plugin.GearStrengthMultiplier.Value;
            EngineReflection.MulField(EngineReflection.GearSpring, gear, m);
            EngineReflection.MulField(EngineReflection.GearDamping, gear, m);
            EngineReflection.MulField(EngineReflection.GearAlign, gear, m);
        }

        internal static void TryBuffFuel(FuelTank tank, float mul)
        {
            if (tank == null || !AircraftIdentity.IsOnXe(tank) || EngineReflection.TankCapacity == null)
                return;
            if (!Plugin.Touched.AddTank(tank.GetInstanceID()))
                return;
            if (Mathf.Abs(mul - 1f) < 0.0001f)
                return;

            try
            {
                float cap = (float)EngineReflection.TankCapacity.GetValue(tank);
                float ratio = cap > 0.01f ? Mathf.Clamp01(tank.fuelMass / cap) : 1f;
                float newCap = cap * mul;
                EngineReflection.TankCapacity.SetValue(tank, newCap);
                tank.fuelMass = newCap * ratio;
            }
            catch { }
        }

        private static void BuffAllEnginesOn(Aircraft aircraft)
        {
            TurbineEngine[] engines = aircraft.GetComponentsInChildren<TurbineEngine>(true);
            for (int i = 0; i < engines.Length; i++)
                TryBuffEngine(engines[i]);

            ConstantSpeedProp[] props = aircraft.GetComponentsInChildren<ConstantSpeedProp>(true);
            for (int i = 0; i < props.Length; i++)
                TryBuffProp(props[i]);

            PropFan[] propFans = aircraft.GetComponentsInChildren<PropFan>(true);
            for (int i = 0; i < propFans.Length; i++)
                TryBuffPropFan(propFans[i]);

            RotorShaft[] rotors = aircraft.GetComponentsInChildren<RotorShaft>(true);
            for (int i = 0; i < rotors.Length; i++)
                TryBuffRotor(rotors[i]);

            DuctedFan[] ducted = aircraft.GetComponentsInChildren<DuctedFan>(true);
            for (int i = 0; i < ducted.Length; i++)
                TryBuffDucted(ducted[i]);

            Turbofan[] fans = aircraft.GetComponentsInChildren<Turbofan>(true);
            for (int i = 0; i < fans.Length; i++)
                TryBuffTurbofan(fans[i]);

            Turbojet[] jets = aircraft.GetComponentsInChildren<Turbojet>(true);
            for (int i = 0; i < jets.Length; i++)
                TryBuffTurbojet(jets[i]);
        }
    }
}
