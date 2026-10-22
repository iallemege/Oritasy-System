using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Per-unit armor fatigue: repeated hits (especially near-thickness AP)
    /// lower effective grade so later rounds are more likely to pen.
    /// Bounce / glance deal no damage until fatigue drops the plate enough to pen.
    /// </summary>
    internal static class ArmorFatigueService
    {
        private class St
        {
            public float Fatigue;
            public float FatigueStamp;
            public float LastHit;
            public int Combo;
        }

        private static readonly Dictionary<int, St> ById = new Dictionary<int, St>(64);
        private const float DecayDelay = 5.5f;
        private const float DecayPerSec = 0.018f;
        private const float ComboWindow = 2.8f;
        private const float MaxFat = 1f;

        internal static float Get(Unit u)
        {
            if (u == null)
                return 0f;
            return GetId(u.GetInstanceID());
        }

        internal static float GetId(int id)
        {
            St st;
            if (!ById.TryGetValue(id, out st) || st == null)
                return 0f;
            Decay(st);
            return st.Fatigue;
        }

        internal static int EffectiveArmor(int armorGrade, float fatigue)
        {
            return EffectiveArmor(armorGrade, fatigue, 3);
        }

        internal static int EffectiveArmor(int armorGrade, float fatigue, int maxDrop)
        {
            if (maxDrop < 0)
                maxDrop = 0;
            int drop = Mathf.RoundToInt(fatigue * maxDrop);
            if (drop < 0)
                drop = 0;
            if (drop > maxDrop)
                drop = maxDrop;
            int g = armorGrade - drop;
            if (g < 0)
                g = 0;
            return g;
        }

        internal static void NoteHit(Unit u, int hit, int pen, int armor)
        {
            if (u == null)
                return;
            NoteHitId(u.GetInstanceID(), hit, pen, armor);
        }

        internal static void NoteHitId(int id, int hit, int pen, int armor)
        {
            St st;
            if (!ById.TryGetValue(id, out st) || st == null)
            {
                st = new St();
                ById[id] = st;
            }
            Decay(st);
            float now = Time.time;
            if (now - st.LastHit <= ComboWindow)
                st.Combo++;
            else
                st.Combo = 1;
            if (st.Combo > 12)
                st.Combo = 12;
            st.LastHit = now;

            float add = 0.04f;
            if (hit == ModularArmorMathService.HitGlance)
                add = 0.09f;
            else if (hit == ModularArmorMathService.HitPen)
                add = 0.16f;
            else if (hit == ModularArmorMathService.HitOvermatch)
                add = 0.07f;
            int gap = pen - armor;
            if (gap < 0)
                gap = -gap;
            if (gap <= 1)
                add *= 1.7f;
            else if (gap <= 2)
                add *= 1.25f;
            add *= 1f + (st.Combo - 1) * 0.12f;
            st.Fatigue += add;
            if (st.Fatigue > MaxFat)
                st.Fatigue = MaxFat;
            st.FatigueStamp = st.Fatigue;
        }

        internal static void EnsurePartialDamage(
            int hit, float listedPierce, ref float pierce, ref float blast, ref float impact)
        {
        }

        internal static float InfantryBouncePass()
        {
            return 0.05f;
        }

        internal static float InfantryGlancePass()
        {
            return 0.16f;
        }

        private static void Decay(St st)
        {
            if (st == null)
                return;
            float idle = Time.time - st.LastHit;
            if (idle <= DecayDelay)
            {
                st.Fatigue = st.FatigueStamp;
                return;
            }
            st.Fatigue = st.FatigueStamp - (idle - DecayDelay) * DecayPerSec;
            if (st.Fatigue < 0f)
                st.Fatigue = 0f;
            if (idle > ComboWindow + 1f)
                st.Combo = 0;
        }
    }
}
