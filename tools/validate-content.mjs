import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';

const namespaceId = /^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$/;
const directoryKinds = new Map([
  ['tools', 'tool'], ['heroes', 'hero'], ['estates', 'estate'],
  ['weapons', 'weapon'], ['charters', 'charter'], ['items', 'item'],
  ['retainers', 'retainer'], ['enemies', 'enemy'], ['evolutions', 'evolution'],
  ['skins', 'skin'],
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

export async function validateContent(dataDirectory) {
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
  if (schemas.size === 0) errors.push('No valid schemas found');
  if (records.length === 0) errors.push('No content records found');
  return { valid: errors.length === 0, records: records.length, schemas: schemas.size, errors };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = await validateContent(process.argv[2] ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../data'));
  console.log(JSON.stringify(result, null, 2));
  process.exitCode = result.valid ? 0 : 1;
}
