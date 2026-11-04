using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.Internal;

internal sealed class NodeBridgeWiring
{
    private readonly Node _node;
    private volatile IDiscordBridge? _wiredClient;

    internal NodeBridgeWiring(Node node) => _node = node;

    internal IDiscordBridge? WiredClient => _wiredClient;

    internal void Wire(IDiscordBridge client)
    {
        if (_wiredClient is not null)
            return;

        client.VoiceStateUpdated += OnDiscordVoiceStateUpdated;
        client.VoiceServerUpdated += OnDiscordVoiceServerUpdated;
        if (client is IGuildRemovalSource guildRemoval)
            guildRemoval.GuildRemoved += OnDiscordGuildRemoved;

        _wiredClient = client;
    }

    internal void Unwire()
    {
        if (_wiredClient is not { } client)
            return;

        client.VoiceStateUpdated -= OnDiscordVoiceStateUpdated;
        client.VoiceServerUpdated -= OnDiscordVoiceServerUpdated;
        if (client is IGuildRemovalSource guildRemoval)
            guildRemoval.GuildRemoved -= OnDiscordGuildRemoved;

        _wiredClient = null;
    }

    private void OnDiscordVoiceStateUpdated(object? sender, VoiceStateUpdatedEventArgs eventArgs)
    {
        if (
            _node.VoiceHandshake.HandleVoiceStateUpdate(
                sender as IDiscordBridge ?? _node.Client!,
                eventArgs
            )
        )
            return;

        if (_node.GetConnection(eventArgs.GuildId) is { } connection)
        {
            connection.ObserveSelfFlags(
                eventArgs.VoiceState.SelfDeaf,
                eventArgs.VoiceState.SelfMute
            );

            var channelId = eventArgs.VoiceState.ChannelId;
            if (channelId is null)
            {
                if (
                    connection.IsConnected
                    && !connection.IsTearingDown
                    && _node.Logger?.IsEnabled(LogLevel.Debug) == true
                )
                    try
                    {
                        _node.Logger?.LogDebug(
                            LogEvents.Node,
                            "The gateway reported no voice channel for guild {GuildId} on a connected connection, treating it as a leave. On the cache-backed bridges (DSharpPlus 4.x, Discord.Net) an unresolved channel reports the same null, and a move into it would tear down a player that is still connected.",
                            eventArgs.GuildId
                        );
                    }
                    catch { }

                _ = _node.GuardAsync(connection.Teardown.HandleVoiceDisconnectedAsync());
            }
            else if (channelId != connection.ChannelId)
                connection.HandleVoiceMoved(channelId.Value, eventArgs.VoiceState.SessionId);
            else
                connection.HandleVoiceSessionUpdated(eventArgs.VoiceState.SessionId);
        }
    }

    private void OnDiscordGuildRemoved(object? sender, GuildRemovedEventArgs eventArgs)
    {
        _node.VoiceHandshake.FailPendingGuildRemoval(eventArgs.GuildId);

        if (_node.GetConnection(eventArgs.GuildId) is { } connection)
            _ = _node.GuardAsync(connection.Teardown.HandleVoiceDisconnectedAsync());
    }

    private void OnDiscordVoiceServerUpdated(object? sender, VoiceServerUpdatedEventArgs eventArgs)
    {
        if (
            _node.VoiceHandshake.HandleVoiceServerUpdate(
                sender as IDiscordBridge ?? _node.Client!,
                eventArgs
            )
        )
            return;

        if (
            !string.IsNullOrWhiteSpace(eventArgs.VoiceServer.Endpoint)
            && !string.IsNullOrWhiteSpace(eventArgs.VoiceServer.Token)
            && _node.GetConnection(eventArgs.GuildId) is { } connection
        )
            _ = _node.GuardAsync(
                connection.HandleVoiceServerUpdatedAsync(
                    eventArgs.VoiceServer.Token,
                    eventArgs.VoiceServer.Endpoint
                )
            );
    }
}
