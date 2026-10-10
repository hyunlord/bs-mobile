import test from 'node:test';
import assert from 'node:assert/strict';
import { motionStatistics, velocityGate, constantInputWindows } from './motion-metrics.mjs';
const samples = (count = 241) => Array.from({ length: count }, (_, i) => ({ frame: String(i), phase: '0', wallSeconds: String(i / 60), inputX: '.25', inputY: '0', focused: 'True', paused: 'False' }));
test('constant velocity has undefined ACF, never a false pass', () => {
  const stats = motionStatistics(Array.from({ length: 181 }, (_, i) => [i, 0]), Array(180).fill(1 / 60), 1080, true);
  assert.equal(stats.speed_cv, 0);
  assert.equal(velocityGate(stats).status, 'undefined-not-pass');
  assert.ok(stats.screen_rms_1080p < 1e-12);
});
test('stop CV is undefined and residual remains measurable', () => {
  const stats = motionStatistics(Array.from({ length: 31 }, (_, i) => [i % 2, 0]), Array(30).fill(1 / 60), 1080, false);
  assert.equal(stats.speed_cv, null);
  assert.ok(stats.screen_rms_1080p > 0);
});
test('invalid dt and nonfinite coordinates fail', () => {
  assert.throws(() => motionStatistics(Array(16).fill([0, 0]), Array(15).fill(0), 1080, true));
  assert.throws(() => motionStatistics(Array(16).fill([NaN, 0]), Array(15).fill(1), 1080, true));
});
test('periodic speed fails lag-two gate', () => {
  const positions = [[0, 0]];
  for (let i = 0; i < 180; i++) positions.push([positions.at(-1)[0] + (i % 2 ? 2 : 1), 0]);
  const stats = motionStatistics(positions, Array(180).fill(1 / 60), 1080, true);
  assert.ok(stats.speed_autocorrelation['2'] > .9);
  assert.equal(velocityGate(stats).status, 'fail');
});
test('last three seconds preserve one second onset', () => {
  const window = constantInputWindows(samples())[0];
  assert.equal(window.status, 'eligible-candidate');
  assert.equal(window.selected_frames[0], 60);
  assert.equal(window.selected_frames.at(-1), 240);
  assert.equal(window.onset_seconds, 1);
});
test('focus gap is never stitched and zero input is invalid', () => {
  const frames = samples(); frames[120].focused = 'False';
  assert.ok(constantInputWindows(frames).every(window => !window.selected_frames.length));
  assert.equal(constantInputWindows(samples().map(row => ({ ...row, inputX: '0' })))[0].status, 'wrong-or-zero-actual-input');
});
test('oblique input requires matching explicit direction', () => {
  const frames = samples().map(row => ({ ...row, inputX: '.2', inputY: '.15' }));
  assert.equal(constantInputWindows(frames)[0].status, 'wrong-or-zero-actual-input');
  assert.equal(constantInputWindows(frames, [.8, .6])[0].status, 'eligible-candidate');
});

test('native analyzer retains full phases and validates metadata', async () => {
  const { mkdtempSync, writeFileSync, readFileSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { analyze } = await import('./analyze-motion-trace.mjs');
  const root = mkdtempSync(join(tmpdir(), 'motion-204-'));
  try {
    const frames = ['frame,phase,wallSeconds,inputX,inputY,focused,paused,height,cameraX,cameraY'];
    const entities = ['frame,kind,id,worldX,worldY,screenX,screenY'];
    for (let i = 0; i < 300; i++) {
      const moving = i <= 240;
      frames.push([i, moving ? 0 : 1, i / 60, moving ? .2 : 0, moving ? .15 : 0, 'True', 'False', 1080, 0, 0].join(','));
      entities.push([i, 'lord', 0, Math.min(i, 240) * .2, Math.min(i, 240) * .15, 30, 40].join(','));
    }
    writeFileSync(join(root, 'normal-frames.csv'), frames.join('\n'));
    writeFileSync(join(root, 'normal-entities.csv'), entities.join('\n'));
    writeFileSync(join(root, 'normal-trace.txt'), 'commandedDirectionX=.8\ncommandedDirectionY=.6\n');
    const report = analyze(root, join(root, 'out'));
    assert.equal(report.results.reduce((sum, row) => sum + row.samples, 0), 300);
    assert.equal(report.results[1].screen.speed_cv, null);
    assert.equal(report.constant_input_windows[0].status, 'eligible-candidate');
    assert.deepEqual(JSON.parse(readFileSync(join(root, 'out/motion-report.json'))).commanded_direction, [.8, .6]);
    writeFileSync(join(root, 'normal-trace.txt'), 'commandedDirectionX=NaN\ncommandedDirectionY=.6\n');
    assert.throws(() => analyze(root, join(root, 'bad')));
  } finally { rmSync(root, { recursive: true, force: true }); }
});
