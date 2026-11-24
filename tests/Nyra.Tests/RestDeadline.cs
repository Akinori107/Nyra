using System.Text;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class RestDeadline
{
    [Test]
    [Arguments(200)]
    [Arguments(400)]
    public async Task ResponseHeadersAndBodyShareOneDeadline(int status)
    {
        using var server = new FakeNodeServer();
        var node = new Node(new Endpoint("127.0.0.1", server.Port, server.Password));
        await using var owned = node.ConfigureAwait(false);
        const string path = "/deadline";
        server.OnRaw("GET", path, async (_, stream) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), server.Stopping).ConfigureAwait(false);
            var body =
                status == 200
                    ? """{"resuming":true,"timeout":60}"""
                    : """{"message":"bad request"}""";
            var headers =
                $"HTTP/1.1 {status} Response\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n";
            await stream
                .WriteAsync(Encoding.UTF8.GetBytes(headers), server.Stopping)
                .ConfigureAwait(false);
            await stream.FlushAsync(server.Stopping).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2), server.Stopping).ConfigureAwait(false);
            await stream
                .WriteAsync(Encoding.UTF8.GetBytes(body), server.Stopping)
                .ConfigureAwait(false);
        });

        await Assert
            .That(async () =>
            {
                await node
                    .Rest.SendExpectingAsync<SessionConfig>(
                        HttpMethod.Get,
                        path,
                        "session response",
                        null,
                        TimeSpan.FromSeconds(3),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            })
            .Throws<TimeoutException>();
    }
}
