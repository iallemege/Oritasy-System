using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oritasy
{
    /// <summary>Local AI infantry disabled for now (spawn/think hitch on foot).</summary>
    internal static class InfantryAi
    {
        internal const int MaxHostile = 6;

        private static readonly List<InfantryAiPawn> Bots = new List<InfantryAiPawn>(12);
        private static float _nextHostileAt;
        private static Hangar[] _hangarCache;
        private static float _hangarCacheUntil;
        private static Material _matFriend;
        private static Material _matFoe;
        private static Material _matGun;

        internal static int Count
        {
            get { return Bots.Count; }
        }

        internal static bool TryMark(int i, out Vector3 pos, out bool friendly)
        {
            pos = Vector3.zero;
            friendly = false;
            if (i < 0 || i >= Bots.Count || Bots[i] == null)
                return false;
            pos = Bots[i].transform.position;
            friendly = Bots[i].Friendly;
            return true;
        }

        internal static InfantryAiPawn BotAt(int i)
        {
            if (i < 0 || i >= Bots.Count)
                return null;
            return Bots[i];
        }

        internal static void Tick()
        {
            if (Bots.Count > 0)
                Clear();
        }

        internal static void SpawnSquad(Vector3 origin, float yaw, bool friendly, int n)
        {
        }

        internal static void Clear()
        {
            for (int i = 0; i < Bots.Count; i++)
            {
                if (Bots[i] != null)
                    UnityEngine.Object.Destroy(Bots[i].gameObject);
            }
            Bots.Clear();
        }

        private static int CountSide(bool friendly)
        {
            int n = 0;
            for (int i = 0; i < Bots.Count; i++)
            {
                if (Bots[i] != null && Bots[i].Friendly == friendly)
                    n++;
            }
            return n;
        }

        private static void SpawnNearEnemyHangar(int n)
        {
            Hangar hangar = EnemyHangar();
            if (hangar == null)
                return;
            Vector3 pos = InfantryAirbaseStations.SpawnNear(hangar);
            if (pos.sqrMagnitude < 0.01f)
                pos = hangar.transform.position + Vector3.up * 0.2f;
            try
            {
                pos += hangar.transform.right * 10f + hangar.transform.forward * 8f;
            }
            catch { }
            float yaw = hangar.transform.eulerAngles.y + 180f;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out hit, 40f, InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                pos = hit.point + Vector3.up * 0.08f;
            SpawnSquad(pos, yaw, false, n);
        }

        private static Hangar[] CachedHangars()
        {
            if (_hangarCache != null && Time.unscaledTime < _hangarCacheUntil)
                return _hangarCache;
            _hangarCacheUntil = Time.unscaledTime + 20f;
            try { _hangarCache = UnityEngine.Object.FindObjectsOfType<Hangar>(); }
            catch { _hangarCache = null; }
            return _hangarCache;
        }

        private static bool HangarIsLocal(Hangar h, FactionHQ hq, Vector3 from)
        {
            if (h == null)
                return true;
            Vector3 hp = h.transform.position;
            if ((hp - from).sqrMagnitude < 380f * 380f)
                return true;
            Airbase ab = null;
            try { ab = h.GetComponentInParent<Airbase>(); }
            catch { }
            if (hq != null && ab != null)
            {
                try
                {
                    if (ab.CurrentHQ != null && object.ReferenceEquals(ab.CurrentHQ, hq))
                        return true;
                }
                catch { }
            }
            if (hq == null)
                return false;
            try
            {
                foreach (Airbase localAb in hq.GetAirbases())
                {
                    if (localAb == null)
                        continue;
                    if ((hp - localAb.transform.position).sqrMagnitude < 450f * 450f)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool HangarIsEnemyOwned(Hangar h, FactionHQ hq)
        {
            if (h == null)
                return false;
            Airbase ab = null;
            try { ab = h.GetComponentInParent<Airbase>(); }
            catch { }
            if (ab == null)
                return false;
            FactionHQ oh = null;
            try { oh = ab.CurrentHQ; }
            catch { }
            if (oh == null)
                return false;
            if (hq != null && object.ReferenceEquals(oh, hq))
                return false;
            return true;
        }

        private static Hangar EnemyHangar()
        {
            FactionHQ hq = null;
            try { GameManager.GetLocalHQ(out hq); }
            catch { }
            Hangar[] all = CachedHangars();
            if (all == null || all.Length == 0)
                return null;
            Vector3 from = InfantryModeService.Active
                ? InfantryModeService.EyePos
                : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            Hangar best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                Hangar h = all[i];
                if (h == null)
                    continue;
                try
                {
                    if (h.Disabled)
                        continue;
                }
                catch { }
                if (MorayRuntime.IsPlayerPad(h))
                    continue;
                if (HangarIsLocal(h, hq, from))
                    continue;
                if (!HangarIsEnemyOwned(h, hq))
                    continue;
                float d = (h.transform.position - from).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = h;
                }
            }
            return best;
        }

        private static void SpawnOne(Vector3 pos, float yaw, bool friendly)
        {
            if (friendly)
                return;
            EnsureMats();
            GameObject go = new GameObject(friendly ? "OritasyAiFriend" : "OritasyAiFoe");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.30f;
            cc.center = new Vector3(0f, 0.88f, 0f);
            cc.slopeLimit = 48f;
            cc.minMoveDistance = 0.002f;
            InfantryPerf.ApplyWalkCollider(cc);
            InfantryAiPawn pawn = go.AddComponent<InfantryAiPawn>();
            pawn.Friendly = friendly;
            pawn.Hp = 80f;
            pawn.Yaw = yaw;
            pawn.Cc = cc;

            InfantryBodyVisual.Attach(go.transform);
            if (go.transform.Find("VanillaPilotBody") == null)
            {
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(go.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.42f, 0.9f, 0.42f);
                Renderer br = body.GetComponent<Renderer>();
                if (br != null)
                {
                    br.sharedMaterial = friendly ? _matFriend : _matFoe;
                    br.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            GameObject hitbox = new GameObject("Hitbox");
            hitbox.transform.SetParent(go.transform, false);
            CapsuleCollider cap = hitbox.AddComponent<CapsuleCollider>();
            cap.height = 1.75f;
            cap.radius = 0.34f;
            cap.center = new Vector3(0f, 0.88f, 0f);

            GameObject eye = new GameObject("Eye");
            eye.transform.SetParent(go.transform, false);
            eye.transform.localPosition = new Vector3(0f, 1.58f, 0.12f);
            pawn.Eye = eye.transform;
            Bots.Add(pawn);
            pawn.CacheSkin();
        }

        private static void EnsureMats()
        {
            if (_matFriend != null)
                return;
            Material src = InfantryFx.UnlitMat();
            if (src == null)
            {
                Shader sh = Shader.Find("Unlit/Color");
                if (sh == null)
                    sh = Shader.Find("Standard");
                src = new Material(sh);
            }
            _matFriend = new Material(src);
            _matFriend.color = new Color(0.22f, 0.42f, 0.28f);
            _matFoe = new Material(src);
            _matFoe.color = new Color(0.48f, 0.16f, 0.12f);
            _matGun = new Material(src);
            _matGun.color = new Color(0.12f, 0.12f, 0.13f);
        }
    }

    internal sealed class InfantryAiPawn : MonoBehaviour, IDamageable
    {
        public bool Friendly;
        public float Hp = 80f;
        public float Yaw;
        public float NextShot;
        public CharacterController Cc;
        public Transform Eye;
        private ArmorProperties _armor;
        private float _vert;
        private float _fatigue;
        private float _hurtAt;
        private bool _aimAir;
        private Vector3 _memPos;
        private float _memUntil;
        private bool _hasMem;
        private bool _seeing;
        private float _scanPhase;
        private Renderer[] _skinRs;
        private Color[] _skinBase;
        private float _flashUntil;
        private Vector3 _wish;
        private bool _wasFlash;

        private void Awake()
        {
            _armor = new ArmorProperties();
            _armor.pierceArmor = 6f;
            _armor.blastArmor = 4f;
            _armor.fireArmor = 3f;
            _armor.pierceTolerance = 3f;
            _armor.blastTolerance = 3f;
            _armor.fireTolerance = 2f;
            _armor.overpressureLimit = 9000f;
        }

        internal void CacheSkin()
        {
            _skinRs = GetComponentsInChildren<Renderer>(true);
            if (_skinRs == null)
                return;
            _skinBase = new Color[_skinRs.Length];
            for (int i = 0; i < _skinRs.Length; i++)
            {
                if (_skinRs[i] == null || _skinRs[i].sharedMaterial == null)
                    continue;
                Material inst = new Material(_skinRs[i].sharedMaterial);
                _skinRs[i].sharedMaterial = inst;
                _skinBase[i] = inst.color;
            }
        }

        private void TickFlash()
        {
            if (_skinRs == null)
                return;
            bool on = Time.time < _flashUntil;
            if (!on && !_wasFlash)
                return;
            _wasFlash = on;
            for (int i = 0; i < _skinRs.Length; i++)
            {
                if (_skinRs[i] == null || _skinRs[i].sharedMaterial == null)
                    continue;
                Color baseC = _skinBase != null && i < _skinBase.Length
                    ? _skinBase[i]
                    : new Color(0.48f, 0.16f, 0.12f);
                _skinRs[i].sharedMaterial.color = on
                    ? Color.Lerp(baseC, new Color(1f, 0.82f, 0.35f), 0.82f)
                    : baseC;
            }
        }

        internal void Think(float dt)
        {
            if (Hp <= 0f || Cc == null)
                return;
            TickFlash();
            Vector3 aim;
            float dist;
            Transform tgtRoot;
            _seeing = PickVisible(out aim, out dist, out tgtRoot);
            if (_seeing)
            {
                _memPos = aim;
                _memUntil = Time.time + InfantryAiSight.MemorySec;
                _hasMem = true;
            }
            else if (_hasMem && Time.time < _memUntil)
            {
                aim = _memPos;
                dist = Vector3.Distance(transform.position, aim);
                tgtRoot = null;
            }
            else
            {
                _hasMem = false;
                _scanPhase += dt;
                Vector3 patrol = transform.forward;
                if (!InfantryAiSight.WalkClear(transform, patrol, 2.6f))
                {
                    Yaw += (80f + Mathf.Sin(_scanPhase * 1.1f) * 25f) * dt;
                    transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
                    _wish = Vector3.zero;
                    Walk(_wish, dt);
                    InfantryBodyVisual.Tick(transform, 0f, false);
                }
                else
                {
                    Yaw += Mathf.Sin(_scanPhase * 0.35f) * 8f * dt;
                    transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
                    _wish = patrol;
                    Walk(_wish, dt);
                    InfantryBodyVisual.Tick(transform, 2.4f, false);
                }
                return;
            }

            Vector3 flat = aim - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.05f)
                Yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            if (Eye != null && _seeing)
            {
                Vector3 to = aim - Eye.position;
                if (to.sqrMagnitude > 0.01f)
                    Eye.rotation = Quaternion.LookRotation(to.normalized);
            }

            double roll = (GetInstanceID() * 0.6180339887 + Time.time * 0.37) % 1.0;
            if (roll < 0.0) roll += 1.0;
            int act = InfantryAiNnService.Decide(dist, Hp, _aimAir, Friendly, Time.time < _hurtAt + 2.5f, roll);
            Vector3 wish = Vector3.zero;
            if (act == InfantryAiNnService.ActAdvance || !_seeing)
                wish = flat.normalized;
            else if (act == InfantryAiNnService.ActRetreat)
                wish = -flat.normalized * 0.55f;
            else if (act == InfantryAiNnService.ActFlank)
            {
                Vector3 side = Vector3.Cross(Vector3.up, flat.normalized);
                if (GetInstanceID() % 2 == 0)
                    side = -side;
                wish = (side + flat.normalized * 0.25f).normalized;
            }
            if (wish.sqrMagnitude > 0.01f && !InfantryAiSight.WalkClear(transform, wish, 2.2f))
            {
                Vector3 left = Vector3.Cross(Vector3.up, wish).normalized;
                Vector3 right = -left;
                if (InfantryAiSight.WalkClear(transform, left, 1.8f))
                    wish = left;
                else if (InfantryAiSight.WalkClear(transform, right, 1.8f))
                    wish = right;
                else
                    wish = Vector3.zero;
            }
            _wish = wish;
            Walk(wish, dt);
            InfantryBodyVisual.Tick(transform, wish.sqrMagnitude > 0.01f ? 3.8f : 0f, false);

            if (_seeing && InfantryAiNnService.WantsFire(act, dist) && Time.time >= NextShot && Eye != null)
            {
                Vector3 muzzle = Eye.position;
                if (!InfantryAiSight.HasLos(muzzle, aim, transform, tgtRoot))
                    return;
                NextShot = Time.time + (Friendly ? 0.16f : 0.24f);
                Vector3 dir = (aim - muzzle).normalized;
                InfantryCombat.AiFire(muzzle + dir * 0.4f, dir, InfantryGun.Ak47, transform);
            }
        }

        private bool PickVisible(out Vector3 aim, out float dist, out Transform tgtRoot)
        {
            aim = Vector3.zero;
            dist = 0f;
            tgtRoot = null;
            _aimAir = false;
            Vector3 eye = Eye != null ? Eye.position : (transform.position + Vector3.up * 1.55f);
            Vector3 fwd = Eye != null ? Eye.forward : transform.forward;
            Vector3 best = Vector3.zero;
            float bestD = float.MaxValue;
            Transform bestRoot = null;
            bool found = false;
            bool bestAir = false;

            if (!Friendly && InfantryModeService.Active && InfantryModeService.PawnRoot != null)
            {
                Transform pr = InfantryModeService.PawnRoot;
                Vector3 p = pr.position + Vector3.up * 1.1f;
                float d = (p - eye).sqrMagnitude;
                float maxR = InfantryAiSight.RangeSoldier;
                if (d < bestD && d <= maxR * maxR
                    && InfantryAiSight.CanSee(eye, fwd, p, maxR, transform, pr))
                {
                    bestD = d;
                    best = p;
                    bestRoot = pr;
                    found = true;
                    bestAir = false;
                }
            }

            int botN = InfantryAi.Count;
            for (int bi = 0; bi < botN; bi++)
            {
                InfantryAiPawn other = InfantryAi.BotAt(bi);
                if (other == null || other.Hp <= 0f)
                    continue;
                if (object.ReferenceEquals(other, this))
                    continue;
                if (other.Friendly == Friendly)
                    continue;
                Vector3 p = other.transform.position + Vector3.up * 1.1f;
                float d = (p - eye).sqrMagnitude;
                float maxR = InfantryAiSight.RangeSoldier;
                if (d >= bestD || d > maxR * maxR)
                    continue;
                if (!InfantryAiSight.CanSee(eye, fwd, p, maxR, transform, other.transform))
                    continue;
                bestD = d;
                best = p;
                bestRoot = other.transform;
                found = true;
                bestAir = false;
            }

            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all != null)
                {
                    FactionHQ hq = null;
                    try { GameManager.GetLocalHQ(out hq); }
                    catch { }
                    for (int i = 0; i < all.Count; i++)
                    {
                        Unit u = all[i];
                        if (u == null || u.disabled)
                            continue;
                        if (!(u is Aircraft) && !(u is GroundVehicle))
                            continue;
                        bool hostile = true;
                        try
                        {
                            FactionHQ oh = u.NetworkHQ;
                            if (hq != null && oh != null)
                                hostile = !object.ReferenceEquals(oh, hq);
                        }
                        catch { }
                        if (Friendly && !hostile)
                            continue;
                        if (!Friendly && hostile)
                            continue;
                        Vector3 p = u.transform.position;
                        float maxR = InfantryAiSight.RangeVehicle;
                        if (u is Aircraft)
                        {
                            p += Vector3.up * 1.2f;
                            maxR = InfantryAiSight.RangeAircraft;
                        }
                        float d = (p - eye).sqrMagnitude;
                        if (d >= bestD || d > maxR * maxR)
                            continue;
                        if (!InfantryAiSight.CanSee(eye, fwd, p, maxR, transform, u.transform))
                            continue;
                        bestD = d;
                        best = p;
                        bestRoot = u.transform;
                        found = true;
                        bestAir = u is Aircraft;
                    }
                }
            }
            catch { }

            if (!found)
                return false;
            aim = best;
            dist = Mathf.Sqrt(bestD);
            tgtRoot = bestRoot;
            _aimAir = bestAir;
            return true;
        }

        private void Walk(Vector3 wish, float dt)
        {
            if (Cc == null)
                return;
            if (wish.sqrMagnitude > 1f)
                wish.Normalize();
            Vector3 vel = wish * 3.8f;
            if (Cc.isGrounded && _vert < 0f)
                _vert = -2f;
            _vert += Physics.gravity.y * dt;
            vel.y = _vert;
            Cc.Move(vel * dt);
        }

        public void TakeDamage(float pierceDamage, float blastDamage, float amountAffected, float fireDamage, float ImpactDamage, PersistentID dealerID)
        {
            int pen = ModularArmorService.PenGradeIncoming(dealerID, pierceDamage);
            WeaponInfo w = ModularArmorService.WeaponFromDealer(dealerID);
            int wep = ModularArmorMathService.ClassifyKinetic(w);
            if (wep == ModularArmorMathService.WepUnknown)
            {
                if (blastDamage > pierceDamage * 0.8f && blastDamage > 8f)
                    wep = ModularArmorMathService.WepRocket;
                else if (pen >= 13)
                    wep = ModularArmorMathService.WepMissile;
                else
                    wep = ModularArmorMathService.WepCannon;
            }
            pen = ModularArmorMathService.ApplyPenFloat(pen);
            if (_fatigue < 0f) _fatigue = 0f;
            if (_fatigue > 1f) _fatigue = 1f;
            int outer = ArmorFatigueService.EffectiveArmor(3, _fatigue, 2);
            int hit = ModularArmorMathService.ResolveHit(pen, outer, wep);
            float fat = 0.05f;
            if (hit == ModularArmorMathService.HitGlance)
                fat = 0.09f;
            else if (hit == ModularArmorMathService.HitPen)
                fat = 0.22f;
            else if (hit == ModularArmorMathService.HitOvermatch)
                fat = 0.42f;
            int gap = pen - outer;
            if (gap < 0)
                gap = -gap;
            if (gap <= 1)
                fat *= 1.7f;
            _fatigue += fat;
            if (_fatigue > 1f)
                _fatigue = 1f;
            _hurtAt = Time.time;
            _flashUntil = Time.time + 0.22f;
            InfantryFx.Sparks(transform.position + Vector3.up * 1.15f, Vector3.up);
            float pass = 0.55f;
            if (hit == ModularArmorMathService.HitOvermatch)
                pass = 0.9f;
            else if (hit == ModularArmorMathService.HitBounce
                || hit == ModularArmorMathService.HitGlance)
                pass = 0.28f;
            float dmg = pierceDamage * pass + blastDamage * pass * 0.4f
                + ImpactDamage * pass + fireDamage * 0.2f;
            if (dmg < 6f)
                dmg = 6f;
            Hp -= dmg;
            if (Hp <= 0f)
                Die();
        }

        public void ApplyDamage(float pierceDamage, float blastDamage, float fireDamage, float impactDamage)
        {
            TakeDamage(pierceDamage, blastDamage, 1f, fireDamage, impactDamage, PersistentID.None);
        }

        public void TakeShockwave(Vector3 origin, float overpressure, float blastPower)
        {
            TakeDamage(overpressure > 4000f ? 20f : 8f, blastPower * 0.02f, 1f, 0f, 0f, PersistentID.None);
        }

        public void Detach(Vector3 velocity, Vector3 relativePos)
        {
        }

        public ArmorProperties GetArmorProperties()
        {
            return _armor;
        }

        public Unit GetUnit()
        {
            return InfantryModeService.LinkedUnit();
        }

        public float GetMass()
        {
            return 90f;
        }

        public Transform GetTransform()
        {
            return transform;
        }

        private void Die()
        {
            InfantryBodyVisual.PlayDead(transform);
            InfantryFx.Sparks(transform.position + Vector3.up, Vector3.up);
            UnityEngine.Object.Destroy(gameObject, 4.5f);
            if (Cc != null)
                Cc.enabled = false;
            Hp = 0f;
            enabled = false;
        }
    }
}
