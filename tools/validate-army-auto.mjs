// Called only after strict schema validation. Numeric operations here use BigInt admission bounds.
export function validateArmyAuto(document) {
  const errors = [];
  const unique = (values, label) => { if (new Set(values).size !== values.length) errors.push(`duplicate ${label}`); };
  unique(document.fixtures.map(f => f.name), 'fixture');
  for (const fixture of document.fixtures) {
    const { rules, attacker, defender, battlefieldDefense, objectiveDefense } = fixture.input;
    const fail = message => errors.push(`${fixture.name}: ${message}`);
    unique(rules.policies.map(p => p.mode), 'policy');
    if (rules.policies.length !== 2) fail('both policies required');
    unique(rules.matchups.map(m => `${m.attackerId}/${m.defenderId}`), 'matchup');
    const max = 2147483647n;
    if (rules.maxSlotsPerSide > 4096 || rules.maxStat + rules.healthBonusCap > Number(max) ||
        rules.maxStat + rules.attackBonusCap > Number(max) ||
        rules.maxStat + rules.defenseBonusCap + battlefieldDefense + objectiveDefense > Number(max)) fail('stat or slot bound');
    let total = 0;
    for (const side of [attacker, defender]) {
      unique(side.troops.map(t => t.id), 'troop');
      unique(side.effects.map(e => `${e.kind}/${e.id}`), 'effect');
      let count = 0;
      for (const troop of side.troops) {
        count += troop.ordinals.length;
        if ([troop.health, troop.attack, troop.defense].some(v => v > rules.maxStat)) fail('troop stat exceeds rule');
      }
      if (count > rules.maxSlotsPerSide) fail('slots exceed rule');
      total += count + side.troops.length;
      if (!side.heroAvailable && side.effects.some(e => e.kind === 'hero' && (e.attack || e.defense || e.health))) fail('unavailable hero contributes');
    }
    if (BigInt(rules.maxRounds) * BigInt(Math.max(1, total)) ** 2n > 200000000n) fail('work budget exceeded');
    const pairs = new Set(rules.matchups.map(m => `${m.attackerId}/${m.defenderId}`));
    for (const a of attacker.troops) for (const d of defender.troops) {
      if (!pairs.has(`${a.id}/${d.id}`) || !pairs.has(`${d.id}/${a.id}`)) fail('missing directional matchup');
    }
    for (const matchup of rules.matchups) {
      const scaled = BigInt(rules.maxStat + rules.attackBonusCap) * BigInt(matchup.multiplier) / 1000n;
      if ((scaled > BigInt(rules.minDamage) ? scaled : BigInt(rules.minDamage)) * BigInt(rules.maxSlotsPerSide) > 9223372036854775807n) fail('damage overflow');
    }
  }
  return errors;
}
