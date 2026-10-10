#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
# Existing environment only: python3 tools/analyze_motion_trace.py TRACE_DIR OUTPUT_DIR
"""Analyze normal presentation traces without mixing moving and settling phases."""
from __future__ import annotations

import csv
import json
import sys
from dataclasses import asdict
from pathlib import Path

import numpy as np

from motion_metrics import MotionInputError, motion_statistics
from motion_segments import constant_input_windows, window_metrics


def analyze(folder: Path, output: Path) -> None:
    """Join exact frame IDs, retain every contiguous phase and write provenance."""
    with (folder / 'normal-frames.csv').open(newline='') as stream:
        frames = list(csv.DictReader(stream))
    with (folder / 'normal-entities.csv').open(newline='') as stream:
        entities = list(csv.DictReader(stream))
    metadata_path = folder / 'normal-trace.txt'
    metadata = dict(line.split('=', 1) for line in metadata_path.read_text().splitlines() if '=' in line) if metadata_path.exists() else {}
    direction = (float(metadata.get('commandedDirectionX', '1')), float(metadata.get('commandedDirectionY', '0')))
    if not np.isfinite(direction).all() or abs(float(np.linalg.norm(direction))-1) > 1e-5:
        raise MotionInputError('Commanded direction must be a finite unit vector')
    by_frame = {int(row['frame']): row for row in frames}
    if len(by_frame) != len(frames):
        message = 'Duplicate frame IDs'
        raise MotionInputError(message)
    output.mkdir(parents=True, exist_ok=True)
    results = []
    series = []
    keys = sorted({(row['kind'], int(row['id'])) for row in entities})
    for kind, entity_id in keys:
        selected = [row for row in entities if row['kind'] == kind and int(row['id']) == entity_id]
        for phase in sorted({int(row['phase']) for row in frames}):
            subset = [row for row in selected if int(by_frame[int(row['frame'])]['phase']) == phase]
            groups: list[list[dict[str, str]]] = []
            for row in subset:
                if not groups or int(row['frame']) != int(groups[-1][-1]['frame']) + 1:
                    groups.append([])
                groups[-1].append(row)
            for segment, group in enumerate(groups):
                linked = [by_frame[int(row['frame'])] for row in group]
                times = np.array([float(row['wallSeconds']) for row in linked])
                xy = np.array([[float(row['screenX']), float(row['screenY'])] for row in group])
                world = np.array([[float(row['worldX']), float(row['worldY'])] for row in group])
                moving = phase in (0, 2)
                identity = {'kind': kind, 'id': entity_id, 'phase': phase, 'segment': segment,
                            'samples': len(group), 'first_frame': int(group[0]['frame']),
                            'last_frame': int(group[-1]['frame']), 'excluded_samples': 0,
                            'nonzero_input_samples': sum(float(row['inputX']) != 0 or float(row['inputY']) != 0 for row in linked)}
                if len(group) < 16:
                    results.append({**identity, 'status': 'insufficient-contiguous-samples'})
                    continue
                heights = {int(row['height']) for row in linked}
                if len(heights) != 1:
                    message = 'Viewport height changed inside phase'
                    raise MotionInputError(message)
                height = heights.pop()
                screen_result = motion_statistics(xy, np.diff(times), height, moving)
                world_result = motion_statistics(world, np.diff(times), 1080, moving)
                camera = np.array([[float(row['cameraX']), float(row['cameraY'])] for row in linked])
                camera_result = motion_statistics(camera, np.diff(times), 1080, moving)
                results.append({**identity, 'status': 'measured', 'screen': asdict(screen_result),
                                'world_speed_cv': world_result.speed_cv,
                                'world_mean_speed_units_per_second': world_result.mean_speed_pixels_per_second,
                                'world_speed_autocorrelation': world_result.speed_autocorrelation,
                                'camera_speed_cv': camera_result.speed_cv,
                                'camera_speed_autocorrelation': camera_result.speed_autocorrelation})
                for i in range(1, len(group)):
                    dt = times[i] - times[i-1]
                    series.append([kind, entity_id, phase, segment, int(group[i]['frame']), times[i],
                                   np.linalg.norm(xy[i]-xy[i-1])/dt * 1080/height,
                                   np.linalg.norm(world[i]-world[i-1])/dt,
                                   xy[i, 0]*1080/height, xy[i, 1]*1080/height])
    report = {'source': str(folder.resolve()), 'scope': 'Native recorded presentation, not physical input-to-photon.',
              'commanded_direction': direction,
              'direction_provenance': 'normal-trace.txt commandedDirectionX/Y; historical absent metadata defaults to cardinal right',
              'phase_contract': '-1 setup,0 commanded-direction25%,1 release,2 commanded-direction100%,3 release,4 release remainder; durations from CSV, no hardcoded block length; all raw phase samples retained.',
              'stationary_selection': 'Actual constant metadata-commanded input within0.005; focused/unpaused and contiguous; final at least3s with at least0.5s preceding onset. Full-phase and onset results retained. Last3s is a stationary candidate, not proof of stationarity; half-window drift and shake disclosed.',
              'constant_input_windows': [asdict(window) for window in constant_input_windows(frames, direction)],
              'window_metrics': [asdict(result) for result in window_metrics(frames, entities, direction)],
              'normalization': 'Screen positions/speed normalized by 1080/actual height; world/camera units unchanged.',
              'stop_contract': 'CV is null in release/settling; RMS and raw motion remain visible. Undefined constant-speed ACF is null.',
              'results': results}
    (output / 'motion-report.json').write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    with (output / 'motion-series.csv').open('w', newline='') as stream:
        writer = csv.writer(stream)
        writer.writerow(['kind','id','phase','segment','frame','seconds','screen_speed_1080p','world_speed','screen_x_1080p','screen_y_1080p'])
        writer.writerows(series)


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit('Usage: analyze_motion_trace.py TRACE_DIR OUTPUT_DIR')
    analyze(Path(sys.argv[1]), Path(sys.argv[2]))
