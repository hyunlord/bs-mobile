import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { readTraceCsv } from './analyze-motion-trace.mjs';
import { mean, step } from './motion-metrics.mjs';
const escape = text => String(text).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
/** Pairs JSON: [{label,before,after}], each path an analysis directory; never mix native/video in one panel. */
export function plot(pairs, output) {
  const width = 1200, rowHeight = 320, height = 70 + pairs.length * rowHeight;
  const parts = [`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><rect width="100%" height="100%" fill="white"/><style>text{font:13px sans-serif;fill:#222}.title{font-size:18px;font-weight:bold}</style><text x="24" y="26" class="title">Motion evidence: actual samples, no interpolation</text><text x="24" y="48">Blue: before · orange: after. Native and estimated video use separate panels; full phases include onset and stop.</text>`];
  pairs.forEach((pair, index) => {
    const datasets = [pair.before, pair.after].map(folder => {
      let rows = readTraceCsv(`${folder}/motion-series.csv`);
      const video = 'background_speed_1080p' in rows[0];
      if (!video) rows = rows.filter(row => row.kind === 'lord' && row.id === '0');
      if (!rows.length) throw new Error('Missing motion series');
      const origin = Number(rows[0].seconds), speedKey = video ? 'background_speed_1080p' : 'world_speed';
      const position = rows.map(row => [Number(row[video ? 'cumulative_x_1080p' : 'screen_x_1080p']), Number(row[video ? 'cumulative_y_1080p' : 'screen_y_1080p'])]);
      const speedGroups = [], residualGroups = [], groups = [];
      rows.forEach((row, i) => {
        const prior = groups.at(-1)?.at(-1);
        if (prior === undefined || (!video && (row.phase !== rows[prior].phase || row.segment !== rows[prior].segment))) groups.push([]);
        groups.at(-1).push(i);
      });
      for (const group of groups) {
        speedGroups.push(group.map(i => [Number(rows[i].seconds) - origin, Number(rows[i][speedKey])]));
        if (group.length < 15) continue;
        residualGroups.push(group.slice(7, -7).map((i, j) => {
          const neighbors = group.slice(j, j + 15).map(k => position[k]);
          return [Number(rows[i].seconds) - origin, step(position[i], [mean(neighbors.map(p => p[0])), mean(neighbors.map(p => p[1]))])];
        }));
      }
      return { video, speedGroups, residualGroups };
    });
    if (datasets[0].video !== datasets[1].video) throw new Error('Native/video cannot share one comparison panel');
    const top = 80 + index * rowHeight;
    parts.push(`<text x="24" y="${top}" class="title">${escape(pair.label)} (${datasets[0].video ? 'video estimated background' : 'native lord'})</text>`);
    for (const [column, field, label] of [[0, 'speedGroups', datasets[0].video ? 'Estimated background speed (1080p px/s)' : 'World speed (Unity units/s)'], [1, 'residualGroups', 'Screen 15-frame MA residual (1080p px)']]) {
      const left = 65 + column * 590, y = top + 35, w = 500, h = 205;
      const points = datasets.flatMap(data => data[field].flat()), xmax = Math.max(...points.map(p => p[0]), 1e-8), ymax = Math.max(...points.map(p => p[1]), 1e-8);
      parts.push(`<text x="${left}" y="${y - 10}">${escape(label)}</text><rect x="${left}" y="${y}" width="${w}" height="${h}" fill="none" stroke="#bbb"/><text x="${left}" y="${y + h + 20}">0</text><text x="${left + w - 75}" y="${y + h + 20}">${xmax.toFixed(2)} seconds</text><text x="${left}" y="${y + 15}">max ${ymax.toPrecision(4)}</text>`);
      datasets.forEach((data, d) => data[field].forEach(group => {
        const coordinates = group.map(([time, value]) => `${(left + time / xmax * w).toFixed(3)},${(y + h - value / ymax * h).toFixed(3)}`).join(' ');
        parts.push(`<polyline points="${coordinates}" fill="none" stroke="${d ? '#c85a10' : '#2367ae'}" stroke-width="1" opacity=".8"/>`);
      }));
    }
  });
  parts.push('</svg>'); mkdirSync(dirname(output), { recursive: true }); writeFileSync(output, parts.join('\n'));
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  if (process.argv.length !== 4) throw new Error('Usage: node tools/plot-motion-comparison.mjs PAIRS.json OUTPUT.svg');
  plot(JSON.parse(readFileSync(process.argv[2], 'utf8')), process.argv[3]);
}
