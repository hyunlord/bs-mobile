#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
# Imported by analyze_motion_trace.py; existing numpy environment, no installs.
"""Explicit constant-input windows; raw phases remain a separate report."""
from __future__ import annotations

from collections.abc import Mapping, Sequence
from dataclasses import dataclass

import numpy as np

from motion_metrics import MotionStatistics, motion_statistics


@dataclass(frozen=True, slots=True)
class StableWindow:
    phase: int
    first_frame: int
    last_frame: int
    input_x: float
    input_y: float
    constant_input_seconds: float
    status: str
    selected_frames: tuple[int, ...]
    onset_frames: tuple[int, ...]
    onset_seconds: float


def constant_input_windows(frames: Sequence[Mapping[str, str]], direction: tuple[float, float] = (1., 0.)) -> list[StableWindow]:
    """Split on actual input, focus, pause, phase, and frame gaps before selection."""
    groups: list[list[Mapping[str, str]]] = []
    for row in frames:
        if int(row['phase']) not in (0, 2):
            continue
        new_group = not groups
        if groups:
            prior = groups[-1][-1]
            new_group = (int(row['frame']) != int(prior['frame']) + 1 or row['phase'] != prior['phase']
                         or row.get('focused') != prior.get('focused') or row.get('paused') != prior.get('paused')
                         or abs(float(row['inputX'])-float(prior['inputX'])) > 1e-5
                         or abs(float(row['inputY'])-float(prior['inputY'])) > 1e-5)
        if new_group:
            groups.append([])
        groups[-1].append(row)
    windows = []
    for group in groups:
        first, last = group[0], group[-1]
        start, end = float(first['wallSeconds']), float(last['wallSeconds'])
        x, y, phase = float(first['inputX']), float(first['inputY']), int(first['phase'])
        status = 'eligible-candidate'
        magnitude = .25 if phase == 0 else 1.
        if abs(x-direction[0]*magnitude) > .005 or abs(y-direction[1]*magnitude) > .005:
            status = 'wrong-or-zero-actual-input'
        elif any(row.get('focused', '').lower() != 'true' for row in group):
            status = 'unfocused-or-focus-unrecorded'
        elif any(row.get('paused', '').lower() != 'false' for row in group):
            status = 'paused-or-pause-unrecorded'
        elif end-start < 3.5:
            status = 'insufficient-duration-for-three-seconds-plus-onset'
        # Include the sample immediately before the three-second boundary, so the
        # actual selected interval is at least 3 s instead of rounding it short.
        boundary = max((i for i, row in enumerate(group) if float(row['wallSeconds']) <= end-3), default=0)
        onset_seconds = float(group[boundary]['wallSeconds'])-start
        if status == 'eligible-candidate' and onset_seconds < .5:
            status = 'insufficient-onset-after-frame-boundary-selection'
        selected = tuple(int(row['frame']) for row in group[boundary:]) if status == 'eligible-candidate' else ()
        onset = tuple(int(row['frame']) for row in group[:boundary+1]) if selected else tuple(int(row['frame']) for row in group)
        windows.append(StableWindow(phase, int(first['frame']), int(last['frame']), x, y, end-start,
                                    status, selected, onset, onset_seconds if selected else end-start))
    return windows


@dataclass(frozen=True, slots=True)
class MotionGate:
    cv: float | None
    maximum_positive_acf_lags_2_to_12: float | None
    status: str


def velocity_gate(stats: MotionStatistics) -> MotionGate:
    """Use the user limits; undefined correlation is explicitly not a pass."""
    values = list(stats.speed_autocorrelation.values())
    complete = all(value is not None for value in values)
    acf = max((value for value in values if value is not None), default=None)
    status = 'undefined-not-pass'
    if stats.speed_cv is not None and complete and acf is not None:
        status = 'pass' if stats.speed_cv <= .10 and acf <= .3 else 'fail'
    return MotionGate(stats.speed_cv, acf, status)


@dataclass(frozen=True, slots=True)
class WindowMetrics:
    kind: str
    id: int
    phase: int
    scope: str
    selection: StableWindow
    selected_first_frame: int
    selected_last_frame: int
    selected_samples: int
    selected_seconds: float
    world: MotionStatistics
    camera: MotionStatistics
    screen: MotionStatistics
    world_second_half_speed_change_ratio: float | None
    maximum_camera_shake_world_units: float | None
    shake_subtracted: bool
    world_gate: MotionGate
    camera_gate: MotionGate
    lord_screen_rms_gate: str
    gate_scope: str = 'Only stationary-candidate is eligible; onset/ineligible metrics are diagnostic, never acceptance. Near-zero screen speed CV is not a lord motion gate.'


def window_metrics(frames: Sequence[Mapping[str, str]], entities: Sequence[Mapping[str, str]], direction: tuple[float, float] = (1., 0.)) -> list[WindowMetrics]:
    """Append explicitly selected/onset rows without modifying full-phase results."""
    results: list[WindowMetrics] = []
    frame_index = {int(row['frame']): row for row in frames}
    entity_index = {(row['kind'], int(row['id']), int(row['frame'])): row for row in entities}
    keys = sorted({(row['kind'], int(row['id'])) for row in entities})
    for window in constant_input_windows(frames, direction):
        for label, selected in [('stationary-candidate', window.selected_frames), ('onset-or-ineligible', window.onset_frames)]:
            if len(selected) < 16:
                continue
            for kind, entity_id in keys:
                if not all((kind, entity_id, frame) in entity_index for frame in selected):
                    continue
                linked = [frame_index[frame] for frame in selected]
                observed = [entity_index[kind, entity_id, frame] for frame in selected]
                times = np.array([float(row['wallSeconds']) for row in linked])
                dt = np.diff(times)
                world = np.array([[float(row['worldX']),float(row['worldY'])] for row in observed])
                screen = np.array([[float(row['screenX']),float(row['screenY'])] for row in observed])
                camera = np.array([[float(row['cameraX']),float(row['cameraY'])] for row in linked])
                world_stats = motion_statistics(world,dt,1080,True)
                camera_stats = motion_statistics(camera,dt,1080,True)
                screen_stats = motion_statistics(screen,dt,int(linked[0]['height']),True)
                speed = np.linalg.norm(np.diff(world,axis=0),axis=1)/dt
                half = len(speed)//2
                mean = float(np.mean(speed))
                drift = float((np.mean(speed[half:])-np.mean(speed[:half]))/mean) if mean > 1e-8 else None
                shake_recorded = all('cameraShakeX' in row and 'cameraShakeY' in row for row in linked)
                shake_max = max((float(np.hypot(float(row['cameraShakeX']),float(row['cameraShakeY']))) for row in linked),default=0.) if shake_recorded else None
                results.append(WindowMetrics(kind,entity_id,window.phase,label,window,selected[0],selected[-1],
                                             len(selected),float(times[-1]-times[0]),world_stats,camera_stats,screen_stats,
                                             drift,shake_max,False,velocity_gate(world_stats),velocity_gate(camera_stats),
                                             ('pass' if screen_stats.screen_rms_1080p<=.5 else 'fail') if kind=='lord' else 'not-lord'))
    return results
