# r8 geometry proposal (documentation only)

One rectangle per zone; total **1664** cells. Start `(24,24,12,12)` and all ten r7 buildings remain unchanged. This is an implementable schematic approximation of the compact B direction, not registration of the earlier painted concept. The isometric outline is wider than the portrait painting because the64×32 projection and single-rectangle contract are retained.

| Zone | x,y | w×h | Cells | Production sites r7→r8 | ANY already-open neighbor |
|---|---|---|---:|---:|---|
| zone:start | 24,24 | 12×12 | 144 | starts open | zone:outskirts-north OR zone:outskirts-east OR zone:outskirts-south OR zone:outskirts-west |
| zone:outskirts-north | 20,20 | 20×4 | 80 | 2→2 | zone:start OR zone:outskirts-east OR zone:outskirts-west OR zone:inner-village OR zone:inner-northeast-wood OR zone:inner-fallow |
| zone:outskirts-east | 36,24 | 4×12 | 48 | 2→2 | zone:start OR zone:outskirts-north OR zone:outskirts-south OR zone:inner-fallow |
| zone:outskirts-south | 20,36 | 20×4 | 80 | 2→2 | zone:start OR zone:outskirts-east OR zone:outskirts-west OR zone:inner-village OR zone:inner-fallow OR zone:inner-ruins |
| zone:outskirts-west | 20,24 | 4×12 | 48 | 2→2 | zone:start OR zone:outskirts-north OR zone:outskirts-south OR zone:inner-village |
| zone:inner-village | 8,20 | 12×20 | 240 | 2→2 | zone:outskirts-north OR zone:outskirts-south OR zone:outskirts-west OR zone:inner-west-hill OR zone:inner-windmill |
| zone:inner-northeast-wood | 20,8 | 20×12 | 240 | 4→3 | zone:outskirts-north OR zone:inner-west-hill OR zone:inner-fields |
| zone:inner-west-hill | 12,10 | 8×10 | 80 | 4→3 | zone:inner-village OR zone:inner-northeast-wood |
| zone:inner-windmill | 12,40 | 8×9 | 72 | 4→3 | zone:inner-village OR zone:inner-ruins |
| zone:inner-fields | 40,10 | 8×10 | 80 | 4→3 | zone:inner-northeast-wood OR zone:inner-fallow |
| zone:inner-fallow | 40,20 | 12×20 | 240 | 4→3 | zone:outskirts-north OR zone:outskirts-east OR zone:outskirts-south OR zone:inner-fields OR zone:inner-riverside |
| zone:inner-ruins | 20,40 | 20×12 | 240 | 4→3 | zone:outskirts-south OR zone:inner-windmill OR zone:inner-riverside |
| zone:inner-riverside | 40,40 | 8×9 | 72 | 4→3 | zone:inner-fallow OR zone:inner-ruins |

Each non-start zone retains its r7 unlock cost60 timber and30 seconds. Seven outer-zone thresholds change4→3 in this DESIGN proposal because r7 defines only three producing building kinds (field, lumber camp, ironworks); existing2 thresholds remain2. This proposed threshold correction is not applied to game data. Neighbor alternatives are OR, while the existing production/cost/reserve/idle-work checks remain AND conditions. No recommended route replaces that rule. Corner-only contact is not adjacency.

## Shared edges

| A | B | Shared cells | Boundary |
|---|---|---:|---|
| zone:start | zone:outskirts-north | 12 | y=24, other-axis[24,36) |
| zone:start | zone:outskirts-east | 12 | x=36, other-axis[24,36) |
| zone:start | zone:outskirts-south | 12 | y=36, other-axis[24,36) |
| zone:start | zone:outskirts-west | 12 | x=24, other-axis[24,36) |
| zone:outskirts-north | zone:outskirts-east | 4 | y=24, other-axis[36,40) |
| zone:outskirts-north | zone:outskirts-west | 4 | y=24, other-axis[20,24) |
| zone:outskirts-north | zone:inner-village | 4 | x=20, other-axis[20,24) |
| zone:outskirts-north | zone:inner-northeast-wood | 20 | y=20, other-axis[20,40) |
| zone:outskirts-north | zone:inner-fallow | 4 | x=40, other-axis[20,24) |
| zone:outskirts-east | zone:outskirts-south | 4 | y=36, other-axis[36,40) |
| zone:outskirts-east | zone:inner-fallow | 12 | x=40, other-axis[24,36) |
| zone:outskirts-south | zone:outskirts-west | 4 | y=36, other-axis[20,24) |
| zone:outskirts-south | zone:inner-village | 4 | x=20, other-axis[36,40) |
| zone:outskirts-south | zone:inner-fallow | 4 | x=40, other-axis[36,40) |
| zone:outskirts-south | zone:inner-ruins | 20 | y=40, other-axis[20,40) |
| zone:outskirts-west | zone:inner-village | 12 | x=20, other-axis[24,36) |
| zone:inner-village | zone:inner-west-hill | 8 | y=20, other-axis[12,20) |
| zone:inner-village | zone:inner-windmill | 8 | y=40, other-axis[12,20) |
| zone:inner-northeast-wood | zone:inner-west-hill | 10 | x=20, other-axis[10,20) |
| zone:inner-northeast-wood | zone:inner-fields | 10 | x=40, other-axis[10,20) |
| zone:inner-windmill | zone:inner-ruins | 9 | x=20, other-axis[40,49) |
| zone:inner-fields | zone:inner-fallow | 8 | y=20, other-axis[40,48) |
| zone:inner-fallow | zone:inner-riverside | 8 | y=40, other-axis[40,48) |
| zone:inner-ruins | zone:inner-riverside | 9 | x=40, other-axis[40,49) |

## River and three bridges

The river remains presentation-only. All water cells are inside the1664-cell logical union, outside the whole start12×12 and outside all ten2×2 footprints. Bridges are visual overlays and create no new unlock or navigation condition. Width is a stepped two-cell ribbon; diagonal joins can be three columns wide.

| Bridge | Rectangle | Host zone | Water cells | Dry approach cells |
|---|---|---|---:|---:|
| bridge:north-wood | (28,16,7,1) | zone:inner-northeast-wood | 3 | 4 |
| bridge:east-fallow | (42,33,6,1) | zone:inner-fallow | 2 | 4 |
| bridge:south-riverside | (42,44,6,1) | zone:inner-riverside | 2 | 4 |

## Verification

```json
{
  "zoneCount": 13,
  "totalArea": 1664,
  "uniqueCells": 1664,
  "unionComponents": 1,
  "enclosedMissingCells": 0,
  "allZonesReachableAfter3ProductionSites": true,
  "configuredProducingBuildingKinds": [
    "m3a:field",
    "m3a:lumber_camp",
    "m3a:ironworks"
  ],
  "zonesReachableAfter2ProductionSites": [
    "zone:start",
    "zone:outskirts-north",
    "zone:outskirts-east",
    "zone:outskirts-south",
    "zone:outskirts-west",
    "zone:inner-village"
  ],
  "riverComponents": 1,
  "visualDryBankCellCounts": [
    1194,
    370
  ],
  "visualDryBanksConnectedByThreeBridges": true,
  "riverStartOverlap": 0,
  "riverBuildingOverlap": 0,
  "initialBuildingCount": 10,
  "buildingCells": 40,
  "bridges": 3
}
```

Generated with `node docs/design/m3-estate-r8/geometry-source.cjs`. This reads r7 and writes documentation only. Game data, Core, runtime water behavior, historicalr3–r7, and save format6 are unchanged.
