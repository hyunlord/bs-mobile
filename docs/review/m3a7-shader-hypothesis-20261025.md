# M3a-7 shader hypothesis: not reproduced, not confirmed

2026-10-25 21:43–21:47 KST. Authorized bounded follow-up to743d40c; PR386 remains draft. **Gate: partial / cause unconfirmed.** No production View, Core, config, save, or placement changes in this follow-up.

## Official behavior

Unity6000.6 documents a plain cyan dummy shader while Editor Game/Scene View variants compile with [EditorSettings.asyncShaderCompilation](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/EditorSettings-asyncShaderCompilation.html). This differs from [ShaderUtil.allowAsyncCompilation](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/ShaderUtil-allowAsyncCompilation.html), a call-site permission used in an older experiment. [anythingCompiling](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/ShaderUtil-anythingCompiling.html) reports current compiler activity, not whether the sampled render is complete. [WaitForEndOfFrame](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/WaitForEndOfFrame.html) does not run in Editor batchmode; no such unconditional wait was introduced.

## Experiment

The same two previously failing EstateMapFlowTests run with Editor-only logging immediately before and after each synchronous capture. CSV includes active page, Unity frame, original async setting, wait-entry compiler state, capture compiler states, cyan count. Pixel predicate unchanged: R<16/G>230/B>230. The optional wait is bounded at10seconds. Default mode remains baseline; waiting is not silently adopted as a proven fix.

| Condition | Runs | Flow tests | Captures | any compiling=true at capture | Cyan pixels |
|---|---:|---:|---:|---:|---:|
| Existing cache, async ON baseline |1|2/2|17|0|0|
| EditorSettings.asyncShaderCompilation=false |1|2/2|17|0|0|
| Async ON, wait up to10s for compiler idle |1|2/2|17|0|0|
| Own-project ShaderCache/db temporarily separated, async ON baseline |1|2/2|17|0|0|

All wait-entry states were already false; the wait run never exercised an active compiler wait. `elapsedThroughCaptureSeconds` includes readback/encoding/inspection and is **not shader wait duration**. Disabling async restored its prior true value in teardown; the following wait/cold run recorded true. Unity's EditorSettings serialization migration was reverted to exact pre-experiment bytes. Cache separation was performed with Unity closed under the owned Mac lock; original cache/db restored by trap. No global cache or other project was changed.

The local ShaderCache separation is not a guarantee that every Unity/driver/shared artifact was cold. No wider shared-cache invalidation was attempted. No shader source was modified to manufacture a variant.

## Conclusion and boundary

The proposed mechanism is documented and consistent with the old exact cyan colour, but **there is no current capture with both cyan and anythingCompiling=true**. The baseline also became normal. These runs therefore establish neither causal confirmation nor falsification. They do not erase the743d40c failures. No false+cyan occurred in this experiment; if it occurs later, stop under the user's explicit rule instead of adding another workaround.

Standalone0/1/30 capture was not performed: the current branch's estate first-entry observer is Editor-only, and the native autoplay harness does not expose an estate-map entry-frame capture route. Adding production automation solely for this probe would cross the requested test-only change boundary. Editor pixels are not claimed as Standalone evidence.

Building spacing, reclaimable-land composition, and new visual approval remain blocked on cyan resolution. Three screenshots are labelled diagnosis-only: current first entry, whole map, after construction versus C. They do not claim the requested new placement correction.

## Batching / safety

User acceptance of r6 structural14/10 (+2 landmarks) recorded. r3–r5 historical rows remain in the preceding report, not new budgets. Existing r6-only base limits and all-version landmark increment tests are unchanged.

All Unity runs used the shared-Mac lock before23:00; final run ended21:47 and lock released. No C05, no merge, no external app/process manipulation. Full two-profile/replay verification not rerun for this bounded hypothesis-only experiment. The only tested-code post-run change renames a CSV column to describe its measured interval honestly; no executable behavior changed.

Reproduction: export M3A7_SHADER_PROBE=baseline,off,or wait; prepare wave-1a and FoundationBuild.Configure; run EstateMapFlowTests.ASavedEstateBuildsClaimsAndSpecializesOnTheMap plus DevelopmentOptionSwitchesTheEstateTabToTheIsometricMapAndBack, respecting shared-Mac lock/time constraints. `off` restores original setting in UnityTearDown. Outputs: artifacts/m3a7-shader-{mode}/captures.csv and PlayMode.xml.
