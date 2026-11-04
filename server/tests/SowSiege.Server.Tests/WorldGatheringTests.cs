using System.Text.Json;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldGatheringTests
{
    private static readonly Guid March = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static GatheringNode Node(int stock = 600, int rate = 1, int capacity = 10) =>
        WorldGathering.Create("slg:resource-1", 1, new(stock, rate, capacity, 600_000), 0);
    private static GatheringNode Claimed(int stock = 600, int rate = 1, int capacity = 10, int soldiers = 1) =>
        WorldGathering.Claim(Node(stock, rate, capacity), 1, March, 1, soldiers, 0).State;

    [Theory]
    [InlineData(0, 1, 10, 1)]
    [InlineData(1, 0, 10, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 10, 0)]
    public void PolicyRequiresPositiveContentQuantities(int stock, int rate, int capacity, long respawn)
    {
        Assert.Throws<InvalidDataException>(() => WorldGathering.Create("slg:node", 1, new(stock, rate, capacity, respawn), 0));
    }

    [Fact]
    public void FirstConfirmedArrivalOwnsLeaseAndRejectedArrivalCannotRetry()
    {
        var first = Claimed();
        var rejected = WorldGathering.Claim(first, 1, Other, 2, 1, 0);
        Assert.False(rejected.Done);
        Assert.Equal("occupied", rejected.Reason);
        Assert.Equal(March, rejected.State.Lease!.MarchId);
        var released = WorldGathering.Cancel(rejected.State, 1, March, 1, 60_000);
        var replay = WorldGathering.Claim(released.State, 1, Other, 2, 1, 60_000);
        Assert.False(replay.Done);
        Assert.Null(replay.State.Lease);
        Assert.Null(replay.Completion);
    }

    [Fact]
    public void ActiveArrivalRetryIsIdempotentAndCannotChangeSoldiers()
    {
        var state = Claimed();
        var retry = WorldGathering.Claim(state, 1, March, 1, 1, 20_000);
        Assert.True(retry.Done);
        Assert.Equal(state, retry.State);
        Assert.False(WorldGathering.Claim(state, 1, March, 1, 2, 20_000).Done);
    }

    [Fact]
    public void FractionalProductionSurvivesPartitionedObservations()
    {
        var initial = Claimed();
        var partial = WorldGathering.Advance(initial, 59_999).State;
        Assert.Equal(0, partial.Lease!.Cargo);
        partial = WorldGathering.Advance(partial, 60_000).State;
        Assert.Equal(1, partial.Lease!.Cargo);
        partial = WorldGathering.Advance(partial, 120_001).State;
        Assert.Equal(WorldGathering.Advance(initial, 120_001).State, partial);
        Assert.Equal(600, partial.RemainingStock + partial.ConfirmedGathered);
    }

    [Fact]
    public void CapacityCompletesAtDeadlineEvenWhenObservedMuchLater()
    {
        var result = WorldGathering.Advance(Claimed(), long.MaxValue);
        Assert.Equal(10, result.Completion!.Cargo);
        Assert.Equal(600_000, result.Completion.AtMs);
        Assert.Equal("capacity-reached", result.Completion.Reason);
        Assert.Equal(590, result.State.RemainingStock);
        Assert.Equal(10, result.State.ConfirmedGathered);
        Assert.Null(result.State.RespawnAtMs);
        Assert.Null(result.State.Lease);
        Assert.Null(WorldGathering.Advance(result.State, long.MaxValue).Completion);
    }

    [Fact]
    public void DepletionUsesExactDeadlineAndStartsRespawnFromThatInstant()
    {
        var state = Claimed(stock: 3, soldiers: 2);
        Assert.Equal(90_000, state.Lease!.DueAtMs);
        Assert.Null(WorldGathering.Advance(state, 89_999).Completion);
        var result = WorldGathering.Advance(state, 200_000);
        Assert.Equal(3, result.Completion!.Cargo);
        Assert.Equal(90_000, result.Completion.AtMs);
        Assert.Equal(690_000, result.State.RespawnAtMs);
        Assert.Equal(3, result.State.ConfirmedGathered);
    }

    [Fact]
    public void NonIntegralDeadlineRoundsUpAndNeverFinishesEarly()
    {
        var state = Claimed(stock: 1, rate: 7);
        Assert.Equal(8_572, state.Lease!.DueAtMs);
        Assert.Null(WorldGathering.Advance(state, 8_571).Completion);
        Assert.Equal(1, WorldGathering.Advance(state, 8_572).Completion!.Cargo);
    }

    [Theory]
    [InlineData(59_999, 0)]
    [InlineData(60_000, 1)]
    [InlineData(90_000, 1)]
    public void CancellationReleasesOnlyIntegralEarnedCargo(long now, int expected)
    {
        var result = WorldGathering.Cancel(Claimed(), 1, March, 1, now);
        Assert.Equal(expected, result.Completion!.Cargo);
        Assert.Equal(now, result.Completion.AtMs);
        Assert.Equal("cancelled", result.Reason);
        Assert.Equal(600 - expected, result.State.RemainingStock);
        Assert.Equal(expected, result.State.ConfirmedGathered);
        Assert.Null(result.State.Lease);
        Assert.Null(WorldGathering.Cancel(result.State, 1, March, 1, now).Completion);
    }

    [Theory]
    [InlineData(600_000)]
    [InlineData(900_000)]
    public void DeadlineWinsOverCancellation(long now)
    {
        var result = WorldGathering.Cancel(Claimed(), 1, March, 1, now);
        Assert.Equal("capacity-reached", result.Reason);
        Assert.Equal(600_000, result.Completion!.AtMs);
    }

    [Fact]
    public void RespawnBoundaryPreservesArrivalWatermarkAndRetiredConservation()
    {
        var depleted = WorldGathering.Advance(Claimed(stock: 1), 60_000).State;
        Assert.False(WorldGathering.Respawn(depleted, 1, 659_999).Done);
        var result = WorldGathering.Respawn(depleted, 1, 660_000);
        Assert.True(result.Done);
        Assert.Equal(2, result.State.Generation);
        Assert.Equal(1, result.State.RemainingStock);
        Assert.Equal(0, result.State.ConfirmedGathered);
        Assert.Equal(1, result.State.LastArrivalOrder);
        Assert.Equal(result.RetiredGeneration!.CreatedStock, result.RetiredGeneration.ConfirmedGathered);
        Assert.False(WorldGathering.Claim(result.State, 2, March, 1, 1, 660_000).Done);
        Assert.False(WorldGathering.Respawn(result.State, 1, 660_000).Done);
    }

    [Fact]
    public void DelayedRespawnReturnsBothCompletionAndRetiredGenerationOnce()
    {
        var result = WorldGathering.Respawn(Claimed(stock: 1), 1, 1_000_000);
        Assert.Equal(60_000, result.Completion!.AtMs);
        Assert.Equal(1, result.Completion.Cargo);
        Assert.NotNull(result.RetiredGeneration);
        var retry = WorldGathering.Respawn(result.State, 1, 1_000_000);
        Assert.Null(retry.Completion);
        Assert.Null(retry.RetiredGeneration);
        Assert.Equal(result.State, retry.State);
    }

    [Fact]
    public void SerializedRestartPreservesFractionAndDeadline()
    {
        var partial = WorldGathering.Advance(Claimed(), 99_999).State;
        var restored = JsonSerializer.Deserialize<GatheringNode>(JsonSerializer.Serialize(partial))!;
        WorldGathering.Validate(restored);
        Assert.Equal(partial, restored);
        Assert.Equal(WorldGathering.Advance(partial, 600_000), WorldGathering.Advance(restored, 600_000));
    }

    [Fact]
    public void BackwardsClockNeverReversesProductionOrHighWatermark()
    {
        var partial = WorldGathering.Advance(Claimed(), 120_000).State;
        Assert.Equal(partial, WorldGathering.Advance(partial, 60_000).State);
        var cancelled = WorldGathering.Cancel(partial, 1, March, 1, 60_000);
        Assert.Equal(120_000, cancelled.Completion!.AtMs);
        Assert.Equal(2, cancelled.Completion.Cargo);
    }

    [Fact]
    public void CompletedMarchCannotTakeStockAgainAndConfirmedTotalPersists()
    {
        var first = WorldGathering.Advance(Claimed(stock: 15), 600_000);
        Assert.False(WorldGathering.Claim(first.State, 1, March, 1, 1, 600_000).Done);
        var second = WorldGathering.Claim(first.State, 1, Other, 2, 1, 600_000);
        var complete = WorldGathering.Advance(second.State, 900_000);
        Assert.Equal(15, first.Completion!.Cargo + complete.Completion!.Cargo);
        Assert.Equal(15, complete.State.ConfirmedGathered);
        Assert.Equal(0, complete.State.RemainingStock);
    }

    [Fact]
    public void ClaimAtDeadlineCarriesPreviousCompletionIntoNewLease()
    {
        var result = WorldGathering.Claim(Claimed(), 1, Other, 2, 1, 600_000);
        Assert.Equal(March, result.Completion!.MarchId);
        Assert.Equal(Other, result.State.Lease!.MarchId);
        Assert.Equal(600_000, result.State.Lease.StartedAtMs);
        Assert.Equal(10, result.State.ConfirmedGathered);
    }

    [Fact]
    public void StaleGenerationAndForeignCancellationLeaveStateUntouched()
    {
        var state = Claimed();
        Assert.Equal(state, WorldGathering.Claim(state, 2, Other, 2, 1, 60_000).State);
        Assert.Equal(state, WorldGathering.Cancel(state, 1, Other, 1, 60_000).State);
        Assert.Equal(state, WorldGathering.Cancel(state, 1, March, 2, 60_000).State);
    }

    [Fact]
    public void CorruptLedgerAndDeadlineAreRejectedAfterRestoration()
    {
        var state = Claimed();
        Assert.Throws<InvalidDataException>(() => WorldGathering.Validate(state with { RemainingStock = 599 }));
        Assert.Throws<InvalidDataException>(() => WorldGathering.Validate(state with { Lease = state.Lease! with { DueAtMs = 1 } }));
        Assert.Throws<InvalidDataException>(() => WorldGathering.Validate(state with { UtcHighWatermark = 600_000 }));
        Assert.Throws<InvalidDataException>(() => WorldGathering.Validate(state with
        {
            UtcHighWatermark = 120_000, RemainingStock = 599, ConfirmedGathered = 1,
            Lease = state.Lease! with { Cargo = 1 }
        }));
    }
}
