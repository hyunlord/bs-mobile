export const weaponGrowthFilename = /^weapon-growth-[a-zA-Z0-9_-]+\.json$/;

// JSON syntax is checked by JSON.parse before this scanner checks repeated keys.
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

export function validateWeaponGrowth(records) {
  const errors = [], files = new Map(records.filter(e => e.kind === 'weapon-growth').map(e => [e.relative, e]));
  for (const { relative, record, schemaValid } of files.values()) {
    if (!schemaValid) continue;
    for (const [id, weapon] of Object.entries(record.weapons)) {
      const label = `${relative}: ${id}`;
      if (weapon.attackModel === 'rays' ? weapon.beamHalfWidth < 1 : weapon.beamHalfWidth !== 0) errors.push(`${label}: invalid beam width for attack model`);
      for (const [index, row] of weapon.levels.entries()) {
        if (row.level !== index + 1) errors.push(`${label}: levels must be contiguous 1..12`);
        if (weapon.attackModel !== 'rays' && row.pierce !== 0) errors.push(`${label}: only rays allow pierce`);
        if (!index) continue;
        const previous = weapon.levels[index - 1];
        if (['damage', 'range', 'count', 'pierce', 'knockback'].some(key => row[key] < previous[key]) || row.cooldownTicks > previous.cooldownTicks) errors.push(`${label}: growth must be monotonic`);
        if (['range', 'count', 'pierce', 'knockback', 'cooldownTicks'].every(key => row[key] === previous[key])) errors.push(`${label}: damage-only or unchanged growth row`);
      }
    }
  }
  for (const { relative, kind, record, schemaValid } of records) {
    if (kind !== 'profile' || !schemaValid || !record.weaponCombat) continue;
    const entry = files.get(record.weaponCombat.definitionsFile);
    if (!entry?.schemaValid) { errors.push(`${relative}: missing or invalid weapon growth definitions`); continue; }
    const actual = Object.keys(entry.record.weapons).sort();
    const selected = [...new Set([...record.selection.weapons, ...(record.testSelection?.weapons ?? [])])].sort();
    if (JSON.stringify(actual) !== JSON.stringify(selected)) errors.push(`${relative}: weapon growth IDs must match selected weapons exactly`);
  }
  return errors;
}
