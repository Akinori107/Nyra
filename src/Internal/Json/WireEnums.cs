using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class WireEnumConverter<TEnum>(
    TEnum unknownValue,
    KeyValuePair<string, TEnum>[] readPairs
) : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly FrozenDictionary<TEnum, string> _writeCache = readPairs.ToFrozenDictionary(
        pair => pair.Value,
        pair => pair.Key
    );

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            reader.Skip();
            return unknownValue;
        }

        foreach (var pair in readPairs)
            if (reader.ValueTextEquals(pair.Key))
                return pair.Value;

        return unknownValue;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!_writeCache.TryGetValue(value, out var name))
            throw new ArgumentException(
                $"The value '{value}' is not defined for enum '{typeof(TEnum).Name}'."
            );

        writer.WriteStringValue(name);
    }
}

internal static class WireEnumRegistry
{
    private static readonly FrozenDictionary<Type, JsonConverter> Converters = new Dictionary<
        Type,
        JsonConverter
    >
    {
        [typeof(ConnectionQuality)] = new WireEnumConverter<ConnectionQuality>(
            ConnectionQuality.Unknown,
            [
                new("unknown", ConnectionQuality.Unknown),
                new("good", ConnectionQuality.Good),
                new("average", ConnectionQuality.Average),
                new("bad", ConnectionQuality.Bad),
                new("disconnected", ConnectionQuality.Disconnected),
                new("disabled", ConnectionQuality.Disabled),
            ]
        ),
        [typeof(CrossfadeCurve)] = new WireEnumConverter<CrossfadeCurve>(
            CrossfadeCurve.Unknown,
            [
                new("linear", CrossfadeCurve.Linear),
                new("exponential", CrossfadeCurve.Exponential),
                new("logarithmic", CrossfadeCurve.Logarithmic),
                new("s-curve", CrossfadeCurve.SCurve),
                new("sine", CrossfadeCurve.Sine),
                new("sinusoidal", CrossfadeCurve.Sinusoidal),
                new("unknown", CrossfadeCurve.Unknown),
            ]
        ),
        [typeof(CrossfadeMode)] = new WireEnumConverter<CrossfadeMode>(
            CrossfadeMode.Unknown,
            [
                new("preload", CrossfadeMode.Preload),
                new("stream", CrossfadeMode.Stream),
                new("smart", CrossfadeMode.Smart),
                new("unknown", CrossfadeMode.Unknown),
            ]
        ),
        [typeof(ErrorSeverity)] = new WireEnumConverter<ErrorSeverity>(
            ErrorSeverity.Unknown,
            [
                new("common", ErrorSeverity.Common),
                new("suspicious", ErrorSeverity.Suspicious),
                new("fault", ErrorSeverity.Fault),
                new("unknown", ErrorSeverity.Unknown),
            ]
        ),
        [typeof(EternalBoxJumpType)] = new WireEnumConverter<EternalBoxJumpType>(
            EternalBoxJumpType.Unknown,
            [
                new("loop", EternalBoxJumpType.Loop),
                new("jump", EternalBoxJumpType.Jump),
                new("unknown", EternalBoxJumpType.Unknown),
            ]
        ),
        [typeof(FadeType)] = new WireEnumConverter<FadeType>(
            FadeType.Unknown,
            [
                new("volume", FadeType.Volume),
                new("tape", FadeType.Tape),
                new("scratch", FadeType.Scratch),
                new("both", FadeType.Both),
                new("unknown", FadeType.Unknown),
            ]
        ),
        [typeof(IpBlockType)] = new WireEnumConverter<IpBlockType>(
            IpBlockType.Unknown,
            [
                new("Inet4Address", IpBlockType.Inet4),
                new("Inet6Address", IpBlockType.Inet6),
                new("unknown", IpBlockType.Unknown),
            ]
        ),
        [typeof(LoadType)] = new WireEnumConverter<LoadType>(
            LoadType.Unknown,
            [
                new("track", LoadType.Track),
                new("playlist", LoadType.Playlist),
                new("search", LoadType.Search),
                new("empty", LoadType.Empty),
                new("error", LoadType.Error),
                new("unknown", LoadType.Unknown),
            ]
        ),
        [typeof(LyricsLoadType)] = new WireEnumConverter<LyricsLoadType>(
            LyricsLoadType.Unknown,
            [
                new("lyrics", LyricsLoadType.Lyrics),
                new("empty", LyricsLoadType.Empty),
                new("error", LyricsLoadType.Error),
                new("unknown", LyricsLoadType.Unknown),
            ]
        ),
        [typeof(MeaningLoadType)] = new WireEnumConverter<MeaningLoadType>(
            MeaningLoadType.Unknown,
            [
                new("meaning", MeaningLoadType.Meaning),
                new("empty", MeaningLoadType.Empty),
                new("error", MeaningLoadType.Error),
                new("unknown", MeaningLoadType.Unknown),
            ]
        ),
        [typeof(MixEndReason)] = new WireEnumConverter<MixEndReason>(
            MixEndReason.Unknown,
            [
                new("FINISHED", MixEndReason.Finished),
                new("REMOVED", MixEndReason.Removed),
                new("ERROR", MixEndReason.Error),
                new("MAIN_ENDED", MixEndReason.MainEnded),
                new("DESTROYED", MixEndReason.Destroyed),
                new("CLEARED", MixEndReason.Cleared),
                new("unknown", MixEndReason.Unknown),
            ]
        ),
        [typeof(RoutePlannerClass)] = new WireEnumConverter<RoutePlannerClass>(
            RoutePlannerClass.Unknown,
            [
                new("RotatingIpRoutePlanner", RoutePlannerClass.Rotating),
                new("NanoIpRoutePlanner", RoutePlannerClass.Nano),
                new("RotatingNanoIpRoutePlanner", RoutePlannerClass.RotatingNano),
                new("BalancingIpRoutePlanner", RoutePlannerClass.Balancing),
                new("unknown", RoutePlannerClass.Unknown),
            ]
        ),
        [typeof(TrackEndReason)] = new WireEnumConverter<TrackEndReason>(
            TrackEndReason.Unknown,
            [
                new("finished", TrackEndReason.Finished),
                new("loadFailed", TrackEndReason.LoadFailed),
                new("stopped", TrackEndReason.Stopped),
                new("replaced", TrackEndReason.Replaced),
                new("cleanup", TrackEndReason.Cleanup),
                new("gapless", TrackEndReason.Gapless),
                new("crossfading", TrackEndReason.Crossfading),
                new("unknown", TrackEndReason.Unknown),
            ]
        ),
    }.ToFrozenDictionary();

    public static JsonConverter<TEnum> For<TEnum>()
        where TEnum : struct, Enum => (JsonConverter<TEnum>)For(typeof(TEnum));

    public static bool Covers(Type type) => Converters.ContainsKey(type);

    public static JsonConverter For(Type type) =>
        Converters.TryGetValue(type, out var converter)
            ? converter
            : throw new KeyNotFoundException($"No wire enum converter for '{type}'.");
}
