# /// script
# requires-python = ">=3.11"
# ///
# How to run: python3 tools/s4-plot.py OUTPUT_DIRECTORY
# Uses already installed matplotlib; never installs dependencies.
"""Render report-derived CSV observations without changing cohort statistics."""

from __future__ import annotations

import csv
import math
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Final

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D

REQUIRED: Final = frozenset(
    {
        "policy", "peopleRule", "seconds", "nObserved", "nAlive", "meanLevel",
        "meanWeaponDamage", "meanToolDamage", "meanWeaponDps", "meanToolDps",
    }
)


@dataclass(frozen=True, slots=True)
class PlotInputError(Exception):
    """A missing or invalid report-derived field prevents a truthful plot."""

    field: str

    def __str__(self) -> str:
        return f"Invalid report-derived plot input: {self.field}"


@dataclass(frozen=True, slots=True)
class Point:
    """An observed cohort row, already aggregated by the Node report generator."""

    policy: str
    rule: str
    seconds: float
    observed: float
    alive: float
    level: float
    weapon: float
    tool: float
    weapon_dps: float | None
    tool_dps: float | None


def number(value: str, field: str) -> float:
    """Parse one finite CSV numeric cell."""
    try:
        parsed = float(value)
    except ValueError as error:
        raise PlotInputError(field) from error
    if not math.isfinite(parsed):
        raise PlotInputError(field)
    return parsed


def read_points(path: Path) -> tuple[Point, ...]:
    """Parse only the columns used by the figure; no statistical recomputation."""
    points: list[Point] = []
    with path.open(encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or not REQUIRED.issubset(reader.fieldnames):
            raise PlotInputError("headers")
        for row in reader:
            points.append(
                Point(
                    policy=row["policy"], rule=row["peopleRule"],
                    seconds=number(row["seconds"], "seconds"),
                    observed=number(row["nObserved"], "nObserved"),
                    alive=number(row["nAlive"], "nAlive"),
                    level=number(row["meanLevel"], "meanLevel"),
                    weapon=number(row["meanWeaponDamage"], "meanWeaponDamage"),
                    tool=number(row["meanToolDamage"], "meanToolDamage"),
                    weapon_dps=number(row["meanWeaponDps"], "meanWeaponDps")
                    if row["meanWeaponDps"] else None,
                    tool_dps=number(row["meanToolDps"], "meanToolDps")
                    if row["meanToolDps"] else None,
                )
            )
    if not points:
        raise PlotInputError("empty curves")
    return tuple(points)


def render(directory: Path) -> None:
    """Plot observed-only level, attributed damage, DPS and cohort sizes."""
    points = read_points(directory / "damage-curves.csv")
    rules = sorted({point.rule for point in points})
    policies = sorted({point.policy for point in points})
    matplotlib.rcParams["svg.hashsalt"] = "bs-mobile-s4-observed-curves"
    matplotlib.rcParams["font.family"] = "DejaVu Sans"
    figure, axes = plt.subplots(4, len(rules), figsize=(6 * len(rules), 15), squeeze=False)
    palette = plt.get_cmap("tab10")
    for column, rule in enumerate(rules):
        for index, policy in enumerate(policies):
            rows = sorted(
                (point for point in points if point.rule == rule and point.policy == policy),
                key=lambda point: point.seconds,
            )
            color = palette(index)
            seconds = [point.seconds for point in rows]
            axes[0, column].plot(seconds, [point.level for point in rows], color=color)
            axes[1, column].plot(seconds, [point.weapon for point in rows], color=color)
            axes[1, column].plot(seconds, [point.tool for point in rows], color=color, linestyle="--")
            interval = [point for point in rows if point.weapon_dps is not None and point.tool_dps is not None]
            axes[2, column].plot([point.seconds for point in interval], [point.weapon_dps for point in interval], color=color)
            axes[2, column].plot([point.seconds for point in interval], [point.tool_dps for point in interval], color=color, linestyle="--")
            axes[3, column].plot(seconds, [point.observed for point in rows], color=color)
            axes[3, column].plot(seconds, [point.alive for point in rows], color=color, linestyle=":")
        titles = (
            "Mean level", "Cumulative direct damage: weapon / tool",
            "Interval direct DPS: weapon / tool", "Observed / alive cohort",
        )
        for row_index, title in enumerate(titles):
            axis = axes[row_index, column]
            axis.set_title(f"People rule {rule} | {title}")
            axis.set_xlabel("Observed simulation seconds")
            axis.grid(alpha=0.2)
            axis.set_ylim(bottom=0)
    handles = [Line2D([0], [0], color=palette(index), label=policy) for index, policy in enumerate(policies)]
    handles.extend(
        [
            Line2D([0], [0], color="black", label="weapon / observed", linestyle="-"),
            Line2D([0], [0], color="black", label="tool activation + growth", linestyle="--"),
            Line2D([0], [0], color="black", label="alive", linestyle=":"),
        ]
    )
    figure.legend(handles=handles, loc="upper center", ncol=5, bbox_to_anchor=(0.5, 0.955))
    figure.suptitle("S4 observed-only cohorts | no forward-fill | ally damage excluded from tool curves", y=0.985)
    figure.text(0.5, 0.015, "Later means describe the observed cohort, not all original cases. Source: damage-curves.csv", ha="center")
    figure.tight_layout(rect=(0, 0.035, 1, 0.92))
    figure.savefig(directory / "S4-curves.svg", metadata={"Date": None})
    figure.savefig(directory / "S4-curves.png", dpi=120, metadata={"Software": "bs-mobile-s4-plot"})
    plt.close(figure)


def main() -> None:
    """Run the plotting surface using already installed scientific tooling."""
    if len(sys.argv) != 2:
        raise PlotInputError("usage: python3 tools/s4-plot.py OUTPUT_DIRECTORY")
    render(Path(sys.argv[1]))


if __name__ == "__main__":
    main()
