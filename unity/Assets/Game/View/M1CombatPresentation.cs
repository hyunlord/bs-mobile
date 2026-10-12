using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        string m1CombatSource, m1CombatShape;
        public void ConfigureM1Combat(string source, string shape, string cue)
        {
            switch(shape) { case "giantblade":case "shield":case "lightning":case "meteor":case "tornado":case "spears":case "arrowrain":case "summon":break; default:throw new ArgumentException("Unknown combat review shape."); }
            m1CombatSource=source; m1CombatShape=shape;
        }
        public Vector2 M1CombatScreenPoint(WorldPoint point) => renderCamera.WorldToScreenPoint(Point(point));
        public bool M1CombatPointVisible(WorldPoint point) => CameraWorldViewport.Contains(Point(point));
        bool DrawM1CombatAttack(WaveAttackView attack, int tick, float alpha, int attackIndex, WorldPoint lordPosition, Vector2 lordPoint)
        {
            if(m1CombatSource==null||attack.Source!=m1CombatSource)return false;
            var radius=attack.Radius/(float)settings.WorldUnitsPerUnityUnit;
            if(attack.Kind=="review-ground-impact")
            {
                var center=Point(attack.Position);
                var composition=waveDefinition.Gear[attack.Source].Composition;
                var remaining=Mathf.Max(0,attack.ExpireTick-tick-alpha);
                var progress=1-Mathf.Clamp01(remaining/Mathf.Max(1,composition.DelayTicks));
                M1Draw("attack","core:muster_horn","wave",center,Vector2.one*radius*2,0,.2f+progress*.35f,new Color(.85f,.78f,.35f));
                if(m1CombatShape=="meteor")M1Draw("attack","core:ember_wand","projectile",center+new Vector2(.3f,1)*radius*(1-progress)*3,Vector2.one*radius*.8f,-100,1,new Color(1,.75f,.35f));
                else M1Draw("attack","core:canal_bow","piercing",center,new Vector2(radius*2*progress,radius*.18f),80,.4f+progress*.6f,Color.white);
            }
            else
            {
                var center=waveMotion.OrbitPosition(attackIndex,alpha,visualTime,settings.WorldUnitsPerUnityUnit,lordPosition,lordPoint);
                if(m1CombatShape=="tornado")
                    for(var i=0;i<6;i++)M1Draw("attack","core:rain_ladle","wave",center+Vector2.up*radius*(i/5f-.5f),new Vector2(radius*(.6f+i*.25f),radius*.3f),Mathf.Sin(visualTime*8+i)*10,.8f,new Color(.65f,.9f,.75f));
                else M1Draw("wave","shield-fragment","default",center,Vector2.one*radius*2,visualTime*90,1,Color.white);
            }
            return true;
        }
        bool DrawM1CombatProjectile(WaveProjectileView projectile, Vector2 position, float direction)
        {
            if(m1CombatSource==null||projectile.Source!=m1CombatSource)return false;
            var diameter=.12f; // Arrow artwork width only; targeting and damage remain Core-owned.
            M1Draw("attack","core:canal_bow","piercing",position,new Vector2(diameter*3,diameter),direction,1,Color.white);
            return true;
        }
        bool DrawM1CombatEvent(WaveEvent value, float age, float opacity)
        {
            if(m1CombatSource==null||value.Source!=m1CombatSource)return false;
            var a=Point(value.Position);var b=Point(value.Target);
            if(value.Kind=="chain-link")
                M1Draw("attack","core:storm_fork","chain",(a+b)*.5f,new Vector2((b-a).magnitude,.1f),Angle(b-a),opacity,Color.white);
            else if(value.Kind=="review-impact")
            {
                var radius=value.Amount/(float)settings.WorldUnitsPerUnityUnit;
                if(m1CombatShape=="spears")M1Draw("attack","core:canal_bow","piercing",a,new Vector2(radius*2,radius*.2f),80,opacity,Color.white);
                else M1Draw("attack","core:muster_horn","wave",a,Vector2.one*radius*2,0,opacity,new Color(1,.8f,.3f));
            }
            else if(value.Kind=="attack")
            {
                var radius=value.Amount/(float)settings.WorldUnitsPerUnityUnit;
                if(m1CombatShape=="giantblade")M1Draw("attack","core:iron_blade","sector90",a,Vector2.one*radius*2,Angle(b-a),opacity,Color.white);
                else if(m1CombatShape=="summon")M1Draw("attack","core:muster_horn","wave",a,Vector2.one*radius*2,0,opacity,Color.white);
            }
            return true;
        }
    }
}
