using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.Abstractions;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.DSharpPlus;

/// <summary>
/// Wraps a <see cref="DiscordClient"/> or <see cref="DiscordShardedClient"/> for Nyra, forwards the voice
/// events of the current user and sends the raw gateway voice state update.
/// </summary>
public sealed class DSharpPlusBridge : IDiscordBridge, IGuildRemovalSource, IDisposable
{
    private object? _client;
    private readonly TaskCompletionSource<ClientInformation> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly int? _shardCount;

    private readonly ConcurrentDictionary<DiscordClient, byte> _gatewaySocketsUp = new();

    /// <summary>Raised when Discord allocates or re-targets the voice server of a guild.</summary>
    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <summary>Raised when the voice state of the current user changes in a guild.</summary>
    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <summary>Raised when the bot was removed from a guild.</summary>
    /// <remarks>Not raised for guild outages.</remarks>
    public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

    /// <summary>Wraps a single <see cref="DiscordClient"/>.</summary>
    /// <remarks>
    /// <see cref="DiscordClient.ShardCount"/> reports the configured count before the first connect, then the
    /// count Discord recommends once connected, never the count the gateway routes by.
    /// </remarks>
    /// <param name="client">The client to wrap.</param>
    /// <param name="shardCount">
    /// The shard count the gateway routes by. Pass it when the client is one shard of a sharded setup so the
    /// bridge can reject guilds of another shard, null skips the rejection.
    /// </param>
    public DSharpPlusBridge(DiscordClient client, int? shardCount = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _shardCount = shardCount;
        Logger = client.Logger;

        client.VoiceStateUpdated += OnVoiceStateUpdated;
        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.Ready += OnClientReady;
        client.GuildDeleted += OnGuildDeleted;
        client.SocketOpened += OnSocketOpened;
        client.SocketClosed += OnSocketClosed;

        if (client.CurrentUser is { } user)
        {
            _gatewaySocketsUp.TryAdd(client, 0);
            _ready.TrySetResult(new ClientInformation(user.Id));
        }
    }

    /// <summary>Wraps a sharded client, one bridge for the voice events of every shard.</summary>
    /// <param name="client">The sharded client to wrap.</param>
    public DSharpPlusBridge(DiscordShardedClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Logger = client.Logger;

        client.VoiceStateUpdated += OnVoiceStateUpdated;
        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.Ready += OnClientReady;
        client.GuildDeleted += OnGuildDeleted;
        client.SocketOpened += OnSocketOpened;
        client.SocketClosed += OnSocketClosed;

        if (client.CurrentUser is { } shardedUser)
        {
            foreach (var shard in client.ShardClients.Values)
                if (shard.CurrentUser is not null)
                    _gatewaySocketsUp.TryAdd(shard, 0);

            _ready.TrySetResult(new ClientInformation(shardedUser.Id));
        }
    }

    /// <inheritdoc />
    public ILogger? Logger { get; }

    internal int? ShardCountHint => _shardCount;

    /// <inheritdoc />
    public async Task SendVoiceUpdateAsync(
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

        var client = GetClientForGuild(guildId);
#pragma warning disable CS0618
        try
        {
            await client
                .SendPayloadAsync(GatewayOpCode.VoiceStateUpdate, payload)
                .ConfigureAwait(false);
        }
        catch (NullReferenceException exception) when (!_gatewaySocketsUp.ContainsKey(client))
        {
            throw new InvalidOperationException(
                "The DSharpPlus gateway is still connecting, it cannot send the voice state update yet.",
                exception
            );
        }
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
                client.GuildDeleted -= OnGuildDeleted;
                client.SocketOpened -= OnSocketOpened;
                client.SocketClosed -= OnSocketClosed;
                break;

            case DiscordShardedClient client:
                client.VoiceStateUpdated -= OnVoiceStateUpdated;
                client.VoiceServerUpdated -= OnVoiceServerUpdated;
                client.Ready -= OnClientReady;
                client.GuildDeleted -= OnGuildDeleted;
                client.SocketOpened -= OnSocketOpened;
                client.SocketClosed -= OnSocketClosed;
                break;
        }
        _ready.TrySetCanceled();

        _gatewaySocketsUp.Clear();

        _client = null;
    }

    private DiscordClient GetClientForGuild(ulong guildId)
    {
        if (_client is null)
            throw new InvalidOperationException(
                "The bridge is disposed, its client reference was released."
            );

        if (_client is DiscordClient client)
        {
            if (
                _shardCount is int total
                && total > 1
                && GuildShard.IdFor(guildId, total) != client.ShardId
            )
                throw new InvalidOperationException(
                    $"The guild {guildId} is not served by shard {client.ShardId} of this sharded client, use the extension of the guild's own shard."
                );

            return client;
        }

        var sharded = (DiscordShardedClient)_client;

        if (sharded.ShardClients.Count < 1)
            throw new InvalidOperationException(
                "The sharded client has not initialized its shards yet, start it before sending voice updates."
            );

        return sharded.GetShard(guildId)
            ?? throw new InvalidOperationException(
                $"The guild {guildId} is not available on any shard of this client, its shard has not cached the guild yet."
            );
    }

    private Task OnClientReady(DiscordClient sender, ReadyEventArgs eventArgs)
    {
        _ready.TrySetResult(new ClientInformation(sender.CurrentUser.Id));
        return Task.CompletedTask;
    }

    private Task OnSocketOpened(DiscordClient sender, SocketEventArgs eventArgs)
    {
        _gatewaySocketsUp.TryAdd(sender, 0);
        return Task.CompletedTask;
    }

    private Task OnSocketClosed(DiscordClient sender, SocketCloseEventArgs eventArgs)
    {
        _gatewaySocketsUp.TryRemove(sender, out _);
        return Task.CompletedTask;
    }

    private Task OnGuildDeleted(DiscordClient sender, GuildDeleteEventArgs eventArgs)
    {
        if (eventArgs.Unavailable || eventArgs.Guild is null)
            return Task.CompletedTask;

        BridgeEvent.RaiseIsolated(
            GuildRemoved,
            this,
            new GuildRemovedEventArgs(eventArgs.Guild.Id),
            Logger
        );
        return Task.CompletedTask;
    }

    private Task OnVoiceStateUpdated(DiscordClient sender, VoiceStateUpdateEventArgs eventArgs)
    {
        if (eventArgs.Guild is null || eventArgs.User?.Id != sender.CurrentUser.Id)
            return Task.CompletedTask;

        BridgeEvent.RaiseIsolated(
            VoiceStateUpdated,
            this,
            new VoiceStateUpdatedEventArgs(
                eventArgs.Guild.Id,
                new DiscordVoiceState(
                    eventArgs.After?.Channel?.Id,
                    eventArgs.SessionId,
                    eventArgs.After?.IsSelfDeafened,
                    eventArgs.After?.IsSelfMuted
                )
            ),
            Logger
        );
        return Task.CompletedTask;
    }

    private Task OnVoiceServerUpdated(DiscordClient sender, VoiceServerUpdateEventArgs eventArgs)
    {
        if (eventArgs.Guild is null)
            return Task.CompletedTask;

        if (
            string.IsNullOrWhiteSpace(eventArgs.Endpoint)
            || string.IsNullOrWhiteSpace(eventArgs.VoiceToken)
        )
            return Task.CompletedTask;

        BridgeEvent.RaiseIsolated(
            VoiceServerUpdated,
            this,
            new VoiceServerUpdatedEventArgs(
                eventArgs.Guild.Id,
                new VoiceServer(eventArgs.VoiceToken, eventArgs.Endpoint)
            ),
            Logger
        );
        return Task.CompletedTask;
    }
}
