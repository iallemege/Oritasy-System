using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Tiny MLP that allocates combat Mode + ACM script. Execution stays in AiCombatBrain.
    /// Weights: plugins/OritasyNn/ai_actions.weights, else embedded imitation of the rule brain.
    /// </summary>
    internal static class AiCombatNnService
    {
        internal const string PrefEnabledKey = "Oritasy.AiNn.Enabled";

        internal const int In = 14;
        internal const int Hidden = 24;
        internal const int NMode = 6;
        internal const int NAcm = 9;

        internal const int ModeHunt = 0;
        internal const int ModeAttack = 1;
        internal const int ModeBreakOff = 2;
        internal const int ModeEvade = 3;
        internal const int ModeCruise = 4;
        internal const int ModeAcm = 5;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _temperature;
        private static ConfigEntry<float> _blend;
        private static ConfigEntry<bool> _log;

        private static bool _ready;
        private static bool _loadAttempted;
        private static string _source = "none";

        private static float[] _w1;
        private static float[] _b1;
        private static float[] _wm;
        private static float[] _bm;
        private static float[] _wa;
        private static float[] _ba;

        private static readonly float[] Feat = new float[In];
        private static readonly float[] Hid = new float[Hidden];
        private static readonly float[] LogitM = new float[NMode];
        private static readonly float[] LogitA = new float[NAcm];
        private static readonly float[] Prob = new float[16];
        private static readonly bool[] Mask = new bool[16];

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("AiNn", "Enabled", true,
                "Neural net allocates AI Mode + ACM. Safety (missile evade / no-target cruise) stays rule-based.");
            _temperature = config.Bind("AiNn", "Temperature", 0.35f,
                "Softmax temperature. Lower = closer to greedy imitation of the rule brain.");
            _blend = config.Bind("AiNn", "Blend", 0.70f,
                "0 = always rule pick, 1 = always network. Mixes NN probs with the rule one-hot.");
            _log = config.Bind("AiNn", "LogDecisions", false,
                "Append Mode/ACM picks to BepInEx/plugins/OritasyNn/picks.csv");

            bool en = _enabled.Value;
            try
            {
                if (PlayerPrefs.HasKey(PrefEnabledKey))
                    en = PlayerPrefs.GetInt(PrefEnabledKey, en ? 1 : 0) != 0;
            }
            catch { }
            ApplyEnabled(en, false);
        }

        internal static bool IsEnabled()
        {
            return _enabled != null && _enabled.Value;
        }

        internal static void SetEnabled(bool on)
        {
            ApplyEnabled(on, true);
        }

        private static void ApplyEnabled(bool on, bool savePrefs)
        {
            if (_enabled != null)
                _enabled.Value = on;
            if (savePrefs)
            {
                try
                {
                    PlayerPrefs.SetInt(PrefEnabledKey, on ? 1 : 0);
                    PlayerPrefs.Save();
                }
                catch { }
            }
        }

        internal static bool IsLive()
        {
            if (!IsEnabled())
                return false;
            EnsureLoaded();
            return _ready;
        }

        internal static string SourceLabel()
        {
            EnsureLoaded();
            return _source;
        }

        internal static void DrawProfileToggle()
        {
            bool on = IsEnabled();
            bool want = GUILayout.Toggle(on,
                UiLang.T(" Neural action allocator (Mode + ACM)", " 神经网络分配行动（模式 + 格斗机动）"));
            if (want != on)
                SetEnabled(want);
            if (!want)
            {
                GUILayout.Label(UiLang.T(
                    "OFF: rule brain picks Hunt/Attack/Break/ACM.",
                    "关：仍用规则大脑选 Hunt/Attack/脱离/格斗。"),
                    GUILayout.ExpandWidth(true));
                return;
            }
            EnsureLoaded();
            if (!_ready)
            {
                GUILayout.Label(UiLang.T(
                    "Weights missing — falling back to rules.",
                    "权重未加载，回退规则大脑。"),
                    GUILayout.ExpandWidth(true));
                return;
            }
            GUILayout.Label(UiLang.T(
                "ON: MLP assigns actions; evade / cruise stay rules.",
                "开：MLP 分配行动；规避/巡航仍走规则。")
                + " [" + _source + "]",
                GUILayout.ExpandWidth(true));
        }

        internal static void Fill(
            float[] x,
            float dist,
            float angle,
            float aspect,
            float mySpd,
            float tgtSpd,
            float corner,
            float ralt,
            float dAlt,
            float skill,
            float acmChance,
            bool canShoot,
            bool shouldBreak,
            bool threat,
            float breakDist)
        {
            float c = corner > 1f ? corner : 1f;
            x[0] = Clamp01(dist / 20000f);
            x[1] = Clamp01(angle / 180f);
            x[2] = Clamp01(aspect / 180f);
            x[3] = Clamp01(Clamp(mySpd / c, 0f, 2f) * 0.5f);
            x[4] = Clamp01(Clamp(tgtSpd / c, 0f, 2f) * 0.5f);
            x[5] = Clamp01(ralt / 8000f);
            x[6] = Clamp01((dAlt + 4000f) / 8000f);
            x[7] = Clamp01(skill);
            x[8] = Clamp01(acmChance);
            x[9] = canShoot ? 1f : 0f;
            x[10] = shouldBreak ? 1f : 0f;
            x[11] = threat ? 1f : 0f;
            x[12] = Clamp01(Clamp(mySpd / (tgtSpd > 1f ? tgtSpd : 1f), 0f, 2f) * 0.5f);
            x[13] = Clamp01(Clamp(dist / (breakDist > 1f ? breakDist : 1f), 0f, 4f) * 0.25f);
        }

        internal static int RuleMode(
            bool threat,
            AiCombatAcmPickService.Script pick,
            float acmChance,
            float rollAcm,
            bool shouldBreak,
            float preferAcmRoll,
            bool canShoot)
        {
            if (threat)
                return ModeEvade;
            if (pick != AiCombatAcmPickService.Script.None && rollAcm <= acmChance)
                return ModeAcm;
            if (shouldBreak)
            {
                if (AiCombatEngagementService.PreferAcmOverBreak(acmChance, preferAcmRoll))
                    return ModeAcm;
                return ModeBreakOff;
            }
            if (canShoot)
                return ModeAttack;
            return ModeHunt;
        }

        internal static AiCombatAcmPickService.Script RuleAcmCommit(
            AiCombatAcmPickService.Script pick,
            int mode,
            bool shouldBreak,
            float mySpd,
            float corner)
        {
            if (mode != ModeAcm)
                return pick;
            if (pick != AiCombatAcmPickService.Script.None)
                return pick;
            if (shouldBreak)
            {
                if (mySpd > corner * 0.9f)
                    return AiCombatAcmPickService.Script.JTurn;
                return AiCombatAcmPickService.Script.EnergyExtend;
            }
            return AiCombatAcmPickService.Script.None;
        }

        /// <summary>
        /// Network Mode + ACM. Returns false if the net is off / unloaded (caller uses rules).
        /// Evade/Cruise are never emitted here — Tick already handled those.
        /// </summary>
        internal static bool TryDecide(
            float dist,
            float angle,
            float aspect,
            float mySpd,
            float tgtSpd,
            float corner,
            float ralt,
            float dAlt,
            float skill,
            float acmChance,
            bool canShoot,
            bool shouldBreak,
            bool breakUntilActive,
            bool allowAcm,
            float breakDist,
            AiCombatAcmPickService.Script rulePick,
            int ruleMode,
            double rollMode,
            double rollAcm,
            out int mode,
            out AiCombatAcmPickService.Script acm)
        {
            mode = ruleMode;
            acm = rulePick;
            if (!IsLive())
                return false;

            Fill(Feat, dist, angle, aspect, mySpd, tgtSpd, corner, ralt, dAlt,
                skill, acmChance, canShoot, shouldBreak, false, breakDist);
            Forward(Feat, LogitM, LogitA);

            Mask[ModeHunt] = true;
            Mask[ModeAttack] = canShoot;
            Mask[ModeBreakOff] = shouldBreak || breakUntilActive;
            Mask[ModeEvade] = false;
            Mask[ModeCruise] = false;
            Mask[ModeAcm] = allowAcm;
            if (!Mask[ModeAttack] && !Mask[ModeBreakOff] && !Mask[ModeAcm])
                Mask[ModeHunt] = true;

            float temp = _temperature != null ? _temperature.Value : 0.35f;
            float blend = _blend != null ? _blend.Value : 0.7f;
            mode = SampleMasked(LogitM, NMode, Mask, temp, blend, ruleMode, rollMode);
            if (mode < 0)
                mode = ModeHunt;

            int ruleAcmI = (int)rulePick;
            if (ruleAcmI < 0 || ruleAcmI >= NAcm)
                ruleAcmI = 0;
            for (int i = 0; i < NAcm; i++)
                Mask[i] = true;
            int acmI = SampleMasked(LogitA, NAcm, Mask, temp, blend, ruleAcmI, rollAcm);
            if (acmI < 0)
                acmI = 0;
            acm = (AiCombatAcmPickService.Script)acmI;

            if (mode == ModeAcm && acm == AiCombatAcmPickService.Script.None)
                acm = RuleAcmCommit(rulePick, ModeAcm, shouldBreak, mySpd, corner);

            MaybeLog(mode, acm, ruleMode, rulePick);
            return true;
        }

        internal static AiCombatAcmPickService.Script BlendAcm(
            AiCombatAcmPickService.Script rulePick,
            float dist,
            float angle,
            float aspect,
            float mySpd,
            float tgtSpd,
            float corner,
            float ralt,
            float dAlt,
            float skill,
            float acmChance,
            bool canShoot,
            bool shouldBreak,
            float breakDist,
            double roll)
        {
            if (!IsLive())
                return rulePick;
            Fill(Feat, dist, angle, aspect, mySpd, tgtSpd, corner, ralt, dAlt,
                skill, acmChance, canShoot, shouldBreak, false, breakDist);
            Forward(Feat, LogitM, LogitA);
            for (int i = 0; i < NAcm; i++)
                Mask[i] = true;
            int ruleAcmI = (int)rulePick;
            if (ruleAcmI < 0 || ruleAcmI >= NAcm)
                ruleAcmI = 0;
            float temp = _temperature != null ? _temperature.Value : 0.35f;
            float blend = _blend != null ? _blend.Value : 0.7f;
            int acmI = SampleMasked(LogitA, NAcm, Mask, temp, blend, ruleAcmI, roll);
            if (acmI < 0)
                return rulePick;
            return (AiCombatAcmPickService.Script)acmI;
        }

        private static void Forward(float[] x, float[] logitM, float[] logitA)
        {
            int h, i;
            for (h = 0; h < Hidden; h++)
            {
                float acc = _b1[h];
                int row = h * In;
                for (i = 0; i < In; i++)
                    acc += _w1[row + i] * x[i];
                Hid[h] = acc > 0f ? acc : 0f;
            }
            for (h = 0; h < NMode; h++)
            {
                float acc = _bm[h];
                int row = h * Hidden;
                for (i = 0; i < Hidden; i++)
                    acc += _wm[row + i] * Hid[i];
                logitM[h] = acc;
            }
            for (h = 0; h < NAcm; h++)
            {
                float acc = _ba[h];
                int row = h * Hidden;
                for (i = 0; i < Hidden; i++)
                    acc += _wa[row + i] * Hid[i];
                logitA[h] = acc;
            }
        }

        private static int SampleMasked(
            float[] logits,
            int n,
            bool[] mask,
            float temp,
            float blend,
            int ruleIdx,
            double roll)
        {
            int i;
            bool any = false;
            for (i = 0; i < n; i++)
            {
                if (mask[i])
                    any = true;
            }
            if (!any)
                return -1;

            if (temp < 0.05f)
                blend = 1f;

            float tmax = logits[0];
            bool have = false;
            for (i = 0; i < n; i++)
            {
                if (!mask[i])
                    continue;
                if (!have || logits[i] > tmax)
                {
                    tmax = logits[i];
                    have = true;
                }
            }
            float t = temp < 0.05f ? 0.05f : temp;
            float sum = 0f;
            for (i = 0; i < n; i++)
            {
                if (!mask[i])
                {
                    Prob[i] = 0f;
                    continue;
                }
                float e = Mathf.Exp((logits[i] - tmax) / t);
                Prob[i] = e;
                sum += e;
            }
            if (sum < 1e-12f)
            {
                for (i = 0; i < n; i++)
                    Prob[i] = mask[i] ? 1f : 0f;
                sum = 0f;
                for (i = 0; i < n; i++)
                    sum += Prob[i];
            }
            for (i = 0; i < n; i++)
                Prob[i] = Prob[i] / sum;

            float b = blend;
            if (b < 0f) b = 0f;
            if (b > 1f) b = 1f;
            if (b < 0.999f)
            {
                for (i = 0; i < n; i++)
                    Prob[i] *= b;
                if (ruleIdx >= 0 && ruleIdx < n && mask[ruleIdx])
                    Prob[ruleIdx] += 1f - b;
                else
                {
                    float add = (1f - b) / Mathf.Max(1f, CountMask(mask, n));
                    for (i = 0; i < n; i++)
                    {
                        if (mask[i])
                            Prob[i] += add;
                    }
                }
                sum = 0f;
                for (i = 0; i < n; i++)
                    sum += Prob[i];
                if (sum > 0f)
                {
                    for (i = 0; i < n; i++)
                        Prob[i] /= sum;
                }
            }

            if (temp < 0.08f)
            {
                int best = -1;
                float bestP = -1f;
                for (i = 0; i < n; i++)
                {
                    if (Prob[i] > bestP)
                    {
                        bestP = Prob[i];
                        best = i;
                    }
                }
                return best;
            }

            double r = roll;
            if (r < 0.0) r = 0.0;
            if (r > 0.999999) r = 0.999999;
            double acc = 0.0;
            for (i = 0; i < n; i++)
            {
                acc += Prob[i];
                if (r <= acc)
                    return i;
            }
            for (i = n - 1; i >= 0; i--)
            {
                if (mask[i])
                    return i;
            }
            return -1;
        }

        private static float CountMask(bool[] mask, int n)
        {
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                if (mask[i])
                    c++;
            }
            return c;
        }

        private static float _nextLogAt;
        private static void MaybeLog(int mode, AiCombatAcmPickService.Script acm, int ruleMode, AiCombatAcmPickService.Script ruleAcm)
        {
            if (_log == null || !_log.Value)
                return;
            float now = Time.unscaledTime;
            if (now < _nextLogAt)
                return;
            _nextLogAt = now + 0.5f;
            try
            {
                string dir = Path.Combine(Paths.PluginPath, "OritasyNn");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "picks.csv");
                bool fresh = !File.Exists(path);
                using (StreamWriter w = File.AppendText(path))
                {
                    if (fresh)
                        w.WriteLine("t,mode,acm,ruleMode,ruleAcm");
                    w.WriteLine(
                        now.ToString("0.00") + "," +
                        mode + "," + (int)acm + "," + ruleMode + "," + (int)ruleAcm);
                }
            }
            catch { }
        }

        private static void EnsureLoaded()
        {
            if (_loadAttempted)
                return;
            _loadAttempted = true;
            _ready = false;
            _source = "none";
            int nW1 = Hidden * In;
            int nWm = NMode * Hidden;
            int nWa = NAcm * Hidden;
            int n = nW1 + Hidden + nWm + NMode + nWa + NAcm;

            float[] data = null;
            try
            {
                string path = Path.Combine(Paths.PluginPath, "OritasyNn", "ai_actions.weights");
                if (File.Exists(path))
                    data = ReadFile(path, n);
                if (data != null)
                    _source = "file";
            }
            catch { }

            if (data == null)
            {
                try
                {
                    if (AiCombatNnWeights.Data != null && AiCombatNnWeights.Data.Length == n
                        && AiCombatNnWeights.In == In && AiCombatNnWeights.Hidden == Hidden)
                    {
                        data = AiCombatNnWeights.Data;
                        _source = "embedded";
                    }
                }
                catch { }
            }

            if (data == null || data.Length != n)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("AiNn: no weights (" + n + " floats)");
                return;
            }

            int o = 0;
            _w1 = Slice(data, ref o, nW1);
            _b1 = Slice(data, ref o, Hidden);
            _wm = Slice(data, ref o, nWm);
            _bm = Slice(data, ref o, NMode);
            _wa = Slice(data, ref o, nWa);
            _ba = Slice(data, ref o, NAcm);
            _ready = true;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("AiNn: loaded " + _source + " (" + n + " floats)");
        }

        private static float[] Slice(float[] src, ref int o, int len)
        {
            float[] d = new float[len];
            Array.Copy(src, o, d, 0, len);
            o += len;
            return d;
        }

        private static float[] ReadFile(string path, int expected)
        {
            byte[] raw = File.ReadAllBytes(path);
            if (raw == null || raw.Length < 28)
                return null;
            if (raw[0] != (byte)'O' || raw[1] != (byte)'A' || raw[2] != (byte)'N' || raw[3] != (byte)'N')
                return null;
            int ver = ReadI32(raw, 4);
            int inn = ReadI32(raw, 8);
            int hid = ReadI32(raw, 12);
            int nm = ReadI32(raw, 16);
            int na = ReadI32(raw, 20);
            int count = ReadI32(raw, 24);
            if (ver != AiCombatNnWeights.Version && ver != 1)
                return null;
            if (inn != In || hid != Hidden || nm != NMode || na != NAcm)
                return null;
            if (count != expected)
                return null;
            if (raw.Length < 28 + expected * 4)
                return null;
            float[] data = new float[expected];
            Buffer.BlockCopy(raw, 28, data, 0, expected * 4);
            return data;
        }

        private static int ReadI32(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
