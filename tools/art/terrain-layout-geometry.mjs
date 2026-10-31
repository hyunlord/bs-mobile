// Native isometric pixels: one cell is 64 x 32; grid anchors may be fractional.
export const project = ([x, y]) => [32 * (x - y), 16 * (x + y)];
export const inverse = ([x, y]) => [x / 64 + y / 32, y / 32 - x / 64];
export function rectangle([x, y, width, height]) {
  return [[x, y], [x + width, y], [x + width, y + height], [x, y + height]];
}
export function polygonsOverlap(a, b) {
  // Separating-axis test; touching edges have zero area and are allowed.
  for (const polygon of [a, b]) {
    for (let i = 0; i < polygon.length; i++) {
      const p = polygon[i], q = polygon[(i + 1) % polygon.length];
      const axis = [q[1] - p[1], p[0] - q[0]];
      const aa = a.map(v => v[0] * axis[0] + v[1] * axis[1]);
      const bb = b.map(v => v[0] * axis[0] + v[1] * axis[1]);
      if (Math.max(...aa) <= Math.min(...bb) + 1e-9 || Math.max(...bb) <= Math.min(...aa) + 1e-9) return false;
    }
  }
  return true;
}
export function footprintPolygon(instance) {
  const [x, y] = instance.grid, [width, height] = instance.footprint;
  return rectangle([x - width / 2, y - height / 2, width, height]);
}
export function spritePolygon(instance, asset) {
  const origin = project(instance.grid);
  const size = asset.size.map((n, i) => n * instance.scale[i]);
  const topLeft = origin.map((n, i) => n - asset.anchor[i] * instance.scale[i]);
  const center = topLeft.map((n, i) => n + size[i] / 2);
  const angle = instance.rotationRadians, cosine = Math.cos(angle), sine = Math.sin(angle);
  return rectangle([-size[0] / 2, -size[1] / 2, ...size]).map(([x, y]) => inverse([
    center[0] + x * cosine - y * sine, center[1] + x * sine + y * cosine,
  ]));
}
export function exclusionReasons(instance, reservations) {
  if (!['prop', 'cluster', 'building-prop'].includes(instance.category)) return [];
  const polygon = footprintPolygon(instance);
  return reservations.filter(r => polygonsOverlap(polygon, r.polygon)).map(r => r.id);
}
