import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { formatCsv } from './csv.mjs';

export const S4_HEADERS = Object.freeze({
  runs: 'caseId,policy,peopleRule,seed,repeatCount,ticks,survived,endReason,deathCause,level,season,hash,randomDraws,weaponDamage,toolActivationDamage,toolGrowthDamage,allyDamage,killExperience,harvestExperience,taxExperience,foodFinal,estateTicks,spawnedEnemies,harvests,ruins,rebuilds,peasants,militia,vassals,equipmentIdsJson,charterIdsJson,charterRanksJson,itemStacksJson,evolutionIdsJson,evolutionActivationsJson,toolGrowthByTargetJson,sourceResultsPath,sourceResultsSha256,sourceMetricsPath,sourceMetricsSha256'.split(','),
  timeline: 'caseId,tick,season,level,enemies,farms,buildings,people,food,lordHealth,weaponDamage,toolActivationDamage,toolGrowthDamage,allyDamage,killExperience,harvestExperience,taxExperience'.split(','),
  cards: 'caseId,choiceIndex,tick,level,offeredIdsJson,chosenId,rarity,chosenKind,chosenGrowthTargetsJson,chosenTagsJson'.split(','),
  loot: 'caseId,acquisitionIndex,tick,sourceKind,sourceId,sourceEntityId,itemId,quantity,stackAfter,foodPaid'.split(','),
  effects: 'caseId,effectId,sourceId,sourceKind,trigger,operation,subject,amount,activationCount,appliedTotal,firstActivationTick,lastActivationTick'.split(','),
  determinism: 'caseId,repeatIndex,hash,ticks,survived,endReason'.split(','),
  metadata: ['key', 'value'],
});
const POLICIES = ['weapon', 'land', 'building', 'people', 'mixed', 'random'];
const PEOPLE_RULES = ['A', 'B', 'C'];
const requireValue = (condition, message) => { if (!condition) throw new Error(message); };
const digest = (bytes) => createHash('sha256').update(bytes).digest('hex');
const json = (value) => JSON.stringify(value);
const canonical = (value) => Array.isArray(value) ? `[${value.map(canonical).join(',')}]` : value && typeof value === 'object' ? `{${Object.keys(value).sort().map((key) => `${json(key)}:${canonical(value[key])}`).join(',')}}` : json(value);
const integer = (value, label, min = 0) => { requireValue(Number.isSafeInteger(value) && value >= min, `${label} must be a safe integer >= ${min}`); return value; };
const text = (value, label) => { requireValue(typeof value === 'string' && value.length > 0, `${label} must be nonempty text`); return value; };
const hash = (value, label, length = 64) => { requireValue(typeof value === 'string' && new RegExp(`^[a-f0-9]{${length}}$`, 'i').test(value), `Invalid ${label}`); return value; };
const list = (value, label) => { requireValue(Array.isArray(value), `${label} must be an array`); return value; };
const object = (value, label) => { requireValue(value && typeof value === 'object' && !Array.isArray(value), `${label} must be an object`); return value; };
const ids = (value, label) => { list(value, label).forEach((id) => text(id, label)); requireValue(new Set(value).size === value.length, `${label} contains duplicate IDs`); return value; };
const positiveMap = (value, label) => { for (const [id, count] of Object.entries(object(value, label))) { text(id, label); integer(count, `${label}.${id}`, 1); } return value; };

export function parseS4Options(args) {
  const [mode, ...rest] = args;
  requireValue(['s4-smoke', 's4-stage'].includes(mode), 'S4 mode must be s4-smoke or s4-stage');
  const options = { mode, profile: 's4-stage-one', workers: 2 };
  const seen = new Set();
  for (let i = 0; i < rest.length; i += 2) {
    const name = rest[i];
    requireValue(['--profile', '--workers'].includes(name) && !seen.has(name) && rest[i + 1] !== undefined, `Unknown, duplicate or incomplete option ${name}`);
    seen.add(name);
    if (name === '--profile') options.profile = rest[i + 1];
    else { requireValue(/^[1-4]$/.test(rest[i + 1]), 'workers must be an integer 1..4'); options.workers = Number(rest[i + 1]); }
  }
  requireValue(/^[a-z0-9][a-z0-9_-]*$/.test(options.profile), 'Unsafe profile name');
  return options;
}
export function makeS4Cases(mode) {
  requireValue(['s4-smoke', 's4-stage'].includes(mode), 'Unknown matrix mode');
  const seeds = Array.from({ length: mode === 's4-smoke' ? 3 : 32 }, (_, index) => 42 + index);
  return POLICIES.flatMap((policy) => PEOPLE_RULES.flatMap((peopleRule) => seeds.map((seed) => ({ caseId: `${policy}__${peopleRule}__${seed}`, policy, peopleRule, seed }))));
}
export async function boundedMap(items, limit, work) {
  integer(limit, 'worker limit', 1);
  requireValue(limit <= 4, 'worker limit exceeds four');
  const output = new Array(items.length);
  let next = 0;
  let failure;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (!failure && next < items.length) {
      const index = next++;
      try { output[index] = await work(items[index], index); }
      catch (error) { failure ??= error; }
    }
  }));
  if (failure) throw failure;
  return output;
}
function runtimeIdentity(metrics, expectedStage = 'S4') {
  const source = object(metrics.sourceMetadata, 'sourceMetadata');
  requireValue(metrics.stage === expectedStage, 'Unexpected telemetry stage');
  const identity = {
    profileId: text(source.profileId, 'profile ID'), profileSha256: hash(source.profileSha256, 'profile hash'), contentSha256: hash(source.contentSha256, 'content hash'),
    sourceCommit: hash(source.commit, 'source commit', 40), sourceTreeSha256: hash(source.sourceTreeSha256, 'source tree hash'), sourceDirty: source.gitDirty,
    simulationAssemblySha256: hash(source.simulationAssemblySha256, 'simulation assembly hash'), coreAssemblySha256: hash(source.coreAssemblySha256, 'core assembly hash'),
    runtime: { framework: text(metrics.runtime, 'runtime'), os: text(metrics.os, 'OS'), architecture: text(metrics.architecture, 'architecture'), stopwatchFrequency: integer(metrics.stopwatchFrequency, 'clock frequency', 1) },
    selectedIds: object(source.selectedIds, 'selected IDs'), cardCatalog: list(source.cardCatalog, 'card catalog'), effectCatalog: list(source.effectCatalog, 'effect catalog'), acquisitionCatalog: object(source.acquisitionCatalog, 'acquisition catalog'),
    timelineSampleIntervalTicks: integer(source.timelineSampleIntervalTicks, 'timeline interval', 1), charterSlots: integer(source.charterSlots, 'charter slots', 1),
    tickRate: integer(source.tickRate, 'tick rate', 1), configuredDurationTicks: integer(source.configuredDurationTicks, 'full duration', 1), requestedTicks: integer(source.requestedDurationTicks, 'requested duration', 1),
    seasonDurationTicks: list(source.seasonDurationTicks, 'season durations'), seasonNames: list(source.seasonNames, 'season names'),
    metaSnapshot: text(source.metaSnapshot, 'meta snapshot'), advertisementUses: integer(source.advertisementUses, 'advertisement uses'), tuningStatus: text(source.tuningStatus, 'tuning status'),
    requestedWarmupTicks: integer(metrics.requestedWarmupTicks, 'requested warmup'), warmupExcluded: metrics.warmupExcluded,
  };
  requireValue(typeof identity.sourceDirty === 'boolean' && identity.warmupExcluded === true, 'Explicit source dirty and excluded warmup required');
  requireValue(source.scenario === 'normal' && source.includeTest === false, 'League requires normal canonical profile');
  requireValue(identity.tickRate === 30 && identity.configuredDurationTicks === 21600 && identity.advertisementUses === 0 && identity.metaSnapshot === 'neutral-no-meta', 'Unexpected S4 game contract');
  const counts = { weapons: 4, tools: 4, charters: 4, items: 12, evolutions: 2, enemies: 6, heroes: 1, estates: 1 };
  for (const [kind, count] of Object.entries(counts)) requireValue(ids(identity.selectedIds[kind], kind).length === count, `Unexpected selected ${kind} count`);
  requireValue(identity.seasonDurationTicks.length === 4 && identity.seasonNames.length === 4 && identity.seasonDurationTicks.every(value => Number.isSafeInteger(value) && value > 0) && identity.seasonDurationTicks.reduce((sum, value) => sum + value, 0) === identity.configuredDurationTicks, 'Invalid season configuration');
  const selectedCards = [...identity.selectedIds.weapons, ...identity.selectedIds.tools, ...identity.selectedIds.charters];
  requireValue(identity.cardCatalog.length === selectedCards.length && identity.cardCatalog.every(entry => ['weapon', 'tool', 'charter'].includes(entry.kind) && identity.selectedIds[entry.kind + 's'].includes(entry.id)), 'Card catalog outside selected kind/IDs');
  requireValue(new Set(identity.cardCatalog.map((entry) => entry.id)).size === identity.cardCatalog.length, 'Duplicate card catalog ID');
  requireValue(new Set(identity.effectCatalog.map((entry) => entry.effectId)).size === identity.effectCatalog.length, 'Duplicate effect catalog ID');
  return identity;
}

export function extractS4Case(packet, expected) {
  const { caseInfo, metrics } = packet;
  const results = list(packet.results, 'results');
  requireValue(results.length === expected.repeatCount, `Repeat count mismatch ${caseInfo.caseId}`);
  const first = results[0];
  const identity = runtimeIdentity(metrics, expected.stage ?? 'S4');
  requireValue(identity.requestedTicks === expected.requestedTicks, 'Case tick cap mismatch');
  requireValue(first.policy === caseInfo.policy && first.peopleRule === caseInfo.peopleRule && first.seed === caseInfo.seed && first.scenario === 'normal', 'Case identity mismatch');
  requireValue(metrics.policy === first.policy && metrics.seed === first.seed && metrics.sourceMetadata.peopleRule === first.peopleRule, 'Metrics case identity mismatch');
  requireValue(integer(first.ticks, 'terminal tick', 1) <= expected.requestedTicks && metrics.ticks === first.ticks && metrics.hash === first.hash, 'Terminal result/metrics mismatch');
  requireValue(metrics.iterations === expected.repeatCount, 'Metrics repeat count mismatch');
  const determinism = results.map((result, repeatIndex) => {
    hash(result.hash, 'result hash');
    requireValue(result.hash === first.hash && result.ticks === first.ticks && result.survived === first.survived && result.endReason === first.endReason, `Determinism failure ${caseInfo.caseId}`);
    requireValue(result.policy === first.policy && result.peopleRule === first.peopleRule && result.seed === first.seed, 'Repeated case identity mismatch');
    return { caseId: caseInfo.caseId, repeatIndex, hash: result.hash, ticks: result.ticks, survived: result.survived, endReason: result.endReason };
  });
  requireValue(typeof first.survived === 'boolean', 'Survival must be explicit');
  const runtime = object(first.runtime, 'runtime result');
  const build = object(runtime.build, 'runtime build');
  const equipmentIds = [...ids(build.weapons, 'owned weapons'), ...ids(build.tools, 'owned tools')];
  const charterRanks = positiveMap(build.charters, 'charter ranks');
  const itemStacks = positiveMap(build.itemStacks, 'item stacks');
  const evolutionIds = ids(build.evolutions, 'active evolutions');
  for (const [values, kind] of [[build.weapons, 'weapons'], [build.tools, 'tools'], [Object.keys(charterRanks), 'charters'], [Object.keys(itemStacks), 'items'], [evolutionIds, 'evolutions']]) {
    requireValue(values.every((id) => identity.selectedIds[kind].includes(id)), `Owned ${kind} outside profile`);
  }
  const toolGrowth = object(runtime.toolGrowthByTarget, 'tool growth by target');
  for (const [id, targets] of Object.entries(toolGrowth)) {
    requireValue(identity.selectedIds.tools.includes(id), 'Unknown production tool');
    for (const [target, count] of Object.entries(object(targets, 'tool target production'))) { requireValue(['land', 'building', 'people'].includes(target), 'Unknown growth target'); integer(count, 'actual production'); }
  }
  const evolutionActivations = list(runtime.evolutionActivations, 'evolution activations');
  for (const event of evolutionActivations) { requireValue(evolutionIds.includes(event.evolutionId) && equipmentIds.includes(event.baseId), 'Evolution activation mismatch'); requireValue(integer(event.tick, 'evolution tick') <= first.ticks, 'Late evolution'); }
  const perTool = object(first.perTool, 'tool attribution');
  const toolActivationDamage = Object.values(perTool).reduce((sum, tool) => sum + integer(tool.activationDamage, 'tool activation damage'), 0);
  const toolGrowthDamage = Object.values(perTool).reduce((sum, tool) => sum + integer(tool.growthDamage, 'tool growth damage'), 0);
  const run = { ...caseInfo, repeatCount: expected.repeatCount, ticks: first.ticks, survived: first.survived, endReason: text(first.endReason, 'end reason'), deathCause: typeof first.deathCause === 'string' ? first.deathCause : '',
    level: integer(first.level, 'level'), season: integer(first.season, 'season'), hash: first.hash, randomDraws: integer(first.randomDraws, 'RNG draws'), weaponDamage: integer(first.weaponDamage, 'weapon damage'), toolActivationDamage, toolGrowthDamage, allyDamage: integer(first.allyDamage, 'ally damage'),
    killExperience: integer(first.killExperience, 'kill XP'), harvestExperience: integer(first.harvestExperience, 'harvest XP'), taxExperience: integer(first.taxExperience, 'tax XP'), foodFinal: integer(first.food, 'food'), estateTicks: integer(first.estateTicks, 'estate ticks'), spawnedEnemies: integer(first.spawnedEnemies, 'spawned enemies'), harvests: integer(first.harvests, 'harvests'), ruins: integer(first.ruins, 'ruins'), rebuilds: integer(first.rebuilds, 'rebuilds'), peasants: integer(first.peasants, 'peasants'), militia: integer(first.militia, 'militia'), vassals: integer(first.vassals, 'vassals'),
    equipmentIdsJson: json(equipmentIds), charterIdsJson: json(Object.keys(charterRanks)), charterRanksJson: json(charterRanks), itemStacksJson: json(itemStacks), evolutionIdsJson: json(evolutionIds), evolutionActivationsJson: json(evolutionActivations), toolGrowthByTargetJson: json(toolGrowth),
    sourceResultsPath: text(packet.resultsPath, 'result path'), sourceResultsSha256: hash(packet.resultsSha256, 'result file hash'), sourceMetricsPath: text(packet.metricsPath, 'metrics path'), sourceMetricsSha256: hash(packet.metricsSha256, 'metrics file hash'),
  };
  requireValue(run.estateTicks <= run.ticks, 'Estate residence exceeds duration');
  requireValue(first.damage === run.weaponDamage + toolActivationDamage + toolGrowthDamage + run.allyDamage, 'Total damage attribution mismatch');
  const cumulative = ['weaponDamage', 'toolActivationDamage', 'toolGrowthDamage', 'allyDamage', 'killExperience', 'harvestExperience', 'taxExperience'];
  const timeline = list(first.timeline, 'timeline').map((sample) => {
    const row = { caseId: caseInfo.caseId, tick: sample.tick, season: sample.season, level: sample.level, enemies: sample.enemies, farms: sample.farms, buildings: sample.buildings, people: sample.people, food: sample.food, lordHealth: sample.lordHealth, weaponDamage: sample.weaponDamage, toolActivationDamage: sample.toolDamage, toolGrowthDamage: sample.growthDamage, allyDamage: sample.allyDamage, killExperience: sample.killExperience, harvestExperience: sample.harvestExperience, taxExperience: sample.taxExperience };
    for (const [key, value] of Object.entries(row)) if (key !== 'caseId') integer(value, `timeline ${key}`);
    return row;
  });
  requireValue(timeline.length >= 2 && timeline[0].tick === 0 && timeline.at(-1).tick === first.ticks, 'Actual tick0 and terminal samples required');
  requireValue(cumulative.every((field) => timeline[0][field] === 0 && timeline.at(-1)[field] === run[field]), 'Timeline boundary totals mismatch');
  for (let i = 1; i < timeline.length; i++) requireValue(timeline[i].tick > timeline[i - 1].tick && cumulative.every((field) => timeline[i][field] >= timeline[i - 1][field]), 'Timeline order or cumulative regression');
  requireValue(timeline.at(-1).level === run.level && timeline.at(-1).season === run.season && timeline.at(-1).food === run.foodFinal, 'Terminal state mismatch');
  const expectedSampleTicks = Array.from({ length: Math.floor(first.ticks / identity.timelineSampleIntervalTicks) + 1 }, (_, index) => index * identity.timelineSampleIntervalTicks);
  if (expectedSampleTicks.at(-1) !== first.ticks) expectedSampleTicks.push(first.ticks);
  requireValue(canonical(timeline.map(sample => sample.tick)) === canonical(expectedSampleTicks), 'Missing or off-grid timeline sample');
  requireValue(timeline.every((sample, index) => sample.level >= 1 && sample.season < identity.seasonNames.length && (index === 0 || (sample.level >= timeline[index - 1].level && sample.season >= timeline[index - 1].season))), 'Invalid or regressing level/season');
  const terminalHealth = timeline.at(-1).lordHealth;
  if (first.survived) requireValue(terminalHealth > 0 && first.ticks === expected.requestedTicks && first.endReason === (expected.requestedTicks < identity.configuredDurationTicks ? 'fixture-duration' : 'duration'), 'Alive lifecycle mismatch');
  else requireValue(terminalHealth === 0 && first.endReason === 'death', 'Death lifecycle mismatch');
  requireValue(timeline.slice(0, -1).every(sample => sample.lordHealth > 0), 'Simulation continued after death');
  const catalog = new Map(identity.cardCatalog.map((entry) => [entry.id, entry]));
  const cards = list(first.cards, 'cards').map((choice, choiceIndex) => {
    const definition = catalog.get(choice.chosen);
    requireValue(definition && ['weapon', 'tool', 'charter'].includes(definition.kind), 'Chosen card missing or item incorrectly dealt');
    requireValue(ids(choice.offered, 'offered IDs').includes(choice.chosen), 'Chosen card not offered');
    requireValue(choice.offered.every((id) => catalog.has(id)), 'Unknown offered card');
    requireValue(integer(choice.tick, 'card tick') <= first.ticks, 'Late card');
    return { caseId: caseInfo.caseId, choiceIndex, tick: choice.tick, level: integer(choice.level, 'choice level'), offeredIdsJson: json(choice.offered), chosenId: choice.chosen, rarity: text(choice.rarity, 'rarity'), chosenKind: definition.kind, chosenGrowthTargetsJson: json(ids(definition.growthTargets, 'growth targets')), chosenTagsJson: json(ids(definition.tags, 'card tags')) };
  });
  const stacks = {};
  const loot = list(runtime.loot, 'loot').map((event, acquisitionIndex) => {
    requireValue(identity.selectedIds.items.includes(event.itemId), 'Unknown acquired item');
    requireValue(['chest', 'cart', 'market'].includes(event.sourceKind), 'Invalid acquisition source');
    requireValue(integer(event.tick, 'loot tick') <= first.ticks, 'Late loot');
    stacks[event.itemId] = (stacks[event.itemId] ?? 0) + integer(event.quantity, 'quantity', 1);
    requireValue(event.stackAfter === stacks[event.itemId], 'Item stack ledger mismatch');
    return { caseId: caseInfo.caseId, acquisitionIndex, tick: event.tick, sourceKind: event.sourceKind, sourceId: text(event.sourceId, 'source ID'), sourceEntityId: integer(event.sourceEntityId, 'loot entity ID'), itemId: event.itemId, quantity: event.quantity, stackAfter: event.stackAfter, foodPaid: integer(event.foodPaid, 'food paid') };
  });
  requireValue(canonical(stacks) === canonical(itemStacks), 'Final item stacks do not match loot');
  const effectDefinitions = new Map(identity.effectCatalog.map((effect) => [effect.effectId, effect]));
  const effectRows = list(runtime.effects, 'effects');
  requireValue(effectRows.length === effectDefinitions.size && new Set(effectRows.map((effect) => effect.effectId)).size === effectDefinitions.size, 'Missing or duplicate effect counters');
  const effects = effectRows.map((effect) => {
    const definition = effectDefinitions.get(effect.effectId);
    requireValue(definition, 'Unknown effect counter');
    for (const key of ['sourceId', 'sourceKind', 'trigger', 'operation', 'subject', 'amount']) requireValue(effect[key] === definition[key], `Effect definition mismatch ${effect.effectId}/${key}`);
    integer(effect.activationCount, 'activation count'); integer(effect.appliedTotal, 'applied total', -Number.MAX_SAFE_INTEGER);
    if (effect.activationCount === 0) requireValue(effect.appliedTotal === 0 && effect.firstActivationTick === null && effect.lastActivationTick === null, 'Inactive effect counters not empty');
    else requireValue(integer(effect.firstActivationTick, 'first activation') <= integer(effect.lastActivationTick, 'last activation') && effect.lastActivationTick <= first.ticks, 'Effect activation bounds invalid');
    return Object.fromEntries(S4_HEADERS.effects.map((key) => [key, key === 'caseId' ? caseInfo.caseId : effect[key]]));
  }).sort((a, b) => a.effectId.localeCompare(b.effectId));
  requireValue(Number.isFinite(metrics.tickP95Ms) && metrics.tickP95Ms >= 0, 'Invalid measured tick p95');
  return { identity, metrics, tables: { runs: [run], timeline, cards, loot, effects, determinism }, actualWarmupTicks: integer(metrics.actualWarmupTicks, 'actual warmup') };
}

export function assembleS4Tables(extracted, options, timing) {
  const cases = makeS4Cases(options.mode);
  requireValue(extracted.length === cases.length, 'Incomplete case matrix');
  const identity = extracted[0].identity;
  const tables = Object.fromEntries(Object.keys(S4_HEADERS).map((name) => [name, []]));
  extracted.forEach((entry, index) => {
    requireValue(entry.tables.runs.length === 1 && entry.tables.runs[0].caseId === cases[index].caseId, 'Unexpected, duplicate or reordered case');
    requireValue(canonical(entry.identity) === canonical(identity), 'Cross-case source/runtime/profile mismatch');
    for (const name of Object.keys(entry.tables)) tables[name].push(...entry.tables[name]);
  });
  requireValue(options.mode !== 's4-stage' || identity.sourceDirty === false, 'Full stage requires clean committed source');
  const metadata = {
    schemaVersion: 1, stage: 'S4', mode: options.mode,
    ...Object.fromEntries(['profileId', 'profileSha256', 'contentSha256', 'sourceCommit', 'sourceTreeSha256', 'sourceDirty', 'simulationAssemblySha256', 'coreAssemblySha256', 'tickRate', 'configuredDurationTicks', 'requestedTicks', 'requestedWarmupTicks', 'warmupExcluded', 'metaSnapshot', 'advertisementUses', 'tuningStatus', 'timelineSampleIntervalTicks', 'charterSlots'].map((key) => [key, identity[key]])),
    invocationArgsJson: json(timing.invocationArgs), measurementStartedAt: timing.startedAt, measurementEndedAt: timing.endedAt, elapsedWallMs: integer(timing.elapsedWallMs, 'wall duration'),
    runtimeJson: json(identity.runtime), policiesJson: json(POLICIES), peopleRulesJson: json(PEOPLE_RULES), seedsJson: json([...new Set(cases.map((entry) => entry.seed))]),
    repeatCount: 3, seasonDurationTicksJson: json(identity.seasonDurationTicks), seasonNamesJson: json(identity.seasonNames), independentCaseCount: cases.length, totalExecutions: cases.length * 3, workerLimit: options.workers,
    actualWarmupTicksMin: Math.min(...extracted.map((entry) => entry.actualWarmupTicks)), actualWarmupTicksMax: Math.max(...extracted.map((entry) => entry.actualWarmupTicks)), scenario: 'normal', shortenedSimulation: options.mode === 's4-smoke', seasonsAccelerated: false,
    acquisitionCatalogJson: json(identity.acquisitionCatalog), itemStackLimit: 'none', selectedIdsJson: json(identity.selectedIds), cardCatalogJson: json(identity.cardCatalog), effectCatalogJson: json(identity.effectCatalog),
    crossoverRule: 'first-sampled-positive-tool-total-gte-positive-weapon-total', rankFieldsJson: json(['survived', 'ticks', 'level', 'totalCombatDamage']), dominanceRule: 'policy-cofirst-in-every-peopleRule-seed-condition', foodMaterialScored: false,
    scope: options.mode === 's4-smoke' ? 'Truncated 900-tick headless integration; no full-stage balance, fun, phone UI or first-tool comprehension claim.' : 'Full-duration headless numeric league; fun, phone UI and first-tool comprehension remain unverified.',
  };
  tables.metadata = Object.entries(metadata).map(([key, value]) => ({ key, value }));
  return tables;
}

export function summarizePolicyDamage(runs) {
  const policyMeans = POLICIES.map(policy => {
    const group = runs.filter(run => run.policy === policy);
    requireValue(group.length > 0, 'Missing policy diagnostic cohort');
    const totalDamage = run => run.weaponDamage + run.toolActivationDamage + run.toolGrowthDamage + run.allyDamage;
    return { policy, independentCases: group.length, damage: group.reduce((sum, run) => sum + totalDamage(run), 0) / group.length, survivalRate: group.filter(run => run.survived).length / group.length, level: group.reduce((sum, run) => sum + run.level, 0) / group.length };
  });
  const mean = policyMeans.reduce((sum, row) => sum + row.damage, 0) / policyMeans.length;
  const policyDamageCv = mean === 0 ? null : Math.sqrt(policyMeans.reduce((sum, row) => sum + (row.damage - mean) ** 2, 0) / policyMeans.length) / Math.abs(mean);
  return { policyMeans, policyDamageCv };
}

function runProcess(executable, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, { stdio: ['ignore', 'ignore', 'pipe'] });
    let stderr = '';
    let timedOut = false;
    const timeout = setTimeout(() => { timedOut = true; child.kill('SIGKILL'); }, 600_000);
    child.stderr.on('data', (chunk) => { stderr = (stderr + chunk.toString()).slice(-16000); });
    child.on('error', (error) => { clearTimeout(timeout); reject(error); });
    child.on('close', (code, signal) => {
      clearTimeout(timeout);
      if (code === 0 && !timedOut) resolve();
      else reject(new Error(`Simulation failed (${timedOut ? 'timeout' : code ?? signal}): ${stderr}`));
    });
  });
}
async function readJson(filename) {
  const bytes = await fs.readFile(filename);
  return { value: JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes)), sha256: digest(bytes) };
}
export async function runS4Cli(args) {
  const options = parseS4Options(args);
  const started = Date.now();
  const startedAt = new Date(started).toISOString();
  await fs.mkdir('artifacts', { recursive: true });
  const destination = path.resolve('artifacts', `league-${options.mode}`);
  const temporary = await fs.mkdtemp(`${destination}.incomplete-`);
  await fs.mkdir(path.join(temporary, 'raw'));
  const requestedTicks = options.mode === 's4-smoke' ? 900 : 21600;
  try {
    const extracted = await boundedMap(makeS4Cases(options.mode), options.workers, async (caseInfo) => {
      const resultsPath = `raw/${caseInfo.caseId}.results.json`;
      const metricsPath = `raw/${caseInfo.caseId}.metrics.json`;
      const simArgs = ['core/src/SowSiege.Sim/bin/Release/net8.0/SowSiege.Sim.dll', '--data', 'data', '--profile', options.profile, '--scenario', 'normal', '--people-rule', caseInfo.peopleRule, '--policy', caseInfo.policy, '--seed', String(caseInfo.seed), '--iterations', '3', '--output', path.join(temporary, resultsPath), '--metrics', path.join(temporary, metricsPath)];
      if (options.mode === 's4-smoke') simArgs.push('--duration-ticks', '900');
      await runProcess('dotnet', simArgs);
      const [results, metrics] = await Promise.all([readJson(path.join(temporary, resultsPath)), readJson(path.join(temporary, metricsPath))]);
      const extractedCase = extractS4Case({ caseInfo, results: results.value, metrics: metrics.value, resultsPath, metricsPath, resultsSha256: results.sha256, metricsSha256: metrics.sha256 }, { repeatCount: 3, requestedTicks });
      requireValue(options.mode !== 's4-stage' || !extractedCase.identity.sourceDirty, 'Full stage requires clean committed source');
      return extractedCase;
    });
    const ended = Date.now();
    const tables = assembleS4Tables(extracted, options, { invocationArgs: args, startedAt, endedAt: new Date(ended).toISOString(), elapsedWallMs: ended - started });
    const baseline = extracted.find(entry => entry.tables.runs[0].caseId === 'mixed__A__42');
    requireValue(baseline, 'Missing timing baseline');
    const diagnostic = summarizePolicyDamage(tables.runs);
    const trend = {
      ...baseline.metrics, balanceDispersion: null, gameplayBalanceClaim: false, policyDamageCv: diagnostic.policyDamageCv, balanceMetric: 'Coefficient of variation of policy mean actual combat damage over independent people-rule/seed cases; diagnostic, not the S4 dominance gate or a balance pass.',
      measurementScope: 'S4 normal league mixed/A/seed42 tick p95 across three repeats; not pooled league p95 or mobile performance.',
      config: { ...baseline.metrics.config, mode: options.mode, profileId: baseline.identity.profileId, policies: POLICIES, peopleRules: PEOPLE_RULES, seeds: [...new Set(makeS4Cases(options.mode).map(entry => entry.seed))], requestedTicks, repeatCount: 3, workerLimit: options.workers, timingStatistic: 'mixed-A-seed42-case-tick-p95' },
      league: { policyMeans: diagnostic.policyMeans, independentCases: extracted.length, totalExecutions: extracted.length * 3, representativeCaseId: baseline.tables.runs[0].caseId, timingStatistic: 'mixed-A-seed42-case-tick-p95', reportSource: 'seven adjacent CSV files; trend JSON is not report input', sourceMetrics: extracted.map(entry => ({ caseId: entry.tables.runs[0].caseId, path: entry.tables.runs[0].sourceMetricsPath, sha256: entry.tables.runs[0].sourceMetricsSha256, tickP95Ms: entry.metrics.tickP95Ms })) },
    };
    // The series is defined by the fixed league configuration and explicit representative case.
    delete trend.policy;
    delete trend.ticks;
    await fs.writeFile(path.join(temporary, 'metrics.json'), `${JSON.stringify(trend, null, 2)}\n`);
    for (const [name, headers] of Object.entries(S4_HEADERS)) await fs.writeFile(path.join(temporary, `${name}.csv`), formatCsv(headers, tables[name]));
    try { await fs.rename(destination, `${destination}.previous-${started}`); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    await fs.rename(temporary, destination);
    console.log(`S4 ${options.mode}: ${tables.runs.length} independent cases / ${tables.determinism.length} executions, seven CSVs at ${destination}`);
    return destination;
  } catch (error) {
    throw new Error(`${error.message}; incomplete raw evidence retained at ${temporary}`, { cause: error });
  }
}
if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  runS4Cli(process.argv.slice(2)).catch((error) => { console.error(error.message); process.exitCode = 1; });
}
