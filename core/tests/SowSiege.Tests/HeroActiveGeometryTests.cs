using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroActiveGeometryTests
{
    [Theory]
    [InlineData(0, 100, 49)]
    [InlineData(100, 0, 50)]
    public void ThinWallStopsAtFirstPlaneEvenWhenBothEndpointsAreOutside(int start, int end, int contact)
    {
        var from = new Position(start, 10); var to = new Position(end, 10);
        Assert.True(HeroActiveGeometry.TryHitBox(from, to, 49, 0, 50, 20, out var hit));
        Assert.Equal(new Position(contact, 10), HeroActiveGeometry.BeforeHit(from, to, hit));
    }

    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(10, 21, false)]
    [InlineData(0, 10, true)]
    public void WallTangencyAndEndpointUseClosedBounds(int startY, int endY, bool expected)
    {
        Assert.Equal(expected, HeroActiveGeometry.TryHitBox(new(0, startY), new(10, endY), 10, 10, 20, 20, out _));
    }

    [Fact]
    public void StartingOnWallAllowsDepartureAndTangentialMotionButBlocksEntry()
    {
        Assert.False(HeroActiveGeometry.TryHitBox(new(10, 15), new(0, 15), 10, 10, 20, 20, out _));
        Assert.False(HeroActiveGeometry.TryHitBox(new(10, 15), new(10, 16), 10, 10, 20, 20, out _));
        Assert.True(HeroActiveGeometry.TryHitBox(new(10, 15), new(11, 15), 10, 10, 20, 20, out var hit));
        Assert.Equal(0, hit.Numerator);
        Assert.Equal(new Position(10, 15), HeroActiveGeometry.BeforeHit(new(10, 15), new(11, 15), hit));
    }

    [Theory]
    [InlineData(50, 0, 0, true)]
    [InlineData(50, 10, 10, true)]
    [InlineData(50, 11, 10, false)]
    [InlineData(110, 0, 10, true)]
    [InlineData(-10, 0, 10, true)]
    [InlineData(111, 0, 10, false)]
    public void SweptCircleIncludesMidsegmentTangentAndEndpoint(int x, int y, int radius, bool expected)
    {
        Assert.Equal(expected, HeroActiveGeometry.IntersectsCircle(new(0, 0), new(100, 0), new(x, y), radius));
    }

    [Fact]
    public void WallClippedSegmentCannotReachEnemyBehindWall()
    {
        var from = new Position(0, 0); var proposed = new Position(100, 0);
        Assert.True(HeroActiveGeometry.TryHitBox(from, proposed, 40, -10, 41, 10, out var hit));
        var actual = HeroActiveGeometry.BeforeHit(from, proposed, hit);
        Assert.False(HeroActiveGeometry.IntersectsCircle(from, actual, new(60, 0), 5));
        Assert.True(HeroActiveGeometry.IntersectsCircle(from, actual, new(20, 0), 5));
    }

    [Fact]
    public void ZeroLengthSegmentDoesNotInventMotionOrMissStartingOverlap()
    {
        Assert.True(HeroActiveGeometry.IntersectsCircle(new(10, 10), new(10, 10), new(10, 15), 5));
        Assert.False(HeroActiveGeometry.IntersectsCircle(new(10, 10), new(10, 10), new(10, 16), 5));
    }

    [Fact]
    public void MaximumCoordinateDiagonalUsesExactArithmeticWithoutCrossProductOverflow()
    {
        Assert.True(HeroActiveGeometry.IntersectsCircle(new(-4000000, -4000000), new(4000000, 4000000), new(0, 1), 1));
        Assert.False(HeroActiveGeometry.IntersectsCircle(new(-4000000, -4000000), new(4000000, 4000000), new(0, 2), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeroActiveGeometry.IntersectsCircle(new(-4000001, 0), new(0, 0), new(0, 0), 1));
    }

    [Fact]
    public void RationalContactOrderingDoesNotOverflowOrMergeDifferentFractions()
    {
        Assert.True(HeroActiveGeometry.CompareFractions(long.MaxValue - 1, long.MaxValue, long.MaxValue - 2, long.MaxValue - 1) > 0);
        Assert.Equal(0, HeroActiveGeometry.CompareFractions(1, 3, 2, 6));
    }

    [Fact]
    public void BattlefieldExitIncludesExactBoundaryAndClipsBothAxesAtFirstContact()
    {
        Assert.True(HeroActiveGeometry.TryExitBox(new(50, 50), new(100, 75), 0, 0, 100, 100, out var edge));
        Assert.Equal(new Position(100, 75), HeroActiveGeometry.BeforeHit(new(50, 50), new(100, 75), edge));
        Assert.True(HeroActiveGeometry.TryExitBox(new(50, 50), new(150, 100), 0, 0, 100, 100, out var outside));
        Assert.Equal(new Position(100, 75), HeroActiveGeometry.BeforeHit(new(50, 50), new(150, 100), outside));
    }

    [Fact]
    public void CircleContactStopsBeforeBodyButAllowsBoundaryDeparture()
    {
        Assert.True(HeroActiveGeometry.TryHitCircle(new(0, 0), new(10, 0), new(5, 0), 1, out var hit));
        Assert.Equal(new Position(3, 0), HeroActiveGeometry.BeforeHit(new(0, 0), new(10, 0), hit));
        Assert.False(HeroActiveGeometry.TryHitCircle(new(5, 0), new(6, 0), new(0, 0), 5, out _));
        Assert.False(HeroActiveGeometry.TryHitCircle(new(5, 0), new(5, 1), new(0, 0), 5, out _));
        Assert.True(HeroActiveGeometry.TryHitCircle(new(5, 0), new(4, 0), new(0, 0), 5, out var inward));
        Assert.Equal(0, inward.Numerator);
        Assert.True(HeroActiveGeometry.IntersectsCircle(new(5, 0), new(6, 0), new(0, 0), 5));
    }
}
