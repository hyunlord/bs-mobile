import { validateMeta } from './meta-content.mjs';
import { weaponGrowthFilename, weaponGrowthDuplicateKeys, validateWeaponGrowth } from './validate-weapon-growth.mjs';
import { validateFirstPlayableDefinitions } from './first-playable-content.mjs';
import { readFile } from 'node:fs/promises';
import { jsonFiles } from './content-files.mjs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';

const namespaceId = /^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$/;
const experimentFilename = /^(?:experiments\/)?tuning-s4b-[a-zA-Z0-9_-]+\.json$/;
const configurationKinds = new Set(['profile', 'tuning', 'experiment-tuning', 'weapon-growth', 'presentation', 'meta']);
const directoryKinds = new Map([
  ['tools', 'tool'], ['heroes', 'hero'], ['estates', 'estate'],
  ['weapons', 'weapon'], ['charters', 'charter'], ['items', 'item'],
  ['vassals', 'vassal'], ['enemies', 'enemy'], ['evolutions', 'evolution'],
  ['skins', 'skin'], ['profiles', 'profile'], ['meta', 'meta'],
]);

function inferKind(relative) {
  if (weaponGrowthFilename.test(relative)) return 'weapon-growth';
  if (relative === 'experiments/tuning-s2-baseline.json') return 'tuning';
  if (relative === 'first-playable-tuning.json') return 'tuning';
  if (experimentFilename.test(relative)) return 'experiment-tuning';
  const segments = relative.split(path.sep);
  if (segments[0] === 'test') segments.shift();
  return directoryKinds.get(segments[0]) ?? (segments.length === 1 ? path.basename(segments[0], '.json') : null);
}

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function experimentTuningDifferences(base, actual, location = '') {
  if (isRecord(base) && isRecord(actual)) {
    return [...new Set([...Object.keys(base), ...Object.keys(actual)])].sort().flatMap(key =>
      experimentTuningDifferences(base[key], actual[key], location ? `${location}.${key}` : key));
  }
  if (Array.isArray(base) && Array.isArray(actual) && base.length === actual.length) {
    return base.flatMap((value, index) => experimentTuningDifferences(value, actual[index], `${location}[${index}]`));
  }
  if (Object.is(base, actual)) return [];
  return [{ path: location, base, actual, allowed: location === 'world.map.lordHealth' || location.startsWith('world.threat.') }];
}

function containsNumber(value) {
  if (typeof value === 'number') return true;
  if (Array.isArray(value)) return value.some(containsNumber);
  return isRecord(value) && Object.values(value).some(containsNumber);
}

export async function validateContent(dataDirectory, { fullPool = false } = {}) {
  const dataRoot = path.resolve(dataDirectory);
  const errors = [];
  const schemas = new Map();
  const records = [];
  const ajv = new Ajv2020({ allErrors: true, strict: true, validateSchema: true });
  let files;
  try {
    files = await jsonFiles(dataRoot);
  } catch (error) {
    return { valid: false, records: 0, schemas: 0, errors: [String(error)] };
  }
  const parsed = new Map();
  for (const file of files) {
    try {
      const text = await readFile(file, 'utf8');
      parsed.set(file, JSON.parse(text));
      if (weaponGrowthFilename.test(path.relative(dataRoot, file)) || ['weapon','meta'].includes(inferKind(path.relative(dataRoot, file)))) {
        for (const key of weaponGrowthDuplicateKeys(text)) errors.push(`${path.relative(dataRoot, file)}: duplicate JSON key ${key}`);
      }
    } catch (error) {
      errors.push(`${path.relative(dataRoot, file)}: invalid JSON: ${String(error)}`);
    }
  }
  for (const [file, schema] of parsed) {
    const relative = path.relative(dataRoot, file);
    if (relative.split(path.sep)[0] !== 'schema') continue;
    const kind = path.basename(file, '.schema.json');
    if (!file.endsWith('.schema.json') || path.dirname(relative) !== 'schema') {
      errors.push(`${relative}: schema must be data/schema/<kind>.schema.json`);
      continue;
    }
    try {
      if (!isRecord(schema) || schema.$schema !== 'https://json-schema.org/draft/2020-12/schema') {
        throw new Error('schema must explicitly declare JSON Schema Draft 2020-12');
      }
      if (!ajv.validateSchema(schema)) throw new Error(ajv.errorsText());
      ajv.addSchema(schema, kind);
      schemas.set(kind, schema);
    } catch (error) {
      errors.push(`${relative}: invalid schema: ${String(error)}`);
    }
  }
  for (const kind of schemas.keys()) {
    try {
      ajv.getSchema(kind);
    } catch (error) {
      errors.push(`schema/${kind}.schema.json: cannot compile schema: ${String(error)}`);
      schemas.delete(kind);
    }
  }
  const ids = new Map();
  for (const [file, record] of parsed) {
    const relative = path.relative(dataRoot, file);
    if (relative.split(path.sep)[0] === 'schema') continue;
    const kind = inferKind(relative);
    if (!kind || !schemas.has(kind)) {
      errors.push(`${relative}: missing schema for content kind ${kind ?? '(unknown directory)'}`);
      continue;
    }
    const validate = ajv.getSchema(kind);
    const schemaValid = validate(record);
    if (!schemaValid) errors.push(`${relative}: schema violation: ${ajv.errorsText(validate.errors, { separator: '; ' })}`);
    if (!isRecord(record)) {
      errors.push(`${relative}: each content file must hold one object`);
      continue;
    }
    records.push({ relative, kind, record, schemaValid });
    if (!['tuning', 'experiment-tuning', 'weapon-growth', 'presentation', 'meta'].includes(kind) || Object.hasOwn(record, 'id')) {
      if (typeof record.id !== 'string' || !namespaceId.test(record.id)) {
        errors.push(`${relative}: invalid namespace ID`);
      } else if (ids.has(record.id)) {
        errors.push(`${relative}: duplicate ID ${record.id}; first defined in ${ids.get(record.id).relative}`);
      } else {
        ids.set(record.id, { relative, kind });
      }
    }
    if (kind === 'tool' && (!isRecord(record.activation) || !isRecord(record.growth))) {
      errors.push(`${relative}: tool requires both activation and growth`);
    }
    if (kind === 'presentation' && schemaValid && record.camera.minHalfHeight > record.camera.maxHalfHeight) errors.push(`${relative}: camera minHalfHeight must not exceed maxHalfHeight`);
    if (kind === 'skin' && containsNumber(record)) errors.push(`${relative}: skin must not contain numeric stats`);
  }
  function reference(relative, field, id, kind) {
    if (!relative.startsWith(`test${path.sep}`) && !relative.startsWith(`profiles${path.sep}`) && ids.get(id)?.relative.startsWith(`test${path.sep}`)) errors.push(`${relative}: canonical reference cannot use test content ${field}`);
    if (typeof id !== 'string' || !namespaceId.test(id)) {
      errors.push(`${relative}: invalid namespace reference ${field}`);
    } else if (ids.get(id)?.kind !== kind) {
      errors.push(`${relative}: unresolved ${kind} reference ${field}=${id}`);
    }
  }
  const experimentFiles = new Map(records.filter(entry => entry.kind === 'experiment-tuning').map(entry => [entry.relative, entry.record]));
  const baseTuning = records.find(entry => entry.kind === 'tuning' && entry.relative === 'tuning.json')?.record;
  const gameplayRecords = records.filter(entry => entry.kind === 'profile' && isRecord(entry.record.gameplay)).map(entry => ({
    relative: entry.relative, kind: 'experiment-tuning', record: { tuning: records.find(config => config.kind === 'tuning' && config.relative === (entry.record.tuningFile ?? 'tuning.json'))?.record, ...entry.record.gameplay },
  }));
  for (const { relative, kind, record } of [...records, ...gameplayRecords]) {
    if (kind === 'profile' && record.tuningFile !== undefined && !records.some(entry => entry.kind === 'tuning' && entry.relative === record.tuningFile && entry.schemaValid)) errors.push(`${relative}: missing or invalid baseline tuning`);
    if (kind === 'experiment-tuning') {
      const profiles = records.filter(entry => entry.kind === 'profile' && (entry.record.experiment?.tuningFile === relative || entry.relative === relative));
      const baselines = profiles.length ? profiles.map(entry => records.find(config => config.kind === 'tuning' && config.relative === (entry.record.tuningFile ?? 'tuning.json'))?.record) : [baseTuning];
      if (baselines.some(baseline => !isRecord(baseline)) || !isRecord(record.tuning)) {
        errors.push(`${relative}: experiment requires valid base tuning.json and embedded tuning`);
      } else {
        for (const difference of baselines.flatMap(baseline => experimentTuningDifferences(baseline, record.tuning))) {
          if (!difference.allowed) errors.push(`${relative}: ${difference.path} is outside preregistered global tuning (only world.map.lordHealth and world.threat.* may differ)`);
        }
      }
      const overrides = Array.isArray(record.enemyOverrides) ? record.enemyOverrides : [];
      const seen = new Set();
      for (const override of overrides) {
        reference(relative, 'enemyOverrides.id', override?.id, 'enemy');
        if (seen.has(override?.id)) errors.push(`${relative}: duplicate enemy override ${override?.id}`);
        seen.add(override?.id);
      }
      const movement = record.experiment?.movement;
      const map = record.tuning?.world?.map;
      if (isRecord(movement) && isRecord(map)) {
        if (movement.decisionPeriodTicks > record.tuning.durationTicks) errors.push(`${relative}: decision period exceeds durationTicks`);
        const points = Array.isArray(movement.circuitOffsets) ? movement.circuitOffsets : [];
        const positions = new Set();
        for (const point of points) {
          if (!Number.isSafeInteger(point?.x) || !Number.isSafeInteger(point?.y)) continue;
          if (Math.abs(point.x) > map.width || Math.abs(point.y) > map.height) errors.push(`${relative}: circuit offset exceeds map bounds`);
          const x = Math.max(0, Math.min(map.width, Math.floor(map.width / 2) + point.x));
          const y = Math.max(0, Math.min(map.height, Math.floor(map.height / 2) + point.y));
          positions.add(`${x},${y}`);
        }
        if (positions.size < 2) errors.push(`${relative}: at least two distinct clamped waypoints required`);
      }
    }
    if (kind === 'profile' && (isRecord(record.experiment) || isRecord(record.gameplay))) {
      const filename = record.experiment?.tuningFile;
      if (!record.gameplay && (typeof filename !== 'string' || !experimentFilename.test(filename))) {
        errors.push(`${relative}: experiment requires a safe tuning filename`);
        continue;
      }
      const wrapper = record.gameplay ?? experimentFiles.get(filename);
      if (!wrapper) {
        errors.push(`${relative}: missing experiment tuning ${filename}`);
        continue;
      }
      const selected = Array.isArray(record.selection?.enemies) ? record.selection.enemies : [];
      const overrides = Array.isArray(wrapper.enemyOverrides) ? wrapper.enemyOverrides : [];
      const overrideIds = overrides.map(override => override?.id);
      if (overrideIds.length !== selected.length || new Set(overrideIds).size !== overrideIds.length || selected.some(id => !overrideIds.includes(id))) {
        errors.push(`${relative}: enemy override IDs must match primary selection.enemies exactly`);
      }
    }
  }
  for (const { relative, kind, record } of records) {
    if ((kind === 'hero' || kind === 'estate') && Object.hasOwn(record, 'startingTool')) {
      reference(relative, 'startingTool', record.startingTool, 'tool');
    }
    if (kind === 'tool' && Array.isArray(record.antiSynergy)) {
      record.antiSynergy.forEach((id, index) => reference(relative, `antiSynergy[${index}]`, id, 'tool'));
    }
    if (kind === 'tuning' || kind === 'experiment-tuning') {
      const tuning = kind === 'tuning' ? record : record.tuning;
      if (!isRecord(tuning)) continue;
      reference(relative, 'defaultHero', tuning.defaultHero, 'hero');
      reference(relative, 'defaultEstate', tuning.defaultEstate, 'estate');
      const world = tuning.world;
      if (!isRecord(world)) continue;
      if (isRecord(world.progression)) reference(relative, 'world.progression.startingWeapon', world.progression.startingWeapon, 'weapon');
      if (Array.isArray(world.seasons) && world.seasons.every(isRecord)) {
        if (world.seasons.reduce((sum, season) => sum + season.durationTicks, 0) !== tuning.durationTicks) {
          errors.push(`${relative}: season durations must sum to durationTicks`);
        }
        if (new Set(world.seasons.map((season) => season.name)).size !== world.seasons.length) {
          errors.push(`${relative}: season names must be unique`);
        }
      }
      const limits = [
        ['enemies', world.threat?.enemyCap], ['farms', world.farms?.capacity],
        ['buildings', world.buildings?.siteCount], ['people', world.people?.maxPeople],
      ];
      for (const [field, capacity] of limits) {
        if (isRecord(world.load) && world.load[field] > capacity) errors.push(`${relative}: load.${field} exceeds capacity`);
      }
      if (isRecord(world.people)) {
        if (world.people.initialFood > world.people.foodCapacity) errors.push(`${relative}: initial food exceeds capacity`);
        if (world.people.initialPeasants >= world.people.maxPeople) errors.push(`${relative}: initial peasants leave no vassal capacity`);
        if (world.people.squadSize > world.people.maxPeople) errors.push(`${relative}: squad size exceeds people capacity`);
      }
      if (isRecord(world.map) && isRecord(world.threat)) {
        if (world.threat.spawnInset * 2 >= Math.min(world.map.width, world.map.height)) errors.push(`${relative}: spawn inset exceeds map bounds`);
        if (world.map.cellSize > Math.min(world.map.width, world.map.height)) errors.push(`${relative}: spatial cell exceeds map bounds`);
      }
      const rarities = world.progression?.rarities;
      if (Array.isArray(rarities) && rarities.every(isRecord) && new Set(rarities.map((rarity) => rarity.name)).size !== rarities.length) {
        errors.push(`${relative}: rarity names must be unique`);
      }
    }
  }
  let gateCounts;
  if (fullPool) {
    const canonical = records.filter(({relative,kind}) => !relative.startsWith(`test${path.sep}`) && !configurationKinds.has(kind));
    const byId = new Map(records.map(entry => [entry.record.id, entry]));
    const list = value => Array.isArray(value) ? value : [];
    const expect = (condition, message) => { if (!condition) errors.push(message); };
    const ref = (entry, field, id, kind) => {
      reference(entry.relative, field, id, kind);
      expect(entry.relative.startsWith(`test${path.sep}`) || !byId.get(id)?.relative.startsWith(`test${path.sep}`), `${entry.relative}: canonical reference cannot use test content ${field}`);
    };
    const counts = {weapon:30,tool:40,charter:16,item:60,vassal:16,enemy:24,evolution:30,hero:1,estate:1};
    for (const [kind,count] of Object.entries(counts)) expect(canonical.filter(e=>e.kind===kind).length===count, `Canonical ${kind} count must equal ${count}`);
    expect(canonical.some(e=>e.kind==='skin'), 'At least one canonical skin required');
    const tags = new Map(); const pairs = new Set(); const recipes = new Set(); let loopLinked = 0;
    const baseEstate = records.find(e=>e.kind==='tuning')?.record.defaultEstate;
    for (const entry of records.filter(e=>!configurationKinds.has(e.kind))) {
      const {record:r,kind,relative} = entry;
      const isCanonical = !relative.startsWith(`test${path.sep}`);
      for (const tag of new Set(isCanonical ? list(r.tags) : [])) tags.set(tag,(tags.get(tag)??0)+1);
      let baseLinked = false;
      for (const link of list(r.loopLinks)) {
        if (!isRecord(link)) continue;
        ref(entry,'loopLinks.estateId',link.estateId,'estate');
        const stages = list(byId.get(link.estateId)?.record.uniqueLoop?.stages);
        expect(stages.some(stage=>stage.id===link.stage), `${relative}: unresolved estate loop stage ${link.stage}`);
        if (link.estateId===baseEstate && stages.some(stage=>stage.id===link.stage)) baseLinked=true;
      }
      if (isCanonical && baseLinked && ['weapon','tool','item'].includes(kind)) loopLinked++;
      if (kind==='hero') for(const id of list(r.affinityEstateIds)) ref(entry,'affinityEstateIds',id,'estate');
      if (kind==='vassal') ref(entry,'heroId',r.heroId,'hero');
      if (kind==='item') for(const id of list(r.linkedToolIds)) ref(entry,'linkedToolIds',id,'tool');
      if (kind==='skin') ref(entry,'targetId',r.targetId,r.targetKind);
      if (kind==='estate') {
        const stages=list(r.uniqueLoop?.stages).map(s=>s.id);
        expect(new Set(stages).size===stages.length,`${relative}: duplicate loop stage`);
      }
      if (kind==='tool') {
        const anti=list(r.antiSynergy); const notes=list(r.antiSynergyNotes);
        expect(notes.length===anti.length && notes.every(n=>anti.includes(n.otherId)) && new Set(notes.map(n=>n.otherId)).size===notes.length,`${relative}: anti-synergy notes must match references exactly`);
        for(const id of anti) {
          ref(entry,'antiSynergy',id,'tool');
          expect(id!==r.id,`${relative}: self anti-synergy forbidden`);
          if(isCanonical && id!==r.id && byId.get(id)?.kind==='tool') pairs.add([r.id,id].sort().join('|'));
        }
      }
      if(kind==='evolution') {
        const inputs=list(r.inputIds); const kinds=r.kind==='weapon-tool'?['weapon','tool']:r.kind==='tool-tool'?['tool','tool']:['tool'];
        expect(inputs.length===kinds.length,`${relative}: evolution input arity mismatch`);
        inputs.forEach((id,index)=>ref(entry,`inputIds[${index}]`,id,kinds[index]));
        expect(new Set(inputs).size===inputs.length,`${relative}: evolution inputs must be distinct`);
        expect(inputs.includes(r.result?.baseId),`${relative}: evolution result base must be an input`);
        const condition=r.growthCondition;
        if(r.kind==='tool-growth') {
          const states={land:['seeded','growing','ripe'],building:['built','ruined','rebuilt'],people:['staffed','mobilized','returned']};
          expect(isRecord(condition) && condition.target===byId.get(inputs[0])?.record.growth?.target && states[condition.target]?.includes(condition.state),`${relative}: invalid evolution growth condition`);
        } else expect(condition===null,`${relative}: non-growth evolution condition must be null`);
        const signature=JSON.stringify([r.kind,[...inputs].sort(),condition ? [condition.target,condition.state,condition.minimum] : null]);
        expect(!recipes.has(signature),`${relative}: duplicate evolution recipe`); recipes.add(signature);
      }
    }
    for(const [tag,count] of tags) expect(count>=3,`Tag ${tag} needs at least 3 distinct canonical records; got ${count}`);
    expect(pairs.size>=8,`At least 8 distinct anti-synergy pairs required; got ${pairs.size}`);
    expect(loopLinked>=15,`At least 15 base-loop-linked weapon/tool/item records required; got ${loopLinked}`);
    for(const kind of ['weapon-tool','tool-tool','tool-growth']) expect(canonical.filter(e=>e.kind==='evolution' && e.record.kind===kind).length===10,`Evolution kind ${kind} count must equal 10`);
    gateCounts = {
      canonicalCounts: Object.fromEntries([...Object.keys(counts),'skin'].map(kind=>[kind,canonical.filter(e=>e.kind===kind).length])),
      minTagDistinctCount: Math.min(...tags.values()),
      noGrowthTools: canonical.filter(e=>e.kind==='tool' && !isRecord(e.record.growth)).length,
      evolutionCounts: Object.fromEntries(['weapon-tool','tool-tool','tool-growth'].map(kind=>[kind,canonical.filter(e=>e.kind==='evolution' && e.record.kind===kind).length])),
      unorderedAntiSynergyPairs:pairs.size,
      skinNumericCount:canonical.filter(e=>e.kind==='skin' && containsNumber(e.record)).length,
      loopLinkedDistinct:loopLinked
    };
    const effectIds = new Map();
    const overrides = [];
    for (const profile of records.filter(e => e.kind === 'profile' && e.record.firstPlayable)) {
      const selection = profile.record.selection ?? {}, runtime = profile.record.runtime ?? {};
      for (const [group, values] of Object.entries(profile.record.runtimeOverrides ?? {})) {
        const selected = group === 'equipment' ? [...list(selection.weapons), ...list(selection.tools)] : list(runtime[group]);
        for (const [id, projection] of Object.entries(values)) {
          expect(selected.includes(id), `${profile.relative}: runtime override outside selected ${group}: ${id}`);
          const original = byId.get(id);
          if (original) overrides.push({ ...original, relative: `${profile.relative}#runtimeOverrides.${group}.${id}`, record: { ...original.record, runtimeProjection: projection } });
        }
      }
    }
    for (const entry of [...records.filter(e=>!configurationKinds.has(e.kind)), ...overrides]) {
      const projection=entry.record.runtimeProjection;
      if(entry.record.designStatus==='s4-runtime' && ['weapon','tool','charter','item','evolution'].includes(entry.kind)) expect(isRecord(projection),`${entry.relative}: S4 content requires runtime projection`);
      if(!isRecord(projection)) continue;
      const actions=list(projection.growthActions);
      const projectionEffectIds = new Set();
      expect(list(projection.effects).length+actions.length>0,`${entry.relative}: empty runtime projection`);
      expect(entry.kind!=='weapon' || actions.length===0,`${entry.relative}: weapon cannot have growth actions`);
      expect(new Set(actions.map(a=>a.target)).size===actions.length,`${entry.relative}: duplicate growth target`);
      for(const action of actions) expect((action.target==='building' && action.operation==='construct' && action.durationTicks===0) || (action.target==='people' && action.operation==='garrison' && action.durationTicks>0),`${entry.relative}: unsupported growth action`);
      for(const effect of list(projection.effects)) {
        expect((!effectIds.has(effect.id) || (entry.relative.includes('#runtimeOverrides.') && effectIds.get(effect.id) === entry.record.id)) && !projectionEffectIds.has(effect.id) && !byId.has(effect.id),`${entry.relative}: duplicate runtime effect ID`);
        effectIds.set(effect.id, entry.record.id); projectionEffectIds.add(effect.id);
        expect(effect.amount!==0 && (effect.operation==='stat-add' || effect.amount>0),`${entry.relative}: invalid effect amount`);
        expect(['stat-add','planting-bias'].includes(effect.operation)===(effect.trigger==='modifier'),`${entry.relative}: modifier trigger mismatch`);
        expect(effect.trigger!=='modifier' || effect.foodCost===0,`${entry.relative}: modifier food cost is unsupported`);
        const subjects={
          'stat-add':['attack-damage','attack-knockback','attack-cooldown','repair-amount','draft-min-rest','draft-speed','worker-speed','worker-incoming-damage','building-front-damage','building-rear-damage'],
          'planting-bias':['existing-edge','estate-inward'],'damage-pulse':['weapon-front'],
          'repair-nearest':['building'],'return-via-building':['building'],'worker-buff':['people'],'rally-returners':['people'],
          'extend-duty':['people'],'guard-return':['people'],'shield-farms':['land'],'damage-young-plots':['land'],'pause-neighbor-growth':['land']
        };
        if(effect.operation==='plant-path') ref(entry,'runtimeProjection.effect.subject',effect.subject,'tool');
        else if(effect.operation==='harvest-near') expect(['weapon','tool'].includes(byId.get(effect.subject)?.kind),`${entry.relative}: unknown harvest subject`);
        else expect(subjects[effect.operation]?.includes(effect.subject),`${entry.relative}: unsupported runtime subject`);
        if(['worker-buff','rally-returners','shield-farms','extend-duty','guard-return','pause-neighbor-growth'].includes(effect.operation)) expect(effect.durationTicks>0,`${entry.relative}: timed effect requires duration`);
        for(const condition of list(effect.conditions)) {
          expect(['count','season'].includes(condition.kind) || condition.minimum===0, `${entry.relative}: unused condition minimum must be zero`);
          if(['equipment-owned','equipment-id'].includes(condition.kind)) expect(['weapon','tool'].includes(byId.get(condition.value)?.kind),`${entry.relative}: invalid effect equipment reference`);
          const values={'equipment-kind':['weapon','tool'],'growth-target':['land','building','people'],'person-role':['peasant','militia','returning','guard','vassal'],'enemy-target':['lord','seed','ripe','building'],'count':['farms','buildings','people','harvests']};
          if(values[condition.kind]) expect(values[condition.kind].includes(condition.value),`${entry.relative}: invalid condition value`);
          if(['near-seed','near-ripe','near-building','estate-inside','estate-outside','building-ruined','building-new','shield-consumed','season'].includes(condition.kind)) expect(condition.value===null && (condition.kind==='season'?condition.minimum<=3:condition.minimum===0),`${entry.relative}: invalid condition parameters`);
        }
      }
    }
    const profiles=records.filter(e=>e.kind==='profile');
    expect(profiles.some(entry=>entry.relative===path.join('profiles','s2-baseline.json')), 'Explicit s2-baseline runtime profile required');
    const allSelected = new Set();
    for(const {record:r,relative} of profiles) {
      const projection = id => {
        const kind = byId.get(id)?.kind;
        const group = kind === 'weapon' || kind === 'tool' ? 'equipment' : `${kind}s`;
        return r.firstPlayable ? r.runtimeOverrides?.[group]?.[id] ?? byId.get(id)?.record.runtimeProjection : byId.get(id)?.record.runtimeProjection;
      };
      if (r.firstPlayable) {
        try {
          validateFirstPlayableDefinitions(r, records.map(e => e.record), records.find(e => e.kind === 'tuning' && e.relative === r.tuningFile)?.record);
        } catch (error) { errors.push(`${relative}: ${error.message}`); }
      }
      if (isRecord(r.runtime)) {
        for (const [directory,kind] of [['charters','charter'],['items','item'],['evolutions','evolution']]) for (const id of list(r.runtime[directory])) {
          reference(relative,`runtime.${directory}`,id,kind); allSelected.add(id);
          expect(byId.get(id)?.record.designStatus==='s4-runtime' && isRecord(byId.get(id)?.record.runtimeProjection),`${relative}: runtime selection requires implemented projection ${id}`);
        }
        const sources=list(r.runtime.tuning?.lootSources);
        expect(new Set(sources.map(s=>s.id)).size===sources.length,`${relative}: duplicate loot channel ID`);
        const equipmentIds=[...list(r.selection?.tools),...list(r.selection?.weapons)];
        const selectedTags=new Set(equipmentIds.flatMap(id=>list(byId.get(id)?.record.tags)));
        for(const id of [...equipmentIds,...list(r.runtime.charters),...list(r.runtime.items),...list(r.runtime.evolutions)]) {
          for(const effect of list(projection(id)?.effects)) {
            if(['plant-path','harvest-near'].includes(effect.operation)) expect(equipmentIds.includes(effect.subject),`${relative}: effect subject outside selected equipment`);
            for(const condition of list(effect.conditions)) {
              if(['equipment-owned','equipment-id'].includes(condition.kind)) expect(equipmentIds.includes(condition.value),`${relative}: condition equipment outside selected profile`);
              if(['owned-tag','equipment-tag'].includes(condition.kind)) expect(selectedTags.has(condition.value),`${relative}: condition tag outside selected profile`);
            }
          }
        }
        for(const id of list(r.runtime.items)) for(const tag of list(projection(id)?.requiredTags)) expect(selectedTags.has(tag),`${relative}: unavailable item tag ${tag}`);
        for(const id of list(r.runtime.evolutions)) for(const input of list(byId.get(id)?.record.inputIds)) expect(equipmentIds.includes(input),`${relative}: evolution input outside selected equipment ${input}`);
      }
      if(relative===path.join('profiles','s4-stage-one.json')) {
        expect(isRecord(r.runtime),`${relative}: S4 runtime projection required`);
        for(const [kind,count] of Object.entries({weapons:4,tools:4,enemies:6,heroes:1,estates:1})) expect(list(r.selection?.[kind]).length===count,`${relative}: S4 selection ${kind} must equal ${count}`);
        for(const [kind,count] of Object.entries({charters:4,items:12,evolutions:2})) expect(list(r.runtime?.[kind]).length===count,`${relative}: S4 runtime ${kind} must equal ${count}`);
      }
      const isBaseline = relative === path.join('profiles', 's2-baseline.json');
      const selected=new Set();
      const expectedCounts={selection:{weapons:3,tools:3,enemies:4,heroes:1,estates:1},testSelection:{weapons:0,tools:2,enemies:0,heroes:1,estates:1}};
      if (isBaseline) for(const [group,kinds] of Object.entries(expectedCounts)) for(const [kind,count] of Object.entries(kinds)) expect(list(r[group]?.[kind]).length===count,`${relative}: baseline profile ${group}.${kind} must select ${count}`);
      for(const group of ['selection','testSelection']) for(const [directory,kind] of directoryKinds) {
        if(!['weapon','tool','enemy','hero','estate'].includes(kind)) continue;
        if (group==='selection') expect(list(r[group]?.[directory]).length>0, `${relative}: runtime profile must select nonempty ${directory}`);
        for(const id of list(r[group]?.[directory])) {
          reference(relative,`${group}.${directory}`,id,kind);
          expect(!selected.has(id),`${relative}: duplicate profile selection ${id}`); selected.add(id); allSelected.add(id);
          const target=byId.get(id);
          expect(target?.record.designStatus==='s2-runtime' || (isRecord(r.runtime) && target?.record.designStatus==='s4-runtime'),`${relative}: candidate cannot enter runtime profile ${id}`);
          expect(Boolean(target?.relative.startsWith(`test${path.sep}`))===(group==='testSelection'),`${relative}: profile test selection boundary mismatch ${id}`);
        }
      }
    }
    for(const entry of records.filter(e=>!configurationKinds.has(e.kind))) expect((['s2-runtime','s4-runtime'].includes(entry.record.designStatus))===allSelected.has(entry.record.id),`${entry.relative}: runtime status must match explicit profile selection union`);
  }
  for (const entry of records.filter(e => e.kind === 'meta' && e.schemaValid)) {
    try {
      if (entry.relative !== path.join('meta', 'progression.json')) throw new Error('Meta catalog must use meta/progression.json');
      validateMeta(entry.record, path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..'), dataRoot);
    } catch (error) { errors.push(`${entry.relative}: meta validation: ${error.message}`); }
  }
  errors.push(...validateWeaponGrowth(records));
  if (schemas.size === 0) errors.push('No valid schemas found');
  if (records.length === 0) errors.push('No content records found');
  return { valid: errors.length === 0, records: records.length, schemas: schemas.size, errors, ...(fullPool ? {gateCounts} : {}) };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = await validateContent(process.argv[2] ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../data'), { fullPool: true });
  console.log(JSON.stringify(result, null, 2));
  process.exitCode = result.valid ? 0 : 1;
}
