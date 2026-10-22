using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Tiny MLP for local infantry AI (advance / hold / flank / retreat / hold-fire).
    /// Imitation weights of the old rule brain; samples with temperature when the
    /// aircraft NN toggle is on. Execution stays in InfantryAiPawn.Think.
    /// </summary>
    internal static class InfantryAiNnService
    {
        internal const int In = 8;
        internal const int Hidden = 10;
        internal const int NAct = 5;

        internal const int ActAdvance = 0;
        internal const int ActHold = 1;
        internal const int ActFlank = 2;
        internal const int ActRetreat = 3;
        internal const int ActHoldFire = 4;

        private static bool _ready;
        private static float[] _w1;
        private static float[] _b1;
        private static float[] _w2;
        private static float[] _b2;
        private static readonly float[] Feat = new float[In];
        private static readonly float[] Hid = new float[Hidden];
        private static readonly float[] Logit = new float[NAct];
        private static readonly float[] Prob = new float[NAct];
        private static readonly bool[] Mask = new bool[NAct];

        internal static void Ensure()
        {
            if (_ready)
                return;
            _w1 = new float[Hidden * In];
            _b1 = new float[Hidden];
            _w2 = new float[NAct * Hidden];
            _b2 = new float[NAct];
            InitImitation();
            _ready = true;
        }

        internal static int RuleAction(float dist, float hp, bool air)
        {
            if (hp < 22f && dist < 45f)
                return ActRetreat;
            if (hp < 18f && dist > 55f)
                return ActHoldFire;
            if (dist > 38f)
                return ActAdvance;
            if (dist < 16f)
                return ActRetreat;
            if (air && dist > 90f)
                return ActHold;
            return ActHold;
        }

        internal static int Decide(
            float dist, float hp, bool air, bool friendly, bool underFire, double roll)
        {
            Ensure();
            int rule = RuleAction(dist, hp, air);
            if (!AiCombatNnService.IsEnabled())
                return rule;

            Feat[0] = Clamp01(dist / 400f);
            Feat[1] = Clamp01(hp / 80f);
            Feat[2] = air ? 1f : 0f;
            Feat[3] = friendly ? 1f : 0f;
            Feat[4] = underFire ? 1f : 0f;
            Feat[5] = dist < 20f ? 1f : 0f;
            Feat[6] = dist > 40f ? 1f : 0f;
            Feat[7] = hp < 28f ? 1f : 0f;
            Forward();

            for (int i = 0; i < NAct; i++)
                Mask[i] = true;
            if (hp > 40f)
                Mask[ActHoldFire] = false;
            if (dist < 10f)
                Mask[ActAdvance] = false;

            return Sample(rule, 0.42f, 0.62f, roll);
        }

        internal static bool WantsFire(int act, float dist)
        {
            if (act == ActHoldFire)
                return false;
            if (act == ActRetreat)
                return dist < 90f;
            return dist < 420f;
        }

        private static void Forward()
        {
            int h, i;
            for (h = 0; h < Hidden; h++)
            {
                float acc = _b1[h];
                int row = h * In;
                for (i = 0; i < In; i++)
                    acc += _w1[row + i] * Feat[i];
                Hid[h] = acc > 0f ? acc : 0f;
            }
            for (h = 0; h < NAct; h++)
            {
                float acc = _b2[h];
                int row = h * Hidden;
                for (i = 0; i < Hidden; i++)
                    acc += _w2[row + i] * Hid[i];
                Logit[h] = acc;
            }
        }

        private static int Sample(int rule, float temp, float blend, double roll)
        {
            float tmax = Logit[0];
            for (int i = 1; i < NAct; i++)
            {
                if (Mask[i] && Logit[i] > tmax)
                    tmax = Logit[i];
            }
            float t = temp < 0.08f ? 0.08f : temp;
            float sum = 0f;
            for (int i = 0; i < NAct; i++)
            {
                if (!Mask[i])
                {
                    Prob[i] = 0f;
                    continue;
                }
                float e = Mathf.Exp((Logit[i] - tmax) / t);
                Prob[i] = e;
                sum += e;
            }
            if (sum < 1e-8f)
                return rule;
            for (int i = 0; i < NAct; i++)
                Prob[i] /= sum;
            if (blend < 0.999f && rule >= 0 && rule < NAct && Mask[rule])
            {
                for (int i = 0; i < NAct; i++)
                    Prob[i] *= blend;
                Prob[rule] += 1f - blend;
                sum = 0f;
                for (int i = 0; i < NAct; i++)
                    sum += Prob[i];
                if (sum > 0f)
                {
                    for (int i = 0; i < NAct; i++)
                        Prob[i] /= sum;
                }
            }
            double r = roll;
            if (r < 0.0) r = 0.0;
            if (r > 0.999999) r = 0.999999;
            double acc = 0.0;
            for (int i = 0; i < NAct; i++)
            {
                acc += Prob[i];
                if (r <= acc)
                    return i;
            }
            return rule;
        }

        private static void InitImitation()
        {
            SetW1(0, 6, 3.4f);
            _b1[0] = -0.4f;
            SetW1(1, 5, 3.6f);
            _b1[1] = -0.3f;
            SetW1(2, 7, 3.2f);
            SetW1(2, 1, -1.8f);
            _b1[2] = 0.2f;
            SetW1(3, 0, -2.2f);
            SetW1(3, 5, -2.0f);
            SetW1(3, 6, -2.0f);
            _b1[3] = 1.6f;
            SetW1(4, 2, 2.8f);
            SetW1(5, 4, 2.4f);
            SetW1(6, 1, 2.2f);
            _b1[6] = -0.6f;
            SetW1(7, 3, 1.4f);
            SetW1(8, 0, 1.8f);
            SetW1(9, 7, 1.6f);
            SetW1(9, 5, 1.2f);

            SetW2(ActAdvance, 0, 2.8f);
            SetW2(ActAdvance, 8, 1.1f);
            _b2[ActAdvance] = 0.15f;
            SetW2(ActHold, 3, 2.6f);
            SetW2(ActHold, 4, 0.8f);
            SetW2(ActHold, 6, 0.7f);
            _b2[ActHold] = 0.35f;
            SetW2(ActFlank, 3, 1.6f);
            SetW2(ActFlank, 5, 0.9f);
            _b2[ActFlank] = -0.05f;
            SetW2(ActRetreat, 1, 2.8f);
            SetW2(ActRetreat, 2, 2.4f);
            SetW2(ActRetreat, 9, 1.3f);
            _b2[ActRetreat] = -0.1f;
            SetW2(ActHoldFire, 2, 2.2f);
            SetW2(ActHoldFire, 0, 0.8f);
            _b2[ActHoldFire] = -0.8f;
        }

        private static void SetW1(int h, int i, float v)
        {
            _w1[h * In + i] = v;
        }

        private static void SetW2(int a, int h, float v)
        {
            _w2[a * Hidden + h] = v;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
