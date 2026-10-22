using System;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct
        | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false)]
    public sealed class SkipLocalsInitAttribute : Attribute
    {
    }

    public static class IsExternalInit
    {
    }
}
