using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Game.App
{
    public interface IDeviceIdentityVault
    {
        string Load();
        void Save(string record);
        byte[] PublicKeySpki();
        byte[] Sign(byte[] message);
    }

    public static class DeviceSignature
    {
        public static byte[] Message(string keyId, string nonce)
        {
            if (!Guid.TryParse(keyId, out var id) || string.IsNullOrEmpty(nonce)) throw new FormatException("Invalid challenge.");
            return Encoding.UTF8.GetBytes("sow-auth-v1\n" + id.ToString("D") + "\n" + nonce);
        }
        public static byte[] P1363(byte[] der)
        {
            if (der == null || der.Length < 8 || der.Length > 72 || der[0] != 0x30 || der[1] != der.Length - 2)
                throw new FormatException("Invalid ECDSA signature.");
            var result = new byte[64]; var offset = 2;
            Integer(der, ref offset, result, 0); Integer(der, ref offset, result, 32);
            if (offset != der.Length) throw new FormatException("Trailing ECDSA data.");
            return result;
        }
        static void Integer(byte[] der, ref int offset, byte[] output, int target)
        {
            if (offset + 2 > der.Length || der[offset++] != 2) throw new FormatException("Invalid ECDSA integer.");
            int count = der[offset++];
            if (count < 1 || count > 33 || offset + count > der.Length || (der[offset] & 0x80) != 0)
                throw new FormatException("Invalid ECDSA integer.");
            if (der[offset] == 0 && count > 1)
            {
                if ((der[offset + 1] & 0x80) == 0) throw new FormatException("Noncanonical ECDSA integer.");
                offset++; count--;
            }
            if (count > 32) throw new FormatException("Oversized ECDSA integer.");
            Buffer.BlockCopy(der, offset, output, target + 32 - count, count); offset += count;
        }
    }

    public sealed class NativeDeviceIdentityVault : IDeviceIdentityVault
    {
        readonly string scope;
        public NativeDeviceIdentityVault(string endpoint)
        {
            using var hash = SHA256.Create();
            scope = "sow-device-v1-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(endpoint))).Replace("-", "").ToLowerInvariant();
        }
        public string Load() => Call("load", null);
        public void Save(string record) { if (Call("save", record) != "ok") throw new InvalidOperationException("Secure storage failed."); }
        public byte[] PublicKeySpki() => Convert.FromBase64String(Call("public", null));
        public byte[] Sign(byte[] message) => DeviceSignature.P1363(Convert.FromBase64String(Call("sign", Convert.ToBase64String(message))));
        string Call(string operation, string value)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var bridge = new AndroidJavaClass("com.sowsiege.auth.DeviceIdentity");
            return bridge.CallStatic<string>("call", activity, scope, operation, value);
#elif (UNITY_IOS && !UNITY_EDITOR) || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var pointer = SowIdentity(scope, operation, value);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Secure device storage unavailable.");
            try { return Marshal.PtrToStringUTF8(pointer); }
            finally { SowIdentityFree(pointer); }
#else
            throw new PlatformNotSupportedException("Secure device storage unavailable on this platform.");
#endif
        }
#if UNITY_IOS && !UNITY_EDITOR
        const string Library = "__Internal";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        const string Library = "SowDeviceIdentity";
#endif
#if (UNITY_IOS && !UNITY_EDITOR) || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport(Library)] static extern IntPtr SowIdentity([MarshalAs(UnmanagedType.LPUTF8Str)] string scope,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string operation, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library)] static extern void SowIdentityFree(IntPtr value);
#endif
    }
}
