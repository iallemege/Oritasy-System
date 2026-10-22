using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Two-seat airframes keep a visible copilot / WSO mesh, and that seat
    /// talks on the radio: boredom, nearby contacts, Veyrn hangar gossip.
    /// </summary>
    internal static class CopilotCrewService
    {
        private const string VisName = "Oritasy_CopilotVis";
        private const float VisInterval = 3.5f;
        private const float LocalRadioMin = 14f;
        private const float LocalRadioMax = 38f;
        private const float NearbyRadioMin = 28f;
        private const float NearbyRadioMax = 55f;
        private const float WarnRangeM = 11000f;

        private static readonly FieldInfo MeshField =
            AccessTools.Field(typeof(Pilot), "skinnedMeshRenderer");
        private static readonly HashSet<int> ClonedPilotIds = new HashSet<int>();

        private static float _nextVis;
        private static float _nextLocalRadio;
        private static float _nextNearbyRadio;
        private static float _nextPrune;
        private static int _lastTopic = -1;
        private static readonly Dictionary<int, int> NameByAc = new Dictionary<int, int>();

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= _nextVis)
            {
                _nextVis = now + VisInterval;
                ScanVisuals();
            }
            if (now >= _nextPrune)
            {
                _nextPrune = now + 30f;
                if (ClonedPilotIds.Count > 96)
                    ClonedPilotIds.Clear();
                if (NameByAc.Count > 64)
                    NameByAc.Clear();
            }
            TickRadio(now);
        }

        internal static void EnsureAircraft(Aircraft ac)
        {
            if (ac == null || !Plugin.IsRuntimeInstance(ac))
                return;
            Pilot[] pilots = ac.pilots;
            if (pilots == null || pilots.Length < 2)
                pilots = ac.GetComponentsInChildren<Pilot>(true);
            if (pilots == null || pilots.Length < 2)
                return;
            Pilot donor = FindDonor(pilots);
            if (donor == null)
                return;
            for (int i = 0; i < pilots.Length; i++)
            {
                Pilot p = pilots[i];
                if (p == null || object.ReferenceEquals(p, donor))
                    continue;
                if (p.dead || p.ejected)
                    continue;
                ShowPilot(p);
                if (HasVisibleMesh(p))
                    continue;
                CloneVisual(donor, p);
                ShowPilot(p);
            }
        }

        internal static bool IsCopilot(Pilot p)
        {
            if (p == null || p.dead || p.ejected)
                return false;
            if (p.playerControlled)
                return false;
            Aircraft ac = p.aircraft;
            if (ac == null)
            {
                try { ac = p.GetComponentInParent<Aircraft>(); }
                catch { ac = null; }
            }
            if (ac == null || ac.pilots == null || ac.pilots.Length < 2)
                return NameLooksCopilot(p);
            Pilot first = ac.pilots[0];
            if (first != null && object.ReferenceEquals(first, p))
                return NameLooksCopilot(p);
            return true;
        }

        internal static void ShowPilot(Pilot p)
        {
            if (p == null)
                return;
            try { p.TogglePilotVisibility(true); }
            catch { }
            SkinnedMeshRenderer assigned = AssignedMesh(p);
            if (assigned != null)
            {
                assigned.enabled = true;
                if (assigned.gameObject != null && !assigned.gameObject.activeSelf)
                    assigned.gameObject.SetActive(true);
            }
            SkinnedMeshRenderer[] skins = null;
            try { skins = p.GetComponentsInChildren<SkinnedMeshRenderer>(true); }
            catch { skins = null; }
            if (skins != null)
            {
                for (int i = 0; i < skins.Length; i++)
                {
                    SkinnedMeshRenderer s = skins[i];
                    if (s == null)
                        continue;
                    s.enabled = true;
                    if (s.gameObject != null && !s.gameObject.activeSelf)
                        s.gameObject.SetActive(true);
                }
            }
            SetRenderersVisibleLayer(p);
        }

        private static void SetRenderersVisibleLayer(Pilot p)
        {
            if (p == null)
                return;
            Renderer[] rs = null;
            try { rs = p.GetComponentsInChildren<Renderer>(true); }
            catch { return; }
            if (rs == null)
                return;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] != null && rs[i].gameObject != null)
                    rs[i].gameObject.layer = 0;
            }
        }

        private static void ScanVisuals()
        {
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            if (all == null)
                return;
            for (int i = 0; i < all.Count; i++)
                EnsureAircraft(all[i]);
        }

        private static Pilot FindDonor(Pilot[] pilots)
        {
            Pilot fallback = null;
            for (int i = 0; i < pilots.Length; i++)
            {
                Pilot p = pilots[i];
                if (p == null || p.dead || p.ejected)
                    continue;
                if (fallback == null)
                    fallback = p;
                if (HasVisibleMesh(p) || AssignedMesh(p) != null)
                    return p;
            }
            return fallback;
        }

        private static bool NameLooksCopilot(Pilot p)
        {
            if (p == null)
                return false;
            string n = p.name;
            if (string.IsNullOrEmpty(n) && p.gameObject != null)
                n = p.gameObject.name;
            if (string.IsNullOrEmpty(n))
                return false;
            return n.IndexOf("WSO", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Jester", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("copilot", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("CoPilot", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("RIO", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static SkinnedMeshRenderer AssignedMesh(Pilot p)
        {
            if (p == null || MeshField == null)
                return null;
            try { return MeshField.GetValue(p) as SkinnedMeshRenderer; }
            catch { return null; }
        }

        private static bool HasVisibleMesh(Pilot p)
        {
            if (p == null)
                return false;
            SkinnedMeshRenderer[] skins = null;
            try { skins = p.GetComponentsInChildren<SkinnedMeshRenderer>(true); }
            catch { return false; }
            if (skins == null)
                return false;
            for (int i = 0; i < skins.Length; i++)
            {
                SkinnedMeshRenderer s = skins[i];
                if (s == null || s.sharedMesh == null)
                    continue;
                if (s.gameObject != null && s.gameObject.name != null
                    && s.gameObject.name.IndexOf(VisName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (s.enabled)
                    return true;
            }
            return false;
        }

        private static Transform FindVisualRoot(Pilot donor)
        {
            if (donor == null || donor.transform == null)
                return null;
            Transform t = donor.transform;
            if (t.childCount <= 0)
            {
                if (donor.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                    return t;
                return null;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c == null)
                    continue;
                if (c.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                    return c;
            }
            if (donor.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                return t;
            return null;
        }

        private static void CloneVisual(Pilot donor, Pilot dest)
        {
            if (donor == null || dest == null || dest.transform == null)
                return;
            int id = dest.GetInstanceID();
            if (ClonedPilotIds.Contains(id))
                return;
            if (dest.transform.Find(VisName) != null)
            {
                ClonedPilotIds.Add(id);
                return;
            }
            Transform root = FindVisualRoot(donor);
            if (root == null)
                return;
            GameObject clone = null;
            try { clone = UnityEngine.Object.Instantiate(root.gameObject, dest.transform, false); }
            catch { clone = null; }
            if (clone == null)
                return;
            clone.name = VisName;
            clone.SetActive(true);
            clone.transform.localPosition = root.localPosition;
            clone.transform.localRotation = root.localRotation;
            clone.transform.localScale = root.localScale;
            StripGameplay(clone);
            EnableRenderers(clone);
            ClonedPilotIds.Add(id);
        }

        private static void StripGameplay(GameObject go)
        {
            if (go == null)
                return;
            Pilot[] extras = go.GetComponentsInChildren<Pilot>(true);
            if (extras != null)
            {
                for (int i = 0; i < extras.Length; i++)
                {
                    if (extras[i] != null)
                        UnityEngine.Object.Destroy(extras[i]);
                }
            }
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            if (cols == null)
                return;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }
        }

        private static void EnableRenderers(GameObject go)
        {
            if (go == null)
                return;
            SkinnedMeshRenderer[] skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins != null)
            {
                for (int i = 0; i < skins.Length; i++)
                {
                    if (skins[i] == null)
                        continue;
                    skins[i].enabled = true;
                    skins[i].updateWhenOffscreen = true;
                    if (skins[i].gameObject != null)
                        skins[i].gameObject.SetActive(true);
                }
            }
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs == null)
                return;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] != null)
                    rs[i].enabled = true;
            }
        }

        private static void TickRadio(float now)
        {
            Aircraft local = LocalAircraft();
            if (local == null)
                return;
            if (now >= _nextLocalRadio)
            {
                if (HasCopilot(local) && !local.disabled)
                {
                    _nextLocalRadio = now + UnityEngine.Random.Range(LocalRadioMin, LocalRadioMax);
                    if (UnityEngine.Random.value > 0.28f)
                        TrySpeak(local, true);
                }
                else
                    _nextLocalRadio = now + 8f;
            }
            if (now < _nextNearbyRadio)
                return;
            _nextNearbyRadio = now + UnityEngine.Random.Range(NearbyRadioMin, NearbyRadioMax);
            if (UnityEngine.Random.value < 0.45f)
                return;
            Aircraft other = PickNearbyTwoSeat(local);
            if (other != null)
                TrySpeak(other, false);
        }

        internal static string BuddyCallsign(Aircraft ac)
        {
            return "Jester";
        }

        internal static bool HasCopilot(Aircraft ac)
        {
            if (ac == null || ac.pilots == null)
                return false;
            int n = 0;
            for (int i = 0; i < ac.pilots.Length; i++)
            {
                Pilot p = ac.pilots[i];
                if (p == null || p.dead || p.ejected)
                    continue;
                n++;
            }
            return n >= 2;
        }

        private static Aircraft PickNearbyTwoSeat(Aircraft local)
        {
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            if (all == null)
                return null;
            Vector3 myPos = local.transform.position;
            float maxSq = 7000f * 7000f;
            Aircraft best = null;
            int seen = 0;
            int pickAt = UnityEngine.Random.Range(0, 6);
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft o = all[i];
                if (o == null || object.ReferenceEquals(o, local) || o.disabled)
                    continue;
                if (!HasCopilot(o))
                    continue;
                if (!SameSide(local, o))
                    continue;
                float sq = (o.transform.position - myPos).sqrMagnitude;
                if (sq > maxSq || sq < 90f)
                    continue;
                if (seen == pickAt)
                    return o;
                best = o;
                seen++;
            }
            return best;
        }

        private static bool SameSide(Aircraft a, Aircraft b)
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

        private static void TrySpeak(Aircraft ac, bool localCrew)
        {
            Unit threat = FindNearbyThreat(ac);
            int topic;
            if (threat != null && UnityEngine.Random.value < 0.55f)
                topic = 0;
            else if (UnityEngine.Random.value < 0.42f)
                topic = 1;
            else
                topic = 2;
            if (topic == _lastTopic && UnityEngine.Random.value < 0.55f)
                topic = (topic + 1 + UnityEngine.Random.Range(0, 2)) % 3;
            _lastTopic = topic;
            string quote;
            int pri;
            if (topic == 0 && threat != null)
            {
                quote = PickWarn(DescribeThreat(threat));
                pri = 6;
            }
            else if (topic == 1)
            {
                quote = PickVeyrn();
                pri = 2;
            }
            else
            {
                quote = PickBored();
                pri = 2;
            }
            if (string.IsNullOrEmpty(quote))
                return;
            AceRadioChatter.TryQueueCrew(BuddyCallsign(ac), quote, pri);
        }

        private static string CrewCallsign(Aircraft ac, bool localCrew)
        {
            return BuddyCallsign(ac);
        }

        private static Unit FindNearbyThreat(Aircraft ac)
        {
            if (ac == null)
                return null;
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { }
            if (all == null)
                return null;
            Vector3 pos = ac.transform.position;
            float maxSq = WarnRangeM * WarnRangeM;
            Unit best = null;
            float bestSq = maxSq;
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i];
                if (u == null || object.ReferenceEquals(u, ac))
                    continue;
                try
                {
                    if (u.disabled)
                        continue;
                }
                catch { }
                if (!IsHostile(ac, u))
                    continue;
                if (!(u is Aircraft) && !(u is GroundVehicle) && !(u is Ship) && !(u is Building))
                    continue;
                float sq = (u.transform.position - pos).sqrMagnitude;
                if (sq >= bestSq)
                    continue;
                bestSq = sq;
                best = u;
            }
            return best;
        }

        private static bool IsHostile(Aircraft from, Unit other)
        {
            if (from == null || other == null)
                return false;
            try
            {
                FactionHQ a = from.NetworkHQ;
                FactionHQ b = other.NetworkHQ;
                if (a == null || b == null || a == b)
                    return false;
                return true;
            }
            catch { return false; }
        }

        private static string DescribeThreat(Unit u)
        {
            if (u is Aircraft)
            {
                if (UnityEngine.Random.value < 0.45f)
                    return UiLang.IsChinese ? "那架" : "that one";
                return UiLang.IsChinese ? "有人" : "someone";
            }
            if (u is Ship)
                return UiLang.IsChinese ? "水上那个" : "that fat thing on the water";
            if (u is Building)
                return UiLang.IsChinese ? "那边" : "over there";
            if (u is GroundVehicle)
                return UiLang.IsChinese ? "地上" : "on the road";
            return UiLang.IsChinese ? "有东西" : "something";
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

        private static string PickBored()
        {
            return Pick(UiLang.IsChinese ? BoredZh : BoredEn);
        }

        private static string PickVeyrn()
        {
            return Pick(UiLang.IsChinese ? VeyrnZh : VeyrnEn);
        }

        private static string PickWarn(string who)
        {
            string s = Pick(UiLang.IsChinese ? WarnZh : WarnEn);
            if (s == null)
                return s;
            if (who == null)
                who = "";
            return s.Replace("{0}", who);
        }

        private static string Pick(string[] arr)
        {
            if (arr == null || arr.Length == 0)
                return "";
            return arr[UnityEngine.Random.Range(0, arr.Length)];
        }

        private static readonly string[] BuddyEn = new string[]
        {
            "Rami", "Jen", "Nico", "Pavel", "Asha"
        };

        private static readonly string[] BuddyZh = new string[]
        {
            "老周", "阿宁", "小陈", "凯", "阿莎"
        };

        private static readonly string[] BoredEn = new string[]
        {
            "you good?",
            "mm.",
            "I need coffee.",
            "if you stall this I'm walking.",
            "hey. you still with me.",
            "quiet. I hate quiet.",
            "don't fall asleep.",
            "yeah I'm still back here.",
            "this is the part where nothing happens.",
            "tap the stick so I know you're alive."
        };

        private static readonly string[] BoredZh = new string[]
        {
            "你还醒着吗。",
            "嗯。",
            "我想喝咖啡。",
            "你要是失速我就跳下去。",
            "喂。你还在吗。",
            "太安静了。烦。",
            "别睡。",
            "我还在后面。",
            "又是这种什么都不发生的时候。",
            "动一下杆，让我知道你没死。"
        };

        private static readonly string[] WarnEn = new string[]
        {
            "left— wait. yeah.",
            "don't go that way.",
            "I see {0}. don't look yet.",
            "hey. hey. two o'clock.",
            "uh. {0}.",
            "stop. {0}.",
            "I don't like that.",
            "we're not alone.",
            "cut left. trust me.",
            "{0}. I said it. that's all."
        };

        private static readonly string[] WarnZh = new string[]
        {
            "左边——等下。对。",
            "别往那边飞。",
            "我看见{0}了。先别抬头。",
            "喂。喂。两点钟。",
            "呃。{0}。",
            "停。{0}。",
            "这个角度我不喜欢。",
            "不只有我们。",
            "往左。听我的。",
            "{0}。我说了。就这样。"
        };

        private static readonly string[] VeyrnEn = new string[]
        {
            "maintenance was swearing about northern firmware again.",
            "same crate, two flags. I saw the stencil.",
            "don't put that G-box on my side.",
            "Veyrn. yeah. it works until it doesn't.",
            "they changed a seeker and told nobody. again.",
            "I wouldn't fly that missile if I knew the lot number.",
            "hangar guys won't look at the yellow container. that's all I'm saying.",
            "bought from the same guy PALA bought from. whatever.",
            "if it says trainer on the crate, it isn't.",
            "don't ask me about Veyrn. I already regret asking."
        };

        private static readonly string[] VeyrnZh = new string[]
        {
            "机务又在骂北方固件。",
            "同一箱货，两面旗。喷漆我见过。",
            "那个 G 箱子别挂我这边。",
            "Veyrn。嗯。能用，直到不能用。",
            "导引头改了，谁也没说。又来。",
            "我要是知道批次号就不挂那导弹。",
            "机库那帮人连黄箱子都不看。我就说这句。",
            "货源跟 PALA 一样。随便吧。",
            "箱子上写教练的，都不是教练。",
            "别问我 Veyrn。我问过一次就后悔。"
        };
    }

    [HarmonyPatch(typeof(Pilot), "TogglePilotVisibility")]
    internal static class Patch_Copilot_ToggleVisibility
    {
        [HarmonyPostfix]
        private static void Postfix(Pilot __instance, bool enabled)
        {
            if (enabled || __instance == null)
                return;
            if (!CopilotCrewService.IsCopilot(__instance))
                return;
            CopilotCrewService.ShowPilot(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "Awake")]
    internal static class Patch_Copilot_PilotAwake
    {
        [HarmonyPostfix]
        private static void Postfix(Pilot __instance)
        {
            if (__instance == null)
                return;
            Aircraft ac = __instance.aircraft;
            if (ac == null)
            {
                try { ac = __instance.GetComponentInParent<Aircraft>(); }
                catch { }
            }
            CopilotCrewService.EnsureAircraft(ac);
        }
    }
}
