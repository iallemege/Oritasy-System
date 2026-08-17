using System;
using System.Security.Cryptography;
using System.Text;

namespace OritasyCdk
{
    /// <summary>
    /// Dated CDK: HMAC in the key, not tied to Oritasy version.
    /// Redeem window = generation UTC date + 30 days.
    /// Format XXXXX-XXXXX-XXXXX.
    /// </summary>
    public static class CdkCodec
    {
        public const int KeyValidDays = 30;
        public const string TestKey = "TEST1-CDK00-ORITA";
        public static readonly DateTime Epoch =
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static readonly DateTime LegacyBatchUtc =
            new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

        private const string Secret = "ORITASY.CDK.1";
        private const string Alpha = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "";
            StringBuilder sb = new StringBuilder(17);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == ' ' || c == '\t')
                    continue;
                if (c >= 'a' && c <= 'z')
                    c = (char)(c - 32);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || c == '-')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Generate(DateTime utcDate, int premiumDays, Random rng)
        {
            if (rng == null)
                rng = new Random();
            DateTime d = utcDate.Kind == DateTimeKind.Utc ? utcDate.Date : utcDate.ToUniversalTime().Date;
            int dayNum = (int)(d - Epoch).TotalDays;
            if (dayNum < 0)
                dayNum = 0;
            char dur;
            if (!TryDurChar(premiumDays, out dur))
                throw new ArgumentOutOfRangeException("premiumDays");
            char[] nonce = new char[8];
            for (int i = 0; i < 8; i++)
                nonce[i] = Alpha[rng.Next(36)];
            string payload = ToBase36(dayNum, 3) + dur + new string(nonce);
            return Format15(payload + Mac3(payload));
        }

        public static bool TryVerify(string raw, out int premiumDays, out DateTime genUtc, out string fail)
        {
            premiumDays = 0;
            genUtc = Epoch;
            fail = "invalid";
            string key = Normalize(raw);
            if (key.Length != 17 || key[5] != '-' || key[11] != '-')
                return false;
            string raw15 = key.Substring(0, 5) + key.Substring(6, 5) + key.Substring(12, 5);
            if (raw15.Length != 15)
                return false;
            string payload = raw15.Substring(0, 12);
            string mac = raw15.Substring(12, 3);
            if (!string.Equals(mac, Mac3(payload), StringComparison.Ordinal))
                return false;
            int dayNum;
            if (!FromBase36(payload, 0, 3, out dayNum))
                return false;
            if (!TryDurDays(payload[3], out premiumDays))
                return false;
            genUtc = Epoch.AddDays(dayNum);
            DateTime today = DateTime.UtcNow.Date;
            if (genUtc > today.AddDays(1))
            {
                fail = "future";
                return false;
            }
            if (today >= genUtc.AddDays(KeyValidDays))
            {
                fail = "expired";
                return false;
            }
            fail = "";
            return true;
        }

        public static bool LegacyStillValid()
        {
            return DateTime.UtcNow.Date < LegacyBatchUtc.AddDays(KeyValidDays);
        }

        public static DateTime LegacyExpireUtc()
        {
            return LegacyBatchUtc.AddDays(KeyValidDays);
        }

        public static bool IsTestKey(string key)
        {
            return string.Equals(Normalize(key), TestKey, StringComparison.Ordinal);
        }

        private static string Mac3(string payload12)
        {
            HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            try
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload12));
                int v = ((hash[0] << 16) | (hash[1] << 8) | hash[2]) % 46656;
                if (v < 0)
                    v = -v;
                return ToBase36(v, 3);
            }
            finally
            {
                hmac.Dispose();
            }
        }

        private static string Format15(string raw15)
        {
            return raw15.Substring(0, 5) + "-" + raw15.Substring(5, 5) + "-" + raw15.Substring(10, 5);
        }

        private static string ToBase36(int n, int width)
        {
            if (n < 0)
                n = 0;
            char[] buf = new char[width];
            for (int i = width - 1; i >= 0; i--)
            {
                buf[i] = Alpha[n % 36];
                n /= 36;
            }
            return new string(buf);
        }

        private static bool FromBase36(string s, int start, int len, out int n)
        {
            n = 0;
            if (s == null || start < 0 || start + len > s.Length)
                return false;
            for (int i = 0; i < len; i++)
            {
                int d = Alpha.IndexOf(s[start + i]);
                if (d < 0)
                    return false;
                n = n * 36 + d;
            }
            return true;
        }

        private static bool TryDurChar(int days, out char c)
        {
            c = '1';
            if (days == 1) { c = '1'; return true; }
            if (days == 3) { c = '3'; return true; }
            if (days == 5) { c = '5'; return true; }
            if (days == 7) { c = '7'; return true; }
            if (days == 14) { c = 'E'; return true; }
            if (days == 30) { c = 'U'; return true; }
            return false;
        }

        private static bool TryDurDays(char c, out int days)
        {
            days = 0;
            if (c == '1') { days = 1; return true; }
            if (c == '3') { days = 3; return true; }
            if (c == '5') { days = 5; return true; }
            if (c == '7') { days = 7; return true; }
            if (c == 'E') { days = 14; return true; }
            if (c == 'U') { days = 30; return true; }
            return false;
        }
    }
}
