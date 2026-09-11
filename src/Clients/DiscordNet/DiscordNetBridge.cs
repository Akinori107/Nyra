using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.DiscordNet;

/// <summary>
/// Wraps a <see cref="DiscordSocketClient"/> or <see cref="DiscordShardedClient"/> for Nyra. The
/// voice state update goes through Discord.Net's public external-audio route, so the bridge
/// keeps the audio channel of each guild to disconnect from later.
/// </summary>
public sealed class DiscordNetBridge : IDiscordBridge, IDisposable
{
    private readonly BaseSocketClient _client;
    private readonly ConcurrentDictionary<ulong, IAudioChannel> _audioChannels = new();
    private readonly TaskCompletionSource<ClientInformation> _ready;

    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;
    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    public DiscordNetBridge(DiscordSocketClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Logger = logger;
        _ready = new TaskCompletionSource<ClientInformation>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.UserVoiceStateUpdated += OnVoiceStateUpdated;
        client.Ready += OnClientReady;
        client.Log += OnClientLog;

        if (client.CurrentUser is { } user)
            _ready.TrySetResult(new ClientInformation("Discord.Net", user.Id));
    }

    public DiscordNetBridge(DiscordShardedClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        Logger = logger;
        _ready = new TaskCompletionSource<ClientInformation>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        client.VoiceServerUpdated += OnVoiceServerUpdated;
        client.UserVoiceStateUpdated += OnVoiceStateUpdated;
        client.ShardReady += OnShardReady;
        client.Log += OnClientLog;

        if (client.CurrentUser is { } shardedUser)
            _ready.TrySetResult(new ClientInformation("Discord.Net", shardedUser.Id));
    }

    /// <inheritdoc />
    public ILogger? Logger { get; }

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

        // Leave: only the channel kept from the join can be disconnected through the public
        // route, there is nothing to send when Nyra never joined through this bridge.
        if (channelId is null)
        {
            if (_audioChannels.TryRemove(guildId, out var channel))
                await channel.DisconnectAsync().ConfigureAwait(false);
            return;
        }

        var guild =
            _client.GetGuild(guildId)
            ?? throw new InvalidOperationException(
                $"Could not find the guild {guildId}, it is not available to the Discord.Net client."
            );

        if (guild.GetChannel(channelId.Value) is not IAudioChannel audioChannel)
            throw new ArgumentException(
                $"The channel {channelId.Value} is not a voice or stage channel.",
                nameof(channelId)
            );

        _audioChannels[guildId] = audioChannel;

        // The disconnect flag keeps Discord.Net from sending a voice state leave ahead of the
        // join, which would surface as a spurious channel-less update to the handshake.
        await audioChannel
            .ConnectAsync(selfDeaf, selfMute, external: true, disconnect: false)
            .ConfigureAwait(false);
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
        _client.Log -= OnClientLog;

        if (_client is DiscordSocketClient socketClient)
            socketClient.Ready -= OnClientReady;

        if (_client is DiscordShardedClient shardedClient)
            shardedClient.ShardReady -= OnShardReady;
    }

    private Task OnClientReady()
    {
        _ready.TrySetResult(new ClientInformation("Discord.Net", _client.CurrentUser.Id));
        return Task.CompletedTask;
    }

    private Task OnShardReady(DiscordSocketClient client)
    {
        _ready.TrySetResult(new ClientInformation("Discord.Net", client.CurrentUser.Id));
        return Task.CompletedTask;
    }

    private Task OnVoiceStateUpdated(
        SocketUser user,
        SocketVoiceState oldState,
        SocketVoiceState state
    )
    {
        if (user.Id != _client.CurrentUser?.Id)
            return Task.CompletedTask;

        var guildId = oldState.VoiceChannel?.Guild?.Id ?? state.VoiceChannel?.Guild?.Id;
        if (guildId is null)
            return Task.CompletedTask;

        VoiceStateUpdated?.Invoke(
            this,
            new VoiceStateUpdatedEventArgs(
                guildId.Value,
                new VoiceState(state.VoiceChannelId, state.VoiceSessionId)
            )
        );
        return Task.CompletedTask;
    }

    private Task OnVoiceServerUpdated(SocketVoiceServer voiceServer)
    {
        // A null endpoint means the update is partial, the bridge's contract is a non-null one.
        if (voiceServer.Endpoint is null)
            return Task.CompletedTask;

        VoiceServerUpdated?.Invoke(
            this,
            new VoiceServerUpdatedEventArgs(
                voiceServer.Guild.Id,
                new VoiceServer(voiceServer.Token, voiceServer.Endpoint)
            )
        );
        return Task.CompletedTask;
    }

    private Task OnClientLog(LogMessage message)
    {
        // Forward Discord.Net's logging into the logger given to the bridge.
        Logger?.Log(
            ToLogLevel(message.Severity),
            default,
            message,
            message.Exception,
            static (state, _) => $"Discord.Net ({state.Source}): {state.Message}"
        );
        return Task.CompletedTask;
    }

    private static LogLevel ToLogLevel(LogSeverity severity) =>
        severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
}
