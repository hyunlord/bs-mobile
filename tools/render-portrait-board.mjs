// Reproduce comparison sheets from actual captures and the approved written fallback.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import os from 'node:os';
import { setTimeout as delay } from 'node:timers/promises';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2).filter(arg => arg !== '--png');
const root = path.resolve(args[0] ?? path.join(repo, 'artifacts'));
const output = path.resolve(args[1] ?? path.join(root, 'portrait-screen-board'));
const rasterize = process.argv.includes('--png');
const screens = [
  ['title', '타이틀', 'portrait-flow-225', ['아무 곳이나 눌러 진행', '첫 실행 → 첫 판', '그 뒤 → 영지']],
  ['run', '판 HUD', 'portrait-flow-225', ['전장 90% 이상', '상단 한 줄 · 장비 아이콘', '하단 막대 없음']],
  ['cards', '레벨업', 'portrait-flow-225', ['세로 카드 3장 · 진화 띠', '다시 뽑기 · 금지 · 고정', '선택 전 0.3초 입력 보호']],
  ['pause', '일시정지', 'portrait-flow-225', ['무기 5 · 도구 4 · 특허장 4', '진화 진행 · 설정', '계속 · 포기 확인']],
  ['estate', '영지', 'portrait-meta-226', ['자원 · 다음 챕터 · 설정', '장원 4채 · 생산 상자', '하단 출정 · 탭 4개']],
  ['departure', '출정 준비', 'portrait-meta-226', ['챕터 1~7 · 적 · 보스 · 상한', '가신 3칸 · 기본 장비', '이번 판 카드 · 출정 시작']],
  ['result', '결산', 'portrait-meta-226', ['시간 · 레벨 · 수확', '실제 자재 · 상한 · 조각', '다시 출정 · 영지로']],
  ['vassals', '가신', 'portrait-meta-226', ['초상 카드 6장 · 상세', '자재 레벨업 · 조각 승급', '잠금과 비용은 실제 데이터']],
  ['challenges', '도전', 'portrait-meta-226', ['실제 도전 · 진행도', '달성한 보상 받기', '지급 기록 중복 방지']],
  ['shop', '상점', 'portrait-bm-227', ['보기 전용 · 외형 · 테마', '패스 · 광고 제거 · 보석', '힘 판매 없음 · 미정 [n]']],
  ['idle', '방치 생산', 'portrait-bm-227', ['모인 시간 / 최대 시간', '받기가 주 버튼', '선택 광고 2배 · 하루 3회 가설']],
  ['revive', '부활', 'portrait-bm-227', ['결산 / 광고로 일어나기', '같은 크기 · 시간 압박 없음', '미정 규칙은 시험 설정으로 표시']],
  ['pass', '시즌 패스', 'portrait-bm-227', ['무료 / 유료 · 고정 보상', '판 안 힘은 없음', '보기 전용 · 미정 [n]']],
];
const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;');
const image = (file, x, y, width, height) => `<image x="${x}" y="${y}" width="${width}" height="${height}" preserveAspectRatio="xMidYMin meet" href="data:image/png;base64,${fs.readFileSync(file).toString('base64')}"/>`;
fs.mkdirSync(output, { recursive: true });
for (const [id, title, group, lines] of screens) {
  const base = path.join(root, group, 'editor');
  const captureId = id === 'revive' ? 'revive-rules-fixture' : id;
  const reference = path.join(base, `${captureId}-390x844.png`);
  const ratio = path.join(base, `${captureId}-1080x2520.png`);
  for (const file of [reference, ratio]) if (!fs.existsSync(file)) throw new Error(`Missing actual capture: ${file}`);
  const provenance = [reference, ratio].map(file => fs.existsSync(`${file}.txt`) ? fs.readFileSync(`${file}.txt`, 'utf8').trim() : 'Editor PlayMode capture; see test-run XML.');
  const labels = lines.map((line, index) => `<text x="40" y="${185 + index * 64}" font-size="21">${escape(line)}</text>`).join('');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="1020" viewBox="0 0 1280 1020"><rect width="1280" height="1020" fill="#f4f0e8"/><style>text{font-family:Apple SD Gothic Neo,Arial,sans-serif;fill:#26313a}</style><text x="32" y="42" font-size="29">${escape(title)} · 서면 규격과 실제 화면</text><text x="32" y="74" font-size="16">캔버스 원본 접근 불가: 승인된 서면 대체 규격. 두 화면은 Editor 실행 캡처이며 실기 증거가 아닙니다.</text><text x="40" y="116" font-size="19">서면 설계 보드</text><text x="455" y="116" font-size="19">기준 390 × 844</text><text x="878" y="116" font-size="19">실기 비율 1080 × 2520 시뮬레이션</text><rect x="24" y="130" width="390" height="844" rx="12" fill="#e8dfca"/>${labels}<text x="40" y="500" font-size="17">공통: 44pt · 안전 영역 · 세로</text><text x="40" y="534" font-size="17">스크롤 화면은 하단 캡처도 함께 확인</text><text x="40" y="568" font-size="17">BM 시험 설정은 캡처 기록에 구별</text>${image(reference, 436, 130, 390, 844)}${image(ratio, 854, 130, 390, 844)}<text x="32" y="1003" font-size="13">${escape(group)}/editor/${escape(id)} · 원본을 늘이거나 내용 수정하지 않음</text></svg>`;
  const labeledSvg = id === 'revive' ? svg.replace('부활 · 서면', '부활 (규칙·계정 시험 설정) · 서면').replace('/editor/revive ·', '/editor/revive-rules-fixture ·') : svg;
  fs.writeFileSync(path.join(output, `${id}.svg`), labeledSvg);
  fs.writeFileSync(path.join(output, `${id}.provenance.txt`), `captureFiles=${reference},${ratio}\n` + provenance.join('\n\n') + '\n');
}
if (rasterize) {
  const chrome = process.env.PORTRAIT_BOARD_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
  for (const [id] of screens) {
    const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'portrait-board-chrome-'));
    const target = path.join(output, `${id}.png`);
    fs.rmSync(target, { force: true });
    const child = spawn(chrome, ['--headless', '--disable-gpu', '--disable-background-networking', '--disable-component-update', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check', '--timeout=5000', `--user-data-dir=${profile}`, '--force-device-scale-factor=1', '--window-size=1280,1020', `--screenshot=${target}`, pathToFileURL(path.join(output, `${id}.svg`)).href], { stdio: 'ignore' });
    let launchError;
    child.on('error', error => { launchError = error; });
    try {
      let png;
      for (let attempt = 0; attempt < 150; attempt++) {
        if (launchError) throw launchError;
        if (fs.existsSync(target)) {
          const candidate = fs.readFileSync(target);
          if (candidate.length > 24 && candidate.subarray(-8, -4).toString() === 'IEND') { png = candidate; break; }
        }
        await delay(200);
      }
      if (!png) throw new Error(`Chrome did not finish the PNG: ${id}`);
      if (png.readUInt32BE(16) !== 1280 || png.readUInt32BE(20) !== 1020) throw new Error(`Unexpected board dimensions: ${id}`);
    } finally {
      child.kill('SIGTERM');
      await delay(200);
      if (child.exitCode === null) child.kill('SIGKILL');
      fs.rmSync(profile, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
    }
  }
}
console.log(`PORTRAIT_BOARD_PASS screens=${screens.length}; output=${output}`);
