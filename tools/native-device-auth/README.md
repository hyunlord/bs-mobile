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


## Development memory backend (authorized 2026-11-02)

Mac Editor/Development selects the memory backend at compile time when using an account endpoint. It does not catch a Keychain failure and fall back. The UI always identifies development-only temporary storage and account loss at process exit. Only loopback endpoints are accepted in this prototype. Non-Mac development platforms currently fail as unsupported; signed-phone real Keychain/Keystore validation remains separate.

Build Editor support with `SOW_DEV_AUTH=1 bash tools/native-device-auth/build-macos.sh`. `unity-tests.sh` and `check-unity.sh` do this automatically. `build-mac.sh` derives the native flag from `UNITY_MAC_DEVELOPMENT`, so Release always compiles without `SowDevelopmentIdentity`, regardless of an inherited environment flag. The C# development vault type is also excluded unless `UNITY_EDITOR || DEVELOPMENT_BUILD`; the release assembly checker rejects that type.

The temporary software P-256 key and session are held in native memory under a separate endpoint/isolation scope. No Keychain, files, PlayerPrefs or private-key export are used. Memory inspection by a debugger is not prevented. Recreating a client in the same process retains its deletion marker; process exit loses the key, token and marker, leaving the development account inaccessible. This cannot validate cross-process recovery, OS security or hardware non-extractability.

To exercise the actual local HTTP lifecycle through the clean Unity TestRunner path, supply `SOW_DEV_AUTH_PROBE_ENDPOINT`, a unique `SOW_DEV_AUTH_PROBE_ID`, and a new `SOW_DEV_AUTH_PROBE_OUTPUT` path while filtering EditMode test `Tests.EditMode.DevelopmentDeviceIdentityTests.DevelopmentIdentitySignsAndOptionallyVerifiesLocalServerLifecycle`. Without these environment values, this test only proves development key generation/signing; it does not claim server coverage. The optional report labels `development-memory-vault-and-local-server` and `process-persistence: NOT_SUPPORTED`. The original native failure record remains unchanged and is not retried by this development probe.
