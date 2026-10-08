export const weaponGrowthFilename = /^(?:experiments\/)?weapon-growth-[a-zA-Z0-9_-]+\.json$/;

export function weaponGrowthDuplicateKeys(text) {
  const tokens = text.match(/"(?:\\.|[^"\\])*"|[{}\[\]:,]|[^\s{}\[\]:,]+/g) ?? [];
  const stack = [], duplicates = [];
  for (let index = 0; index < tokens.length; index++) {
    const token = tokens[index];
    if (token === '{') stack.push(new Set());
    else if (token === '[') stack.push(null);
    else if (token === '}' || token === ']') stack.pop();
    else if (token.startsWith('"') && tokens[index + 1] === ':' && stack.at(-1)) {
      const key = JSON.parse(token), keys = stack.at(-1);
      if (keys.has(key)) duplicates.push(key);
      keys.add(key);
    }
  }
  return duplicates;
}

function growthErrors(weapon, label) {
  const errors = [];
  if (weapon.attackModel === 'rays' ? weapon.beamHalfWidth < 1 : weapon.beamHalfWidth !== 0) errors.push(`${label}: invalid beam width for attack model`);
  for (const [index, row] of weapon.levels.entries()) {
    if (row.level !== index + 1) errors.push(`${label}: levels must be contiguous 1..12`);
    if (weapon.attackModel !== 'rays' && row.pierce !== 0) errors.push(`${label}: only rays allow pierce`);
    if (!index) continue;
    const previous = weapon.levels[index - 1];
    if (['damage', 'range', 'count', 'pierce', 'knockback'].some(key => row[key] < previous[key]) || row.cooldownTicks > previous.cooldownTicks) errors.push(`${label}: growth must be monotonic`);
    if (['range', 'count', 'pierce', 'knockback', 'cooldownTicks'].every(key => row[key] === previous[key])) errors.push(`${label}: damage-only or unchanged growth row`);
  }
  return errors;
}

export function validateWeaponGrowth(records) {
  const errors = [], files = new Map(records.filter(e => e.kind === 'weapon-growth').map(e => [e.relative, e]));
  const weapons = new Map(records.filter(e => e.kind === 'weapon').map(e => [e.record.id, e]));
  for (const { relative, record, schemaValid } of weapons.values()) {
    if (schemaValid && record.growth) errors.push(...growthErrors(record.growth, relative));
  }
  for (const { relative, record, schemaValid } of files.values()) {
    if (!schemaValid || record.contractVersion !== 1) continue;
    for (const [id, weapon] of Object.entries(record.weapons)) errors.push(...growthErrors(weapon, `${relative}: ${id}`));
  }
  for (const { relative, kind, record, schemaValid } of records) {
    if (kind !== 'profile' || !schemaValid || !record.weaponCombat) continue;
    const extension = record.weaponCombat;
    const selected = [...new Set([...record.selection.weapons, ...(record.testSelection?.weapons ?? [])])].sort();
    let actual = selected;
    if (extension.definitionsFile !== undefined) {
      const entry = files.get(extension.definitionsFile);
      if (!entry?.schemaValid) { errors.push(`${relative}: missing or invalid weapon growth definitions`); continue; }
      if (entry.record.contractVersion !== extension.contractVersion) errors.push(`${relative}: weapon growth contract versions differ`);
      actual = (entry.record.contractVersion === 1 ? Object.keys(entry.record.weapons) : [...entry.record.weapons]).sort();
    }
    if (JSON.stringify(actual) !== JSON.stringify(selected)) errors.push(`${relative}: weapon growth IDs must match selected weapons exactly`);
    for (const id of selected) {
      const weapon = weapons.get(id);
      if (extension.contractVersion === 2 && (!weapon?.schemaValid || !weapon.record.growth)) errors.push(`${relative}: canonical growth missing for ${id}`);
      if (extension.contractVersion === 1 && weapon?.record.growth) errors.push(`${relative}: duplicate numeric source for ${id}`);
    }
  }
  return errors;
}
