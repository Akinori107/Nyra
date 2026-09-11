using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Nyra.DSharpPlus;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Examples;

/// <summary>
/// Slash-command audio module: join/leave voice, play links or search terms, control playback
/// and apply filters. Slow operations defer the interaction first, well within Discord's
/// three-second response deadline.
/// </summary>
public sealed class MusicCommands : ApplicationCommandModule
{
    [SlashCommand("ping", "Replies with pong")]
    public async Task PingAsync(InteractionContext context) =>
        await context.CreateResponseAsync("Pong!").ConfigureAwait(false);

    [SlashCommand("join", "Joins the voice channel you are in")]
    public async Task JoinAsync(InteractionContext context)
    {
        // The voice handshake can take seconds, acknowledge within the deadline first.
        await context.DeferAsync().ConfigureAwait(false);

        var connection = await ConnectToCallerAsync(context).ConfigureAwait(false);
        if (connection is null)
        {
            await EditResponseAsync(
                    context,
                    "You are not in a voice channel, or no audio node is ready yet."
                )
                .ConfigureAwait(false);
            return;
        }

        var mention = connection.ChannelId is { } channelId
            ? $"<#{channelId}>"
            : "the voice channel";
        await EditResponseAsync(context, $"Joined {mention}.").ConfigureAwait(false);
    }

    [SlashCommand("leave", "Leaves the voice channel")]
    public async Task LeaveAsync(InteractionContext context)
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection.DisconnectAsync().ConfigureAwait(false);
        await context.CreateResponseAsync("Left the voice channel.").ConfigureAwait(false);
    }

    [SlashCommand("play", "Plays a track from a link, or searches for one")]
    public async Task PlayAsync(
        InteractionContext context,
        [Option("query", "A direct link, or a term to search for")] string query
    )
    {
        // Track loading round-trips the audio node, acknowledge within the deadline first.
        await context.DeferAsync().ConfigureAwait(false);

        var connection = await ConnectToCallerAsync(context).ConfigureAwait(false);
        if (connection is null)
        {
            await EditResponseAsync(
                    context,
                    "You are not in a voice channel, or no audio node is ready yet."
                )
                .ConfigureAwait(false);
            return;
        }

        // SearchType.Auto (the default): links are sent as-is, plain terms search YouTube.
        var result = await connection.Node.LoadTracksAsync(query).ConfigureAwait(false);

        if (result is LoadResult.LoadFailed { Error.Message: { } failure })
        {
            await EditResponseAsync(context, $"Failed to load the track: {failure}")
                .ConfigureAwait(false);
            return;
        }

        var track = result switch
        {
            LoadResult.TrackLoaded loaded => loaded.Track,
            LoadResult.SearchLoaded search => search.Tracks.FirstOrDefault(),
            LoadResult.PlaylistLoaded playlist => playlist.PlaylistInfo.SelectedTrack >= 0
                ? playlist.Tracks[playlist.PlaylistInfo.SelectedTrack]
                : playlist.Tracks.FirstOrDefault(),
            _ => null,
        };

        if (track is null)
        {
            await EditResponseAsync(context, "No tracks found.").ConfigureAwait(false);
            return;
        }

        await connection.PlayAsync(track).ConfigureAwait(false);
        await EditResponseAsync(context, $"Now playing: {track.Info}").ConfigureAwait(false);
    }

    [SlashCommand("pause", "Pauses playback")]
    public async Task PauseAsync(InteractionContext context)
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection.PauseAsync().ConfigureAwait(false);
        await context.CreateResponseAsync("Paused.").ConfigureAwait(false);
    }

    [SlashCommand("resume", "Resumes playback")]
    public async Task ResumeAsync(InteractionContext context)
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection.ResumeAsync().ConfigureAwait(false);
        await context.CreateResponseAsync("Resumed.").ConfigureAwait(false);
    }

    [SlashCommand("stop", "Stops playback and clears the track")]
    public async Task StopAsync(InteractionContext context)
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection.StopAsync().ConfigureAwait(false);
        await context.CreateResponseAsync("Stopped.").ConfigureAwait(false);
    }

    [SlashCommand("seek", "Seeks to a position in the current track, in seconds")]
    public async Task SeekAsync(
        InteractionContext context,
        [Option("seconds", "The position in seconds, e.g. 42 or 42.5")] [Minimum(0)] double seconds
    )
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var position = TimeSpan.FromSeconds(seconds);
        await connection.SeekAsync(position).ConfigureAwait(false);
        await context
            .CreateResponseAsync($"Seeked to {FormatPosition(position)}.")
            .ConfigureAwait(false);
    }

    [SlashCommand("volume", "Sets the volume, 0-1000, where 100 is the default")]
    public async Task VolumeAsync(
        InteractionContext context,
        [Option("volume", "The volume, 0-1000")] [Minimum(0)] [Maximum(1000)] long volume
    )
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection.SetVolumeAsync((int)volume).ConfigureAwait(false);
        await context.CreateResponseAsync($"Volume set to {volume}.").ConfigureAwait(false);
    }

    [SlashCommand("nowplaying", "Shows the current track")]
    public async Task NowPlayingAsync(InteractionContext context)
    {
        var connection = GetConnection(context);
        var track = connection?.CurrentTrack;
        if (connection is null || track is null)
        {
            await context
                .CreateResponseAsync("Nothing is playing.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var embed = new DiscordEmbedBuilder
        {
            Title = track.Info.Title,
            Url = track.Info.Uri,
            Description = $"by {track.Info.Author}",
            Color = new DiscordColor(0x5865F2),
        };
        embed.AddField("Source", track.Info.SourceName, inline: true);
        embed.AddField(
            "Length",
            track.Info.IsStream ? "live" : FormatPosition(track.Info.Length),
            inline: true
        );
        if (connection.Position is { } position)
            embed.AddField("Position", FormatPosition(position), inline: true);
        await context.CreateResponseAsync(embed).ConfigureAwait(false);
    }

    [SlashCommand("nightcore", "Speeds up and pitches up the audio")]
    public async Task NightcoreAsync(InteractionContext context)
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection
            .AddFilterAsync(new TimescaleFilter(Speed: 1.5, Pitch: 1.5))
            .ConfigureAwait(false);
        await context.CreateResponseAsync("Nightcore mode on.").ConfigureAwait(false);
    }

    [SlashCommand("daycore", "Slows down and pitches down the audio")]
    public async Task DaycoreAsync(InteractionContext context)
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection
            .AddFilterAsync(new TimescaleFilter(Speed: 0.75, Pitch: 0.75))
            .ConfigureAwait(false);
        await context.CreateResponseAsync("Daycore mode on.").ConfigureAwait(false);
    }

    [SlashCommand("reset", "Clears all audio filters")]
    public async Task ResetAsync(InteractionContext context)
    {
        if (GetConnection(context) is not { } connection)
        {
            await context
                .CreateResponseAsync("I am not connected to a voice channel here.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await connection.ClearFiltersAsync().ConfigureAwait(false);
        await context.CreateResponseAsync("Filters cleared.").ConfigureAwait(false);
    }

    private static GuildConnection? GetConnection(InteractionContext context) =>
        context.Guild is null ? null : context.Client.GetNyra()?.GetConnection(context.Guild.Id);

    private static async Task<GuildConnection?> ConnectToCallerAsync(InteractionContext context)
    {
        var channel = context.Member?.VoiceState?.Channel;
        if (channel is null)
            return null;

        var nyra = context.Client.GetNyra();
        if (nyra is null)
            return null;

        // A guild plays on one node, reuse its existing connection instead of joining a
        // second time through the least-loaded node.
        if (context.Guild is not null && nyra.GetConnection(context.Guild.Id) is { } existing)
            return existing;

        return nyra.GetNode() is { } node
            ? await node.ConnectAsync(channel).ConfigureAwait(false)
            : null;
    }

    private static Task EditResponseAsync(InteractionContext context, string content) =>
        context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(content));

    private static string FormatPosition(TimeSpan position) =>
        $"{(int)position.TotalHours}:{position.Minutes:D2}:{position.Seconds:D2}";
}
