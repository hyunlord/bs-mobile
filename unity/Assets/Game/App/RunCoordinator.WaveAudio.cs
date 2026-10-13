using System;
using System.Collections.Generic;
using SowSiege.Core;
namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        readonly Game.View.WaveBossPhaseCue bossPhaseCue=new Game.View.WaveBossPhaseCue();
        readonly Dictionary<(string prefix, string source, string suffix), string> waveCueNames = new Dictionary<(string, string, string), string>();
        WaveRuntimeDefinition waveCueDefinition;
        static string WaveCueName(string prefix, string source) => prefix + source.Substring(source.IndexOf(':') + 1).Replace('_', '-');
        string WaveCue(string prefix, string source, string suffix = "")
        {
            var key = (prefix, source, suffix);
            if (!waveCueNames.TryGetValue(key, out var cue)) { cue = WaveCueName(prefix, source) + suffix; waveCueNames.Add(key, cue); }
            return cue;
        }
        static WaveEnemyView FindWaveEnemy(WaveRuntimeFrame waveFrame, int id)
        {
            for (var i = 0; i < waveFrame.Enemies.Count; i++) if (waveFrame.Enemies[i].Id == id) return waveFrame.Enemies[i];
            return default;
        }
        static bool HasCommonEvent(RunFrame frame, PresentationKind kind, bool positiveAmount)
        {
            for (var i = 0; i < frame.Events.Count; i++)
                if (frame.Events[i].Kind == kind && (!positiveAmount || frame.Events[i].Amount > 0)) return true;
            return false;
        }
#if UNITY_EDITOR
        public static string EditorWaveCueName(string prefix, string source) => WaveCueName(prefix, source);
#endif
        void AcceptWaveAudio(RunFrame frame = null, WaveRuntimeFrame waveFrame = null)
        {
            frame ??= CurrentFrame; waveFrame ??= CurrentWave;
            if(waveFrame==null||waveSound==null)return;
            var definition=runCatalog.WaveRuntime;
            if (!ReferenceEquals(waveCueDefinition, definition)) { waveCueNames.Clear(); waveCueDefinition = definition; }
            for (var enemyIndex = 0; enemyIndex < frame.Enemies.Count; enemyIndex++)
            {
                var enemy = frame.Enemies[enemyIndex];
                if(definition.Enemies[enemy.DefinitionId].Kind!=WaveEnemyKind.FloodBoss)continue;
                var state=FindWaveEnemy(waveFrame,enemy.Id);
                if(state.Phase!=null&&bossPhaseCue.Accept(enemy.Id,state.Phase))waveSound.Play("boss-tusk-stuck");
            }
            for (var eventIndex = 0; eventIndex < waveFrame.Events.Count; eventIndex++)
            {
                var value = waveFrame.Events[eventIndex];
                if(value.Id<=lastWaveSound)continue;lastWaveSound=value.Id;
                if(value.Kind=="hit"&&value.Amount>0)
                {
                    if(!HasCommonEvent(frame,PresentationKind.Damage, true))waveSound.PlayHit(value.Id);
                    continue;
                }
                if(value.Kind=="enemy-killed")
                {
                    if(!HasCommonEvent(frame,PresentationKind.EnemyKilled, false))waveSound.PlayKill();
                    continue;
                }
                string cue=null;
                if(combatReview != null && combatReview.CueFor(value.Source) != null && (value.Kind=="attack"||value.Kind=="projectile-launched"||value.Kind=="chain-link"||value.Kind=="orbit-fragment"||value.Kind=="review-impact")) cue=combatReview.CueFor(value.Source);
                else if(value.Kind=="evolution-activated"){cue=WaveCue("evolution-",value.Source);sound?.PlayEvolutionHaptic();}
                else if(value.Kind=="enemy-tell")
                {
                    var enemy=definition.Enemies[value.Source];
                    var view=FindWaveEnemy(waveFrame,value.SubjectId);
                    cue=enemy.Kind==WaveEnemyKind.FloodBoss?(view.Phase=="tell-water"?"boss-bank-break":"boss-tusk-charge"):WaveCue("enemy-",value.Source,"-tell");
                }
                else if(value.Kind=="reward-created")
                {
                    var source=value.Source;
                    if(definition.Evolutions.TryGetValue(source,out var evolution))
                    {
                        string growthSource = null;
                        for (var i = 0; i < evolution.InputIds.Length; i++)
                            if (definition.Gear[evolution.InputIds[i]].Kind >= WaveAttackKind.SeedFan) { growthSource = evolution.InputIds[i]; break; }
                        source = growthSource ?? throw new InvalidOperationException("Sequence contains no matching element");
                    }
                    cue=WaveCue("complete-",source);
                }
                else if(value.Kind=="attack"||value.Kind=="projectile-launched"||value.Kind=="chain-link"||value.Kind=="orbit-fragment")
                {
                    var source=value.Source;
                    if(definition.Evolutions.TryGetValue(source,out var evolution)){source=evolution.InputIds[0];sound?.PlayEvolutionHaptic();}
                    if(definition.Gear.TryGetValue(source,out var gear))cue=WaveCue(gear.Kind>=WaveAttackKind.SeedFan?"tool-":"weapon-",source);
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
