using DSharpPlus.Entities;

namespace Nyra.DSharpPlus.Nightly;

/// <summary>Nyra entry points that need DSharpPlus channel types.</summary>
public static class NodeExtensions
{
    /// <summary>Creates the guild connection on the node and joins the bot to the voice channel.</summary>
    /// <remarks>
    /// A stage channel joins the bot as audience, Nyra sends no request to speak, so audio only reaches it once
    /// a stage moderator unsuppresses the bot. A join for a channel with a live connection returns that
    /// connection, one racing a disconnect takes over the dying connection and returns a fresh one.
    /// </remarks>
    /// <param name="node">The node to connect through.</param>
    /// <param name="channel">The voice channel to join. A stage channel joins as audience.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The guild connection of the join, the existing one when its registry entry is still live.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="node"/> or <paramref name="channel"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The channel is not a voice or stage channel, or does not belong to a guild.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The channel's guild id or channel id is zero.</exception>
    /// <exception cref="ObjectDisposedException">The node was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The node is not started and ready yet, or the guild is already connected to another channel or through
    /// a different node, disconnect it before connecting again.
    /// </exception>
    /// <exception cref="Nyra.Exceptions.VoiceHandshakeException">
    /// The voice handshake of the join failed, see its message for the cause.
    /// </exception>
    /// <exception cref="Nyra.Exceptions.NodeUnreachableException">
    /// The node became unreachable between the handshake and the player registration.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// The node's REST endpoint did not answer the player registration within its bound.
    /// </exception>
    /// <exception cref="Nyra.Exceptions.RestException">
    /// The node rejected the player registration, see its message for the reply.
    /// </exception>
    /// <exception cref="OperationCanceledException">The join was cancelled through the token.</exception>
    public static Task<GuildConnection> ConnectAsync(
        this Node node,
        DiscordChannel channel,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.Type is not (DiscordChannelType.Voice or DiscordChannelType.Stage))
            throw new ArgumentException(
                "The channel must be a voice or stage channel.",
                nameof(channel)
            );

        if (channel.GuildId is null)
            throw new ArgumentException("The channel does not belong to a guild.", nameof(channel));

        return node.ConnectAsync(channel.GuildId.Value, channel.Id, cancellationToken);
    }
}
