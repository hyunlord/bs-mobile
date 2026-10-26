# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy", "pillow"]
# ///
# ─── How to run ───
# python3 tools/art-authoring/measure-estate-layers.py INPUT OUTPUT [BASELINE]
# python3 tools/art-authoring/measure-estate-layers.py --self-test
"""Measure captured pixels, never substitute nominal mesh width for visible dirt."""
from __future__ import annotations

import csv
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Final

import numpy as np
from numpy.typing import NDArray
from PIL import Image, ImageDraw

Array = NDArray[np.float64]
STEP: Final = 0.25
THRESHOLD: Final = 12.0
ROIS: Final = (("dark-upper", (35, 130, 280, 180)),
               ("dark-lower", (35, 530, 280, 590)),
               ("lit-screen-fixed", (90, 320, 170, 380)))
STAGES: Final = ("0-ground", "1-night-light", "2-roads", "3-sites-decor", "4-final")


@dataclass(frozen=True, slots=True)
class Centre:
    index: int
    sample: int
    x: float
    y: float
    nominal: float
    mesh_width: float = 0.0


@dataclass(frozen=True, slots=True)
class CrossSection:
    index: int
    run: int
    fraction: float
    x: float
    y: float
    nx: float
    ny: float
    nominal: float


def image_path(folder: Path, stage: str) -> Path:
    return folder / f"estate-r7-layers-{stage}-390x844.png"


def load_rgb(path: Path) -> Array:
    with Image.open(path) as image:
        return np.asarray(image.convert("RGB"), dtype=np.float64)


def write_csv(path: Path, header: tuple[str, ...], rows: list[list[str | int | float]]) -> None:
    with path.open("w", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(header)
        writer.writerows(rows)


def centres(folder: Path, height: int) -> list[Centre]:
    """Unity coordinates have their origin at the image's bottom left."""
    source = folder / "estate-r7-road-centrelines.csv"
    if not source.exists():
        return []
    with source.open(newline="") as handle:
        return [Centre(int(r["index"]), int(r["sample"]), float(r["screenX"]),
                       height - float(r["screenY"]), float(r["nominalWidthPx"]), float(r["meshWidthPx"]))
                for r in csv.DictReader(handle)]


def straight_samples(points: list[Centre]) -> list[CrossSection]:
    """Choose predetermined fractions of straight runs, including invisible roads."""
    result: list[CrossSection] = []
    for index in sorted({p.index for p in points}):
        road = sorted((p for p in points if p.index == index), key=lambda p: p.sample)
        start = 0
        run = 0
        while start < len(road) - 1:
            a, b = road[start:start + 2]
            direction = np.array([b.x - a.x, b.y - a.y])
            length = float(np.linalg.norm(direction))
            if length < 0.01:
                start += 1
                continue
            direction /= length
            end = start + 1
            while end < len(road) - 1:
                c, d = road[end:end + 2]
                delta = np.array([d.x - c.x, d.y - c.y])
                norm = float(np.linalg.norm(delta))
                if norm < 0.01 or float(np.dot(delta / norm, direction)) < 0.999:
                    break
                end += 1
            b = road[end]
            length = float(np.hypot(b.x - a.x, b.y - a.y))
            if length >= 30:
                for fraction in (0.25, 0.5, 0.75):
                    result.append(CrossSection(index, run, fraction, a.x + (b.x - a.x) * fraction,
                                               a.y + (b.y - a.y) * fraction,
                                               -float(direction[1]), float(direction[0]), a.nominal))
                run += 1
            start = end
    return result


def bilinear(image: Array, x: Array, y: Array) -> Array:
    """Sample with pixel centres at n+0.5; out-of-image samples become NaN."""
    height, width = image.shape[:2]
    valid = (x >= 0.5) & (x <= width - 0.5) & (y >= 0.5) & (y <= height - 0.5)
    xx, yy = np.clip(x - 0.5, 0, width - 1), np.clip(y - 0.5, 0, height - 1)
    ix, iy = np.floor(xx).astype(int), np.floor(yy).astype(int)
    jx, jy = np.minimum(ix + 1, width - 1), np.minimum(iy + 1, height - 1)
    fx, fy = (xx - ix)[:, None], (yy - iy)[:, None]
    result = (image[iy, ix] * (1 - fx) + image[iy, jx] * fx) * (1 - fy)
    result += (image[jy, ix] * (1 - fx) + image[jy, jx] * fx) * fy
    result[~valid] = np.nan
    return result


def connected_width(profile: Array, threshold: float) -> float:
    """Only count the threshold component containing the actual ribbon centre."""
    middle = len(profile) // 2
    active = np.isfinite(profile) & (profile >= threshold)
    if not active[middle]:
        return 0.0
    left = right = middle
    while left > 0 and active[left - 1]:
        left -= 1
    while right < len(profile) - 1 and active[right + 1]:
        right += 1
    return (right - left + 1) * STEP


def report(folder: Path, output: Path, label: str) -> Image.Image:
    """Produce fixed-screen ROI and paired-layer pixel metrics plus annotations."""
    roi_rows: list[list[str | int | float]] = []
    for stage in STAGES:
        image = load_rgb(image_path(folder, stage))
        for name, (x0, y0, x1, y1) in ROIS:
            rgb = image[y0:y1, x0:x1].mean(axis=(0, 1))
            roi_rows.append([label, stage, name, x0, y0, x1, y1, *rgb.tolist(),
                             float(np.dot(rgb, [0.2126, 0.7152, 0.0722]))])
    write_csv(output / f"{label}-rois.csv", ("capture", "stage", "roi", "x0", "y0", "x1", "y1", "r", "g", "b", "luma_srgb"), roi_rows)
    before = load_rgb(image_path(folder, STAGES[1]))
    after = load_rgb(image_path(folder, STAGES[2]))
    assert before.shape == after.shape == (844, 390, 3), "Expected unscaled 390x844 paired captures"
    annotated = Image.fromarray(after.astype(np.uint8))
    draw = ImageDraw.Draw(annotated)
    points = centres(folder, after.shape[0])
    rows: list[list[str | int | float]] = []
    offsets = np.arange(-40, 40 + STEP / 2, STEP)
    for number, section in enumerate(straight_samples(points)):
        x, y = section.x + offsets * section.nx, section.y + offsets * section.ny
        delta = np.max(np.abs(bilinear(after, x, y) - bilinear(before, x, y)), axis=1)
        near = np.abs(offsets) <= section.nominal / 2 + 1
        peak = float(np.nanmax(delta[near]))
        width = connected_width(delta, THRESHOLD)
        fwhm = connected_width(delta, peak / 2) if peak > 0 else 0.0
        junction = any(p.index != section.index and np.hypot(p.x - section.x, p.y - section.y) < 30 for p in points)
        status = "junction-nearby" if junction else "review-annotated"
        if not np.isfinite(delta).all():
            status = "screen-clipped"
        nearest = min((p for p in points if p.index == section.index), key=lambda p: np.hypot(p.x - section.x, p.y - section.y))
        rows.append([label, number, section.index, section.run, section.fraction, section.x, section.y,
                     section.nominal, nearest.mesh_width, nearest.sample, THRESHOLD, width, fwhm, float(delta[len(delta) // 2]), peak, status])
        radius = max(12.0, section.nominal / 2 + 2)
        draw.line((section.x - section.nx * radius, section.y - section.ny * radius,
                   section.x + section.nx * radius, section.y + section.ny * radius), fill="magenta", width=1)
        draw.text((section.x + 2, section.y + 2), str(number), fill="white", stroke_width=1, stroke_fill="black")
    write_csv(output / f"{label}-widths.csv", ("capture", "id", "road", "run", "fraction", "x", "y", "nominal_mesh_px", "nearest_mesh_width_px", "nearest_mesh_sample", "rgb_max_threshold", "centre_connected_width_px", "centre_connected_fwhm_px", "centre_delta", "local_peak_delta", "status"), rows)
    for name, rect in ROIS:
        draw.rectangle(rect, outline="red", width=1)
        draw.text(rect[:2], name, fill="white", stroke_width=1, stroke_fill="black")
    annotated.save(output / f"{label}-annotated.png")
    return annotated


def self_test() -> None:
    """Known-width stripe, disconnected island, and translated mesh coordinate checks."""
    profile = np.zeros(321)
    profile[140:180] = 20
    profile[240:260] = 30
    assert connected_width(profile, 12) == 10
    profile[160] = 0
    assert connected_width(profile, 12) == 0
    image = np.zeros((60, 60, 3))
    image[:, 25:35] = 20
    samples = bilinear(image, np.array([25.5, 30.5, 35.5]), np.array([30.5] * 3))
    assert samples[:, 0].tolist() == [20, 20, 0]
    points = [Centre(1, i, float(i * 10), 40, 18) for i in range(9)]
    assert [s.x for s in straight_samples(points)] == [20, 40, 60]
    print("PASS: connected width, disconnected island, bilinear pixel centres, fixed sample fractions")


def main() -> None:
    if sys.argv[1:] == ["--self-test"]:
        self_test()
        return
    if len(sys.argv) not in (3, 4):
        raise SystemExit(__doc__ + "\nUsage: measure-estate-layers.py INPUT OUTPUT [BASELINE]")
    source, output = Path(sys.argv[1]), Path(sys.argv[2])
    output.mkdir(parents=True, exist_ok=True)
    after = report(source, output, "after")
    if len(sys.argv) == 4:
        before = report(Path(sys.argv[3]), output, "before")
        comparison = Image.new("RGB", (780, 866), "black")
        comparison.paste(before, (0, 22))
        comparison.paste(after, (390, 22))
        draw = ImageDraw.Draw(comparison)
        draw.text((8, 4), "BEFORE (actual capture)", fill="white")
        draw.text((398, 4), "AFTER (actual capture)", fill="white")
        comparison.save(output / "before-after-annotated.png")
    (output / "METHOD.md").write_text(
        "# Actual capture measurements\n\n"
        "Widths use paired stage2 - stage1 pixels, max absolute sRGB channel difference >=12, "
        "0.25px bilinear sampling. Only the contiguous interval containing the actual mesh centre counts. "
        "FWHM uses half the local peak contrast within nominal half-width+1px; it is not nominal width. "
        "Sampling positions are 25/50/75% of straight runs >=30px, selected before evaluating pixels. "
        "Junction-nearby and clipped rows must not be treated as unobstructed width evidence. "
        "Review annotated lines for additional occlusion, crossings, and anti-aliasing effects. "
        "The C# raw span/count CSV remains the independent capture record.\n\n"
        "Missing geometry produces a header-only width CSV, never inferred baseline widths. "
        "ROI luma is Rec709-weighted sRGB (not linear luminance). Fixed screen ROIs are not "
        "the same world land after a pivot/camera change; the lit ROI in particular needs a "
        "world-anchored supplementary comparison before claiming lighting parity.\n", encoding="utf-8")


if __name__ == "__main__":
    main()
