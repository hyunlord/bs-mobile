using System.Text.Json.Nodes;
using System.Text;
using SowSiege.Core;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class ContentEstateTests
{
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "data", "meta")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository data missing.");
    }

    [Fact]
    public void NewAccountsUseExplicitR8AndInternalCodecRoundTrips()
    {
        var content = EstateContent.Load(Root());
        var state = AccountEstate.NewPrototype(content.Meta, content.Config, 0, 0);
        Assert.Equal("m3a-prototype-r8", state.Estate!.ConfigVersion);
        Assert.Equal(6, state.SchemaVersion);
        var bytes = MetaSaveCodec.Encode(state);
        Assert.Equal(bytes, MetaSaveCodec.Encode(MetaSaveCodec.Decode(bytes).State!));
        Assert.Empty(MetaValidation.ValidateState(content.Meta, state));
    }

    [Fact]
    public void RejectedCommandStillContainsAdvancedClock()
    {
        var content = EstateContent.Load(Root());
        var state = AccountEstate.NewPrototype(content.Meta, content.Config, 0, 0);
        var rejected = AccountEstate.StartConstruction(content.Meta, content.Config, state, 7, 7, int.MaxValue);
        Assert.False(rejected.Done);
        Assert.Equal("unknown-building", rejected.Reason);
        Assert.Equal(7, rejected.State.Estate!.LastMonotonicSeconds);
    }
    [Fact]
    public void AuthorityRejectsStaleVersionsAndChangedContentBeforeAdvancing()
    {
        var application = new EstateApplication(EstateContent.Load(Root()));
        var account = application.Create(1000);
        var stale = new SowSiege.Server.Contracts.EstateCommand(Guid.NewGuid(), 8, "claim-production", null);
        Assert.Throws<CommandConflictException>(() => application.Apply(account, stale, 100000));
        Assert.Throws<CommandConflictException>(() => application.Apply(account with { ContentHash = "different" }, null, 100000));
        Assert.Equal(0, account.Version);
        Assert.Equal(0, account.Clock.LogicalSeconds);
    }
    [Theory]
    [InlineData("contractVersion")]
    [InlineData("clockToleranceSeconds")]
    public void TamperedCatalogFailsBeforeAccountCreation(string field)
    {
        var meta = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Root(), "data", "meta", "progression.json")))!;
        if (field == "contractVersion") { meta[field] = 99; }
        else { meta["economy"]![field] = -1; }
        byte[] estate = File.ReadAllBytes(Path.Combine(Root(), "data", "opt-in", "account-estate", "m3a-prototype-r8.json"));
        Assert.Throws<InvalidDataException>(() => EstateContent.Parse(Encoding.UTF8.GetBytes(meta.ToJsonString()), estate));
    }
}
