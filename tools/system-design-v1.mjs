import { createHash } from 'node:crypto';
import Ajv2020 from 'ajv/dist/2020.js';

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
  const items = ofKind('item'); quota(items, 42, 42, 'item');
  check(items.filter(record => record.behaviorClass === 'numeric-only').length * 5 <= items.length, 'numeric-only items exceed 20 percent');
  quota(ofKind('charter'), 12, 12, 'charter');
  const evolutions = ofKind('evolution'); quota(evolutions, 24, 24, 'evolution');
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
  quota(ofKind('vassal'), 8, 8, 'vassal');
  const materials = ofKind('material'); quota(materials, 4, 4, 'material');
  for (const direction of ['sources', 'sinks']) unique(materials.map(record => record[direction].map(link => link.contentId).sort().join('|')), `material ${direction} must have distinct content signatures`);
  quota(ofKind('manor'), 6, 8, 'manor');
  const systems = design.systems.map(system => system.id); unique(systems, 'duplicate system ID');
  check(systems.every(id => !byId.has(id)), 'system IDs collide with proposed content');
  check(JSON.stringify(systems) === JSON.stringify(design.influenceMatrix.systemIds), 'matrix system IDs must exactly match system order');
  check(design.influenceMatrix.cells.length === systems.length && design.influenceMatrix.cells.every(row => row.length === systems.length), 'matrix must cover every ordered system pair');
  unique(design.visualLanguage.map(row => row.kind), 'duplicate visual language kind');
  check([...new Set(content.map(record => record.kind))].every(kind => design.visualLanguage.some(row => row.kind === kind)), 'visual language must cover every content kind');
  quota(design.implementationWaves, 5, 5, 'implementation wave');
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
  for (const rule of systemDesignRuleReport(design).rules) check(rule.pass, `${rule.id}: expected ${JSON.stringify(rule.expected)}; actual ${JSON.stringify(rule.actual)}; violating ${rule.violatingIds.join(',')}`);
  return errors;
}

export function systemDesignRuleReport(design) {
  const content=design.content, ofKind=kind=>content.filter(r=>r.kind===kind);
  const gear=content.filter(r=>['weapon','tool'].includes(r.kind)), items=ofKind('item'), evos=ofKind('evolution');
  const rules=[];
  const rule=(id,expected,actual,violatingIds=[],pass=violatingIds.length===0)=>rules.push({id,expected,actual,violatingIds:[...new Set(violatingIds)].sort(),pass});
  const invalid=(records,predicate)=>records.filter(r=>!predicate(r)).map(r=>r.id);
  const xp=invalid(ofKind('tool'),r=>r.returns?.some(x=>x.channel==='xp')&&({'growth-complete':'growth-cycle','shipment-complete':'shipment','mission-return':'mission'}[r.harvest?.event]===r.harvest?.oncePer)&&Boolean(r.harvest?.visibleMoment));
  rule('D11-01-tool-xp',15,ofKind('tool').length-xp.length,xp,ofKind('tool').length===15&&!xp.length);
  const covered=new Set(evos.flatMap(r=>r.inputIds));const uncovered=invalid(gear,r=>covered.has(r.id));
  const recipeKind=r=>(r.inputIds??[]).map(id=>content.find(c=>c.id===id)?.kind??'missing').sort().join('|');
  const weaponTool=evos.filter(r=>recipeKind(r)==='tool|weapon').length;
  const toolTool=evos.filter(r=>recipeKind(r)==='tool|tool').length;
  const invalidRecipes=invalid(evos,r=>['tool|weapon','tool|tool'].includes(recipeKind(r)));
  rule('D11-02-evolution-coverage',{recipes:24,weaponTool:18,toolTool:6,gear:33},{recipes:evos.length,weaponTool,toolTool,gear:gear.length-uncovered.length},[...uncovered,...invalidRecipes],evos.length===24&&weaponTool===18&&toolTool===6&&gear.length===33&&!uncovered.length&&!invalidRecipes.length);
  const linked=items.filter(r=>r.itemScope==='linked'),universal=items.filter(r=>r.itemScope==='universal');
  const links=Object.fromEntries(gear.map(r=>[r.id,linked.filter(i=>[...(i.linkedToolIds??[]),...(i.linkedWeaponIds??[])].includes(r.id)).length]));
  const badItems=invalid(items,r=>r.itemScope==='linked'?(r.linkedToolIds?.length??0)+(r.linkedWeaponIds?.length??0)>0:r.itemScope==='universal'&&r.linkedToolIds?.length===0&&r.linkedWeaponIds?.length===0);
  const badLinks=Object.keys(links).filter(id=>links[id]<1||links[id]>4);
  const numeric=items.filter(r=>r.behaviorClass==='numeric-only').length;
  rule('D11-03-item-links',{total:42,linked:36,universal:6,minLinks:1,maxLinks:4,numericMaxPercent:20},{total:items.length,linked:linked.length,universal:universal.length,gearLinks:links,numeric},[...badItems,...badLinks],items.length===42&&linked.length===36&&universal.length===6&&numeric*5<=items.length&&!badItems.length&&!badLinks.length);
  const gap=invalid(ofKind('weapon'),r=>r.costKind==='shape-gap');rule('D11-04-weapon-gap',18,ofKind('weapon').length-gap.length,gap);
  const cards=invalid(content,r=>typeof r.cardText==='string'&&[...r.cardText].length<=40&&r.cardText.split('→').length===2&&r.cardText.split('→').every(side=>/[가-힣]/u.test(side)));
  rule('D11-05-card-text',{maxCodepoints:40,arrows:1,koreanEachSide:true},{records:content.length,valid:content.length-cards.length},cards);
  const readable=invalid(gear,r=>typeof r.readability?.baseAction==='string'&&r.readability.baseAction.trim()&&(r.readability.conditionalVariant===null||(typeof r.readability.conditionalVariant?.visibleState==='string'&&typeof r.readability.conditionalVariant?.action==='string')));
  rule('D11-05-readable-gear',33,gear.length-readable.length,readable);
  const mobileIds=['core:carpenter_hammer','core:sheltered_sowing','core:guarded_harvest'];
  const mobile=content.filter(r=>mobileIds.includes(r.id));
  const stationary=invalid(mobile,r=>/반경/.test(JSON.stringify(r))&&!/머무(?:르|름)|제자리|정지해야/.test(JSON.stringify(r)));
  rule('D11-06-mobile-work',mobileIds,mobile.map(r=>r.id),stationary,mobile.length===3&&!stationary.length);
  const allies=content.filter(r=>r.kind==='vassal'||r.kind==='tool'&&r.targetKind==='people'||r.kind==='evolution'&&r.inputIds.some(id=>content.some(t=>t.id===id&&t.kind==='tool'&&t.targetKind==='people'))||r.primitives?.some(id=>['unit:ally-task','unit:ally-support'].includes(id)));
  const groups=invalid(allies,r=>r.allyGroup?.entityUnit==='group'&&r.allyGroup.capPolicy==='shared-active-group-budget'&&['reuse-existing','do-not-spawn'].includes(r.allyGroup.onCap)&&r.allyGroup.representation==='representative-sprites'&&r.allyGroup.capValue===null&&r.allyGroup.spriteCount===null);
  rule('D11-07-group-policy',{unit:'group',cap:'shared-active-group-budget',numericValues:null},{records:allies.length,valid:allies.length-groups.length},groups);
  const access=invalid(ofKind('material'),r=>r.guarantee?.targetSelection==='before-run'&&r.guarantee.earlyOffer==='first-equipment-offer'&&r.guarantee.clearGrant==='chapter-clear-base'&&r.guarantee.quantity===null&&r.guarantee.clearGrantScope==='all-catalog-materials'&&r.guarantee.targetAvailability==='profile-supports-early-tool'&&ofKind('tool').some(t=>t.id===r.guarantee.earlyToolId)&&r.sources.some(s=>s.contentId===r.guarantee.earlyToolId));
  rule('D11-08-material-access',4,ofKind('material').length-access.length,access);
  const scenes=invalid(ofKind('vassal'),r=>['setup','action','visibleResult'].every(k=>typeof r.signatureScene?.[k]==='string'&&r.signatureScene[k].trim())&&ofKind('vassal').filter(v=>v.signatureScene?.action===r.signatureScene.action).length===1);
  rule('D11-09-vassal-scenes',8,ofKind('vassal').length-scenes.length,scenes,ofKind('vassal').length===8&&!scenes.length);
  const waveIds=['wave-1a','wave-1b','wave-2','wave-3','wave-4'];
  rule('D11-11-wave-order',waveIds,design.implementationWaves.map(w=>w.id),[],JSON.stringify(waveIds)===JSON.stringify(design.implementationWaves.map(w=>w.id)));
  const fixed=['iron_blade','ward_orbit','storm_fork','ember_wand','harvest_scythe','seed_bag','rain_ladle','carpenter_hammer','muster_horn','sowing_sworddance','warded_masonry','sheltered_sowing','raider','seed_mite','crop_grazer','ram_runner','shield_raider','wine_wasp','flood_tusk','bitter_seed_dust','clay_water_bead','crop_guard_signet','joiner_square','meadow_buckle','levy_bread_wrap'].map(id=>'core:'+id).concat('meta:chapter_1');
  const wave=design.implementationWaves.find(w=>w.id==='wave-1a')?.contentIds??[];
  const waveUniversal=wave.filter(id=>universal.some(r=>r.id===id));
  const expectedUniversal=['core:gathering_loop','core:wayfarer_boots'];
  const badWave=[...fixed.filter(id=>!wave.includes(id)),...wave.filter(id=>!fixed.includes(id)&&!expectedUniversal.includes(id)),...expectedUniversal.filter(id=>!wave.includes(id))];
  rule('D11-11-wave-1a',{fixedIds:fixed.sort(),universalIds:expectedUniversal,universalItems:2,total:28},{contentIds:[...wave].sort(),universalItems:waveUniversal.length,total:wave.length},badWave,!badWave.length&&waveUniversal.length===2&&wave.length===28);
  const edges=design.influenceMatrix.cells.flat().filter(v=>v!=='none').length;
  const systemsHash=createHash('sha256').update(JSON.stringify({systems:design.systems,influenceMatrix:design.influenceMatrix})).digest('hex');
  const expectedHash='2d123f37d155363cd3be5a1d2b34c0b67c6b234146f1a20559652b2120076c37';
  rule('D11-preserved-systems',{systems:13,edges:41,sha256:expectedHash},{systems:design.systems.length,edges,sha256:systemsHash},[],design.systems.length===13&&edges===41&&systemsHash===expectedHash);
  const roles=Object.fromEntries(['crowd','single','control','survival','estate'].map(role=>[role,ofKind('weapon').filter(r=>r.primaryRole===role).length]));
  const forms=new Set(ofKind('weapon').map(r=>r.form)).size;
  rule('D11-preserved-weapon-forms',{forms:16,roles:{crowd:4,single:4,control:4,survival:3,estate:3}},{forms,roles},[],forms===16&&roles.crowd===4&&roles.single===4&&roles.control===4&&roles.survival===3&&roles.estate===3);
  const contract=design.primitiveContract, units=contract?.units??[], support=contract?.operationSupport??[];
  const validators=new Map(), primitiveErrors=[];
  const ajv=new Ajv2020({allErrors:true,strict:true});
  for(const unit of units) {
    try { validators.set(unit.id,ajv.compile(unit.paramSchema)); }
    catch { primitiveErrors.push(unit.id); }
    if(unit.paramSchema?.type!=='object'||unit.paramSchema?.additionalProperties!==false||unit.operationSupportIds.some(id=>!support.some(row=>row.id===id))) primitiveErrors.push(unit.id);
  }
  for(const record of content) {
    if(!record.primitives?.length||new Set(record.primitives).size!==record.primitives.length||JSON.stringify([...(record.primitives??[])].sort())!==JSON.stringify(Object.keys(record.params??{}).sort())||!Array.isArray(record.bespoke)) { primitiveErrors.push(record.id);continue; }
    const sameIds=(a,b)=>JSON.stringify([...(a??[])].sort())===JSON.stringify([...(b??[])].sort());
    const referencesExist=value=>typeof value==='string'?(!/^(core|meta):/.test(value)||content.some(r=>r.id===value)):Array.isArray(value)?value.every(referencesExist):value!==null&&typeof value==='object'?Object.values(value).every(referencesExist):true;
    for(const id of record.primitives) {
      const params=record.params[id];
      if(!validators.get(id)?.(params)||!referencesExist(params)) primitiveErrors.push(record.id);
      if(id==='unit:evolution-replace'&&!sameIds(params.inputIds,record.inputIds)) primitiveErrors.push(record.id);
      if(id==='unit:material-guarantee'&&params.earlyToolId!==record.guarantee?.earlyToolId) primitiveErrors.push(record.id);
      if(id==='unit:equipment-scope'&&(params.scope!==record.itemScope||!sameIds(params.toolIds,record.linkedToolIds)||!sameIds(params.weaponIds,record.linkedWeaponIds))) primitiveErrors.push(record.id);
      if(id==='unit:chapter-route'&&(params.bossId!==record.bossId||params.order!==record.order)) primitiveErrors.push(record.id);
      if(id==='unit:meta-route'&&(!sameIds(params.sources,record.sources?.map(r=>r.contentId)??record.inputIds)||!sameIds(params.sinks,record.sinks?.map(r=>r.contentId)??[]))) primitiveErrors.push(record.id);
      if(id==='unit:harvest-contact'&&params.sourceIds.some(source=>!content.some(r=>r.id===source&&r.kind==='tool'))) primitiveErrors.push(record.id);
    }
  }
  const metadataValid=contract?.version==='1.1.0'&&new Set(units.map(u=>u.id)).size===units.length&&new Set(support.map(u=>u.id)).size===support.length;
  rule('D11-10-primitives',{allRecords:true,strictParameters:true,version:'1.1.0'},{records:content.length,units:units.length,operationSupport:support.length,unitCoverage:Object.fromEntries(units.map(unit=>[unit.id,content.filter(r=>r.primitives?.includes(unit.id)).length])),support: support.map(row=>({id:row.id,operation:row.operation,profiles:row.profiles,observed:row.observed,semantic:row.semantic})),bespokeRecords:content.filter(r=>r.bespoke?.length).map(r=>r.id).sort()},primitiveErrors,metadataValid&&units.length>0&&!primitiveErrors.length);
  return {contract:'system-design-rules-v1.1',revision:design.revision,rules};
}
