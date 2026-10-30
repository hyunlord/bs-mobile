using System.Text.Json;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class RunSettlementTests
{
    private const string Proof = "slg:proof-field-boundary";
    private static readonly byte[] Key = Convert.FromHexString(RecruitTests.TestKeyHex);
    private static readonly Lazy<RunVerifier> SharedVerifier = new(() => Verifier());

    internal static RunVerifier Verifier(int maxBytes = 4 * 1024 * 1024) =>
        new(new(Path.Combine(ContentEstateTests.Root(), "data"), "production", maxBytes, TimeSpan.FromSeconds(120), 1));

    /// <summary>Records a normal-input replay with the server-issued options, ending by quit after <paramref name="ticks"/> or at the profile end.</summary>
    internal static (byte[] Bytes, ReplayEnd End) Record(RunVerifier verifier, int seed, int ticks, Func<ReplayCommand, ReplayCommand>? tamper = null)
    {
        string root = Path.Combine(ContentEstateTests.Root(), "data");
        var catalog = SowSiege.Sim.ContentLoader.Load(root, profileName: "production");
        var options = verifier.ExpectedOptions(seed);
        var session = new InteractiveSession(catalog, options);
        using var output = new MemoryStream();
        ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
        int tick = 0;
        void Send(ReplayCommandKind kind, PlayerInput input = default, string? card = null)
        {
            var command = new ReplayCommand(session.NextSequence, tick, kind, input, card);
            command = tamper?.Invoke(command) ?? command;
            session.Apply(command);
            ReplayCodec.WriteCommand(output, command);
        }
        while (session.View.Status != RunStatus.Completed && tick < ticks)
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                var cards = session.View.CaptureCards();
                Send(ReplayCommandKind.ChooseCard, card: cards.Cards[(seed + tick) % cards.Cards.Count]);
                continue;
            }
            int phase = tick / 120 % 4;
            Send(ReplayCommandKind.Advance, phase switch { 0 => new PlayerInput(1000, 0), 1 => new PlayerInput(0, 1000), 2 => new PlayerInput(-1000, 0), _ => new PlayerInput(0, -1000) });
            tick++;
        }
        var summary = session.GetSummary();
        var kind = summary.EndReason == "death" ? ReplayEndKind.Death : summary.EndReason == "duration" ? ReplayEndKind.Duration : ReplayEndKind.Quit;
        var end = new ReplayEnd(session.NextSequence, summary.Tick, kind, summary.StateHash);
        ReplayCodec.WriteEnd(output, end);
        return (output.ToArray(), end);
    }

    private static (ResearchDomain Handler, DomainContext Context) Handler(long utc = 1_000_000)
    {
        var estate = EstateContent.Load(ContentEstateTests.Root());
        var handler = new ResearchDomain(ResearchContent.Load(ContentEstateTests.Root()), new(60_000, 3, estate.Config.WalletCaps));
        var account = new EstateApplication(estate).Create(0);
        return (handler, new DomainContext(account, handler.InitialState, utc, Guid.NewGuid(), Key));
    }

    private static JsonElement Payload(object value) => JsonSerializer.SerializeToElement(value);

    private static ExpeditionRun Run(DomainResult result) => result.State.GetProperty("runs").Deserialize<ExpeditionRun[]>(AccountStore.Json)!.Last();

    [Fact]
    public void VerifiedSurvivalGrantsProofAndCappedMaterialsOnce()
    {
        var (handler, context) = Handler();
        var issued = handler.Execute(context, "start-expedition", Payload(new { proofId = Proof, resource = "meta:grain" }));
        Assert.Equal((true, "issued"), (issued.Done, issued.Reason));
        var run = Run(issued);
        Assert.Equal((context.CommandId!.Value, ExpeditionRules.SeedOf(Key), "open"), (run.RunId, run.Seed, run.Status));
        Assert.Equal("run-open", handler.Execute(context with { State = issued.State }, "start-expedition", Payload(new { proofId = Proof, resource = "meta:grain" })).Reason);
        Assert.Equal("unknown-proof", handler.Execute(context, "start-expedition", Payload(new { proofId = "slg:proof-made-up", resource = "meta:grain" })).Reason);

        var after = context with { State = issued.State, UtcMilliseconds = context.UtcMilliseconds + 1000 };
        var verdict = Payload(new { runId = run.RunId, verdict = "verified", endKind = "Duration", tick = 21600, stateHash = new string('a', 64), replayHash = new string('b', 64) });
        var settled = handler.Execute(after, ExpeditionRules.SettleOperation, verdict);
        Assert.Equal((true, "objective-complete"), (settled.Done, settled.Reason));
        Assert.Equal(context.Account.State.Wallet["meta:grain"] + 60, settled.AccountState.Wallet["meta:grain"]);
        Assert.Contains(Proof, settled.State.GetProperty("proofs").EnumerateArray().Select(p => p.GetString()));
        var again = handler.Execute(after with { State = settled.State, Account = after.Account with { State = settled.AccountState } }, ExpeditionRules.SettleOperation, verdict);
        Assert.Equal((false, "already-settled"), (again.Done, again.Reason));
        Assert.Equal(settled.AccountState.Wallet["meta:grain"], again.AccountState.Wallet["meta:grain"]);
    }

    [Fact]
    public void RewardIsCappedByDataAndWalletAndDeathGrantsNothing()
    {
        var (handler, context) = Handler();
        var full = context.Account with { State = context.Account.State with { Wallet = new(context.Account.State.Wallet) { ["meta:grain"] = 1990 } } };
        var issued = handler.Execute(context with { Account = full }, "start-expedition", Payload(new { proofId = Proof, resource = "meta:grain" }));
        var run = Run(issued);
        var settled = handler.Execute(context with { Account = full, State = issued.State }, ExpeditionRules.SettleOperation,
            Payload(new { runId = run.RunId, verdict = "verified", endKind = "Duration", tick = 1, stateHash = "x", replayHash = "y" }));
        Assert.Equal(2000, settled.AccountState.Wallet["meta:grain"]);
        Assert.Equal(10, Run(settled).Granted);

        var second = Handler();
        var issued2 = second.Handler.Execute(second.Context, "start-expedition", Payload(new { proofId = Proof, resource = "meta:timber" }));
        var died = second.Handler.Execute(second.Context with { State = issued2.State }, ExpeditionRules.SettleOperation,
            Payload(new { runId = Run(issued2).RunId, verdict = "verified", endKind = "Death", tick = 10, stateHash = "x", replayHash = "y" }));
        Assert.Equal((true, "objective-failed", 0), (died.Done, died.Reason, Run(died).Granted));
        Assert.Empty(died.State.GetProperty("proofs").EnumerateArray());
        Assert.Equal(second.Context.Account.State.Wallet["meta:timber"], died.AccountState.Wallet["meta:timber"]);
    }

    [Fact]
    public void FailedVerificationsUseAttemptsAndExpiryClosesTheRun()
    {
        var (handler, context) = Handler();
        var state = handler.Execute(context, "start-expedition", Payload(new { proofId = Proof, resource = "meta:iron" })).State;
        var runId = Run(new(context.Account.State, state, state, true, "", [])).RunId;
        var bad = Payload(new { runId, verdict = "mismatch", endKind = (string?)null, tick = 0, stateHash = (string?)null, replayHash = "r" });
        var timeout = handler.Execute(context with { State = state }, ExpeditionRules.SettleOperation, Payload(new { runId, verdict = "timeout", tick = 0, replayHash = "r" }));
        Assert.Equal("verify-timeout", timeout.Reason);
        for (int i = 1; i <= 3; i++)
        {
            var result = handler.Execute(context with { State = state }, ExpeditionRules.SettleOperation, bad);
            state = result.State;
            Assert.Equal(i < 3 ? "open" : "failed", Run(result).Status);
        }
        Assert.Equal("already-failed", handler.Execute(context with { State = state }, ExpeditionRules.SettleOperation, bad).Reason);

        var (fresh, start) = Handler();
        var open = fresh.Execute(start, "start-expedition", Payload(new { proofId = Proof, resource = "meta:iron" }));
        var late = fresh.Execute(start with { State = open.State, UtcMilliseconds = start.UtcMilliseconds + 60_001 }, ExpeditionRules.SettleOperation,
            Payload(new { runId = Run(open).RunId, verdict = "verified", endKind = "Duration", tick = 1, stateHash = "x", replayHash = "y" }));
        Assert.Equal(("run-expired", "expired"), (late.Reason, Run(late).Status));
    }

    [Fact]
    public async Task VerifierReExecutesNormalReplaysAndRejectsTampering()
    {
        var verifier = SharedVerifier.Value;
        var runId = Guid.NewGuid();
        var (bytes, end) = Record(verifier, 4242, 600);
        var verified = await verifier.VerifyAsync(runId, 4242, bytes, CancellationToken.None);
        Assert.Equal(("verified", end.Kind.ToString(), end.Tick, end.StateHash), (verified.Verdict, verified.EndKind, verified.Tick, verified.StateHash));
        Assert.Equal("options-mismatch", (await verifier.VerifyAsync(runId, 4243, bytes, CancellationToken.None)).Verdict);
        var (debug, _) = Record(verifier, 4242, 50, c => c.Sequence == 0 ? c with { Kind = ReplayCommandKind.SetInvulnerable, Value = 1 } : c);
        Assert.Equal("debug-command", (await verifier.VerifyAsync(runId, 4242, debug, CancellationToken.None)).Verdict);
        var forged = (byte[])bytes.Clone();
        forged[^5] ^= 0x01; // inside the terminal state hash text
        Assert.Contains((await verifier.VerifyAsync(runId, 4242, forged, CancellationToken.None)).Verdict, new[] { "mismatch", "malformed" });
        Assert.Equal("malformed", (await verifier.VerifyAsync(runId, 4242, [1, 2, 3], CancellationToken.None)).Verdict);
        Assert.Equal("too-large", (await Verifier(maxBytes: 16).VerifyAsync(runId, 4242, bytes, CancellationToken.None)).Verdict);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task SubmittedReplaySettlesOnceThroughTheAccountWriter()
    {
        await using var fixture = await SlgFixture.CreateAsync(Key);
        var verifier = SharedVerifier.Value;
        long version = (await fixture.ViewAsync()).Estate.Version;
        var start = new AccountCommand(Guid.NewGuid(), version, ResearchDomain.Name, "start-expedition", Payload(new { proofId = Proof, resource = "meta:grain" }));
        var issued = SlgFixture.Parse(await fixture.Store.ExecuteAccountAsync(fixture.Id, start, CancellationToken.None));
        Assert.Equal("issued", issued.Reason);
        var run = issued.Domains[ResearchDomain.Name].GetProperty("runs")[0].Deserialize<ExpeditionRun>(AccountStore.Json)!;
        Assert.Equal(start.CommandId, run.RunId);
        int grain = issued.Resources.Single(r => r.Id == "meta:grain").Amount;

        var (bytes, end) = Record(verifier, run.Seed, 30_000);
        var settled = SlgFixture.Parse(await verifier.SubmitAsync(fixture.Store, fixture.Id, run.RunId, bytes, CancellationToken.None));
        bool survived = end.Kind == ReplayEndKind.Duration;
        Assert.Equal((true, survived ? "objective-complete" : "objective-failed"), (settled.Done, settled.Reason));
        Assert.Equal(grain + (survived ? 60 : 0), settled.Resources.Single(r => r.Id == "meta:grain").Amount);
        Assert.Equal(survived, settled.Domains[ResearchDomain.Name].GetProperty("proofs").EnumerateArray().Any(p => p.GetString() == Proof));

        // Duplicate and concurrent resubmissions never grant twice.
        var again = await Task.WhenAll(verifier.SubmitAsync(fixture.Store, fixture.Id, run.RunId, bytes, CancellationToken.None),
            verifier.SubmitAsync(fixture.Store, fixture.Id, run.RunId, bytes, CancellationToken.None));
        Assert.All(again, json => Assert.Equal("already-settled", SlgFixture.Parse(json).Reason));
        Assert.Single(await fixture.EventsAsync("expedition-settled"));
        Assert.Equal(grain + (survived ? 60 : 0), (await fixture.ViewAsync()).Resources.Single(r => r.Id == "meta:grain").Amount);
        await Assert.ThrowsAsync<CommandConflictException>(() => verifier.SubmitAsync(fixture.Store, fixture.Id, Guid.NewGuid(), bytes, CancellationToken.None));
    }
}
