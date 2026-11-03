using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.Abstractions;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using DspVoiceServerUpdatedEventArgs = DSharpPlus.EventArgs.VoiceServerUpdatedEventArgs;
using DspVoiceStateUpdatedEventArgs = DSharpPlus.EventArgs.VoiceStateUpdatedEventArgs;

namespace Nyra.DSharpPlus.Nightly;

/// <summary>
/// Wraps a <see cref="DiscordClient"/> for Nyra, forwards the voice events of the current user and sends the
/// raw gateway voice state update.
/// </summary>
/// <remarks>
/// The DSharpPlus nightly line (5.0.0-nightly-*) removed the C# events from the built client, subscriptions
/// happen through <c>DiscordClientBuilder.ConfigureEventHandlers</c> at build time only. This bridge is
/// therefore created empty, wired into the builder by <see cref="DiscordClientBuilderExtensions.UseNyra"/>,
/// and bound to the built client on the first dispatched event.
/// <para>
/// Sharded clients built with <c>CreateSharded</c> are plain <see cref="DiscordClient"/>s whose
/// <c>IShardOrchestrator</c> routes guild-scoped payloads by the guild's shard slot internally, so one
/// bridge covers plain and sharded setups alike. The slot math carries no shard offset, so a guild outside
/// this process's shard set throws (KeyNotFoundException or ArgumentOutOfRangeException, depending on the
/// shard id) and, with a nonzero stride, a guild inside the set can be routed to the wrong shard.
/// </para>
/// </remarks>
public sealed class DSharpPlusBridge : IDiscordBridge, IGuildRemovalSource, IDisposable
{
    private DiscordClient? _client;
    private readonly TaskCompletionSource<ClientInformation> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private volatile bool _disposed;

    /// <summary>Raised when Discord allocates or re-targets the voice server of a guild.</summary>
    public event EventHandler<Abstractions.VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <summary>Raised when the voice state of the current user changes in a guild.</summary>
    public event EventHandler<Abstractions.VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <summary>Raised when the bot was removed from a guild.</summary>
    /// <remarks>Not raised for guild outages.</remarks>
    public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

    internal DSharpPlusBridge() { }

    internal void Bind(DiscordClient client, bool gatewayOperational = true)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (ReferenceEquals(Volatile.Read(ref _client), client) && _ready.Task.IsCompleted)
            return;

        if (Interlocked.CompareExchange(ref _client, client, null) is { } bound)
        {
            if (!ReferenceEquals(bound, client))
            {
                try
                {
                    Logger?.LogError(
                        LogEvents.Bridge,
                        "The Nyra bridge was already bound to another DiscordClient, the events of the second client die inside the dispatcher's per-handler catch."
                    );
                }
                catch { }

                throw new InvalidOperationException(
                    "The Nyra bridge is already bound to another DiscordClient, use one bridge per client."
                );
            }

            if (gatewayOperational && client.CurrentUser is { } lateUser)
                _ready.TrySetResult(new ClientInformation(lateUser.Id));
            return;
        }

        Logger = client.Logger;
        if (gatewayOperational && client.CurrentUser is { } user)
            _ready.TrySetResult(new ClientInformation(user.Id));
    }

    /// <inheritdoc />
    public ILogger? Logger { get; private set; }

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

        var client = Volatile.Read(ref _client);
        if (client is null)
            throw new InvalidOperationException(
                "The Nyra bridge has not been bound to a DiscordClient yet, the event handlers only run after the client connects."
            );

        var payload = new Dictionary<string, object?>
        {
            ["guild_id"] = guildId.ToString(),
            ["channel_id"] = channelId?.ToString(),
            ["self_mute"] = selfMute,
            ["self_deaf"] = selfDeaf,
        };

#pragma warning disable DSP0004
        try
        {
            await client
                .SendPayloadAsync(GatewayOpCode.VoiceStateUpdate, payload, guildId)
                .ConfigureAwait(false);
        }
        catch (NullReferenceException exception) when (!client.AllShardsConnected)
        {
            throw new InvalidOperationException(
                "The DSharpPlus gateway is still connecting, it cannot send the voice state update yet.",
                exception
            );
        }
#pragma warning restore DSP0004
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
        _disposed = true;
        _ready.TrySetCanceled();
    }

    internal Task OnClientStarted(DiscordClient sender, ClientStartedEventArgs eventArgs)
    {
        if (_disposed)
            return Task.CompletedTask;

        if (
            !sender.Intents.HasIntent(DiscordIntents.Guilds)
            || !sender.Intents.HasIntent(DiscordIntents.GuildVoiceStates)
        )
            try
            {
                sender.Logger.LogCritical(
                    LogEvents.Intents,
                    "The Nyra extension is registered but the guilds and guild voice states intents are not both enabled. Voice connections cannot work without them."
                );
            }
            catch { }

        Bind(sender, gatewayOperational: false);
        return Task.CompletedTask;
    }

    internal Task OnSessionCreated(DiscordClient sender, SessionCreatedEventArgs eventArgs)
    {
        if (_disposed)
            return Task.CompletedTask;

        Bind(sender);
        return Task.CompletedTask;
    }

    internal Task OnGuildDeleted(DiscordClient sender, GuildDeletedEventArgs eventArgs)
    {
        if (_disposed)
            return Task.CompletedTask;

        if (!ReferenceEquals(_client, sender))
            return Task.CompletedTask;

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

    internal Task OnVoiceStateUpdated(DiscordClient sender, DspVoiceStateUpdatedEventArgs eventArgs)
    {
        if (_disposed)
            return Task.CompletedTask;

        Bind(sender);

        if (
            eventArgs.GuildId is not { } guildId
            || sender.CurrentUser is not { } self
            || eventArgs.UserId != self.Id
        )
            return Task.CompletedTask;

        BridgeEvent.RaiseIsolated(
            VoiceStateUpdated,
            this,
            new Abstractions.VoiceStateUpdatedEventArgs(
                guildId,
                new DiscordVoiceState(
                    eventArgs.ChannelId,
                    eventArgs.SessionId,
                    eventArgs.After.IsSelfDeafened,
                    eventArgs.After.IsSelfMuted
                )
            ),
            Logger
        );
        return Task.CompletedTask;
    }

    internal Task OnVoiceServerUpdated(
        DiscordClient sender,
        DspVoiceServerUpdatedEventArgs eventArgs
    )
    {
        if (_disposed)
            return Task.CompletedTask;

        Bind(sender);

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
            new Abstractions.VoiceServerUpdatedEventArgs(
                eventArgs.Guild.Id,
                new VoiceServer(eventArgs.VoiceToken, eventArgs.Endpoint)
            ),
            Logger
        );
        return Task.CompletedTask;
    }
}
