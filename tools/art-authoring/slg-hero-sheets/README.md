# SLG hero sprite sheets

Offline packing of three SLG draft heroes (field_guide, river_warden, ruin_seeker) from two
Releases into one atlas PNG per hero under `unity/Assets/Art/`.

## Regeneration

```sh
gh release download hero-walk-world-status-20261030-r1 -R hyunlord/bs-mobile -D artifacts/slg-heroes
gh release download hero-basic-gem-20261030-r1        -R hyunlord/bs-mobile -D artifacts/slg-heroes
node tools/art-authoring/slg-hero-sheets/pack.mjs artifacts/slg-heroes
```

## Source files

### Release `hero-walk-world-status-20261030-r1`

| File | SHA-256 |
|------|---------|
| `field_guide-walk16-r1-1x.png` | `3fe7d6b21297b36382b234863970bfd1e2949eb700779086e085c68139d4b190` |
| `river-warden-walk16-r1-1x.png` | `80204a1698e86bbe27a862523b6801f582bddd76231e6e570da9b3c264b7fbab` |
| `ruin_seeker-walk16-r1-1x.png` | `ebcefb8f2b97c584f644cba1cd5638bf9e00523f39f0dc039589e9b058818362` |

Note: walk release uses `river-warden` (hyphen); basic-gem release uses `river_warden`
(underscore). The packer normalises both to `river_warden`.

### Release `hero-basic-gem-20261030-r1`

| File | SHA-256 |
|------|---------|
| `field_guide-idle6-r1-1x.png` | `970d5d5ecfc1e8861fbe97e750f78c2c897d9d3f6783b88b743d428aaf46796c` |
| `field_guide-hit3-r1-1x.png` | `f34ac39af886b489cdca9b9c550d1d58e3b00071b38b630055f39b0689246800` |
| `field_guide-death1-r1-1x.png` | `e4cac85793fbc5143bd735e0c19f0c07f0e21105d6e4f8ac49283449d510c268` |
| `river_warden-idle6-r1-1x.png` | `49ae06987b87040cdcb8023e34f8fc5fb2564a7a408d638b96092fa2fa2fbe9c` |
| `river_warden-hit3-r1-1x.png` | `e2357e266266f74ebd87e1b25156575dd7fb6f9426bd0d8b7b7b61077405f8f4` |
| `river_warden-death1-r1-1x.png` | `2ec33a2cc59c8d42588b803ab78f44d04f6f502a08bda55e295ca3c19f1a69fa` |
| `ruin_seeker-idle6-r1-1x.png` | `31e4ccaf25b0bc00c0e6b50942d54ef5797cb217634ce693ee4e5c7cd7a36935` |
| `ruin_seeker-hit3-r1-1x.png` | `2b002f44594ebdd7f20254031fd63dbb777a11979c371ade88318c9addc05f69` |
| `ruin_seeker-death1-r1-1x.png` | `90c854efe2dcc9b76d329cff1ca42028e42be7fa142942a5b6e9815f14627b00` |

## Atlas layout (per hero)

- Size: **2564 × 964** px at 1x (matches knight 1x scale)
- Padding: 2 px outer margin, no inter-row gap
- Cell: 320 × 240 px

| Atlas row | y start | Content |
|-----------|---------|---------|
| 0 | 2 | walk frames 0–7 |
| 1 | 242 | walk frames 8–15 |
| 2 | 482 | idle frames 0–5 |
| 3 | 722 | hit frames 0–2 |

Death role reuses the last hit cell (x=642, y=722, same rule as `frontier_knight`).

## Roles registered

Each hero gets four roles in both `first-playable-manifest.json` and `wave1a-manifest.json`:

```
hero.slg.field_guide.{walk,idle,hit,death}
hero.slg.river_warden.{walk,idle,hit,death}
hero.slg.ruin_seeker.{walk,idle,hit,death}
```

Bindings use `kind: "hero"` with `contentId: "slg:field_guide"` etc., matching the
`slg:*` content IDs in `data/opt-in/slg/content-draft-v1.json`.

## Design notes

- **Pivot**: foot line y=184 from top of cell → `pivot.y = 56/240 ≈ 0.2333`
- **worldSize**: matches `frontier_knight` (same 320×240 cell; will refine with art review)
- **frameMs**: walk=36 ms, idle=200 ms, hit=83 ms, death=83 ms (matches knight timing)
- **1x chosen**: knight runtime uses 1x atlases (atlas width 2564 = 8 × 320 + 4)
- **ASTC4**: Android + iOS override (textureFormat 48), same as `hero-knight.png.meta`
- SLG heroes are `[가설]` / draft; `runtimeEnabled: false` in content-draft-v1.json
