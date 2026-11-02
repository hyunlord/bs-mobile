using System.Text.Json;
using SowSiege.Server.Host;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldCombatViewTests
{
    [Fact]
    public void ResponseProjectionCannotExposeDefenderPersistenceSnapshot()
    {
        var run = new WorldCombatRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test:target", 1,
            "test:encounter", "active", 2, 3000, null, [], new(1000, 1000, 0, 0), null, [])
        {
            DefenderVersion = 713,
            DefenderDomains = "private-other-account-research-and-runs",
            DefenderResult = []
        };
        var view = WorldCombatView.From(run);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.TryGetProperty("defenderDomains", out _));
        Assert.False(json.RootElement.TryGetProperty("defenderVersion", out _));
        Assert.False(json.RootElement.TryGetProperty("defenderResult", out _));
        Assert.DoesNotContain(run.DefenderDomains!, json.RootElement.GetRawText());
        Assert.Equal(run.RunId, view.RunId);
        Assert.Equal(run.Revision, view.Revision);
        Assert.Equal(run.Frontier, view.Frontier);
    }
}
