using System;
using System.Linq;
using SowSiege.Core;
namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        readonly Game.View.WaveBossPhaseCue bossPhaseCue=new Game.View.WaveBossPhaseCue();
        void AcceptWaveAudio()
        {
            if(Wave==null||waveSound==null)return;
            var definition=runCatalog.WaveRuntime;
            foreach(var enemy in Frame.Enemies)
            {
                if(definition.Enemies[enemy.DefinitionId].Kind!=WaveEnemyKind.FloodBoss)continue;
                var state=Wave.Enemies.FirstOrDefault(e=>e.Id==enemy.Id);
                if(state!=null&&bossPhaseCue.Accept(enemy.Id,state.Phase))waveSound.Play("boss-tusk-stuck");
            }
            foreach(var value in Wave.Events)
            {
                if(value.Id<=lastWaveSound)continue;lastWaveSound=value.Id;
                if(value.Kind=="hit"&&value.Amount>0)
                {
                    if(!Frame.Events.Any(e=>e.Kind==PresentationKind.Damage&&e.Amount>0))waveSound.PlayCommon("hit");
                    continue;
                }
                if(value.Kind=="enemy-killed")
                {
                    if(!Frame.Events.Any(e=>e.Kind==PresentationKind.EnemyKilled))waveSound.PlayCommon("kill");
                    continue;
                }
                var suffix=value.Source.Substring(value.Source.IndexOf(':')+1).Replace('_','-');
                string cue=null;
                if(value.Kind=="evolution-activated")cue="evolution-"+suffix;
                else if(value.Kind=="enemy-tell")
                {
                    var enemy=definition.Enemies[value.Source];
                    var view=Wave.Enemies.FirstOrDefault(e=>e.Id==value.SubjectId);
                    cue=enemy.Kind==WaveEnemyKind.FloodBoss?(view?.Phase=="tell-water"?"boss-bank-break":"boss-tusk-charge"):"enemy-"+suffix+"-tell";
                }
                else if(value.Kind=="reward-created")
                {
                    var source=value.Source;
                    if(definition.Evolutions.TryGetValue(source,out var evolution))source=evolution.InputIds.First(id=>definition.Gear[id].Kind>=WaveAttackKind.SeedFan);
                    cue="complete-"+source.Substring(source.IndexOf(':')+1).Replace('_','-');
                }
                else if(value.Kind=="attack"||value.Kind=="projectile-launched"||value.Kind=="chain-link"||value.Kind=="orbit-fragment")
                {
                    var source=value.Source;
                    if(definition.Evolutions.TryGetValue(source,out var evolution))source=evolution.InputIds[0];
                    if(definition.Gear.TryGetValue(source,out var gear))cue=(gear.Kind>=WaveAttackKind.SeedFan?"tool-":"weapon-")+source.Substring(source.IndexOf(':')+1).Replace('_','-');
                }
                else if(value.Kind=="seed-detour")cue="item-bitter-seed-dust";
                else if(value.Kind=="item-selected"&&definition.Items.TryGetValue(value.Source,out var item))
                {
                    cue=item.Kind switch {WaveItemKind.FrontOrbit=>"item-joiner-square",WaveItemKind.RaiderAim=>"weapon-iron-blade",WaveItemKind.PickupRadius=>"item-gathering-loop",WaveItemKind.MoveSpeed=>"item-wayfarer-boots",_=>null};
                }
                else if(value.Kind=="dry-strike")cue="tool-rain-ladle-dry";
                else if(value.Kind=="water-carried")cue="item-clay-water-bead";
                else if(value.Kind=="harvest-guard")cue="item-crop-guard-signet";
                else if(value.Kind=="field-meal")cue="item-levy-bread-wrap";
                if(cue!=null)waveSound.Play(cue);
            }
        }
    }
}
