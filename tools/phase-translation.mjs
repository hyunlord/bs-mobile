/** Phase-only translation with a square-root Hann window and 0.01-pixel local DFT refinement.
 * Confidence reproduces the OpenCV phaseCorrelate normalized 5x5 peak-neighborhood sum.
 * Algorithm reference: https://github.com/opencv/opencv/blob/4.x/modules/imgproc/src/phasecorr.cpp
 * No native modules or installed analysis packages are required.
 */
const plans = new Map();
function radix2(re, im, inverse) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) { [re[i], re[j]] = [re[j], re[i]]; [im[i], im[j]] = [im[j], im[i]]; }
  }
  for (let size = 2; size <= n; size *= 2) {
    const angle = (inverse ? 2 : -2) * Math.PI / size, cr = Math.cos(angle), ci = Math.sin(angle);
    for (let offset = 0; offset < n; offset += size) {
      let wr = 1, wi = 0;
      for (let k = 0; k < size / 2; k++) {
        const a = offset + k, b = a + size / 2, tr = wr * re[b] - wi * im[b], ti = wr * im[b] + wi * re[b];
        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
        const next = wr * cr - wi * ci; wi = wr * ci + wi * cr; wr = next;
      }
    }
  }
  if (inverse) for (let i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
}
function transform(re, im, inverse = false) {
  const n = re.length;
  if ((n & (n - 1)) === 0) { radix2(re, im, inverse); return; }
  if (inverse) {
    for (let i = 0; i < n; i++) im[i] = -im[i];
    transform(re, im);
    for (let i = 0; i < n; i++) { re[i] /= n; im[i] /= -n; }
    return;
  }
  let plan = plans.get(n);
  if (!plan) {
    let length = 1; while (length < n * 2 - 1) length *= 2;
    const cos = new Float64Array(n), sin = new Float64Array(n), br = new Float64Array(length), bi = new Float64Array(length);
    for (let i = 0; i < n; i++) {
      const angle = Math.PI * (i * i % (n * 2)) / n;
      cos[i] = Math.cos(angle); sin[i] = Math.sin(angle); br[i] = cos[i]; bi[i] = sin[i];
      if (i) { br[length - i] = cos[i]; bi[length - i] = sin[i]; }
    }
    radix2(br, bi, false); plan = { length, cos, sin, br, bi }; plans.set(n, plan);
  }
  const ar = new Float64Array(plan.length), ai = new Float64Array(plan.length);
  for (let i = 0; i < n; i++) { ar[i] = re[i] * plan.cos[i] + im[i] * plan.sin[i]; ai[i] = im[i] * plan.cos[i] - re[i] * plan.sin[i]; }
  radix2(ar, ai, false);
  for (let i = 0; i < ar.length; i++) { const r = ar[i] * plan.br[i] - ai[i] * plan.bi[i]; ai[i] = ar[i] * plan.bi[i] + ai[i] * plan.br[i]; ar[i] = r; }
  radix2(ar, ai, true);
  for (let i = 0; i < n; i++) { re[i] = ar[i] * plan.cos[i] + ai[i] * plan.sin[i]; im[i] = ai[i] * plan.cos[i] - ar[i] * plan.sin[i]; }
}
export function fft2(real, imaginary, width, height, inverse = false) {
  const re = Float64Array.from(real), im = Float64Array.from(imaginary ?? new Float64Array(real.length));
  for (let y = 0; y < height; y++) transform(re.subarray(y * width, (y + 1) * width), im.subarray(y * width, (y + 1) * width), inverse);
  const cr = new Float64Array(height), ci = new Float64Array(height);
  for (let x = 0; x < width; x++) {
    for (let y = 0; y < height; y++) { cr[y] = re[y * width + x]; ci[y] = im[y * width + x]; }
    transform(cr, ci, inverse);
    for (let y = 0; y < height; y++) { re[y * width + x] = cr[y]; im[y * width + x] = ci[y]; }
  }
  return { re, im };
}
function crossSpectrum(a, b, width, height, epsilonMode = false) {
  const first = fft2(a, null, width, height), second = fft2(b, null, width, height);
  const re = new Float64Array(a.length), im = new Float64Array(a.length);
  for (let i = 0; i < a.length; i++) {
    const r = second.re[i] * first.re[i] + second.im[i] * first.im[i], q = second.im[i] * first.re[i] - second.re[i] * first.im[i];
    const magnitude = Math.hypot(r, q);
    const scale = epsilonMode ? magnitude / (magnitude * magnitude + Number.EPSILON) : 1 / Math.max(magnitude, 1e-12);
    re[i] = r * scale; im[i] = q * scale;
  }
  return { re, im };
}
function optimalSize(n) { for (;; n++) { let rest = n; for (const factor of [2, 3, 5]) while (rest % factor === 0) rest /= factor; if (rest === 1) return n; } }
function confidence(a, b, width, height) {
  const w = optimalSize(width), h = optimalSize(height), pa = new Float64Array(w * h), pb = new Float64Array(w * h);
  for (let y = 0; y < height; y++) { pa.set(a.subarray(y * width, (y + 1) * width), y * w); pb.set(b.subarray(y * width, (y + 1) * width), y * w); }
  const spectrum = crossSpectrum(pb, pa, w, h, true), raw = fft2(spectrum.re, spectrum.im, w, h, true).re;
  let peak = -Infinity, px = 0, py = 0;
  const at = (x, y) => raw[((y + Math.ceil(h / 2)) % h) * w + (x + Math.ceil(w / 2)) % w];
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) if (at(x, y) > peak) { peak = at(x, y); px = x; py = y; }
  let sum = 0;
  for (let y = Math.max(0, py - 2); y <= Math.min(h - 1, py + 2); y++) for (let x = Math.max(0, px - 2); x <= Math.min(w - 1, px + 2); x++) sum += at(x, y);
  return sum;
}
function refine(spectrum, width, height, dx, dy, spacing) {
  const leftRe = new Float64Array(21 * width), leftIm = new Float64Array(21 * width);
  for (let iy = 0; iy < 21; iy++) for (let y = 0; y < height; y++) {
    const frequency = (y < Math.ceil(height / 2) ? y : y - height) / height;
    const angle = 2 * Math.PI * (dy + (iy - 10) * spacing) * frequency, cr = Math.cos(angle), ci = Math.sin(angle);
    for (let x = 0; x < width; x++) { const i = y * width + x, j = iy * width + x; leftRe[j] += cr * spectrum.re[i] - ci * spectrum.im[i]; leftIm[j] += cr * spectrum.im[i] + ci * spectrum.re[i]; }
  }
  const xr = new Float64Array(width * 21), xi = new Float64Array(width * 21);
  for (let x = 0; x < width; x++) for (let ix = 0; ix < 21; ix++) {
    const frequency = (x < Math.ceil(width / 2) ? x : x - width) / width, angle = 2 * Math.PI * frequency * (dx + (ix - 10) * spacing);
    xr[x * 21 + ix] = Math.cos(angle); xi[x * 21 + ix] = Math.sin(angle);
  }
  let peak = -Infinity, bestX = 0, bestY = 0;
  for (let iy = 0; iy < 21; iy++) for (let ix = 0; ix < 21; ix++) {
    let sum = 0;
    for (let x = 0; x < width; x++) sum += leftRe[iy * width + x] * xr[x * 21 + ix] - leftIm[iy * width + x] * xi[x * 21 + ix];
    if (sum > peak) { peak = sum; bestX = ix; bestY = iy; }
  }
  return { dx: dx + (bestX - 10) * spacing, dy: dy + (bestY - 10) * spacing };
}
export function phaseTranslation(previous, current, width, height) {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width < 16 || height < 16 || previous.length !== width * height || current.length !== previous.length || !previous.every(Number.isFinite) || !current.every(Number.isFinite)) throw new Error('Phase correlation needs equal finite 2D patches at least 16 pixels wide/high');
  const a = new Float64Array(previous.length), b = new Float64Array(previous.length);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const window = Math.sqrt((.5 - .5 * Math.cos(2 * Math.PI * x / (width - 1))) * (.5 - .5 * Math.cos(2 * Math.PI * y / (height - 1))));
    const i = y * width + x; a[i] = previous[i] * window; b[i] = current[i] * window;
  }
  const spectrum = crossSpectrum(a, b, width, height), surface = fft2(spectrum.re, spectrum.im, width, height, true).re;
  let peak = 0; for (let i = 1; i < surface.length; i++) if (surface[i] > surface[peak]) peak = i;
  const x = peak % width, y = Math.floor(peak / width);
  let shift = { dx: x <= Math.floor(width / 2) ? x : x - width, dy: y <= Math.floor(height / 2) ? y : y - height };
  for (const spacing of [.1, .01]) shift = refine(spectrum, width, height, shift.dx, shift.dy, spacing);
  return { ...shift, response: confidence(a, b, width, height) };
}
