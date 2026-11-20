using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.Internal;

internal interface IWireEnum<TEnum>
    where TEnum : struct, Enum
{
    static abstract IReadOnlyList<KeyValuePair<string, TEnum>> WireNames { get; }

    static abstract TEnum UnknownValue { get; }
}

internal sealed class WireEnumConverter<TSelf, TEnum> : JsonConverter<TEnum>
    where TSelf : IWireEnum<TEnum>
    where TEnum : struct, Enum
{
    private readonly IReadOnlyList<KeyValuePair<string, TEnum>> _readPairs = TSelf.WireNames;
    private readonly FrozenDictionary<TEnum, string> _writeCache =
        TSelf.WireNames.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            reader.Skip();
            return TSelf.UnknownValue;
        }

        foreach (var pair in _readPairs)
            if (reader.ValueTextEquals(pair.Key))
                return pair.Value;

        return TSelf.UnknownValue;
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
        [typeof(ConnectionQuality)] =
            new WireEnumConverter<ConnectionQualityWire, ConnectionQuality>(),
        [typeof(CrossfadeCurve)] = new WireEnumConverter<CrossfadeCurveWire, CrossfadeCurve>(),
        [typeof(CrossfadeMode)] = new WireEnumConverter<CrossfadeModeWire, CrossfadeMode>(),
        [typeof(ErrorSeverity)] = new WireEnumConverter<ErrorSeverityWire, ErrorSeverity>(),
        [typeof(EternalBoxJumpType)] =
            new WireEnumConverter<EternalBoxJumpTypeWire, EternalBoxJumpType>(),
        [typeof(FadeType)] = new WireEnumConverter<FadeTypeWire, FadeType>(),
        [typeof(IpBlockType)] = new WireEnumConverter<IpBlockTypeWire, IpBlockType>(),
        [typeof(LoadType)] = new WireEnumConverter<LoadTypeWire, LoadType>(),
        [typeof(LyricsLoadType)] = new WireEnumConverter<LyricsLoadTypeWire, LyricsLoadType>(),
        [typeof(MeaningLoadType)] = new WireEnumConverter<MeaningLoadTypeWire, MeaningLoadType>(),
        [typeof(MixEndReason)] = new WireEnumConverter<MixEndReasonWire, MixEndReason>(),
        [typeof(RoutePlannerClass)] =
            new WireEnumConverter<RoutePlannerClassWire, RoutePlannerClass>(),
        [typeof(TrackEndReason)] = new WireEnumConverter<TrackEndReasonWire, TrackEndReason>(),
    }.ToFrozenDictionary();

    public static bool Covers(Type type) => Converters.ContainsKey(type);

    public static JsonConverter For(Type type) =>
        Converters.TryGetValue(type, out var converter)
            ? converter
            : throw new KeyNotFoundException($"No wire enum converter for '{type}'.");
}

internal sealed class ConnectionQualityWire : IWireEnum<ConnectionQuality>
{
    public static IReadOnlyList<KeyValuePair<string, ConnectionQuality>> WireNames { get; } =
    [
        new("unknown", ConnectionQuality.Unknown),
        new("good", ConnectionQuality.Good),
        new("average", ConnectionQuality.Average),
        new("bad", ConnectionQuality.Bad),
        new("disconnected", ConnectionQuality.Disconnected),
        new("disabled", ConnectionQuality.Disabled),
    ];

    public static ConnectionQuality UnknownValue => ConnectionQuality.Unknown;
}

internal sealed class CrossfadeCurveWire : IWireEnum<CrossfadeCurve>
{
    public static IReadOnlyList<KeyValuePair<string, CrossfadeCurve>> WireNames { get; } =
    [
        new("linear", CrossfadeCurve.Linear),
        new("exponential", CrossfadeCurve.Exponential),
        new("logarithmic", CrossfadeCurve.Logarithmic),
        new("s-curve", CrossfadeCurve.SCurve),
        new("sine", CrossfadeCurve.Sine),
        new("sinusoidal", CrossfadeCurve.Sinusoidal),
        new("unknown", CrossfadeCurve.Unknown),
    ];

    public static CrossfadeCurve UnknownValue => CrossfadeCurve.Unknown;
}

internal sealed class CrossfadeModeWire : IWireEnum<CrossfadeMode>
{
    public static IReadOnlyList<KeyValuePair<string, CrossfadeMode>> WireNames { get; } =
    [
        new("preload", CrossfadeMode.Preload),
        new("stream", CrossfadeMode.Stream),
        new("smart", CrossfadeMode.Smart),
        new("unknown", CrossfadeMode.Unknown),
    ];

    public static CrossfadeMode UnknownValue => CrossfadeMode.Unknown;
}

internal sealed class ErrorSeverityWire : IWireEnum<ErrorSeverity>
{
    public static IReadOnlyList<KeyValuePair<string, ErrorSeverity>> WireNames { get; } =
    [
        new("common", ErrorSeverity.Common),
        new("suspicious", ErrorSeverity.Suspicious),
        new("fault", ErrorSeverity.Fault),
        new("unknown", ErrorSeverity.Unknown),
    ];

    public static ErrorSeverity UnknownValue => ErrorSeverity.Unknown;
}

internal sealed class EternalBoxJumpTypeWire : IWireEnum<EternalBoxJumpType>
{
    public static IReadOnlyList<KeyValuePair<string, EternalBoxJumpType>> WireNames { get; } =
    [
        new("loop", EternalBoxJumpType.Loop),
        new("jump", EternalBoxJumpType.Jump),
        new("unknown", EternalBoxJumpType.Unknown),
    ];

    public static EternalBoxJumpType UnknownValue => EternalBoxJumpType.Unknown;
}

internal sealed class FadeTypeWire : IWireEnum<FadeType>
{
    public static IReadOnlyList<KeyValuePair<string, FadeType>> WireNames { get; } =
    [
        new("volume", FadeType.Volume),
        new("tape", FadeType.Tape),
        new("scratch", FadeType.Scratch),
        new("both", FadeType.Both),
        new("unknown", FadeType.Unknown),
    ];

    public static FadeType UnknownValue => FadeType.Unknown;
}

internal sealed class IpBlockTypeWire : IWireEnum<IpBlockType>
{
    public static IReadOnlyList<KeyValuePair<string, IpBlockType>> WireNames { get; } =
    [
        new("Inet4Address", IpBlockType.Inet4),
        new("Inet6Address", IpBlockType.Inet6),
        new("unknown", IpBlockType.Unknown),
    ];

    public static IpBlockType UnknownValue => IpBlockType.Unknown;
}

internal sealed class LoadTypeWire : IWireEnum<LoadType>
{
    public static IReadOnlyList<KeyValuePair<string, LoadType>> WireNames { get; } =
    [
        new("track", LoadType.Track),
        new("playlist", LoadType.Playlist),
        new("search", LoadType.Search),
        new("empty", LoadType.Empty),
        new("error", LoadType.Error),
        new("unknown", LoadType.Unknown),
    ];

    public static LoadType UnknownValue => LoadType.Unknown;
}

internal sealed class LyricsLoadTypeWire : IWireEnum<LyricsLoadType>
{
    public static IReadOnlyList<KeyValuePair<string, LyricsLoadType>> WireNames { get; } =
    [
        new("lyrics", LyricsLoadType.Lyrics),
        new("empty", LyricsLoadType.Empty),
        new("error", LyricsLoadType.Error),
        new("unknown", LyricsLoadType.Unknown),
    ];

    public static LyricsLoadType UnknownValue => LyricsLoadType.Unknown;
}

internal sealed class MeaningLoadTypeWire : IWireEnum<MeaningLoadType>
{
    public static IReadOnlyList<KeyValuePair<string, MeaningLoadType>> WireNames { get; } =
    [
        new("meaning", MeaningLoadType.Meaning),
        new("empty", MeaningLoadType.Empty),
        new("error", MeaningLoadType.Error),
        new("unknown", MeaningLoadType.Unknown),
    ];

    public static MeaningLoadType UnknownValue => MeaningLoadType.Unknown;
}

internal sealed class MixEndReasonWire : IWireEnum<MixEndReason>
{
    public static IReadOnlyList<KeyValuePair<string, MixEndReason>> WireNames { get; } =
    [
        new("FINISHED", MixEndReason.Finished),
        new("REMOVED", MixEndReason.Removed),
        new("ERROR", MixEndReason.Error),
        new("MAIN_ENDED", MixEndReason.MainEnded),
        new("DESTROYED", MixEndReason.Destroyed),
        new("CLEARED", MixEndReason.Cleared),
        new("unknown", MixEndReason.Unknown),
    ];

    public static MixEndReason UnknownValue => MixEndReason.Unknown;
}

internal sealed class RoutePlannerClassWire : IWireEnum<RoutePlannerClass>
{
    public static IReadOnlyList<KeyValuePair<string, RoutePlannerClass>> WireNames { get; } =
    [
        new("RotatingIpRoutePlanner", RoutePlannerClass.Rotating),
        new("NanoIpRoutePlanner", RoutePlannerClass.Nano),
        new("RotatingNanoIpRoutePlanner", RoutePlannerClass.RotatingNano),
        new("BalancingIpRoutePlanner", RoutePlannerClass.Balancing),
        new("unknown", RoutePlannerClass.Unknown),
    ];

    public static RoutePlannerClass UnknownValue => RoutePlannerClass.Unknown;
}

internal sealed class TrackEndReasonWire : IWireEnum<TrackEndReason>
{
    public static IReadOnlyList<KeyValuePair<string, TrackEndReason>> WireNames { get; } =
    [
        new("finished", TrackEndReason.Finished),
        new("loadFailed", TrackEndReason.LoadFailed),
        new("stopped", TrackEndReason.Stopped),
        new("replaced", TrackEndReason.Replaced),
        new("cleanup", TrackEndReason.Cleanup),
        new("gapless", TrackEndReason.Gapless),
        new("crossfading", TrackEndReason.Crossfading),
        new("unknown", TrackEndReason.Unknown),
    ];

    public static TrackEndReason UnknownValue => TrackEndReason.Unknown;
}
