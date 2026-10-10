#!/usr/bin/env node
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { motionStatistics } from './motion-metrics.mjs';
import { phaseTranslation } from './phase-translation.mjs';

function command(program, args) {
  const result = spawnSync(program, args, { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error(`${program} failed: ${result.error?.message ?? result.stderr}`);
  return result.stdout;
}
export function grayPatch(bgr) {
  const gray = new Float64Array(bgr.length / 3);
  // OpenCV's 8-bit BGR2GRAY fixed-point coefficients (15 fractional bits).
  for (let i = 0; i < gray.length; i++) gray[i] = (bgr[i * 3] * 3735 + bgr[i * 3 + 1] * 19235 + bgr[i * 3 + 2] * 9798 + 16384) >> 15;
  return gray;
}
function median(values) { const a = [...values].sort((x, y) => x - y), m = Math.floor(a.length / 2); return a.length % 2 ? a[m] : (a[m - 1] + a[m]) / 2; }
export async function analyze(video, start, duration, roi, output) {
  if (!Number.isFinite(start) || !Number.isFinite(duration) || duration <= 0) throw new Error('Need finite start and positive duration');
  const probe = JSON.parse(command('ffprobe', ['-v', 'error', '-select_streams', 'v:0', '-show_frames', '-show_streams', '-show_entries', 'frame=best_effort_timestamp_time:stream=width,height:stream_side_data=rotation', '-of', 'json', video]));
  const timestamps = probe.frames.map(row => Number(row.best_effort_timestamp_time));
  if (timestamps.some((t, i) => !Number.isFinite(t) || i > 0 && t <= timestamps[i - 1])) throw new Error('Video PTS must be finite and strictly increasing');
  const indices = timestamps.map((time, i) => start <= time && time <= start + duration ? i : -1).filter(i => i >= 0);
  if (indices.length < 16) throw new Error('Selected video interval has fewer than 16 frames');
  let { width: frameWidth, height: frameHeight } = probe.streams[0];
  const rotation = probe.streams[0].side_data_list?.find(row => row.rotation !== undefined)?.rotation ?? 0;
  if (Math.abs(rotation) % 180 === 90) [frameWidth, frameHeight] = [frameHeight, frameWidth];
  const [x, y, width, height] = roi;
  if (!roi.every(Number.isInteger) || Math.min(x, y) < 0 || Math.min(width, height) < 16 || x + width > frameWidth || y + height > frameHeight) throw new Error('ROI is outside the frame or too small');
  fs.mkdirSync(output, { recursive: true });
  for (const index of [indices[0], indices[Math.floor(indices.length / 2)], indices.at(-1)]) {
    command('ffmpeg', ['-v', 'error', '-i', video, '-vf', `select=eq(n\\,${index}),drawbox=x=${x}:y=${y}:w=${width + 1}:h=${height + 1}:color=red:t=2`, '-frames:v', '1', '-fps_mode', 'passthrough', '-y', path.join(output, `roi-frame-${index}.png`)]);
  }
  const process = spawn('ffmpeg', ['-v', 'error', '-i', video, '-an', '-sn', '-vf', `select=between(n\\,${indices[0]}\\,${indices.at(-1)}),format=bgr24,crop=${width}:${height}:${x}:${y}:exact=1`, '-fps_mode', 'passthrough', '-f', 'rawvideo', '-pix_fmt', 'bgr24', 'pipe:1']);
  let stderr = '', processError;
  process.stderr.on('data', chunk => { stderr += chunk.toString(); });
  const exited = new Promise(resolve => { process.on('error', error => { processError = error; resolve(-1); }); process.on('close', resolve); });
  const bytesPerFrame = width * height * 3, bytes = Buffer.allocUnsafe(bytesPerFrame);
  let filled = 0, decoded = 0, previous = null;
  const shifts = [], confidence = [], textures = [];
  try {
    for await (const chunk of process.stdout) {
      let offset = 0;
      while (offset < chunk.length) {
        const count = Math.min(bytesPerFrame - filled, chunk.length - offset);
        chunk.copy(bytes, filled, offset, offset + count); offset += count; filled += count;
        if (filled !== bytesPerFrame) continue;
        filled = 0;
        const patch = grayPatch(bytes), average = patch.reduce((a, b) => a + b, 0) / patch.length;
        textures.push(Math.sqrt(patch.reduce((sum, value) => sum + (value - average) ** 2, 0) / patch.length));
        if (previous !== null) { const result = phaseTranslation(previous, patch, width, height); shifts.push([result.dx, result.dy]); confidence.push(result.response); }
        previous = patch; decoded++;
      }
    }
  } catch (error) { process.kill(); await exited; throw error; }
  const status = await exited;
  if (status !== 0 || processError) throw new Error(`Cannot decode video: ${processError?.message ?? stderr}`);
  if (filled || decoded !== indices.length) throw new Error(`Decoded ${decoded} frames, expected ${indices.length}; trailing bytes ${filled}`);
  const xy = [[0, 0]];
  for (const [dx, dy] of shifts) { const previousXY = xy.at(-1); xy.push([previousXY[0] + dx, previousXY[1] + dy]); }
  const times = indices.map(i => timestamps[i]), dt = times.slice(1).map((t, i) => t - times[i]);
  const low = confidence.filter(value => value < .5).length;
  const report = {
    source: path.resolve(video), start_seconds: start, duration_seconds: duration, roi_xywh: roi, screen_width: frameWidth, screen_height: frameHeight,
    scope: 'Estimated background ROI translation; not lord position or ground-truth camera motion. Inspect first/middle/last ROI frames for entities, HUD and animation.',
    comparison_limit: 'Different input/scene/ROI makes cross-video CV causal attribution invalid; disclose unmatched conditions.',
    decoder: { implementation: 'ffmpeg CLI BGR24, then OpenCV-compatible 15-bit grayscale; ffprobe presentation timestamps', version: command('ffmpeg', ['-version']).split('\n')[0], compatibility_limit: 'The phase algorithm matches independent same-pixel reference fixtures. FFmpeg and historical OpenCV-bundled video decoders can produce different pixels; full-video numerical parity is not guaranteed. Compare reports made with the same decoder build.' },
    confidence: { method: 'OpenCV-compatible normalized 5x5 phase peak response, heuristic not probability; low <0.5; texture SD <5 is weak.', minimum_response: Math.min(...confidence), median_response: median(confidence), low_response_pairs: low, pairs: confidence.length, minimum_texture_sd: Math.min(...textures), status: low || Math.min(...textures) < 5 ? 'needs-inspection' : 'correlation-supported-not-ground-truth' },
    metrics: motionStatistics(xy, dt, frameHeight, true),
  };
  fs.writeFileSync(path.join(output, 'motion-report.json'), `${JSON.stringify(report, null, 2)}\n`);
  const rows = ['frame,seconds,dx,dy,response,background_speed_1080p,cumulative_x_1080p,cumulative_y_1080p'];
  shifts.forEach(([dx, dy], j) => rows.push([indices[j + 1], times[j + 1], dx, dy, confidence[j], Math.hypot(dx, dy) / dt[j] * 1080 / frameHeight, xy[j + 1][0] * 1080 / frameHeight, xy[j + 1][1] * 1080 / frameHeight].join(',')));
  fs.writeFileSync(path.join(output, 'motion-series.csv'), `${rows.join('\n')}\n`);
  return report;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.length !== 10) throw new Error('Usage: node tools/analyze-motion-video.mjs VIDEO START_SEC DURATION_SEC X Y W H OUTPUT_DIR');
  await analyze(process.argv[2], Number(process.argv[3]), Number(process.argv[4]), process.argv.slice(5, 9).map(Number), process.argv[9]);
}
