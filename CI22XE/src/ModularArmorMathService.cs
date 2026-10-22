using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Discrete AP / armor grades (0–20) plus facing. Grade 21 is Rutherford-only.
    /// Machine guns and autocannons use separate caliber tables.
    /// </summary>
    internal static class ModularArmorMathService
    {
        internal const int GradeMax = 20;
        internal const int GradeRutherford = 21;

        internal const int WepUnknown = 0;
        internal const int WepMg = 1;
        internal const int WepCannon = 2;
        internal const int WepRocket = 3;
        internal const int WepMissile = 4;
        internal const int WepBomb = 5;
        internal const int WepEnergy = 6;

        internal const int FaceFront = 0;
        internal const int FaceSide = 1;
        internal const int FaceRear = 2;
        internal const int FaceTop = 3;
        internal const int FaceBottom = 4;

        internal const int KindFighter = 0;
        internal const int KindAttack = 1;
        internal const int KindBomber = 2;
        internal const int KindHelo = 3;
        internal const int KindMbt = 4;
        internal const int KindIfv = 5;
        internal const int KindSoft = 6;
        internal const int KindWarship = 7;
        internal const int KindEscort = 8;
        internal const int KindCarrier = 9;
        internal const int KindBoat = 10;
        internal const int KindSuperHeavy = 11;
        internal const int KindInfantry = 12;
        internal const int KindBuilding = 13;

        internal const int HitBounce = 0;
        internal const int HitGlance = 1;
        internal const int HitPen = 2;
        internal const int HitOvermatch = 3;

        internal static int ClampGrade(int g)
        {
            if (g < 0) return 0;
            if (g > GradeMax) return GradeMax;
            return g;
        }

        /// <summary>Incoming world travel dir (dealer → part) into facing.</summary>
        internal static int FacingFromIncoming(Transform unitTf, Vector3 incomingWorld)
        {
            if (unitTf == null)
                return FaceSide;
            Vector3 n = incomingWorld;
            if (n.sqrMagnitude < 0.0001f)
                return FaceFront;
            n.Normalize();
            Vector3 local = unitTf.InverseTransformDirection(n);
            float ax = Mathf.Abs(local.x);
            float ay = Mathf.Abs(local.y);
            float az = Mathf.Abs(local.z);
            if (ay >= ax && ay >= az)
                return local.y < 0f ? FaceTop : FaceBottom;
            if (ax >= az)
                return FaceSide;
            return local.z >= 0f ? FaceFront : FaceRear;
        }

        internal static int ArmorGrade(int kind, int face, int vanillaTier)
        {
            if (kind == KindBuilding)
                return 6;
            if (kind == KindSuperHeavy)
                return GradeMax;
            int g = BaseGrade(kind, face);
            if (vanillaTier > 0)
            {
                int t = ClampGrade(vanillaTier * 4);
                int front = t;
                int side = t - 4;
                int rear = t - 8;
                int top = t - 4;
                if (kind == KindMbt || kind == KindWarship || kind == KindCarrier)
                {
                    front = t + 4;
                    side = t;
                    rear = t - 4;
                    top = t - 4;
                }
                else if (kind == KindIfv || kind == KindEscort)
                {
                    front = t;
                    side = t - 4;
                    rear = t - 8;
                    top = t - 8;
                }
                int byFace = side;
                if (face == FaceFront) byFace = front;
                else if (face == FaceRear) byFace = rear;
                else if (face == FaceTop) byFace = top;
                else if (face == FaceBottom) byFace = t;
                if (byFace > g)
                    g = byFace;
            }
            return ClampGrade(g);
        }

        internal static int BaseGrade(int kind, int face)
        {
            if (kind == KindMbt)
                return Pick(face, 16, 12, 8, 8, 12);
            if (kind == KindIfv)
                return Pick(face, 12, 8, 5, 5, 8);
            if (kind == KindSoft)
                return Pick(face, 3, 3, 3, 3, 3);
            if (kind == KindWarship)
                return Pick(face, 12, 16, 12, 12, 16);
            if (kind == KindCarrier)
                return Pick(face, 12, 12, 12, 12, 16);
            if (kind == KindEscort)
                return Pick(face, 8, 12, 8, 8, 12);
            if (kind == KindBoat)
                return Pick(face, 4, 8, 4, 4, 8);
            if (kind == KindAttack)
                return Pick(face, 8, 7, 4, 4, 8);
            if (kind == KindBomber)
                return Pick(face, 4, 4, 4, 4, 4);
            if (kind == KindHelo)
                return Pick(face, 4, 4, 4, 4, 4);
            if (kind == KindSuperHeavy)
                return Pick(face, 20, 20, 20, 20, 20);
            if (kind == KindInfantry)
                return Pick(face, 10, 10, 10, 10, 10);
            if (kind == KindBuilding)
                return Pick(face, 6, 6, 6, 6, 6);
            return Pick(face, 7, 4, 4, 4, 4);
        }

        private static int Pick(int face, int front, int side, int rear, int top, int bottom)
        {
            if (face == FaceFront) return front;
            if (face == FaceRear) return rear;
            if (face == FaceTop) return top;
            if (face == FaceBottom) return bottom;
            return side;
        }

        internal static int PenGradeForWeapon(WeaponInfo w, float pierceDamage)
        {
            if (IsNuclearWeapon(w))
                return 11;

            int wep = ClassifyKinetic(w);
            int calMm = 0;
            float interval = 0f;
            float effectiveness = 0f;
            bool bomb = false;
            if (w != null)
            {
                calMm = ParseCaliberMm(WeaponBlob(w));
                try { interval = w.fireInterval; }
                catch { }
                try { effectiveness = w.armorTierEffectiveness; }
                catch { }
                try { bomb = w.bomb || w.glideBomb; }
                catch { }
                if (wep == WepUnknown && (w.gun || w.energy))
                    wep = ClassifyGun(WeaponBlob(w), calMm, interval, pierceDamage, w.energy);
            }

            if (wep == WepMg)
                return MgPenGrade(calMm);
            if (wep == WepCannon)
                return CannonPenGrade(calMm, pierceDamage);
            if (wep == WepEnergy)
                return 14;

            int fromPierce = PierceToGrade(pierceDamage);
            if (wep == WepBomb || bomb)
            {
                if (fromPierce < 12)
                    fromPierce = 12;
                return ClampGrade(fromPierce);
            }
            if (wep == WepRocket)
            {
                if (fromPierce < 13)
                    fromPierce = 13;
                return ClampGrade(fromPierce);
            }

            int fromEff = -1;
            if (effectiveness > 0.05f)
                fromEff = ClampGrade(Mathf.RoundToInt(effectiveness * 4f));
            if (fromEff >= 0 && fromEff > fromPierce)
                return fromEff;
            return ClampGrade(fromPierce);
        }

        /// <summary>Per-shot AP scatter: every weapon ±1 grade.</summary>
        internal static int ApplyPenFloat(int grade)
        {
            int d = UnityEngine.Random.Range(-1, 2);
            return ClampGrade(grade + d);
        }

        internal static bool IsNuclearWeapon(WeaponInfo w)
        {
            if (w == null)
                return false;
            try
            {
                if (w.nuclear)
                    return true;
            }
            catch { }
            return IsNukeNamed(WeaponBlob(w) + " " + (w.name != null ? w.name : string.Empty));
        }

        internal static bool IsNukeNamed(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return false;
            if (NameHas(blob, "[10kt]") || NameHas(blob, "[5kt]") || NameHas(blob, "[1.5kt]")
                || NameHas(blob, "[15kt]") || NameHas(blob, " 10kt") || NameHas(blob, " 5kt")
                || NameHas(blob, "1.5kt") || NameHas(blob, "15kt"))
                return true;
            if (NameHas(blob, "acnm") || NameHas(blob, "shardfall"))
                return true;
            return false;
        }

        internal static bool IsAcmSeries(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return false;
            if (NameHas(blob, "acm-119") || NameHas(blob, "acm_119") || NameHas(blob, "acm119")
                || NameHas(blob, "acnm-118") || NameHas(blob, "acnm_118") || NameHas(blob, "acnm")
                || NameHas(blob, "agm-t") || NameHas(blob, "agm_t")
                || NameHas(blob, "agm-119") || NameHas(blob, "agm_119")
                || NameHas(blob, "gs25"))
                return true;
            return false;
        }

        internal static bool IsForeignPack(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return false;
            if (IsAcmSeries(blob))
                return true;
            if (NameHas(blob, "aam-2cv") || NameHas(blob, "aam2cv") || NameHas(blob, "aam_2cv")
                || NameHas(blob, "tgm-85") || NameHas(blob, "tgm85") || NameHas(blob, "kh85mt")
                || NameHas(blob, "shardfall")
                || NameHas(blob, "pulse250") || NameHas(blob, "aam-250") || NameHas(blob, "aam_250"))
                return true;
            return false;
        }

        internal static int ClassifyKinetic(WeaponInfo w)
        {
            if (w == null)
                return WepUnknown;
            try
            {
                if (w.energy)
                    return WepEnergy;
            }
            catch { }
            try
            {
                if (w.bomb || w.glideBomb)
                    return WepBomb;
            }
            catch { }

            string blob = WeaponBlob(w);
            if (NameHas(blob, "rocket") || NameHas(blob, "ffar") || NameHas(blob, "s-8")
                || NameHas(blob, "s-13") || NameHas(blob, "zuni") || NameHas(blob, "hydra"))
                return WepRocket;

            bool gun = false;
            bool missile = false;
            try { gun = w.gun; }
            catch { }
            try { missile = w.missile || w.laserGuided; }
            catch { }
            if (gun)
            {
                int calMm = ParseCaliberMm(blob);
                float interval = 0f;
                try { interval = w.fireInterval; }
                catch { }
                return ClassifyGun(blob, calMm, interval, w.pierceDamage, false);
            }
            if (missile)
                return WepMissile;
            return WepUnknown;
        }

        internal static int ClassifyGun(string blob, int calMm, float fireInterval, float pierceDamage, bool energy)
        {
            if (energy)
                return WepEnergy;
            if (calMm > 0 && calMm < 16)
                return WepMg;
            if (calMm >= 16)
                return WepCannon;
            if (IsMgName(blob) && !IsCannonName(blob))
                return WepMg;
            if (IsCannonName(blob))
                return WepCannon;
            if (fireInterval > 0f && fireInterval <= 0.045f && pierceDamage < 18f)
                return WepMg;
            return WepCannon;
        }

        internal static int MgPenGrade(int calMm)
        {
            if (calMm <= 0)
                return 3;
            if (calMm <= 8)
                return 2;
            if (calMm <= 13)
                return 4;
            if (calMm <= 15)
                return 5;
            return 4;
        }

        internal static int CannonPenGrade(int calMm, float pierceDamage)
        {
            if (calMm <= 0)
                return ClampGrade(PierceToGrade(pierceDamage));
            if (calMm <= 20) return 6;
            if (calMm <= 23) return 7;
            if (calMm <= 25) return 8;
            if (calMm <= 27) return 9;
            if (calMm <= 30) return 10;
            if (calMm <= 35) return 10;
            if (calMm <= 40) return 12;
            if (calMm <= 57) return 16;
            if (calMm <= 76) return 17;
            if (calMm <= 105) return 18;
            if (calMm <= 125) return 19;
            return 20;
        }

        internal static int PierceToGrade(float pierceDamage)
        {
            if (pierceDamage >= 300f) return 20;
            if (pierceDamage >= 150f) return 18;
            if (pierceDamage >= 80f) return 16;
            if (pierceDamage >= 40f) return 13;
            if (pierceDamage >= 20f) return 10;
            if (pierceDamage >= 10f) return 7;
            if (pierceDamage >= 4f) return 4;
            if (pierceDamage > 0.5f) return 2;
            return 0;
        }

        internal static int ParseCaliberMm(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return 0;
            if (NameHas(blob, ".50") || NameHas(blob, "50cal") || NameHas(blob, "50-cal")
                || NameHas(blob, "12.7") || NameHas(blob, "12,7"))
                return 13;
            if (NameHas(blob, "7.62") || NameHas(blob, "7,62") || NameHas(blob, "7.92"))
                return 8;
            if (NameHas(blob, "14.5") || NameHas(blob, "14,5"))
                return 15;
            int best = 0;
            for (int i = 0; i < blob.Length; i++)
            {
                char c = blob[i];
                if (c < '0' || c > '9')
                    continue;
                int n = 0;
                int j = i;
                while (j < blob.Length && blob[j] >= '0' && blob[j] <= '9')
                {
                    n = n * 10 + (blob[j] - '0');
                    j++;
                }
                int k = j;
                while (k < blob.Length && (blob[k] == ' ' || blob[k] == '-' || blob[k] == '_'))
                    k++;
                if (k + 1 < blob.Length)
                {
                    char a = blob[k];
                    char b = blob[k + 1];
                    if ((a == 'm' || a == 'M') && (b == 'm' || b == 'M'))
                    {
                        if (n > best && n >= 5 && n <= 460)
                            best = n;
                    }
                }
                i = j;
            }
            return best;
        }

        internal static bool IsMgName(string blob)
        {
            return NameHas(blob, "machinegun") || NameHas(blob, "machine gun")
                || NameHas(blob, "hmg") || NameHas(blob, "lmg")
                || NameHas(blob, "minigun") || NameHas(blob, "m134")
                || NameHas(blob, "gau-19") || NameHas(blob, "gau19")
                || NameHas(blob, "m2hb") || NameHas(blob, " mg-")
                || NameHas(blob, " mg ") || NameHas(blob, "_mg");
        }

        internal static bool IsCannonName(string blob)
        {
            return NameHas(blob, "cannon") || NameHas(blob, "autocannon")
                || NameHas(blob, "gau-8") || NameHas(blob, "gau8")
                || NameHas(blob, "gau-12") || NameHas(blob, "m61")
                || NameHas(blob, "vulcan") || NameHas(blob, "bk-27")
                || NameHas(blob, "bk27") || NameHas(blob, "aden")
                || NameHas(blob, "defa") || NameHas(blob, "gsh")
                || NameHas(blob, "gsh-") || NameHas(blob, "nr-23")
                || NameHas(blob, "nr-30") || NameHas(blob, "m230")
                || NameHas(blob, "gi-30") || NameHas(blob, "gi30");
        }

        internal static string WeaponBlob(WeaponInfo w)
        {
            if (w == null)
                return string.Empty;
            string a = w.weaponName != null ? w.weaponName : string.Empty;
            string b = w.shortName != null ? w.shortName : string.Empty;
            string d = w.description != null ? w.description : string.Empty;
            return a + " " + b + " " + d;
        }

        /// <summary>
        /// KE guns / shells overmatch as soon as they beat the plate.
        /// Missiles need +2. HEAT / bombs still need +4 so they dump in the plate.
        /// </summary>
        internal static int OvermatchNeed(int wep)
        {
            if (wep == WepMg || wep == WepCannon)
                return 1;
            if (wep == WepMissile)
                return 2;
            return 4;
        }

        internal static bool IsKeNamed(WeaponInfo w)
        {
            if (w == null)
                return false;
            try
            {
                string n = (w.weaponName != null ? w.weaponName : string.Empty) + " "
                    + (w.shortName != null ? w.shortName : string.Empty) + " "
                    + (w.name != null ? w.name : string.Empty);
                if (n.IndexOf("[KE]", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (n.IndexOf("_KE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsPureKeWeapon(int wep)
        {
            return wep == WepMg || wep == WepCannon;
        }

        internal static bool IsPureKeWeapon(int wep, WeaponInfo w)
        {
            if (IsKeNamed(w))
                return true;
            if (IsPureKeWeapon(wep))
                return true;
            if (w == null)
                return false;
            try
            {
                if (w.gun && !w.missile && !w.bomb && !w.glideBomb
                    && w.blastDamage < w.pierceDamage * 0.35f)
                    return true;
            }
            catch { }
            return false;
        }

        internal static int ResolveHit(int penGrade, int armorGrade)
        {
            return ResolveHit(penGrade, armorGrade, WepUnknown);
        }

        internal static int ResolveHit(int penGrade, int armorGrade, int wep)
        {
            if (armorGrade >= GradeRutherford)
                return HitBounce;
            int diff = penGrade - armorGrade;
            if (diff >= OvermatchNeed(wep))
                return HitOvermatch;
            if (diff >= 0)
                return HitPen;
            if (diff >= -3)
                return HitGlance;
            return HitBounce;
        }

        /// <summary>
        /// Overmatch punches through: this plate keeps little pierce; leftover is chained.
        /// </summary>
        internal static float PierceMul(int hit)
        {
            if (hit == HitOvermatch) return 0.42f;
            if (hit == HitPen) return 1f;
            return 0f;
        }

        internal static float BlastMul(int hit, bool bomb)
        {
            return BlastMul(hit, bomb, false);
        }

        internal static float BlastMul(int hit, bool bomb, bool pureKe)
        {
            if (pureKe)
                return 0f;
            if (bomb)
                return 1f;
            if (hit == HitOvermatch) return 0.55f;
            if (hit == HitPen) return 0.85f;
            return 0f;
        }

        internal static float ImpactMul(int hit)
        {
            if (hit == HitBounce || hit == HitGlance)
                return 0f;
            if (hit == HitOvermatch) return 2.1f;
            return 1.55f;
        }

        /// <summary>
        /// Vanilla guns send almost all damage as pierce with impact ~0.
        /// Build a KE term so AP shells and autocannon actually hit as kinetic.
        /// </summary>
        internal static float SynthesizeKe(
            int wep, float listedPierce, float impact, int calMm, float massKg, float speedMps)
        {
            float ke = impact;
            if (wep == WepMg || wep == WepCannon)
            {
                float fromPen = listedPierce * 2.4f + calMm * 1.2f;
                if (fromPen < 10f)
                    fromPen = 10f;
                if (ke < fromPen)
                    ke = fromPen;
                if (speedMps > 80f && massKg > 0.05f)
                {
                    float motion = massKg * speedMps * 0.04f;
                    if (motion > ke)
                        ke = motion;
                }
            }
            else if (wep == WepMissile)
            {
                float fromPierce = listedPierce * 1.6f;
                float motion = 0f;
                if (massKg > 0.5f && speedMps > 40f)
                    motion = massKg * speedMps * 0.018f;
                float want = fromPierce;
                if (motion > want)
                    want = motion;
                if (ke < want)
                    ke = want;
            }
            else if (ke < listedPierce * 0.45f)
                ke = listedPierce * 0.45f;
            if (ke > 1800f)
                ke = 1800f;
            return ke;
        }

        internal static float OverpenLeftoverKe(float impactAfterMul)
        {
            return impactAfterMul * 0.65f;
        }

        internal static float OverpenLeftoverPierce(float pierceAfterMul)
        {
            return pierceAfterMul * 0.9f;
        }

        /// <summary>Part armor vs hull facing. Turret thicker; sensors / tracks thinner.</summary>
        internal static int ModuleArmorAdj(int mod)
        {
            if (mod == ModularArmorService.ModTurret)
                return 2;
            if (mod == ModularArmorService.ModGun)
                return -2;
            if (mod == ModularArmorService.ModEngine)
                return -2;
            if (mod == ModularArmorService.ModMobility)
                return -4;
            if (mod == ModularArmorService.ModAmmo)
                return -1;
            if (mod == ModularArmorService.ModFuel)
                return -3;
            if (mod == ModularArmorService.ModSensors)
                return -6;
            if (mod == ModularArmorService.ModCommand)
                return -2;
            if (mod == ModularArmorService.ModAirframe)
                return 0;
            return 0;
        }

        /// <summary>
        /// Collision / crash impact vs facing armor grade (0–20).
        /// Grade 0 keeps full impact; grade 20 keeps about 8%.
        /// </summary>
        internal static float CollisionArmorMul(int armorGrade)
        {
            if (armorGrade >= GradeRutherford)
                return 0f;
            int g = ClampGrade(armorGrade);
            return Mathf.Lerp(1f, 0.08f, g / (float)GradeMax);
        }

        internal static bool NameHas(string blob, string token)
        {
            if (string.IsNullOrEmpty(blob) || string.IsNullOrEmpty(token))
                return false;
            return blob.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
