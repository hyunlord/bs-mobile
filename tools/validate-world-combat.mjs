// Authoring checks after the opt-in JSON schema succeeds. Core validates issued
// snapshots again; passing this fixture does not approve ownership or provenance.
export function validateWorldCombat(document) {
  const errors = [];
  const s = document.snapshot;
  const abilities = ['Attack', 'Health', 'Movement', 'Growth', 'Allies', 'Experience'];
  const sourceKinds = ['hero', 'vassal', 'research'];
  const maximumEntries = 4096;
  const maximumInt = 2147483647;
  const maximumLong = 9223372036854775807n;
  function ordered(entries, label) {
    for (let i = 1; i < entries.length; i++) {
      if (entries[i - 1].id >= entries[i].id) errors.push(`${label}: IDs must be unique and in ordinal order`);
    }
  }
  function position(entry, label) {
    const b = s.battlefield;
    if (entry.x < b.minX || entry.x > b.maxX || entry.y < b.minY || entry.y > b.maxY) errors.push(`${label}: position outside battlefield`);
  }
  const b = s.battlefield;
  if (b.minX >= b.maxX || b.minY >= b.maxY) errors.push('battlefield: minimum must precede maximum');
  const width = BigInt(b.maxX) - BigInt(b.minX), height = BigInt(b.maxY) - BigInt(b.minY);
  if (width * width + height * height > maximumLong) errors.push('battlefield: squared diagonal overflows signed 64-bit');
  const rulesId = s.mode === 'direct-pve' ? 'world-combat-v1' : 'army-auto-v1';
  if (s.provenance.rulesId !== rulesId) errors.push('provenance: rules ID does not match combat mode');
  for (const [label, entries] of [['objectives', s.objectives], ['troops', s.troops], ['cohorts', s.cohorts], ['markers', s.markers], ['hero.skills', s.hero.skills]]) ordered(entries, label);
  const limits = s.objectives.filter(o => o.kind === 'time-limit');
  if (limits.length !== 1) errors.push('objectives: exactly one time-limit required');
  const limit = limits.length === 1 ? limits[0].limitTicks : 0;
  if (s.mode === 'direct-pve' && !s.objectives.some(o => o.kind !== 'time-limit')) errors.push('objectives: direct PvE requires a non-time objective');
  const cohorts = new Map(s.cohorts.map(c => [c.id, c]));
  const markers = new Map(s.markers.map(m => [m.id, m]));
  for (const o of s.objectives) {
    if (o.kind === 'time-limit') {
      if (o.targetId !== '' || o.requiredCount !== 0 || o.holdTicks !== 0 || o.limitTicks <= 0) errors.push(`${o.id}: invalid time-limit payload`);
    } else if (o.kind === 'defeat-designated') {
      const cohort = cohorts.get(o.targetId);
      if (!cohort || o.requiredCount <= 0 || o.requiredCount > cohort.count || o.holdTicks !== 0 || o.limitTicks !== 0) errors.push(`${o.id}: invalid or unachievable designated cohort objective`);
    } else if (!markers.has(o.targetId) || o.holdTicks <= 0 || o.holdTicks > limit || o.requiredCount !== 0 || o.limitTicks !== 0) {
      errors.push(`${o.id}: invalid or unachievable hold-marker objective`);
    }
  }
  let troops = 0;
  for (const troop of s.troops) {
    troops += troop.n;
    if (troop.n !== troop.slots.length) errors.push(`${troop.id}: N must exactly match stable slots`);
    for (const [index, slot] of troop.slots.entries()) {
      if (slot.ordinal !== index) errors.push(`${troop.id}: slot ordinals must be contiguous from zero`);
      position(slot, `${troop.id}/${slot.ordinal}`);
    }
  }
  if (troops > maximumEntries) errors.push('troops: total N exceeds protocol slot limit');
  let enemies = 0;
  for (const cohort of s.cohorts) {
    enemies += cohort.count;
    if (cohort.spawnTick >= limit) errors.push(`${cohort.id}: spawn tick must precede time limit`);
    position(cohort, cohort.id);
  }
  if (enemies > maximumEntries) errors.push('cohorts: total spawn count exceeds protocol slot limit');
  for (const marker of s.markers) position(marker, marker.id);
  for (const [index, effect] of s.effects.entries()) {
    if (effect.ability !== abilities[index]) errors.push('effects: one entry per ability in protocol order required');
    let raw = 0;
    for (const [sourceIndex, source] of effect.sources.entries()) {
      raw += source.raw;
      if (source.kind === 'hero' && !(source.sourceId === s.hero.id && source.contentHash === s.hero.contentHash) &&
        !s.hero.skills.some(skill => skill.id === source.sourceId && skill.contentHash === source.contentHash)) errors.push(`${effect.ability}: unknown hero effect provenance`);
      const previous = effect.sources[sourceIndex - 1];
      if (previous && (sourceKinds.indexOf(previous.kind) > sourceKinds.indexOf(source.kind) || previous.kind === source.kind && previous.sourceId >= source.sourceId)) errors.push(`${effect.ability}: sources must be unique and ordered by kind then ordinal ID`);
    }
    if (raw > maximumInt || effect.raw !== raw) errors.push(`${effect.ability}: raw must equal checked sum of sources`);
    if (effect.bonus !== Math.min(raw, effect.cap)) errors.push(`${effect.ability}: bonus must apply common cap exactly once`);
  }
  return errors;
}
