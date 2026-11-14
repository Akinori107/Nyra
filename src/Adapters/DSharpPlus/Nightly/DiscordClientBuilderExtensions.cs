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
    > BuilderExtensions = [];
    private static readonly ConditionalWeakTable<DiscordClient, NyraExtension> Extensions = [];

    /// <summary>Enables Nyra on a DSharpPlus client being built.</summary>
    /// <remarks>
    /// DSharpPlus nightly only takes event handlers at build time, so call this before <c>Build</c> and get the
    /// extension from the built client with <see cref="GetNyra"/>. A sharded client needs one call, and running
    /// only part of the shards in a process is not supported.
    /// <para>
    /// You dispose the extension with <see cref="NyraExtension.DisposeAsync"/>, the container does not. Build the
    /// builder only once, and use a new builder to enable Nyra again after disposing it.
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
            BuilderExtensions.AddOrUpdate(builder, extension);

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
                Extensions.AddOrUpdate(client, live);
            }
            return live;
        }

        return null;
    }

    /// <summary>Gets the Nyra extension of a sharded client, one built through the sharded builder.</summary>
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
