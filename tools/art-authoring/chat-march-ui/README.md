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
