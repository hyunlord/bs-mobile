import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { validate, plan, hash, verifySourceFiles } from "./plan.mjs";
const read = (p) => JSON.parse(readFileSync(new URL(p, import.meta.url)));
const contract = read("./contract.json"),
    sources = read("./source-assets.json");
const config = read(
    "../../../data/opt-in/account-estate/m3a-prototype-r8.json",
);
const clone = (x) => structuredClone(x);
const run = (c = contract, state = {}) => plan(c, sources, config, state);
test("all 13 zones and 64 approved source records validate", () => {
    validate(contract, sources, config);
    assert.equal(sources.assets.length, 64);
    assert.equal(contract.zones.length, 13);
    assert.equal(contract.defaultBuildingWidthPx, 75);
    assert.equal(contract.minimumBuildingWidthPx, 50);
});
test("Astra FNV matches known vectors and UTF16 arithmetic", () => {
    assert.equal(hash(""), 2166136261);
    assert.equal(hash("hello"), 1335831723);
});
test("order, camera and review zoom do not change map candidates", () => {
    const c = clone(contract);
    c.zones.reverse();
    c.themes.reverse();
    for (const t of c.themes) t.variants.reverse();
    assert.deepEqual(
        run(c, { camera: [9, 2], zoom: 75 }),
        run(contract, { camera: [0, 0], zoom: 50 }),
    );
});
test("opening only changes bright/dark selection, never coordinates", () => {
    const a = run();
    const b = run(contract, {
        openZoneIds: config.zones.map((z) => z.id).reverse(),
    });
    assert.deepEqual(
        a.map(({ file, state, ...x }) => x),
        b.map(({ file, state, ...x }) => x),
    );
    assert.ok(a.every((x) => x.state === "dark"));
    assert.ok(b.every((x) => x.state === "bright"));
});
test("historical profiles emit nothing", () => {
    for (const v of [3, 4, 5, 6, 7])
        assert.deepEqual(
            plan(
                contract,
                sources,
                { ...config, version: `m3a-prototype-r${v}` },
                {},
            ),
            [],
        );
});
test("whole footprints stay inside zone and reject overlapping reservation", () => {
    const original = run();
    assert.ok(original.length > 0);
    const p = original[0];
    const filtered = run(contract, { reserved: [p.footprint] });
    assert.ok(!filtered.some((x) => x.key === p.key));
    for (const x of original) {
        const z = config.zones.find((z) => z.id === x.zoneId),
            r = x.footprint;
        assert.ok(
            r.x >= z.x &&
                r.y >= z.y &&
                r.x + r.width <= z.x + z.width &&
                r.y + r.height <= z.y + z.height,
        );
    }
});
test("malformed IDs, pairs, scalar settings and source geometry fail closed", () => {
    for (const mutate of [
        (c) => c.zones.pop(),
        (c) => (c.zones[0].theme = "missing"),
        (c) => (c.themes[0].density = NaN),
        (c) => (c.themes[0].spacingCells = 0),
        (c) => (c.reviewBuildingWidthsPx = [50]),
        (c) => (c.defaultBuildingWidthPx = 60),
    ]) {
        const c = clone(contract);
        mutate(c);
        assert.throws(() => validate(c, sources, config));
    }
    const s = clone(sources);
    s.assets.pop();
    assert.throws(() => validate(contract, s, config));
    const s2 = clone(sources);
    s2.assets[0].pivotTopLeft = [999, 0];
    assert.throws(() => validate(contract, s2, config));
});
test("invalid reservations rejected instead of ignored", () => {
    assert.throws(() =>
        run(contract, { reserved: [{ x: 0, y: 0, width: -1, height: 1 }] }),
    );
    assert.throws(() => run(contract, { openZoneIds: ["wrong"] }));
});
test("source verification rejects missing PNGs", () =>
    assert.throws(() =>
        verifySourceFiles(sources, "/nonexistent/terrain-test"),
    ));

test("CLI exports candidates with honest classification and rejects extra flags", () => {
    const args = [
        "tools/art-authoring/estate-zone-terrain/plan.mjs",
        "tools/art-authoring/estate-zone-terrain/contract.json",
        "tools/art-authoring/estate-zone-terrain/source-assets.json",
        "data/opt-in/account-estate/m3a-prototype-r8.json",
    ];
    const out = JSON.parse(execFileSync(process.execPath, args));
    assert.equal(
        out.classification,
        "OFFLINE_CANDIDATES_NOT_RUNTIME_OR_DENSITY_PROOF",
    );
    assert.ok(out.candidates.length > 0);
    assert.throws(() =>
        execFileSync(process.execPath, [...args, "--bad"], { stdio: "pipe" }),
    );
});
test("source verifier rejects stale SHA, wrong canvas, nonPNG and traversal", () => {
    const dir = mkdtempSync(join(tmpdir(), "sow-495-"));
    try {
        const bytes = Buffer.from(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5wAAAABJRU5ErkJggg==",
            "base64",
        );
        writeFileSync(join(dir, "one.png"), bytes);
        const a = {
            file: "one.png",
            canvas: [1, 1],
            sha256: createHash("sha256").update(bytes).digest("hex"),
        };
        assert.equal(verifySourceFiles({ assets: [a] }, dir).length, 1);
        for (const changed of [
            { ...a, sha256: "0".repeat(64) },
            { ...a, canvas: [2, 1] },
            { ...a, file: "../" + dir.split("/").at(-1) + "/one.png" },
        ])
            assert.throws(() => verifySourceFiles({ assets: [changed] }, dir));
        writeFileSync(join(dir, "one.png"), "not png");
        assert.throws(() => verifySourceFiles({ assets: [a] }, dir));
    } finally {
        rmSync(dir, { recursive: true });
    }
});
test("future Unity consumer must still test projected bounds; reservations do not claim it", () => {
    assert.equal(contract.candidateOnly, true);
    const a = run();
    for (let i = 0; i < a.length; i++)
        for (let j = i + 1; j < a.length; j++) {
            const x = a[i].footprint,
                y = a[j].footprint;
            assert.ok(
                !(
                    x.x < y.x + y.width &&
                    y.x < x.x + x.width &&
                    x.y < y.y + y.height &&
                    y.y < x.y + x.height
                ),
            );
        }
    const s = clone(sources);
    s.assets.reverse();
    assert.deepEqual(plan(contract, s, config, {}), a);
});

test("building changes hide catalog entries without refilling quota", () => {
    const c = clone(contract);
    for (const t of c.themes) {
        t.density = 1;
        t.maxCount = 2;
    }
    const before = run(c);
    const r = before[0].footprint;
    const hit = (p) =>
        p.footprint.x < r.x + r.width &&
        r.x < p.footprint.x + p.footprint.width &&
        p.footprint.y < r.y + r.height &&
        r.y < p.footprint.y + p.footprint.height;
    assert.deepEqual(
        run(c, { reserved: [r] }),
        before.filter((p) => !hit(p)),
    );
});
test("approved default75/min50 and both review widths preserve placement", () => {
    for (const value of [null, 50, 60]) {
        const c = clone(contract);
        c.defaultBuildingWidthPx = value;
        assert.throws(() => run(c));
    }
    const invalid = clone(contract);
    invalid.minimumBuildingWidthPx = 49;
    assert.throws(() => run(invalid));
    assert.deepEqual(run(contract, { zoom: 75 }), run(contract, { zoom: 50 }));
    const s = clone(sources);
    s.assets.find(
        (a) => a.variantId === "forest-cluster-01" && a.state === "dark",
    ).projection = "flat-uv";
    assert.throws(() => validate(contract, s, config));
});

test("seeded small-prop spacing and scale stay bounded and hide-only", () => {
    const dense = read("./dense-source-assets.json");
    const propAssets = dense.assets.filter(
        (a) => a.projection === "isometric-sprite" && a.scale === 1,
    );
    const s = clone(sources);
    s.assets.push(...propAssets);
    const c = clone(contract);
    const t = c.themes.find((t) => t.id === "village");
    Object.assign(t, {
        mode: "candidate-plan",
        spacingChoices: [3, 4],
        scaleRange: [0.85, 1],
        density: 1,
        maxCount: 16,
        variants: [...new Set(propAssets.map((a) => a.variantId))].map(
            (variantId) => ({ variantId, reserveCells: [2, 2] }),
        ),
    });
    const first = plan(c, s, config);
    const props = first.filter((p) => p.theme === "village");
    assert.ok(props.length > 1);
    assert.ok(props.every((p) => p.scale >= 0.85 && p.scale <= 1));
    assert.ok(new Set(props.map((p) => p.scale)).size > 1);
    t.spacingChoices.reverse();
    assert.deepEqual(plan(c, s, config, { zoom: 50 }), first);
    const r = props[0].footprint;
    const hit = (p) =>
        p.footprint.x < r.x + r.width &&
        r.x < p.footprint.x + p.footprint.width &&
        p.footprint.y < r.y + r.height &&
        r.y < p.footprint.y + p.footprint.height;
    assert.deepEqual(
        plan(c, s, config, { reserved: [r] }),
        first.filter((p) => !hit(p)),
    );
    for (const value of [[], [0], [-1], [NaN]]) {
        t.spacingChoices = value;
        assert.throws(() => plan(c, s, config));
    }
    t.spacingChoices = [3, 4];
    t.scaleRange = [0.8, 1.1];
    assert.throws(() => plan(c, s, config));
});
test("dense sources preserve all120 alternatives and explicit anchors", () => {
    const d = read("./dense-source-assets.json");
    assert.equal(d.assets.length, 120);
    assert.equal(new Set(d.assets.map((a) => a.file)).size, 120);
    assert.equal(d.assets.filter((a) => a.scale === 1).length, 60);
    for (const a of d.assets) {
        assert.match(a.sha256, /^[a-f0-9]{64}$/);
        assert.ok(a.pivotTopLeft.every((v, i) => v >= 0 && v <= a.canvas[i]));
        if (a.projection === "registered-chunk")
            assert.deepEqual(a.pivotTopLeft, [0, 0]);
    }
    assert.equal(
        d.scenes.classification,
        "ART_COMPOSITION_NOT_VALIDATED_GAME_PLACEMENT",
    );
});

test("field r3 selection covers six forms and three sizes without general-prop fallback", async () => {
    const { validateFieldSelection, selectFieldVariants } =
        await import("./field-sources.mjs");
    const field = read("./field-source-assets.json");
    const dense = read("./dense-source-assets.json");
    validateFieldSelection(field, dense);
    for (const size of ["S", "M", "L"])
        for (const scale of [1, 2])
            for (const state of ["bright", "dark"]) {
                const selected = selectFieldVariants(
                    field,
                    dense,
                    size,
                    scale,
                    state,
                );
                assert.deepEqual(
                    selected.map((a) => a.shape),
                    [1, 2, 3, 4, 5, 6],
                );
                assert.ok(
                    selected.every(
                        (a) => a.metadata === "VARIETY-FIELDS-r3.json",
                    ),
                );
                assert.ok(
                    selected.every(
                        (a) => !dense.assets.some((b) => a.file === b.file),
                    ),
                );
            }
    assert.throws(() => selectFieldVariants(field, dense, "XL", 1, "bright"));
    const broken = clone(field);
    broken.assets[0].pivotTopLeft[0]++;
    assert.throws(() => validateFieldSelection(broken, dense), /pivot/);
    const duplicate = clone(field);
    duplicate.assets[0] = clone(duplicate.assets[1]);
    assert.throws(() => validateFieldSelection(duplicate, dense), /unique/);
});

test("field updates cannot opt into river r3 even when it shares the Release", async () => {
    const { validateFieldSelection } = await import("./field-sources.mjs");
    const field = read("./field-source-assets.json");
    const dense = read("./dense-source-assets.json");
    assert.throws(
        () =>
            validateFieldSelection(
                { ...field, riverMetadata: "variety-river-r3-placement.json" },
                dense,
            ),
        /river r2/,
    );
    const changed = clone(dense);
    const river = changed.assets.find(
        (a) => a.projection === "registered-chunk",
    );
    river.file = river.file.replace("dense-river-r2", "variety-river-r3");
    assert.throws(() => validateFieldSelection(field, changed), /river r3/);
    river.file = dense.assets.find(
        (a) => a.projection === "registered-chunk",
    ).file;
    river.metadata = "variety-river-r3-placement.json";
    assert.throws(() => validateFieldSelection(field, changed), /river r3/);
});
