// Authoring source only: explicit inspected cutout rectangles and anatomical joint attachments.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url));
const rigs=[];
function rig(slug,source,family,height,root=[260,280]){const r={slug,source:`unity/Assets/Game/Editor/Authoring/EnemyRigs/${source}-parts.png`,family,height,root,ground:[260,444],bones:[],sprites:[]};rigs.push(r);return r;}
function bone(r,id,parent,offset,angle=0){r.bones.push({id,parent,offset,angle});return id;}
function sprite(r,bone,rect,anchor,scale,angle=0,extra={}){r.sprites.push({bone,rect,anchor,scale,angle,...extra});}
// The ground animals have fixed trunks. Only articulated limbs/head/tail animate.
{
const r=rig('seed_mite','mite','tripod',.44703,[246,230]);r.ground=[256,420];
const legs=[
 ['farRear',[1040,110,200,550],[1120,185],[80,-43],.28,-45,1],
 ['farMiddle',[875,660,190,505],[966,735],[106,9],.29,-40,0],
 ['farFront',[1080,693,170,470],[1140,770],[126,55],.29,-25,1],
 ['nearRear',[14,110,210,540],[138,186],[-81,-40],.29,30,0],
 ['nearMiddle',[43,653,204,495],[143,726],[-102,0],.29,15,1],
 ['nearFront',[248,690,205,480],[352,765],[-89,67],.31,0,0]
];
for(const [id,rect,anchor,offset,scale,angle,phase]of legs){bone(r,id,'root',offset,angle);r.bones.at(-1).gaitPhase=phase;sprite(r,id,rect,anchor,scale);}
bone(r,'body','root',[0,0]);sprite(r,'body',[339,153,555,535],[594,406],.46);
bone(r,'head','body',[46,110],-15);sprite(r,'head',[510,775,348,360],[601,840],.33);
}
{
const r=rig('crop_grazer','grazer','quadruped',.414199,[240,244]);r.ground=[260,470];
const legs=[
 ['farRear',[676,803,183,442],[799,850],[-65,30],.37,0,1],
 ['farFront',[958,812,243,420],[1031,866],[102,49],.40,-8,0],
 ['nearRear',[59,806,212,426],[207,866],[-97,36],.41,6,0],
 ['nearFront',[374,816,226,397],[430,864],[39,75],.44,0,1]
];
for(const [id,rect,anchor,offset,scale,angle,phase]of legs){bone(r,id,'root',offset,angle);r.bones.at(-1).gaitPhase=phase;sprite(r,id,rect,anchor,scale);}
bone(r,'body','root',[0,0]);sprite(r,'body',[40,145,645,643],[344,430],.39);
bone(r,'head','body',[85,74]);sprite(r,'head',[720,414,395,411],[831,471],.41);
bone(r,'crest','head',[-24,-32]);sprite(r,'crest',[632,3,613,475],[975,407],.29);
}
function human(slug,source,charger=false,shield=false){
const r=rig(slug,source,charger?'charger':'human',shield?.542403:charger?.44589:.44235,[270,300]);r.ground=[275,444];
const parts=charger?{
 cape:[[15,42,520,465],[459,91],.35],body:[[548,65,351,428],[731,353],.42],head:[[946,83,300,403],[1080,392],.36],
 farUpper:[[738,502,231,283],[929,549],.34],farLower:[[981,580,235,285],[1029,623],.34],nearUpper:[[107,500,279,292],[333,548],.34],nearLower:[[397,542,247,331],[447,582],.34],
 farThigh:[[735,775,274,222],[805,834],.39],farShin:[[743,951,272,275],[838,978],.40],nearThigh:[[215,794,262,219],[286,844],.39],nearShin:[[278,956,218,275],[361,996],.40]
}:shield?{
 cape:[[255,18,560,572],[727,136],.32],body:[[409,379,391,569],[603,663],.34],head:[[135,17,168,251],[242,226],.37],
 farUpper:[[777,495,166,194],[840,548],.38],farLower:[[859,652,168,238],[921,702],.38],nearUpper:[[112,479,185,196],[225,534],.38],nearLower:[[76,631,112,264],[140,666],.38],
 farThigh:[[769,799,163,230],[837,857],.36],farShin:[[818,970,185,270],[879,997],.37],nearThigh:[[273,793,166,219],[333,837],.36],nearShin:[[308,960,139,283],[372,1000],.37],
 shield:[[827,33,243,586],[928,256],.38],sword:[[1052,22,190,520],[1135,154],.34]
}:{
 cape:[[704,45,530,521],[1129,118],.33],body:[[329,82,349,573],[504,333],.37],head:[[42,20,252,301],[185,259],.33],
 farUpper:[[686,461,140,181],[758,512],.42],farLower:[[732,638,112,233],[785,668],.37],nearUpper:[[78,350,145,189],[168,401],.43],nearLower:[[59,542,164,247],[113,580],.38],
 farThigh:[[480,829,136,251],[550,884],.30],farShin:[[689,875,145,334],[753,941],.31],nearThigh:[[53,829,126,240],[127,872],.33],nearShin:[[237,876,169,333],[317,943],.32],
 spear:[[860,559,373,640],[1018,962],.40]
};
const add=(id,parent,offset,part,angle=0)=>{bone(r,id,parent,offset,angle);const [rect,anchor,scale]=parts[part];sprite(r,id,rect,anchor,scale);};
add('cape','root',[-14,-104],'cape');
add('farUpper','root',[36,-94],'farUpper',-12);
add('farLower','farUpper',[0,48],'farLower',-6);
add('farThigh','root',[19,0],'farThigh',-3);add('farShin','farThigh',[0,61],'farShin');
add('nearThigh','root',[-24,5],'nearThigh',3);add('nearShin','nearThigh',[0,59],'nearShin');
add('body','root',[0,0],'body');
add('head','body',charger?[26,-92]:[5,-113],'head',charger?7:0);
add('nearUpper','root',[-36,-88],'nearUpper',12);add('nearLower','nearUpper',[0,50],'nearLower',-12);
if(shield){add('sword','farLower',[6,66],'sword',35);add('shield','nearLower',[-4,31],'shield',-12);}
if(parts.spear)add('spear','nearLower',[3,62],'spear',-47);
return r;
}
human('raider','raider');human('shield_raider','shield',false,true);human('ram_runner','charger',true);
{
const r=rig('wine_wasp','wasp','wingbeat',.47337,[260,250]);r.ground=[260,427];
const legs=[[[42,793,172,415],[170,837],[-25,4]],[[255,804,173,435],[347,839],[0,26]],[[488,804,117,401],[532,849],[27,41]],[[690,805,128,399],[777,843],[54,34]],[[882,807,182,435],[921,847],[67,13]],[[1081,817,159,386],[1122,864],[80,-3]]];
for(let i=0;i<6;i++){bone(r,'leg'+i,'root',legs[i][2],(i-3)*11);r.bones.at(-1).gaitPhase=i%2;sprite(r,'leg'+i,legs[i][0],legs[i][1],.24);}
bone(r,'abdomen','root',[-12,1],-115);sprite(r,'abdomen',[708,429,367,387],[771,537],.36);
bone(r,'backWingL','root',[-27,-20]);sprite(r,'backWingL',[69,316,388,143],[426,349],.36);
bone(r,'backWingR','root',[15,-27]);sprite(r,'backWingR',[797,315,398,140],[825,356],.36);
bone(r,'wingL','root',[-21,-24]);sprite(r,'wingL',[17,12,494,304],[481,277],.38);
bone(r,'wingR','root',[17,-29]);sprite(r,'wingR',[706,9,529,303],[744,277],.38);
bone(r,'body','root',[0,0]);sprite(r,'body',[304,375,369,383],[463,547],.36);
bone(r,'antennaL','body',[23,7],-42);sprite(r,'antennaL',[34,493,210,301],[218,565],.19);
bone(r,'antennaR','body',[34,1],22);sprite(r,'antennaR',[1023,491,208,303],[1050,557],.19);
}
fs.writeFileSync(path.join(folder,'rigs.json'),JSON.stringify({schemaVersion:1,canvas:512,frameSize:256,padding:4,columns:8,outlinePixels:1,poses:['walk-0','walk-1','walk-2','walk-3','walk-4','walk-5','walk-6','walk-7','tell-0','tell-1','idle'],rigs},null,2)+'\n');
console.log('Authored 6 enemy rigs.');
