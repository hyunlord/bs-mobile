using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record CoreAssemblyMetadata(string TargetFramework, string Mvid, string Sha256, string Location)
{
    public static CoreAssemblyMetadata Read()
    {
        var assembly = typeof(Simulation).Assembly;
        return new(assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? throw new InvalidOperationException("Core has no target framework metadata."),
            assembly.ManifestModule.ModuleVersionId.ToString("D"), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))), assembly.Location);
    }

    public static CoreAssemblyMetadata VerifyHostBinding()
    {
        var identity = Read();
#if COMPAT_CORE
        const string expected = ".NETStandard,Version=v2.1";
#else
        const string expected = ".NETCoreApp,Version=v8.0";
#endif
        if (identity.TargetFramework != expected) { throw new InvalidOperationException($"Expected Core {expected}; loaded {identity.TargetFramework} from {identity.Location}."); }
        return identity;
    }
}
