# Device identity bridge

The Apple bridge uses a Secure Enclave P-256 key and device-only Keychain session item. It never exports a private key or requests biometric UI; unavailable hardware, locked storage, missing entitlements and denied access fail closed. iOS compiles the `.mm` plug-in. macOS builds the same source using `bash tools/native-device-auth/build-macos.sh`; follow the shared Mac lock and daytime rules before invoking a compiler or Unity. Sign distribution applications with the project's normal user-owned signing process; this tool does not obtain or store signing keys.

Android uses AndroidKeyStore EC signing plus a separate AES-GCM key for session ciphertext under `Context.getNoBackupFilesDir()`. The signing key is never encoded or serialized. No authentication fallback stores secrets in PlayerPrefs or a plaintext file.

Both platforms return X.509 SPKI public keys and ASN.1 DER ECDSA signatures. `DeviceSignature` converts DER to the server's fixed-width IEEE P1363 encoding and constructs the exact UTF-8 nonce message.

Sources checked 2026-11-02: [Apple Secure Enclave](https://developer.apple.com/documentation/security/protecting-keys-with-the-secure-enclave), [Android Keystore](https://developer.android.com/privacy-and-security/keystore), [KeyGenParameterSpec](https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec).

Mobile packaging, Keychain entitlements and device behavior need actual platform verification; a Mac syntax check is not that evidence. Generated macOS libraries belong to the local checkout and are not source artifacts.

Prototype configuration currently uses `--account-server https://…` or `SOW_ACCOUNT_SERVER`. HTTP is restricted to Editor/Development loopback. No production endpoint or mobile signing configuration is embedded. A phone build requires a separately selected deployment URL and platform packaging validation. This is not an installed mobile authentication delivery.
