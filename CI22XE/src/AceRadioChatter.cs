using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Ace Combat-style grunt radio subtitles. Nearby unnamed AI aircraft speak on
    /// the same beats as AC mooks: first contact, missile shot, incoming, on-six,
    /// hit, and going down.
    /// </summary>
    internal static class AceRadioChatter
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> RangeM;
        internal static ConfigEntry<float> HoldSeconds;

        private const float ScanInterval = 0.35f;
        private const float GlobalGap = 1.55f;
        private const float SpeakerGap = 5.5f;
        private const float EventGap = 2.4f;
        private const float OnSixDist = 1800f;
        private const float AdvantageDist = 2200f;
        private const float HitMinDamage = 6f;
        private const float FadeIn = 0.12f;
        private const float FadeOut = 0.4f;
        private const float NearbyHearM = 7000f;
        private const float ConvoyHearM = 8000f;
        private const int ConvoyMinCount = 2;
        private const float AceRumorAfterSec = 480f;

        private static readonly FieldInfo MissileTargetField = AccessTools.Field(typeof(Missile), "target");

        private static readonly Dictionary<int, float> LastSpeakAt = new Dictionary<int, float>(64);
        private static readonly Dictionary<int, byte> Contacted = new Dictionary<int, byte>(64);
        private static readonly Dictionary<int, float> LastOnSixAt = new Dictionary<int, float>(32);
        private static readonly Dictionary<int, float> LastAdvAt = new Dictionary<int, float>(32);
        private static readonly float[] LastEventAt = new float[14];

        private static Line _current;
        private static Line _queued;
        private static bool _hasCurrent;
        private static bool _hasQueued;
        private static float _nextScan;
        private static float _nextGlobal;
        private static float _nextPrune;
        private static Aircraft _pendingAce;
        private static Unit _pendingGround;
        private static float _pendingAceAt;
        private static float _pendingGroundAt;
        private static float _nextArmyIntercept;
        private static bool _armyFirstBurst;
        private static float _nextFriendlyChat;
        private static float _nextBetrayedChat;
        private static GUIStyle _callStyle;
        private static GUIStyle _quoteStyle;
        private static Font _styledFont;

        private struct Line
        {
            public string Callsign;
            public string Quote;
            public bool Enemy;
            public bool Intercept;
            public float Born;
            public float Dies;
            public int Priority;
        }

        internal static void Bind(ConfigFile cfg)
        {
            if (cfg == null)
                return;
            Enabled = cfg.Bind("Presentation", "AceRadioChatter", true,
                "Ace Combat-style grunt radio subtitles for nearby AI aircraft and ground vehicles.");
            RangeM = cfg.Bind("Presentation", "AceRadioRangeMeters", 7000f,
                "Max range (m) to hear radio. Capped at 7 km — only nearby traffic.");
            HoldSeconds = cfg.Bind("Presentation", "AceRadioHoldSeconds", 3.2f,
                "How long each subtitle stays on screen.");
        }

        internal static bool IsOn()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static void Tick()
        {
            if (!IsOn())
            {
                _hasCurrent = false;
                _hasQueued = false;
                return;
            }
            if (!InMission())
                return;

            float now = Time.unscaledTime;
            if (_hasCurrent && now >= _current.Dies)
            {
                _hasCurrent = false;
                if (_hasQueued)
                {
                    _current = _queued;
                    _hasQueued = false;
                    _hasCurrent = true;
                    _nextGlobal = now + GlobalGap;
                }
            }

            if (now < _nextScan)
                return;
            _nextScan = now + ScanInterval;
            if (_pendingAce != null && now >= _pendingAceAt)
            {
                Aircraft ace = _pendingAce;
                _pendingAce = null;
                TrySpeak(ace, 8, 8);
            }
            if (_pendingGround != null && now >= _pendingGroundAt)
            {
                Unit g = _pendingGround;
                _pendingGround = null;
                TrySpeak(g, 8, 8);
            }
            ScanGeometry();
            ScanGroundVehicles();
            ScanFriendlyAir(now);
            ScanBetrayedChatter(now);
            TickArmyIntercept(now);
            if (now >= _nextPrune)
            {
                _nextPrune = now + 20f;
                PruneStale(now);
            }
        }

        internal static void Draw()
        {
            if (!IsOn() || !_hasCurrent)
                return;
            if (OritasyPresentation.SplashActive)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            if (!InMission())
                return;

            EnsureStyles();
            float now = Time.unscaledTime;
            float alpha = 1f;
            float age = now - _current.Born;
            float left = _current.Dies - now;
            if (age < FadeIn)
                alpha = Mathf.Clamp01(age / FadeIn);
            else if (left < FadeOut)
                alpha = Mathf.Clamp01(left / FadeOut);
            if (alpha <= 0.02f)
                return;

            float w = Mathf.Clamp(UiScaleService.Width * 0.48f, 420f, 720f);
            float h = 58f;
            float x = (UiScaleService.Width - w) * 0.5f;
            float y = UiScaleService.Height * 0.74f;

            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.04f, 0.06f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

            Color accent;
            if (_current.Intercept)
                accent = new Color(1f, 0.78f, 0.22f, 0.95f * alpha);
            else if (_current.Enemy)
                accent = new Color(1f, 0.38f, 0.22f, 0.95f * alpha);
            else
                accent = new Color(0.28f, 0.82f, 1f, 0.95f * alpha);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(x, y, 5f, h), Texture2D.whiteTexture);

            _callStyle.normal.textColor = new Color(accent.r, accent.g, accent.b, alpha);
            _quoteStyle.normal.textColor = new Color(1f, 1f, 1f, 0.96f * alpha);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 16f, y + 4f, w - 24f, 18f), _current.Callsign, _callStyle);
            GUI.Label(new Rect(x + 16f, y + 22f, w - 24f, 30f), "\"" + _current.Quote + "\"", _quoteStyle);
            GUI.color = prev;
        }

        internal static void NotifyDefected()
        {
            if (!IsOn() || !InMission())
                return;
            float now = Time.unscaledTime;
            _nextArmyIntercept = now + 4.4f;
            _armyFirstBurst = true;
            Unit speaker = FindNearestBetrayed(true);
            Aircraft local = LocalAircraft();
            if (speaker == null || local == null || !InRange(local, speaker))
                return;
            string quote = PickTraitorQuote(speaker, speaker is GroundVehicle);
            if (string.IsNullOrEmpty(quote))
                quote = UiLang.T("Traitor!", "叛徒！");
            ForceLine(Callsign(speaker), quote, IsHostileToLocal(speaker), false, 9, 10);
        }

        internal static void NotifyMissile(Missile missile)
        {
            if (!IsOn() || missile == null || !InMission())
                return;
            Aircraft local = LocalAircraft();
            if (local == null)
                return;

            Unit owner = null;
            try { owner = missile.owner; }
            catch { }
            Aircraft shooter = owner as Aircraft;
            GroundVehicle gvShooter = owner as GroundVehicle;
            Unit tgt = ResolveMissileTarget(missile);
            Aircraft victim = tgt as Aircraft;
            GroundVehicle gvVictim = tgt as GroundVehicle;

            bool shooterGrunt = IsGrunt(shooter, false);
            bool victimGrunt = IsGrunt(victim, false);
            bool gvShooterGrunt = IsGruntUnit(gvShooter, false);
            bool gvVictimGrunt = IsGruntUnit(gvVictim, false);
            bool atPlayer = victim != null && GameManager.IsLocalAircraft(victim);
            bool fromPlayer = shooter != null && GameManager.IsLocalAircraft(shooter);

            if (shooterGrunt && InRange(local, shooter) && (atPlayer || victim == null))
                TrySpeak(shooter, 2, 5);
            else if (gvShooterGrunt && InRange(local, gvShooter) && (atPlayer || victim == null))
                TrySpeak(gvShooter, 2, 5);
            else if (victimGrunt && InRange(local, victim) && (fromPlayer || shooterGrunt || gvShooterGrunt))
                TrySpeak(victim, 3, 6);
            else if (gvVictimGrunt && InRange(local, gvVictim) && (fromPlayer || shooterGrunt || gvShooterGrunt))
                TrySpeak(gvVictim, 3, 6);
        }

        internal static void NotifyHit(Unit unit, float damage)
        {
            if (!IsOn() || unit == null || damage < HitMinDamage)
                return;
            if (!InMission() || !IsGruntUnit(unit, false))
                return;
            if (UnitIsDown(unit))
                return;
            Aircraft local = LocalAircraft();
            if (local == null || !InRange(local, unit))
                return;
            TrySpeak(unit, 6, 7);
        }

        internal static void NotifyDown(Unit unit)
        {
            if (!IsOn() || unit == null || !InMission())
                return;
            if (!IsGruntUnit(unit, true))
                return;
            if (!UnitIsDown(unit))
                return;
            Aircraft local = LocalAircraft();
            if (local == null)
                return;
            if (!InRange(local, unit))
                return;
            TrySpeak(unit, 7, 9);
            Aircraft ac = unit as Aircraft;
            if (ac != null)
                MaybeNearbyAce(local, ac);
            else
                MaybeNearbyGround(local, unit);
        }

        private static void ScanGeometry()
        {
            Aircraft local = LocalAircraft();
            if (local == null)
                return;
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            if (all == null)
                return;

            Vector3 myPos = local.transform.position;
            Vector3 myFwd = local.transform.forward;
            myFwd.y = 0f;
            if (myFwd.sqrMagnitude > 0.01f)
                myFwd.Normalize();

            float range = EffectiveHearRange();
            float rangeSq = range * range;
            float now = Time.unscaledTime;

            for (int i = 0; i < all.Count; i++)
            {
                Aircraft other = all[i];
                if (other == null || !IsGrunt(other, false))
                    continue;
                if (!IsHostileToLocal(other))
                    continue;
                Vector3 d = other.transform.position - myPos;
                float sq = d.sqrMagnitude;
                if (sq > rangeSq || sq < 1f)
                    continue;

                int id = other.GetInstanceID();
                if (!Contacted.ContainsKey(id))
                {
                    if (TrySpeak(other, 1, 3))
                        Contacted[id] = 1;
                    continue;
                }

                try
                {
                    if (other.IsLanded())
                        continue;
                }
                catch { }

                float dist = Mathf.Sqrt(sq);
                Vector3 toOther = d;
                toOther.y = 0f;
                if (toOther.sqrMagnitude < 1f)
                    continue;
                toOther.Normalize();

                Vector3 otherFwd = other.transform.forward;
                otherFwd.y = 0f;
                if (otherFwd.sqrMagnitude < 0.01f)
                    continue;
                otherFwd.Normalize();

                // Player on their six.
                if (dist < OnSixDist
                    && Vector3.Dot(otherFwd, toOther) < -0.5f
                    && Vector3.Dot(myFwd, toOther) > 0.35f)
                {
                    float last;
                    if (!LastOnSixAt.TryGetValue(id, out last) || now - last > 8f)
                    {
                        LastOnSixAt[id] = now;
                        TrySpeak(other, 4, 7);
                    }
                    continue;
                }

                // They have the advantage (on player's six).
                if (dist < AdvantageDist
                    && Vector3.Dot(myFwd, toOther) < -0.45f
                    && Vector3.Dot(otherFwd, -toOther) > 0.35f)
                {
                    float lastA;
                    if (!LastAdvAt.TryGetValue(id, out lastA) || now - lastA > 10f)
                    {
                        LastAdvAt[id] = now;
                        TrySpeak(other, 5, 4);
                    }
                }
            }
        }

        private static void ScanGroundVehicles()
        {
            Aircraft local = LocalAircraft();
            if (local == null)
                return;
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { }
            if (all == null)
                return;

            Vector3 myPos = local.transform.position;
            Vector3 myFwd = local.transform.forward;
            myFwd.y = 0f;
            if (myFwd.sqrMagnitude > 0.01f)
                myFwd.Normalize();

            float range = EffectiveHearRange();
            float rangeSq = range * range;
            float now = Time.unscaledTime;
            float myAlt = myPos.y;

            for (int i = 0; i < all.Count; i++)
            {
                GroundVehicle gv = all[i] as GroundVehicle;
                if (gv == null || !IsGruntUnit(gv, false))
                    continue;
                if (!IsHostileToLocal(gv))
                    continue;
                Vector3 d = gv.transform.position - myPos;
                float sq = d.sqrMagnitude;
                if (sq > rangeSq || sq < 1f)
                    continue;

                int id = gv.GetInstanceID();
                if (!Contacted.ContainsKey(id))
                {
                    if (TrySpeak(gv, 1, 3))
                        Contacted[id] = 1;
                    continue;
                }

                float dist = Mathf.Sqrt(sq);
                Vector3 toGv = d;
                toGv.y = 0f;
                if (toGv.sqrMagnitude < 1f)
                    continue;
                toGv.Normalize();

                // Player lining up a gun run (Ace Combat ground "they're coming in low").
                float altAgl = myAlt - gv.transform.position.y;
                if (dist < 2200f && altAgl > 20f && altAgl < 900f
                    && Vector3.Dot(local.transform.forward, (gv.transform.position - myPos).normalized) > 0.55f)
                {
                    float last;
                    if (!LastOnSixAt.TryGetValue(id, out last) || now - last > 8f)
                    {
                        LastOnSixAt[id] = now;
                        TrySpeak(gv, 4, 7);
                    }
                    continue;
                }

                if (dist < 8000f && VehicleTrackingPlayer(gv, local))
                {
                    float lastA;
                    if (!LastAdvAt.TryGetValue(id, out lastA) || now - lastA > 10f)
                    {
                        LastAdvAt[id] = now;
                        TrySpeak(gv, 5, 4);
                    }
                }
            }
        }

        private static void ScanFriendlyAir(float now)
        {
            if (now < _nextFriendlyChat)
                return;
            Aircraft local = LocalAircraft();
            if (local == null)
            {
                _nextFriendlyChat = now + 4f;
                return;
            }
            Aircraft pick = PickNearbyFriendlyAir(local);
            if (pick == null)
            {
                _nextFriendlyChat = now + 5f;
                return;
            }
            _nextFriendlyChat = now + UnityEngine.Random.Range(11f, 24f);
            int ev = 11;
            if (AceRumorUnlocked() && UnityEngine.Random.value < 0.34f)
                ev = 12;
            TrySpeak(pick, ev, 2);
        }

        private static Aircraft PickNearbyFriendlyAir(Aircraft local)
        {
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            if (all == null)
                return null;
            float rangeSq = EffectiveHearRange() * EffectiveHearRange();
            Vector3 myPos = local.transform.position;
            Aircraft best = null;
            int seen = 0;
            int pickAt = UnityEngine.Random.Range(0, 8);
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft o = all[i];
                if (o == null || object.ReferenceEquals(o, local) || !IsGrunt(o, false))
                    continue;
                if (IsHostileToLocal(o) || AirportDefection.IsBetrayedFaction(o))
                    continue;
                try
                {
                    if (o.IsLanded())
                        continue;
                }
                catch { }
                float sq = (o.transform.position - myPos).sqrMagnitude;
                if (sq > rangeSq || sq < 80f)
                    continue;
                if (seen == pickAt)
                    return o;
                best = o;
                seen++;
            }
            return best;
        }

        private static bool VehicleTrackingPlayer(GroundVehicle gv, Aircraft local)
        {
            if (gv == null || local == null)
                return false;
            try
            {
                Weapon[] weapons = gv.GetComponentsInChildren<Weapon>(true);
                if (weapons == null)
                    return false;
                int lid = local.GetInstanceID();
                for (int i = 0; i < weapons.Length; i++)
                {
                    Weapon w = weapons[i];
                    if (w == null)
                        continue;
                    Unit t = null;
                    try { t = w.GetTarget(); }
                    catch { }
                    if (t != null && t.GetInstanceID() == lid)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void MaybeNearbyAce(Aircraft local, Aircraft downed)
        {
            if (UnityEngine.Random.value > 0.42f)
                return;
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            if (all == null)
                return;
            Vector3 pos = downed.transform.position;
            Aircraft best = null;
            float bestSq = 9000f * 9000f;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft o = all[i];
                if (o == null || object.ReferenceEquals(o, downed) || !IsGrunt(o, false))
                    continue;
                if (!SameSide(o, downed))
                    continue;
                float sq = (o.transform.position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = o;
                }
            }
            if (best != null && InRange(local, best))
            {
                _pendingAce = best;
                _pendingAceAt = Time.unscaledTime + 1.65f;
            }
        }

        private static void MaybeNearbyGround(Aircraft local, Unit downed)
        {
            if (UnityEngine.Random.value > 0.42f)
                return;
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { }
            if (all == null || downed == null)
                return;
            Vector3 pos = downed.transform.position;
            GroundVehicle best = null;
            float bestSq = 6000f * 6000f;
            for (int i = 0; i < all.Count; i++)
            {
                GroundVehicle o = all[i] as GroundVehicle;
                if (o == null || object.ReferenceEquals(o, downed) || !IsGruntUnit(o, false))
                    continue;
                if (!SameSide(o, downed))
                    continue;
                float sq = (o.transform.position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = o;
                }
            }
            if (best != null && InRange(local, best))
            {
                _pendingGround = best;
                _pendingGroundAt = Time.unscaledTime + 1.65f;
            }
        }

        private static bool TrySpeak(Unit speaker, int ev, int priority)
        {
            if (speaker == null)
                return false;
            float now = Time.unscaledTime;
            if (now < _nextGlobal)
                return false;
            ev = RemapTraitorEvent(speaker, ev);
            if (ev == 12 && !AceRumorUnlocked())
                ev = 11;
            if (ev >= 0 && ev < LastEventAt.Length && now - LastEventAt[ev] < EventGap)
                return false;
            int id = speaker.GetInstanceID();
            float last;
            if (LastSpeakAt.TryGetValue(id, out last) && now - last < SpeakerGap)
                return false;

            string quote;
            bool awacs = ev == 1;
            if (ev == 12)
                quote = PickAceRumor();
            else if (ev == 9)
                quote = PickTraitorQuote(speaker, speaker is GroundVehicle);
            else if (awacs)
                quote = PickAwacsLine(speaker is GroundVehicle);
            else
                quote = PickLine(ev, speaker is GroundVehicle);
            if (awacs)
                quote = FillAwacsSlots(quote, speaker);
            else
                quote = FillRadioSlots(quote, speaker);
            if (string.IsNullOrEmpty(quote))
                return false;

            Line line = new Line();
            line.Callsign = awacs ? AwacsCallsign() : Callsign(speaker);
            line.Quote = quote;
            line.Enemy = awacs ? false : IsHostileToLocal(speaker);
            line.Intercept = awacs;
            line.Born = now;
            float hold = HoldSeconds != null ? HoldSeconds.Value : 3.2f;
            if (hold < 1.6f)
                hold = 1.6f;
            line.Dies = now + hold;
            line.Priority = priority;

            if (!_hasCurrent)
            {
                _current = line;
                _hasCurrent = true;
            }
            else if (priority > _current.Priority || now - _current.Born > 1.1f)
            {
                _current = line;
                _hasCurrent = true;
                _hasQueued = false;
            }
            else if (!_hasQueued)
            {
                _queued = line;
                _hasQueued = true;
            }
            else
                return false;

            LastSpeakAt[id] = now;
            if (ev >= 0 && ev < LastEventAt.Length)
                LastEventAt[ev] = now;
            _nextGlobal = now + GlobalGap;
            return true;
        }

        private static int RemapTraitorEvent(Unit speaker, int ev)
        {
            int tone = AirportDefection.TraitorTone(speaker);
            if (tone != 1 && tone != 2 && tone != 4)
                return ev;
            if (ev == 1 || ev == 2 || ev == 4 || ev == 5)
            {
                if (UnityEngine.Random.value < 0.78f)
                    return 9;
            }
            return ev;
        }

        private static void ForceLine(string callsign, string quote, bool enemy, bool intercept, int ev, int priority)
        {
            quote = FillRadioSlots(quote, null);
            if (string.IsNullOrEmpty(quote))
                return;
            float now = Time.unscaledTime;
            Line line = new Line();
            line.Callsign = callsign;
            line.Quote = quote;
            line.Enemy = enemy;
            line.Intercept = intercept;
            line.Born = now;
            float hold = HoldSeconds != null ? HoldSeconds.Value : 3.2f;
            if (intercept)
                hold = Mathf.Max(hold, 4.4f);
            if (hold < 1.6f)
                hold = 1.6f;
            line.Dies = now + hold;
            line.Priority = priority;
            _current = line;
            _hasCurrent = true;
            _hasQueued = false;
            if (ev >= 0 && ev < LastEventAt.Length)
                LastEventAt[ev] = now;
            _nextGlobal = now + GlobalGap;
        }

        private static Unit FindNearestBetrayed(bool preferLastLeft)
        {
            Unit lastLeft = preferLastLeft ? FindNearestBetrayedCore(true) : null;
            if (lastLeft != null)
                return lastLeft;
            return FindNearestBetrayedCore(false);
        }

        private static Unit FindNearestBetrayedCore(bool lastLeftOnly)
        {
            Aircraft local = LocalAircraft();
            if (local == null)
                return null;
            Vector3 myPos = local.transform.position;
            float hear = EffectiveHearRange();
            float bestSq = hear * hear;
            Unit best = null;
            List<Aircraft> air = null;
            try { air = UnitRegistry.allAircraft; }
            catch { }
            if (air != null)
            {
                for (int i = 0; i < air.Count; i++)
                {
                    Aircraft o = air[i];
                    if (o == null || !IsGrunt(o, false) || !AirportDefection.IsBetrayedFaction(o))
                        continue;
                    if (lastLeftOnly && !AirportDefection.IsLastLeftFaction(o))
                        continue;
                    float sq = (o.transform.position - myPos).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = o;
                    }
                }
            }
            if (best != null)
                return best;
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { }
            if (all == null)
                return null;
            for (int i = 0; i < all.Count; i++)
            {
                GroundVehicle gv = all[i] as GroundVehicle;
                if (gv == null || !IsGruntUnit(gv, false) || !AirportDefection.IsBetrayedFaction(gv))
                    continue;
                if (lastLeftOnly && !AirportDefection.IsLastLeftFaction(gv))
                    continue;
                float sq = (gv.transform.position - myPos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = gv;
                }
            }
            return best;
        }

        private static void ScanBetrayedChatter(float now)
        {
            if (!AirportDefection.TraitorRadioActive)
                return;
            if (now < _nextBetrayedChat)
                return;
            Aircraft local = LocalAircraft();
            if (local == null)
            {
                _nextBetrayedChat = now + 4f;
                return;
            }
            Unit pick = PickNearbyBetrayed(local);
            if (pick == null)
            {
                _nextBetrayedChat = now + 4f;
                return;
            }
            _nextBetrayedChat = now + UnityEngine.Random.Range(8f, 16f);
            TrySpeak(pick, 9, 5);
        }

        private static Unit PickNearbyBetrayed(Aircraft local)
        {
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            float rangeSq = EffectiveHearRange() * EffectiveHearRange();
            Vector3 myPos = local.transform.position;
            Unit best = null;
            int seen = 0;
            int pickAt = UnityEngine.Random.Range(0, 8);
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Aircraft o = all[i];
                    if (o == null || object.ReferenceEquals(o, local) || !IsGrunt(o, false))
                        continue;
                    if (!AirportDefection.IsBetrayedFaction(o))
                        continue;
                    try
                    {
                        if (o.IsLanded())
                            continue;
                    }
                    catch { }
                    float sq = (o.transform.position - myPos).sqrMagnitude;
                    if (sq > rangeSq || sq < 80f)
                        continue;
                    if (seen == pickAt)
                        return o;
                    best = o;
                    seen++;
                }
            }
            if (best != null)
                return best;
            List<Unit> units = null;
            try { units = UnitRegistry.allUnits; }
            catch { }
            if (units == null)
                return null;
            for (int i = 0; i < units.Count; i++)
            {
                GroundVehicle gv = units[i] as GroundVehicle;
                if (gv == null || !IsGruntUnit(gv, false) || !AirportDefection.IsBetrayedFaction(gv))
                    continue;
                float sq = (gv.transform.position - myPos).sqrMagnitude;
                if (sq > rangeSq)
                    continue;
                if (seen == pickAt)
                    return gv;
                best = gv;
                seen++;
            }
            return best;
        }

        private static void TickArmyIntercept(float now)
        {
            if (!AirportDefection.TraitorRadioActive)
                return;
            if (now < _nextArmyIntercept)
                return;
            Aircraft local = LocalAircraft();
            bool hostileConvoy = local != null && HasNearbyBetrayedConvoy(local, true);
            bool allyConvoy = local != null && AirportDefection.DefectHopCount >= 2
                && HasNearbyBetrayedConvoy(local, false);
            if (local == null || (!hostileConvoy && !allyConvoy))
            {
                _nextArmyIntercept = now + 3f;
                return;
            }
            if (now < _nextGlobal && !_armyFirstBurst)
                return;
            _nextArmyIntercept = now + UnityEngine.Random.Range(16f, 34f);
            bool first = _armyFirstBurst;
            _armyFirstBurst = false;
            if (hostileConvoy)
            {
                string quote = PickArmyLine(first);
                if (string.IsNullOrEmpty(quote))
                    return;
                string call = UiLang.T("[INTERCEPT] Army HQ", "【截获】陆军指挥网");
                ForceLine(call, quote, true, true, 10, 8);
                return;
            }
            string allyQuote = PickAllyCmdLine(first);
            if (string.IsNullOrEmpty(allyQuote))
                return;
            string allyCall = UiLang.T("[CMD] Army HQ", "【指挥】陆军指挥网");
            ForceLine(allyCall, allyQuote, false, false, 10, 7);
        }

        private static string PickArmyLine(bool first)
        {
            int hops = AirportDefection.DefectHopCount;
            string[] en;
            string[] cn;
            if (hops >= 2)
            {
                en = first ? ArmyAgainFirstEn : ArmyAgainNetEn;
                cn = first ? ArmyAgainFirstZh : ArmyAgainNetZh;
            }
            else if (first)
            {
                en = ArmyFirstEn;
                cn = ArmyFirstZh;
            }
            else
            {
                en = ArmyNetEn;
                cn = ArmyNetZh;
            }
            string key;
            if (hops >= 2)
                key = first ? "army.againFirst" : "army.againNet";
            else
                key = first ? "army.first" : "army.net";
            return FormatFieldLine(key, en, cn);
        }

        private static string PickAllyCmdLine(bool first)
        {
            string[] en;
            string[] cn;
            en = first ? AllyCmdAgainFirstEn : AllyCmdAgainEn;
            cn = first ? AllyCmdAgainFirstZh : AllyCmdAgainZh;
            string key = first ? "army.allyFirst" : "army.allyNet";
            return FormatFieldLine(key, en, cn);
        }

        private static string FormatFieldLine(string key, string[] en, string[] cn)
        {
            bool zh = UiLang.IsChinese;
            string field = AirportDefection.DefectFieldName;
            if (string.IsNullOrEmpty(field))
                field = zh ? "机场" : "the airfield";
            string s = AceRadioBank.PickMergedFmt(key, en, cn, field);
            if (!string.IsNullOrEmpty(s))
                return s;
            if (en == null || en.Length == 0)
                return "";
            int i = UnityEngine.Random.Range(0, en.Length);
            s = (zh && cn != null && i < cn.Length) ? cn[i] : en[i];
            return s.Replace("{0}", field);
        }

        private static string PickTraitorQuote(Unit speaker, bool ground)
        {
            int tone = AirportDefection.TraitorTone(speaker);
            string[] en = null;
            string[] cn = null;
            string key;
            if (ground)
            {
                if (tone == 3)
                {
                    en = GndBitterAllyEn;
                    cn = GndBitterAllyZh;
                    key = "gnd.bitter";
                }
                else if (tone == 2)
                {
                    en = GndRepeatHostEn;
                    cn = GndRepeatHostZh;
                    key = "gnd.repeat";
                }
                else
                {
                    en = GndTraitorEn;
                    cn = GndTraitorZh;
                    key = "gnd.traitor";
                }
            }
            else if (tone == 3)
            {
                en = BitterAllyEn;
                cn = BitterAllyZh;
                key = "air.bitter";
            }
            else if (tone == 2)
            {
                en = RepeatHostEn;
                cn = RepeatHostZh;
                key = "air.repeat";
            }
            else
            {
                en = TraitorEn;
                cn = TraitorZh;
                key = "air.traitor";
            }
            string merged = AceRadioBank.PickMerged(key, en, cn);
            if (!string.IsNullOrEmpty(merged))
                return merged;
            return PickFrom(en, cn);
        }

        private static string PickFrom(string[] en, string[] zh)
        {
            if (en == null || en.Length == 0)
                return "";
            int i = UnityEngine.Random.Range(0, en.Length);
            if (UiLang.IsChinese && zh != null && i < zh.Length)
                return zh[i];
            return en[i];
        }

        private static string PickLine(int ev, bool ground)
        {
            bool zh = UiLang.IsChinese;
            string[] en = null;
            string[] cn = null;
            if (ground)
            {
                switch (ev)
                {
                    case 1:
                        en = GndContactEn;
                        cn = GndContactZh;
                        break;
                    case 2:
                        en = GndFoxEn;
                        cn = GndFoxZh;
                        break;
                    case 3:
                        en = GndIncomingEn;
                        cn = GndIncomingZh;
                        break;
                    case 4:
                        en = GndStrafeEn;
                        cn = GndStrafeZh;
                        break;
                    case 5:
                        en = GndTrackEn;
                        cn = GndTrackZh;
                        break;
                    case 6:
                        en = GndHitEn;
                        cn = GndHitZh;
                        break;
                    case 7:
                        en = GndDownEn;
                        cn = GndDownZh;
                        break;
                    case 8:
                        en = GndAceEn;
                        cn = GndAceZh;
                        break;
                    case 9:
                        en = GndTraitorEn;
                        cn = GndTraitorZh;
                        break;
                }
            }
            else
            {
                switch (ev)
                {
                    case 1:
                        en = ContactEn;
                        cn = ContactZh;
                        break;
                    case 2:
                        en = FoxEn;
                        cn = FoxZh;
                        break;
                    case 3:
                        en = IncomingEn;
                        cn = IncomingZh;
                        break;
                    case 4:
                        en = OnSixEn;
                        cn = OnSixZh;
                        break;
                    case 5:
                        en = AdvantageEn;
                        cn = AdvantageZh;
                        break;
                    case 6:
                        en = HitEn;
                        cn = HitZh;
                        break;
                    case 7:
                        en = DownEn;
                        cn = DownZh;
                        break;
                    case 8:
                        en = AceEn;
                        cn = AceZh;
                        break;
                    case 9:
                        en = TraitorEn;
                        cn = TraitorZh;
                        break;
                    case 11:
                        en = AllyIdleEn;
                        cn = AllyIdleZh;
                        break;
                }
            }
            if (en == null || en.Length == 0)
                return AceRadioBank.PickMerged(LineKey(ev, ground), en, cn);
            string merged = AceRadioBank.PickMerged(LineKey(ev, ground), en, cn);
            if (!string.IsNullOrEmpty(merged))
                return merged;
            int i = UnityEngine.Random.Range(0, en.Length);
            if (zh && cn != null && i < cn.Length)
                return cn[i];
            return en[i];
        }

        private static string LineKey(int ev, bool ground)
        {
            if (ground)
            {
                switch (ev)
                {
                    case 1: return "gnd.contact";
                    case 2: return "gnd.fox";
                    case 3: return "gnd.incoming";
                    case 4: return "gnd.strafe";
                    case 5: return "gnd.track";
                    case 6: return "gnd.hit";
                    case 7: return "gnd.down";
                    case 8: return "gnd.ace";
                    case 9: return "gnd.traitor";
                    default: return "gnd.contact";
                }
            }
            switch (ev)
            {
                case 1: return "air.contact";
                case 2: return "air.fox";
                case 3: return "air.incoming";
                case 4: return "air.onsix";
                case 5: return "air.adv";
                case 6: return "air.hit";
                case 7: return "air.down";
                case 8: return "air.ace";
                case 9: return "air.traitor";
                case 11: return "air.idle";
                default: return "air.idle";
            }
        }

        private static string Callsign(Unit u)
        {
            int n = 1;
            try { n = (u.GetInstanceID() & 0x7fffffff) % 12 + 1; }
            catch { }
            string num = n < 10 ? "0" + n.ToString() : n.ToString();
            bool gnd = u is GroundVehicle;
            int tone = AirportDefection.TraitorTone(u);
            if (tone == 3)
            {
                if (gnd)
                    return UiLang.T("WARY-G" + num, "戒备地面-" + num);
                return UiLang.T("WARY-" + num, "戒备友军-" + num);
            }
            if (tone == 1 || tone == 2)
            {
                if (gnd)
                    return UiLang.T("EX-GND-" + num, "前友军地面-" + num);
                return UiLang.T("EX-" + num, "前友军-" + num);
            }
            if (IsHostileToLocal(u))
            {
                if (gnd)
                    return UiLang.T("GND-" + num, "地面-" + num);
                return UiLang.T("TGT-" + num, "敌机-" + num);
            }
            if (gnd)
                return UiLang.T("ALLY-G" + num, "友军地面-" + num);
            return UiLang.T("ALLY-" + num, "友军-" + num);
        }

        internal static bool UnitIsDown(Unit unit)
        {
            if (unit == null)
                return false;
            try
            {
                if (unit.disabled)
                    return true;
            }
            catch { }
            Aircraft ac = unit as Aircraft;
            if (ac == null)
                return false;
            try
            {
                if (ac.HasEjected())
                    return true;
            }
            catch { }
            return false;
        }

        private static bool IsGrunt(Aircraft ac, bool allowDead)
        {
            return IsGruntUnit(ac, allowDead);
        }

        private static bool IsGruntUnit(Unit unit, bool allowDead)
        {
            if (unit == null)
                return false;
            try
            {
                if (!allowDead && unit.disabled)
                    return false;
            }
            catch { }
            Aircraft ac = unit as Aircraft;
            if (ac != null)
            {
                try
                {
                    if (GameManager.IsLocalAircraft(ac))
                        return false;
                }
                catch { }
                try
                {
                    Player p = ac.Player;
                    if (p != null && p.SteamID != 0UL)
                        return false;
                }
                catch { }
                return true;
            }
            GroundVehicle gv = unit as GroundVehicle;
            if (gv != null)
            {
                try
                {
                    Player owner = gv.Networkowner;
                    if (owner != null)
                    {
                        if (GameManager.IsLocalPlayer(owner))
                            return false;
                        if (owner.SteamID != 0UL)
                            return false;
                    }
                }
                catch { }
                return true;
            }
            return false;
        }

        private static bool InRange(Aircraft local, Unit other)
        {
            if (local == null || other == null)
                return false;
            float range = EffectiveHearRange();
            try
            {
                return (other.transform.position - local.transform.position).sqrMagnitude
                    <= range * range;
            }
            catch { return false; }
        }

        private static float EffectiveHearRange()
        {
            float want = RangeM != null ? RangeM.Value : NearbyHearM;
            if (want > NearbyHearM)
                want = NearbyHearM;
            if (want < 1500f)
                want = 1500f;
            return want;
        }

        private static bool HasNearbyBetrayedConvoy(Aircraft local, bool hostile)
        {
            if (local == null)
                return false;
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { }
            if (all == null)
                return false;
            float rangeSq = ConvoyHearM * ConvoyHearM;
            Vector3 myPos = local.transform.position;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                GroundVehicle gv = all[i] as GroundVehicle;
                if (gv == null || !IsGruntUnit(gv, false))
                    continue;
                if (!AirportDefection.IsBetrayedFaction(gv))
                    continue;
                if (IsHostileToLocal(gv) != hostile)
                    continue;
                float sq = (gv.transform.position - myPos).sqrMagnitude;
                if (sq > rangeSq)
                    continue;
                n++;
                if (n >= ConvoyMinCount)
                    return true;
            }
            return false;
        }

        private static string PickAceRumor()
        {
            bool zh = UiLang.IsChinese;
            string squadEn;
            string squadZh;
            EnemyAceSquadron(out squadEn, out squadZh);
            string squad = zh ? squadZh : squadEn;
            string s = AceRadioBank.PickMergedFmt("air.rumor", AceRumorEn, AceRumorZh, squad);
            if (!string.IsNullOrEmpty(s))
                return s;
            int i = UnityEngine.Random.Range(0, AceRumorEn.Length);
            s = (zh && i < AceRumorZh.Length) ? AceRumorZh[i] : AceRumorEn[i];
            return s.Replace("{0}", squad);
        }

        private static void EnemyAceSquadron(out string en, out string zh)
        {
            bool localBdf = LocalFactionIsBdf();
            if (localBdf)
            {
                en = "Red Banner";
                zh = "红旗中队";
            }
            else
            {
                en = "Knight Squadron";
                zh = "骑士中队";
            }
        }

        private static bool AceRumorUnlocked()
        {
            try
            {
                return Time.timeSinceLevelLoad >= AceRumorAfterSec;
            }
            catch
            {
                return false;
            }
        }

        private static string FillAwacsSlots(string quote, Unit detected)
        {
            if (string.IsNullOrEmpty(quote))
                return quote;
            if (quote.IndexOf("{L}") >= 0)
                quote = quote.Replace("{L}", RadioMapPlace.Describe(detected));
            return quote;
        }

        private static string AwacsCallsign()
        {
            if (LocalFactionIsBdf())
                return UiLang.T("STRIL", "STRIL");
            return UiLang.T("AWACS", "AWACS");
        }

        private static string PickAwacsLine(bool ground)
        {
            if (ground)
                return PickFrom(AwacsGndEn, AwacsGndZh);
            return PickFrom(AwacsAirEn, AwacsAirZh);
        }

        private static string FillRadioSlots(string quote, Unit speaker)
        {
            if (string.IsNullOrEmpty(quote))
                return quote;
            if (quote.IndexOf("{P}") >= 0)
                quote = quote.Replace("{P}", PlayerCall());
            if (quote.IndexOf("{L}") >= 0)
            {
                Unit place = speaker;
                Aircraft local = LocalAircraft();
                if (speaker != null && IsHostileToLocal(speaker) && local != null)
                    place = local;
                else if (place == null)
                    place = local;
                quote = quote.Replace("{L}", RadioMapPlace.Describe(place));
            }
            return quote;
        }

        private static string PlayerCall()
        {
            bool zh = UiLang.IsChinese;
            Aircraft local = LocalAircraft();
            if (local == null)
                return zh ? "目标" : "the target";
            if (LocalFactionIsBdf())
                return zh ? "三条线" : "Trigger";
            return zh ? "红蝎子" : "Antares";
        }

        private static bool LocalFactionIsBdf()
        {
            Aircraft ac = LocalAircraft();
            FactionHQ hq = null;
            if (ac != null)
            {
                try { hq = ac.NetworkHQ; }
                catch { }
            }
            string n = "";
            try
            {
                if (hq != null && hq.faction != null && hq.faction.factionName != null)
                    n = hq.faction.factionName;
            }
            catch { }
            if (string.IsNullOrEmpty(n) && hq != null)
            {
                try { n = hq.name; }
                catch { }
            }
            if (string.IsNullOrEmpty(n))
                return true;
            string u = n.ToUpperInvariant();
            if (u.IndexOf("PALA") >= 0 || u.IndexOf("PRIMEVA") >= 0)
                return false;
            if (u.IndexOf("BDF") >= 0 || u.IndexOf("BOSCALI") >= 0)
                return true;
            return true;
        }

        private static bool IsHostileToLocal(Unit unit)
        {
            Aircraft local = LocalAircraft();
            if (local == null || unit == null)
                return true;
            try
            {
                FactionHQ a = local.NetworkHQ;
                FactionHQ b = unit.NetworkHQ;
                if (a != null && b != null)
                    return a != b;
            }
            catch { }
            return true;
        }

        private static bool SameSide(Unit a, Unit b)
        {
            if (a == null || b == null)
                return false;
            try
            {
                FactionHQ ha = a.NetworkHQ;
                FactionHQ hb = b.NetworkHQ;
                return ha != null && hb != null && ha == hb;
            }
            catch { return false; }
        }

        private static Aircraft LocalAircraft()
        {
            Aircraft ac = null;
            try
            {
                if (GameManager.GetLocalAircraft(out ac) && ac != null && Plugin.IsRuntimeInstance(ac))
                    return ac;
            }
            catch { }
            return Plugin.ResolveGuiAircraft();
        }

        private static bool InMission()
        {
            try
            {
                GameState gs = GameManager.gameState;
                return gs == GameState.SinglePlayer || gs == GameState.Multiplayer;
            }
            catch { return false; }
        }

        private static Unit ResolveMissileTarget(Missile missile)
        {
            if (missile == null || MissileTargetField == null)
                return null;
            try { return MissileTargetField.GetValue(missile) as Unit; }
            catch { return null; }
        }

        private static void PruneStale(float now)
        {
            PruneMap(LastSpeakAt, now, 30f);
            PruneMap(LastOnSixAt, now, 30f);
            PruneMap(LastAdvAt, now, 30f);
        }

        private static void PruneMap(Dictionary<int, float> map, float now, float keep)
        {
            if (map == null || map.Count < 48)
                return;
            List<int> drop = null;
            foreach (KeyValuePair<int, float> kv in map)
            {
                if (now - kv.Value > keep)
                {
                    if (drop == null)
                        drop = new List<int>(8);
                    drop.Add(kv.Key);
                }
            }
            if (drop == null)
                return;
            for (int i = 0; i < drop.Count; i++)
                map.Remove(drop[i]);
        }

        private static void EnsureStyles()
        {
            Font font = ChineseFontPatch.CjkFont;
            if (_callStyle == null)
            {
                _callStyle = new GUIStyle(GUI.skin.label);
                _callStyle.alignment = TextAnchor.MiddleLeft;
                _callStyle.fontStyle = FontStyle.Bold;
                _callStyle.fontSize = 13;
            }
            if (_quoteStyle == null)
            {
                _quoteStyle = new GUIStyle(GUI.skin.label);
                _quoteStyle.alignment = TextAnchor.MiddleLeft;
                _quoteStyle.fontStyle = FontStyle.Bold;
                _quoteStyle.fontSize = 18;
                _quoteStyle.wordWrap = true;
            }
            if (font != null && font != _styledFont)
            {
                _callStyle.font = font;
                _quoteStyle.font = font;
                _styledFont = font;
            }
        }

        private static readonly string[] ContactEn = new string[]
        {
            "Contact! Enemy aircraft!",
            "I've got visual!",
            "Engaging the target!",
            "Bandit on radar!"
        };
        private static readonly string[] AwacsAirEn = new string[]
        {
            "Contact, hostile aircraft, {L}.",
            "Radar contact, airborne, {L}.",
            "Hostile air at {L}.",
            "Airborne target, {L}."
        };
        private static readonly string[] AwacsAirZh = new string[]
        {
            "发现敌机，{L}。",
            "雷达接触，空中目标，{L}。",
            "发现空中目标，{L}。",
            "空中目标，{L}方向。"
        };
        private static readonly string[] AwacsGndEn = new string[]
        {
            "Contact, ground target, {L}.",
            "Hostile ground at {L}.",
            "Ground units, {L}.",
            "Surface contact, {L}."
        };
        private static readonly string[] AwacsGndZh = new string[]
        {
            "发现地面目标，{L}。",
            "地面接触，{L}。",
            "发现敌方地面单位，{L}。",
            "地面目标，{L}方向。"
        };
        private static readonly string[] ContactZh = new string[]
        {
            "发现敌机！",
            "目视确认！",
            "开始交战！",
            "雷达发现目标！"
        };
        private static readonly string[] FoxEn = new string[]
        {
            "Fox two!",
            "Missile away!",
            "I've got a lock... firing!",
            "Weapons release!"
        };
        private static readonly string[] FoxZh = new string[]
        {
            "导弹发射！",
            "导弹已出筒！",
            "锁定，发射！",
            "武器投放！"
        };
        private static readonly string[] IncomingEn = new string[]
        {
            "Missile inbound!",
            "Break! Break!",
            "He's firing on me!",
            "Incoming! Flares!"
        };
        private static readonly string[] IncomingZh = new string[]
        {
            "导弹接近！",
            "机动！机动！",
            "对方朝我开火了！",
            "来弹！干扰弹！"
        };
        private static readonly string[] OnSixEn = new string[]
        {
            "He's on my six!",
            "I can't shake him!",
            "Get him off me!",
            "Damn, he's behind me!"
        };
        private static readonly string[] OnSixZh = new string[]
        {
            "目标在我后方！",
            "甩不掉目标！",
            "目标在我六点钟，请求支援！",
            "该死，目标在我六点钟！"
        };
        private static readonly string[] AdvantageEn = new string[]
        {
            "I've got him!",
            "He's mine!",
            "You can't escape!",
            "Lined up... firing!"
        };
        private static readonly string[] AdvantageZh = new string[]
        {
            "咬住了！",
            "已咬住目标！",
            "目标跑不掉！",
            "瞄准……开火！"
        };
        private static readonly string[] HitEn = new string[]
        {
            "I've been hit!",
            "I'm hit! I'm hit!",
            "Damage! I'm trailing smoke!",
            "That one got me!"
        };
        private static readonly string[] HitZh = new string[]
        {
            "我被击中了！",
            "中弹了！中弹了！",
            "受损！在冒烟！",
            "这一发打中了！"
        };
        private static readonly string[] DownEn = new string[]
        {
            "MAYDAY! MAYDAY! MAYDAY!",
            "Eject! Eject! Eject!"
        };
        private static readonly string[] DownZh = new string[]
        {
            "弹射！弹射！弹射！",
            "MAYDAY！MAYDAY！MAYDAY！"
        };
        private static readonly string[] AceEn = new string[]
        {
            "He's too strong!",
            "We can't touch him!",
            "What is that guy?!",
            "This one's a monster!"
        };
        private static readonly string[] AceZh = new string[]
        {
            "压制不住目标！",
            "根本打不中目标！",
            "该目标机动过强！",
            "拦不住目标！"
        };

        private static readonly string[] GndContactEn = new string[]
        {
            "Enemy aircraft incoming!",
            "We've got visual on the bandit!",
            "Air raid! Air raid!",
            "Aircraft spotted!"
        };
        private static readonly string[] GndContactZh = new string[]
        {
            "发现敌机来袭！",
            "目视确认敌机！",
            "空袭警报！空袭警报！",
            "发现飞机！"
        };
        private static readonly string[] GndFoxEn = new string[]
        {
            "Fire!",
            "Launching SAMs!",
            "Engaging the aircraft!",
            "Missiles away!"
        };
        private static readonly string[] GndFoxZh = new string[]
        {
            "开火！",
            "对空导弹发射！",
            "拦截敌机！",
            "导弹出筒！"
        };
        private static readonly string[] GndIncomingEn = new string[]
        {
            "We're under attack!",
            "They're hitting the convoy!",
            "Take cover!",
            "Incoming air strike!"
        };
        private static readonly string[] GndIncomingZh = new string[]
        {
            "正在遭受空袭！",
            "车队遇袭！",
            "隐蔽！隐蔽！",
            "空中打击来了！"
        };
        private static readonly string[] GndStrafeEn = new string[]
        {
            "They're coming in low!",
            "Gun run! Break up!",
            "He's lining up on us!",
            "Get off the road!"
        };
        private static readonly string[] GndStrafeZh = new string[]
        {
            "敌机超低空来袭！",
            "要扫射了！散开！",
            "对准我们了！",
            "快离开道路！"
        };
        private static readonly string[] GndTrackEn = new string[]
        {
            "Tracking the target!",
            "I've got a lock!",
            "Don't let him get away!",
            "AAA, fire!"
        };
        private static readonly string[] GndTrackZh = new string[]
        {
            "跟踪目标！",
            "锁定空中目标！",
            "别让目标脱离！",
            "高炮，开火！"
        };
        private static readonly string[] GndHitEn = new string[]
        {
            "We've been hit!",
            "Direct hit! Direct hit!",
            "We're taking fire!",
            "Armor's been penetrated!"
        };
        private static readonly string[] GndHitZh = new string[]
        {
            "被击中了！",
            "直接命中！直接命中！",
            "火力覆盖过来了！",
            "装甲被打穿了！"
        };
        private static readonly string[] GndDownEn = new string[]
        {
            "It's no use...!",
            "We're finished!",
            "Abandon the vehicle!",
            "The column's gone!"
        };
        private static readonly string[] GndDownZh = new string[]
        {
            "撑不住了……！",
            "全完了！",
            "弃车！",
            "车队没了！"
        };
        private static readonly string[] GndAceEn = new string[]
        {
            "They're wiping out the armor!",
            "The tanks are all gone!",
            "We can't stop him!",
            "He's tearing the ground units apart!"
        };
        private static readonly string[] GndAceZh = new string[]
        {
            "装甲全被干掉了！",
            "坦克全没了！",
            "拦不住目标！",
            "地面部队被拆光了！"
        };

        private static readonly string[] TraitorEn = new string[]
        {
            "Traitor! That's one of ours!",
            "He defected! Weapons free!",
            "Don't let that traitor go!",
            "IFF flipped — shoot him down!",
            "You sold us out!",
            "Splash the defector!"
        };
        private static readonly string[] TraitorZh = new string[]
        {
            "叛徒！那是我方目标！",
            "目标投敌了！开火！",
            "不要放过叛变目标！",
            "识别码变了，打下来！",
            "叛变目标，开火！",
            "击落叛变目标！"
        };
        private static readonly string[] GndTraitorEn = new string[]
        {
            "Traitor aircraft! AAA up!",
            "That's the defector — fire!",
            "HQ, traitor in our sector!",
            "Don't let him leave the field!"
        };
        private static readonly string[] GndTraitorZh = new string[]
        {
            "叛徒飞机！防空准备！",
            "那是刚叛变的目标！打！",
            "指挥部，叛变目标在我防区！",
            "别让叛变目标离开机场！"
        };
        private static readonly string[] RepeatHostEn = new string[]
        {
            "He switched again! Kill him!",
            "Twice a traitor — weapons free!",
            "He used us and ran. Splash him!",
            "Don't let that double-dealer leave!",
            "Second time. No warning shots!"
        };
        private static readonly string[] RepeatHostZh = new string[]
        {
            "目标又叛了！这次别手软！",
            "双面货，打下来！",
            "才投过来又跑了！开火！",
            "第二次了，别让目标走！",
            "换边了就别想走！"
        };
        private static readonly string[] GndRepeatHostEn = new string[]
        {
            "Defector switched sides again! Fire!",
            "AAA, that's the two-timer!",
            "HQ, he ran back. Still a hostile!"
        };
        private static readonly string[] GndRepeatHostZh = new string[]
        {
            "叛徒又换边了！打！",
            "防空注意，那是两面派目标！",
            "指挥部，目标又跑了，仍按敌机打！"
        };
        private static readonly string[] BitterAllyEn = new string[]
        {
            "Don't stay close. He already sold us once.",
            "He's back. IFF matches. Trust does not.",
            "Command said: do not task the returnee.",
            "Watch him. Traitors switch twice.",
            "Fuel's fine. Keep distance from that airframe."
        };
        private static readonly string[] BitterAllyZh = new string[]
        {
            "别靠近归队目标，曾经叛变过。",
            "回来了？识别码对上了，人不可靠。",
            "指挥部说了，不要把任务交给归队目标。",
            "看着点，叛徒也会换边。",
            "油量正常，与归队目标保持间隔。"
        };
        private static readonly string[] GndBitterAllyEn = new string[]
        {
            "Returnee overhead. Do not salute.",
            "That's the one who left. Stay on guns.",
            "HQ: friendly IFF, untrusted pilot."
        };
        private static readonly string[] GndBitterAllyZh = new string[]
        {
            "归队目标在头顶。别敬礼。",
            "就是先前叛变的目标。炮口别松。",
            "指挥：识别是友，飞行员不可靠。"
        };
        private static readonly string[] SerialHostEn = new string[]
        {
            "Mercenary. Both flags on his log. Splash him.",
            "Third time. No warning shots.",
            "Serial defector — treat as hostile, always.",
            "He belongs to nobody. Kill the track."
        };
        private static readonly string[] SerialHostZh = new string[]
        {
            "该目标两边都待过，别信识别码。",
            "第三次了，别警告，直接打。",
            "换过边的货，永远当敌。",
            "佣兵而已，击落有赏。"
        };
        private static readonly string[] GndSerialHostEn = new string[]
        {
            "Serial defector inbound. Fire.",
            "No side. No mercy. Engage.",
            "AAA, mercenary track, weapons free!"
        };
        private static readonly string[] GndSerialHostZh = new string[]
        {
            "又换边的来了，打。",
            "哪边都不是，别留情。",
            "防空，佣兵目标，自由开火！"
        };
        private static readonly string[] SerialAllyEn = new string[]
        {
            "He's back again. Do not cover him.",
            "Serial defector. Fly your own fight.",
            "Command net: that tail number is poison.",
            "He came back. Do not cover him."
        };
        private static readonly string[] SerialAllyZh = new string[]
        {
            "又回来了。别给归队目标掩护。",
            "看着就行，别靠近。",
            "指挥网：那个机号有毒。",
            "让归队目标单独飞。"
        };
        private static readonly string[] GndSerialAllyEn = new string[]
        {
            "Friendly IFF, serial traitor. Ignore his calls.",
            "He's on our list twice. Stay cold.",
            "Do not convoy with that aircraft."
        };
        private static readonly string[] GndSerialAllyZh = new string[]
        {
            "识别是友，人是反复叛徒。别接该目标呼叫。",
            "名单上记了两次。冷处理。",
            "别跟叛变目标一起走。"
        };
        private static readonly string[] ArmyFirstEn = new string[]
        {
            "All stations: one of our aircraft has defected. Treat as hostile.",
            "Air defense, shift coverage — traitor may egress from {0}.",
            "Confirm IFF change at {0}. Defector is now an enemy track."
        };
        private static readonly string[] ArmyFirstZh = new string[]
        {
            "各单位注意，有一架我方飞机已叛变，按敌机处置。",
            "防空网转向，叛徒可能从{0}方向脱离。",
            "确认{0}识别码变更，叛变目标按敌情处理。"
        };
        private static readonly string[] ArmyNetEn = new string[]
        {
            "Third battalion, hold forward movement until air picture is clear.",
            "Artillery: check fire toward {0}, avoid hitting our own ground.",
            "Pass the defector's airframe to all batteries.",
            "Intercept has priority — keep him off the convoys.",
            "Ground watch: traitor paint/IFF has changed. New hostile.",
            "SAM battery two, stay hot on low-level approaches.",
            "Resupply column, reroute south. Air threat from former friendly.",
            "Command net: do not answer his calls. He is not ours."
        };
        private static readonly string[] ArmyNetZh = new string[]
        {
            "第三营暂停前出，先把空情摸清。",
            "炮兵注意，{0}方向暂时停火，避免误伤地面。",
            "把叛徒的机型通报给各连。",
            "拦截优先，不要让叛变目标靠近车队。",
            "地面监视：叛徒已换识别，按新敌情处理。",
            "二号防空连保持热备，注意低空接近。",
            "补给车队改走南线，注意前友军空中威胁。",
            "指挥网：不要接该目标呼叫，已经不是自己人。"
        };
        private static readonly string[] ArmyAgainFirstEn = new string[]
        {
            "All stations: the traitor defected again from {0}. Still hostile.",
            "He switched sides a second time. Weapons free on that airframe.",
            "Confirm second IFF flip at {0}. Do not answer his calls."
        };
        private static readonly string[] ArmyAgainFirstZh = new string[]
        {
            "各单位，叛徒从{0}再次换边，仍按敌机处置。",
            "第二次叛变。对该机自由开火。",
            "确认{0}识别码再次翻转，不要接该目标呼叫。"
        };
        private static readonly string[] ArmyAgainNetEn = new string[]
        {
            "Both nets have him as traitor. Do not answer.",
            "He used this army and ran. Keep batteries hot.",
            "Convoys: the two-timer is airborne. Stay dispersed.",
            "Command: former friendly, former enemy, now enemy again."
        };
        private static readonly string[] ArmyAgainNetZh = new string[]
        {
            "两边都按叛徒处置。不要接该目标呼叫。",
            "该目标用过我们又跑了。防空保持热备。",
            "车队注意，两面派在空中，拉开间距。",
            "指挥：前友军、前敌军，现在又是敌。"
        };
        private static readonly string[] ArmySerialFirstEn = new string[]
        {
            "Serial defector left {0}. Treat as mercenary hostile.",
            "Third hop. Both flags burned. Splash the track.",
            "No more IFF games. Kill confirmation on that airframe."
        };
        private static readonly string[] ArmySerialFirstZh = new string[]
        {
            "又换边的离开{0}，按敌机处置。",
            "第三次了。两边旗都烧过。击落该目标。",
            "别再看识别码。确认击落该目标。"
        };
        private static readonly string[] ArmySerialNetEn = new string[]
        {
            "He belongs to nobody. Batteries stay hot.",
            "Mercenary track. Intercept has standing priority.",
            "Do not waste a warning. Serial traitor inbound."
        };
        private static readonly string[] ArmySerialNetZh = new string[]
        {
            "该目标哪边都不是。防空保持热备。",
            "佣兵目标。拦截长期优先。",
            "别浪费警告。反复叛徒在空中。"
        };
        private static readonly string[] AllyCmdAgainFirstEn = new string[]
        {
            "Command: the defector is back. Do not give him tasking.",
            "All flights: returnee at {0} is untrusted. Hold interval.",
            "Friendly IFF restored. Trust is not. Watch that tail."
        };
        private static readonly string[] AllyCmdAgainFirstZh = new string[]
        {
            "指挥：叛变飞行员已归队。禁止单独任务。",
            "各机注意，{0}归队者不可靠，保持间隔。",
            "识别码恢复友军。信任不恢复。盯着那个机尾。"
        };
        private static readonly string[] AllyCmdAgainEn = new string[]
        {
            "Do not cover the returnee. He sold this army once.",
            "Convoy net: friendly paint, untrusted pilot. Stay cold.",
            "If he peels off, let him. We do not chase a traitor home."
        };
        private static readonly string[] AllyCmdAgainZh = new string[]
        {
            "不要掩护归队目标。曾经叛变过。",
            "车队网：涂装是友，飞行员不可靠。冷处理。",
            "归队目标脱离就让走。我们不追叛徒回家。"
        };
        private static readonly string[] AllyCmdSerialFirstEn = new string[]
        {
            "Command: serial defector re-joined at {0}. Number only, no lead.",
            "Third hop. He flies with us, not for us.",
            "All stations: that tail is poison. Do not salute."
        };
        private static readonly string[] AllyCmdSerialFirstZh = new string[]
        {
            "指挥：反复叛变者在{0}归队。只给编号，不授指挥。",
            "第三次了。该目标跟我们飞，不为我们飞。",
            "各单位：那个机尾有毒。别敬礼。"
        };
        private static readonly string[] AllyCmdSerialEn = new string[]
        {
            "Ignore his calls. Serial traitor on friendly IFF.",
            "Keep him off the column. We do not owe him cover.",
            "If the nets argue, remember: both sides already named him traitor."
        };
        private static readonly string[] AllyCmdSerialZh = new string[]
        {
            "别接该目标呼叫。反复叛徒挂着友军识别。",
            "别让归队目标靠近车队。我们不欠掩护。",
            "两边指挥网都按叛徒处置。记住这点。"
        };
        private static readonly string[] AllyIdleEn = new string[]
        {
            "Fuel's good. Holding heading.",
            "Quiet sector. Eyes open.",
            "Copy, still on station.",
            "Altitude's fine. Stay loose.",
            "Nothing on radar yet.",
            "Copy, still with you."
        };
        private static readonly string[] AllyIdleZh = new string[]
        {
            "油量正常，航向保持。",
            "本空域暂无目标，保持警戒。",
            "收到，继续值守。",
            "高度正常。",
            "雷达暂无目标。",
            "收到，保持航向。"
        };
        private static readonly string[] AceRumorEn = new string[]
        {
            "Rumor is {0} might be coming in. Stay sharp.",
            "Command chatter — {0} could spin up today.",
            "If {0} shows, we abort the attack.",
            "Stay sharp. {0} has been seen on this front.",
            "Don't get cocky. {0} might be inbound."
        };
        private static readonly string[] AceRumorZh = new string[]
        {
            "指挥通报，{0}可能进入本空域，保持警戒。",
            "各机注意，{0}可能转场进入。",
            "如{0}进入，终止攻击，脱离接触。",
            "保持警戒，本方向有{0}活动迹象。",
            "不要大意，{0}可能接近。"
        };
    }

    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class Patch_AceRadio_StartMissile
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            AceRadioChatter.NotifyMissile(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "UnitDisabled")]
    internal static class Patch_AceRadio_AircraftDown
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance, bool oldState, bool newState)
        {
            if (__instance == null || !newState || oldState)
                return;
            AceRadioChatter.NotifyDown(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "ApplyDamage")]
    internal static class Patch_AceRadio_ApplyDamage
    {
        [HarmonyPostfix]
        private static void Postfix(UnitPart __instance, float netPierceDamage, float netBlastDamage,
            float netFireDamage, float netImpactDamage)
        {
            if (!AceRadioChatter.IsOn() || __instance == null)
                return;
            float dmg = netPierceDamage + netBlastDamage + netFireDamage + netImpactDamage;
            if (dmg < 6f)
                return;
            Unit u = __instance.parentUnit;
            if (!(u is Aircraft) && !(u is GroundVehicle))
                return;
            if (AceRadioChatter.UnitIsDown(u))
                AceRadioChatter.NotifyDown(u);
            else
                AceRadioChatter.NotifyHit(u, dmg);
        }
    }

    [HarmonyPatch(typeof(GroundVehicle), "UnitDisabled")]
    internal static class Patch_AceRadio_GroundDown
    {
        [HarmonyPostfix]
        private static void Postfix(GroundVehicle __instance, bool oldState, bool newState)
        {
            if (__instance == null || !newState || oldState)
                return;
            AceRadioChatter.NotifyDown(__instance);
        }
    }
}
