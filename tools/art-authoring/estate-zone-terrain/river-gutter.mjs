// Fill only atlas padding. Sprite crop pixels and registered geometry remain byte-identical.
export function extrudeGutter(atlas, rect, padding = 1) {
  const [x, y, width, height] = rect;
  if (!Number.isInteger(padding) || padding < 1 || x < padding || y < padding ||
      x + width + padding > atlas.width || y + height + padding > atlas.height)
    throw new Error('River gutter exceeds allocated atlas padding');
  for (let dy = -padding; dy < height + padding; dy++) {
    for (let dx = -padding; dx < width + padding; dx++) {
      if (dx >= 0 && dx < width && dy >= 0 && dy < height) continue;
      const sx = x + Math.max(0, Math.min(width - 1, dx));
      const sy = y + Math.max(0, Math.min(height - 1, dy));
      atlas.pixels.copy(atlas.pixels, ((y + dy) * atlas.width + x + dx) * 4,
        (sy * atlas.width + sx) * 4, (sy * atlas.width + sx) * 4 + 4);
    }
  }
}
