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
    private const int EventMixStarted = 11;
    private const int EventMixEnded = 12;
    private const int EventVolumeChanged = 13;
    private const int EventFiltersChanged = 14;
    private const int EventSeek = 15;
    private const int EventPause = 16;
    private const int EventPlayerCreated = 17;
    private const int EventPlayerDestroyed = 18;
    private const int EventPlayerReconnecting = 19;
    private const int EventPlayerConnected = 20;
    private const int EventStreamMetadata = 21;
    private const int EventEternalBoxInfo = 22;
    private const int EventEternalBoxJump = 23;

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
                // Cold path: pay one string allocation to make the log useful, only when Debug is on.
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
                var args = Deserialize<WebSocketClosedEventArgs>(ref payload);
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

            case EventMixStarted:
            {
                var args = Deserialize<MixStartedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleMixStarted(args);
                break;
            }

            case EventMixEnded:
            {
                var args = Deserialize<MixEndedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleMixEnded(args);
                break;
            }

            case EventVolumeChanged:
            {
                var args = Deserialize<VolumeChangedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleVolumeChanged(args);
                break;
            }

            case EventFiltersChanged:
            {
                // The filters member needs the server-kind converter, so this one event
                // deserializes with the per-kind options instead of the shared ones.
                var args = Deserialize<FiltersChangedEventArgs>(
                    ref payload,
                    NyraJson.OptionsFor(_node.IsNodeLink)
                );
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleFiltersChanged(args);
                break;
            }

            case EventSeek:
            {
                var args = Deserialize<TrackSoughtEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleTrackSought(args);
                break;
            }

            case EventPause:
            {
                var args = Deserialize<PauseChangedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandlePauseChanged(args);
                break;
            }

            case EventPlayerCreated:
            {
                // Player.Filters needs the server-kind converter, same as FiltersChangedEvent.
                var args = Deserialize<PlayerCreatedEventArgs>(
                    ref payload,
                    NyraJson.OptionsFor(_node.IsNodeLink)
                );
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandlePlayerCreated(args);
                break;
            }

            case EventPlayerDestroyed:
            {
                var args = Deserialize<PlayerDestroyedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandlePlayerDestroyed(args);
                break;
            }

            case EventPlayerReconnecting:
            {
                var args = Deserialize<PlayerReconnectingEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandlePlayerReconnecting(args);
                break;
            }

            case EventPlayerConnected:
            {
                var args = Deserialize<PlayerConnectedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandlePlayerConnected(args);
                break;
            }

            case EventStreamMetadata:
            {
                var args = Deserialize<StreamMetadataUpdatedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleStreamMetadataUpdated(args);
                break;
            }

            case EventEternalBoxInfo:
            {
                var args = Deserialize<EternalBoxInfoUpdatedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleEternalBoxInfoUpdated(args);
                break;
            }

            case EventEternalBoxJump:
            {
                var args = Deserialize<EternalBoxJumpedEventArgs>(ref payload);
                if (args is not null)
                    _node.GetConnection(args.GuildId)?.HandleEternalBoxJumped(args);
                break;
            }

            default:
                // NodeLink emits further event types outside player lifecycle and playback
                // state, they are not part of the shared protocol and are ignored.
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
            // session id is set before the next message is processed, only the PATCH continues
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

        if (reader.ValueTextEquals("MixStartedEvent"u8))
            return EventMixStarted;

        if (reader.ValueTextEquals("MixEndedEvent"u8))
            return EventMixEnded;

        if (reader.ValueTextEquals("VolumeChangedEvent"u8))
            return EventVolumeChanged;

        if (reader.ValueTextEquals("FiltersChangedEvent"u8))
            return EventFiltersChanged;

        if (reader.ValueTextEquals("SeekEvent"u8))
            return EventSeek;

        if (reader.ValueTextEquals("PauseEvent"u8))
            return EventPause;

        if (reader.ValueTextEquals("PlayerCreatedEvent"u8))
            return EventPlayerCreated;

        if (reader.ValueTextEquals("PlayerDestroyedEvent"u8))
            return EventPlayerDestroyed;

        if (reader.ValueTextEquals("PlayerReconnectingEvent"u8))
            return EventPlayerReconnecting;

        if (reader.ValueTextEquals("PlayerConnectedEvent"u8))
            return EventPlayerConnected;

        if (reader.ValueTextEquals("StreamMetadataEvent"u8))
            return EventStreamMetadata;

        if (reader.ValueTextEquals("EternalBoxInfoEvent"u8))
            return EventEternalBoxInfo;

        if (reader.ValueTextEquals("EternalBoxJumpEvent"u8))
            return EventEternalBoxJump;

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

    private static T? Deserialize<T>(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(ref reader, options);
}
