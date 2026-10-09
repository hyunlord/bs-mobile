import { readFile } from 'node:fs/promises';
import { systemDesignRuleReport } from './system-design-v1.mjs';

const path = process.argv[2] ?? new URL('../data/system-design-v1.json', import.meta.url);
const design = JSON.parse(await readFile(path, 'utf8'));
const report = systemDesignRuleReport(design);
console.log(JSON.stringify(report, null, 2));
if (report.rules.some(rule => !rule.pass)) process.exitCode = 1;
