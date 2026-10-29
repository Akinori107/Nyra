using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using Nyra.Abstractions;
using Nyra.Exceptions;

namespace Nyra.Tests;

internal sealed record FakeRestCall(
    string Method,
    string Path,
    string? Query,
    string? Body,
    string? Authorization
);

internal sealed record FakeRestResponse(int Status, string Body, string? RetryAfter = null);

internal sealed class GaugeRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly Dictionary<(string Node, string Instrument), double> _values = [];

    public GaugeRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Nyra")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<int>(
            (instrument, value, tags, _) => Record(instrument, value, tags)
        );
        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => Record(instrument, value, tags)
        );
        _listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => Record(instrument, value, tags)
        );
        _listener.Start();
    }

    public Dictionary<string, double> Read(string node)
    {
        _values.Clear();
        _listener.RecordObservableInstruments();
        return _values
            .Where(pair => pair.Key.Node == node)
            .ToDictionary(pair => pair.Key.Instrument, pair => pair.Value);
    }

    public void Dispose() => _listener.Dispose();

    private void Record(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags
    )
    {
        foreach (var tag in tags)
            if (tag.Key == "node" && tag.Value is string node)
                _values[(node, instrument.Name)] = value;
    }
}

internal sealed class RecordingList<T> : IEnumerable<T>
{
    private readonly List<T> _items = [];

    public void Add(T item)
    {
        lock (_items)
            _items.Add(item);
    }

    public void Clear()
    {
        lock (_items)
            _items.Clear();
    }

    public int Count
    {
        get
        {
            lock (_items)
                return _items.Count;
        }
    }

    public bool Contains(T item)
    {
        lock (_items)
            return _items.Contains(item);
    }

    public T[] Snapshot()
    {
        lock (_items)
            return [.. _items];
    }

    public int FindIndex(Predicate<T> match) => Array.FindIndex(Snapshot(), match);

    public int FindLastIndex(Predicate<T> match)
    {
        var snapshot = Snapshot();
        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            if (match(snapshot[i]))
                return i;
        }
        return -1;
    }

    public IEnumerator<T> GetEnumerator() => Snapshot().AsEnumerable().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

internal class FakeBridge : IDiscordBridge, IGuildRemovalSource, IAsyncDisposable
{
    public const ulong UserId = 424242424242424242;

    private TaskCompletionSource<ClientInformation> _ready = CreateReady();

    private static TaskCompletionSource<ClientInformation> CreateReady() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RecordingList<(ulong GuildId, ulong? ChannelId)> VoiceSends { get; } = new();

    private readonly TaskCompletionSource<bool> _neverCompletes = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    public ILogger? Logger { get; set; }

    public void FailWaits()
    {
        _ready.TrySetCanceled();
        _ready = CreateReady();
    }

    public Task<ClientInformation> WaitForReadyAsync(
        CancellationToken cancellationToken = default
    ) => _ready.Task.WaitAsync(cancellationToken);

    public bool FaultVoiceSends { get; set; }

    public bool HangVoiceSends { get; set; }

    public virtual Task SendVoiceUpdateAsync(
        ulong guildId,
        ulong? channelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default
    )
    {
        if (FaultVoiceSends)
            throw new InvalidOperationException("the fake bridge refuses the send");
        VoiceSends.Add((guildId, channelId));
        if (HangVoiceSends)
            return _neverCompletes.Task;
        return Task.CompletedTask;
    }

    public void FireVoiceState(ulong guildId, ulong? channelId, string? sessionId) =>
        VoiceStateUpdated?.Invoke(
            this,
            new VoiceStateUpdatedEventArgs(guildId, new DiscordVoiceState(channelId, sessionId))
        );

    public void FireVoiceServer(ulong guildId, string token, string endpoint) =>
        VoiceServerUpdated?.Invoke(
            this,
            new VoiceServerUpdatedEventArgs(guildId, new VoiceServer(token, endpoint))
        );

    public ClientInformation Identification { get; set; } = new(UserId);

    public void GoReady() => _ready.TrySetResult(Identification);

    public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

    public void FireGuildRemoved(ulong guildId) =>
        GuildRemoved?.Invoke(this, new GuildRemovedEventArgs(guildId));

    public ValueTask DisposeAsync()
    {
        FailWaits();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeNodeServer : IDisposable
{
    private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    private readonly ConcurrentDictionary<string, Func<FakeRestCall, FakeRestResponse>> _handlers =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Func<FakeRestCall, Stream, Task>> _rawHandlers =
        new(StringComparer.Ordinal);

    public string Password { get; init; } = "youshallnotpass";

    public bool IsNodeLink { get; init; } = true;

    public string Semver { get; init; } = "3.9.1";

    public string SessionId { get; init; } = "fake-session";

    public bool ResumeOnReconnect { get; set; } = true;

    public TimeSpan HandshakeDelay { get; set; } = TimeSpan.Zero;

    public bool GhostHandshakes { get; set; }

    public bool IgnoreWebsocketClose { get; set; }

    public bool GhostProbes { get; set; }

    public bool FailWebsocketHandshakes { get; set; }

    public bool CloseAfterHandshake { get; set; }

    public string? InfoBody { get; set; }

    public int InfoStatus { get; set; } = 200;

    public bool RejectWrongPassword { get; init; } = true;

    public ConcurrentQueue<FakeRestCall> RestCalls { get; } = [];

    public ConcurrentQueue<string> WebsocketIns { get; } = [];

    public ConcurrentQueue<Dictionary<string, string>> WebsocketHeaders { get; } = [];

    public int WebsocketConnections;

    public volatile bool ReadySent;

    public volatile bool LastResumeFlag;

    public volatile bool SuppressReady;

    private volatile NetworkStream? _stream;

    public int Port { get; }

    public FakeNodeServer()
    {
        _listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        _listener.Start(16);
        Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public void On(string method, string path, Func<FakeRestCall, FakeRestResponse> handler) =>
        _handlers[$"{method} {path}"] = handler;

    public void OnRaw(string method, string path, Func<FakeRestCall, Stream, Task> handler) =>
        _rawHandlers[$"{method} {path}"] = handler;

    public void On(string method, string path, int status, string body) =>
        On(method, path, _ => new FakeRestResponse(status, body));

    public async Task PushAsync(string json)
    {
        var stream = _stream ?? throw new InvalidOperationException("No websocket is connected.");
        await SendFrameOnStreamAsync(stream, 0x1, Encoding.UTF8.GetBytes(json))
            .ConfigureAwait(false);
    }

    public async Task PushFrameAsync(int opcode, bool fin, string payload)
    {
        var stream = _stream ?? throw new InvalidOperationException("No websocket is connected.");
        await SendFrameOnStreamAsync(stream, opcode, Encoding.UTF8.GetBytes(payload), fin)
            .ConfigureAwait(false);
    }

    public async Task DropConnectionAsync(ushort code, string reason)
    {
        var stream = _stream;
        if (stream is null)
            return;
        var payload = new byte[2 + Encoding.UTF8.GetByteCount(reason)];
        payload[0] = (byte)(code >> 8);
        payload[1] = (byte)(code & 0xff);
        Encoding.UTF8.GetBytes(reason).AsSpan().CopyTo(payload.AsSpan(2));
        await SendFrameOnStreamAsync(stream, 0x8, payload).ConfigureAwait(false);
        stream.Close();
    }

    public void KillConnection()
    {
        _stream?.Close();
    }

    public object? CaptureWebsocket() => _stream;

    public async Task PushOnCapturedAsync(object captured, string json)
    {
        if (captured is NetworkStream stream)
            await SendFrameOnStreamAsync(stream, 0x1, Encoding.UTF8.GetBytes(json))
                .ConfigureAwait(false);
    }

    public void KillCaptured(object captured)
    {
        if (captured is NetworkStream stream)
            stream.Close();
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
            client.NoDelay = true;
            using var _ = client;
            var stream = client.GetStream();
            var buffer = new byte[128 * 1024];
            var carried = 0;

            while (!_cts.IsCancellationRequested)
            {
                int headEnd;
                while ((headEnd = HeadEnd(buffer, carried)) < 0)
                {
                    if (carried == buffer.Length)
                        return;
                    var read = await stream
                        .ReadAsync(buffer.AsMemory(carried), _cts.Token)
                        .ConfigureAwait(false);
                    if (read == 0)
                        return;
                    carried += read;
                }

                var head = Encoding.UTF8.GetString(buffer, 0, headEnd);
                var lines = head.Split("\r\n");
                var parts = lines[0].Split(' ');
                if (parts.Length < 2)
                    return;
                var method = parts[0];
                var target = parts[1];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines[1..])
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0)
                        headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }

                var bodyStart = headEnd + 4;
                var bodyLength =
                    headers.TryGetValue("Content-Length", out var lengthText)
                    && int.TryParse(lengthText, out var parsed)
                        ? parsed
                        : 0;
                var total = bodyStart + bodyLength;
                while (carried < total)
                {
                    var read = await stream
                        .ReadAsync(buffer.AsMemory(carried), _cts.Token)
                        .ConfigureAwait(false);
                    if (read == 0)
                        return;
                    carried += read;
                }

                var body =
                    bodyLength > 0 ? Encoding.UTF8.GetString(buffer, bodyStart, bodyLength) : null;
                var queryAt = target.IndexOf('?');
                var path = queryAt >= 0 ? target[..queryAt] : target;
                var query = queryAt >= 0 ? target[(queryAt + 1)..] : null;
                headers.TryGetValue("Authorization", out var authorization);

                if (
                    GhostProbes
                    && !(
                        path == "/v4/websocket"
                        && headers.TryGetValue("Upgrade", out var ghostUpgrade)
                        && ghostUpgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, _cts.Token).ConfigureAwait(false);
                    return;
                }

                if (
                    path == "/v4/websocket"
                    && headers.TryGetValue("Upgrade", out var upgrade)
                    && upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)
                )
                {
                    WebsocketHeaders.Enqueue(headers);
                    Interlocked.Increment(ref WebsocketConnections);
                    await HandleWebsocketAsync(stream, headers, buffer, total, carried)
                        .ConfigureAwait(false);
                    return;
                }

                var call = new FakeRestCall(method, path, query, body, authorization);
                RestCalls.Enqueue(call);
                if (_rawHandlers.TryGetValue($"{method} {path}", out var rawHandler))
                {
                    await rawHandler(call, stream).ConfigureAwait(false);
                    return;
                }
                var response = Answer(call);
                var sent = Encoding.UTF8.GetBytes(response.Body);
                var reason = response.Status switch
                {
                    200 => "OK",
                    201 => "Created",
                    204 => "No Content",
                    400 => "Bad Request",
                    401 => "Unauthorized",
                    403 => "Forbidden",
                    404 => "Not Found",
                    429 => "Too Many Requests",
                    500 => "Internal Server Error",
                    _ => "Status",
                };
                var headBack =
                    $"HTTP/1.1 {response.Status} {reason}\r\n"
                    + "Content-Type: application/json\r\n"
                    + (IsNodeLink ? string.Empty : "Lavalink-Api-Version: 4\r\n")
                    + (
                        response.RetryAfter is { } retryAfter
                            ? $"Retry-After: {retryAfter}\r\n"
                            : string.Empty
                    )
                    + "Content-Length: "
                    + sent.Length
                    + "\r\n"
                    + "Connection: close\r\n\r\n";
                await stream
                    .WriteAsync(Encoding.UTF8.GetBytes(headBack), _cts.Token)
                    .ConfigureAwait(false);
                await stream.WriteAsync(sent, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception) { }
    }

    private static int HeadEnd(byte[] buffer, int length)
    {
        var limit = length - 3;
        for (var i = 0; i < limit; i++)
        {
            if (
                buffer[i] == 13
                && buffer[i + 1] == 10
                && buffer[i + 2] == 13
                && buffer[i + 3] == 10
            )
                return i;
        }
        return -1;
    }

    private (int Status, string Reason)? RejectedUpgrade(Dictionary<string, string> headers)
    {
        headers.TryGetValue("Authorization", out var presented);
        if (presented != Password)
            return (401, "Unauthorized");
        headers.TryGetValue("User-Id", out var userId);
        if (IsNodeLink)
        {
            if (userId is null || userId.Length is < 18 or > 19 || !userId.All(char.IsAsciiDigit))
                return (400, "Bad Request");
            if (!headers.ContainsKey("Client-Name"))
                return (400, "Bad Request");
            return null;
        }
        if (string.IsNullOrEmpty(userId) || (long.TryParse(userId, out var parsed) && parsed == 0))
            return (400, "Bad Request");
        return null;
    }

    private FakeRestResponse Answer(FakeRestCall call)
    {
        if (RejectWrongPassword && call.Authorization != Password)
            return IsNodeLink
                ? new FakeRestResponse(401, "Unauthorized")
                : new FakeRestResponse(
                    string.IsNullOrEmpty(call.Authorization) ? 401 : 403,
                    string.Empty
                );

        if (_handlers.TryGetValue($"{call.Method} {call.Path}", out var handler))
            return handler(call);

        if (call.Path == "/v4/info")
        {
            if (InfoStatus != 200)
                return new FakeRestResponse(InfoStatus, "{}");
            return new FakeRestResponse(
                200,
                InfoBody
                    ?? $$"""
                    {"version":{"semver":"{{Semver}}"}{{(
                        IsNodeLink ? ",\"isNodelink\":true" : string.Empty
                    )}},"plugins":[]}
                    """
            );
        }

        return new FakeRestResponse(
            404,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    status = 404,
                    error = "Not Found",
                    message = IsNodeLink
                        ? "The requested route was not found."
                        : $"No endpoint {call.Method} {call.Path}.",
                    path = call.Path,
                }
            )
        );
    }

    private async Task HandleWebsocketAsync(
        NetworkStream stream,
        Dictionary<string, string> headers,
        byte[] buffer,
        int consumed,
        int carried
    )
    {
        _stream = stream;

        if (GhostHandshakes)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _cts.Token).ConfigureAwait(false);
            return;
        }
        if (FailWebsocketHandshakes)
        {
            await stream
                .WriteAsync(
                    Encoding.UTF8.GetBytes(
                        "HTTP/1.1 500 Server Refuses\r\nConnection: close\r\n\r\n"
                    ),
                    _cts.Token
                )
                .ConfigureAwait(false);
            return;
        }
        if (RejectWrongPassword && RejectedUpgrade(headers) is { } rejection)
        {
            await stream
                .WriteAsync(
                    Encoding.UTF8.GetBytes(
                        $"HTTP/1.1 {rejection.Status} {rejection.Reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                    ),
                    _cts.Token
                )
                .ConfigureAwait(false);
            return;
        }
        var key = headers["Sec-WebSocket-Key"];
        if (CloseAfterHandshake)
        {
            var acceptKey = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + WsGuid))
            );
            await stream
                .WriteAsync(
                    Encoding.UTF8.GetBytes(
                        "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "
                            + acceptKey
                            + "\r\n\r\n"
                    ),
                    _cts.Token
                )
                .ConfigureAwait(false);
            await Task.Delay(100, _cts.Token).ConfigureAwait(false);
            stream.Close();
            return;
        }
        if (HandshakeDelay > TimeSpan.Zero)
            await Task.Delay(HandshakeDelay, _cts.Token).ConfigureAwait(false);
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WsGuid)));
        var resuming =
            ResumeOnReconnect
            && headers.TryGetValue("Session-Id", out var sent)
            && sent == SessionId;
        LastResumeFlag = resuming;
        var handshake =
            "HTTP/1.1 101 Switching Protocols\r\n"
            + "Upgrade: websocket\r\n"
            + "Connection: Upgrade\r\n"
            + (IsNodeLink ? string.Empty : $"Session-Resumed: {(resuming ? "true" : "false")}\r\n")
            + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream
            .WriteAsync(Encoding.UTF8.GetBytes(handshake), _cts.Token)
            .ConfigureAwait(false);

        if (!IsNodeLink && !long.TryParse(headers["User-Id"], out _))
        {
            await SendFrameOnStreamAsync(stream, 0x8, [0x03, 0xF3]).ConfigureAwait(false);
            return;
        }

        if (!SuppressReady)
        {
            await SendTextAsync(
                    stream,
                    $$"""{"op":"ready","resumed":{{(resuming ? "true" : "false")}},"sessionId":"{{SessionId}}"}"""
                )
                .ConfigureAwait(false);
            ReadySent = true;
        }

        var reader = new FrameReader(stream);
        while (!_cts.IsCancellationRequested)
        {
            var frame = await reader.ReadAsync(_cts.Token).ConfigureAwait(false);
            if (frame is null)
                return;
            switch (frame.Opcode)
            {
                case 0x1:
                    WebsocketIns.Enqueue(Encoding.UTF8.GetString(frame.Payload));
                    break;
                case 0x8:
                    if (IgnoreWebsocketClose)
                        break;
                    await SendFrameOnStreamAsync(stream, 0x8, frame.Payload).ConfigureAwait(false);
                    return;
                case 0x9:
                    await SendFrameOnStreamAsync(stream, 0xA, frame.Payload).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task SendTextAsync(NetworkStream stream, string text) =>
        await SendFrameOnStreamAsync(stream, 0x1, Encoding.UTF8.GetBytes(text))
            .ConfigureAwait(false);

    private async Task SendFrameOnStreamAsync(
        NetworkStream stream,
        int opcode,
        byte[] payload,
        bool fin = true
    )
    {
        try
        {
            await SendFrameCoreAsync(stream, opcode, payload, fin).ConfigureAwait(false);
        }
        catch (Exception) when (ExceptionIsTransport()) { }
    }

    private static bool ExceptionIsTransport() => true;

    private async Task SendFrameCoreAsync(
        NetworkStream stream,
        int opcode,
        byte[] payload,
        bool fin = true
    )
    {
        _stream = stream;
        var header = new byte[10];
        header[0] = (byte)((fin ? 0x80 : 0x00) | opcode);
        var length = payload.Length;
        if (length < 126)
        {
            header[1] = (byte)length;
            await stream.WriteAsync(header.AsMemory(0, 2), _cts.Token).ConfigureAwait(false);
        }
        else if (length <= 0xffff)
        {
            header[1] = 126;
            header[2] = (byte)(length >> 8);
            header[3] = (byte)(length & 0xff);
            await stream.WriteAsync(header.AsMemory(0, 4), _cts.Token).ConfigureAwait(false);
        }
        else
        {
            header[1] = 127;
            for (var i = 0; i < 8; i++)
                header[2 + i] = (byte)((length >> (8 * (7 - i))) & 0xff);
            await stream.WriteAsync(header.AsMemory(0, 10), _cts.Token).ConfigureAwait(false);
        }
        await stream.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
        await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
    }

    private sealed record Frame(int Opcode, byte[] Payload);

    private sealed class FrameReader(NetworkStream stream)
    {
        private readonly byte[] _head = new byte[2];

        public async Task<Frame?> ReadAsync(CancellationToken token)
        {
            if (!await FillAsync(_head, token).ConfigureAwait(false))
                return null;
            var opcode = _head[0] & 0x0f;
            var masked = (_head[1] & 0x80) != 0;
            var length = _head[1] & 0x7f;
            if (length == 126)
            {
                var ext = new byte[2];
                if (!await FillAsync(ext, token).ConfigureAwait(false))
                    return null;
                length = (ext[0] << 8) | ext[1];
            }
            else if (length == 127)
            {
                var ext = new byte[8];
                if (!await FillAsync(ext, token).ConfigureAwait(false))
                    return null;
                length = 0;
                for (var i = 0; i < 8; i++)
                    length = (length << 8) | ext[i];
                if (length > 16 * 1024 * 1024)
                    return null;
            }

            var mask = new byte[4];
            if (masked && !await FillAsync(mask, token).ConfigureAwait(false))
                return null;

            var payload = new byte[length];
            if (length > 0 && !await FillAsync(payload, token).ConfigureAwait(false))
                return null;
            if (masked)
                for (var i = 0; i < payload.Length; i++)
                    payload[i] ^= mask[i & 3];
            return new Frame(opcode, payload);
        }

        private async Task<bool> FillAsync(byte[] target, CancellationToken token)
        {
            var filled = 0;
            while (filled < target.Length)
            {
                var read = await stream
                    .ReadAsync(target.AsMemory(filled), token)
                    .ConfigureAwait(false);
                if (read == 0)
                    return false;
                filled += read;
            }
            return true;
        }
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        _listener.Stop();
        try
        {
            _acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException) { }
        _listener.Dispose();
        _cts.Dispose();
    }
}

internal sealed record Stack : IAsyncDisposable
{
    public required FakeBridge Bridge { get; init; }

    public required NyraExtension Extension { get; init; }

    public required Node Node { get; init; }

    public required FakeNodeServer Server { get; init; }

    public async ValueTask DisposeAsync()
    {
        await Extension.DisposeAsync().ConfigureAwait(false);
        Server.Dispose();
    }
}

internal static class NodeHarness
{
    public const string PlayerPath = "/v4/sessions/fake-session/players/777";

    public static void AcceptPlayerPatch(FakeNodeServer server, ulong guild = 777) =>
        server.On(
            "PATCH",
            $"/v4/sessions/fake-session/players/{guild}",
            _ => new FakeRestResponse(
                200,
                $$$"""{"guildId":"{{{guild}}}","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            )
        );

    public static async Task<GuildConnection> ConnectGuildAsync(
        FakeBridge bridge,
        Node node,
        FakeNodeServer server,
        ulong guild = 777,
        ulong channel = 555
    )
    {
        AcceptPlayerPatch(server, guild);
        var connect = node.ConnectAsync(guild, channel);
        await Task.Delay(150).ConfigureAwait(false);
        bridge.FireVoiceState(guild, channel, "vsess");
        bridge.FireVoiceServer(guild, "tok", "127.0.0.1");
        return await connect.ConfigureAwait(false);
    }

    public static int DeadPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task AwaitRestAsync(Func<Task> call)
    {
        await Assert
            .That(async () =>
            {
                await call().ConfigureAwait(false);
            })
            .Throws<RestException>();
    }

    public static void InjectShards(ShardedGatewayClient sharded, params GatewayClient[] clients)
    {
        var stateType =
            typeof(ShardedGatewayClient).GetNestedType("State", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("ShardedGatewayClient.State");
        var state = Activator.CreateInstance(stateType)!;
        stateType.GetProperty("Clients")!.SetValue(state, clients);
        Reflect.Field(sharded, "_state", state);
    }

    public static void ForceReady(GatewayClient client) =>
        Reflect.Field(client, "_state", NewWsState(ready: true));

    public static object NewWsState(bool ready)
    {
        var stateType =
            typeof(WebSocketClient).GetNestedType("State", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("WebSocketClient.State");
        var state = Activator.CreateInstance(stateType)!;
        if (ready)
        {
            stateType
                .GetMethod("IndicateReady", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(state, new object?[] { null });
        }
        return state;
    }
}
