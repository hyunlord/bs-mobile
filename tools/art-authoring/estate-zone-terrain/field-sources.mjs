// Approved field additions only: river r3 in the same Release is not selected.
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { basename, resolve } from "node:path";
import { pathToFileURL } from "node:url";
const riverMetadata = "dense-river-r2-placement.json";
const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
export function validateFieldSelection(selection, dense) {
    if (selection.riverMetadata !== riverMetadata)
        throw new Error("Only approved river r2 may be selected");
    const river = dense.assets.filter((a) =>
        ["registered-chunk", "context-module"].includes(a.projection),
    );
    if (
        river.length !== 92 ||
        river.some(
            (a) =>
                a.metadata !== riverMetadata ||
                !a.file.startsWith("dense-river-r2-"),
        )
    )
        throw new Error("River r2 identities changed or river r3 was selected");
    if (
        selection.assets.length !== 72 ||
        new Set(selection.assets.map((a) => a.file)).size !== 72
    )
        throw new Error("Expected 72 unique field alternatives");
    const combinations = new Set();
    for (const a of selection.assets) {
        if (
            !Number.isInteger(a.shape) ||
            a.shape < 1 ||
            a.shape > 6 ||
            !["S", "M", "L"].includes(a.size) ||
            ![1, 2].includes(a.scale) ||
            !["bright", "dark"].includes(a.state)
        )
            throw new Error("Invalid field form, size, scale or state");
        const expectedFile = `variety-r8-field-between-${String(a.shape).padStart(2, "0")}-${a.size}-${a.state}-r1-${a.scale}x.png`;
        if (
            a.file !== basename(a.file) ||
            a.file !== expectedFile ||
            !/^[a-f0-9]{64}$/.test(a.sha256)
        )
            throw new Error("Invalid field identity");
        const factor = { S: 1, M: 1.5, L: 2 }[a.size] * a.scale;
        for (const [name, base] of [
            ["canvas", [80, 52]],
            ["pivotTopLeft", [40, 32]],
            ["floorPixels", [64, 32]],
        ]) {
            if (
                JSON.stringify(a[name]) !==
                JSON.stringify(base.map((v) => v * factor))
            )
                throw new Error(`Invalid field ${name}`);
        }
        if (a.projection !== "isometric-sprite")
            throw new Error("Invalid field projection");
        combinations.add(`${a.shape}/${a.size}/${a.state}/${a.scale}`);
    }
    if (combinations.size !== 72) throw new Error("Missing field combination");
}
export function selectFieldVariants(selection, dense, size, scale, state) {
    validateFieldSelection(selection, dense);
    if (
        !["S", "M", "L"].includes(size) ||
        ![1, 2].includes(scale) ||
        !["bright", "dark"].includes(state)
    )
        throw new Error("Invalid field selection request");
    // No general-prop fallback: the field slot is exclusively the approved r3 set.
    return selection.assets
        .filter(
            (a) => a.size === size && a.scale === scale && a.state === state,
        )
        .sort((a, b) => a.shape - b.shape);
}
export function buildFieldSources(directory, dense) {
    const read = (name) => readFileSync(resolve(directory, name));
    const original = JSON.parse(read("VARIETY-FIELDS-r3.json"));
    const release = new Map(
        JSON.parse(read("release-assets.json")).assets.map((a) => [a.name, a]),
    );
    const selection = {
        release: "estate-r8-terrain-488-20261028",
        status: "user-approved-fields-r3-not-runtime-imported",
        riverMetadata,
        excludedRiver: "variety-river-r3: explicitly not adopted by user",
        metadata: [
            "VARIETY-FIELDS-r3.json",
            "VARIETY-PIECES-r3.md",
            "VARIETY-PROVENANCE-r3.json",
        ].map((file) => ({ file, sha256: hash(read(file)) })),
        originalStatus: original.status,
        anchorQualification: original.method,
        runtimeSelection:
            "Six forms by three nominal sizes; choose one resolution and light state. Field slots exclusively select r3, never also general r2 props; general props are retained for non-field slots only. River stays r2. No r3 scene placements are imported.",
        assets: original.files.map((a) => ({
            variantId: `field-between-${String(a.shape).padStart(2, "0")}-${a.size}`,
            file: a.file,
            sha256: a.sha256,
            shape: a.shape,
            size: a.size,
            state: a.state,
            scale: a.scale,
            canvas: a.canvas,
            pivotTopLeft: a.floorCenterTopLeft,
            floorPixels: a.floorPixels,
            projection: "isometric-sprite",
            metadata: "VARIETY-FIELDS-r3.json",
            sourceSHA256: a.sourceSHA256,
        })),
    };
    validateFieldSelection(selection, dense);
    for (const a of selection.assets)
        if (release.get(a.file)?.digest !== `sha256:${a.sha256}`)
            throw new Error(`Release digest mismatch: ${a.file}`);
    return selection;
}
if (
    process.argv[1] &&
    import.meta.url === pathToFileURL(resolve(process.argv[1])).href
) {
    if (process.argv.length !== 3)
        throw new Error(
            "Usage: node field-sources.mjs downloaded-metadata-directory",
        );
    const dense = JSON.parse(
        readFileSync(new URL("./dense-source-assets.json", import.meta.url)),
    );
    console.log(
        JSON.stringify(buildFieldSources(process.argv[2], dense), null, 2),
    );
}
