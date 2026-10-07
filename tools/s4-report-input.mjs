import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { parseCsv } from './csv.mjs';

export const HEADERS = Object.fromEntries(Object.entries({
  runs: 'caseId,policy,peopleRule,seed,repeatCount,ticks,survived,endReason,deathCause,level,season,hash,randomDraws,weaponDamage,toolActivationDamage,toolGrowthDamage,allyDamage,killExperience,harvestExperience,taxExperience,foodFinal,estateTicks,spawnedEnemies,harvests,ruins,rebuilds,peasants,militia,vassals,equipmentIdsJson,charterIdsJson,charterRanksJson,itemStacksJson,evolutionIdsJson,evolutionActivationsJson,toolGrowthByTargetJson,sourceResultsPath,sourceResultsSha256,sourceMetricsPath,sourceMetricsSha256',
  timeline: 'caseId,tick,season,level,enemies,farms,buildings,people,food,lordHealth,weaponDamage,toolActivationDamage,toolGrowthDamage,allyDamage,killExperience,harvestExperience,taxExperience',
  cards: 'caseId,choiceIndex,tick,level,offeredIdsJson,chosenId,rarity,chosenKind,chosenGrowthTargetsJson,chosenTagsJson',
  loot: 'caseId,acquisitionIndex,tick,sourceKind,sourceId,sourceEntityId,itemId,quantity,stackAfter,foodPaid',
  effects: 'caseId,effectId,sourceId,sourceKind,trigger,operation,subject,amount,activationCount,appliedTotal,firstActivationTick,lastActivationTick',
  determinism: 'caseId,repeatIndex,hash,ticks,survived,endReason', metadata: 'key,value'
}).map(([key, value]) => [key, value.split(',')]));
export const CUMULATIVE = ['weaponDamage','toolActivationDamage','toolGrowthDamage','allyDamage','killExperience','harvestExperience','taxExperience'];
const TEXT = new Set('caseId,policy,peopleRule,endReason,deathCause,hash,sourceResultsPath,sourceResultsSha256,sourceMetricsPath,sourceMetricsSha256,chosenId,rarity,chosenKind,sourceKind,sourceId,itemId,effectId,trigger,operation,subject'.split(','));
export function requireValue(condition, message) { if (!condition) throw new Error(`S4 CSV: ${message}`); }
function integer(value, field, signed = false) {
  requireValue(typeof value === 'string' && (signed ? /^-?\d+$/ : /^\d+$/).test(value), `${field} must be an integer`);
  const result = Number(value); requireValue(Number.isSafeInteger(result), `${field} exceeds safe integer`); return result;
}
function record(value, label) { requireValue(value !== null && typeof value === 'object' && !Array.isArray(value), `${label} must be object`); return value; }
function list(value, label) { requireValue(Array.isArray(value), `${label} must be array`); return value; }
function strings(value, label) { list(value, label); requireValue(value.every(x => typeof x === 'string' && x.length > 0) && new Set(value).size === value.length, `${label} must contain unique nonempty strings`); return value; }
const equal = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const sameSet = (a, b) => equal([...a].sort(), [...b].sort());
function parseRows(name, rows) {
  return rows.map(row => Object.fromEntries(Object.entries(row).map(([field, value]) => {
    if (field.endsWith('Json')) { try { return [field, JSON.parse(value)]; } catch { throw new Error(`S4 CSV: invalid JSON ${name}.${field}`); } }
    if (field === 'survived') { requireValue(value === 'true' || value === 'false', 'survived must be boolean'); return [field, value === 'true']; }
    if (TEXT.has(field)) return [field, value];
    if (['firstActivationTick','lastActivationTick'].includes(field) && value === '') return [field, null];
    return [field, integer(value, `${name}.${field}`, ['amount','appliedTotal','lordHealth'].includes(field))];
  })));
}
function parseMetadata(rows) {
  const metadata = {};
  for (const { key, value } of rows) { requireValue(key && !Object.hasOwn(metadata, key), `duplicate metadata ${key}`); metadata[key] = key.endsWith('Json') ? JSON.parse(value) : value; }
  for (const field of ['schemaVersion','repeatCount','tickRate','requestedTicks','configuredDurationTicks','independentCaseCount','totalExecutions','workerLimit','requestedWarmupTicks','actualWarmupTicksMin','actualWarmupTicksMax','advertisementUses','charterSlots','timelineSampleIntervalTicks','elapsedWallMs']) metadata[field] = integer(metadata[field], `metadata.${field}`);
  for (const field of ['stage','mode','profileId','profileSha256','contentSha256','sourceCommit','sourceTreeSha256','sourceDirty','runtimeJson','policiesJson','peopleRulesJson','seedsJson','seasonDurationTicksJson','seasonNamesJson','scenario','shortenedSimulation','seasonsAccelerated','warmupExcluded','metaSnapshot','tuningStatus','acquisitionCatalogJson','itemStackLimit','selectedIdsJson','cardCatalogJson','effectCatalogJson','crossoverRule','rankFieldsJson','dominanceRule','foodMaterialScored','scope','invocationArgsJson','measurementStartedAt','measurementEndedAt']) requireValue(Object.hasOwn(metadata, field), `missing metadata ${field}`);
  strings(metadata.policiesJson, 'policies'); strings(metadata.peopleRulesJson, 'rules'); list(metadata.seedsJson, 'seeds');
  requireValue(sameSet(metadata.policiesJson, ['weapon','land','building','people','mixed','random']) && sameSet(metadata.peopleRulesJson, ['A','B','C']), 'six policies and A/B/C required');
  requireValue(metadata.seedsJson.every(Number.isSafeInteger) && new Set(metadata.seedsJson).size === metadata.seedsJson.length && metadata.seedsJson.length > 0, 'unique integer seeds required');
  const expectedSeeds = Array.from({ length: metadata.mode === 's4-stage' ? 32 : 3 }, (_, i) => i + 42);
  requireValue(equal(metadata.seedsJson, expectedSeeds), 'mode seed range mismatch');
  requireValue(metadata.schemaVersion === 1 && metadata.stage === 'S4' && ['s4-smoke','s4-stage'].includes(metadata.mode), 'unsupported stage/schema/mode');
  requireValue(metadata.repeatCount === 3 && metadata.tickRate > 0 && metadata.requestedTicks > 0 && metadata.timelineSampleIntervalTicks > 0, 'invalid repeat/rate/duration');
  requireValue(metadata.foodMaterialScored === 'false' && metadata.itemStackLimit === 'none' && metadata.advertisementUses === 0 && metadata.metaSnapshot === 'neutral-no-meta', 'neutral no-stockpile protocol required');
  requireValue(equal(metadata.rankFieldsJson, ['survived','ticks','level','totalCombatDamage']) && metadata.dominanceRule === 'policy-cofirst-in-every-peopleRule-seed-condition', 'ranking protocol mismatch');
  requireValue(/^[a-f0-9]{40}$/i.test(metadata.sourceCommit), 'invalid sourceCommit');
  list(metadata.seasonDurationTicksJson, 'season durations'); strings(metadata.seasonNamesJson, 'season names');
  requireValue(metadata.seasonDurationTicksJson.length === 4 && metadata.seasonNamesJson.length === 4 && metadata.seasonDurationTicksJson.every(x => Number.isSafeInteger(x) && x > 0) && metadata.seasonDurationTicksJson.reduce((a,b)=>a+b,0) === metadata.configuredDurationTicks, 'invalid season configuration');
  for (const field of ['profileSha256','contentSha256','sourceTreeSha256']) requireValue(/^[a-f0-9]{64}$/i.test(metadata[field]), `invalid ${field}`);
  for (const kind of ['weapons','tools','charters','items','evolutions','enemies','heroes','estates']) strings(record(metadata.selectedIdsJson, 'selectedIds')[kind], kind);
  for (const [kind, count] of Object.entries({weapons:4,tools:4,charters:4,items:12,evolutions:2,enemies:6,heroes:1,estates:1})) requireValue(metadata.selectedIdsJson[kind].length === count, `selected ${kind} count mismatch`);
  requireValue(metadata.requestedTicks === (metadata.mode === 's4-stage' ? metadata.configuredDurationTicks : 900), 'mode duration mismatch');
  for (const key of ['sourceDirty','shortenedSimulation','seasonsAccelerated','warmupExcluded']) requireValue(['true','false'].includes(metadata[key]), `metadata ${key} boolean required`);
  requireValue(metadata.scenario === 'normal', 'normal scenario required');
  requireValue(metadata.shortenedSimulation === String(metadata.mode === 's4-smoke') && (metadata.requestedTicks < metadata.configuredDurationTicks) === (metadata.mode === 's4-smoke'), 'shortenedSimulation mode mismatch');
  requireValue(metadata.warmupExcluded === 'true' && metadata.seasonsAccelerated === 'false', 'warmup/season protocol mismatch');
  list(metadata.cardCatalogJson, 'cardCatalog'); list(metadata.effectCatalogJson, 'effectCatalog');
  list(record(metadata.acquisitionCatalogJson, 'acquisitionCatalog').lootSources, 'loot sources');
  return metadata;
}
function indexRows(rows, key, label) {
  const map = new Map(); for (const row of rows) { const id = key(row); requireValue(!map.has(id), `duplicate ${label}: ${id}`); map.set(id, row); } return map;
}
export function validateTables(tables) {
  const metadata = parseMetadata(tables.metadata);
  const data = Object.fromEntries(Object.entries(tables).filter(([name]) => name !== 'metadata').map(([name, rows]) => [name, parseRows(name, rows)]));
  const runs = indexRows(data.runs, row => row.caseId, 'case'); const selected = metadata.selectedIdsJson;
  const expected = metadata.policiesJson.flatMap(policy => metadata.peopleRulesJson.flatMap(rule => metadata.seedsJson.map(seed => `${policy}__${rule}__${seed}`)));
  requireValue(sameSet([...runs.keys()], expected), 'missing or extra matrix case');
  requireValue(metadata.independentCaseCount === runs.size && metadata.totalExecutions === runs.size * metadata.repeatCount, 'metadata matrix count mismatch');
  const cardCatalog = indexRows(metadata.cardCatalogJson, row => row.id, 'card catalog'); const effectCatalog = indexRows(metadata.effectCatalogJson, row => row.effectId, 'effect catalog');
  for (const definition of effectCatalog.values()) { requireValue(typeof definition.unit === 'string' && definition.unit.length > 0 && typeof definition.implementationNote === 'string', 'effect unit/note required'); requireValue(selected[`${definition.sourceKind}s`]?.includes(definition.sourceId), 'effect source not selected'); }
  for (const definition of cardCatalog.values()) { requireValue(['weapon','tool','charter'].includes(definition.kind), 'invalid card kind'); requireValue(selected[`${definition.kind}s`].includes(definition.id), 'card catalog outside selected ids'); strings(definition.growthTargets, 'catalog targets'); strings(definition.tags, 'catalog tags'); }
  for (const [name, rows] of Object.entries(data)) if (name !== 'runs') for (const row of rows) requireValue(runs.has(row.caseId), `orphan ${name} case`);
  const grouped = Object.fromEntries(Object.entries(data).filter(([name]) => name !== 'runs').map(([name, rows]) => { const map = new Map(expected.map(id => [id, []])); for (const row of rows) map.get(row.caseId).push(row); return [name, map]; }));
  for (const run of data.runs) {
    requireValue(run.caseId === `${run.policy}__${run.peopleRule}__${run.seed}` && run.repeatCount === metadata.repeatCount, 'case identity/repeat mismatch');
    requireValue(run.ticks <= metadata.requestedTicks && run.estateTicks <= run.ticks, 'run duration/residence out of bounds');
    for (const field of ['hash','sourceResultsSha256','sourceMetricsSha256']) requireValue(/^[a-f0-9]{64}$/i.test(run[field]), `invalid run ${field}`);
    for (const [field, allowed] of [['equipmentIdsJson',[...selected.weapons,...selected.tools]],['charterIdsJson',selected.charters],['evolutionIdsJson',selected.evolutions]]) { strings(run[field], field); requireValue(run[field].every(id => allowed.includes(id)), `unknown ${field}`); }
    for (const [field, allowed] of [['itemStacksJson',selected.items],['charterRanksJson',selected.charters]]) for (const [id, count] of Object.entries(record(run[field], field))) requireValue(allowed.includes(id) && Number.isSafeInteger(count) && count > 0, `invalid ${field}`);
    requireValue(sameSet(Object.keys(run.charterRanksJson), run.charterIdsJson) && run.charterIdsJson.length <= metadata.charterSlots, 'charter rank/slot mismatch');
    const evoEvents = list(run.evolutionActivationsJson, 'evolutionActivations');
    requireValue(sameSet(evoEvents.map(x => x.evolutionId), run.evolutionIdsJson) && new Set(evoEvents.map(x => x.evolutionId)).size === evoEvents.length, 'evolution activation mismatch');
    for (const event of evoEvents) requireValue(Number.isSafeInteger(event.tick) && event.tick >= 0 && event.tick <= run.ticks && run.equipmentIdsJson.includes(event.baseId), 'invalid evolution event');
    for (const [tool, targets] of Object.entries(record(run.toolGrowthByTargetJson, 'toolGrowthByTarget'))) { requireValue(selected.tools.includes(tool), 'unknown growth tool'); for (const [target, count] of Object.entries(record(targets, 'growth targets'))) requireValue(['land','building','people'].includes(target) && Number.isSafeInteger(count) && count >= 0, 'invalid growth target count'); }
    const samples = grouped.timeline.get(run.caseId); requireValue(samples.length > 0 && samples[0].tick === 0 && samples.at(-1).tick === run.ticks, 'actual tick0/terminal required');
    const expectedTicks = Array.from({length:Math.floor(run.ticks/metadata.timelineSampleIntervalTicks)+1},(_,i)=>i*metadata.timelineSampleIntervalTicks); if (expectedTicks.at(-1)!==run.ticks) expectedTicks.push(run.ticks);
    requireValue(equal(samples.map(row=>row.tick),expectedTicks), 'missing/unexpected periodic sample');
    const terminal = samples.at(-1);
    requireValue(run.survived === (terminal.lordHealth > 0), 'survival and terminal health mismatch');
    requireValue(!run.survived || run.ticks === metadata.requestedTicks, 'premature alive duration');
    requireValue(run.endReason === (run.survived ? metadata.mode === 's4-smoke' ? 'fixture-duration' : 'duration' : 'death'), 'endReason contradicts survival/mode');
    requireValue(terminal.food === run.foodFinal, 'terminal food mismatch');
    for (let i = 0; i < samples.length; i++) {
      const sample = samples[i]; requireValue(sample.tick <= run.ticks && (i === 0 || sample.tick > samples[i-1].tick), 'duplicate/unordered timeline tick');
      requireValue(sample.level > 0 && sample.season < metadata.seasonNamesJson.length, 'invalid level/season');
      requireValue(i === 0 || sample.level >= samples[i-1].level, 'regressing level');
      requireValue(i === 0 || sample.season >= samples[i-1].season, 'regressing season');
      for (const key of CUMULATIVE) requireValue(i === 0 ? sample[key] === 0 : sample[key] >= samples[i-1][key], 'nonmonotonic cumulative telemetry');
    }
    for (const key of [...CUMULATIVE,'level','season']) requireValue(samples.at(-1)[key] === run[key], `terminal ${key} mismatch`);
    const repeats = grouped.determinism.get(run.caseId); requireValue(repeats.length === metadata.repeatCount, 'missing repeat');
    requireValue(sameSet(repeats.map(x => x.repeatIndex), Array.from({ length: metadata.repeatCount }, (_, i) => i)), 'duplicate repeat index');
    for (const repeat of repeats) for (const key of ['hash','ticks','survived','endReason']) requireValue(repeat[key] === run[key], `repeat ${key} mismatch`);
    const cards = grouped.cards.get(run.caseId);
    for (const [i, card] of cards.entries()) {
      const def = cardCatalog.get(card.chosenId); requireValue(card.choiceIndex === i && card.tick <= run.ticks && (i === 0 || card.tick >= cards[i-1].tick) && def, 'invalid card index/tick/id');
      strings(card.offeredIdsJson, 'offers'); requireValue(card.offeredIdsJson.includes(card.chosenId) && card.offeredIdsJson.every(id => cardCatalog.has(id)), 'chosen card not offered/unknown offer');
      requireValue(card.chosenKind === def.kind && sameSet(strings(card.chosenGrowthTargetsJson, 'chosen targets'), def.growthTargets) && sameSet(strings(card.chosenTagsJson, 'chosen tags'), def.tags), 'card catalog mismatch');
    }
    const inventory = {};
    for (const [i, loot] of grouped.loot.get(run.caseId).entries()) {
      requireValue(loot.acquisitionIndex === i && loot.tick <= run.ticks && ['chest','cart','market'].includes(loot.sourceKind) && selected.items.includes(loot.itemId) && loot.quantity > 0, 'invalid loot event');
      requireValue(metadata.acquisitionCatalogJson.lootSources.some(source => source.id === loot.sourceId && source.kind === loot.sourceKind), 'unknown loot source');
      inventory[loot.itemId] = (inventory[loot.itemId] ?? 0) + loot.quantity; requireValue(inventory[loot.itemId] === loot.stackAfter, 'loot stackAfter mismatch');
    }
    requireValue(sameSet(Object.keys(inventory), Object.keys(run.itemStacksJson)) && Object.entries(inventory).every(([id, count]) => run.itemStacksJson[id] === count), 'final loot stacks mismatch');
    const counters = indexRows(grouped.effects.get(run.caseId), row => row.effectId, 'effect counter'); requireValue(sameSet([...counters.keys()], [...effectCatalog.keys()]), 'missing/extra effect counters');
    for (const counter of counters.values()) {
      const def = effectCatalog.get(counter.effectId);
      for (const key of ['sourceId','sourceKind','trigger','operation','subject','amount']) requireValue(counter[key] === def[key], `effect ${key} mismatch`);
      requireValue(counter.activationCount === 0 ? counter.firstActivationTick === null && counter.lastActivationTick === null && counter.appliedTotal === 0 : counter.firstActivationTick !== null && counter.lastActivationTick !== null && counter.firstActivationTick <= counter.lastActivationTick && counter.lastActivationTick <= run.ticks, 'effect counter/tick mismatch');
    }
  }
  return { ...data, metadata, grouped };
}
export async function loadLeague(directory) {
  const tables = {}; const manifest = [];
  for (const [name, headers] of Object.entries(HEADERS)) {
    const bytes = await readFile(join(directory, `${name}.csv`)); const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    const rows = parseCsv(text, headers); tables[name] = rows;
    manifest.push({ file: `${name}.csv`, sha256: createHash('sha256').update(bytes).digest('hex'), rows: rows.length });
  }
  return { ...validateTables(tables), manifest };
}
