using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.DiscordNet;

/// <summary>Wraps a <see cref="DiscordSocketClient"/> or <see cref="DiscordShardedClient"/> for Nyra.</summary>
public sealed class DiscordNetBridge
    : IDiscordBridge,
        IGuildRemovalSource,
        IVoiceListenerSource,
        IDisposable
{
    private readonly BaseSocketClient _client;
    private readonly ConcurrentDictionary<ulong, IAudioChannel> _audioChannels = new();

    private readonly GuildRemovalFence _removalFence = new();

    private readonly Lock _guildPinGate = new();

    private bool _disposed;

    private readonly TaskCompletionSource<ClientInformation> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    /// <summary>Raised when Discord allocates or re-targets the voice server of a guild.</summary>
    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <summary>Raised when the voice state of the current user changes in a guild.</summary>
    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <summary>Raised when the bot was removed from a guild.</summary>
    /// <remarks>Not raised for guild outages.</remarks>
    public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

    /// <summary>Raised when a user who is not a bot joins, leaves or changes a voice channel.</summary>
    public event EventHandler<ListenerVoiceStateUpdatedEventArgs>? ListenerVoiceStateUpdated;

    /// <summary>Wraps a <see cref="DiscordSocketClient"/>, forwarding its voice events.</summary>
    /// <param name="client">The client to wrap.</param>
    /// <param name="logger">The logger to write Nyra's messages to, Discord.Net has no ILogger.</param>
    public DiscordNetBridge(DiscordSocketClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Logger = logger;

        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.UserVoiceStateUpdated += OnVoiceStateUpdated;
        client.Ready += OnClientReady;
        client.LeftGuild += OnLeftGuild;

        if (client.CurrentUser is { } user)
            PublishUserId(user.Id);
    }

    /// <summary>Wraps a <see cref="DiscordShardedClient"/>, forwarding the voice events of every shard.</summary>
    /// <param name="client">The sharded client to wrap.</param>
    /// <param name="logger">The logger to write Nyra's messages to, Discord.Net has no ILogger.</param>
    public DiscordNetBridge(DiscordShardedClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Logger = logger;

        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.UserVoiceStateUpdated += OnVoiceStateUpdated;
        client.ShardReady += OnShardReady;
        client.LeftGuild += OnLeftGuild;

        if (client.CurrentUser is { } shardedUser)
            PublishUserId(shardedUser.Id);
    }

    /// <inheritdoc />
    public ILogger? Logger { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<ulong>? GetListeners(ulong guildId, ulong channelId)
    {
        if (_client.GetGuild(guildId)?.GetVoiceChannel(channelId) is not { } channel)
            return null;

        var self = _client.CurrentUser?.Id;
        return
        [
            .. channel
                .ConnectedUsers.Where(user => !user.IsBot && user.Id != self)
                .Select(user => user.Id),
        ];
    }

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
        var joinStartTick = Environment.TickCount64;

        if (channelId is null)
        {
            if (_audioChannels.TryRemove(guildId, out var channel))
                await BoundedAsync(channel.DisconnectAsync(), cancellationToken)
                    .ConfigureAwait(false);
            else if (
                _client.CurrentUser is not null
                && GetGuildOrNull(guildId)?.CurrentUser?.VoiceChannel is { } current
            )
                await BoundedAsync(current.DisconnectAsync(), cancellationToken)
                    .ConfigureAwait(false);
            return;
        }

        var guild = GetGuildOrNull(guildId)
            ?? throw new InvalidOperationException(
                $"Could not find the guild {guildId}, it is not available to the Discord.Net client."
            );

        var resolvedChannel = guild.GetChannel(channelId.Value)
            ?? throw new InvalidOperationException(
                $"The channel {channelId.Value} was not found in guild {guildId}, it may have been deleted."
            );
        if (resolvedChannel is not IAudioChannel audioChannel)
            throw new ArgumentException(
                $"The channel {channelId.Value} is not a voice or stage channel.",
                nameof(channelId)
            );

        lock (_guildPinGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_removalFence.RemovedSince(guildId, joinStartTick))
                throw new InvalidOperationException(
                    $"The bot was removed from guild {guildId} while it was joining, the voice state update was not sent."
                );

            _audioChannels[guildId] = audioChannel;
            _removalFence.Forget(guildId);
        }

        await BoundedAsync(
                audioChannel.ConnectAsync(selfDeaf, selfMute, external: true, disconnect: false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private SocketGuild? GetGuildOrNull(ulong guildId)
    {
        try
        {
            return _client.GetGuild(guildId);
        }
        catch (NullReferenceException)
        {
            return null;
        }
        catch (DivideByZeroException exception)
        {
            throw new InvalidOperationException(
                "The sharded client has not logged in or has logged out, its shard set is unknown, it cannot resolve guilds.",
                exception
            );
        }
    }

    private static async Task BoundedAsync(Task send, CancellationToken cancellationToken)
    {
        try
        {
            await send.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = send.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
            throw;
        }
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
        _client.VoiceServerUpdated -= OnVoiceServerUpdated;
        _client.UserVoiceStateUpdated -= OnVoiceStateUpdated;
        _client.LeftGuild -= OnLeftGuild;

        if (_client is DiscordSocketClient socketClient)
            socketClient.Ready -= OnClientReady;

        if (_client is DiscordShardedClient shardedClient)
            shardedClient.ShardReady -= OnShardReady;

        _ready.TrySetCanceled();

        lock (_guildPinGate)
        {
            _disposed = true;

            _audioChannels.Clear();
            _removalFence.Clear();
        }
    }

    private Task OnClientReady()
    {
        PublishUserId(_client.CurrentUser.Id);
        return Task.CompletedTask;
    }

    private Task OnShardReady(DiscordSocketClient client)
    {
        PublishUserId(client.CurrentUser.Id);
        return Task.CompletedTask;
    }

    private void PublishUserId(ulong userId) => _ready.TrySetResult(new ClientInformation(userId));

    private Task OnLeftGuild(SocketGuild guild)
    {
        lock (_guildPinGate)
        {
            _removalFence.Mark(guild.Id);
            _audioChannels.TryRemove(guild.Id, out _);
        }

        BridgeEvent.RaiseIsolated(GuildRemoved, this, new GuildRemovedEventArgs(guild.Id), Logger);
        return Task.CompletedTask;
    }

    private Task OnVoiceStateUpdated(
        SocketUser user,
        SocketVoiceState oldState,
        SocketVoiceState state
    )
    {
        var guildId = oldState.VoiceChannel?.Guild?.Id
            ?? state.VoiceChannel?.Guild?.Id
            ?? (user as SocketGuildUser)?.Guild?.Id;

        if (user.Id != _client.CurrentUser?.Id)
        {
            if (guildId is { } listenerGuildId && !user.IsBot)
                BridgeEvent.RaiseIsolated(
                    ListenerVoiceStateUpdated,
                    this,
                    new ListenerVoiceStateUpdatedEventArgs(
                        listenerGuildId,
                        user.Id,
                        state.VoiceChannelId
                    ),
                    Logger
                );
            return Task.CompletedTask;
        }

        _removalFence.Sweep();

        if (guildId is null)
            return Task.CompletedTask;

        BridgeEvent.RaiseIsolated(
            VoiceStateUpdated,
            this,
            new VoiceStateUpdatedEventArgs(
                guildId.Value,
                new DiscordVoiceState(
                    state.VoiceChannelId,
                    state.VoiceSessionId,
                    state.IsSelfDeafened,
                    state.IsSelfMuted
                )
            ),
            Logger
        );
        return Task.CompletedTask;
    }

    private Task OnVoiceServerUpdated(SocketVoiceServer voiceServer)
    {
        if (
            string.IsNullOrWhiteSpace(voiceServer.Endpoint)
            || string.IsNullOrWhiteSpace(voiceServer.Token)
        )
            return Task.CompletedTask;

        BridgeEvent.RaiseIsolated(
            VoiceServerUpdated,
            this,
            new VoiceServerUpdatedEventArgs(
                voiceServer.Guild.Id,
                new VoiceServer(voiceServer.Token, voiceServer.Endpoint)
            ),
            Logger
        );
        return Task.CompletedTask;
    }
}
