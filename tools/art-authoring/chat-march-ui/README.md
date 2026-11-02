# Approved chat and march UI atlas (#640)

Source: [chat-march-ui-iron-20261031-r1](https://github.com/hyunlord/bs-mobile/releases/tag/chat-march-ui-iron-20261031-r1).
`sources.json` pins the downloaded PNG SHA-256, source dimensions and interpretation. All source files remain in the Release; runtime packing never paints, recolors, trims or resizes them.

```sh
gh release download chat-march-ui-iron-20261031-r1 -R hyunlord/bs-mobile \
  --pattern '*-2x.png' --dir artifacts/chat-march/source
node tools/art-authoring/chat-march-ui/pack.mjs
node tools/art-authoring/chat-march-ui/verify.mjs
```

31 delivered 2x parts are inventoried. 29 are registered: chat 2, march/target 27. Exclusions remain in the source inventory: old `reward-iron-r1` is superseded by #590 wallet iron r2; the proposed skirmish row is outside the GDD15 two-unit roster. Neither is registered. No source files are removed.

Chat uses a separate 128×56 texture so opening a message menu does not load the dormant world parts. March uses 2048×2252. Both are non-readable, no mipmaps, RGBA32 on Mac and ASTC4 on Android/iOS. Memory figures are estimates, not device measurements. Runtime loads each atlas only when a role requests it.

Only the plain target card and formation paper panel use 9-slice. Each retains 32 source pixels per edge (16 points at 2x), covering the photographed corner ornament. All composited headers, troop rows, icons and tracks stay Simple at their authored aspect. 9-slice is runtime geometry; source pixels remain identical. Actual resized panel rendering must still be checked in Unity.

The resource-target card is not a grade selector. Grade icons are registered only for future monster/stronghold displays; resource stock must come from the server. Registration does not provide stock, marching, gathering or combat APIs. Those bindings remain pending their server contracts.

## Live Unity capture (pending execution)

`Game.AllianceChatCapture.AllianceChatCapture.Start` runs in the Editor with an
explicit empty `ALLIANCE_CAPTURE_DIR`, a private `ALLIANCE_CAPTURE_TOKEN_FILE`,
`ALLIANCE_CAPTURE_MESSAGE_ID`, and `ALLIANCE_CAPTURE_AUTHOR_ID`. The IDs must be
an actual visible message and its second account author on the local server.
Create/join/post through the existing prototype/API setup before launching;
no wallet, message or identity is fabricated by the capture runner. The server
endpoint uses the existing Barracks connection configuration.

The runner connects via the real settings screen and navigates the Alliance
route, then captures report receipt, block filtering and unblock restoration.
The final two captures are explicitly asset QA: source-density and resized
paper panels, not implemented world gameplay. They verify the 32-pixel border
appearance in actual Unity. All capture work requires the shared Mac slot and
lock; a written harness is not execution or visual evidence. Keep bearer files
private and exclude them from screenshots, text sidecars and Release uploads.

Set `ALLIANCE_CAPTURE_DEVICE_LOGIN=1` with `SOW_ACCOUNT_SERVER` and
`ALLIANCE_CAPTURE_ACCOUNT_ID` to use an already provisioned native device
identity instead of the private prototype bearer file. This mode signs in
through the actual account UI and enters Alliance directly without visiting
Barracks first, exercising connection destination restoration. Provision the
visible message for that exact server account beforehand. For the restart
case, preserve that account's exact pending alliance journal between Editor
runs; verify the server receives the same command ID/body and hydration occurs
before the Alliance route resumes. This is planned runtime evidence, not a
claim that the harness has already run.

## Prototype restart fault injection (pending execution)

The native device-auth route is separately blocked on the current unsigned
Unity host by OSStatus `-34018` (`errSecMissingEntitlement`). Do not repeat it
unchanged or claim prototype HTTP as native-auth evidence. Device capture can
also reuse an already valid in-memory token, which is not a fresh challenge.

For real server deduplication evidence, start `restart-proxy.mjs` with
`ALLIANCE_PROXY_UPSTREAM=http://127.0.0.1:5080`, a distinct
`ALLIANCE_PROXY_PORT=5081`, and a new `ALLIANCE_PROXY_LOG` inside the owned
artifact directory. Point Unity's existing prototype `--server-url` at
`http://127.0.0.1:5081`, use the existing private prototype token file, and set
`ALLIANCE_CAPTURE_DROP_REPORT=1` (device-login mode must be unset).

The proxy forwards the actual report to the real server and consumes its
successful receipt, then drops that one response. The UI must expose the
uncertain state. The Editor capture recreates the transport instance without
removing the durable journal, registers the alliance reader, and reconnects
through the normal prototype `Connect` coroutine. The proxy checks identical
command ID, whole request SHA-256 and whole deduplicated response SHA-256,
then requires a full `/v1/account` projection after replay. The capture checks
`AccountReady`, resolved pending state and the real Alliance destination
before recording the receipt screenshot. It then continues block/unblock and
paper captures. A separate failure screenshot `03b` documents the injection.

This is a **transport-instance restart with fault injection**, not an OS/app
process restart or native credential test. The proxy records no headers,
bearer, request plaintext or server response plaintext. Retain any failed
log/screens as failures. Stop only this owned proxy after the capture; its
final log records replay/hydration booleans. Native first-entry remains blocked;
synthetic-auth route tests, if used, must be labeled separately.

### Two Editor processes

Use the stronger process-persistence scenario for the runtime gate. Keep the
owned fault proxy running across both Editor invocations. In both invocations
set the same absolute `ALLIANCE_CAPTURE_PENDING_ROOT` (a new owned directory),
`ALLIANCE_CAPTURE_CHECKPOINT` (a new owned JSON path), proxy endpoint and private
prototype token file. Set `ALLIANCE_CAPTURE_DROP_REPORT=1` in both; keep native
device-login mode unset. Use distinct empty output directories for A and B.

1. Phase A: `ALLIANCE_CAPTURE_RESTART_PHASE=A`. The actual UI sends the report,
   captures the dropped-response uncertainty and writes a checkpoint containing
   only Editor PID and command SHA-256. `EditorApplication.Exit` ends this own
   process normally, leaving its ordinary durable journal intact. Phase A
   success is not overall restart success.
2. Wait for that Editor process to exit normally, then launch a fresh Editor
   process with `ALLIANCE_CAPTURE_RESTART_PHASE=B`. Before connecting, the runner
   requires a different PID and the exact checkpoint command hash loaded from
   disk. It registers the alliance decoder, uses ordinary `Connect` to retry
   the existing journal, then checks full account hydration and the actual
   Alliance screen. It records the receipt, block/unblock and two panel sizes.
3. Compare proxy evidence: original/replay command ID and body hash identical,
   deduplicated response identical, and successful full account GET after replay.
   Phase B records both process IDs, the original command hash and final route.

Both phase logs and screenshots are retained independently. This tests an
actual orderly **Editor process shutdown/restart**; it does not prove mobile OS
termination, native auth or device storage. Leaving the phase variable unset
still selects the earlier single-process transport recreation scenario.

Runtime correction (2026-11-02): the first socket-disconnect attempt was
immediately retried by Unity HTTP, so its receipt resolved before shutdown.
That attempt is preserved as FAIL for the restart scenario (although exact
server deduplication was observed). The proxy now discards the real successful
report receipt and returns an explicitly injected **HTTP 503** once. This
models a server-committed command whose successful reply did not reach the
client without relying on a connection failure that the HTTP layer masks.
Evidence uses `discard-successful-report-receipt-return-503`; the existing
`DROP_REPORT` environment flag selects this corrected fault scenario.
