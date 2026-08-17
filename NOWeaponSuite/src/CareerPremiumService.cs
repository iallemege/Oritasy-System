using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Offline CDK premium. Dated HMAC keys work across Oritasy versions;
    /// each key must be redeemed within 30 days of its generation date.
    /// Legacy 2026-08-15 whitelist keys expire 2026-09-14.
    /// </summary>
    internal static class CareerPremiumService
    {
        private const string PrefUntil = "WeXon.Career.premUntil";
        private const string PrefUsed = "WeXon.Career.premUsed";
        private const string HashPrefix = "ORITASY.CDK.1|";
        private const string TestHash =
            "92aa95f422c261fad9a768793641356cbf9dfe05a3e063c980fded4e0ddb3f26";
        internal const int XpMul = 10;

        private static readonly Dictionary<string, int> Map = BuildMap();
        private static readonly HashSet<string> Used = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loaded;
        private static long _untilTicks;
        private static string _input = "";
        private static string _msgEn = "";
        private static string _msgZh = "";
        private static bool _msgOk;

        internal static int BaseXpMul()
        {
            return IsActive() ? XpMul : 1;
        }

        internal static bool IsActive()
        {
            EnsureLoaded();
            return RemainingTicks() > 0L;
        }

        internal static string StatusLine(bool zh)
        {
            EnsureLoaded();
            if (!IsActive())
                return zh ? "未激活" : "Inactive";
            return zh ? ("已激活  " + FormatRemainZh()) : ("ACTIVE  " + FormatRemainEn());
        }

        /// <summary>Dev panel: set remaining real-time days from now. 0 clears.</summary>
        internal static void DevSetDays(int days)
        {
            EnsureLoaded();
            if (days <= 0)
                _untilTicks = 0L;
            else
                _untilTicks = DateTime.UtcNow.AddDays(days).Ticks;
            Persist();
        }

        internal static void DrawSection(GUIStyle title, GUIStyle body, GUIStyle btn, GUIStyle warn)
        {
            EnsureLoaded();
            GUILayout.Label(ModUiLang.T("CDK input", "CDK 输入口"), title);

            if (IsActive())
            {
                GUILayout.Label(ModUiLang.T(
                    "ACTIVE  " + FormatRemainEn(),
                    "已激活  " + FormatRemainZh()), body);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ModUiLang.T("CDK", "CDK"), GUILayout.Width(40f));
                if (_input == null)
                    _input = "";
                GUI.SetNextControlName("OritasyCdkInput");
                _input = GUILayout.TextField(_input, 17, GUILayout.MinWidth(220f), GUILayout.Height(24f));
                if (GUILayout.Button(ModUiLang.T("Redeem", "兑换"), btn, GUILayout.Width(72f), GUILayout.Height(24f)))
                    TryRedeem(_input);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(_msgEn))
            {
                GUIStyle st = _msgOk ? body : warn;
                GUILayout.Label(ModUiLang.T(_msgEn, _msgZh), st);
            }
        }

        private static void TryRedeem(string raw)
        {
            if (IsActive())
            {
                SetMsg(false,
                    "Premium is still active. Wait until it expires before redeeming another key.",
                    "高级账号尚未到期，不能兑换其他 key。");
                return;
            }

            string key = OritasyCdk.CdkCodec.Normalize(raw);
            if (key.Length != 17)
            {
                SetMsg(false,
                    "Enter a key like XXXXX-XXXXX-XXXXX.",
                    "请输入 XXXXX-XXXXX-XXXXX 格式的 CDK。");
                return;
            }

            string hash = HashKey(key);
            bool test = string.Equals(hash, TestHash, StringComparison.Ordinal)
                || OritasyCdk.CdkCodec.IsTestKey(key);
            int days = 0;
            if (test)
            {
                days = 1;
            }
            else
            {
                int datedDays;
                DateTime genUtc;
                string fail;
                if (OritasyCdk.CdkCodec.TryVerify(key, out datedDays, out genUtc, out fail))
                {
                    days = datedDays;
                }
                else if (string.Equals(fail, "expired", StringComparison.Ordinal)
                    || string.Equals(fail, "future", StringComparison.Ordinal))
                {
                    SetMsg(false, "This CDK has expired.", "此 CDK 已过期。");
                    return;
                }
                else if (Map.TryGetValue(hash, out days) && days > 0)
                {
                    if (!OritasyCdk.CdkCodec.LegacyStillValid())
                    {
                        SetMsg(false, "This CDK has expired.", "此 CDK 已过期。");
                        return;
                    }
                }
                else
                {
                    SetMsg(false, "This CDK is not valid.", "此 CDK 无效。");
                    return;
                }
            }

            if (!test && Used.Contains(hash))
            {
                SetMsg(false,
                    "This CDK has already been used.",
                    "此 CDK 已经使用过。");
                return;
            }

            DateTime until = DateTime.UtcNow.AddDays(days);
            _untilTicks = until.Ticks;
            if (!test)
                Used.Add(hash);
            Persist();
            SetMsg(true,
                "Redeemed +" + days + " day(s). Premium until " + until.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ".",
                "兑换成功 +" + days + " 天。高级账号至 " + until.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "。");
            _input = "";
        }

        private static void SetMsg(bool ok, string en, string zh)
        {
            _msgOk = ok;
            _msgEn = en;
            _msgZh = zh;
        }

        private static long RemainingTicks()
        {
            long left = _untilTicks - DateTime.UtcNow.Ticks;
            if (left < 0L)
                return 0L;
            return left;
        }

        private static string FormatRemainEn()
        {
            TimeSpan t = new TimeSpan(RemainingTicks());
            if (t.TotalDays >= 1.0)
                return ((int)t.TotalDays) + "d " + t.Hours + "h left";
            if (t.TotalHours >= 1.0)
                return t.Hours + "h " + t.Minutes + "m left";
            return t.Minutes + "m left";
        }

        private static string FormatRemainZh()
        {
            TimeSpan t = new TimeSpan(RemainingTicks());
            if (t.TotalDays >= 1.0)
                return "剩余 " + ((int)t.TotalDays) + "天" + t.Hours + "小时";
            if (t.TotalHours >= 1.0)
                return "剩余 " + t.Hours + "小时" + t.Minutes + "分";
            return "剩余 " + t.Minutes + "分钟";
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            try
            {
                string raw = PlayerPrefs.GetString(PrefUntil, "0");
                long ticks;
                if (!long.TryParse(raw, out ticks))
                    ticks = 0L;
                _untilTicks = ticks;
            }
            catch { _untilTicks = 0L; }

            Used.Clear();
            try
            {
                string blob = PlayerPrefs.GetString(PrefUsed, "");
                if (!string.IsNullOrEmpty(blob))
                {
                    string[] parts = blob.Split(new char[] { ',' });
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string p = parts[i] != null ? parts[i].Trim() : "";
                        if (p.Length > 0)
                            Used.Add(p);
                    }
                }
            }
            catch { }
        }

        private static void Persist()
        {
            try
            {
                PlayerPrefs.SetString(PrefUntil, _untilTicks.ToString());
                StringBuilder sb = new StringBuilder();
                foreach (string h in Used)
                {
                    if (sb.Length > 0)
                        sb.Append(',');
                    sb.Append(h);
                }
                PlayerPrefs.SetString(PrefUsed, sb.ToString());
                PlayerPrefs.Save();
            }
            catch { }
        }

        private static string HashKey(string key)
        {
            SHA256 sha = SHA256.Create();
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(HashPrefix + key);
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
            finally
            {
                sha.Dispose();
            }
        }

        private static Dictionary<string, int> BuildMap()
        {
            Dictionary<string, int> Map = new Dictionary<string, int>(StringComparer.Ordinal);
            Map[TestHash] = 1;
            Map["0eec33ce74b96afcb9352197679406fc38266a196a0ae52fce02e90c21dfcf22"] = 1;
            Map["cb275b52d306693f3e6c0121263751e79ea7c82e6f7daa9526dc3e37273cb421"] = 1;
            Map["6c1abea73fbbd2ccc42d3813146649e4b40c687e7d4919465916a294523194cc"] = 1;
            Map["54cc8583562e70bacdf543088c87c02d0aaa6b0e3d8352e52f24cdae0b82e3f7"] = 1;
            Map["1aa57c8e48bb043f7ab3083dcba046b7f0256949fc64e04d0d844713f6b58b12"] = 1;
            Map["dfe5a7b9780c88d67f3e755fafc5959a9e43035390609f6c7525648d5ad19774"] = 1;
            Map["3bf0054944874811fa904b29d4c11413cae92403ca1359080a11a5e5d9ef1384"] = 1;
            Map["5bf0d7d49c97c6661f35ec50c1104535a5b48c39dee3a3a975d376d818159061"] = 1;
            Map["830dafd5ec996aaee31eb7b0332103c312c039959c1d591e5b267d425d245fd3"] = 1;
            Map["5b4da4d2004d67c88e903acc75aaeddaca6dc79052fc2dad0fdf1933376107dd"] = 1;
            Map["46679acdb9f0dd3447fa2f2ccbbd8a2db14b8d2c85cabb22f9e67b0f4453adbf"] = 3;
            Map["815b9b15cacf601f569325fd32f24d9feb24b4fa848baba076798cec2a6d3c17"] = 3;
            Map["83685b10f06a555140c83fd8738cbe4ec4d21ae46b52dcb32a04d00fd2b7c50e"] = 3;
            Map["207c4fdb86b6073e07aaae82a00c8bd76cbdd81cc06a071253d8cf167a09c3c9"] = 3;
            Map["1d125d1ce622c98cb72e1c96c3fc1a0d5fe1295e3211ace5a27e102a28a1ba61"] = 3;
            Map["947da31a863f565db1705cd216b5d1404e4e7660230081d3967f053166f0c6cf"] = 3;
            Map["738cad1aa73e664c9a4de57b97dd29ce0364fa1f1a1ac1d46b633dad8c549792"] = 3;
            Map["04229859e68deade9a676fbed64388252361db91b47050de6c0c194481bf0853"] = 3;
            Map["09293d1d32264d18210b9ea24437638bae399f3d66bc5b3bb1a9f2490b9cdd7f"] = 3;
            Map["3aefba780c1b90eeaf801b2000bd8f734220eceabebb0f5253c742ec518b9dea"] = 3;
            Map["d1739d714a6968ba9aa77abf68f6089b33195e26e81e753e5e39208ab823bc7f"] = 5;
            Map["0640a22514a71cbe27be4f69cad632bd65b092a5f2d572a97acc3147c4f96b35"] = 5;
            Map["b329b71d20fb48afa467077ba01c5ae60d5d29b73816cfd52d013edce3696014"] = 5;
            Map["087ac0de943d91eb34bf558051c12582550c18870543cd3a509c09764b89cd14"] = 5;
            Map["e7aed0db0471558f25cb24cedc95bd16294110cdfb9fa136aa52e07340a13478"] = 5;
            Map["b7a42078dde227f0e138ddd0fa1a08c30327fc11dd1596b05c8dbbd4fa239abd"] = 5;
            Map["821f14d8bbaf4465e056582b4ee4d6a2c0ac83b976adb5d4ed179e83b92d97d8"] = 5;
            Map["e33856abd9a01eb194a3626c41e74cdd2a566c85377cf4120cf2809244b77d90"] = 5;
            Map["27cd3291c04ccecda67e21fbe432bcff288595da91b2dd3b339720f2774e50af"] = 5;
            Map["ba02b8c692e47fe26b466b6ca87571d7d05d725858362d3aac9ccca2d54ef79b"] = 5;
            Map["19b13adb8dc040896665e7a67750de2ed8aadf42203f12352a5c500e0aaa854f"] = 7;
            Map["4f1b954b5c882ea2a3b2531b76467fb18f088175cd617a20120c57e4037d701a"] = 7;
            Map["4defc3b8edaee032f091404f92e886ac3d4438b63a7d4882d2f679ba5ac162fc"] = 7;
            Map["79e849ad2b52ff13475d3645183e13ed88be30a0e3b6076703955d2b68ab6277"] = 7;
            Map["d761cab9ec5dc0b3cdc76d4124ee76ba1a987854a18e46e90c351b6bf2340eb5"] = 7;
            Map["349ddfed6d03c5afab59d813bc99ab5b30900bcdf816f55289ebd50c208bde28"] = 7;
            Map["88a3430242c364dfff435ce41327148535f49cb8f9e24595defa3925ea002630"] = 7;
            Map["fe3c75125032568f30f2f6c85735904dddfe5a5f4fee9232480c3e2740bf578a"] = 7;
            Map["0294dfa23ffde9a4401d60c3901800f78f4b795ddc6e6fbb6ac81c8ddf6ef953"] = 7;
            Map["aa2dedd090a5097e2fd261d61ac8f2f78959008cb64755814fe1a1ce4d7e7262"] = 7;
            Map["640ae343c5150ce0cc3acae5933890081a920bae6ffcc093516821a25c2809c6"] = 14;
            Map["8a3c4b25b649be7d0adaa1d222e12bf9b04e336cceabe8f1a22679ebe5e4c4e5"] = 14;
            Map["bc5c0c9288a122024559ec798d3672da2e73217ab440cfdd9152eb83b43a79d5"] = 14;
            Map["46332c4b6700d0ab39b9273c3061378255410704f13259a97c6be2349313ff2f"] = 14;
            Map["f74401ae417d42732d443d46caccfd6f7f34eb9c7ec1337fce4fda7ff1a43eb6"] = 14;
            Map["b10c26a977d6bf7e8798ff08bfeba72b2fe5e79ce77aae8632a818757b01c1fc"] = 14;
            Map["d556dde910e4f338b6fab6fd56594137b891c7d9a1fb99ce3c1dc38b39f9cf9e"] = 14;
            Map["1d22f4e8909dc0cf06e5e36dfe0a6f97708f488c8c28e4264c9a2a5ea3b8d249"] = 14;
            Map["1b587cae4522b1108be748e2ce29be827c00c69a894bdd92f944299d876860fd"] = 14;
            Map["7129bbf58ac1e64cce0657a5f3f049b5700301e9e22335f0de5590e8d35e3b8f"] = 14;
            Map["1c11554f4385dec5c35942216f99a8d1ab0b5ca8c069e8e161bd3cb98cd6fc5a"] = 30;
            Map["976b4581f4cea87148dcdecaf81cbe9c62939f158e858b52d82674200104a8a1"] = 30;
            Map["3b64fe75b495eb3326385a352f0de6e531d1f45d033dcaf1fb40acc2d685b492"] = 30;
            Map["cef6e558f5e5cb0c9aa9326334f8b6190c884f7fc14c67e779a45405d7838b07"] = 30;
            Map["43959fc7db7b3ca9fdf95318e2d3857afd465d5cf610b68ae5f25cba647fe8e3"] = 30;
            Map["b105879344780306b9a02333aa387d010d717a5f093243d10891b7ab81f97b21"] = 30;
            Map["5da6145859f6b0c846a5e63b7e3bc42ae9cf6e67a5bfdf8df1477468822477b5"] = 30;
            Map["099d7edcb42e6787cb703927c3959994e26d9dc18368991cead1b500b5f1b030"] = 30;
            Map["bdd5b4c9f2d43bc6fb4f05c969c05e3714cfb8bcdee03efd9e5eb6022b6e0816"] = 30;
            Map["30e9d3977ad31a54e5e8062b1197f0c90f30996919c60e2f5c6eed99c90afa25"] = 30;
            return Map;
        }
    }
}
