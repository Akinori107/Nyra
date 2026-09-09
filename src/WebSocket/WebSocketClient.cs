using System.Net.WebSockets;

namespace Nyra.WebSocket;

/// <summary>
/// Thin wrapper around <see cref="ClientWebSocket"/>: a dedicated receive loop that hands out
/// raw UTF-8 payloads (no intermediate string allocations) and graceful closure. Single-use: one <see cref="ConnectAsync"/> per instance, reconnection policy lives in the node
/// connection, which creates a fresh client per attempt.
/// </summary>
internal sealed class WebSocketClient : IDisposable
{
    // Protocol messages are small, buffering a larger one unboundedly would exhaust memory.
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly Lock _lock = new();
    private ClientWebSocket? _websocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    /// <summary>Raw UTF-8 bytes of a text message. The memory is only valid until the handler returns.</summary>
    public event Action<ReadOnlyMemory<byte>>? TextMessageReceived;

    public event Action<WebSocketCloseStatus?, string?, Exception?>? Closed;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
                return _websocket is { State: WebSocketState.Open };
        }
    }

    public async Task ConnectAsync(
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken
    )
    {
        ClientWebSocket websocket;
        CancellationTokenSource receiveCts;
        lock (_lock)
        {
            websocket = new ClientWebSocket();
            receiveCts = new CancellationTokenSource();
            _websocket = websocket;
            _receiveCts = receiveCts;
        }

        websocket.Options.KeepAliveInterval = TimeSpan.FromMinutes(2);
        foreach (var (key, value) in headers)
            websocket.Options.SetRequestHeader(key, value);

        try
        {
            await websocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            // Direct invocation: the loop is fully async, a thread-pool hop would only add latency.
            var receiveTask = ReceiveLoopAsync(websocket, receiveCts.Token);
            lock (_lock)
                _receiveTask = receiveTask;
        }
        catch
        {
            lock (_lock)
            {
                if (ReferenceEquals(_websocket, websocket))
                {
                    _websocket = null;
                    _receiveCts = null;
                }
            }

            receiveCts.Dispose();
            websocket.Dispose();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ClientWebSocket? websocket;
        CancellationTokenSource? receiveCts;
        Task? receiveTask;
        lock (_lock)
        {
            websocket = _websocket;
            receiveCts = _receiveCts;
            receiveTask = _receiveTask;
            _websocket = null;
            _receiveCts = null;
            _receiveTask = null;
        }

        if (websocket is null)
            return;

        if (websocket.State == WebSocketState.Open)
        {
            using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            closeCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await websocket
                    .CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client shutting down",
                        closeCts.Token
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The server may already be gone, the socket is discarded either way.
            }
        }

        receiveCts?.Cancel();
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The receive loop always settles its own exceptions before returning.
            }
        }

        receiveCts?.Dispose();
        websocket.Dispose();
    }

    public void Dispose()
    {
        ClientWebSocket? websocket;
        CancellationTokenSource? receiveCts;
        Task? receiveTask;
        lock (_lock)
        {
            websocket = _websocket;
            receiveCts = _receiveCts;
            receiveTask = _receiveTask;
            _websocket = null;
            _receiveCts = null;
            _receiveTask = null;
        }

        receiveCts?.Cancel();

        // Brief wait outside the lock: the token already told the loop to unwind.
        try
        {
            receiveTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // Observe and ignore: the receive loop never throws unobserved.
        }

        receiveCts?.Dispose();
        websocket?.Dispose();
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket websocket,
        CancellationToken cancellationToken
    )
    {
        // Protocol messages are small (well under 16 KB), so virtually all of them arrive
        // as a single frame and are dispatched straight from this buffer.
        var buffer = new byte[16 * 1024];
        try
        {
            while (
                !cancellationToken.IsCancellationRequested && websocket.State == WebSocketState.Open
            )
            {
                // The Memory overload returns a ValueTask and allocates nothing when the
                // frame is already buffered, unlike the ArraySegment overload.
                var result = await websocket
                    .ReceiveAsync((Memory<byte>)buffer, cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await AcknowledgeCloseAsync(websocket).ConfigureAwait(false);
                    Closed?.Invoke(websocket.CloseStatus, websocket.CloseStatusDescription, null);
                    return;
                }

                if (result.EndOfMessage)
                {
                    // Fast path: single-frame message, zero copies beyond the receive buffer.
                    if (result.MessageType == WebSocketMessageType.Text)
                        TextMessageReceived?.Invoke(buffer.AsMemory(0, result.Count));
                    continue;
                }

                // Fragmented message: assemble the frames before dispatching.
                using var message = new MemoryStream();
                message.Write(buffer, 0, result.Count);
                while (true)
                {
                    result = await websocket
                        .ReceiveAsync((Memory<byte>)buffer, cancellationToken)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await AcknowledgeCloseAsync(websocket).ConfigureAwait(false);
                        Closed?.Invoke(
                            websocket.CloseStatus,
                            websocket.CloseStatusDescription,
                            null
                        );
                        return;
                    }

                    if (message.Length + result.Count > MaxMessageBytes)
                        throw new InvalidOperationException(
                            $"The server sent a message larger than {MaxMessageBytes} bytes."
                        );

                    message.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                        break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                    TextMessageReceived?.Invoke(
                        message.GetBuffer().AsMemory(0, (int)message.Length)
                    );

                // Binary frames are not part of the Lavalink/NodeLink protocol and are ignored.
            }
        }
        catch (OperationCanceledException)
        {
            Closed?.Invoke(websocket.CloseStatus, websocket.CloseStatusDescription, null);
        }
        catch (Exception exception)
        {
            Closed?.Invoke(websocket.CloseStatus, websocket.CloseStatusDescription, exception);
        }
        finally
        {
            // The loop owns the socket once it ends, so a closed or errored connection never
            // leaks one, Stop/Dispose also dispose it, and ClientWebSocket tolerates that.
            websocket.Dispose();
        }
    }

    private static async Task AcknowledgeCloseAsync(ClientWebSocket websocket)
    {
        // Acknowledge the close frame so the server sees a clean disconnect.
        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await websocket
                .CloseAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", closeCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The server did not complete the close handshake in time, or the socket is already
            // dead, it is discarded either way.
        }
    }
}
