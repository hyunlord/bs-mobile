using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Game.App
{
    public static class DeviceIdentityVaultFactory
    {
        public static bool DevelopmentEnabled
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
        public static IDeviceIdentityVault Create(string endpoint)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DevelopmentEnabled) return new DevelopmentDeviceIdentityVault(endpoint);
#endif
            return new NativeDeviceIdentityVault(endpoint);
        }
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Explicit development-only storage: nothing survives process exit and no OS vault is touched.
    public sealed class DevelopmentDeviceIdentityVault : IDeviceIdentityVault
    {
        readonly string scope;
        public DevelopmentDeviceIdentityVault(string endpoint, string isolation = "interactive")
        {
            if (!DeviceAuthHttp.ValidEndpoint(endpoint, true) || !new Uri(endpoint).IsLoopback)
                throw new ArgumentException("Development identity requires a loopback server.");
            using var hash = SHA256.Create();
            scope = "sow-dev-memory-v1-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(endpoint.TrimEnd('/') + "\n" + isolation))).Replace("-", "").ToLowerInvariant();
        }
        public string Load() => Call("load", null);
        public void Save(string record) { if (Call("save", record) != "ok") throw new InvalidOperationException("Development memory storage failed."); }
        public byte[] PublicKeySpki() => Convert.FromBase64String(Call("public", null));
        public byte[] Sign(byte[] message) => DeviceSignature.P1363(Convert.FromBase64String(Call("sign", Convert.ToBase64String(message))));
        string Call(string operation, string value)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            var pointer = SowDevelopmentIdentity(scope, operation, value);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Development identity unavailable.");
            try { return Marshal.PtrToStringUTF8(pointer); }
            finally { SowIdentityFree(pointer); }
#else
            throw new PlatformNotSupportedException("Development memory identity currently supports Mac only.");
#endif
        }
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("SowDeviceIdentity")] static extern IntPtr SowDevelopmentIdentity([MarshalAs(UnmanagedType.LPUTF8Str)] string scope,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string operation, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("SowDeviceIdentity")] static extern void SowIdentityFree(IntPtr value);
#endif
    }
#endif
}
