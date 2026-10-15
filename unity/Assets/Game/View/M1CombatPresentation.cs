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
                if(m1CombatShape=="arrowrain")return true;   // drawn by DrawArrowVolleys (flight, warning circles, landing)
                var center=Point(attack.Position);
                var composition=waveDefinition.Gear[attack.Source].Composition;
                var remaining=Mathf.Max(0,attack.ExpireTick-tick-alpha);
                var progress=1-Mathf.Clamp01(remaining/Mathf.Max(1,composition.DelayTicks));
                M1Draw("attack","core:muster_horn","wave",center,Vector2.one*radius*2,0,.2f+progress*.35f,new Color(.85f,.78f,.35f));
                if(m1CombatShape=="meteor")M1Draw("attack","core:ember_wand","projectile",center+new Vector2(.3f,1)*radius*(1-progress)*3,Vector2.one*radius*.8f,-100,1,new Color(1,.75f,.35f));
                else DrawM1CombatSpear(center,radius*progress,.4f+progress*.6f);
            }
            else
            {
                var center=waveMotion.OrbitPosition(attackIndex,alpha,visualTime,settings.WorldUnitsPerUnityUnit,lordPosition,lordPoint);
                if(m1CombatShape=="tornado"||m1CombatShape=="shield")DrawOrbitAttack(attack,center,tick,alpha,lordPoint,m1CombatShape);
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
            public long EventId = -1; public int Tick; public Vector2 Origin; public float Aim, CoreRadius, Radius, HalfAngle, Accepted, Strength; public bool Fitted, Evolved, ShakeFired, Recorded; public int AcceptedFrame;
            public float Visible => Accepted + BladeSpec.Seconds(BladeSpec.WindupFrames);
            public readonly Vector2[] Kills = new Vector2[M1BladeKillCapacity]; public int KillCount, KillsDropped;
            // On-screen hit positions of this strike (the arc reaches in over them; the evolved sword plants at their centre).
            public readonly Vector2[] Hits = new Vector2[64]; public int HitCount; public Vector2 HitSum; public float Inner = float.PositiveInfinity;
            public bool SwordPlaced, SwordClear; public Vector2 SwordAt, SwordCrowd, Centre; public float SwordDropHeight, SwordFromHero, Outer, Edge, Stage, Half = GiantBladeEnvelope.CoreHalfAngle;
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
        public int M1BladeEvolvedStrikes { get; private set; }
        public int BladeDeferredHits { get; private set; }
        public int BladePresentedHits { get; private set; }

        void ResetM1Blades()
        {
            foreach (var strike in m1BladeStrikes) { strike.EventId = -1; strike.KillCount = strike.KillsDropped = 0; strike.Fitted = false; }
            m1BladeHits.Clear(); m1BladeNext = 0; m1BladeHitTick = -1;
            M1BladeStrikes = M1BladeKillsOutside = M1BladeKillsInside = M1BladeStrikesFullyVisible = M1BladeEvolvedStrikes = BladeDeferredHits = BladePresentedHits = 0; M1BladeMaximumRadius = 0;
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
                strike.Accepted = visualTime; strike.AcceptedFrame = PresentedFrames + 1; strike.Evolved = WeaponEvolved(value.Source); strike.ShakeFired = strike.Recorded = false;
                WaveGearDefinition reference = null; GearReference?.Gear.TryGetValue(value.Source, out reference);
                strike.Strength = LightningSpec.Strength(MusterFormationRule.Ratio(waveDefinition.Gear[value.Source], EquipmentLevel(value.Source), reference));
                if (strike.Evolved) M1BladeEvolvedStrikes++;
                strike.Stage = strike.Evolved ? 1 : BladeSpec.NormalStageShare * GearTier(value.Source);
                ResolveBladeHits(strike, value.Tick);
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
            // The kill waits for the wind-up, then for the blade to sweep to it.
            delay = Mathf.Max(0, strike.Visible - visualTime) + GiantBladeEnvelope.ArrivalSeconds(strike.Aim, strike.Origin, at);
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

        M1BladeStrike M1BladeStrikeFor(long eventId)
        {
            foreach (var strike in m1BladeStrikes) if (strike.EventId == eventId) return strike;
            return null;
        }

        float M1CombatEffectSeconds(WaveEvent value, float fallback)
            => value.Kind == "attack" && IsM1GiantBlade(value.Source) ? BladeSpec.Seconds(BladeSpec.WindupFrames) + GiantBladeEnvelope.LifetimeSeconds : fallback;
        bool DrawM1CombatEvent(WaveEvent value, float age, float opacity)
        {
            if(value.Source==null||!m1CombatShapes.TryGetValue(value.Source,out var m1CombatShape))return false;
            var a=Point(value.Position);var b=Point(value.Target);
            if(value.Kind=="chain-link")
            {
                // Drawn by DrawLightning (zigzag core + violet bloom + branches); the old thin ribbon is retired.
            }
            else if(value.Kind=="review-impact")
            {
                var radius=value.Amount/(float)settings.WorldUnitsPerUnityUnit;
                if(m1CombatShape=="spears")DrawM1CombatSpear(a,radius,opacity);
                else if(m1CombatShape=="arrowrain"){}   // landing drawn by DrawArrowVolleys
                else M1Draw("attack","core:muster_horn","wave",a,Vector2.one*radius*2,0,opacity,new Color(1,.8f,.3f));
            }
            else if(value.Kind=="attack")
            {
                var radius=value.Amount/(float)settings.WorldUnitsPerUnityUnit;
                if(m1CombatShape=="giantblade"){}   // drawn by DrawBladeStrikes (wind-up, sweep, fade)
                else if(m1CombatShape=="summon")
                {
                    var progress=Mathf.Clamp01(age/WorldEffects.AttackLifetimeSeconds);
                    // The call is a thin expanding outline only; a filled ring would tint the whole screen over the soldiers.
                    DrawMusterRing(a,radius,new Color(.65f,.82f,1),opacity*.35f);
                    DrawMusterRing(a,radius*Mathf.Lerp(.3f,1,progress),Color.white,opacity*.55f);
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

        // Same authored spear laid along a direction: foot at the hand, tip `length` away.
        public static void PoseAlong(Vector2 foot, Vector2 direction, float length, Vector2 pivot, out Vector2 position, out Vector2 size, out float degrees)
        {
            var axis=Tip-Foot;
            size=Vector2.one*(Mathf.Max(0,length)/axis.magnitude);
            degrees=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg-Mathf.Atan2(axis.y,axis.x)*Mathf.Rad2Deg;
            var footOffset=Quaternion.Euler(0,0,degrees)*Vector2.Scale(Foot-pivot,size);
            position=foot-new Vector2(footOffset.x,footOffset.y);
        }
    }
}
