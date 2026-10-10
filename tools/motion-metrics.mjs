/** Reproducible #204 statistics. No resampling, stop CV, or undefined-ACF pass. */
export const mean = values => values.reduce((sum, value) => sum + value, 0) / values.length;
export const step = (a, b) => Math.hypot(b[0] - a[0], b[1] - a[1]);
export function motionStatistics(position, dt, height, moving) {
  if (position.length < 16 || position.length !== dt.length + 1 || !(height > 0)
      || position.some(p => p.length !== 2 || p.some(x => !Number.isFinite(x)))
      || dt.some(x => !Number.isFinite(x) || x <= 0)) throw new Error('Need >=16 finite XY samples, positive dt per interval and height');
  const steps = dt.map((_, i) => step(position[i], position[i + 1]));
  const speeds = steps.map((value, i) => value / dt[i]);
  const average = mean(speeds), centered = speeds.map(x => x - average);
  const energy = centered.reduce((sum, x) => sum + x * x, 0);
  const acf = Object.fromEntries(Array.from({ length: 11 }, (_, index) => {
    const lag = index + 2;
    return [String(lag), energy > 1e-16 && centered.length > lag
      ? centered.slice(lag).reduce((sum, x, i) => sum + x * centered[i], 0) / energy : null];
  }));
  const residual = position.slice(7, -7).map((p, i) => {
    const neighbors = position.slice(i, i + 15);
    return step(p, [mean(neighbors.map(x => x[0])), mean(neighbors.map(x => x[1]))]) ** 2;
  });
  return { phase: moving ? 'moving' : 'stop-or-settling', intervals: dt.length,
    mean_speed_pixels_per_second: average,
    speed_cv: moving && average > 1e-8 ? Math.sqrt(energy / speeds.length) / average : null,
    speed_autocorrelation: acf, screen_rms_1080p: Math.sqrt(mean(residual)) * 1080 / height,
    maximum_step_1080p: Math.max(...steps) * 1080 / height,
    duration_seconds: dt.reduce((sum, x) => sum + x, 0),
    rms_method: 'Euclidean residual from centered 15-frame moving average; seven edge samples each side excluded; scale=1080/height',
    autocorrelation_method: 'Biased autocovariance divided by lag-zero sum; speed=Euclidean displacement / actual dt; lags 2..12' };
}
export function velocityGate(stats) {
  const values = Object.values(stats.speed_autocorrelation), valid = values.filter(x => x !== null);
  const acf = valid.length ? Math.max(...valid) : null;
  return { cv: stats.speed_cv, maximum_positive_acf_lags_2_to_12: acf,
    status: stats.speed_cv !== null && valid.length === values.length && acf !== null
      ? stats.speed_cv <= .10 && acf <= .3 ? 'pass' : 'fail' : 'undefined-not-pass' };
}
export function constantInputWindows(frames, direction = [1, 0]) {
  const groups = [];
  for (const row of frames) {
    if (![0, 2].includes(Number(row.phase))) continue;
    const prior = groups.at(-1)?.at(-1);
    if (!prior || Number(row.frame) !== Number(prior.frame) + 1 || row.phase !== prior.phase
        || row.focused !== prior.focused || row.paused !== prior.paused
        || Math.abs(Number(row.inputX) - Number(prior.inputX)) > 1e-5
        || Math.abs(Number(row.inputY) - Number(prior.inputY)) > 1e-5) groups.push([]);
    groups.at(-1).push(row);
  }
  return groups.map(group => {
    const first = group[0], last = group.at(-1), phase = Number(first.phase);
    const start = Number(first.wallSeconds), end = Number(last.wallSeconds);
    const x = Number(first.inputX), y = Number(first.inputY), magnitude = phase === 0 ? .25 : 1;
    let status = 'eligible-candidate';
    if (Math.abs(x - direction[0] * magnitude) > .005 || Math.abs(y - direction[1] * magnitude) > .005) status = 'wrong-or-zero-actual-input';
    else if (group.some(row => row.focused?.toLowerCase() !== 'true')) status = 'unfocused-or-focus-unrecorded';
    else if (group.some(row => row.paused?.toLowerCase() !== 'false')) status = 'paused-or-pause-unrecorded';
    else if (end - start < 3.5) status = 'insufficient-duration-for-three-seconds-plus-onset';
    const boundary = Math.max(0, group.findLastIndex(row => Number(row.wallSeconds) <= end - 3));
    const onsetSeconds = Number(group[boundary].wallSeconds) - start;
    if (status === 'eligible-candidate' && onsetSeconds < .5) status = 'insufficient-onset-after-frame-boundary-selection';
    const selected = status === 'eligible-candidate' ? group.slice(boundary).map(row => Number(row.frame)) : [];
    return { phase, first_frame: Number(first.frame), last_frame: Number(last.frame), input_x: x, input_y: y,
      constant_input_seconds: end - start, status, selected_frames: selected,
      onset_frames: (selected.length ? group.slice(0, boundary + 1) : group).map(row => Number(row.frame)),
      onset_seconds: selected.length ? onsetSeconds : end - start };
  });
}
