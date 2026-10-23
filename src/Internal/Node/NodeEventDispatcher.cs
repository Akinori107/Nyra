using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nyra.Entities;
using Nyra.EventArgs;

namespace Nyra.Internal;

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
    private const int EventConnectionStatus = 24;
    private const int EventWorkerFailed = 25;

    private readonly Node _node;

    public NodeEventDispatcher(Node node)
    {
        _node = node;
    }

    public void Dispatch(ReadOnlySpan<byte> data, int connectionEpoch)
    {
        var reader = new Utf8JsonReader(data);
        reader.Read();

        var payload = reader;

        var op = 0;
        var type = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("op"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                    op = ReadOperation(ref reader);
                else
                    reader.Skip();
                if (type != 0 || op != OpEvent)
                    break;
            }
            else if (reader.ValueTextEquals("type"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                    type = ReadEventType(ref reader);
                else
                    reader.Skip();
                if (op != 0)
                    break;
            }
            else
                reader.Skip();
        }

        switch (op)
        {
            case OpReady:
            {
                var args = Deserialize<NodeReadyEventArgs>(ref payload);
                if (args is not null)
                    _ = _node.GuardAsync(
                        _node.EventHandlers.HandleReadyAsync(args, connectionEpoch)
                    );
                break;
            }

            case OpStats:
            {
                var stats = Deserialize<Stats>(ref payload);
                if (stats is not null)
                    _node.EventHandlers.HandleStats(stats);
                break;
            }

            case OpPlayerUpdate:
            {
                var update = Deserialize<PlayerUpdateEventArgs>(ref payload);
                if (update is not null)
                    Route(update.GuildId, "playerUpdate")?.HandlePlayerUpdate(update);
                break;
            }

            case OpEvent:
                DispatchEvent(ref payload, type);
                break;

            default:
                if (_node.Logger?.IsEnabled(LogLevel.Debug) == true)
                {
                    var operationName = ReadMemberText(payload, "op"u8);
                    _node.Logger.LogDebug(
                        LogEvents.Node,
                        operationName is null
                            ? "Node {NodeName} sent a frame without an op member, ignored."
                            : "Node {NodeName} sent an unknown operation '{Operation}'.",
                        _node.Name,
                        operationName ?? "<missing>"
                    );
                }
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
                    Route(args.GuildId, "TrackStartEvent")?.HandleTrackStart(args);
                break;
            }

            case EventTrackEnd:
            {
                var args = Deserialize<TrackFinishedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "TrackEndEvent")?.HandleTrackEnd(args);
                break;
            }

            case EventTrackException:
            {
                var args = Deserialize<TrackExceptionEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "TrackExceptionEvent")?.HandleTrackException(args);
                break;
            }

            case EventTrackStuck:
            {
                var args = Deserialize<TrackStuckEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "TrackStuckEvent")?.HandleTrackStuck(args);
                break;
            }

            case EventWebSocketClosed:
            {
                var args = Deserialize<WebSocketClosedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "WebSocketClosedEvent")?.HandleWebSocketClosed(args);
                break;
            }

            case EventWorkerFailed:
            {
                var args = Deserialize<WorkerFailedEventArgs>(ref payload);
                if (args is not null)
                    _ = _node.GuardAsync(
                        _node.EventHandlers.HandleWorkerFailedAsync(
                            _node.EventHandlers.SnapshotWorkerFailed(args),
                            args
                        )
                    );
                break;
            }

            case EventLyricsFound:
            {
                var args = Deserialize<LyricsFoundEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "LyricsFoundEvent")?.HandleLyricsFound(args);
                break;
            }

            case EventLyricsNotFound:
            {
                var args = Deserialize<LyricsNotFoundEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "LyricsNotFoundEvent")?.HandleLyricsNotFound(args);
                break;
            }

            case EventLyricsLine:
            {
                var args = Deserialize<LyricsLineEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "LyricsLineEvent")?.HandleLyricsLine(args);
                break;
            }

            case EventSponsorBlockSegmentsLoaded:
            {
                var args = Deserialize<SponsorBlockSegmentsLoadedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "SponsorBlockSegmentsLoadedEvent")
                        ?.HandleSponsorBlockSegmentsLoaded(args);
                break;
            }

            case EventSponsorBlockSegmentSkipped:
            {
                var args = Deserialize<SponsorBlockSegmentSkippedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "SponsorBlockSegmentSkippedEvent")
                        ?.HandleSponsorBlockSegmentSkipped(args);
                break;
            }

            case EventMixStarted:
            {
                var args = Deserialize<MixStartedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "MixStartedEvent")?.HandleMixStarted(args);
                break;
            }

            case EventMixEnded:
            {
                var args = Deserialize<MixEndedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "MixEndedEvent")?.HandleMixEnded(args);
                break;
            }

            case EventVolumeChanged:
            {
                var args = Deserialize<VolumeChangedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "VolumeChangedEvent")?.HandleVolumeChanged(args);
                break;
            }

            case EventFiltersChanged:
            {
                var args = Deserialize<FiltersChangedEventArgs>(
                    ref payload,
                    NyraJson.OptionsFor(_node.IsNodeLink)
                );
                if (args is not null)
                    Route(args.GuildId, "FiltersChangedEvent")?.HandleFiltersChanged(args);
                break;
            }

            case EventSeek:
            {
                var args = Deserialize<TrackSoughtEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "SeekEvent")?.HandleTrackSought(args);
                break;
            }

            case EventPause:
            {
                var args = Deserialize<PauseChangedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "PauseEvent")?.HandlePauseChanged(args);
                break;
            }

            case EventPlayerCreated:
            {
                var args = Deserialize<PlayerCreatedEventArgs>(
                    ref payload,
                    NyraJson.OptionsFor(_node.IsNodeLink)
                );
                if (args is not null)
                    Route(args.GuildId, "PlayerCreatedEvent")?.HandlePlayerCreated(args);
                break;
            }

            case EventPlayerDestroyed:
            {
                var args = Deserialize<PlayerDestroyedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "PlayerDestroyedEvent")?.HandlePlayerDestroyed(args);
                break;
            }

            case EventPlayerReconnecting:
            {
                var args = Deserialize<PlayerReconnectingEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "PlayerReconnectingEvent")?.HandlePlayerReconnecting(args);
                break;
            }

            case EventPlayerConnected:
            {
                var args = Deserialize<PlayerConnectedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "PlayerConnectedEvent")?.HandlePlayerConnected(args);
                break;
            }

            case EventStreamMetadata:
            {
                var args = Deserialize<StreamMetadataUpdatedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "StreamMetadataEvent")?.HandleStreamMetadataUpdated(args);
                break;
            }

            case EventEternalBoxInfo:
            {
                var args = Deserialize<EternalBoxInfoUpdatedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "EternalBoxInfoEvent")?.HandleEternalBoxInfoUpdated(args);
                break;
            }

            case EventEternalBoxJump:
            {
                var args = Deserialize<EternalBoxJumpedEventArgs>(ref payload);
                if (args is not null)
                    Route(args.GuildId, "EternalBoxJumpEvent")?.HandleEternalBoxJumped(args);
                break;
            }

            case EventConnectionStatus:
            {
                var status = Deserialize<ConnectionStatus>(ref payload);
                if (status is not null)
                    _node.EventHandlers.HandleConnectionStatus(status);
                break;
            }

            default:
                if (_node.Logger?.IsEnabled(LogLevel.Debug) == true)
                {
                    var eventTypeName = ReadMemberText(payload, "type"u8);
                    _node.Logger.LogDebug(
                        LogEvents.Node,
                        eventTypeName is null
                            ? "Node {NodeName} sent an event without a type member, ignored."
                            : "Node {NodeName} sent an unknown event type '{EventType}'.",
                        _node.Name,
                        eventTypeName ?? "<missing>"
                    );
                }
                break;
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

        if (reader.ValueTextEquals("ConnectionStatusEvent"u8))
            return EventConnectionStatus;

        if (reader.ValueTextEquals("WorkerFailedEvent"u8))
            return EventWorkerFailed;

        return 0;
    }

    private static string? ReadMemberText(Utf8JsonReader reader, ReadOnlySpan<byte> name)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals(name))
            {
                reader.Read();
                return reader.TokenType is JsonTokenType.String
                    ? reader.GetString()
                    : "(not a string)";
            }

            reader.Skip();
        }

        return null;
    }

    private GuildConnection? Route(ulong guildId, string eventName)
    {
        var connection = _node.GetConnection(guildId);
        if (connection is null && _node.Logger?.IsEnabled(LogLevel.Debug) == true)
            _node.Logger.LogDebug(
                LogEvents.Node,
                "Node {NodeName} sent {EventName} for guild {GuildId} with no connection to route it to, the event was dropped.",
                _node.Name,
                eventName,
                guildId
            );

        return connection;
    }

    private static T? Deserialize<T>(ref Utf8JsonReader reader) =>
        NyraJson.Deserialize<T>(ref reader, NyraJson.SerializerOptions);

    private static T? Deserialize<T>(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        NyraJson.Deserialize<T>(ref reader, options);
}
