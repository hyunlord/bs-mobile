// Compare supplied boards with current Editor captures; never adjudicates visual approval.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const checkOnly = args.includes('--check-only');
const positional = args.filter(value => value !== '--check-only');
if (positional.length > 2 || positional.some(value => value.startsWith('--'))) throw new Error('Usage: node tools/render-portrait-reference.mjs [capture-root] [output] [--check-only]');
const root = path.resolve(positional[0] ?? path.join(repo, 'artifacts/portrait-234'));
const output = path.resolve(positional[1] ?? path.join(root, 'comparison'));
const design = path.join(repo, 'docs/design/portrait-v0');
const inventory = JSON.parse(fs.readFileSync(path.join(repo, 'docs/review/portrait-234-elements.json'), 'utf8'));
if (inventory.screens.length !== 13 || new Set(inventory.screens.map(screen => screen.id)).size !== 13) throw new Error('Expected 13 unique screen inventories');
const digest = buffer => createHash('sha256').update(buffer).digest('hex');
function png(file, width, height) {
  const data = fs.readFileSync(file);
  if (data.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a' || data.readUInt32BE(16) !== width || data.readUInt32BE(20) !== height) throw new Error(`Unexpected PNG dimensions: ${file}; expected ${width}x${height}`);
  return { file: path.relative(repo, file), sha256: digest(data), uri: `data:image/png;base64,${data.toString('base64')}` };
}
const assetRoot = path.join(repo, 'unity/Assets/Resources/PortraitV0');
const assets = fs.readdirSync(assetRoot).filter(name => /\.(png|jpg)$/i.test(name)).sort().map(name => {
  const runtime = fs.readFileSync(path.join(assetRoot, name));
  const original = fs.readFileSync(path.join(design, 'source/a', name));
  if (!runtime.equals(original)) throw new Error(`Runtime asset differs from supplied source: ${name}`);
  return { name, sha256: digest(runtime), byteIdentical: true };
});
if (!assets.length) throw new Error('No runtime PortraitV0 assets found');
const screens = inventory.screens.map(screen => {
  const captureBase = path.join(root, `editor-${screen.group}`, screen.capture);
  const actual = png(`${captureBase}-390x844.png`, 390, 844);
  const long = png(`${captureBase}-1080x2520.png`, 1080, 2520);
  const reference = png(path.join(design, 'boards', screen.board), 780, 1688);
  const provenance = fs.readFileSync(`${captureBase}-390x844.png.txt`, 'utf8').trim();
  const longProvenance = fs.readFileSync(`${captureBase}-1080x2520.png.txt`, 'utf8').trim();
  if (!provenance || !longProvenance) throw new Error(`Missing capture provenance: ${screen.id}`);
  return { ...screen, reference, actual, long, provenance, longProvenance };
});
if (checkOnly) {
  console.log(`PORTRAIT_REFERENCE_INPUTS_VALID screens=${screens.length} byteIdenticalAssets=${assets.length}; visual approval not evaluated`);
} else {
  const require = createRequire(import.meta.url);
  const candidates = [process.env.PORTRAIT_PLAYWRIGHT, path.join(os.homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')].filter(Boolean);
  let playwright;
  try { playwright = require('playwright'); }
  catch (error) {
    if (error.code !== 'MODULE_NOT_FOUND') throw error;
    const installed = candidates.find(candidate => fs.existsSync(path.join(candidate, 'package.json')));
    if (!installed) throw new Error('Existing Playwright is required. Set PORTRAIT_PLAYWRIGHT to its installed package directory; no installation is performed.');
    playwright = require(installed);
  }
  fs.mkdirSync(output, { recursive: true });
  const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;');
  const pair = (screen, actual) => `<main class="pair"><img alt="제공 설계" src="${screen.reference.uri}"><img alt="Editor 실제 캡처" src="${actual.uri}"></main>`;
  const html = body => `<!doctype html><meta charset="utf-8"><style>html,body{margin:0;background:#202020;color:#fff;font-family:Arial,sans-serif}.pair{display:flex;width:780px;height:844px}.pair img{width:390px;height:844px;object-fit:contain;display:block}h1{font-size:22px}a{color:#c8dbff}.grid{display:grid;grid-template-columns:repeat(3,390px);gap:12px}.tile img{display:block;width:390px;height:422px}.tile p{margin:4px 0 8px;font-size:14px}</style>${body}`;
  const browser = await playwright.chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 780, height: 844 }, deviceScaleFactor: 1 });
    for (const screen of screens) {
      for (const [suffix, actual] of [['', screen.actual], ['-long', screen.long]]) {
        const file = path.join(output, `${screen.id}${suffix}.html`);
        fs.writeFileSync(file, html(pair(screen, actual)));
        await page.goto(pathToFileURL(file).href);
        await page.locator('img').evaluateAll(images => Promise.all(images.map(image => image.decode())));
        await page.screenshot({ path: path.join(output, `${screen.id}${suffix}.png`) });
      }
    }
    const tiles = screens.map(screen => `<article class="tile"><a href="${screen.id}.png"><img src="${screen.id}.png" alt="${escape(screen.label)} 설계와 Editor 비교"></a><p>${escape(screen.label)} · 왼쪽 설계 / 오른쪽 Editor · <a href="${screen.id}-long.png">긴 화면 비율</a></p></article>`).join('');
    const overview = path.join(output, 'index.html');
    fs.writeFileSync(overview, html(`<h1>제공 설계 / 실제 Editor · 관문: 부분, 사용자 시각 승인 대기</h1><p>이미지 내용 수정 없음. 긴 화면은 비율을 유지하며 여백 표시. 실제 기기 검증 아님.</p><div class="grid">${tiles}</div>`));
    await page.setViewportSize({ width: 1194, height: 900 });
    await page.goto(pathToFileURL(overview).href);
    await page.locator('img').evaluateAll(images => Promise.all(images.map(image => image.decode())));
    await page.screenshot({ path: path.join(output, 'overview.png'), fullPage: true });
  } finally { await browser.close(); }
  const provenance = screens.map(({ reference, actual, long, ...screen }) => ({ ...screen, reference: { file: reference.file, sha256: reference.sha256 }, actual: { file: actual.file, sha256: actual.sha256 }, long: { file: long.file, sha256: long.sha256 } }));
  fs.writeFileSync(path.join(output, 'comparison.json'), JSON.stringify({ visualGate: 'partial-awaiting-user-approval', assets, screens: provenance }, null, 2) + '\n');
  console.log(`PORTRAIT_REFERENCE_RENDERED screens=${screens.length} byteIdenticalAssets=${assets.length}; visualGate=partial-awaiting-user-approval; output=${output}`);
}
