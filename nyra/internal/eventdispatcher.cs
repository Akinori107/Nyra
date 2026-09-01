using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nyra.Entities;
using Nyra.EventArgs;

namespace Nyra.Internal;

/// <summary>
/// Routes websocket operations to the node. Runs synchronously on the receive loop: handlers
/// run inline, and only the session-resuming REST call of <c>ready</c> continues asynchronously.
/// </summary>
internal sealed class NodeEventDispatcher
{
    private const int OpReady = 1;
    private const int OpStats = 2;
    private const int OpPlayerUpdate = 3;
    private const int OpEvent = 4;

    private const int EventTrackStart = 1;
    private const int EventTrackEnd = 2;
    private const int EventTrackException = 3;
    private const int EventTrackStuck = 4;
    private const int EventWebSocketClosed = 5;
    private const int EventLyricsFound = 6;
    private const int EventLyricsNotFound = 7;
    private const int EventLyricsLine = 8;
    private const int EventSponsorBlockSegmentsLoaded = 9;
    private const int EventSponsorBlockSegmentSkipped = 10;

    private readonly Node _node;

    public NodeEventDispatcher(Node node)
    {
        _node = node;
    }

    public void Dispatch(ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        reader.Read(); // StartObject

        // Copy kept at the object start: typed deserialization re-reads from there.
        var payload = reader;

        var op = 0;
        var type = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("op"u8))
            {
                reader.Read();
                op = ReadOperation(ref reader);
                if (type != 0)
                    break;
            }
            else if (reader.ValueTextEquals("type"u8))
            {
                reader.Read();
                type = ReadEventType(ref reader);
                if (op != 0)
                    break;
            }
            else
            {
                reader.Skip();
            }
        }

        switch (op)
        {
            case OpReady:
            {
                var args = Deserialize<NodeReadyEventArgs>(ref payload);
                if (args is not null)
                    RunReadyAsync(args);
                break;
            }

            case OpStats:
            {
                var stats = Deserialize<Stats>(ref payload);
                if (stats is not null)
                    _node.HandleStats(stats);
                break;
            }

            case OpPlayerUpdate:
            {
                var update = Deserialize<PlayerUpdateEventArgs>(ref payload);
                if (update is not null)
                    _node.GetConnection(update.GuildId)?.HandlePlayerUpdate(update);
                break;
            }

            case OpEvent:
                DispatchEvent(ref payload, type);
                break;

            default:
                // Cold path: pay one string allocation to make the log useful — only when Debug is on.
                if (_node.Logger?.IsEnabled(LogLevel.Debug) == true)
                    _node.Logger.LogDebug(
                        "Node {NodeName} sent an unknown operation '{Operation}'.",
                        _node.Name,
                        ReadOperationName(data)
                    );
                break;
        }
    }

    private void DispatchEvent(ref Utf8JsonReader payload, int type)
    {
        switch (type)
        {
            case EventTrackStart:
            {
                var args = Deserialize<TrackStartedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleTrackStart(args);
                break;
            }

            case EventTrackEnd:
            {
                var args = Deserialize<TrackFinishedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleTrackEnd(args);
                break;
            }

            case EventTrackException:
            {
                var args = Deserialize<TrackExceptionEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleTrackException(args);
                break;
            }

            case EventTrackStuck:
            {
                var args = Deserialize<TrackStuckEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleTrackStuck(args);
                break;
            }

            case EventWebSocketClosed:
            {
                var args = Deserialize<WebsocketClosedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleWebSocketClosed(args);
                break;
            }

            case EventLyricsFound:
            {
                var args = Deserialize<LyricsFoundEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleLyricsFound(args);
                break;
            }

            case EventLyricsNotFound:
            {
                var args = Deserialize<LyricsNotFoundEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleLyricsNotFound(args);
                break;
            }

            case EventLyricsLine:
            {
                var args = Deserialize<LyricsLineEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleLyricsLine(args);
                break;
            }

            case EventSponsorBlockSegmentsLoaded:
            {
                var args = Deserialize<SponsorBlockSegmentsLoadedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleSponsorBlockSegmentsLoaded(args);
                break;
            }

            case EventSponsorBlockSegmentSkipped:
            {
                var args = Deserialize<SponsorBlockSegmentSkippedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleSponsorBlockSegmentSkipped(args);
                break;
            }

            default:
                // NodeLink emits extra event types (player lifecycle, seek, volume and so on);
                // they are not part of the shared protocol and are ignored.
                if (_node.Logger?.IsEnabled(LogLevel.Debug) == true)
                    _node.Logger.LogDebug(
                        "Node {NodeName} sent an unknown event type '{EventType}'.",
                        _node.Name,
                        ReadEventTypeName(ref payload)
                    );
                break;
        }
    }

    private async void RunReadyAsync(NodeReadyEventArgs args)
    {
        try
        {
            // HandleReadyAsync runs synchronously up to the session-resuming REST call, so the
            // session id is set before the next message is processed; only the PATCH continues
            // off the receive loop.
            await _node.HandleReadyAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _node.HandleError(exception);
        }
    }

    private static int ReadOperation(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("ready"u8))
            return OpReady;
        if (reader.ValueTextEquals("stats"u8))
            return OpStats;
        if (reader.ValueTextEquals("playerUpdate"u8))
            return OpPlayerUpdate;
        if (reader.ValueTextEquals("event"u8))
            return OpEvent;
        return 0;
    }

    private static int ReadEventType(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("TrackStartEvent"u8))
            return EventTrackStart;

        if (reader.ValueTextEquals("TrackEndEvent"u8))
            return EventTrackEnd;

        if (reader.ValueTextEquals("TrackExceptionEvent"u8))
            return EventTrackException;

        if (reader.ValueTextEquals("TrackStuckEvent"u8))
            return EventTrackStuck;

        if (reader.ValueTextEquals("WebSocketClosedEvent"u8))
            return EventWebSocketClosed;

        if (reader.ValueTextEquals("LyricsFoundEvent"u8))
            return EventLyricsFound;

        if (reader.ValueTextEquals("LyricsNotFoundEvent"u8))
            return EventLyricsNotFound;

        if (reader.ValueTextEquals("LyricsLineEvent"u8))
            return EventLyricsLine;

        if (reader.ValueTextEquals("SponsorBlockSegmentsLoadedEvent"u8))
            return EventSponsorBlockSegmentsLoaded;

        if (reader.ValueTextEquals("SponsorBlockSegmentSkippedEvent"u8))
            return EventSponsorBlockSegmentSkipped;

        return 0;
    }

    /// <summary>Cold-path helper: recovers the operation name for logging unknown operations.</summary>
    private static string? ReadOperationName(ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        reader.Read(); // StartObject
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("op"u8))
            {
                reader.Read();
                return reader.GetString();
            }

            reader.Skip();
        }

        return null;
    }

    /// <summary>Cold-path helper: recovers the event type name for logging unknown events.</summary>
    private static string? ReadEventTypeName(ref Utf8JsonReader payload)
    {
        var reader = payload;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("type"u8))
            {
                reader.Read();
                return reader.GetString();
            }

            reader.Skip();
        }

        return null;
    }

    private static T? Deserialize<T>(ref Utf8JsonReader reader) =>
        JsonSerializer.Deserialize<T>(ref reader, NyraJson.SerializerOptions);
}
