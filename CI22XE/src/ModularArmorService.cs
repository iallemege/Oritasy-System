using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Modular hits for ground vehicles / ships, facing armor grades for tanks and aircraft,
    /// per-weapon AP grade, red top-right module damage text.
    /// </summary>
    internal static class ModularArmorService
    {
        internal const int ModHull = 0;
        internal const int ModTurret = 1;
        internal const int ModGun = 2;
        internal const int ModMobility = 3;
        internal const int ModEngine = 4;
        internal const int ModAmmo = 5;
        internal const int ModFuel = 6;
        internal const int ModSensors = 7;
        internal const int ModCommand = 8;
        internal const int ModAirframe = 9;

        private static readonly FieldInfo ArmorTierField =
            AccessTools.Field(typeof(UnitDefinition), "armorTier");
        private static readonly FieldInfo MissilePierceField =
            AccessTools.Field(typeof(Missile), "pierceDamage");
        private static readonly FieldInfo MissileInfoField =
            AccessTools.Field(typeof(Missile), "info");
        private static readonly Dictionary<int, WeaponInfo> LastWeapon =
            new Dictionary<int, WeaponInfo>(64);

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _hudEnabled;

        private static GUIStyle _hudStyle;
        private static string _hudLine;
        private static float _hudUntil;
        private static int _hudHitKind;
        private static Color _hudColor = Color.white;
        private static float _lastHudAt;
        private static int _lastHudMod = -1;
        private static int _lastHudVictim;

        private static UnitPart _stashPart;
        private static int _stashMod;
        private static int _stashHit;
        private static int _stashFace;
        private static bool _stashLocal;
        private static bool _stashLocalVictim;
        private static bool _stashVehicleOrShip;
        private static bool _stashOverpen;
        private static Unit _stashVictim;
        private static PersistentID _stashDealer;
        private static float _stashOverpenKe;
        private static float _stashOverpenPierce;
        private static float _stashKeShown;
        private static float _stashHpBefore;
        private static int _chainDepth;

        private static int _infantryPen = -1;
        private static Vector3 _infantryDir = Vector3.forward;
        private static float _infantryUntil;
        private static bool _infantryPureKe;

        internal static void Bind(ConfigFile config)
        {
            _enabled = config.Bind("Combat", "ModularArmor", true,
                "Facing armor grades (0–20, Rutherford 21) + per-weapon AP grade. Machine guns and autocannons are graded separately.");
            _hudEnabled = config.Bind("Combat", "ModularArmorHud", true,
                "Top-right module hit text. White = light damage, red = heavy / destroy.");
        }

        internal static bool Enabled
        {
            get { return _enabled == null || _enabled.Value; }
        }

        internal static bool HudEnabled
        {
            get { return _hudEnabled == null || _hudEnabled.Value; }
        }

        internal static int FacingArmor(Unit u, int face)
        {
            if (u == null)
                return 0;
#if ORITASY_COMBINED
            try
            {
                if (WeXon.GFactorRutherfordField.IsShielded(u))
                    return ModularArmorMathService.GradeRutherford;
            }
            catch { }
#endif
            int kind = ClassifyUnit(u);
            int tier = ReadVanillaTier(u);
            return ModularArmorMathService.ArmorGrade(kind, face, tier);
        }

        internal static void RememberInfantryShot(int penGrade, Vector3 incomingWorld)
        {
            RememberInfantryShot(penGrade, incomingWorld, false);
        }

        internal static void RememberInfantryShot(int penGrade, Vector3 incomingWorld, bool pureKe)
        {
            _infantryPen = ModularArmorMathService.ClampGrade(penGrade);
            _infantryDir = incomingWorld;
            _infantryUntil = Time.unscaledTime + 0.12f;
            _infantryPureKe = pureKe;
        }

        internal static int PenGradeIncoming(PersistentID dealerID, float pierceDamage)
        {
            Unit dealer = ResolveDealer(dealerID);
            WeaponInfo w = ResolveWeapon(dealer);
            float listed = pierceDamage;
            if (w != null)
            {
                try
                {
                    if (w.pierceDamage > listed)
                        listed = w.pierceDamage;
                }
                catch { }
            }
            Missile mis = dealer as Missile;
            if (mis != null)
            {
                float mp = ReadMissilePierce(mis);
                if (mp > listed)
                    listed = mp;
            }
            return ModularArmorMathService.PenGradeForWeapon(w, listed);
        }

        internal static void RememberShot(Unit owner, WeaponStation station)
        {
            if (owner == null || station == null)
                return;
            WeaponInfo w = null;
            try { w = station.WeaponInfo; }
            catch { }
            if (w == null)
                return;
            int id = owner.GetInstanceID();
            LastWeapon[id] = w;
        }

        internal static void FilterTakeDamage(
            UnitPart part,
            ref float pierceDamage,
            ref float blastDamage,
            ref float fireDamage,
            ref float impactDamage,
            PersistentID dealerID)
        {
            _stashPart = null;
            _stashLocal = false;
            _stashLocalVictim = false;
            _stashVehicleOrShip = false;
            _stashOverpen = false;
            _stashVictim = null;
            _stashKeShown = 0f;
            _stashHpBefore = 0f;
            _stashFace = ModularArmorMathService.FaceFront;
            if (!Enabled || part == null)
                return;

            if (ComponentRepair.TryZeroRepairImpact(
                part, dealerID, ref pierceDamage, ref blastDamage, ref fireDamage, ref impactDamage))
                return;

            Unit victim = null;
            try { victim = part.parentUnit; }
            catch { }
            if (victim == null)
                return;
#if ORITASY_COMBINED
            try
            {
                if (WeXon.GFactorRutherfordField.IsShielded(victim)
                    || WeXon.GFactorRutherfordField.IsInsideField(victim))
                {
                    pierceDamage = 0f;
                    blastDamage = 0f;
                    impactDamage = 0f;
                    _stashPart = part;
                    _stashMod = ClassifyModule(part, victim is GroundVehicle, victim is Ship, victim is Aircraft);
                    _stashHit = ModularArmorMathService.HitBounce;
                    _stashFace = ModularArmorMathService.FaceFront;
                    _stashLocal = false;
                    _stashLocalVictim = (victim is Aircraft) && IsLocalVictim(victim);
                    _stashVehicleOrShip = (victim is GroundVehicle) || (victim is Ship);
                    return;
                }
            }
            catch { }
#endif

            bool vehicle = victim is GroundVehicle;
            bool ship = victim is Ship;
            bool aircraft = victim is Aircraft;
            bool building = victim is Building;
            if (!vehicle && !ship && !aircraft && !building)
                return;

            Unit dealer = ResolveDealer(dealerID);
            int kind = building
                ? ModularArmorMathService.KindBuilding
                : ClassifyUnit(victim);
            Vector3 incoming = IncomingDir(dealer, part, victim);
            bool infShot = _infantryPen >= 0 && Time.unscaledTime <= _infantryUntil;
            if (infShot && _infantryDir.sqrMagnitude > 0.0001f)
                incoming = _infantryDir;
            int face = ModularArmorMathService.FacingFromIncoming(
                victim.transform, incoming);
            int tier = building ? 0 : ReadVanillaTier(victim);
            int armor = ModularArmorMathService.ArmorGrade(kind, face, tier);
            int mod = building
                ? ModHull
                : ClassifyModule(part, vehicle, ship, aircraft);
            if (!building)
                armor = ModularArmorMathService.ClampGrade(armor + ModularArmorMathService.ModuleArmorAdj(mod));

            bool collision = false;
            try { collision = !dealerID.IsValid && impactDamage > 0.01f && pierceDamage < 0.08f; }
            catch { collision = dealer == null && impactDamage > 0.01f && pierceDamage < 0.08f; }
            if (collision)
            {
                float cm = ModularArmorMathService.CollisionArmorMul(armor);
                impactDamage *= cm;
                int hitC = ModularArmorMathService.HitPen;
                if (cm <= 0.18f)
                    hitC = ModularArmorMathService.HitBounce;
                else if (cm <= 0.50f)
                    hitC = ModularArmorMathService.HitGlance;
                _stashPart = part;
                _stashMod = mod;
                _stashHit = hitC;
                _stashFace = face;
                _stashLocal = false;
                _stashLocalVictim = aircraft && IsLocalVictim(victim);
                _stashVehicleOrShip = vehicle || ship;
                return;
            }

            float listedPierce = pierceDamage;
            WeaponInfo w = ResolveWeapon(dealer);
            if (w != null)
            {
                try
                {
                    if (w.pierceDamage > listedPierce)
                        listedPierce = w.pierceDamage;
                }
                catch { }
            }
            Missile mis = dealer as Missile;
            if (mis != null)
            {
                float mp = ReadMissilePierce(mis);
                if (mp > listedPierce)
                    listedPierce = mp;
            }

            int pen;
            int wep;
            if (infShot)
            {
                pen = _infantryPen;
                wep = _infantryPureKe
                    ? ModularArmorMathService.WepCannon
                    : ModularArmorMathService.WepRocket;
            }
            else
            {
                pen = ModularArmorMathService.PenGradeForWeapon(w, listedPierce);
                wep = ModularArmorMathService.ClassifyKinetic(w);
                if (wep == ModularArmorMathService.WepUnknown && w != null)
                {
                    try
                    {
                        if (w.gun)
                            wep = ModularArmorMathService.WepCannon;
                        else if (w.missile)
                            wep = ModularArmorMathService.WepMissile;
                    }
                    catch { }
                }
            }
            if (mis != null && wep == ModularArmorMathService.WepUnknown)
                wep = ModularArmorMathService.WepMissile;
            bool gunShellKe = false;
#if ORITASY_COMBINED
            if (mis != null)
            {
                try
                {
                    if (WeXon.Plugin.IsGunShellMissile(mis))
                    {
                        wep = ModularArmorMathService.WepCannon;
                        gunShellKe = true;
                    }
                }
                catch { }
            }
#endif
            if (ModularArmorMathService.IsKeNamed(w))
            {
                wep = ModularArmorMathService.WepCannon;
                gunShellKe = true;
            }

            bool pureKe = gunShellKe || (infShot ? _infantryPureKe
                : ModularArmorMathService.IsPureKeWeapon(wep, w));
            if (_chainDepth == 0 && !infShot)
            {
                int calMm = 0;
                float massKg = 0f;
                float speedMps = 0f;
                if (w != null)
                {
                    try { calMm = ModularArmorMathService.ParseCaliberMm(ModularArmorMathService.WeaponBlob(w)); }
                    catch { }
                    try
                    {
                        if (w.massPerRound > 0.05f)
                            massKg = w.massPerRound;
                    }
                    catch { }
                    try
                    {
                        if (w.muzzleVelocity > 1f)
                            speedMps = w.muzzleVelocity;
                    }
                    catch { }
                }
                if (mis != null)
                {
                    try
                    {
                        if (mis.rb != null)
                        {
                            if (mis.rb.mass > 0.2f && massKg < 0.2f)
                                massKg = mis.rb.mass;
                            float spd = mis.rb.velocity.magnitude;
                            if (spd > speedMps)
                                speedMps = spd;
                        }
                    }
                    catch { }
                }
                impactDamage = ModularArmorMathService.SynthesizeKe(
                    wep, listedPierce, impactDamage, calMm, massKg, speedMps);
                if (ModularArmorMathService.IsKeNamed(w))
                    blastDamage = 0f;
                else if (pureKe)
                {
                    blastDamage *= 0.06f;
                    pierceDamage *= 0.38f;
                }
            }

            if (_chainDepth == 0)
                pen = ModularArmorMathService.ApplyPenFloat(pen);
            int armorEff = armor;
            if (_chainDepth == 0)
                armorEff = ArmorFatigueService.EffectiveArmor(armor, ArmorFatigueService.Get(victim));
            int hit = ModularArmorMathService.ResolveHit(pen, armorEff, wep);
            if (_chainDepth == 0)
                ArmorFatigueService.NoteHit(victim, hit, pen, armor);

            bool bomb = false;
            if (w != null)
            {
                try { bomb = w.bomb || w.glideBomb; }
                catch { }
            }
            if (ModularArmorMathService.ClassifyKinetic(w) == ModularArmorMathService.WepBomb)
                bomb = true;

            float pm = ModularArmorMathService.PierceMul(hit);
            float bm = ModularArmorMathService.BlastMul(hit, bomb, pureKe);
            float im = ModularArmorMathService.ImpactMul(hit);
            pierceDamage *= pm;
            blastDamage *= bm;
            impactDamage *= im;
            if (hit == ModularArmorMathService.HitBounce
                || hit == ModularArmorMathService.HitGlance)
                fireDamage = 0f;

            _stashPart = part;
            _stashMod = mod;
            _stashHit = hit;
            _stashFace = face;
            _stashLocal = infShot || IsLocalDealer(dealer);
            _stashLocalVictim = IsLocalVictim(victim);
            _stashVehicleOrShip = vehicle || ship;
            _stashVictim = victim;
            _stashDealer = dealerID;
            _stashKeShown = impactDamage;
            try { _stashHpBefore = part.hitPoints; }
            catch { _stashHpBefore = 100f; }
            _stashOverpen = !building && hit == ModularArmorMathService.HitOvermatch && _chainDepth == 0;
            if (_stashOverpen)
            {
                _stashOverpenKe = ModularArmorMathService.OverpenLeftoverKe(impactDamage);
                _stashOverpenPierce = ModularArmorMathService.OverpenLeftoverPierce(pierceDamage);
            }
        }

        internal static void FilterMapBuilding(
            MapBuilding mb,
            ref float pierceDamage,
            ref float blastDamage,
            ref float fireDamage,
            ref float impactDamage,
            PersistentID dealerID)
        {
            if (!Enabled || mb == null)
                return;
            Unit dealer = ResolveDealer(dealerID);
            Vector3 incoming = Vector3.forward;
            bool infShot = _infantryPen >= 0 && Time.unscaledTime <= _infantryUntil;
            if (infShot && _infantryDir.sqrMagnitude > 0.0001f)
                incoming = _infantryDir;
            else if (dealer != null && dealer.transform != null && mb.transform != null)
                incoming = mb.transform.position - dealer.transform.position;
            int face = ModularArmorMathService.FacingFromIncoming(mb.transform, incoming);
            int armor = ModularArmorMathService.ArmorGrade(
                ModularArmorMathService.KindBuilding, face, 0);
            float listedPierce = pierceDamage;
            WeaponInfo w = ResolveWeapon(dealer);
            if (w != null)
            {
                try
                {
                    if (w.pierceDamage > listedPierce)
                        listedPierce = w.pierceDamage;
                }
                catch { }
            }
            int pen;
            int wep;
            if (infShot)
            {
                pen = _infantryPen;
                wep = _infantryPureKe
                    ? ModularArmorMathService.WepCannon
                    : ModularArmorMathService.WepRocket;
            }
            else
            {
                pen = ModularArmorMathService.PenGradeForWeapon(w, listedPierce);
                wep = ModularArmorMathService.ClassifyKinetic(w);
            }
            bool bomb = false;
            if (w != null)
            {
                try { bomb = w.bomb || w.glideBomb; }
                catch { }
            }
            if (ModularArmorMathService.ClassifyKinetic(w) == ModularArmorMathService.WepBomb)
                bomb = true;
            if (ModularArmorMathService.IsNuclearWeapon(w))
                bomb = true;
            pen = ModularArmorMathService.ApplyPenFloat(pen);
            int id = mb.GetInstanceID();
            int armorEff = ArmorFatigueService.EffectiveArmor(armor, ArmorFatigueService.GetId(id));
            int hit = ModularArmorMathService.ResolveHit(pen, armorEff, wep);
            ArmorFatigueService.NoteHitId(id, hit, pen, armor);
            pierceDamage *= ModularArmorMathService.PierceMul(hit);
            blastDamage *= ModularArmorMathService.BlastMul(hit, bomb, false);
            impactDamage *= ModularArmorMathService.ImpactMul(hit);
            if (hit == ModularArmorMathService.HitBounce
                || hit == ModularArmorMathService.HitGlance)
                fireDamage = 0f;
        }

        internal static void NotifyAfterHit(UnitPart part)
        {
            if (_stashPart == null || !object.ReferenceEquals(_stashPart, part))
                return;
            bool show = _stashLocal || _stashLocalVictim;
            bool localVictim = _stashLocalVictim;
            int mod = _stashMod;
            int hit = _stashHit;
            int face = _stashFace;
            bool overpen = _stashOverpen;
            Unit victim = _stashVictim;
            PersistentID dealer = _stashDealer;
            float leftoverKe = _stashOverpenKe;
            float leftoverPierce = _stashOverpenPierce;
            float keShown = _stashKeShown;
            float hpBefore = _stashHpBefore;
            _stashPart = null;
            _stashLocalVictim = false;
            _stashOverpen = false;
            _stashVictim = null;
            if (localVictim)
            {
                try
                {
                    AirframeWearService.NotifyLocationHit(part, mod, hit, face);
                }
                catch { }
            }

            int innerMod = -1;
            if (overpen && _chainDepth == 0 && victim != null
                && leftoverKe + leftoverPierce > 4f)
            {
                UnitPart inner = FindInteriorModule(victim, part, mod);
                if (inner != null)
                {
                    innerMod = ClassifyModule(inner, victim is GroundVehicle, victim is Ship, victim is Aircraft);
                    _chainDepth = 1;
                    try
                    {
                        inner.TakeDamage(leftoverPierce, 0f, 1f, 0f, leftoverKe, dealer);
                    }
                    catch { }
                    _chainDepth = 0;
                }
            }

            if (!show || !HudEnabled)
                return;
            PushHud(part, mod, hit, innerMod, keShown, hpBefore);
        }

        internal static void DrawHud()
        {
            if (!HudEnabled)
                return;
            if (string.IsNullOrEmpty(_hudLine) || Time.unscaledTime > _hudUntil)
            {
                _hudLine = null;
                return;
            }
            if (OritasyPresentation.SplashActive)
                return;
            try
            {
                if (JoinMenuFactionFix.SelectionUiOpen())
                    return;
            }
            catch { }
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label);
                _hudStyle.fontSize = 18;
                _hudStyle.fontStyle = FontStyle.Bold;
                _hudStyle.alignment = TextAnchor.UpperRight;
                _hudStyle.wordWrap = false;
            }
            _hudStyle.normal.textColor = _hudColor;

            float w = 540f;
            float x = UiScaleService.Width - w - 18f;
            float y = 72f;
#if ORITASY_COMBINED
            try { y = WeXon.StrategicArsenal.CornerStackTopY(); }
            catch { }
#endif
            if (y < 48f)
                y = 72f;
            Rect box = new Rect(x, y, w, 32f);
            int prevDepth = GUI.depth;
            GUI.depth = -996;
            Color prev = GUI.color;
            GUI.color = new Color(0.04f, 0.04f, 0.04f, 0.58f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(box, _hudLine, _hudStyle);
            GUI.color = prev;
            GUI.depth = prevDepth;
        }

        private static void PushHud(UnitPart part, int mod, int hit, int innerMod, float ke, float hpBefore)
        {
            int victimId = 0;
            try
            {
                if (part.parentUnit != null)
                    victimId = part.parentUnit.GetInstanceID();
            }
            catch { }
            float now = Time.unscaledTime;
            if (victimId == _lastHudVictim && mod == _lastHudMod && now - _lastHudAt < 0.28f)
                return;
            _lastHudAt = now;
            _lastHudMod = mod;
            _lastHudVictim = victimId;
            _hudHitKind = hit;

            float hpAfter = 0f;
            try { hpAfter = part.hitPoints; }
            catch { }
            bool destroyed = hpAfter <= 0.01f;
            float lost = hpBefore - hpAfter;
            if (lost < 0f)
                lost = 0f;
            float sev = HitSeverity(hit, innerMod >= 0, destroyed, lost, hpBefore, ke);
            _hudColor = SeverityColor(sev);

            string modName = ModuleLabel(mod);
            string verb;
            if (hit == ModularArmorMathService.HitBounce)
                verb = UiLang.T("NO PENETRATION", "未击穿");
            else if (hit == ModularArmorMathService.HitGlance)
                verb = UiLang.T("GLANCING HIT", "跳弹/擦伤");
            else if (hit == ModularArmorMathService.HitOvermatch)
                verb = UiLang.T("OVERPEN", "过穿");
            else if (destroyed)
                verb = UiLang.T("DESTROYED", "损毁");
            else
                verb = UiLang.T("DAMAGED", "损伤");
            string keBit = ke > 1f ? "  KE " + Mathf.RoundToInt(ke) : "";
            if (innerMod >= 0 && hit == ModularArmorMathService.HitOvermatch)
                _hudLine = modName + "  →  " + ModuleLabel(innerMod) + "  " + verb + keBit;
            else
                _hudLine = modName + "  " + verb + keBit;
            _hudUntil = now + 2.4f;
        }

        private static float HitSeverity(int hit, bool chained, bool destroyed, float lost, float hpBefore, float ke)
        {
            float s = 0.04f;
            if (hit == ModularArmorMathService.HitGlance)
                s = 0.22f;
            else if (hit == ModularArmorMathService.HitPen)
                s = 0.52f;
            else if (hit == ModularArmorMathService.HitOvermatch)
                s = 0.78f;
            if (chained && s < 0.86f)
                s = 0.86f;
            if (destroyed)
                s = 1f;
            float denom = hpBefore > 8f ? hpBefore : 100f;
            float fromHp = lost / denom;
            if (fromHp > s)
                s = fromHp;
            float fromKe = ke / 480f;
            if (fromKe > 1f)
                fromKe = 1f;
            s = s * 0.72f + fromKe * 0.28f;
            if (s < 0f) s = 0f;
            if (s > 1f) s = 1f;
            return s;
        }

        /// <summary>0 = white (light), 1 = red (heavy).</summary>
        private static Color SeverityColor(float s)
        {
            return new Color(1f, 1f - 0.92f * s, 1f - 0.92f * s, 0.98f);
        }

        private static int BehindModule(int mod)
        {
            if (mod == ModTurret || mod == ModGun || mod == ModAirframe || mod == ModHull)
                return ModEngine;
            if (mod == ModEngine)
                return ModAmmo;
            if (mod == ModSensors)
                return ModCommand;
            if (mod == ModMobility)
                return ModHull;
            return ModAmmo;
        }

        private static readonly Dictionary<int, UnitPart[]> PartCache = new Dictionary<int, UnitPart[]>(64);
        private static readonly Dictionary<int, float> PartCacheUntil = new Dictionary<int, float>(64);

        private static UnitPart FindInteriorModule(Unit victim, UnitPart hitPart, int hitMod)
        {
            if (victim == null)
                return null;
            Aircraft ac = victim as Aircraft;
            if (ac != null && ac.partLookup != null && ac.partLookup.Count >= 2)
                return PickInterior(ac.partLookup, hitPart, hitMod, victim);
            UnitPart[] parts = CachedParts(victim);
            if (parts == null || parts.Length < 2)
                return null;
            return PickInterior(parts, hitPart, hitMod, victim);
        }

        private static UnitPart[] CachedParts(Unit victim)
        {
            int id = 0;
            try { id = victim.GetInstanceID(); }
            catch { return null; }
            float until;
            UnitPart[] parts;
            if (PartCache.TryGetValue(id, out parts) && PartCacheUntil.TryGetValue(id, out until)
                && Time.unscaledTime < until && parts != null)
                return parts;
            try { parts = victim.GetComponentsInChildren<UnitPart>(true); }
            catch { parts = null; }
            if (parts == null)
                return null;
            PartCache[id] = parts;
            PartCacheUntil[id] = Time.unscaledTime + 2.5f;
            return parts;
        }

        private static UnitPart PickInterior(IList<UnitPart> parts, UnitPart hitPart, int hitMod, Unit victim)
        {
            if (parts == null)
                return null;
            int want = BehindModule(hitMod);
            bool vehicle = victim is GroundVehicle;
            bool ship = victim is Ship;
            bool aircraft = victim is Aircraft;
            UnitPart fallback = null;
            int n = parts.Count;
            for (int i = 0; i < n; i++)
            {
                UnitPart p = parts[i];
                if (p == null || object.ReferenceEquals(p, hitPart))
                    continue;
                try
                {
                    if (p.hitPoints <= 0.01f)
                        continue;
                }
                catch { }
                int m = ClassifyModule(p, vehicle, ship, aircraft);
                if (m == want)
                    return p;
                if (fallback == null && m != hitMod)
                    fallback = p;
            }
            if (fallback != null)
                return fallback;
            for (int i = 0; i < n; i++)
            {
                if (parts[i] != null && !object.ReferenceEquals(parts[i], hitPart))
                    return parts[i];
            }
            return null;
        }

        internal static string ModuleLabel(int mod)
        {
            if (mod == ModTurret) return UiLang.T("TURRET", "炮塔");
            if (mod == ModGun) return UiLang.T("GUN", "火炮");
            if (mod == ModMobility) return UiLang.T("MOBILITY", "行动装置");
            if (mod == ModEngine) return UiLang.T("ENGINE", "动力");
            if (mod == ModAmmo) return UiLang.T("AMMO", "弹药舱");
            if (mod == ModFuel) return UiLang.T("FUEL", "燃料");
            if (mod == ModSensors) return UiLang.T("SENSORS", "雷达/观瞄");
            if (mod == ModCommand) return UiLang.T("BRIDGE", "指挥室");
            if (mod == ModAirframe) return UiLang.T("AIRFRAME", "机体");
            return UiLang.T("HULL", "车体/舰体");
        }

        internal static int ClassifyModule(UnitPart part, bool vehicle, bool ship, bool aircraft)
        {
            if (part == null)
                return vehicle || ship ? ModHull : ModAirframe;
            if (aircraft)
                return ClassifyAircraftModule(part);
            string blob = PartBlob(part);
            if (ContainsAny(blob, "ammo", "magazine", "rack", "silo", "warhead", "missilebay", "vls"))
                return ModAmmo;
            if (ContainsAny(blob, "fuel", "bunker", "cell"))
                return ModFuel;
            if (ContainsAny(blob, "radar", "sensor", "optic", "mast", "antenna", "director", "search", "ew", "irst"))
                return ModSensors;
            if (ContainsAny(blob, "bridge", "cic", "island", "cockpit", "crew", "cabin", "conning"))
                return ModCommand;
            if (ContainsAny(blob, "engine", "motor", "turbine", "reactor", "boiler", "propulsion",
                "propeller", "screw", "shaft", "gearbox", "powerplant"))
                return ModEngine;
            if (ContainsAny(blob, "track", "wheel", "suspension", "sprocket", "roadwheel", "drive",
                "gear", "landing", "strut", "oleo"))
                return ModMobility;
            if (ContainsAny(blob, "gun", "cannon", "barrel", "howitzer", "artillery", "ciws", "phalanx"))
                return ModGun;
            if (ContainsAny(blob, "turret", "cupola", "barbette", "mount"))
                return ModTurret;
            if (ContainsAny(blob, "wing", "flap", "slat", "aileron", "elevator", "rudder",
                "stabil", "tail", "fuselage", "canopy", "nose"))
                return ModHull;
            return ModHull;
        }

        private static int ClassifyAircraftModule(UnitPart part)
        {
            string blob = PartBlob(part);
            if (ContainsAny(blob, "cockpit", "canopy", "pilot", "cabin", "crew"))
                return ModCommand;
            if (ContainsAny(blob, "fuel", "bunker", "cell"))
                return ModFuel;
            if (ContainsAny(blob, "engine", "motor", "turbine", "nacelle", "powerplant",
                "turboshaft", "gearbox", "rotor", "propeller", "propulsion", "shaft"))
                return ModEngine;
            if (ContainsAny(blob, "ammo", "magazine", "rack", "silo", "warhead", "missilebay"))
                return ModAmmo;
            if (ContainsAny(blob, "radar", "sensor", "optic", "antenna", "irst", "ew", "director"))
                return ModSensors;
            if (ContainsAny(blob, "landing", "strut", "oleo", "gear", "wheel"))
                return ModMobility;
            if (ContainsAny(blob, "tail", "boom", "empennage", "tilt", "swash", "mast"))
                return ModMobility;
            if (ContainsAny(blob, "gun", "cannon", "barrel", "howitzer", "ciws"))
                return ModGun;
            if (ContainsAny(blob, "wing", "flap", "slat", "aileron", "elevator", "rudder",
                "stabil", "fuselage", "nose"))
                return ModAirframe;
            return InferAircraftModuleByPose(part);
        }

        private static int InferAircraftModuleByPose(UnitPart part)
        {
            Unit u = null;
            try { u = part.parentUnit; }
            catch { }
            if (u == null || u.transform == null || part.transform == null)
                return ModAirframe;
            Vector3 lp;
            try { lp = u.transform.InverseTransformPoint(part.transform.position); }
            catch { return ModAirframe; }
            float ax = Mathf.Abs(lp.x);
            float ay = Mathf.Abs(lp.y);
            float az = Mathf.Abs(lp.z);
            bool heavy = false;
            try
            {
                if (u is Aircraft)
                {
                    string key = AircraftIdentity.GetKey((Aircraft)u);
                    heavy = AircraftIdentity.IsSah46(key) || AircraftIdentity.IsVl49(key)
                        || AircraftIdentity.IsUh90(key);
                }
            }
            catch { }
            if (heavy)
            {
                if (lp.y > 1.1f && ay >= ax * 0.55f)
                    return ModEngine;
                if (lp.z < -2.2f)
                    return ModMobility;
                if (lp.z > 2.4f)
                    return ModCommand;
                if (ax > 2.2f)
                    return ModEngine;
                if (ax < 1.8f && ay < 1.4f && az < 3.2f)
                    return ModEngine;
            }
            else
            {
                if (lp.z > 2.6f)
                    return ModCommand;
                if (lp.z < -2.2f)
                    return ModEngine;
                if (ax > 3.2f)
                    return ModAirframe;
            }
            return ModAirframe;
        }

        internal static int ClassifyUnit(Unit u)
        {
            if (u == null)
                return ModularArmorMathService.KindSoft;
            if (u is Building)
                return ModularArmorMathService.KindBuilding;
            if (u is Aircraft)
                return ClassifyAircraft((Aircraft)u);
            if (u is Ship)
                return ClassifyShip((Ship)u);
            if (u is GroundVehicle)
                return ClassifyVehicle((GroundVehicle)u);
            return ModularArmorMathService.KindSoft;
        }

        private static int ClassifyAircraft(Aircraft ac)
        {
            try
            {
                string key = AircraftIdentity.GetKey(ac);
                if (AircraftIdentity.IsSah46(key) || AircraftIdentity.IsVl49(key))
                    return ModularArmorMathService.KindSuperHeavy;
            }
            catch { }
            string blob = UnitBlob(ac);
            if (ContainsAny(blob, "sah-64", "sah64", "sah-46", "sah46", "chicane",
                "vl-49", "vl49", "tarantula", "bird-eating", "quadvtol"))
                return ModularArmorMathService.KindSuperHeavy;
            if (ContainsAny(blob, "heli", "helo", "rotor", "vtol", "osprey", "tilt", "ah-", "uh-", "mi-"))
                return ModularArmorMathService.KindHelo;
            if (ContainsAny(blob, "bomber", "b-52", "b-2", "b-1", "tu-95", "tu-160", "sfb"))
                return ModularArmorMathService.KindBomber;
            if (ContainsAny(blob, "attack", "cas", "warthog", "frogfoot", "a-10", "su-25", "a-19", "ci-22"))
                return ModularArmorMathService.KindAttack;
            return ModularArmorMathService.KindFighter;
        }

        private static int ClassifyShip(Ship ship)
        {
            string blob = UnitBlob(ship);
            if (ContainsAny(blob, "carrier", "lhd", "lha", "cvn", "supercarrier"))
                return ModularArmorMathService.KindCarrier;
            if (ContainsAny(blob, "patrol", "boat", "cutter", "missile boat"))
                return ModularArmorMathService.KindBoat;
            if (ContainsAny(blob, "frigate", "corvette", "ffg", "ddg", "destroyer"))
                return ModularArmorMathService.KindEscort;
            if (ContainsAny(blob, "cruiser", "battleship", "battlecruiser", "cg", "bb"))
                return ModularArmorMathService.KindWarship;
            float mass = SafeMass(ship);
            if (mass >= 80000f)
                return ModularArmorMathService.KindCarrier;
            if (mass >= 8000f)
                return ModularArmorMathService.KindWarship;
            if (mass >= 1500f)
                return ModularArmorMathService.KindEscort;
            return ModularArmorMathService.KindBoat;
        }

        private static int ClassifyVehicle(GroundVehicle gv)
        {
            try
            {
                VehicleDefinition vd = gv.definition as VehicleDefinition;
                if (vd != null)
                {
                    VehicleType t = vd.vehicleType;
                    if (t == VehicleType.MBT)
                        return ModularArmorMathService.KindMbt;
                    if (t == VehicleType.AFV)
                        return ModularArmorMathService.KindIfv;
                    if (t == VehicleType.TRUCK || t == VehicleType.UGV || t == VehicleType.LCV
                        || t == VehicleType.RDR)
                        return ModularArmorMathService.KindSoft;
                    if (t == VehicleType.ART || t == VehicleType.AAA
                        || t == VehicleType.IR_SAM || t == VehicleType.R_SAM)
                        return ModularArmorMathService.KindIfv;
                }
            }
            catch { }
            string blob = UnitBlob(gv);
            if (ContainsAny(blob, "tank", "mbt", "abrams", "leopard", "challenger", "merkava",
                "armata", "t-72", "t72", "t-80", "t80", "t-90", "t90", "t-14", "platoon tank"))
                return ModularArmorMathService.KindMbt;
            if (ContainsAny(blob, "ifv", "apc", "bmp", "bradley", "warrior", "lav", "stryker",
                "marder", "cv90", "puma", "aifv"))
                return ModularArmorMathService.KindIfv;
            float mass = SafeMass(gv);
            if (mass >= 35000f)
                return ModularArmorMathService.KindMbt;
            if (mass >= 12000f)
                return ModularArmorMathService.KindIfv;
            return ModularArmorMathService.KindSoft;
        }

        private static float SafeMass(Unit u)
        {
            try { return u.GetMass(); }
            catch { return 0f; }
        }

        private static int ReadVanillaTier(Unit u)
        {
            if (ArmorTierField == null || u == null || u.definition == null)
                return 0;
            try
            {
                object v = ArmorTierField.GetValue(u.definition);
                if (v is int)
                    return (int)v;
                if (v is float)
                    return Mathf.RoundToInt((float)v);
            }
            catch { }
            return 0;
        }

        private static string UnitBlob(Unit u)
        {
            string n = u != null && u.name != null ? u.name : "";
            try
            {
                if (u.definition != null)
                {
                    if (!string.IsNullOrEmpty(u.definition.unitName))
                        n = n + " " + u.definition.unitName;
                    if (!string.IsNullOrEmpty(u.definition.code))
                        n = n + " " + u.definition.code;
                    if (!string.IsNullOrEmpty(u.definition.jsonKey))
                        n = n + " " + u.definition.jsonKey;
                }
            }
            catch { }
            return n;
        }

        private static string PartBlob(UnitPart part)
        {
            string n = part.name != null ? part.name : "";
            try
            {
                Transform tf = part.transform;
                if (tf != null)
                {
                    n = n + " " + tf.name;
                    if (tf.parent != null)
                        n = n + " " + tf.parent.name;
                }
            }
            catch { }
            try { n = n + " " + part.GetType().Name; }
            catch { }
            return n;
        }

        private static bool ContainsAny(string blob, params string[] tokens)
        {
            if (string.IsNullOrEmpty(blob) || tokens == null)
                return false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (ModularArmorMathService.NameHas(blob, tokens[i]))
                    return true;
            }
            return false;
        }

        private static Vector3 IncomingDir(Unit dealer, UnitPart part, Unit victim)
        {
            if (dealer == null && victim != null)
            {
                try
                {
                    if (victim.rb != null && victim.rb.velocity.sqrMagnitude > 1f)
                        return victim.rb.velocity;
                }
                catch { }
            }
            Vector3 to = Vector3.forward;
            try { to = part.transform.position; }
            catch
            {
                try { to = victim.transform.position; }
                catch { }
            }
            Vector3 from = to - Vector3.forward;
            if (dealer != null && dealer.transform != null)
                from = dealer.transform.position;
            Missile mis = dealer as Missile;
            if (mis != null)
            {
                try
                {
                    if (mis.rb != null && mis.rb.velocity.sqrMagnitude > 4f)
                        return mis.rb.velocity;
                    return mis.transform.forward;
                }
                catch { }
            }
            Vector3 d = to - from;
            if (d.sqrMagnitude < 0.01f && victim != null && victim.transform != null)
                d = victim.transform.forward;
            return d;
        }

        internal static WeaponInfo WeaponFromDealer(PersistentID dealerID)
        {
            return ResolveWeapon(ResolveDealer(dealerID));
        }

        private static Unit ResolveDealer(PersistentID dealerID)
        {
            if (!dealerID.IsValid)
                return null;
            Unit dealer = null;
            try
            {
                if (!dealerID.TryGetUnit(out dealer))
                    return null;
            }
            catch { return null; }
            return dealer;
        }

        private static WeaponInfo ResolveWeapon(Unit dealer)
        {
            if (dealer == null)
                return null;
            Missile mis = dealer as Missile;
            if (mis != null)
            {
                WeaponInfo info = ReadMissileInfo(mis);
                if (info != null)
                    return info;
            }
            int id = dealer.GetInstanceID();
            WeaponInfo last = null;
            if (LastWeapon.TryGetValue(id, out last) && last != null)
                return last;
            Aircraft ac = dealer as Aircraft;
            if (ac != null && ac.weaponStations != null)
            {
                WeaponInfo best = null;
                float bestPen = -1f;
                try
                {
                    for (int i = 0; i < ac.weaponStations.Count; i++)
                    {
                        WeaponStation st = ac.weaponStations[i];
                        if (st == null || st.WeaponInfo == null)
                            continue;
                        WeaponInfo w = st.WeaponInfo;
                        if (!w.gun && !w.energy)
                            continue;
                        float p = w.armorTierEffectiveness;
                        if (w.pierceDamage > p)
                            p = w.pierceDamage;
                        if (p > bestPen)
                        {
                            bestPen = p;
                            best = w;
                        }
                    }
                }
                catch { }
                if (best != null)
                    return best;
            }
            return null;
        }

        private static float ReadMissilePierce(Missile mis)
        {
            if (mis == null || MissilePierceField == null)
                return 0f;
            try { return (float)MissilePierceField.GetValue(mis); }
            catch { return 0f; }
        }

        private static WeaponInfo ReadMissileInfo(Missile mis)
        {
            if (mis == null || MissileInfoField == null)
                return null;
            try { return MissileInfoField.GetValue(mis) as WeaponInfo; }
            catch { return null; }
        }

        private static bool IsLocalVictim(Unit victim)
        {
            if (victim == null)
                return false;
            try
            {
                if (GameManager.IsLocalAircraft(victim))
                    return true;
            }
            catch { }
            Aircraft local = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return false;
            }
            catch { return false; }
            return object.ReferenceEquals(victim, local);
        }

        private static bool IsLocalDealer(Unit dealer)
        {
            if (dealer == null)
                return false;
            try
            {
                if (GameManager.IsLocalAircraft(dealer))
                    return true;
            }
            catch { }
            Missile mis = dealer as Missile;
            if (mis == null)
                return false;
            Aircraft local = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return false;
            }
            catch { return false; }
            try
            {
                if (object.ReferenceEquals(mis.owner, local))
                    return true;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeDamage")]
    internal static class Patch_UnitPart_TakeDamage_ModularArmor
    {
        [HarmonyPrefix]
        private static void Prefix(
            UnitPart __instance,
            ref float pierceDamage,
            ref float blastDamage,
            ref float fireDamage,
            ref float impactDamage,
            PersistentID dealerID)
        {
            ModularArmorService.FilterTakeDamage(
                __instance, ref pierceDamage, ref blastDamage, ref fireDamage, ref impactDamage, dealerID);
        }

        [HarmonyPostfix]
        private static void Postfix(UnitPart __instance)
        {
            ModularArmorService.NotifyAfterHit(__instance);
        }
    }

    [HarmonyPatch(typeof(Weapon), "Fire", new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    internal static class Patch_Weapon_Fire_ModularArmor
    {
        [HarmonyPostfix]
        private static void Postfix(Unit owner, WeaponStation weaponStation)
        {
            ModularArmorService.RememberShot(owner, weaponStation);
        }
    }
}
