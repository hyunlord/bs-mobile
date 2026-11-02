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
