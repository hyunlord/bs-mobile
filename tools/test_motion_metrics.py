#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
# Run: python3 -m pytest tools/test_motion_metrics.py
"""Known-motion checks before using estimates on game footage."""
from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest
import motion_metrics as metrics


def test_constant_velocity_has_zero_cv_and_no_defined_autocorrelation() -> None:
    dt = np.full(120, 1 / 60)
    position = np.column_stack((np.arange(121) * .5, np.zeros(121)))
    result = metrics.motion_statistics(position, dt, 1080, True)
    assert result.speed_cv == pytest.approx(0, abs=1e-10)
    assert all(value is None for value in result.speed_autocorrelation.values())
    assert result.screen_rms_1080p == pytest.approx(0, abs=1e-10)


def test_stopped_motion_does_not_claim_zero_cv() -> None:
    result = metrics.motion_statistics(np.zeros((61, 2)), np.full(60, 1 / 60), 1080, False)
    assert result.speed_cv is None
    assert result.phase == 'stop-or-settling'


def test_repeating_judder_has_lag_two_correlation() -> None:
    steps = np.tile([0., 1.], 100)
    xy = np.column_stack((np.r_[0., np.cumsum(steps)], np.zeros(201)))
    result = metrics.motion_statistics(xy, np.full(200, 1 / 60), 1080, True)
    assert result.speed_cv == pytest.approx(1)
    assert result.speed_autocorrelation['2'] == pytest.approx(.99)


def test_subpixel_translation_recovers_direction_and_magnitude() -> None:
    rng = np.random.default_rng(204)
    source = rng.normal(size=(128, 128))
    # Fourier shift provides a known fractional translation without a resampler.
    fy, fx = np.meshgrid(np.fft.fftfreq(128), np.fft.fftfreq(128), indexing='ij')
    target = np.fft.ifft2(np.fft.fft2(source) * np.exp(-2j * np.pi * (fx * .35 + fy * -.65))).real
    estimate = metrics.phase_translation(source, target)
    assert estimate.dx == pytest.approx(.35, abs=.15)
    assert estimate.dy == pytest.approx(-.65, abs=.15)
    assert estimate.response > .5


def test_invalid_intervals_fail_instead_of_disappearing() -> None:
    with pytest.raises(metrics.MotionInputError):
        metrics.motion_statistics(np.zeros((20, 2)), np.zeros(19), 1080, True)


def test_trace_phase_join_reports_motion_and_stop_without_trimming(tmp_path: Path) -> None:
    import csv
    import json
    from analyze_motion_trace import analyze

    frame_path = tmp_path / 'normal-frames.csv'
    entity_path = tmp_path / 'normal-entities.csv'
    with frame_path.open('w', newline='') as stream:
        writer = csv.writer(stream)
        writer.writerow(['frame','phase','wallSeconds','inputX','inputY','height','cameraX','cameraY'])
        for i in range(120):
            writer.writerow([i,0 if i<60 else 1,i/60,1 if i<60 else 0,0,1080,min(i,59)*.5,0])
    with entity_path.open('w', newline='') as stream:
        writer = csv.writer(stream)
        writer.writerow(['frame','kind','id','screenX','screenY','worldX','worldY'])
        for i in range(120):
            writer.writerow([i,'lord',0,min(i,59)*.5,0,min(i,59)*.5,0])
    output=tmp_path/'result'
    analyze(tmp_path,output)
    report=json.loads((output/'motion-report.json').read_text())
    moving, stopped=report['results']
    assert moving['excluded_samples']==0
    assert moving['world_speed_cv']==pytest.approx(0,abs=1e-12)
    assert stopped['screen']['speed_cv'] is None
    assert stopped['nonzero_input_samples']==0


def test_stationary_window_requires_real_input_and_keeps_explicit_onset() -> None:
    from motion_segments import constant_input_windows
    frames=[{'frame':str(i),'phase':'0','wallSeconds':str(i/60),'inputX':'.25','inputY':'0','focused':'True','paused':'False'} for i in range(241)]
    window=constant_input_windows(frames)[0]
    assert window.status=='eligible-candidate'
    assert window.selected_frames[0]==60
    assert window.selected_frames[-1]==240
    assert window.onset_seconds==pytest.approx(1)
    assert window.onset_frames[0]==0
    for row in frames:
        row['inputX']='0'
    invalid=constant_input_windows(frames)[0]
    assert invalid.status=='wrong-or-zero-actual-input'
    assert not invalid.selected_frames


def test_focus_gap_cannot_be_stitched_into_three_second_window() -> None:
    from motion_segments import constant_input_windows
    frames=[{'frame':str(i),'phase':'2','wallSeconds':str(i/60),'inputX':'1','inputY':'0','focused':'True','paused':'False'} for i in range(241)]
    frames[120]['focused']='False'
    assert all(not window.selected_frames for window in constant_input_windows(frames))


def test_undefined_autocorrelation_never_certifies_gate() -> None:
    from motion_segments import velocity_gate
    stats=metrics.motion_statistics(np.column_stack((np.arange(181),np.zeros(181))),np.full(180,1/60),1080,True)
    assert velocity_gate(stats).status=='undefined-not-pass'


def test_oblique_window_requires_explicit_commanded_direction() -> None:
    from motion_segments import constant_input_windows
    frames = [{'frame': str(i), 'phase': '0', 'wallSeconds': str(i/60), 'inputX': '.2', 'inputY': '.15', 'focused': 'True', 'paused': 'False'} for i in range(241)]
    assert constant_input_windows(frames)[0].status == 'wrong-or-zero-actual-input'
    assert constant_input_windows(frames, (.8, .6))[0].status == 'eligible-candidate'
