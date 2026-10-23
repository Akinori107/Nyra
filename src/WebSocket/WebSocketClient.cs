using System.Net.WebSockets;

namespace Nyra.WebSocket;

internal sealed class WebSocketClient : IDisposable
{
    private const int MaxMessageBytes = 1024 * 1024;

    internal const string HandshakeStatusCodeKey = "Nyra.HandshakeStatusCode";

    private readonly Lock _lock = new();
    private ClientWebSocket? _websocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    private readonly AsyncLocal<bool> _onReceiveLoop = new();

    public event Action<ReadOnlyMemory<byte>>? TextMessageReceived;

    public event Action<WebSocketCloseStatus?, string?, Exception?>? Closed;

    private void RaiseClosed(ClientWebSocket websocket, Exception? exception = null)
    {
        try
        {
            Closed?.Invoke(websocket.CloseStatus, websocket.CloseStatusDescription, exception);
        }
        catch { }
    }

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
            _receiveTask = null;
        }

        try
        {
            websocket.Options.KeepAliveInterval = TimeSpan.FromMinutes(2);
            websocket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            websocket.Options.CollectHttpResponseDetails = true;

            foreach (var (key, value) in headers)
                websocket.Options.SetRequestHeader(key, value);

            await websocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (websocket.HttpStatusCode != 0)
                exception.Data[HandshakeStatusCodeKey] = (int)websocket.HttpStatusCode;

            lock (_lock)
                if (ReferenceEquals(_websocket, websocket))
                {
                    _websocket = null;
                    _receiveCts = null;
                }

            receiveCts.Dispose();
            websocket.Dispose();
            throw;
        }
    }

    public void StartReceiving()
    {
        lock (_lock)
        {
            if (_websocket is null || _receiveTask is not null)
                return;

            _receiveTask = ReceiveLoopAsync(_websocket, _receiveCts!.Token);
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
            catch (Exception) { }
        }

        receiveCts?.Cancel();
        if (receiveTask is not null && !_onReceiveLoop.Value)
            await receiveTask
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

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

        if (!_onReceiveLoop.Value)
            try
            {
                receiveTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception) { }

        receiveCts?.Dispose();
        websocket?.Dispose();
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket websocket,
        CancellationToken cancellationToken
    )
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        _onReceiveLoop.Value = true;

        var buffer = new byte[16 * 1024];
        try
        {
            while (
                !cancellationToken.IsCancellationRequested && websocket.State == WebSocketState.Open
            )
            {
                var result = await websocket
                    .ReceiveAsync((Memory<byte>)buffer, cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await AcknowledgeCloseAsync(websocket).ConfigureAwait(false);
                    RaiseClosed(websocket);
                    return;
                }

                if (result.EndOfMessage)
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                        TextMessageReceived?.Invoke(buffer.AsMemory(0, result.Count));
                    continue;
                }

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
                        RaiseClosed(websocket);
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
            }

            RaiseClosed(websocket);
        }
        catch (OperationCanceledException)
        {
            RaiseClosed(websocket);
        }
        catch (Exception exception)
        {
            RaiseClosed(websocket, exception);
        }
        finally
        {
            websocket.Dispose();
        }
    }

    private static async Task AcknowledgeCloseAsync(ClientWebSocket websocket)
    {
        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var status = websocket.CloseStatus is { } received and not WebSocketCloseStatus.Empty
            ? received
            : WebSocketCloseStatus.NormalClosure;

        try
        {
            await websocket
                .CloseAsync(status, "Acknowledged", closeCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception) { }
    }
}
