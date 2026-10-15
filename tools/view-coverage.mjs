import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Core content IDs (weapon, tool, evolution, enemy, hero) and View code paths must resolve art in every profile that can reach them.
// Requirements are derived from data and View source; the contract only scopes code paths and records reviewed gaps.
const readJson = (root, file) => JSON.parse(fs.readFileSync(path.join(root, file), 'utf8'));
const check = (value, message) => { if (!value) throw new Error(message); };
const unique = values => [...new Set(values)];
export const contractFile = 'tools/view-coverage.json';
export const fixtureFile = 'tools/fixtures/view-coverage.expected.json';
export const gapKey = gap => `${gap.profile}|${gap.type}|${gap.key}`;

export function runtimeSources(root, dirs) {
  const result = [];
  const walk = dir => {
    for (const entry of fs.readdirSync(path.join(root, dir), { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const file = path.posix.join(dir, entry.name);
      if (entry.isDirectory()) { if (entry.name !== 'Tests' && entry.name !== 'Editor') walk(file); }
      else if (entry.name.endsWith('.cs')) result.push({ file, text: fs.readFileSync(path.join(root, file), 'utf8') });
    }
  };
  for (const dir of dirs) walk(dir);
  return result;
}

function evidence(root, items, label, errors) {
  for (const item of items ?? []) {
    const file = path.join(root, item.file);
    if (!fs.existsSync(file) || !fs.readFileSync(file, 'utf8').includes(item.text)) errors.push(`${label}: cited code no longer present in ${item.file}: ${item.text}`);
  }
}

function sourceLine(root, cite, label) {
  const line = fs.readFileSync(path.join(root, cite.file), 'utf8').split('\n').find(value => value.includes(cite.text));
  check(line, `${label}: cited code no longer present in ${cite.file}: ${cite.text}`);
  return line;
}

// The View's wave attack form ternary, e.g. gear.Kind==WaveAttackKind.Arc?"sector90":...:"wave".
export function parseWaveForms(line) {
  const forms = Object.fromEntries([...line.matchAll(/gear\.Kind==WaveAttackKind\.(\w+)\?"(\w+)"/g)].map(m => [m[1], m[2]]));
  const fallback = line.match(/:"(\w+)";\s*$/)?.[1];
  check(Object.keys(forms).length && fallback, 'wave attack form ternary could not be parsed');
  return { forms, fallback };
}

export function parseEvolutionBase(line) {
  const m = line.match(/evolution\.Kind==WaveEvolutionKind\.(\w+)\?WaveAttackKind\.(\w+):WaveAttackKind\.(\w+)/);
  check(m, 'wave evolution attack-source rule could not be parsed');
  return { kind: m[1], then: m[2], otherwise: m[3] };
}

export function parseEnemyRig(text) {
  const names = text.match(/Names\s*=\s*\{([^}]*)\}/)?.[1].match(/"([^"]+)"/g)?.map(value => value.slice(1, -1));
  const prefix = text.match(/var prefix\s*=\s*"([^"]+)"\s*\+\s*Names\[i\]\s*\+\s*"([^"]*)"/);
  const poses = [...text.matchAll(/ResolveRole\(prefix\s*\+\s*"([^"]+)"\)/g)].map(m => m[1]);
  const index = Object.fromEntries([...text.matchAll(/WaveEnemyKind\.(\w+)\s*=>\s*(\d+)/g)].map(m => [m[1], Number(m[2])]));
  check(names?.length && prefix && poses.length && Object.keys(index).length, 'EnemyRigAtlas names, prefix, poses or kind index could not be parsed');
  return { names, prefix: prefix[1], separator: prefix[2], poses, index };
}

// Literal role and binding requests in runtime View/App code.
export function literalRequests(sources) {
  const result = [];
  for (const { file, text } of sources) {
    for (const m of text.matchAll(/ResolveRole\("([^"]+)"\)/g)) result.push({ file, type: 'role', key: m[1] });
    for (const m of text.matchAll(/\bResolve\(\s*"([^"|]+)"\s*,\s*"([^"|]*)"\s*,\s*"([^"|]+)"\s*\)/g)) result.push({ file, type: 'binding', key: `${m[1]}|${m[2]}|${m[3]}` });
  }
  return result;
}

export function coreIds(root, profileFile, tuningFile) {
  const profile = readJson(root, profileFile);
  const ids = { weapon: [...profile.selection.weapons], tool: [...profile.selection.tools], evolution: [], enemy: [...profile.selection.enemies], hero: [...profile.selection.heroes] };
  if (profile.waveRuntimeFile) {
    const wave = readJson(root, path.posix.join('data', profile.waveRuntimeFile));
    for (const binding of wave.bindings) if (ids[binding.kind]) ids[binding.kind].push(binding.id);
    ids.evolution.push(...Object.keys(wave.definition.evolutions));
    ids.enemy.push(...Object.keys(wave.definition.enemies), wave.definition.bossId);
    ids.hero.push(wave.tuning.defaultHero);
  } else {
    ids.evolution.push(...profile.runtime.evolutions);
    if (tuningFile) ids.hero.push(readJson(root, tuningFile).defaultHero);
  }
  return Object.fromEntries(Object.entries(ids).map(([kind, values]) => [kind, unique(values).sort()]));
}

function profileRequirements(root, contract, name, config, sources, errors) {
  const required = [];
  const need = (type, key, source) => required.push({ type, key, source });
  const ids = coreIds(root, config.profile, config.tuning);
  const profile = readJson(root, config.profile);
  for (const [kind, values] of Object.entries(ids)) for (const id of values) {
    need('binding', `icon|${id}|default`, `${kind} ${id}`);
    for (const state of contract.actorStates[kind] ?? []) need('binding', `${kind}|${id}|${state}`, `${kind} ${id}`);
  }
  if (profile.waveRuntimeFile) {
    const definition = readJson(root, path.posix.join('data', profile.waveRuntimeFile)).definition;
    const { forms, fallback } = parseWaveForms(sourceLine(root, contract.waveAttack.formSource, 'wave attack form'));
    const base = parseEvolutionBase(sourceLine(root, contract.waveAttack.evolutionBaseSource, 'wave evolution base'));
    const special = contract.waveAttack.specialForms;
    for (const [kind, rule] of Object.entries(special)) evidence(root, rule.evidence, `wave ${kind} form`, errors);
    const formOf = kind => Object.hasOwn(special, kind) ? special[kind].form : forms[kind] ?? fallback;
    for (const kind of ['weapon', 'tool']) for (const id of ids[kind]) {
      const gear = definition.gear[id];
      if (!gear) { errors.push(`${name}: ${kind} ${id} has no wave gear definition`); continue; }
      const form = formOf(gear.kind);
      if (form) need('binding', `attack|${id}|${form}`, `${kind} ${id} (${gear.kind})`);
    }
    for (const id of ids.evolution) {
      const evolution = definition.evolutions[id];
      const wanted = evolution.kind === base.kind ? base.then : base.otherwise;
      const input = evolution.inputIds.find(value => definition.gear[value]?.kind === wanted);
      if (input) need('binding', `attack|${id}|${formOf(wanted)}`, `evolution ${id} (${evolution.kind} via ${input})`);
      else if (!evolution.inputIds.some(value => Object.hasOwn(special, definition.gear[value]?.kind) && special[definition.gear[value].kind].form === null))
        errors.push(`${name}: evolution ${id} (${evolution.kind}) has no View attack path`);
    }
  } else {
    evidence(root, Object.values(contract.firstPlayableAttack), 'first-playable attack form', errors);
    for (const id of ids.weapon) {
      const form = profile.firstPlayable?.weapons?.[id]?.form;
      if (form) need('binding', `attack|${id}|${form}`, `weapon ${id}`);
      else errors.push(`${name}: weapon ${id} declares no attack form`);
    }
    for (const id of ids.tool) {
      const shape = readJson(root, `data/tools/${id.split(':')[1]}.json`).activation?.shape;
      if (shape) need('binding', `attack|${id}|${shape}`, `tool ${id}`);
      else errors.push(`${name}: tool ${id} declares no activation shape`);
    }
    for (const id of ids.evolution) {
      const evolution = readJson(root, `data/evolutions/${id.split(':')[1]}.json`);
      for (const effect of evolution.runtimeProjection?.effects ?? [])
        if (effect.operation === 'damage-pulse') need('binding', `attack|${id}|${effect.subject === 'weapon-front' ? 'sector180' : 'disk'}`, `evolution ${id} damage-pulse`);
    }
  }
  // Per-ID View tables (e.g. a Resources JSON keyed by gear designRef or evolution id) must list every ID of their kinds.
  for (const table of contract.presentationTables ?? []) {
    if (!table.profiles.includes(name)) continue;
    const file = path.join(root, table.file);
    if (!fs.existsSync(file)) { errors.push(`presentation table missing: ${table.file}`); continue; }
    const content = JSON.parse(fs.readFileSync(file, 'utf8'));
    const wave = profile.waveRuntimeFile ? readJson(root, path.posix.join('data', profile.waveRuntimeFile)).definition : null;
    for (const section of table.sections) for (const kind of section.kinds) for (const id of ids[kind]) {
      const value = section.key === 'designRef' ? (kind === 'evolution' ? wave?.evolutions[id] : wave?.gear[id])?.designRef : id;
      if (!value) { errors.push(`${name}: ${kind} ${id} has no ${section.key} for ${table.file}`); continue; }
      required.push({ type: 'table', key: `${table.file}#${section.path}:${value}`, source: `${kind} ${id}`, present: Object.hasOwn(content[section.path] ?? {}, value) });
    }
  }
  const scopes = new Map(contract.scopes.map(scope => [scope.file, scope]));
  for (const request of literalRequests(sources)) {
    const scope = scopes.get(request.file);
    if (!scope || scope.profiles.includes(name)) need(request.type, request.key, `literal ${request.file}`);
  }
  if (contract.enemyRig.profiles.includes(name) && definitionEnemies(root, profile)) {
    const rig = parseEnemyRig(sources.find(source => source.file === contract.enemyRig.file)?.text ?? '');
    for (const [id, enemy] of Object.entries(definitionEnemies(root, profile))) {
      const index = rig.index[enemy.kind];
      if (index === undefined) continue;
      for (const pose of rig.poses) need('role', `${rig.prefix}${rig.names[index]}${rig.separator}${pose}`, `enemy rig ${id} (${enemy.kind})`);
    }
  }
  return { ids, required };
}

const definitionEnemies = (root, profile) => profile.waveRuntimeFile ? readJson(root, path.posix.join('data', profile.waveRuntimeFile)).definition.enemies : null;

export function analyze(root, contract = readJson(root, contractFile)) {
  check(contract.schemaVersion === 1, 'view coverage contract schemaVersion must be 1');
  const errors = [];
  const sources = runtimeSources(root, contract.viewSources);
  const files = new Set(sources.map(source => source.file));
  for (const scope of contract.scopes) {
    if (!files.has(scope.file)) errors.push(`scope file is not a runtime View source: ${scope.file}`);
    for (const profile of scope.profiles) if (!contract.profiles[profile]) errors.push(`scope ${scope.file}: unknown profile ${profile}`);
    evidence(root, scope.evidence, `scope ${scope.file}`, errors);
    for (const pin of scope.pinned ?? []) {
      const pattern = new RegExp(pin.pattern, 'g');
      const count = runtimeSources(root, [pin.dir]).reduce((sum, source) => sum + (source.text.match(pattern)?.length ?? 0), 0);
      if (count !== pin.count) errors.push(`scope ${scope.file}: ${pin.pattern} occurs ${count} times in ${pin.dir}, contract pins ${pin.count}; review the profile scope (${pin.reason})`);
    }
  }
  evidence(root, contract.enemyRig.evidence, 'enemy rig', errors);
  const profiles = {};
  const gaps = [];
  for (const [name, config] of Object.entries(contract.profiles)) {
    const manifest = readJson(root, config.manifest);
    const roles = new Set(manifest.roles.map(role => role.id));
    const bindings = new Set(manifest.bindings.map(b => `${b.kind}|${b.contentId}|${b.state}`));
    const { ids, required } = profileRequirements(root, contract, name, config, sources, errors);
    const merged = new Map();
    for (const item of required) {
      const id = `${item.type}|${item.key}`;
      if (!merged.has(id)) merged.set(id, { type: item.type, key: item.key, sources: [], ...(item.present === undefined ? {} : { present: item.present }) });
      const sourcesForKey = merged.get(id).sources;
      if (!sourcesForKey.includes(item.source)) sourcesForKey.push(item.source);
    }
    const requirements = [...merged.values()].sort((a, b) => `${a.type}|${a.key}`.localeCompare(`${b.type}|${b.key}`));
    for (const item of requirements) {
      if (item.type !== 'table') item.present = item.type === 'role' ? roles.has(item.key) : bindings.has(item.key);
      if (!item.present) gaps.push({ profile: name, type: item.type, key: item.key, sources: item.sources });
    }
    profiles[name] = { manifest: config.manifest, ids, requirements };
  }
  return { profiles, gaps, errors };
}

export function verify(result, knownGaps) {
  const problems = [...result.errors];
  const known = new Map(knownGaps.map(gap => [gapKey(gap), gap]));
  for (const gap of knownGaps) if (!gap.issue || !/^#\d+$/.test(gap.issue)) problems.push(`known gap ${gapKey(gap)} needs an issue reference like #123`);
  const actual = new Set(result.gaps.map(gapKey));
  for (const gap of result.gaps) if (!known.has(gapKey(gap))) problems.push(`missing ${gap.type} ${gap.key} in ${gap.profile} (required by ${gap.sources.join('; ')})`);
  for (const key of known.keys()) if (!actual.has(key)) problems.push(`known gap is resolved or no longer required; remove it from ${contractFile}: ${key}`);
  return problems;
}

// The EditMode test resolves this exact list through the real ArtCatalog; keep the committed copy current.
export function fixture(result, knownGaps) {
  const known = new Set(knownGaps.map(gapKey));
  return { schemaVersion: 1, generatedBy: 'node tools/view-coverage.mjs --write', profiles: Object.entries(result.profiles).map(([name, value]) => ({
    name,
    roles: value.requirements.filter(item => item.type === 'role' && item.present).map(item => item.key),
    bindings: value.requirements.filter(item => item.type === 'binding' && item.present).map(item => item.key),
    knownGapRoles: value.requirements.filter(item => item.type === 'role' && known.has(`${name}|role|${item.key}`)).map(item => item.key),
    knownGapBindings: value.requirements.filter(item => item.type === 'binding' && known.has(`${name}|binding|${item.key}`)).map(item => item.key),
  })) };
}

export function summary(result) {
  return Object.fromEntries(Object.entries(result.profiles).map(([name, value]) => [name, {
    ids: Object.fromEntries(Object.entries(value.ids).map(([kind, ids]) => [kind, ids.length])),
    requirements: value.requirements.length,
    missing: value.requirements.filter(item => !item.present).length,
  }]));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const root = path.resolve(args.find(value => !value.startsWith('--')) ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
  const contract = readJson(root, contractFile);
  const result = analyze(root, contract);
  if (args.includes('--report')) {
    console.log(JSON.stringify({ summary: summary(result), gaps: result.gaps, errors: result.errors }, null, 2));
    process.exit(0);
  }
  const expected = JSON.stringify(fixture(result, contract.knownGaps), null, 2) + '\n';
  if (args.includes('--write')) fs.writeFileSync(path.join(root, fixtureFile), expected);
  const problems = verify(result, contract.knownGaps);
  const fixturePath = path.join(root, fixtureFile);
  if (!fs.existsSync(fixturePath) || fs.readFileSync(fixturePath, 'utf8') !== expected) problems.push(`${fixtureFile} is stale; run node tools/view-coverage.mjs --write`);
  if (problems.length) { for (const problem of problems) console.error('VIEW_COVERAGE_FAIL ' + problem); process.exit(1); }
  console.log('VIEW_COVERAGE_PASS ' + JSON.stringify(summary(result)));
}
