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
                else M1Draw("wave","shield-fragment","default",center,Vector2.one*radius*1.65f,visualTime*90,.52f,Color.white);
            }
            return true;
        }
        void DrawM1CombatSpear(Vector2 ground, float height, float opacity)
        {
            var visual=Resolve("icon","core:ditch_pike","default");
            M1CombatSpearGeometry.Pose(ground,height*1.35f,visual.Pivot,out var position,out var size,out var degrees);
            Draw(visual,GameVisualTokens.AttackLayer,position,0,size,degrees,opacity,Color.white,applyTween:false);
        }
        bool DrawM1CombatProjectile(WaveProjectileView projectile, Vector2 position, float direction)
        {
            if(projectile.Source==null||!m1CombatShapes.ContainsKey(projectile.Source))return false;
            var diameter=.12f; // Arrow artwork width only; targeting and damage remain Core-owned.
            M1Draw("attack","core:canal_bow","piercing",position,new Vector2(diameter*3,diameter),direction,1,Color.white);
            return true;
        }
        sealed class M1BladeStrike
        {
            public long EventId = -1; public int Tick; public Vector2 Origin; public float Aim, CoreRadius, Radius, HalfAngle; public bool Fitted;
            public readonly Vector2[] Kills = new Vector2[M1BladeKillCapacity]; public int KillCount, KillsDropped;
        }
        const int M1BladeKillCapacity = 512;
        readonly M1BladeStrike[] m1BladeStrikes = { new M1BladeStrike(), new M1BladeStrike(), new M1BladeStrike(), new M1BladeStrike() };
        readonly Dictionary<int,int> m1BladeHits = new Dictionary<int,int>(1024);
        int m1BladeNext, m1BladeHitTick = -1;
        public int M1BladeStrikes { get; private set; }
        public int M1BladeKillsOutside { get; private set; }
        public int M1BladeKillsInside { get; private set; }
        public float M1BladeMaximumRadius { get; private set; }
        public int M1BladeStrikesFullyVisible { get; private set; }

        void ResetM1Blades()
        {
            foreach (var strike in m1BladeStrikes) { strike.EventId = -1; strike.KillCount = strike.KillsDropped = 0; strike.Fitted = false; }
            m1BladeHits.Clear(); m1BladeNext = 0; m1BladeHitTick = -1;
            M1BladeStrikes = M1BladeKillsOutside = M1BladeKillsInside = M1BladeStrikesFullyVisible = 0; M1BladeMaximumRadius = 0;
        }

        bool IsM1GiantBlade(string source) => source != null && m1CombatShapes.TryGetValue(source, out var shape) && shape == "giantblade";

        // Core emits the strike's hits before its attack event and the kills at the end of the same tick.
        void AcceptM1Blade(WaveEvent value)
        {
            if (!IsM1GiantBlade(value.Source)) return;
            if (value.Kind == "hit" && value.Amount > 0)
            {
                if (value.Tick != m1BladeHitTick) { m1BladeHits.Clear(); m1BladeHitTick = value.Tick; }
                m1BladeHits[value.SubjectId] = value.Tick;
            }
            else if (value.Kind == "attack")
            {
                var strike = m1BladeStrikes[m1BladeNext++ % m1BladeStrikes.Length];
                var origin = Point(value.Position);
                strike.EventId = value.Id; strike.Tick = value.Tick; strike.Origin = origin; strike.Aim = Angle(Point(value.Target) - origin);
                strike.CoreRadius = value.Amount / (float)settings.WorldUnitsPerUnityUnit;
                strike.KillCount = strike.KillsDropped = 0; strike.Fitted = false; M1BladeStrikes++;
            }
        }

        M1BladeStrike M1BladeStrikeAt(int tick)
        {
            for (var i = 1; i <= m1BladeStrikes.Length; i++)
            {
                var strike = m1BladeStrikes[(m1BladeNext - i + m1BladeStrikes.Length * 4) % m1BladeStrikes.Length];
                if (strike.EventId >= 0 && strike.Tick == tick) return strike;
            }
            return null;
        }

        bool M1BladeKill(WaveEvent value, Vector2 kill, Vector2 rendered, bool hasRendered, out float delay, out Vector2 direction)
        {
            delay = 0; direction = Vector2.zero;
            if (!m1BladeHits.TryGetValue(value.SubjectId, out var tick) || tick != value.Tick) return false;
            var strike = M1BladeStrikeAt(value.Tick);
            if (strike == null) return false;
            var viewport = CameraWorldViewport;
            AddM1BladeKill(strike, kill, viewport);
            if (hasRendered) AddM1BladeKill(strike, rendered, viewport);
            var at = hasRendered ? rendered : kill;
            delay = GiantBladeEnvelope.ArrivalSeconds(strike.Aim, strike.Origin, at);
            direction = GiantBladeEnvelope.FallDirection(strike.Origin, at);
            return true;
        }

        static void AddM1BladeKill(M1BladeStrike strike, Vector2 point, Rect viewport)
        {
            if (!viewport.Contains(point)) return;
            if (strike.KillCount == M1BladeKillCapacity) { strike.KillsDropped++; return; }
            strike.Kills[strike.KillCount++] = point; strike.Fitted = false;
        }

        Rect M1BladeVisibleBounds()
        {
            var viewport = CameraWorldViewport;
            if (worldOverlayPixels.width <= 0 || worldOverlayPixels.height <= 0) return viewport;
            var overlay = OverlayWorldBounds();
            var bounds = Rect.MinMaxRect(Mathf.Max(viewport.xMin, overlay.xMin), Mathf.Max(viewport.yMin, overlay.yMin), Mathf.Min(viewport.xMax, overlay.xMax), Mathf.Min(viewport.yMax, overlay.yMax));
            return bounds.width > 0 && bounds.height > 0 ? bounds : viewport;
        }

        void FitM1Blade(M1BladeStrike strike)
        {
            if (strike.Fitted) return;
            var visible = M1BladeVisibleBounds();
            GiantBladeEnvelope.Fit(strike.Origin, strike.Aim, strike.CoreRadius, visible, strike.Kills, strike.KillCount, out strike.Radius, out strike.HalfAngle);
            strike.Fitted = true;
            if (GiantBladeEnvelope.FullyVisible(strike.Origin, strike.Aim, strike.Radius, strike.HalfAngle, visible)) M1BladeStrikesFullyVisible++;
            M1BladeMaximumRadius = Mathf.Max(M1BladeMaximumRadius, strike.Radius);
            for (var i = 0; i < strike.KillCount; i++)
            {
                if (GiantBladeEnvelope.Contains(strike.Origin, strike.Aim, strike.Radius, strike.HalfAngle, strike.Kills[i])) M1BladeKillsInside++;
                else M1BladeKillsOutside++;
            }
        }

#if UNITY_EDITOR
        public bool EditorM1BladeEnvelope(long attackEventId, out Vector2 origin, out float aim, out float radius, out float halfAngle, out Vector2[] kills)
        {
            foreach (var strike in m1BladeStrikes)
            {
                if (strike.EventId != attackEventId) continue;
                FitM1Blade(strike);
                origin = strike.Origin; aim = strike.Aim; radius = strike.Radius; halfAngle = strike.HalfAngle;
                kills = new Vector2[strike.KillCount]; System.Array.Copy(strike.Kills, kills, strike.KillCount);
                return true;
            }
            origin = default; aim = radius = halfAngle = 0; kills = null;
            return false;
        }
#endif

        void DrawM1CombatBlade(WaveEvent value, float age)
        {
            var strike = M1BladeStrikeFor(value.Id);
            if (strike == null) return;
            FitM1Blade(strike);
            var opacity = GiantBladeEnvelope.Opacity(age);
            if (opacity <= 0) return;
            var origin = strike.Origin; var radius = strike.Radius; var halfAngle = strike.HalfAngle; var aim = strike.Aim;
            // The authored fan pivot is its origin, its width is one radius and it spans the Core 90° fan; wider kill
            // envelopes add rotated stamps so the drawn trajectory still covers every on-screen victim.
            var arc = Resolve("attack","core:iron_blade","sector90");
            var extra = Mathf.Max(0, halfAngle - GiantBladeEnvelope.CoreHalfAngle);
            var stamps = extra <= 0 ? 1 : 2 + Mathf.CeilToInt(extra / GiantBladeEnvelope.CoreHalfAngle);
            for (var i = 0; i < stamps; i++)
            {
                var angle = stamps == 1 ? aim : Mathf.Lerp(aim - extra, aim + extra, i / (float)(stamps - 1));
                Draw(arc,GameVisualTokens.AttackLayer,origin,age,arc.WorldSize*radius,angle,opacity*.75f,Color.white,applyTween:false);
                DrawWaveAttackEdge(arc,origin,age,arc.WorldSize*radius,angle,GameVisualTokens.Attack,opacity);
                Draw(arc,GameVisualTokens.AttackLayer,origin,age,arc.WorldSize*(radius*.62f),angle,opacity*.55f,new Color(.8f,.95f,1),applyTween:false);
            }
            // One large blade sweeps the whole envelope in SweepSeconds and rests at its end; victims fall as it passes.
            var sweep = GiantBladeEnvelope.SweepAngle(aim, halfAngle, age);
            var visible = M1BladeVisibleBounds();
            for (var trail = 3; trail >= 0; trail--)
            {
                var angle = Mathf.Max(aim - halfAngle, sweep - trail * 9);
                var direction = M1Direction(angle);
                // The sword itself is the read of what killed them, so it always ends inside the screen.
                var length = Mathf.Min(radius, GiantBladeEnvelope.VisibleReach(origin, direction, visible) * .98f);
                M1Draw("icon","core:iron_blade","default",origin+direction*(length*.5f),
                    new Vector2(Mathf.Max(radius*.12f,length*.18f),length*.92f),angle-90,opacity*(trail==0?1:.18f),trail==0?Color.white:new Color(.7f,.9f,1));
            }
        }

        M1BladeStrike M1BladeStrikeFor(long eventId)
        {
            foreach (var strike in m1BladeStrikes) if (strike.EventId == eventId) return strike;
            return null;
        }

        float M1CombatEffectSeconds(WaveEvent value, float fallback)
            => value.Kind == "attack" && IsM1GiantBlade(value.Source) ? GiantBladeEnvelope.LifetimeSeconds : fallback;
        bool DrawM1CombatEvent(WaveEvent value, float age, float opacity)
        {
            if(value.Source==null||!m1CombatShapes.TryGetValue(value.Source,out var m1CombatShape))return false;
            var a=Point(value.Position);var b=Point(value.Target);
            if(value.Kind=="chain-link")
            {
                var length=(b-a).magnitude;
                M1Draw("attack","core:storm_fork","chain",(a+b)*.5f,new Vector2(length,.36f),Angle(b-a),opacity*.3f,new Color(.55f,.8f,1));
                M1Draw("attack","core:storm_fork","chain",(a+b)*.5f,new Vector2(length,.18f),Angle(b-a),opacity,Color.white);
            }
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
                if(m1CombatShape=="giantblade")DrawM1CombatBlade(value,age);
                else if(m1CombatShape=="summon")
                {
                    var progress=Mathf.Clamp01(age/WorldEffects.AttackLifetimeSeconds);
                    var diameter=radius*2;
                    // The call ring stays secondary to the soldiers it summons.
                    M1Draw("attack","core:muster_horn","wave",a,Vector2.one*diameter,0,opacity*.1f,new Color(.65f,.82f,1));
                    M1Draw("attack","core:muster_horn","wave",a,Vector2.one*diameter*Mathf.Lerp(.3f,1,progress),0,opacity*.16f,Color.white);
                }
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
