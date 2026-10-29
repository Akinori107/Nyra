using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nyra.Tests;

public sealed class ConnectBounds
{
    private sealed class SilentListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public int Port { get; }

        public SilentListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(16);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener
                        .AcceptTcpClientAsync(_cts.Token)
                        .ConfigureAwait(false);
                    _ = client;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Dispose();
            _cts.Dispose();
        }
    }

    private sealed class ProbingListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public int Port { get; }

        public ProbingListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(16);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                using var _ = client;
                client.NoDelay = true;
                var stream = client.GetStream();
                var buffer = new byte[8192];
                while (!_cts.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                    if (read == 0)
                        return;
                    var text = Encoding.UTF8.GetString(buffer, 0, read);
                    if (text.Contains("/v4/info"))
                    {
                        var body =
                            """{"version":{"semver":"3.10.0"},"isNodelink":true,"plugins":[]}""";
                        var answer =
                            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "
                            + Encoding.UTF8.GetByteCount(body)
                            + "\r\nConnection: close\r\n\r\n"
                            + body;
                        await stream
                            .WriteAsync(Encoding.UTF8.GetBytes(answer), _cts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Dispose();
            _cts.Dispose();
        }
    }

    [Test]
    public async Task SecondStopStaysQuiet()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "twice",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        await Assert
            .That(async () => await node.DisconnectAsync().ConfigureAwait(false))
            .ThrowsNothing();
        await Assert.That(node.IsConnected).IsFalse();

        await Assert
            .That(async () => await node.DisconnectAsync().ConfigureAwait(false))
            .ThrowsNothing();
        await Assert.That(node.IsReady).IsFalse();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
