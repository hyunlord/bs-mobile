# #390 night verification stop — 2026-10-26

Status: FAILED. Repeated base-off batch-budget failure in two flow tests; stopped under the user rule. No native build/retry after stop.

## Unity tests
commit: 80a558b7d727f2b5d1609414230f6724a25f99e7
first-playable EditMode: passed 841, failed 0, skipped 25
first-playable PlayMode: passed 40, failed 1, skipped 44
wave-1a EditMode: passed 851, failed 0, skipped 15
wave-1a PlayMode: passed 70, failed 2, skipped 13
skipped in every profile: 2 unexplained, 1 listed

## Failures

- First configure failed CS0234 (`Debug.Log` resolved to `Game.Debug`). Fixed only qualification to `UnityEngine.Debug.Log` in 80a558b; retry compiled successfully.
- first-playable PlayMode: AstraBReview initial capture cyanPixels=329160, anythingCompilingBefore=true, anythingCompilingAfter=true, precedingDomainReloadObserved=false. This records correlation, not a proven cause. Guard retained.
- wave-1a PlayMode: DevelopmentOptionSwitchesTheEstateTabToTheIsometricMapAndBack and HistoricalR6SaveKeepsItsBytesAndGridThroughTheRealEstateEntry both failed base-off batch budget: actual10 > expected9. Budget unchanged. Same failure twice: no further fix/retry.
- Both profiles skip two unexplained opt-in tests: ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes and MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures. No skip exceptions added.

## Captures / limits

Four r7 first/whole/construction/completed screenshots were generated during the successful fresh-account flow. Comparison sheets place approved keyart C LEFT and Editor capture RIGHT. Edge-foundation crop is a3x nearest-neighbour enlargement of the first-entry central area. They do not override failed suites or substitute for native evidence.

Native construction retry remains NOT RUN because the repeat-failure stop condition fired. Core, save rules, historical configs, batch threshold and cyan guard were not changed to pass. Both PRs remain draft; merge requires user approval.
