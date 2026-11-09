using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Nyra.DiscordNet;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.Queue;

namespace Nyra.Examples;

[CommandContextType(InteractionContextType.Guild)]
internal sealed class MusicCommands : InteractionModuleBase<SocketInteractionContext>
{
    private const int QueuePageSize = 10;

    private static readonly ConditionalWeakTable<
        GuildQueue,
        StrongBox<IMessageChannel>
    > QueueChannels = [];
    private static readonly Lock QueueChannelsGate = new();
    private static readonly AsyncLocal<bool> InCommand = new();

    public static ILogger? Logger { get; set; }

    [SlashCommand("join", "Join your voice channel")]
    public async Task JoinAsync()
    {
        if (await ConnectToCallerAsync().ConfigureAwait(false) is not { } connection)
            return;

        var mention = connection.ChannelId is { } channelId
            ? $"<#{channelId}>"
            : "the voice channel";
        await EditResponseAsync($"Joined {mention}.").ConfigureAwait(false);
    }

    [SlashCommand("leave", "Leave the voice channel")]
    public async Task LeaveAsync()
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        await connection.UseQueue().StopAsync().ConfigureAwait(false);
        await connection.DisconnectAsync().ConfigureAwait(false);
        await RespondAsync("Left the voice channel.").ConfigureAwait(false);
    }

    [SlashCommand("play", "Play a song from a link or a search")]
    public async Task PlayAsync(
        [Autocomplete(typeof(TrackHandler))]
        [Summary("query", "A link, or the name of a song")]
            string query,
        [Autocomplete(typeof(SourceHandler))]
        [Summary("source", "Where to search, like YouTube Music or Spotify")]
            string? source = null
    )
    {
        if (
            await ResolveSourceAsync(SearchSources.IsLink(query) ? null : source)
                .ConfigureAwait(false)
            is not { } searchType
        )
            return;

        if (await ConnectToCallerAsync().ConfigureAwait(false) is not { } connection)
            return;

        var result = await connection.Node.LoadTracksAsync(query, searchType).ConfigureAwait(false);

        if (result is LoadResult.LoadFailed { Error.Message: { } failure })
        {
            await FailDeferredAsync($"Couldn't load that track: {failure}").ConfigureAwait(false);
            return;
        }

        var tracks = TracksToQueue(result);
        if (tracks.Count == 0)
        {
            await FailDeferredAsync("No results found.").ConfigureAwait(false);
            return;
        }

        var summary = result is LoadResult.PlaylistLoaded { PlaylistInfo: var info }
            ? $"Added {tracks.Count} tracks from {info.Name} to the queue."
            : $"Added {tracks[0].Info.Title} to the queue.";
        await EnqueueAsync(connection, tracks, summary).ConfigureAwait(false);
    }

    [SlashCommand("mix", "Add a bunch of songs by genre or artist")]
    public async Task MixAsync(
        [Summary("name", "A genre or an artist, like Pop")] string name,
        [Summary("count", "How many songs to add")] [MinValue(1)] [MaxValue(25)] long count = 10,
        [Autocomplete(typeof(SourceHandler))]
        [Summary("source", "Where to search, like YouTube Music or Spotify")]
            string? source = null
    )
    {
        if (SearchSources.IsLink(name))
        {
            await ReplyEphemeralAsync("A mix is made from a name, use /play for a link.")
                .ConfigureAwait(false);
            return;
        }

        if (await ResolveSourceAsync(source).ConfigureAwait(false) is not { } searchType)
            return;

        if (await ConnectToCallerAsync().ConfigureAwait(false) is not { } connection)
            return;

        var result = await connection.Node.LoadTracksAsync(name, searchType).ConfigureAwait(false);
        if (result is not LoadResult.SearchLoaded { Tracks: { Count: > 0 } found })
        {
            await FailDeferredAsync($"No results found for {name}.").ConfigureAwait(false);
            return;
        }

        var tracks = found.Take((int)count).ToList();
        await EnqueueAsync(
                connection,
                tracks,
                $"Added {tracks.Count} tracks for {name} to the queue."
            )
            .ConfigureAwait(false);
    }

    [SlashCommand("queue", "See what is playing and what is next")]
    public async Task ShowQueueAsync()
    {
        if (await RequireConnectionAsync().ConfigureAwait(false) is not { } connection)
            return;

        if (BuildQueuePage(connection, 0) is not { } view)
        {
            await ReplyEphemeralAsync("The queue is empty.").ConfigureAwait(false);
            return;
        }

        await RespondAsync(embed: view.Embed, components: view.Buttons, ephemeral: true)
            .ConfigureAwait(false);
    }

    [ComponentInteraction("queue:*")]
    public async Task ShowQueuePageAsync(int page)
    {
        if (await RequireConnectionAsync().ConfigureAwait(false) is not { } connection)
            return;

        if (BuildQueuePage(connection, page) is not { } view)
        {
            await ReplyEphemeralAsync("The queue is empty.").ConfigureAwait(false);
            return;
        }

        await ((SocketMessageComponent)Context.Interaction)
            .UpdateAsync(message =>
            {
                message.Embed = view.Embed;
                message.Components = view.Buttons;
            })
            .ConfigureAwait(false);
    }

    [SlashCommand("history", "See the songs that played before")]
    public async Task ShowHistoryAsync()
    {
        if (await RequireConnectionAsync().ConfigureAwait(false) is not { } connection)
            return;

        var history = connection.UseQueue().History;
        if (history.Count == 0)
        {
            await ReplyEphemeralAsync("Nothing has played yet.").ConfigureAwait(false);
            return;
        }

        var embed = new EmbedBuilder
        {
            Title = "History",
            Description = string.Join(
                '\n',
                history
                    .Reverse()
                    .Take(QueuePageSize)
                    .Select((track, index) => $"{index + 1}. {track.Info.Title}")
            ),
            Color = new Color(0x5865F2),
            Footer =
                history.Count > QueuePageSize
                    ? new EmbedFooterBuilder
                    {
                        Text = $"Showing the last {QueuePageSize} of {history.Count}",
                    }
                    : null,
        }.Build();

        await RespondAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("skip", "Skip to the next song")]
    public async Task SkipAsync(
        [Summary("number", "A number from /queue to jump to")]
        [MinValue(1)]
        [MaxValue(1000)]
            long number = 1
    )
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        if (connection.CurrentTrack is null)
        {
            await ReplyEphemeralAsync("Nothing is playing.").ConfigureAwait(false);
            return;
        }

        InCommand.Value = true;
        if (
            await connection.UseQueue().SkipAsync((int)number).ConfigureAwait(false) is not { } next
        )
        {
            await ReplyEphemeralAsync(
                    number == 1
                        ? "There's nothing next in the queue."
                        : $"There's no song number {number} in the queue."
                )
                .ConfigureAwait(false);
            return;
        }

        await RespondAsync($"Skipped. Now playing: {next.Info.Title}").ConfigureAwait(false);
    }

    [SlashCommand("shuffle", "Shuffle the songs that are next")]
    public async Task ShuffleAsync()
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        await connection.UseQueue().ShuffleAsync().ConfigureAwait(false);
        await RespondAsync("Shuffled the queue.").ConfigureAwait(false);
    }

    [SlashCommand("remove", "Remove a song from the queue")]
    public async Task RemoveAsync(
        [Summary("number", "A number from /queue")] [MinValue(1)] [MaxValue(1000)] long number
    )
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        if (
            await connection.UseQueue().RemoveAsync((int)number - 1).ConfigureAwait(false)
            is not { } removed
        )
        {
            await ReplyEphemeralAsync($"There's no song number {number} in the queue.")
                .ConfigureAwait(false);
            return;
        }

        await RespondAsync($"Removed {removed.Info.Title}.").ConfigureAwait(false);
    }

    [SlashCommand("move", "Move a song to another place in the queue")]
    public async Task MoveAsync(
        [Summary("from", "A number from /queue")] [MinValue(1)] [MaxValue(1000)] long from,
        [Summary("to", "The number it should have")] [MinValue(1)] [MaxValue(1000)] long to
    )
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        if (
            !await connection.UseQueue().MoveAsync((int)from - 1, (int)to - 1).ConfigureAwait(false)
        )
        {
            await ReplyEphemeralAsync("Both numbers need to be in the queue.")
                .ConfigureAwait(false);
            return;
        }

        await RespondAsync("Moved.").ConfigureAwait(false);
    }

    [SlashCommand("previous", "Go back to the song that played before")]
    public async Task PreviousAsync()
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        InCommand.Value = true;
        if (await connection.UseQueue().PreviousAsync().ConfigureAwait(false) is not { } previous)
        {
            await ReplyEphemeralAsync("There's no previous song.").ConfigureAwait(false);
            return;
        }

        await RespondAsync($"Went back. Now playing: {previous.Info.Title}").ConfigureAwait(false);
    }

    [SlashCommand("clear", "Clear the queue and keep the current song")]
    public async Task ClearAsync()
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        await connection.UseQueue().ClearAsync().ConfigureAwait(false);
        await RespondAsync("Cleared the queue.").ConfigureAwait(false);
    }

    [SlashCommand("loop", "Repeat the song or the whole queue")]
    public async Task LoopAsync([Summary("mode", "What to repeat")] LoopMode mode)
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        connection.UseQueue().Loop = mode;

        var reply = mode switch
        {
            LoopMode.Track => "Looping this track.",
            LoopMode.Queue => "Looping the queue.",
            _ => "Loop is off.",
        };
        await RespondAsync(reply).ConfigureAwait(false);
    }

    [SlashCommand("stop", "Stop the music and clear the queue")]
    public async Task StopAsync()
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        await connection.UseQueue().StopAsync().ConfigureAwait(false);
        await RespondAsync("Stopped.").ConfigureAwait(false);
    }

    [SlashCommand("seek", "Seek to a time in the song")]
    public async Task SeekAsync(
        [Summary(
            "position",
            "A time like 90 or 1:34. Use +40 or -40 to move from the current time"
        )]
            string position
    )
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        if (connection.CurrentTrack is not { } track)
        {
            await ReplyEphemeralAsync("Nothing is playing.").ConfigureAwait(false);
            return;
        }

        if (!track.Info.IsSeekable)
        {
            await ReplyEphemeralAsync("You can't seek in this track.").ConfigureAwait(false);
            return;
        }

        if (!TryParseSeek(position, out var seconds, out var relative))
        {
            await ReplyEphemeralAsync("Try a time like 90, 1:34, +30 or -30.")
                .ConfigureAwait(false);
            return;
        }

        var requested = relative
            ? (connection.Position ?? TimeSpan.Zero).TotalSeconds + seconds
            : seconds;
        if (requested > track.Info.Length.TotalSeconds)
        {
            await ReplyEphemeralAsync("That's past the end of the track.").ConfigureAwait(false);
            return;
        }

        var target = TimeSpan.FromSeconds(Math.Max(0, requested));
        await connection.SeekAsync(target).ConfigureAwait(false);
        await RespondAsync(
                $"Seeked to {FormatPosition(target)}/{FormatPosition(track.Info.Length)}."
            )
            .ConfigureAwait(false);
    }

    [SlashCommand("volume", "Change the volume")]
    public async Task VolumeAsync(
        [Summary("volume", "From 0 to 1000, 100 is the default")]
        [MinValue(0)]
        [MaxValue(1000)]
            long volume
    )
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        await connection.SetVolumeAsync((int)volume).ConfigureAwait(false);
        await RespondAsync($"Volume set to {volume}%.").ConfigureAwait(false);
    }

    [SlashCommand("nowplaying", "See the song that is playing")]
    public async Task NowPlayingAsync()
    {
        var connection = GetConnection();
        var track = connection?.CurrentTrack;
        if (connection is null || track is null)
        {
            await ReplyEphemeralAsync("Nothing is playing.").ConfigureAwait(false);
            return;
        }

        var embed = new EmbedBuilder
        {
            Title = track.Info.Title,
            Url = track.Info.Uri,
            Description = $"by {track.Info.Author}",
            Color = new Color(0x5865F2),
        };
        embed.AddField("Source", track.Info.SourceName, inline: true);
        embed.AddField(
            "Length",
            track.Info.IsStream ? "live" : FormatPosition(track.Info.Length),
            inline: true
        );
        if (connection.Position is { } position)
            embed.AddField("Position", FormatPosition(position), inline: true);
        await RespondAsync(embed: embed.Build()).ConfigureAwait(false);
    }

    [SlashCommand("filter", "Change how the music sounds")]
    public async Task FilterAsync(
        [Autocomplete(typeof(FilterPresetHandler))]
        [Summary("preset", "The effect to use, off removes it")]
            string preset
    )
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        var found = FilterPresets.All.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, preset, StringComparison.OrdinalIgnoreCase)
        );
        if (found is null)
        {
            await ReplyEphemeralAsync($"There's no filter called '{preset}'.")
                .ConfigureAwait(false);
            return;
        }

        if (found.Filter is null && found.Bands is null)
        {
            await connection.Filters.ClearAsync().ConfigureAwait(false);
            await RespondAsync("Filters cleared.").ConfigureAwait(false);
            return;
        }

        var turnedOff = FilterPresets
            .All.Where(other =>
                other != found
                && other.Filter is { } active
                && active.GetType() == found.Filter?.GetType()
                && connection.Filters.IsApplied(active)
            )
            .Select(other => other.Name)
            .ToList();

        if (found.Filter is { } filter)
            await connection.Filters.AddAsync(filter).ConfigureAwait(false);
        else
            await connection.Filters.SetEqualizerAsync(found.Bands).ConfigureAwait(false);

        await RespondAsync(
                turnedOff.Count == 0
                    ? $"Applied the {found.Name} filter."
                    : $"Applied the {found.Name} filter, turned off {string.Join(", ", turnedOff)} so they don't conflict."
            )
            .ConfigureAwait(false);
    }

    [SlashCommand("crossfade", "Blend the end of a song into the next one (NodeLink only)")]
    public async Task CrossfadeAsync([Summary("enabled", "On or off")] bool enabled)
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        await connection
            .Effects.SetCrossfadeAsync(new CrossfadeOptions { Enabled = enabled })
            .ConfigureAwait(false);

        connection.UseQueue().PreloadNext = enabled;

        await RespondAsync($"Crossfade {(enabled ? "on" : "off")}.").ConfigureAwait(false);
    }

    [SlashCommand("ducking", "Lower the music when someone talks (NodeLink only)")]
    public async Task DuckingAsync(
        [Summary("enabled", "On or off")] bool enabled,
        [Summary("volume", "How loud the music stays while someone talks, in percent")]
        [MinValue(0)]
        [MaxValue(100)]
            long volume = 30
    )
    {
        if (await RequireControlAsync().ConfigureAwait(false) is not { } connection)
            return;

        if (enabled)
            await connection
                .Effects.SetFadingAsync(
                    new FadingOptions
                    {
                        Ducking = new DuckingOptions { TargetVolume = volume / 100f },
                    }
                )
                .ConfigureAwait(false);

        await connection.Effects.SetDuckingAsync(enabled).ConfigureAwait(false);
        await RespondAsync(
                enabled
                    ? $"Ducking on. The music drops to {volume}% when someone speaks."
                    : "Ducking off."
            )
            .ConfigureAwait(false);
    }

    [SlashCommand("status", "See which audio server is playing here")]
    public async Task StatusAsync()
    {
        if (await RequireNodeAsync().ConfigureAwait(false) is not { } node)
            return;

        var stats = node.Stats ?? await node.GetStatsAsync().ConfigureAwait(false);
        var embed = new EmbedBuilder { Title = node.Name, Color = new Color(0x5865F2) };
        embed.AddField(
            "Server",
            $"{(node.IsNodeLink ? "NodeLink" : "Lavalink")} {node.Version?.ToString() ?? "dev build"}",
            inline: true
        );
        embed.AddField(
            "Players",
            $"{stats.PlayingPlayers} playing of {stats.Players}",
            inline: true
        );
        embed.AddField(
            "Uptime",
            $"{(int)stats.UptimeDuration.TotalHours}h {stats.UptimeDuration.Minutes}m",
            inline: true
        );
        embed.AddField("Memory", $"{stats.Memory.Used / 1024 / 1024} MB", inline: true);
        embed.AddField(
            "CPU",
            $"{stats.Cpu.NodelinkLoad ?? stats.Cpu.LavalinkLoad:P0}",
            inline: true
        );
        if (stats.FrameStats is { } frames)
            embed.AddField(
                "Frames",
                $"{frames.Sent} sent, {frames.Nulled} nulled, {frames.Deficit} deficit",
                inline: true
            );
        await RespondAsync(embed: embed.Build(), ephemeral: true).ConfigureAwait(false);
    }

    internal static Node? NodeFor(IInteractionContext context)
    {
        var nyra = (context.Client as DiscordSocketClient)?.GetNyra();
        return (context.Guild?.Id is { } id ? nyra?.GetConnection(id)?.Node : null)
            ?? nyra?.GetNode();
    }

    private SocketVoiceChannel? CallerChannel => (Context.User as SocketGuildUser)?.VoiceChannel;

    private GuildConnection? GetConnection() =>
        Context.Client.GetNyra()?.GetConnection(Context.Guild.Id);

    private async Task<Node?> RequireNodeAsync()
    {
        if (NodeFor(Context) is { } node)
            return node;

        await ReplyEphemeralAsync("I'm not ready to play music yet, try again in a moment.")
            .ConfigureAwait(false);
        return null;
    }

    private async Task<GuildConnection?> RequireConnectionAsync()
    {
        if (GetConnection() is { } connection)
            return connection;

        await ReplyEphemeralAsync("I'm not in a voice channel.").ConfigureAwait(false);
        return null;
    }

    private async Task<GuildConnection?> RequireControlAsync()
    {
        if (await RequireConnectionAsync().ConfigureAwait(false) is not { } connection)
            return null;

        var channelId = CallerChannel?.Id;
        if (channelId is not null && channelId == connection.ChannelId)
            return connection;

        await ReplyEphemeralAsync("You need to be in my voice channel.").ConfigureAwait(false);
        return null;
    }

    private async Task<GuildConnection?> ConnectToCallerAsync()
    {
        if (CallerChannel is not { } channel)
        {
            await ReplyEphemeralAsync("Join a voice channel first.").ConfigureAwait(false);
            return null;
        }

        var nyra = Context.Client.GetNyra();
        if (nyra?.GetConnection(Context.Guild.Id) is { } existing)
        {
            if (existing.ChannelId != channel.Id)
            {
                await ReplyEphemeralAsync("I'm already playing in another voice channel.")
                    .ConfigureAwait(false);
                return null;
            }

            await DeferAsync().ConfigureAwait(false);
            return existing;
        }

        var permissions = Context.Guild.CurrentUser.GetPermissions(channel);
        if (!permissions.Connect || !permissions.Speak)
        {
            await ReplyEphemeralAsync(
                    $"I can't join {channel.Mention}, I need the Connect and Speak permissions."
                )
                .ConfigureAwait(false);
            return null;
        }

        if (await RequireNodeAsync().ConfigureAwait(false) is not { } node)
            return null;

        await DeferAsync().ConfigureAwait(false);
        return await node.ConnectAsync(channel).ConfigureAwait(false);
    }

    private static (Embed Embed, MessageComponent Buttons)? BuildQueuePage(
        GuildConnection connection,
        int page
    )
    {
        var queue = connection.UseQueue();
        var upcoming = queue.Upcoming;
        var current = connection.CurrentTrack;
        if (current is null && upcoming.Count == 0)
            return null;

        var lastPage = Math.Max(0, (upcoming.Count - 1) / QueuePageSize);
        page = Math.Clamp(page, 0, lastPage);

        var lines = new List<string>();
        if (current is not null)
            lines.Add($"Now playing: {current.Info.Title}");
        lines.AddRange(
            upcoming
                .Skip(page * QueuePageSize)
                .Take(QueuePageSize)
                .Select((track, index) => $"{page * QueuePageSize + index + 1}. {track.Info.Title}")
        );

        var footer =
            $"Loop: {queue.Loop} - Total queue duration: {FormatPosition(queue.TotalDuration)}";
        var embed = new EmbedBuilder
        {
            Title = "Queue",
            Description = string.Join('\n', lines),
            Color = new Color(0x5865F2),
            Footer = new EmbedFooterBuilder
            {
                Text = lastPage > 0 ? $"Page {page + 1}/{lastPage + 1} - {footer}" : footer,
            },
        }.Build();

        var buttons = new ComponentBuilder();
        if (lastPage > 0)
            buttons
                .WithButton(
                    emote: new Emoji("\u25C0\uFE0F"),
                    customId: $"queue:{page - 1}",
                    disabled: page == 0
                )
                .WithButton(
                    emote: new Emoji("\u25B6\uFE0F"),
                    customId: $"queue:{page + 1}",
                    disabled: page == lastPage
                );

        return (embed, buttons.Build());
    }

    private static IReadOnlyList<Track> TracksToQueue(LoadResult result) =>
        result switch
        {
            LoadResult.TrackLoaded loaded => [loaded.Track],
            LoadResult.SearchLoaded { Tracks: [var first, ..] } => [first],
            LoadResult.PlaylistLoaded playlist => playlist.Tracks,
            _ => [],
        };

    private async Task EnqueueAsync(
        GuildConnection connection,
        IReadOnlyList<Track> tracks,
        string summary
    )
    {
        var queue = connection.UseQueue();
        FollowQueue(queue, Context.Channel);
        InCommand.Value = true;
        var started = await queue.EnqueueAsync(tracks).ConfigureAwait(false);
        var reply = started switch
        {
            null => summary,
            _ when tracks.Count == 1 => $"Now playing: {started.Info.Title}",
            _ => $"{summary}\nNow playing: {started.Info.Title}",
        };
        await EditResponseAsync(reply).ConfigureAwait(false);
    }

    private static void FollowQueue(GuildQueue queue, IMessageChannel channel)
    {
        lock (QueueChannelsGate)
        {
            if (QueueChannels.TryGetValue(queue, out var existing))
            {
                existing.Value = channel;
                return;
            }

            var target = new StrongBox<IMessageChannel>(channel);
            QueueChannels.Add(queue, target);
            queue.Advanced += (_, args) =>
                InCommand.Value
                    ? Task.CompletedTask
                    : SendAsync(target, $"Now playing: {args.Track.Info.Title}");
            queue.TrackFailed += (_, args) =>
                SendAsync(target, $"Couldn't play {args.Track.Info.Title}, skipping.");
            queue.HandlerError += (_, args) =>
            {
                Logger?.LogWarning(args.Exception, "A queue handler threw");
                return Task.CompletedTask;
            };
            queue.Connection.TrackException += (_, args) =>
            {
                Logger?.LogWarning(
                    "Couldn't play {Track}: {Reason}",
                    args.Track?.Info.Title ?? "the track",
                    args.Exception.Message
                );
                return Task.CompletedTask;
            };
        }
    }

    private static async Task SendAsync(StrongBox<IMessageChannel> target, string message)
    {
        try
        {
            await target.Value!.SendMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger?.LogWarning(exception, "Couldn't send a message to the channel");
        }
    }

    private async Task<SearchType?> ResolveSourceAsync(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return SearchType.Auto;

        if (SearchSources.Find(source) is not { } found)
        {
            await ReplyEphemeralAsync($"There's no source called '{source}'.")
                .ConfigureAwait(false);
            return null;
        }

        if (
            NodeFor(Context) is { } node
            && !await SearchSources.IsAvailableAsync(found, node).ConfigureAwait(false)
        )
        {
            await ReplyEphemeralAsync($"{found.Name} isn't available on this server.")
                .ConfigureAwait(false);
            return null;
        }

        return found.Type;
    }

    private Task ReplyEphemeralAsync(string content) => RespondAsync(content, ephemeral: true);

    private async Task FailDeferredAsync(string content)
    {
        await DeleteOriginalResponseAsync().ConfigureAwait(false);
        await FollowupAsync(content, ephemeral: true).ConfigureAwait(false);
    }

    private Task<IUserMessage> EditResponseAsync(string content) =>
        ModifyOriginalResponseAsync(properties => properties.Content = content);

    private static bool TryParseSeek(string input, out double seconds, out bool relative)
    {
        seconds = 0;
        var text = input.Trim();
        var negative = text.StartsWith('-');
        relative = negative || text.StartsWith('+');
        if (relative)
            text = text[1..].TrimStart();

        var parts = text.Split(':');
        if (parts.Length > 3)
            return false;

        for (var index = 0; index < parts.Length; index++)
        {
            var style =
                index == parts.Length - 1 ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
            if (
                !double.TryParse(parts[index], style, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value)
                || (index > 0 && value >= 60)
            )
                return false;

            seconds = seconds * 60 + value;
        }

        if (negative)
            seconds = -seconds;

        return true;
    }

    private static string FormatPosition(TimeSpan position) =>
        position.TotalHours >= 1
            ? $"{(int)position.TotalHours}:{position.Minutes:D2}:{position.Seconds:D2}"
            : $"{position.Minutes}:{position.Seconds:D2}";
}

internal sealed record FilterPreset(
    string Name,
    IFilter? Filter,
    bool NodeLinkOnly = false,
    string? Kind = null,
    IReadOnlyList<EqualizerBand>? Bands = null
);

internal static class FilterPresets
{
    public static readonly FilterPreset Off = new("off", null);

    public static readonly IReadOnlyList<FilterPreset> All =
    [
        new("nightcore", new TimescaleFilter(Speed: 1.5, Pitch: 1.5), Kind: "timescale"),
        new("daycore", new TimescaleFilter(Speed: 0.75, Pitch: 0.75), Kind: "timescale"),
        new(
            "bassboost",
            null,
            Kind: "equalizer",
            Bands:
            [
                new EqualizerBand(0, 0.25f),
                new EqualizerBand(1, 0.2f),
                new EqualizerBand(2, 0.15f),
                new EqualizerBand(3, 0.1f),
            ]
        ),
        new(
            "karaoke",
            new KaraokeFilter(Level: 1f, MonoLevel: 1f, FilterBand: 220f, FilterWidth: 100f),
            Kind: "karaoke"
        ),
        new("8d", new RotationFilter(RotationHz: 0.2), Kind: "rotation"),
        new(
            "reverb",
            new ReverbFilter(RoomSize: 0.6f, Damping: 0.5f, Mix: 0.4f),
            NodeLinkOnly: true
        ),
        new("phonograph", new PhonographFilter(Depth: 0.3f, Crackle: 0.3f), NodeLinkOnly: true),
        Off,
    ];

    public static async Task<bool> IsAvailableAsync(FilterPreset preset, Node node)
    {
        var info = await NodeInfos.GetAsync(node).ConfigureAwait(false);
        if (node.IsNodeLink)
            return true;

        return !preset.NodeLinkOnly && (preset.Kind is null || info.Filters.Contains(preset.Kind));
    }
}

internal sealed record SearchSource(
    string Name,
    SearchType Type,
    string Manager,
    bool NodeLinkOnly = false
);

internal static class NodeInfos
{
    private static readonly ConcurrentDictionary<Node, NodeInfo> Cache = new();

    public static async Task<NodeInfo> GetAsync(Node node)
    {
        if (Cache.TryGetValue(node, out var cached))
            return cached;

        return Cache[node] = await node.GetNodeInfoAsync().ConfigureAwait(false);
    }
}

internal static class SearchSources
{
    public static readonly IReadOnlyList<SearchSource> All =
    [
        new("YouTube", SearchType.YouTube, "youtube"),
        new("YouTube Music", SearchType.YouTubeMusic, "youtube"),
        new("SoundCloud", SearchType.SoundCloud, "soundcloud"),
        new("Spotify", SearchType.Spotify, "spotify"),
        new("Apple Music", SearchType.AppleMusic, "applemusic"),
        new("Deezer", SearchType.Deezer, "deezer"),
        new("Bandcamp", SearchType.Bandcamp, "bandcamp", NodeLinkOnly: true),
    ];

    public static bool IsLink(string text) =>
        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static SearchSource? Find(string name) =>
        All.FirstOrDefault(source =>
            string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase)
        );

    public static async Task<bool> IsAvailableAsync(SearchSource source, Node node)
    {
        var info = await NodeInfos.GetAsync(node).ConfigureAwait(false);
        return info.SourceManagers.Contains(source.Manager, StringComparer.OrdinalIgnoreCase)
            && (!source.NodeLinkOnly || node.IsNodeLink);
    }
}

internal sealed class SourceHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services
    )
    {
        var node = MusicCommands.NodeFor(context);
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        var choices = new List<AutocompleteResult>();
        if (node is not null)
            try
            {
                foreach (var source in SearchSources.All)
                    if (
                        source.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)
                        && await SearchSources.IsAvailableAsync(source, node).ConfigureAwait(false)
                    )
                        choices.Add(new AutocompleteResult(source.Name, source.Name));
            }
            catch (Exception)
            {
                choices.Clear();
            }

        return AutocompletionResult.FromSuccess(choices);
    }
}

internal sealed class TrackHandler : AutocompleteHandler
{
    private const int MinLetters = 2;
    private const int MaxSuggestions = 5;
    private const int MaxLength = 100;
    private static readonly TimeSpan SuggestDelay = TimeSpan.FromSeconds(1.5);
    private static readonly ConcurrentDictionary<ulong, int> Latest = new();

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services
    )
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString()?.Trim() ?? string.Empty;
        var ticket = Latest.AddOrUpdate(context.User.Id, 1, (_, last) => last + 1);

        if (typed.Length < MinLetters || SearchSources.IsLink(typed))
            return AutocompletionResult.FromSuccess([]);

        await Task.Delay(SuggestDelay).ConfigureAwait(false);
        if (Latest.GetValueOrDefault(context.User.Id) != ticket)
            return AutocompletionResult.FromSuccess([]);

        var chosen = autocompleteInteraction
            .Data.Options.FirstOrDefault(option => option.Name == "source")
            ?.Value?.ToString();
        var searchType = chosen is null ? null : SearchSources.Find(chosen)?.Type;

        try
        {
            var node = MusicCommands.NodeFor(context);
            if (
                node is null
                || await node.LoadTracksAsync(typed, searchType ?? SearchType.Auto)
                    .ConfigureAwait(false)
                    is not LoadResult.SearchLoaded { Tracks: var tracks }
            )
                return AutocompletionResult.FromSuccess([]);

            return AutocompletionResult.FromSuccess(
                tracks
                    .Take(MaxSuggestions)
                    .Select(track => new AutocompleteResult(
                        Truncate($"{track.Info.Title} - {track.Info.Author}"),
                        track.Info.Uri is { Length: <= MaxLength } uri
                            ? uri
                            : Truncate(track.Info.Title)
                    ))
            );
        }
        catch (Exception)
        {
            return AutocompletionResult.FromSuccess([]);
        }
    }

    private static string Truncate(string text) =>
        text.Length <= MaxLength ? text : text[..(MaxLength - 3)] + "...";
}

internal sealed class FilterPresetHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services
    )
    {
        var node = MusicCommands.NodeFor(context);
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        var available = new List<FilterPreset>();
        if (node is not null)
            try
            {
                foreach (var preset in FilterPresets.All)
                    if (
                        preset != FilterPresets.Off
                        && await FilterPresets.IsAvailableAsync(preset, node).ConfigureAwait(false)
                    )
                        available.Add(preset);
            }
            catch (Exception)
            {
                available.Clear();
            }

        if (available.Count > 0)
            available.Add(FilterPresets.Off);

        return AutocompletionResult.FromSuccess(
            available
                .Where(preset => preset.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Select(preset => new AutocompleteResult(preset.Name, preset.Name))
        );
    }
}
