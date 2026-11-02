# Device identity bridge

The Apple bridge uses a Secure Enclave P-256 key and device-only Keychain session item. It never exports a private key or requests biometric UI; unavailable hardware, locked storage, missing entitlements and denied access fail closed. iOS compiles the `.mm` plug-in. macOS builds the same source using `bash tools/native-device-auth/build-macos.sh`; follow the shared Mac lock and daytime rules before invoking a compiler or Unity. Sign distribution applications with the project's normal user-owned signing process; this tool does not obtain or store signing keys.

Android uses AndroidKeyStore EC signing plus a separate AES-GCM key for session ciphertext under `Context.getNoBackupFilesDir()`. The signing key is never encoded or serialized. No authentication fallback stores secrets in PlayerPrefs or a plaintext file.

Both platforms return X.509 SPKI public keys and ASN.1 DER ECDSA signatures. `DeviceSignature` converts DER to the server's fixed-width IEEE P1363 encoding and constructs the exact UTF-8 nonce message.

Sources checked 2026-11-02: [Apple Secure Enclave](https://developer.apple.com/documentation/security/protecting-keys-with-the-secure-enclave), [Android Keystore](https://developer.android.com/privacy-and-security/keystore), [KeyGenParameterSpec](https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec).

Mobile packaging, Keychain entitlements and device behavior need actual platform verification; a Mac syntax check is not that evidence. Generated macOS libraries belong to the local checkout and are not source artifacts.

Prototype configuration currently uses `--account-server https://…` or `SOW_ACCOUNT_SERVER`. HTTP is restricted to Editor/Development loopback. No production endpoint or mobile signing configuration is embedded. A phone build requires a separately selected deployment URL and platform packaging validation. This is not an installed mobile authentication delivery.

## Actual local-server probe

After obtaining the shared Mac slot, build the native bridge, prepare Unity, and start the development server with guest creation enabled. Run Unity with `-batchmode -projectPath <checkout>/unity -executeMethod Game.Editor.DeviceAuthRuntimeProbe.Run` and these arguments:

- `--device-auth-probe-endpoint http://127.0.0.1:<port>` (loopback only)
- `--device-auth-probe-id <unique-run-id>` (letters, digits and hyphens; at most 64 characters)
- `--device-auth-probe-output <checkout>/artifacts/login/<run-id>.txt` (must not already exist)
- `--device-auth-probe-head <full-tested-SHA>`
- `-logFile <checkout>/artifacts/login/<run-id>.log`

Do not pass `-quit`: the driver exits after its asynchronous checks. It uses a distinct credential scope, refuses a populated scope, and does not inspect the normal player's account. It validates public key generation, native signing, secure save/reload, real guest creation, a fresh challenge/device sign-in even when access is cached, deletion, restart without automatic restoration, explicit same-key restoration and restored-session persistence. The resulting test account remains restored; its private key and session remain only in the OS vault. The report contains no credentials or HTTP payloads.

A failure is evidence, not permission to reset the scope or change the run ID and repeat an unchanged cause. Preserve the stage, exception type and numeric Apple OS status, share failures with other agents, and follow the shared-Mac two-failure stop rule. This Editor probe does not establish Android/iOS device behavior or UI usability.
