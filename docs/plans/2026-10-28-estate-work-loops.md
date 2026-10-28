# Estate work loops implementation plan

Goal: approved hoe/hammer/sack sprites animate account-estate residents without Core, saved-state, economy or combat changes.
Prerequisite: #431 merged; create a NEW main-based issue/worktree/branch, do not reuse #431.

1. Asset contract: use hoe r2 (96x96, anchor30/83,8x200ms), hammer r1 (64x76,22/66,8x150ms), sack r1 (66x66,31.453125/59.5546875,8x300ms). Hoe combined dust sheet only, no duplicate dust. Preserve current atlases/old source in history; extend reproducible estate importer/spec and per-frame anchors. Source manifests' old pending words are historical; current user approval governs.
2. Cached deterministic resident plan: stable building ID, completed field edge hoe, active construction edge hammer replacing static pose. Total visible dynamic workers/carriers <=6, not6perkind. No production effect. Preserve static decoration on/off semantics.
3. Animation: stationary view caches Sprite arrays and updates only changed sprite. Sack uses same carrier identity and route; exactly one2.4s cycle at granary arrival before route resumes. Time is View-only and absolute/session-based so UI redraw does not restart sack. Route priority/selection sorted deterministically; hidden maps cannot mutate Core/account or create duplicate residents.
4. Tests before implementation: frame/anchor/provenance; completed-field vsLv0 eligibility; hammer replacement; globalcap6 inclcarrier; stableIDplacement; arrival phase boundaries, onecycle notperframe; rebuilding sameview time yieldssamepose; hidden/pause behavior explicit; no perframe LINQ/alloc/layout.
5. GDD05/07 update samePR (version/date/status) and report. m3-account-estate.md currently missing onmain (#360), do not silently copy speculative design.
6. Root-scheduled Unity slot only: bothprofiles+existing supplements;30s fixed60fps offline estate capture showshoe+hammer+sack resume,1x/stills; batchdelta prior/new samefixture; replay+savebyte parity. Do not call offlinevideo a realperformance measurement. Max6 and no perframe allocations must be inspected. DraftPR with exact source/CI and selectedRelease samples; no merge without user.

Existing touchpoints: EstateDecor.cs resident plan / static poses; PortraitMetaPanels.EstateMap.cs standing layer and EstateCarryView creation; EstateCarryView.cs cached images and depth crossings; EstateConstructionArt.cs EstateCarryRoute; tools/estate-art.{json,mjs}; EstateConstructionArtTests.cs / EstateDecorTests.cs; isolated PlayMode fixture + offline capture harness.
