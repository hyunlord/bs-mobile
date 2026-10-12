using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        readonly Dictionary<string,string> m1CombatShapes=new Dictionary<string,string>(8,StringComparer.Ordinal);
        public void ClearM1Combat() => m1CombatShapes.Clear();
        public void ConfigureM1Combat(string source, string shape, string cue)
        {
            if(string.IsNullOrEmpty(source))throw new ArgumentException("A combat review source is required.",nameof(source));
            switch(shape) { case "giantblade":case "shield":case "lightning":case "meteor":case "tornado":case "spears":case "arrowrain":case "summon":break; default:throw new ArgumentException("Unknown combat review shape."); }
            m1CombatShapes[source]=shape;
        }
        public Vector2 M1CombatScreenPoint(WorldPoint point) => renderCamera.WorldToScreenPoint(Point(point));
        public bool M1CombatPointVisible(WorldPoint point) => CameraWorldViewport.Contains(Point(point));
        bool DrawM1CombatAttack(WaveAttackView attack, int tick, float alpha, int attackIndex, WorldPoint lordPosition, Vector2 lordPoint)
        {
            if(attack.Source==null||!m1CombatShapes.TryGetValue(attack.Source,out var m1CombatShape))return false;
            var radius=attack.Radius/(float)settings.WorldUnitsPerUnityUnit;
            if(attack.Kind=="review-ground-impact")
            {
                var center=Point(attack.Position);
                var composition=waveDefinition.Gear[attack.Source].Composition;
                var remaining=Mathf.Max(0,attack.ExpireTick-tick-alpha);
                var progress=1-Mathf.Clamp01(remaining/Mathf.Max(1,composition.DelayTicks));
                M1Draw("attack","core:muster_horn","wave",center,Vector2.one*radius*2,0,.2f+progress*.35f,new Color(.85f,.78f,.35f));
                if(m1CombatShape=="meteor")M1Draw("attack","core:ember_wand","projectile",center+new Vector2(.3f,1)*radius*(1-progress)*3,Vector2.one*radius*.8f,-100,1,new Color(1,.75f,.35f));
                else if(m1CombatShape=="arrowrain")
                {
                    // Height is presentation only; every shaft lands inside the locked Core impact circle at its due tick.
                    for(var arrow=0;arrow<3;arrow++)
                    {
                        var landing=center+M1Direction(arrow*120)*radius*.35f;
                        var position=landing+new Vector2(-.2f,1)*radius*(1-progress)*4;
                        M1Draw("attack","core:canal_bow","piercing",position,new Vector2(radius*.9f,radius*.13f),-78,.4f+progress*.6f,Color.white);
                    }
                }
                else DrawM1CombatSpear(center,radius*progress,.4f+progress*.6f);
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
        void DrawM1CombatSpear(Vector2 ground, float height, float opacity)
        {
            var visual=Resolve("icon","core:ditch_pike","default");
            M1CombatSpearGeometry.Pose(ground,height,visual.Pivot,out var position,out var size,out var degrees);
            Draw(visual,GameVisualTokens.AttackLayer,position,0,size,degrees,opacity,Color.white,applyTween:false);
        }
        bool DrawM1CombatProjectile(WaveProjectileView projectile, Vector2 position, float direction)
        {
            if(projectile.Source==null||!m1CombatShapes.ContainsKey(projectile.Source))return false;
            var diameter=.12f; // Arrow artwork width only; targeting and damage remain Core-owned.
            M1Draw("attack","core:canal_bow","piercing",position,new Vector2(diameter*3,diameter),direction,1,Color.white);
            return true;
        }
        void DrawM1CombatBlade(Vector2 origin, float aim, float radius, float age, float opacity)
        {
            // The authored fan pivot is its origin and its width is one radius, not a diameter.
            var arc=Resolve("attack","core:iron_blade","sector90");
            Draw(arc,GameVisualTokens.AttackLayer,origin,age,arc.WorldSize*radius,aim,opacity*.75f,Color.white,applyTween:false);
            // Interior brush strokes connect the near field to the actual outer hit envelope.
            Draw(arc,GameVisualTokens.AttackLayer,origin,age,arc.WorldSize*(radius*.42f),aim,opacity*.25f,new Color(.75f,.9f,1),applyTween:false);
            Draw(arc,GameVisualTokens.AttackLayer,origin,age,arc.WorldSize*(radius*.7f),aim,opacity*.4f,new Color(.8f,.95f,1),applyTween:false);
            var progress=Mathf.Clamp01(age/WorldEffects.AttackLifetimeSeconds);
            var sweep=aim+Mathf.Lerp(-38,38,progress);
            for(var trail=2;trail>=0;trail--)
            {
                var angle=Mathf.Max(aim-38,sweep-trail*10);
                var direction=M1Direction(angle);
                M1Draw("icon","core:iron_blade","default",origin+direction*(radius*.48f),
                    new Vector2(radius*.16f,radius*.84f),angle-90,opacity*(trail==0?1:.14f),trail==0?Color.white:new Color(.7f,.9f,1));
            }
        }
        bool DrawM1CombatEvent(WaveEvent value, float age, float opacity)
        {
            if(value.Source==null||!m1CombatShapes.TryGetValue(value.Source,out var m1CombatShape))return false;
            var a=Point(value.Position);var b=Point(value.Target);
            if(value.Kind=="chain-link")
                M1Draw("attack","core:storm_fork","chain",(a+b)*.5f,new Vector2((b-a).magnitude,.1f),Angle(b-a),opacity,Color.white);
            else if(value.Kind=="review-impact")
            {
                var radius=value.Amount/(float)settings.WorldUnitsPerUnityUnit;
                if(m1CombatShape=="spears")DrawM1CombatSpear(a,radius,opacity);
                else if(m1CombatShape=="arrowrain")
                {
                    for(var arrow=0;arrow<3;arrow++)M1Draw("attack","core:canal_bow","piercing",a+M1Direction(arrow*120)*radius*.35f,new Vector2(radius*.9f,radius*.13f),-78,opacity,Color.white);
                    M1Draw("attack","core:muster_horn","wave",a,Vector2.one*radius*2,0,opacity*.35f,new Color(.8f,.9f,1));
                }
                else M1Draw("attack","core:muster_horn","wave",a,Vector2.one*radius*2,0,opacity,new Color(1,.8f,.3f));
            }
            else if(value.Kind=="attack")
            {
                var radius=value.Amount/(float)settings.WorldUnitsPerUnityUnit;
                if(m1CombatShape=="giantblade")DrawM1CombatBlade(a,Angle(b-a),radius,age,opacity);
                else if(m1CombatShape=="summon")M1Draw("attack","core:muster_horn","wave",a,Vector2.one*radius*2,0,opacity,Color.white);
            }
            return true;
        }
    }

    public static class M1CombatSpearGeometry
    {
        // Authored 887px cell: opaque axial endpoints, in bottom-left pixel-center coordinates.
        public static readonly Vector2 Foot=new Vector2(69.5f,36.5f)/887f;
        public static readonly Vector2 Tip=new Vector2(865.5f,855.5f)/887f;

        public static void Pose(Vector2 ground, float height, Vector2 pivot, out Vector2 position, out Vector2 size, out float degrees)
        {
            var axis=Tip-Foot;
            size=Vector2.one*(Mathf.Max(0,height)/axis.magnitude);
            degrees=90-Mathf.Atan2(axis.y,axis.x)*Mathf.Rad2Deg;
            var footOffset=Quaternion.Euler(0,0,degrees)*Vector2.Scale(Foot-pivot,size);
            position=ground-new Vector2(footOffset.x,footOffset.y);
        }
    }
}
