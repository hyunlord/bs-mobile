#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
# Existing environment: python3 tools/plot_motion_comparison.py BEFORE_DIR AFTER_DIR OUTPUT.png
"""Plot measured before/after series without resampling or interpolating samples."""
from __future__ import annotations

import csv
import sys
from pathlib import Path

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

from motion_metrics import MotionInputError


def plot(before: Path, after: Path, output: Path) -> None:
    """Keep separate axes/time origins and explicitly name estimated video motion."""
    fig, axes = plt.subplots(2, 2, figsize=(14, 7), constrained_layout=True)
    formats = []
    for column, folder in enumerate((before, after)):
        with (folder/'motion-series.csv').open(newline='') as stream:
            rows = list(csv.DictReader(stream))
        video = 'background_speed_1080p' in rows[0]
        formats.append(video)
        if not video:
            rows = [row for row in rows if row['kind']=='lord' and row['id']=='0']
        if not rows:
            message = 'No lord or background series to plot'
            raise MotionInputError(message)
        seconds=np.array([float(row['seconds']) for row in rows]); seconds-=seconds[0]
        speed_name='background_speed_1080p' if video else 'screen_speed_1080p'
        x_name='cumulative_x_1080p' if video else 'screen_x_1080p'
        y_name='cumulative_y_1080p' if video else 'screen_y_1080p'
        speed=np.array([float(row[speed_name]) for row in rows])
        xy=np.array([[float(row[x_name]),float(row[y_name])] for row in rows])
        axes[0,column].plot(seconds,speed,linewidth=.8)
        axes[0,column].set_title(('Before' if column==0 else 'After')+' | '+folder.name)
        axes[0,column].set_ylabel('Background estimated speed (1080p px/s)' if video else 'Lord screen speed (1080p px/s)')
        groups=[list(range(len(rows)))]
        if not video:
            groups=[]
            for i,row in enumerate(rows):
                if not groups or row['phase']!=rows[groups[-1][-1]]['phase'] or row['segment']!=rows[groups[-1][-1]]['segment']:
                    groups.append([])
                groups[-1].append(i)
        for group in groups:
            if len(group)<15:
                continue
            indices=np.array(group)
            positions=xy[indices]
            average=np.column_stack([np.convolve(positions[:,a],np.ones(15)/15,'valid') for a in range(2)])
            residual=np.linalg.norm(positions[7:-7]-average,axis=1)
            axes[1,column].plot(seconds[indices[7:-7]],residual,linewidth=.8)
        axes[1,column].set_ylabel('15-frame MA residual (1080p px)')
        axes[1,column].set_xlabel('Elapsed seconds (each recording origin)')
        for axis in axes[:,column]:
            axis.grid(alpha=.2)
    if formats[0]!=formats[1]:
        message='Cannot compare native lord trace against estimated background video'
        raise MotionInputError(message)
    fig.suptitle('Measured motion; no interpolated samples. Compare input, phase and ROI provenance before attributing a change.')
    output.parent.mkdir(parents=True,exist_ok=True)
    fig.savefig(output,dpi=160)
    plt.close(fig)


if __name__=='__main__':
    if len(sys.argv)!=4:
        raise SystemExit('Usage: plot_motion_comparison.py BEFORE_DIR AFTER_DIR OUTPUT.png')
    plot(Path(sys.argv[1]),Path(sys.argv[2]),Path(sys.argv[3]))
