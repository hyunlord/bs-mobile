import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

// A test skipped by every content profile never runs anywhere. tools/unity-tests.sh fails on such tests unless
// tools/unity-skip-exceptions.txt names them with a reason, and fails on listed names that now run somewhere.
const decode = text => text.replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');

export function testResults(xml) {
  const results = new Map();
  for (const chunk of xml.split('<test-case').slice(1)) {
    const tag = chunk.slice(0, chunk.indexOf('>'));
    const name = tag.match(/\sfullname="([^"]*)"/)?.[1];
    const result = tag.match(/\sresult="([^"]*)"/)?.[1];
    if (!name || !result) continue;
    const reason = chunk.match(/<reason>\s*<message>\s*<!\[CDATA\[([\s\S]*?)\]\]>/)?.[1].trim() ?? '';
    results.set(decode(name), { result, reason });
  }
  return results;
}

export function parseExceptions(text) {
  const entries = new Map(), errors = [];
  for (const raw of text.split('\n')) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const bar = line.indexOf('|');
    const name = (bar < 0 ? line : line.slice(0, bar)).trim(), reason = bar < 0 ? '' : line.slice(bar + 1).trim();
    if (!reason) errors.push(`Exception ${name} has no reason; write "<full test name> | <reason>"`);
    entries.set(name, reason);
  }
  return { entries, errors };
}

export function checkSkippedEverywhere(xmlTexts, exceptionsText) {
  const seen = new Map();
  for (const xml of xmlTexts) for (const [name, outcome] of testResults(xml)) {
    if (!seen.has(name)) seen.set(name, []);
    seen.get(name).push(outcome);
  }
  const skipped = [...seen].filter(([, runs]) => runs.every(run => run.result === 'Skipped'))
    .map(([name, runs]) => ({ name, reasons: [...new Set(runs.map(run => run.reason))] })).sort((a, b) => a.name.localeCompare(b.name));
  const { entries, errors } = parseExceptions(exceptionsText);
  const unexplained = skipped.filter(test => !entries.has(test.name));
  const skippedNames = new Set(skipped.map(test => test.name));
  for (const name of entries.keys()) if (!skippedNames.has(name)) errors.push(`Exception ${name} is not skipped in every profile any more (or no longer exists); remove it from the list`);
  return { skipped, unexplained, errors };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [exceptionsPath, ...xmlPaths] = process.argv.slice(2);
  const { skipped, unexplained, errors } = checkSkippedEverywhere(xmlPaths.map(p => fs.readFileSync(p, 'utf8')), fs.readFileSync(exceptionsPath, 'utf8'));
  for (const test of skipped) console.error(`${unexplained.includes(test) ? 'UNEXPLAINED' : 'listed'} ${test.name} :: ${test.reasons.join(' / ')}`);
  for (const error of errors) console.error(error);
  console.log(`skipped in every profile: ${unexplained.length} unexplained, ${skipped.length - unexplained.length} listed`);
  process.exit(unexplained.length || errors.length ? 1 : 0);
}
