// Called only after the opt-in draft JSON schema succeeds. These are authoring
// checks, not a runtime loader or a combat/balance implementation.
export function validateSlgContentDraft(draft) {
  const errors = [];
  const groups = [draft.troops, draft.heroes, draft.researchNodes, draft.worldEncounters];
  const entries = groups.flat();
  const ids = new Set();
  for (const entry of entries) {
    if (ids.has(entry.id)) errors.push(`duplicate draft ID ${entry.id}`);
    ids.add(entry.id);
  }
  const scopes = {
    'run.heroDamagePercent': ['controlled-hero'],
    'run.heroMaxHealthPercent': ['controlled-hero'],
    'run.heroMovementPercent': ['controlled-hero'],
    'run.growthPercent': ['controlled-hero-growth-actions'],
    'run.allyDamagePercent': ['standing-allies-only', 'standing-allies-in-radius'],
    'run.allyMaxHealthPercent': ['standing-allies-only', 'standing-allies-in-radius'],
    'run.experiencePercent': ['controlled-hero'],
    'training.speedPercent': ['new-training-jobs'],
    'construction.speedPercent': ['new-construction-jobs'],
    'march.speedPercent': ['new-round-trip-march-snapshot'],
    'estate.grainProductionPercent': ['estate-map-only'],
    'estate.timberProductionPercent': ['estate-map-only'],
    'estate.ironProductionPercent': ['estate-map-only'],
    'estate.storageSeconds': ['estate-production-storage'],
  };
  const effects = draft.researchNodes.map(node => [node.id, node.effect]);
  for (const hero of draft.heroes) {
    for (const ability of [hero.passive, hero.skill]) {
      if (ids.has(ability.id)) errors.push(`duplicate draft ID ${ability.id}`);
      ids.add(ability.id);
      effects.push([ability.id, ability.effect]);
    }
    if (hero.skill.durationSeconds > hero.skill.cooldownSeconds) errors.push(`${hero.id}: skill duration exceeds cooldown`);
    for (const key of Object.keys(hero.baseCombat)) {
      if (hero.baseCombat[key] !== draft.heroes[0].baseCombat[key]) errors.push(`${hero.id}: unequal level-one base combat ${key}`);
    }
  }
  for (const [id, effect] of effects) {
    if (effect.value > draft.sharedCaps[effect.domain]) errors.push(`${id}: effect exceeds shared cap`);
    if (effect.unit !== (effect.domain === 'estate.storageSeconds' ? 'seconds' : 'percent')) errors.push(`${id}: effect unit does not match domain`);
    if (!scopes[effect.domain]?.includes(effect.scope)) errors.push(`${id}: invalid effect scope`);
  }
  const levels = draft.contracts.barracksLevels;
  for (const [index, level] of levels.entries()) {
    if (level.level !== index || level.marchCapacity > level.totalCapacity || level.beds > level.totalCapacity || level.batchCapacity > level.totalCapacity) errors.push(`invalid barracks capacity at level ${index}`);
  }
  const research = new Map(draft.researchNodes.map(node => [node.id, node]));
  const encounters = new Set(draft.worldEncounters.map(encounter => encounter.id));
  const proofs = new Set(draft.researchNodes.map(node => node.prerequisites.proofId));
  if (proofs.size !== draft.researchNodes.length) errors.push('duplicate research proof ID');
  const fallbackProofs = new Set(draft.contracts.personalFallback.proofIds);
  for (const proof of proofs) if (!fallbackProofs.has(proof)) errors.push(`fallback missing research proof ${proof}`);
  for (const proof of fallbackProofs) if (!proofs.has(proof)) errors.push(`fallback unresolved research proof ${proof}`);
  for (const hero of draft.heroes) {
    for (const id of hero.relatedEncounterIds) {
      if (!encounters.has(id)) errors.push(`${hero.id}: unresolved encounter ${id}`);
    }
  }
  const visiting = new Set(), visited = new Set();
  function visit(id) {
    if (visiting.has(id)) { errors.push(`research prerequisite cycle at ${id}`); return; }
    if (visited.has(id)) return;
    visiting.add(id);
    for (const prerequisite of research.get(id).prerequisites.nodes) {
      if (!research.has(prerequisite.id)) errors.push(`${id}: unresolved research ${prerequisite.id}`);
      else visit(prerequisite.id);
    }
    visiting.delete(id);
    visited.add(id);
  }
  for (const id of research.keys()) visit(id);
  for (const encounter of draft.worldEncounters) {
    for (const id of encounter.outcomes.researchProofIds) {
      if (!proofs.has(id)) errors.push(`${encounter.id}: unresolved research proof ${id}`);
    }
    if (encounter.recommendedTroops < encounter.minimumTroops) errors.push(`${encounter.id}: recommended troops below minimum`);
    if (encounter.recommendedTroops > Math.max(...levels.map(level => level.marchCapacity))) errors.push(`${encounter.id}: recommended troops exceed march capacity`);
    if (encounter.objective.holdSeconds > encounter.timeLimitSeconds) errors.push(`${encounter.id}: objective exceeds time limit`);
    if ((encounter.category === 'stronghold') !== (encounter.outcomes.worldState === 'recovered')) errors.push(`${encounter.id}: category/world-state mismatch`);
    const materials = Object.values(encounter.materialReward).reduce((sum, amount) => sum + amount, 0);
    if (materials > encounter.minimumTroops * draft.contracts.materialReward.carryPerDepartingSoldier) errors.push(`${encounter.id}: reward exceeds minimum departing troop capacity`);
  }
  return errors;
}
