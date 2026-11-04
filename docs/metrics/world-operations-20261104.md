# World recovery and AOI measurement — 2026-11-04

Status: local PostgreSQL/TestServer measurement, not a production capacity or network latency gate.

The measured implementation is commit `613baa4c1f53c0973b14493d8da35016dc914688` on the stacked server-only world operations branch. The content map hash is `3cbeb5a76d758a6e4b3e441eaab4f8133ccb716263f506338495445800c4081d`. The 512×512 map has 32×32 chunks. All cities were allocated through AccountStore.CreateAsync and checked for unique, passable, approved starting positions.

Environment: Apple M4 Max, arm64, macOS 27.0.1, .NET SDK 10.0.403/runtime 10.0.14, PostgreSQL 17.11 in the owned codex-world container on port 55443. PostgreSQL max_connections remained 100; fixture/host pools were 4 and the schema administration pool was 1. A lightweight connection observer ran every 0.5 seconds. No Unity/native measurement ran concurrently under the shared lock.

## Reproduction

Use the shared-Mac daytime lock and an isolated PostgreSQL database. SERVER_TEST_POSTGRES comes from the caller's environment and must not be committed or printed. From server/:

```sh
WORLD_OPERATIONS_MEASURE=1 \
WORLD_MEASURE_ACCOUNTS=10000 \
WORLD_MEASURE_MARCHES=1000 \
WORLD_MEASURE_BATCH_SIZES=1,32,128,512 \
WORLD_MEASURE_OUTPUT=/absolute/owned/output \
dotnet test tests/SowSiege.Server.Tests --configuration Release \
  --filter FullyQualifiedName~WorldOperationsMeasurements
```

Without explicit opt-in, both measurement facts are skipped while normal correctness tests still run. Counts and output path must be supplied. A smoke execution used 16 accounts, 16 marches and budgets 1/32; it passed both facts. Production content, rate limiting and database connection limits were unchanged.

## Interpretation and limits

AOI reports SQL EXPLAIN ANALYZE, direct store calls and authenticated ASP.NET TestServer requests separately. HTTP includes authentication, the world recovery barrier, query, serialization and the response body; it excludes TCP/TLS. Dense/empty/boundary cases have one first call and 25 warm calls. A 256-chunk sweep and 32 requests in groups of four are additional samples. The shipped 60-request/minute limiter stays enabled, with at most 48 offered requests/minute; pacing waits are separate rows and count toward overall elapsed service throughput. The API is polling, not a streaming subscription.

Recovery issues 1,000 actual marches against one resource target, advances server time by 30 days and drains the real persisted event queue with budgets 1/32/128/512. This is contention on one finite resource: one collector can exhaust it, while competing marches return. It covers arrival, return, actual gathering exhaustion and respawn, not 1,000 independent collectors or PvP throughput. Account/training fixture preparation and dispatch are setup, excluded from drain timing. The fixture compares final troop, wallet and held-material outcomes in deterministic allocation order between budgets and checks all troops returned, no pending event, one retirement and the expected new target generation.

Each selected event consumes one slot, including combat replay attempts that lose optimistic validation. The measurement itself is gathering-only; separate runtime tests cover direct and automatic combat batches. Batch size 128 remains a provisional operational default, not an optimum or SLA. One run per budget and one machine cannot establish production capacity. An injected losing combat CAS and an exact final-admission clock race remain explicit test gaps.

Failures were retained: two missing test namespace imports, then two pool1 AOI failures caused by the real account purge worker holding the world connection while renting another one. The fix reuses the barrier transaction through AOI/Auth/Alliance paths and releases startup migration connections before recovery. Both hosted services remain enabled in the pool1 regression. Invalid-signature nonce commits, per-account purge transactions and fresh-transaction alliance retries are preserved.

## Results

Normal server suite: **372 passed, 0 failed, 2 explicit measurement skips**. Smoke measurements: **2/2 passed**. Full measurements: **2/2 passed**, 12m11s combined, including pacing/setup/verification. PostgreSQL had only the observer connection after fixture disposal.

| Recovery budget | Committed events | Drain seconds | Events/s | Batch p95 ms |
| --- | ---: | ---: | ---: | ---: |
| 1 | 2001 | 23.715 | 84.38 | 15.633 |
| 32 | 2001 | 18.871 | 106.03 | 336.346 |
| 128 | 2001 | 19.938 | 100.36 | 1362.653 |
| 512 | 2001 | 19.263 | 103.88 | 5111.734 |

All four budgets produced the same final troop/wallet/held-material outcomes, 2,001 committed scheduling events, 1,000 returned marches, restored troop availability, exhausted stock credits and generation 2. The 128 default remains provisional: its measured single-batch p95 was 1.363s, versus 0.336s for 32. Total catch-up time did not improve monotonically with larger batches; these single-run results do not identify an optimum.

| AOI case | Samples | Store p95 ms | HTTP p95 ms |
| --- | ---: | ---: | ---: |
| dense-warm | 25 | 3.274 | 11.693 |
| empty-warm | 25 | 1.640 | 7.127 |
| boundary-warm | 25 | 1.874 | 5.663 |
| sweep | 256 | 2.071 | 7.025 |
| concurrent4 | 32 | N/A | 35.330 |

The dense chunk held 1,024 cities; SQL execution was 1.372ms (planning 0.579ms). Empty/boundary SQL execution was 0.112/0.032ms. First authenticated dense request was 64.310ms and is separate from warm samples. HTTP completed 366 successful requests, with 424.875s of explicit rate-limit pacing waits. Those waits are not part of per-request p95, and per-request p95 must not be presented as offered throughput. Account allocation/setup took 45.773s.

The compact [CSV](world-operations-20261104.csv) retains measured aggregates, and [provenance](world-operations-20261104-provenance.json) records source/content/runtime and setup. Full successful raw logs and duplicate rendered reports are not uploaded.
