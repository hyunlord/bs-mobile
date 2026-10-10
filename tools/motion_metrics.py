#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
# Run with the existing Python/numpy environment; no packages are installed.
"""Pure, reproducible motion statistics for #204; units are explicit in output."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Final

import numpy as np
from numpy.typing import NDArray

FloatArray = NDArray[np.float64]
LAGS: Final = tuple(range(2, 13))


class MotionInputError(ValueError):
    """Malformed or insufficient motion data; never silently filter it."""


@dataclass(frozen=True, slots=True)
class Translation:
    dx: float
    dy: float
    response: float


@dataclass(frozen=True, slots=True)
class MotionStatistics:
    phase: str
    intervals: int
    mean_speed_pixels_per_second: float
    speed_cv: float | None
    speed_autocorrelation: dict[str, float | None]
    screen_rms_1080p: float
    maximum_step_1080p: float
    duration_seconds: float
    rms_method: str = 'Euclidean residual from centered 15-frame moving average; seven edge samples each side excluded; scale=1080/height'
    autocorrelation_method: str = 'Biased autocovariance divided by lag-zero sum; speed=Euclidean displacement / actual dt; lags 2..12'


def motion_statistics(position: FloatArray, dt: FloatArray, height: int, moving: bool) -> MotionStatistics:
    """Measure motion, preserving stop phases instead of dividing by zero speed."""
    if (position.ndim != 2 or position.shape[1] != 2 or len(position) != len(dt) + 1
            or len(position) < 16 or height <= 0 or not np.isfinite(position).all()
            or not np.isfinite(dt).all() or np.any(dt <= 0)):
        message = 'Need >=16 finite XY samples, one positive dt per interval, and positive screen height'
        raise MotionInputError(message)
    steps = np.linalg.norm(np.diff(position, axis=0), axis=1)
    speeds = steps / dt
    mean = float(np.mean(speeds))
    centered = speeds - mean
    energy = float(np.dot(centered, centered))
    acf = {str(lag): (float(np.dot(centered[:-lag], centered[lag:]) / energy)
                     if energy > 1e-16 and len(centered) > lag else None) for lag in LAGS}
    mean_xy = np.column_stack([np.convolve(position[:, axis], np.ones(15) / 15, mode='valid') for axis in range(2)])
    residual = position[7:-7] - mean_xy
    scale = 1080 / height
    return MotionStatistics('moving' if moving else 'stop-or-settling', len(dt), mean,
                            float(np.std(speeds) / mean) if moving and mean > 1e-8 else None,
                            acf, float(np.sqrt(np.mean(np.sum(residual ** 2, axis=1)))) * scale,
                            float(np.max(steps)) * scale, float(np.sum(dt)))


def phase_translation(previous: FloatArray, current: FloatArray) -> Translation:
    """Estimate ROI motion with installed OpenCV; response is a heuristic, not probability."""
    import cv2

    if previous.shape != current.shape or previous.ndim != 2 or min(previous.shape) < 16:
        message = 'Phase correlation needs equal 2D image patches at least 16 pixels wide/high'
        raise MotionInputError(message)
    window = cv2.createHanningWindow((previous.shape[1], previous.shape[0]), cv2.CV_64F)
    _, response = cv2.phaseCorrelate(previous.copy(), current.copy(), window)
    spectrum = np.fft.fft2(current * window) * np.conj(np.fft.fft2(previous * window))
    spectrum /= np.maximum(np.abs(spectrum), 1e-12)
    peak = np.unravel_index(np.argmax(np.fft.ifft2(spectrum).real), spectrum.shape)
    dy = float(peak[0] if peak[0] <= previous.shape[0] // 2 else peak[0] - previous.shape[0])
    dx = float(peak[1] if peak[1] <= previous.shape[1] // 2 else peak[1] - previous.shape[1])
    # Local upsampled inverse DFT avoids OpenCV centroid bias at fractional pixels.
    fy, fx = np.fft.fftfreq(previous.shape[0]), np.fft.fftfreq(previous.shape[1])
    for spacing in (.1, .01):
        xs, ys = dx + np.arange(-10, 11) * spacing, dy + np.arange(-10, 11) * spacing
        surface = (np.exp(2j * np.pi * ys[:, None] * fy) @ spectrum
                   @ np.exp(2j * np.pi * fx[:, None] * xs)).real
        iy, ix = np.unravel_index(np.argmax(surface), surface.shape)
        dx, dy = float(xs[ix]), float(ys[iy])
    return Translation(dx, dy, float(response))
