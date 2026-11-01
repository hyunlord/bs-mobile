using StackExchange.Redis;

namespace SowSiege.Server.Infrastructure;

// A disposable presence hint only. Authentication and account state always come from PostgreSQL.
public sealed class ConnectionCache(string connection) : IDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> client = new(() => ConnectionMultiplexer.ConnectAsync(connection + ",abortConnect=false,connectTimeout=500,asyncTimeout=500"));

    public async Task TouchAsync(Guid accountId)
    {
        try
        {
            var redis = await client.Value;
            await redis.GetDatabase().StringSetAsync("prototype:presence:" + accountId, "connected", TimeSpan.FromMinutes(1));
        }
        catch (RedisException)
        {
            // Redis loss cannot turn a committed account command into an HTTP failure.
        }
    }

    public void Dispose()
    {
        if (client.IsValueCreated && client.Value.IsCompletedSuccessfully)
        {
            client.Value.Result.Dispose();
        }
    }
}
