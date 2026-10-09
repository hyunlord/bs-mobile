// Design proposals are a separate namespace of records, never runtime selections.
export function validateSystemDesign(design, currentRecords, researchIds) {
  const errors = [];
  const check = (condition, message) => { if (!condition) errors.push(`system-design-v1: ${message}`); };
  const content = design.content;
  const byId = new Map(content.map(record => [record.id, record]));
  const ofKind = kind => content.filter(record => record.kind === kind);
  const unique = (values, message) => check(new Set(values).size === values.length, message);
  const quota = (records, min, max, label) => check(records.length >= min && records.length <= max, `${label} count must be ${min}..${max}`);
  const reference = (owner, id, kinds) => check(byId.has(id) && kinds.includes(byId.get(id).kind), `${owner}: unresolved proposed ${kinds.join('/')} reference ${id}`);
  const current = new Map();
  const addSource = (record, kind) => {
    if (!record.id) return;
    const entries = current.get(record.id) ?? [];
    entries.push({ ...record, kind }); current.set(record.id, entries);
  };
  for (const entry of currentRecords) {
    if (entry.relative?.startsWith('test/')) continue;
    if (entry.kind === 'meta') {
      for (const [key, kind] of Object.entries({ materials: 'material', chapters: 'chapter', manorBuildings: 'manor', vassals: 'vassal' })) {
        for (const record of entry.record[key] ?? []) addSource(record, kind);
      }
    } else if (entry.kind !== 'system-design-v1') addSource(entry.record, entry.kind);
  }
  unique(content.map(record => record.id), 'duplicate proposed ID');
  for (const record of content) {
    const namespace = ['chapter', 'material', 'manor'].includes(record.kind) ? 'meta:' : 'core:';
    check(record.id.startsWith(namespace), `${record.id}: kind requires ${namespace} namespace`);
    const existing = current.get(record.id) ?? [];
    if (record.origin === 'new') check(existing.length === 0, `${record.id}: existing ID cannot have new origin`);
    else {
      check(record.sourceId === record.id, `${record.id}: sourceId must preserve canonical ID`);
      check(existing.some(source => source.kind === record.kind && source.name === record.name), `${record.id}: revised source kind/name must match canonical record`);
    }
    for (const ref of record.researchRefs) check(researchIds.has(ref), `${record.id}: unknown research reference ${ref}`);
    for (const id of record.linkedToolIds ?? []) reference(record.id, id, ['tool']);
    for (const id of record.linkedWeaponIds ?? []) reference(record.id, id, ['weapon']);
    if (record.kind === 'vassal') check(existing.some(source => source.kind === 'vassal') && record.origin === 'revised', `${record.id}: vassal must come from original roster`);
    if (record.kind === 'chapter') {
      reference(record.id, record.bossId, ['enemy']);
      check(byId.get(record.bossId)?.tier === 'boss', `${record.id}: chapter requires boss tier`);
    }
    if (record.kind === 'manor') for (const id of record.inputIds) reference(record.id, id, ['material']);
    if (record.kind === 'material') {
      for (const direction of ['sources', 'sinks']) unique(record[direction].map(link => link.contentId), `${record.id}: duplicate material ${direction} contentId`);
      for (const source of record.sources) reference(record.id, source.contentId, ['tool', 'enemy', 'chapter', 'manor']);
      for (const sink of record.sinks) reference(record.id, sink.contentId, ['tool', 'manor', 'vassal']);
    }
  }
  const weapons = ofKind('weapon'); quota(weapons, 16, 20, 'weapon');
  check(new Set(weapons.map(record => record.form)).size >= 10, 'weapons require at least 10 forms');
  for (const form of new Set(weapons.map(record => record.form))) {
    const group = weapons.filter(record => record.form === form);
    check(group.length <= 2, `weapon form ${form} exceeds two`);
    unique(group.map(record => record.uniqueMechanic.trim()), `weapon form ${form} requires distinct unique mechanics`);
  }
  const roles = ['crowd', 'single', 'control', 'survival', 'estate'].map(role => weapons.filter(record => record.primaryRole === role).length);
  check(Math.min(...roles) >= 3 && Math.max(...roles) - Math.min(...roles) <= 1, 'weapon primary roles must be balanced');
  const tools = ofKind('tool'); quota(tools, 14, 16, 'tool');
  for (const kind of ['land', 'building', 'people']) {
    const group = tools.filter(record => record.targetKind === kind); quota(group, 4, 5, `${kind} tool`);
    unique(group.map(record => record.remnant.key), `${kind} duplicate remnant key`);
    unique(group.map(record => record.growth.mechanism.trim()), `${kind} duplicate growth mechanism`);
  }
  const items = ofKind('item'); quota(items, 30, 40, 'item');
  check(items.filter(record => record.behaviorClass === 'numeric-only').length * 5 <= items.length, 'numeric-only items exceed 20 percent');
  quota(ofKind('charter'), 12, 12, 'charter');
  const evolutions = ofKind('evolution'); quota(evolutions, 16, 20, 'evolution');
  for (const record of evolutions) for (const id of record.inputIds) reference(record.id, id, ['weapon', 'tool']);
  unique(evolutions.map(record => [...record.inputIds].sort().join('|')), 'duplicate unordered evolution recipe');
  check(evolutions.filter(record => record.inputIds.map(id => byId.get(id)?.kind).sort().join('|') === 'tool|weapon').length * 2 >= evolutions.length, 'at least half of evolution recipes must be weapon-tool');
  const enemies = ofKind('enemy');
  for (const [tier, min, max] of [['normal', 12, 14], ['elite', 4, 4], ['boss', 4, 5]]) quota(enemies.filter(record => record.tier === tier), min, max, `${tier} enemy`);
  for (const boss of enemies.filter(record => record.tier === 'boss')) unique(boss.phasePatterns.map(phase => phase.action.trim()), `${boss.id}: duplicate boss phase action`);
  const chapters = ofKind('chapter').sort((a, b) => a.order - b.order); quota(chapters, 10, 10, 'chapter');
  unique(chapters.map(record => record.order), 'duplicate chapter order');
  unique(chapters.map(record => record.device.key), 'duplicate chapter device');
  unique(chapters.map(record => record.device.rule.trim()), 'duplicate chapter device rule');
  const groups = [];
  for (const chapter of chapters) {
    if (groups.at(-1)?.id === chapter.bossId) groups.at(-1).count++;
    else groups.push({ id: chapter.bossId, count: 1 });
  }
  check(groups.every(group => group.count >= 2 && group.count <= 3), 'boss must change every 2..3 chapters');
  unique(groups.map(group => group.id), 'chapter boss returns in nonconsecutive group');
  check(enemies.filter(record => record.tier === 'boss').every(boss => groups.some(group => group.id === boss.id)), 'every boss needs a chapter group');
  quota(ofKind('vassal'), 6, 8, 'vassal');
  const materials = ofKind('material'); quota(materials, 4, 4, 'material');
  for (const direction of ['sources', 'sinks']) unique(materials.map(record => record[direction].map(link => link.contentId).sort().join('|')), `material ${direction} must have distinct content signatures`);
  quota(ofKind('manor'), 6, 8, 'manor');
  const systems = design.systems.map(system => system.id); unique(systems, 'duplicate system ID');
  check(systems.every(id => !byId.has(id)), 'system IDs collide with proposed content');
  check(JSON.stringify(systems) === JSON.stringify(design.influenceMatrix.systemIds), 'matrix system IDs must exactly match system order');
  check(design.influenceMatrix.cells.length === systems.length && design.influenceMatrix.cells.every(row => row.length === systems.length), 'matrix must cover every ordered system pair');
  unique(design.visualLanguage.map(row => row.kind), 'duplicate visual language kind');
  check([...new Set(content.map(record => record.kind))].every(kind => design.visualLanguage.some(row => row.kind === kind)), 'visual language must cover every content kind');
  quota(design.implementationWaves, 3, 4, 'implementation wave');
  unique(design.implementationWaves.map(wave => wave.id), 'duplicate implementation wave ID');
  const assignments = design.implementationWaves.flatMap(wave => wave.contentIds);
  unique(assignments, 'content assigned to multiple implementation waves');
  check(assignments.length === content.length && assignments.every(id => byId.has(id)), 'waves must assign every proposed content ID exactly once');
  for (const wave of design.implementationWaves) for (const id of wave.systemIds) check(systems.includes(id), `${wave.id}: unknown system reference ${id}`);
  check(systems.every(id => design.implementationWaves.some(wave => wave.systemIds.includes(id))), 'waves must cover all systems');
  const waveById = new Map(design.implementationWaves.flatMap((wave, index) => wave.contentIds.map(id => [id, index])));
  for (const record of content) {
    const dependencies = [...(record.linkedToolIds ?? []), ...(record.linkedWeaponIds ?? []), ...(record.inputIds ?? [])];
    for (const id of dependencies) check(waveById.get(id) <= waveById.get(record.id), `${record.id}: dependency ${id} must be assigned to same or prior wave`);
  }
  return errors;
}
