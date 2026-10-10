using System.Collections.Generic;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class ThreatCorridorTests
    {
        static ThreatCorridor Lane(int id, int impact, float y) => new ThreatCorridor
        { Id = id, ImpactTick = impact, TellTicks = 30, Origin = new Vector2(0, y), Target = new Vector2(5, y), Radius = .2f };

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
            var candidates=new List<ThreatCorridor>{Lane(1,10,0),Lane(2,20,.7f),Lane(3,30,3)};
            if(reverse)candidates.Reverse();
            var selected=new List<ThreatCorridor>(ThreatCorridors.VisibleLimit);
            foreach(var lane in candidates)ThreatCorridors.Consider(lane,selected);
            ThreatCorridors.Consider(Lane(4,15,.35f),selected);
            Assert.That(selected.Count,Is.EqualTo(2)); Assert.That(selected.Capacity,Is.EqualTo(3));
            Assert.That(selected[0].Id,Is.EqualTo(1)); Assert.That(selected[0].ImpactTick,Is.EqualTo(10));
            Assert.That(selected[1].Id,Is.EqualTo(3));
            Assert.That(ThreatCorridors.Contains(selected[0],new Vector2(3,.89f)),Is.True);
            ThreatCorridors.Consider(Lane(5,40,5),selected);
            Assert.That(selected.Count,Is.EqualTo(3)); Assert.That(selected[2].Id,Is.EqualTo(5));
        }
    }
}
