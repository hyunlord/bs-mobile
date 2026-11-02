using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public static class GatheringServices
{
    public static IServiceCollection AddGathering(this IServiceCollection services) => services
        .AddSingleton(provider => GatheringContent.Load(provider.GetRequiredService<IConfiguration>()["GameContentRoot"]!))
        .AddSingleton<GatheringStore>();

    public static Task InitializeGatheringAsync(this WebApplication app) =>
        app.Services.GetRequiredService<GatheringStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
}
