using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SowSiege.Server.Host;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class SocialTransportTests
{
    [Fact]
    public async Task ProductionProviderTransportDoesNotFollowCredentialRedirects()
    {
        await using var services = new ServiceCollection().AddLogging().AddSocialAuth().BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("social-auth");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        async Task Redirect()
        {
            using var accepted = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = accepted.GetStream();
            var buffer = new byte[4096];
            int bytes = await stream.ReadAsync(buffer, deadline.Token);
            Assert.True(bytes > 0);
            byte[] reply = Encoding.ASCII.GetBytes("HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:1/credential-sink\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(reply, deadline.Token);
        }
        Task server = Redirect();
        using var response = await client.PostAsync($"http://127.0.0.1:{port}/provider", new StringContent("synthetic-credential"), deadline.Token);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        await server;
    }
}
