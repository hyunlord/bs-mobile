// Rebuild from downloaded, original Release metadata; does not import runtime art.
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
const digest = (bytes) => createHash("sha256").update(bytes).digest("hex");
export function buildDenseSources(directory) {
    const read = (name) => JSON.parse(readFileSync(resolve(directory, name)));
    const release = new Map(
        read("release-assets.json").assets.map((a) => [a.name, a]),
    );
    const metadataNames = [
        "DENSE-PIECES-r2.md",
        "DENSE-PROPS-final-r2.json",
        "DENSE-FOG-r2.json",
        "dense-river-r2-placement.json",
        "DENSE-SCENES-r2.json",
        "NATIVE-CAMERA-r1.json",
    ];
    const metadata = metadataNames.map((file) => ({
        file,
        sha256: digest(readFileSync(resolve(directory, file))),
    }));
    const assets = [];
    function add(a) {
        const expected = release.get(a.file)?.digest;
        if (!expected || expected !== `sha256:${a.sha256}`)
            throw new Error(`Release digest mismatch: ${a.file}`);
        assets.push(a);
    }
    for (const metadata of ["DENSE-PROPS-final-r2.json", "DENSE-FOG-r2.json"]) {
        for (const a of read(metadata).assets) {
            const fog = metadata === "DENSE-FOG-r2.json";
            add({
                variantId: a.id ?? a.file.replace(/-r2-[12]x\.png$/, ""),
                file: a.file,
                sha256: a.sha256,
                metadata,
                projection: fog ? "exterior-alpha-overlay" : "isometric-sprite",
                state: a.state ?? "exterior",
                scale: a.scale ?? (a.file.endsWith("-2x.png") ? 2 : 1),
                canvas: a.canvas,
                pivotTopLeft: a.pivotTopLeft,
                floorPixels: a.floorPixels ?? null,
                previewOpacity: a.previewOpacity ?? null,
            });
        }
    }
    const river = read("dense-river-r2-placement.json");
    for (const [category, list] of [
        ["registered-chunk", river.chunks],
        ["context-module", river.contextModules],
    ]) {
        for (const a of list)
            for (const scale of [1, 2]) {
                const file = a[`file${scale}x`];
                const sha256 = release
                    .get(file)
                    ?.digest?.replace(/^sha256:/, "");
                add({
                    variantId: a.id ?? `${a.role}-${a.state}`,
                    file,
                    sha256,
                    metadata: "dense-river-r2-placement.json",
                    projection: category,
                    state: a.state,
                    scale,
                    canvas: a.nativeSize.map((x) => x * scale),
                    pivotTopLeft:
                        category === "registered-chunk"
                            ? [0, 0]
                            : a.floorAnchorNative.map((x) => x * scale),
                    anchorMeaning:
                        category === "registered-chunk"
                            ? "top-left-native-origin"
                            : "source-floor-anchor",
                    nativeOrigin: a.nativeOrigin,
                    nativeSize: a.nativeSize,
                    floorAnchorNative: a.floorAnchorNative ?? null,
                    usage: a.usage ?? a.placement,
                });
            }
    }
    if (
        assets.length !== 120 ||
        new Set(assets.map((a) => a.file)).size !== 120
    )
        throw new Error("Expected 120 unique assets");
    return {
        release: "estate-r8-terrain-488-20261028",
        status: "approved-sources-not-runtime-imports",
        metadata,
        runtimeSelection:
            "Choose one scale and chunks OR validated context modules, never both at same location.",
        scenes: {
            placements: read("DENSE-SCENES-r2.json").placements.length,
            classification: "ART_COMPOSITION_NOT_VALIDATED_GAME_PLACEMENT",
        },
        nativeCamera: read("NATIVE-CAMERA-r1.json"),
        assets,
    };
}
if (
    process.argv[1] &&
    import.meta.url === pathToFileURL(resolve(process.argv[1])).href
) {
    if (process.argv.length !== 3)
        throw new Error(
            "Usage: node dense-sources.mjs downloaded-metadata-directory",
        );
    console.log(JSON.stringify(buildDenseSources(process.argv[2]), null, 2));
}
