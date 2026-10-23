using System.Runtime.CompilerServices;
using DSharpPlus;
using Microsoft.Extensions.DependencyInjection;

namespace Nyra.DSharpPlus.Nightly;

/// <summary>Entry points for enabling and accessing Nyra on DSharpPlus nightly clients.</summary>
public static class DiscordClientBuilderExtensions
{
    private static readonly Lock UseNyraGate = new();

    private static readonly ConditionalWeakTable<
        DiscordClientBuilder,
        NyraExtension
    > BuilderExtensions = new();
    private static readonly ConditionalWeakTable<DiscordClient, NyraExtension> Extensions = new();

    /// <summary>Enables Nyra on a DSharpPlus client being built.</summary>
    /// <remarks>
    /// The DSharpPlus nightly line only accepts event handlers at build time (<c>ConfigureEventHandlers</c>),
    /// so Nyra must be enabled on the <see cref="DiscordClientBuilder"/> before <c>Build</c>. The extension is
    /// retrieved from the built client with <see cref="GetNyra"/>.
    /// <para>
    /// A sharded client built with <c>CreateSharded</c> needs exactly one registration, its orchestrator
    /// routes guild-scoped payloads by the guild's shard slot. A guild outside this process's shard set
    /// throws, and with a nonzero stride a guild inside the set can be routed to the wrong shard.
    /// </para>
    /// <para>
    /// Disposal is yours, the container only disposes the services it created itself. The nodes keep running
    /// until <see cref="NyraExtension.DisposeAsync"/> is called on the instance this method registered.
    /// </para>
    /// <para>
    /// The event handler collection accepts registrations only, no removal. Enabling Nyra again on the same
    /// builder after disposing the extension leaves the disposed pair registered as no-ops, so build a fresh
    /// builder to re-enable Nyra.
    /// </para>
    /// <para>
    /// Build the builder at most once while this Nyra pair is alive. Every Build constructs a new client and the
    /// shared pair binds to the first one, so the later clients' voice and session events never arrive.
    /// </para>
    /// </remarks>
    /// <param name="builder">The client builder to enable Nyra on.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DiscordClientBuilder UseNyra(this DiscordClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        lock (UseNyraGate)
        {
            if (BuilderExtensions.TryGetValue(builder, out var existing) && !existing.IsDisposed)
                return builder;

            var bridge = new DSharpPlusBridge();
            var extension = new NyraExtension(bridge);
            BuilderExtensions.Remove(builder);
            BuilderExtensions.Add(builder, extension);

            builder.ConfigureServices(services =>
            {
                services.AddSingleton(bridge);
                services.AddSingleton(extension);
            });
            builder.ConfigureEventHandlers(events =>
                events
                    .HandleClientStarted(bridge.OnClientStarted)
                    .HandleSessionCreated(bridge.OnSessionCreated)
                    .HandleGuildDeleted(bridge.OnGuildDeleted)
                    .HandleVoiceStateUpdated(bridge.OnVoiceStateUpdated)
                    .HandleVoiceServerUpdated(bridge.OnVoiceServerUpdated)
            );

            return builder;
        }
    }

    /// <summary>Gets the Nyra extension of a client built by a <see cref="UseNyra"/> builder.</summary>
    /// <param name="client">The built client to read the extension of.</param>
    /// <returns>The extension of the client, null when Nyra was not enabled on its builder, or its extension
    /// was disposed.
    /// </returns>
    public static NyraExtension? GetNyra(this DiscordClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (Extensions.TryGetValue(client, out var extension) && !extension.IsDisposed)
            return extension;

        var resolved = client.ServiceProvider?.GetService<NyraExtension>();
        if (resolved is { } live && !live.IsDisposed)
        {
            lock (UseNyraGate)
            {
                Extensions.Remove(client);
                Extensions.Add(client, live);
            }
            return live;
        }

        return null;
    }

    /// <summary>Gets the Nyra extension of a sharded client, one built through the sharded builder.</summary>
    /// <remarks>
    /// <see cref="DiscordClient.AllShardsConnected"/> is a live flag, not an awaitable, callers that need
    /// every shard online before subscribing nodes can poll it, the extension itself is safe to read as soon
    /// as the client is built.
    /// </remarks>
    /// <param name="client">The sharded client (built with CreateSharded) to read the extension of.</param>
    /// <returns>
    /// A single entry under key 0, regardless of the client's shard ids, the one extension covers every shard.
    /// </returns>
    public static Task<IReadOnlyDictionary<int, NyraExtension>> GetNyraAsync(
        this DiscordClient client
    )
    {
        ArgumentNullException.ThrowIfNull(client);

        var extensions = new Dictionary<int, NyraExtension>();
        if (client.GetNyra() is { } extension)
            extensions[0] = extension;

        return Task.FromResult<IReadOnlyDictionary<int, NyraExtension>>(extensions);
    }
}
