import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';

const namespaceId = /^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$/;
const directoryKinds = new Map([
  ['tools', 'tool'], ['heroes', 'hero'], ['estates', 'estate'],
  ['weapons', 'weapon'], ['charters', 'charter'], ['items', 'item'],
  ['vassals', 'vassal'], ['enemies', 'enemy'], ['evolutions', 'evolution'],
  ['skins', 'skin'], ['profiles', 'profile'],
]);

async function jsonFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.sort((a, b) => a.name.localeCompare(b.name)).map(async (entry) => {
    const filename = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error(`Symbolic links are not content: ${filename}`);
    if (entry.isDirectory()) return jsonFiles(filename);
    return entry.isFile() && entry.name.endsWith('.json') ? [filename] : [];
  }));
  return nested.flat();
}

function inferKind(relative) {
  const segments = relative.split(path.sep);
  if (segments[0] === 'test') segments.shift();
  return directoryKinds.get(segments[0]) ?? (segments.length === 1 ? path.basename(segments[0], '.json') : null);
}

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
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
      parsed.set(file, JSON.parse(await readFile(file, 'utf8')));
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
    if (!validate(record)) errors.push(`${relative}: schema violation: ${ajv.errorsText(validate.errors, { separator: '; ' })}`);
    if (!isRecord(record)) {
      errors.push(`${relative}: each content file must hold one object`);
      continue;
    }
    records.push({ relative, kind, record });
    if (kind !== 'tuning' || Object.hasOwn(record, 'id')) {
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
  for (const { relative, kind, record } of records) {
    if ((kind === 'hero' || kind === 'estate') && Object.hasOwn(record, 'startingTool')) {
      reference(relative, 'startingTool', record.startingTool, 'tool');
    }
    if (kind === 'tool' && Array.isArray(record.antiSynergy)) {
      record.antiSynergy.forEach((id, index) => reference(relative, `antiSynergy[${index}]`, id, 'tool'));
    }
    if (kind === 'tuning') {
      reference(relative, 'defaultHero', record.defaultHero, 'hero');
      reference(relative, 'defaultEstate', record.defaultEstate, 'estate');
      const world = record.world;
      if (!isRecord(world)) continue;
      if (isRecord(world.progression)) reference(relative, 'world.progression.startingWeapon', world.progression.startingWeapon, 'weapon');
      if (Array.isArray(world.seasons) && world.seasons.every(isRecord)) {
        if (world.seasons.reduce((sum, season) => sum + season.durationTicks, 0) !== record.durationTicks) {
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
    const canonical = records.filter(({relative,kind}) => !relative.startsWith(`test${path.sep}`) && !['tuning','profile'].includes(kind));
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
    for (const entry of records.filter(e=>!['profile','tuning'].includes(e.kind))) {
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
    const profiles=records.filter(e=>e.kind==='profile');
    expect(profiles.some(entry=>entry.relative===path.join('profiles','s2-baseline.json')), 'Explicit s2-baseline runtime profile required');
    const allSelected = new Set();
    for(const {record:r,relative} of profiles) {
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
          expect(target?.record.designStatus==='s2-runtime',`${relative}: candidate cannot enter runtime profile ${id}`);
          expect(Boolean(target?.relative.startsWith(`test${path.sep}`))===(group==='testSelection'),`${relative}: profile test selection boundary mismatch ${id}`);
        }
      }
    }
    for(const entry of records.filter(e=>!['profile','tuning'].includes(e.kind))) expect((entry.record.designStatus==='s2-runtime')===allSelected.has(entry.record.id),`${entry.relative}: runtime status must match explicit profile selection union`);
  }
  if (schemas.size === 0) errors.push('No valid schemas found');
  if (records.length === 0) errors.push('No content records found');
  return { valid: errors.length === 0, records: records.length, schemas: schemas.size, errors, ...(fullPool ? {gateCounts} : {}) };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = await validateContent(process.argv[2] ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../data'), { fullPool: true });
  console.log(JSON.stringify(result, null, 2));
  process.exitCode = result.valid ? 0 : 1;
}
