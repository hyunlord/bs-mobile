using System.Collections.Generic;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class ThreatCorridorTests
    {
        [TestCase(false)] [TestCase(true)]
        public void GrazingCorridorKeepsArrowInsideVisibleThreatEvenWhenAxisIsOffscreen(bool reverse)
        {
            var viewport=new Rect(0,0,10,10);
            var lane=Lane(1,30,-.5f);lane.Origin=new Vector2(-5,-.5f);lane.Target=new Vector2(15,-.5f);lane.Radius=1;
            if(reverse){var swap=lane.Origin;lane.Origin=lane.Target;lane.Target=swap;}
            var start=lane.Origin;var end=lane.Target;
            Assert.That(ThreatCorridors.Intersects(viewport,lane),Is.True);
            Assert.That(ThreatCorridors.ClipSegment(viewport,ref start,ref end),Is.False);
            var arrow=ThreatCorridors.VisibleArrowPosition(viewport,lane,.2f);
            Assert.That(viewport.Contains(arrow),Is.True);
            Assert.That(arrow.y,Is.GreaterThan(0));
            Assert.That(ThreatCorridors.Contains(lane,arrow),Is.True);
            lane.Origin=new Vector2(-.3f,-.3f);lane.Target=new Vector2(-.2f,-.2f);lane.Radius=.5f;
            arrow=ThreatCorridors.VisibleArrowPosition(viewport,lane,.1f);
            Assert.That(viewport.Contains(arrow),Is.True);
            Assert.That(ThreatCorridors.Contains(lane,arrow),Is.True);
        }

        [Test] public void OffscreenThreatClipsAtEdgeAndPreservesTravelDirection()
        {
            var viewport=new Rect(0,0,10,10);
            var start=new Vector2(-5,5);var end=new Vector2(8,5);
            Assert.That(ThreatCorridors.ClipSegment(viewport,ref start,ref end),Is.True);
            Assert.That(start,Is.EqualTo(new Vector2(0,5)));Assert.That(end,Is.EqualTo(new Vector2(8,5)));
            start=new Vector2(15,5);end=new Vector2(-5,5);
            Assert.That(ThreatCorridors.ClipSegment(viewport,ref start,ref end),Is.True);
            Assert.That(start,Is.EqualTo(new Vector2(10,5)));Assert.That(end,Is.EqualTo(new Vector2(0,5)));
        }

        [Test] public void CornerIntersectionAndOffscreenNonintersectionAreDistinct()
        {
            var viewport=new Rect(0,0,10,10);
            var start=new Vector2(-5,-5);var end=new Vector2(5,5);
            Assert.That(ThreatCorridors.ClipSegment(viewport,ref start,ref end),Is.True);
            Assert.That(start,Is.EqualTo(Vector2.zero));
            var lane=Lane(1,30,-2);lane.Origin=new Vector2(-5,-2);lane.Target=new Vector2(15,-2);
            Assert.That(ThreatCorridors.Intersects(viewport,lane),Is.False);
            lane.Origin=new Vector2(-.19f,-.19f);lane.Target=lane.Origin;lane.Radius=.2f;
            Assert.That(ThreatCorridors.Intersects(viewport,lane),Is.False,"Expanded rectangle corners must not create nonexistent capsule threats.");
            lane.Origin=new Vector2(-.1f,-.1f);lane.Target=lane.Origin;
            Assert.That(ThreatCorridors.Intersects(viewport,lane),Is.True);
            start=new Vector2(-5,5);end=new Vector2(-1,5);
            Assert.That(ThreatCorridors.ClipSegment(viewport,ref start,ref end),Is.False,"Infinite forward extension is not the actual attack segment.");
        }

        static ThreatCorridor Lane(int id, int impact, float y) => new ThreatCorridor
        { Id = id, ImpactTick = impact, TellTicks = 30, Origin = new Vector2(0, y), Target = new Vector2(5, y), Radius = .2f };

        [TestCase(false)] [TestCase(true)]
        public void LocalChargeStripKeepsHeroEndAndNeverExceedsScreenThird(bool reverse)
        {
            var lane = Lane(1, 30, 5);
            lane.Origin = new Vector2(-20, 5); lane.Target = new Vector2(20, 5);
            if (reverse) { var swap = lane.Origin; lane.Origin = lane.Target; lane.Target = swap; }
            var lord = new Vector2(4, 5);
            ThreatCorridors.LocalSegment(lane, lord, 10f / 3f, out var start, out var end);
            Assert.That(Vector2.Distance(start, end), Is.EqualTo(10f / 3f).Within(.0001f));
            var direction = (lane.Target - lane.Origin).normalized;
            Assert.That(Vector2.Dot(end - lord, direction), Is.EqualTo(lane.Radius).Within(.0001f));
            Assert.That(Vector2.Dot(end - start, direction), Is.GreaterThan(0));
            Assert.That(ThreatCorridors.Contains(lane, start), Is.True);
            Assert.That(ThreatCorridors.Contains(lane, end), Is.True);
        }

        [Test] public void ShortChargeDoesNotExtendAndWaterKeepsItsHazardLength()
        {
            var lane = Lane(1, 30, 0);
            ThreatCorridors.LocalSegment(lane, new Vector2(100, 0), 20, out var start, out var end);
            Assert.That(start.x, Is.EqualTo(-.2f).Within(.0001f));
            Assert.That(end.x, Is.EqualTo(5.2f).Within(.0001f));
            lane.Water = true;
            ThreatCorridors.LocalSegment(lane, Vector2.zero, 1, out start, out end);
            Assert.That(Vector2.Distance(start, end), Is.EqualTo(5.4f).Within(.0001f));
        }

        [TestCase(false)] [TestCase(true)]
        public void OffscreenEntryArrowStaysAtIncomingEdgeIncludingGrazing(bool reverse)
        {
            var viewport = new Rect(0, 0, 10, 10);
            var lane = Lane(1, 30, 5); lane.Origin = new Vector2(-5, 5); lane.Target = new Vector2(15, 5);
            if (reverse) { var swap = lane.Origin; lane.Origin = lane.Target; lane.Target = swap; }
            var arrow = ThreatCorridors.EntryArrowPosition(viewport, lane, .2f);
            Assert.That(arrow.x, Is.EqualTo(reverse ? 9.8f : .2f).Within(.0001f));
            Assert.That(ThreatCorridors.Contains(lane, arrow), Is.True);
            lane.Origin = new Vector2(lane.Origin.x, -.5f); lane.Target = new Vector2(lane.Target.x, -.5f); lane.Radius = 1;
            arrow = ThreatCorridors.EntryArrowPosition(viewport, lane, .2f);
            Assert.That(viewport.Contains(arrow), Is.True);
            Assert.That(arrow.y, Is.LessThanOrEqualTo(.2f));
            Assert.That(reverse ? 10 - arrow.x : arrow.x, Is.LessThanOrEqualTo(.2f));
            Assert.That(ThreatCorridors.Contains(lane, arrow), Is.True);
        }

        [Test] public void EarliestThreeDistinctLanesRemainAndParallelOverlapsMerge()
        {
            var candidates = new List<ThreatCorridor> { Lane(9, 90, 3), Lane(4, 40, 2), Lane(3, 30, 1), Lane(2, 20, .1f), Lane(1, 10, 0) };
            var selected = new List<ThreatCorridor>(); ThreatCorridors.Select(candidates, selected);
            Assert.That(selected.Count, Is.EqualTo(3));
            Assert.That(selected[0].Id, Is.EqualTo(1)); Assert.That(selected[1].Id, Is.EqualTo(3)); Assert.That(selected[2].Id, Is.EqualTo(4));
            Assert.That(ThreatCorridors.Contains(selected[0], new Vector2(3, .29f)), Is.True);
        }

        [Test] public void CrossingThreatsRemainDistinctAndFillUsesCoreDeadline()
        {
            var lane = Lane(1, 60, 0); var crossing = Lane(2, 61, 0);
            crossing.Origin = new Vector2(2, -2); crossing.Target = new Vector2(2, 2);
            var selected = new List<ThreatCorridor>(); ThreatCorridors.Select(new List<ThreatCorridor> { crossing, lane }, selected);
            Assert.That(selected.Count, Is.EqualTo(2));
            Assert.That(ThreatCorridors.Progress(lane, 45, 1), Is.EqualTo(.5f).Within(.001f));
            Assert.That(ThreatCorridors.Progress(lane, 60, 1), Is.EqualTo(1));
            Assert.That(ThreatCorridors.OriginVisible(new Rect(0, 0, 10, 10), new Vector2(-1, 5)), Is.False);
        }

        [Test] public void VisualRectangleCoversBothCapsuleEndsWithoutEndpointMarkers()
        {
            var lane=Lane(1,30,0);
            ThreatCorridors.CoveredSegment(lane,out var start,out var end);
            Assert.That(start.x,Is.EqualTo(-lane.Radius).Within(.001f));
            Assert.That(end.x,Is.EqualTo(5+lane.Radius).Within(.001f));
            Assert.That(ThreatCorridors.Contains(lane,new Vector2(5.19f,0)),Is.True);
        }

        [Test] public void CrowdedStreamingSelectionDoesNotGrowItsThreeEntryBuffer()
        {
            var selected=new List<ThreatCorridor>(ThreatCorridors.VisibleLimit);
            for(var index=1000;index>0;index--)ThreatCorridors.Consider(Lane(index,index,index),selected);
            Assert.That(selected.Count,Is.EqualTo(3)); Assert.That(selected.Capacity,Is.EqualTo(3));
            Assert.That(selected[0].ImpactTick,Is.EqualTo(1)); Assert.That(selected[2].ImpactTick,Is.EqualTo(3));
        }

        [TestCase(false)] [TestCase(true)]
        public void BridgeCollapsesExpandedOverlapsAndPreservesEarliestImpact(bool reverse)
        {
            var second=Lane(2,20,0); second.Origin=new Vector2(5,0); second.Target=new Vector2(10,0);
            var candidates=new List<ThreatCorridor>{Lane(1,10,0),second,Lane(3,30,3)};
            if(reverse)candidates.Reverse();
            var selected=new List<ThreatCorridor>(ThreatCorridors.VisibleLimit);
            foreach(var lane in candidates)ThreatCorridors.Consider(lane,selected);
            var bridge=Lane(4,15,0); bridge.Target=new Vector2(10,0);
            ThreatCorridors.Consider(bridge,selected);
            Assert.That(selected.Count,Is.EqualTo(2)); Assert.That(selected.Capacity,Is.EqualTo(3));
            Assert.That(selected[0].Id,Is.EqualTo(1)); Assert.That(selected[0].ImpactTick,Is.EqualTo(10));
            Assert.That(selected[1].Id,Is.EqualTo(3));
            Assert.That(ThreatCorridors.Contains(selected[0],new Vector2(9.9f,.19f)),Is.True);
            Assert.That(selected[0].Radius,Is.EqualTo(.2f).Within(.001f));
            ThreatCorridors.Consider(Lane(5,40,5),selected);
            Assert.That(selected.Count,Is.EqualTo(3)); Assert.That(selected[2].Id,Is.EqualTo(5));
        }

        [Test] public void OffsetBridgeDoesNotTurnDistinctHazardsIntoOneBlanket()
        {
            var selected=new List<ThreatCorridor>(3);
            ThreatCorridors.Consider(Lane(1,10,0),selected);
            ThreatCorridors.Consider(Lane(2,20,.7f),selected);
            ThreatCorridors.Consider(Lane(3,30,3),selected);
            ThreatCorridors.Consider(Lane(4,15,.35f),selected);
            Assert.That(selected.Count,Is.EqualTo(3));
            Assert.That(selected[0].Id,Is.EqualTo(1)); Assert.That(selected[1].Id,Is.EqualTo(4)); Assert.That(selected[2].Id,Is.EqualTo(2));
            foreach(var lane in selected)Assert.That(lane.Radius,Is.EqualTo(.2f).Within(.001f));
        }

        [TestCase(false)] [TestCase(true)]
        public void HundredsOfOffsetParallelThreatsStayWithinOriginalWidthBudget(bool reverse)
        {
            var selected=new List<ThreatCorridor>(3);
            for(var step=0;step<500;step++)
            {
                var index=reverse?499-step:step;
                ThreatCorridors.Consider(Lane(index,index+1,index*.025f),selected);
            }
            Assert.That(selected.Count,Is.EqualTo(3)); Assert.That(selected.Capacity,Is.EqualTo(3));
            Assert.That(selected[0].ImpactTick,Is.EqualTo(1));
            foreach(var lane in selected)
            {
                Assert.That(lane.SourceRadius,Is.EqualTo(.2f));
                Assert.That(lane.Radius,Is.LessThanOrEqualTo(.30001f));
                Assert.That(Vector2.Distance(lane.Origin,lane.Target),Is.LessThanOrEqualTo(6.25001f));
            }
        }

        [Test] public void CollinearChainCannotGrowBeyondOriginalLengthBudget()
        {
            var selected=new List<ThreatCorridor>(3);
            for(var index=0;index<200;index++)
            {
                var lane=Lane(index,index+1,0);
                lane.Origin+=Vector2.right*(index*.1f); lane.Target+=Vector2.right*(index*.1f);
                ThreatCorridors.Consider(lane,selected);
            }
            foreach(var lane in selected)
            {
                Assert.That(lane.SourceLength,Is.EqualTo(5).Within(.00001f));
                Assert.That(Vector2.Distance(lane.Origin,lane.Target),Is.LessThanOrEqualTo(6.25001f));
            }
        }
    }
}
