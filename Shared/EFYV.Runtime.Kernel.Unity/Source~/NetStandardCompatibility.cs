using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Efyv.RuntimeKernel;

// Unity 6000.6's .NET Standard 2.1 surface predates this .NET guard. Keeping
// the compatibility type in the binding namespace lets the unchanged official
// source retain its normal call site and exception contract.
internal sealed class ArgumentOutOfRangeException : System.ArgumentOutOfRangeException
{
    internal ArgumentOutOfRangeException(string parameterName)
        : base(parameterName)
    {
    }

    internal static void ThrowIfNegative(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}

// Unity loads native plug-ins by the DllImport library name. .NET Standard 2.1
// has no NativeLibrary resolver API, so the official resolver hook becomes a
// deliberate no-op; all ABI declarations remain solely in the official source.
internal static class NativeLibrary
{
    internal static void SetDllImportResolver(
        Assembly assembly,
        Func<string, Assembly, DllImportSearchPath?, nint> resolver)
    {
    }

    internal static bool TryLoad(string libraryPath, out nint handle)
    {
        handle = 0;
        return false;
    }
}
