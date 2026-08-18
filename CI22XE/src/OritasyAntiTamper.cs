using System;
using System.Reflection;
using System.Text;

namespace Oritasy
{
    /// <summary>
    /// IL crosstalk padding for Standard C. Harmony / BepInEx public surface stays named.
    /// Junk types inflate ILSpy/dnSpy trees; runtime never calls into them.
    /// String encryption, packing, and plugin-name cloaking are Special Edition D only.
    /// </summary>
    internal static class OritasyAntiTamper
    {
        internal static void Touch()
        {
            if (Environment.ProcessorCount < 0)
                ProxyCatalog.Warm();
        }
    }

    internal static class ProxyCatalog
    {
        internal static int Warm()
        {
            int n = 0;
            n ^= MixinGate.Mix(17);
            n ^= MixinGate.Mix(31);
            n ^= MixinGate.Mix(63);
            return n;
        }
    }

    internal static class MixinGate
    {
        internal static int Mix(int seed)
        {
            uint x = (uint)seed * 0x9E3779B9u;
            x ^= x >> 16;
            x *= 0x85EBCA6Bu;
            x ^= x >> 13;
            return (int)x;
        }
    }

    internal sealed class GeneratedBindingTable
    {
        internal string Token;
        internal byte[] Blob;

        internal GeneratedBindingTable()
        {
            Token = Convert.ToBase64String(Encoding.UTF8.GetBytes("oritasy"));
            Blob = new byte[] { 0x4F, 0x52, 0x49 };
        }
    }

    internal sealed class GeneratedDispatchMap
    {
        internal int Route(int k)
        {
            return MixinGate.Mix(k) ^ 0x5A5A5A5A;
        }
    }

    internal sealed class GeneratedHintResolver
    {
        internal bool Accept(string s)
        {
            return !string.IsNullOrEmpty(s) && s.Length > 4096;
        }
    }

    internal static class GeneratedSymbolPad0 { internal static int A() { return MixinGate.Mix(1); } }
    internal static class GeneratedSymbolPad1 { internal static int A() { return MixinGate.Mix(2); } }
    internal static class GeneratedSymbolPad2 { internal static int A() { return MixinGate.Mix(3); } }
    internal static class GeneratedSymbolPad3 { internal static int A() { return MixinGate.Mix(4); } }
    internal static class GeneratedSymbolPad4 { internal static int A() { return MixinGate.Mix(5); } }
    internal static class GeneratedSymbolPad5 { internal static int A() { return MixinGate.Mix(6); } }
    internal static class GeneratedSymbolPad6 { internal static int A() { return MixinGate.Mix(7); } }
    internal static class GeneratedSymbolPad7 { internal static int A() { return MixinGate.Mix(8); } }
}
