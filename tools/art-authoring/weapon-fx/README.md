# Weapon game sprites

Puts Astra's approved weapon game sprites into `unity/Assets/Art/wave1a-weapon-fx.png` (#266, after the six weapon review
videos were rejected on 2026-10-19).

```sh
gh release download weapon-game-assets-20261019 -D /tmp/weapon-fx -p "*.png" -p weapon-fx-manifest-draft.json
node tools/art-authoring/weapon-fx/replace-cells.mjs /tmp/weapon-fx
node tools/wave1a-art.mjs "$PWD"
```

- Ten roles change pictures: `fx.iron_blade.slash`, `fx.canal_bow.arrow|streak|splash`, `fx.ward_orbit.shield|trail`,
  `fx.spore_fan.patch`, `fx.ditch_pike.pike|thrust` and `fx.granary_sling.sack`. Each delivered cell is copied unchanged into
  the cell the wave-1a manifest already gives that role; cell sizes, pivots and world sizes match the delivery.
- The slash canvas grew from 503x76 to 512x128, so it moves to a free slot (1412, 1284) and its old cell is cleared. The blade
  renderer's white-core band (`BladeSpec.SlashCoreFrom/To`) follows the new picture: rows 70-100, v .211-.453 (inner edge).
- `fx.granary_sling.aroma` is a proposed new role without a consumer and is not added. The roles Astra reused unchanged keep
  their cells.
- The script fails if any atlas pixel outside the replaced cells (and the cleared slash cell) changes. `replaced-cells.json`
  records the release, the cell hashes and the atlas hash.
