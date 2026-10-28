// Offline candidate contract only. Unity must still reject projected art/road/door intersections at current state.
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { resolve, basename } from "node:path";
import { pathToFileURL } from "node:url";
const require = (condition, message) => {
    if (!condition) throw new Error(message);
};
const ordinal = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
const positive = (x) => Number.isFinite(x) && x > 0;
const pair = (a) =>
    Array.isArray(a) && a.length === 2 && a.every(Number.isFinite);
export function hash(s) {
    let h = 2166136261;
    for (let i = 0; i < s.length; i++)
        h = Math.imul(h ^ s.charCodeAt(i), 16777619);
    return h >>> 0;
}
function rectangle(r) {
    require(r &&
        Number.isFinite(r.x) &&
        Number.isFinite(r.y) &&
        positive(r.width) &&
        positive(r.height), "Invalid reservation rectangle");
}
const intersects = (a, b) =>
    a.x < b.x + b.width &&
    b.x < a.x + a.width &&
    a.y < b.y + b.height &&
    b.y < a.y + a.height;
export function validate(c, s, config) {
    require(c.version === "estate-zone-themes-v1" &&
        c.configVersion === "m3a-prototype-r8" &&
        c.candidateOnly === true, "Unsupported candidate contract");
    require(Number.isSafeInteger(c.seed), "Invalid seed");
    require(JSON.stringify(c.reviewBuildingWidthsPx) === "[75,50]" &&
        c.defaultBuildingWidthPx === 75 &&
        c.minimumBuildingWidthPx ===
            50, "Approved camera contract requires default75/minimum50");
    require(s.release ===
        "estate-r8-terrain-488-20261028", "Unapproved source Release");
    const ids = new Set(config.zones.map((z) => z.id));
    require(c.zones.length === 13 &&
        ids.size === 13 &&
        new Set(c.zones.map((z) => z.id)).size === 13 &&
        c.zones.every((z) => ids.has(z.id)), "13 exact zone IDs required");
    const assets = new Map();
    for (const a of s.assets) {
        require(typeof a.variantId === "string" &&
            a.variantId.length > 0 &&
            ["bright", "dark"].includes(a.state), "Bad source key/state");
        require(typeof a.file === "string" &&
            basename(a.file) === a.file &&
            a.file.endsWith(".png") &&
            /^[a-f0-9]{64}$/.test(a.sha256), "Invalid source identity");
        require(pair(a.canvas) &&
            a.canvas.every((x) => Number.isSafeInteger(x) && x > 0) &&
            pair(a.pivotTopLeft) &&
            a.pivotTopLeft.every(
                (x, i) => x >= 0 && x <= a.canvas[i],
            ), "Invalid source geometry");
        require(["flat-uv", "isometric-sprite"].includes(
            a.projection,
        ), "Invalid projection");
        const id = `${a.variantId}|${a.state}`;
        require(!assets.has(id), "Duplicate source state");
        assets.set(id, a);
    }
    for (const a of s.assets)
        require(assets.has(
            `${a.variantId}|${a.state === "bright" ? "dark" : "bright"}`,
        ), "Missing source state pair");
    const themes = new Map();
    for (const t of c.themes) {
        require(!themes.has(t.id), "Duplicate theme");
        themes.set(t.id, t);
        require(Number.isSafeInteger(t.spacingCells) &&
            t.spacingCells > 0 &&
            Number.isFinite(t.density) &&
            t.density >= 0 &&
            t.density <= 1 &&
            Number.isSafeInteger(t.maxCount) &&
            t.maxCount >= 0, "Invalid theme budget");
        require(t.spacingChoices === undefined ||
            (Array.isArray(t.spacingChoices) &&
                t.spacingChoices.length > 0 &&
                t.spacingChoices.every(
                    (x) => Number.isSafeInteger(x) && x > 0,
                )), "Invalid spacing choices");
        require(t.scaleRange === undefined ||
            (pair(t.scaleRange) &&
                t.scaleRange[0] > 0 &&
                t.scaleRange[0] <= t.scaleRange[1] &&
                t.scaleRange[1] <=
                    1), "Scale must stay within conservative footprint");
        require(["candidate-plan", "preserve-existing"].includes(
            t.mode,
        ), "Unknown mode");
        require(t.mode === "preserve-existing"
            ? t.variants.length === 0
            : t.variants.length > 0, "Invalid theme variants");
        require(new Set(t.variants.map((v) => v.variantId)).size ===
            t.variants.length, "Duplicate variant");
        for (const v of t.variants)
            require(["bright", "dark"].every(
                (state) =>
                    assets.get(`${v.variantId}|${state}`)?.projection ===
                    "isometric-sprite",
            ) &&
                pair(v.reserveCells) &&
                v.reserveCells.every(
                    (x) => Number.isSafeInteger(x) && x > 0,
                ), "Invalid variant/footprint");
    }
    require(c.zones.every((z) => themes.has(z.theme)), "Unknown zone theme");
    return { assets, themes };
}
export function plan(c, s, config, state = {}) {
    if (config.version !== c.configVersion) return [];
    const { assets, themes } = validate(c, s, config);
    const reserved = state.reserved ?? [];
    reserved.forEach(rectangle);
    const open = new Set(state.openZoneIds ?? []);
    require([...open].every((id) =>
        config.zones.some((z) => z.id === id),
    ), "Unknown open zone");
    const output = [];
    for (const mapping of [...c.zones].sort((a, b) => ordinal(a.id, b.id))) {
        const z = config.zones.find((z) => z.id === mapping.id),
            t = themes.get(mapping.theme);
        rectangle(z);
        if (t.mode === "preserve-existing") continue;
        const variants = [...t.variants].sort((a, b) =>
                ordinal(a.variantId, b.variantId),
            ),
            candidates = [];
        const spacing = [...(t.spacingChoices ?? [t.spacingCells])].sort(
            (a, b) => a - b,
        );
        const step = (axis, x, y) =>
            spacing[
                hash([c.seed, c.version, z.id, axis, x, y].join("|")) %
                    spacing.length
            ];
        for (let y = z.y; y < z.y + z.height; y += step("row", z.x, y))
            for (let x = z.x; x < z.x + z.width; x += step("column", x, y)) {
                const prefix = `${c.seed}|${c.version}|${z.id}|${x}|${y}`;
                if (hash(`${prefix}|density`) / 4294967296 >= t.density)
                    continue;
                const v = variants[hash(`${prefix}|variant`) % variants.length],
                    key = `${prefix}|${v.variantId}`;
                const footprint = {
                    x,
                    y,
                    width: v.reserveCells[0],
                    height: v.reserveCells[1],
                };
                if (
                    x + footprint.width > z.x + z.width ||
                    y + footprint.height > z.y + z.height
                )
                    continue;
                candidates.push({
                    key,
                    zoneId: z.id,
                    theme: t.id,
                    variant: v.variantId,
                    scale: t.scaleRange
                        ? t.scaleRange[0] +
                          (t.scaleRange[1] - t.scaleRange[0]) *
                              (hash(`${key}|scale`) / 4294967296)
                        : 1,
                    footprint,
                    priority: hash(key),
                });
            }
        const accepted = [];
        for (const p of candidates.sort(
            (a, b) => a.priority - b.priority || ordinal(a.key, b.key),
        )) {
            if (accepted.length >= t.maxCount) break;
            if (accepted.some((a) => intersects(a.footprint, p.footprint)))
                continue;
            const visibility = open.has(z.id) ? "bright" : "dark",
                asset = assets.get(`${p.variant}|${visibility}`);
            accepted.push({ ...p, state: visibility, file: asset.file });
        }
        output.push(
            ...accepted.filter(
                (p) => !reserved.some((r) => intersects(r, p.footprint)),
            ),
        );
    }
    return output.sort((a, b) => ordinal(a.key, b.key));
}
export function verifySourceFiles(s, directory) {
    return s.assets.map((a) => {
        require(typeof a.file === "string" &&
            basename(a.file) === a.file &&
            a.file.endsWith(".png"), "Unsafe source path");
        const bytes = readFileSync(resolve(directory, a.file));
        require(bytes
            .subarray(0, 8)
            .equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])), "Not PNG");
        require(createHash("sha256").update(bytes).digest("hex") ===
            a.sha256, `Source SHA mismatch: ${a.file}`);
        require(bytes.readUInt32BE(16) === a.canvas[0] &&
            bytes.readUInt32BE(20) ===
                a.canvas[1], `Source canvas mismatch: ${a.file}`);
        return {
            file: a.file,
            sha256: a.sha256,
            width: a.canvas[0],
            height: a.canvas[1],
        };
    });
}
if (
    process.argv[1] &&
    import.meta.url === pathToFileURL(resolve(process.argv[1])).href
) {
    const read = (p) => JSON.parse(readFileSync(p, "utf8"));
    require(process.argv.length ===
        5, "Usage: node plan.mjs contract.json source-assets.json r8-config.json");
    const [c, s, config] = process.argv.slice(2).map(read);
    console.log(
        JSON.stringify(
            {
                classification:
                    "OFFLINE_CANDIDATES_NOT_RUNTIME_OR_DENSITY_PROOF",
                candidates: plan(c, s, config),
            },
            null,
            2,
        ),
    );
}
