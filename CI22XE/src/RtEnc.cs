using System;

namespace Oritasy
{
    /// <summary>
    /// Startup string pool decoder. Post-build rewriter replaces ldstr with ldsfld
    /// into fields filled here. Keep the XOR identical to tools/protect_dll.cs.
    /// </summary>
    internal static class RtEnc
    {
        internal const int Key = 0xA5;
        internal const int Stride = 13;

        internal static string Dec(string p)
        {
            if (p == null || p.Length == 0)
                return p;
            char[] c = p.ToCharArray();
            int n = c.Length;
            int k = Key;
            for (int i = 0; i < n; i++)
            {
                int mix = (i * Stride) & 255;
                c[i] = (char)(c[i] ^ k ^ mix);
            }
            return new string(c);
        }
    }
}
