#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
# Existing numpy/OpenCV environment only; no installation.
# python3 tools/analyze_motion_video.py VIDEO START_SEC DURATION_SEC X Y W H OUTPUT_DIR
"""Measure one explicitly selected background ROI using actual decoded video PTS."""
from __future__ import annotations

import csv
import json
import subprocess
import sys
from dataclasses import asdict
from pathlib import Path

import cv2
import numpy as np

from motion_metrics import MotionInputError, motion_statistics, phase_translation


def analyze(video: Path, start: float, duration: float, roi: tuple[int, int, int, int], output: Path) -> None:
    """Retain low-confidence estimates; mark them, never silently select smooth pairs."""
    probe = subprocess.run(['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
                            '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(video)],
                           check=True, capture_output=True, text=True)
    timestamps = [float(row['best_effort_timestamp_time']) for row in json.loads(probe.stdout)['frames']]
    indices = [i for i, time in enumerate(timestamps) if start <= time <= start + duration]
    if len(indices) < 16:
        message = 'Selected video interval has fewer than 16 frames'
        raise MotionInputError(message)
    capture = cv2.VideoCapture(str(video))
    x, y, width, height = roi
    frame_width = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH))
    frame_height = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT))
    if min(x, y) < 0 or min(width, height) < 16 or x + width > frame_width or y + height > frame_height:
        capture.release()
        message = 'ROI is outside the frame or too small'
        raise MotionInputError(message)
    capture.set(cv2.CAP_PROP_POS_FRAMES, indices[0])
    previous = None
    shifts = []
    confidence = []
    textures = []
    output.mkdir(parents=True, exist_ok=True)
    try:
        for index in indices:
            ok, frame = capture.read()
            if not ok:
                message = f'Cannot decode video frame {index}'
                raise MotionInputError(message)
            patch = cv2.cvtColor(frame[y:y+height, x:x+width], cv2.COLOR_BGR2GRAY).astype(np.float64)
            textures.append(float(np.std(patch)))
            if index in (indices[0], indices[len(indices)//2], indices[-1]):
                cv2.rectangle(frame, (x,y), (x+width,y+height), (0,0,255), 2)
                cv2.imwrite(str(output / f'roi-frame-{index}.png'), frame)
            if previous is not None:
                result = phase_translation(previous, patch)
                shifts.append((result.dx, result.dy))
                confidence.append(result.response)
            previous = patch
    finally:
        capture.release()
    delta = np.array(shifts)
    xy = np.vstack((np.zeros((1,2)), np.cumsum(delta, axis=0)))
    times = np.array([timestamps[i] for i in indices])
    result = motion_statistics(xy, np.diff(times), frame_height, True)
    low = sum(value < .5 for value in confidence)
    report = {'source': str(video.resolve()), 'start_seconds': start, 'duration_seconds': duration,
              'roi_xywh': list(roi), 'screen_width': frame_width, 'screen_height': frame_height,
              'scope': 'Estimated background ROI translation; not lord position or ground-truth camera motion. Inspect first/middle/last ROI frames for entities, HUD and animation.',
              'comparison_limit': 'Different input/scene/ROI makes cross-video CV causal attribution invalid; disclose unmatched conditions.',
              'confidence': {'method': 'OpenCV phase response, heuristic not probability; low <0.5; texture SD <5 is weak.',
                             'minimum_response': min(confidence), 'median_response': float(np.median(confidence)),
                             'low_response_pairs': low, 'pairs': len(confidence), 'minimum_texture_sd': min(textures),
                             'status': 'needs-inspection' if low or min(textures)<5 else 'correlation-supported-not-ground-truth'},
              'metrics': asdict(result)}
    (output/'motion-report.json').write_text(json.dumps(report, indent=2, allow_nan=False)+'\n')
    with (output/'motion-series.csv').open('w', newline='') as stream:
        writer=csv.writer(stream)
        writer.writerow(['frame','seconds','dx','dy','response','background_speed_1080p','cumulative_x_1080p','cumulative_y_1080p'])
        for j, (dx,dy) in enumerate(shifts):
            writer.writerow([indices[j+1],times[j+1],dx,dy,confidence[j],np.hypot(dx,dy)/(times[j+1]-times[j])*1080/frame_height,xy[j+1,0]*1080/frame_height,xy[j+1,1]*1080/frame_height])


if __name__ == '__main__':
    if len(sys.argv) != 9:
        raise SystemExit('Usage: analyze_motion_video.py VIDEO START_SEC DURATION_SEC X Y W H OUTPUT_DIR')
    analyze(Path(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3]),
            (int(sys.argv[4]),int(sys.argv[5]),int(sys.argv[6]),int(sys.argv[7])),Path(sys.argv[8]))
