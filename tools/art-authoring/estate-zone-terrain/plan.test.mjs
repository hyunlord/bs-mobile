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
    assert.equal(contract.defaultBuildingWidthPx, null);
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
test("future single default config supports either reviewed width without placement changes", () => {
    for (const width of [75, 50]) {
        const c = clone(contract);
        c.defaultBuildingWidthPx = width;
        assert.deepEqual(run(c), run());
    }
    const s = clone(sources);
    s.assets.find(
        (a) => a.variantId === "forest-cluster-01" && a.state === "dark",
    ).projection = "flat-uv";
    assert.throws(() => validate(contract, s, config));
});
