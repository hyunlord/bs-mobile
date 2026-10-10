import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { motionStatistics, constantInputWindows, velocityGate, mean, step } from './motion-metrics.mjs';

/** These trace CSVs contain only unquoted scalar fields; reject unsupported rows. */
export function readTraceCsv(path) {
  const [header, ...lines] = readFileSync(path, 'utf8').trim().split(/\r?\n/);
  const names = header.split(',');
  return lines.map(line => {
    const values = line.split(',');
    if (values.length !== names.length || line.includes('"')) throw new Error(`Malformed scalar trace CSV: ${path}`);
    return Object.fromEntries(names.map((name, i) => [name, values[i]]));
  });
}
const xy = (rows, x, y) => rows.map(row => [Number(row[x]), Number(row[y])]);
const deltas = rows => rows.slice(1).map((row, i) => Number(row.wallSeconds) - Number(rows[i].wallSeconds));
export function analyze(folder, output) {
  const frames = readTraceCsv(join(folder, 'normal-frames.csv'));
  const entities = readTraceCsv(join(folder, 'normal-entities.csv'));
  const metadataPath = join(folder, 'normal-trace.txt');
  const metadata = existsSync(metadataPath) ? Object.fromEntries(readFileSync(metadataPath, 'utf8').split(/\r?\n/).filter(line => line.includes('=')).map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)])) : {};
  const direction = [Number(metadata.commandedDirectionX ?? 1), Number(metadata.commandedDirectionY ?? 0)];
  if (direction.some(x => !Number.isFinite(x)) || Math.abs(Math.hypot(...direction) - 1) > 1e-5) throw new Error('Commanded direction must be a finite unit vector');
  const byFrame = new Map(frames.map(row => [Number(row.frame), row]));
  if (byFrame.size !== frames.length) throw new Error('Duplicate frame IDs');
  const entityKey = row => `${row.kind}:${row.id}:${row.frame}`;
  const entityIndex = new Map(entities.map(row => [entityKey(row), row]));
  if (entityIndex.size !== entities.length) throw new Error('Duplicate entity frame IDs');
  if (entities.some(row => !byFrame.has(Number(row.frame)))) throw new Error('Entity has no frame');
  const keys = [...new Set(entities.map(row => `${row.kind}:${row.id}`))].sort();
  const results = [], series = [];
  for (const key of keys) {
    const [kind, idText] = key.split(':'), id = Number(idText);
    const selected = entities.filter(row => row.kind === kind && Number(row.id) === id);
    for (const phase of [...new Set(frames.map(row => Number(row.phase)))].sort((a, b) => a - b)) {
      const groups = [];
      for (const row of selected.filter(row => Number(byFrame.get(Number(row.frame)).phase) === phase)) {
        if (!groups.length || Number(row.frame) !== Number(groups.at(-1).at(-1).frame) + 1) groups.push([]);
        groups.at(-1).push(row);
      }
      groups.forEach((group, segment) => {
        const linked = group.map(row => byFrame.get(Number(row.frame))), moving = [0, 2].includes(phase);
        const identity = { kind, id, phase, segment, samples: group.length, first_frame: Number(group[0].frame), last_frame: Number(group.at(-1).frame), excluded_samples: 0,
          nonzero_input_samples: linked.filter(row => Number(row.inputX) !== 0 || Number(row.inputY) !== 0).length };
        if (group.length < 16) { results.push({ ...identity, status: 'insufficient-contiguous-samples' }); return; }
        const heights = new Set(linked.map(row => Number(row.height)));
        if (heights.size !== 1) throw new Error('Viewport height changed inside phase');
        const height = [...heights][0], dt = deltas(linked), screen = xy(group, 'screenX', 'screenY'), world = xy(group, 'worldX', 'worldY');
        const screenStats = motionStatistics(screen, dt, height, moving), worldStats = motionStatistics(world, dt, 1080, moving);
        const cameraStats = motionStatistics(xy(linked, 'cameraX', 'cameraY'), dt, 1080, moving);
        results.push({ ...identity, status: 'measured', screen: screenStats, world_speed_cv: worldStats.speed_cv,
          world_mean_speed_units_per_second: worldStats.mean_speed_pixels_per_second,
          world_speed_autocorrelation: worldStats.speed_autocorrelation, camera_speed_cv: cameraStats.speed_cv, camera_speed_autocorrelation: cameraStats.speed_autocorrelation });
        for (let i = 1; i < group.length; i++) series.push([kind, id, phase, segment, Number(group[i].frame), Number(linked[i].wallSeconds), step(screen[i - 1], screen[i]) / dt[i - 1] * 1080 / height, step(world[i - 1], world[i]) / dt[i - 1], screen[i][0] * 1080 / height, screen[i][1] * 1080 / height]);
      });
    }
  }
  const windows = constantInputWindows(frames, direction), metrics = [];
  for (const window of windows) for (const [scope, selected] of [['stationary-candidate', window.selected_frames], ['onset-or-ineligible', window.onset_frames]]) {
    if (selected.length < 16) continue;
    for (const key of keys) {
      const [kind, idText] = key.split(':'), id = Number(idText);
      if (!selected.every(frame => entityIndex.has(`${key}:${frame}`))) continue;
      const linked = selected.map(frame => byFrame.get(frame)), observed = selected.map(frame => entityIndex.get(`${key}:${frame}`));
      const dt = deltas(linked), world = xy(observed, 'worldX', 'worldY');
      const worldStats = motionStatistics(world, dt, 1080, true), cameraStats = motionStatistics(xy(linked, 'cameraX', 'cameraY'), dt, 1080, true);
      const screenStats = motionStatistics(xy(observed, 'screenX', 'screenY'), dt, Number(linked[0].height), true);
      const speed = dt.map((value, i) => step(world[i], world[i + 1]) / value), half = Math.floor(speed.length / 2), average = mean(speed);
      metrics.push({ kind, id, phase: window.phase, scope, selection: window, selected_first_frame: selected[0], selected_last_frame: selected.at(-1), selected_samples: selected.length,
        selected_seconds: Number(linked.at(-1).wallSeconds) - Number(linked[0].wallSeconds), world: worldStats, camera: cameraStats, screen: screenStats,
        world_second_half_speed_change_ratio: average > 1e-8 ? (mean(speed.slice(half)) - mean(speed.slice(0, half))) / average : null,
        maximum_camera_shake_world_units: linked.every(row => 'cameraShakeX' in row && 'cameraShakeY' in row) ? Math.max(...linked.map(row => Math.hypot(Number(row.cameraShakeX), Number(row.cameraShakeY)))) : null,
        shake_subtracted: false, world_gate: velocityGate(worldStats), camera_gate: velocityGate(cameraStats), lord_screen_rms_gate: kind === 'lord' ? screenStats.screen_rms_1080p <= .5 ? 'pass' : 'fail' : 'not-lord',
        gate_scope: 'Only stationary-candidate is eligible; onset/ineligible metrics are diagnostic, never acceptance. Near-zero screen speed CV is not a lord motion gate.' });
    }
  }
  const report = { source: resolve(folder), scope: 'Native recorded presentation, not physical input-to-photon.', commanded_direction: direction,
    direction_provenance: 'normal-trace.txt commandedDirectionX/Y; historical absent metadata defaults to cardinal right',
    phase_contract: '-1 setup,0 commanded-direction25%,1 release,2 commanded-direction100%,3 release,4 release remainder; durations from CSV, no hardcoded block length; all raw phase samples retained.',
    stationary_selection: 'Actual constant metadata-commanded input within0.005; focused/unpaused and contiguous; final at least3s with at least0.5s preceding onset. Full-phase and onset results retained. Last3s is a stationary candidate, not proof of stationarity; half-window drift and shake disclosed.',
    constant_input_windows: windows, window_metrics: metrics,
    normalization: 'Screen positions/speed normalized by 1080/actual height; world/camera units unchanged.',
    stop_contract: 'CV is null in release/settling; RMS and raw motion remain visible. Undefined constant-speed ACF is null.', results };
  mkdirSync(output, { recursive: true });
  writeFileSync(join(output, 'motion-report.json'), JSON.stringify(report, null, 2) + '\n');
  writeFileSync(join(output, 'motion-series.csv'), ['kind,id,phase,segment,frame,seconds,screen_speed_1080p,world_speed,screen_x_1080p,screen_y_1080p', ...series.map(row => row.join(','))].join('\n') + '\n');
  return report;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  if (process.argv.length !== 4) throw new Error('Usage: node tools/analyze-motion-trace.mjs TRACE_DIR OUTPUT_DIR');
  analyze(process.argv[2], process.argv[3]);
}
