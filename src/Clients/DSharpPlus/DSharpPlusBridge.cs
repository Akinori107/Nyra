using global::DSharpPlus;
using global::DSharpPlus.EventArgs;
using global::DSharpPlus.Net.Abstractions;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.DSharpPlus;

/// <summary>
/// Wraps a <see cref="DiscordClient"/> or <see cref="DiscordShardedClient"/> for Nyra: forwards
/// the voice events of the current user and sends the raw gateway voice state update.
/// </summary>
public sealed class DSharpPlusBridge : IDiscordBridge, IDisposable
{
    private readonly object _client;
    private readonly TaskCompletionSource<ClientInformation> _ready;

    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;
    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    public DSharpPlusBridge(DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Logger = client.Logger;
        _ready = new TaskCompletionSource<ClientInformation>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        client.VoiceStateUpdated += OnVoiceStateUpdated;
        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.Ready += OnClientReady;

        if (client.CurrentUser is { } user)
            _ready.TrySetResult(new ClientInformation("DSharpPlus", user.Id));
    }

    public DSharpPlusBridge(DiscordShardedClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Logger = client.Logger;
        _ready = new TaskCompletionSource<ClientInformation>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        client.VoiceStateUpdated += OnVoiceStateUpdated;
        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.Ready += OnClientReady;

        if (client.CurrentUser is { } shardedUser)
            _ready.TrySetResult(new ClientInformation("DSharpPlus", shardedUser.Id));
    }

    /// <inheritdoc />
    public ILogger? Logger { get; }

    /// <inheritdoc />
    public Task SendVoiceUpdateAsync(
        ulong guildId,
        ulong? channelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new Dictionary<string, object?>
        {
            ["guild_id"] = guildId.ToString(),
            ["channel_id"] = channelId?.ToString(),
            ["self_mute"] = selfMute,
            ["self_deaf"] = selfDeaf,
        };

        // The only public DSharpPlus API that can send the voice state update (op 4), obsolete
        // only because it bypasses validation that no specialized voice method offers anyway.
#pragma warning disable CS0618
        return GetClientForGuild(guildId).SendPayloadAsync(GatewayOpCode.VoiceStateUpdate, payload);
#pragma warning restore CS0618
    }

    /// <inheritdoc />
    public Task<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _ready.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        switch (_client)
        {
            case DiscordClient client:
                client.VoiceStateUpdated -= OnVoiceStateUpdated;
                client.VoiceServerUpdated -= OnVoiceServerUpdated;
                client.Ready -= OnClientReady;
                break;

            case DiscordShardedClient client:
                client.VoiceStateUpdated -= OnVoiceStateUpdated;
                client.VoiceServerUpdated -= OnVoiceServerUpdated;
                client.Ready -= OnClientReady;
                break;
        }
    }

    private DiscordClient GetClientForGuild(ulong guildId) =>
        _client is DiscordClient client
            ? client
            : ((DiscordShardedClient)_client).GetShard(guildId);

    private Task OnClientReady(DiscordClient sender, ReadyEventArgs eventArgs)
    {
        _ready.TrySetResult(new ClientInformation("DSharpPlus", sender.CurrentUser.Id));
        return Task.CompletedTask;
    }

    private Task OnVoiceStateUpdated(DiscordClient sender, VoiceStateUpdateEventArgs eventArgs)
    {
        if (eventArgs.Guild is null || eventArgs.User?.Id != sender.CurrentUser.Id)
            return Task.CompletedTask;

        VoiceStateUpdated?.Invoke(
            this,
            new VoiceStateUpdatedEventArgs(
                eventArgs.Guild.Id,
                new VoiceState(eventArgs.After?.Channel?.Id, eventArgs.SessionId)
            )
        );
        return Task.CompletedTask;
    }

    private Task OnVoiceServerUpdated(DiscordClient sender, VoiceServerUpdateEventArgs eventArgs)
    {
        if (eventArgs.Guild is null)
            return Task.CompletedTask;

        VoiceServerUpdated?.Invoke(
            this,
            new VoiceServerUpdatedEventArgs(
                eventArgs.Guild.Id,
                new VoiceServer(eventArgs.VoiceToken, eventArgs.Endpoint)
            )
        );
        return Task.CompletedTask;
    }
}
