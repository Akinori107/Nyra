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
        new KeyValuePair<string, ConnectionQuality>("unknown", ConnectionQuality.Unknown),
        new KeyValuePair<string, ConnectionQuality>("good", ConnectionQuality.Good),
        new KeyValuePair<string, ConnectionQuality>("average", ConnectionQuality.Average),
        new KeyValuePair<string, ConnectionQuality>("bad", ConnectionQuality.Bad),
        new KeyValuePair<string, ConnectionQuality>("disconnected", ConnectionQuality.Disconnected),
        new KeyValuePair<string, ConnectionQuality>("disabled", ConnectionQuality.Disabled),
    ];

    public static ConnectionQuality UnknownValue => ConnectionQuality.Unknown;
}

internal sealed class CrossfadeCurveWire : IWireEnum<CrossfadeCurve>
{
    public static IReadOnlyList<KeyValuePair<string, CrossfadeCurve>> WireNames { get; } =
    [
        new KeyValuePair<string, CrossfadeCurve>("linear", CrossfadeCurve.Linear),
        new KeyValuePair<string, CrossfadeCurve>("exponential", CrossfadeCurve.Exponential),
        new KeyValuePair<string, CrossfadeCurve>("logarithmic", CrossfadeCurve.Logarithmic),
        new KeyValuePair<string, CrossfadeCurve>("s-curve", CrossfadeCurve.SCurve),
        new KeyValuePair<string, CrossfadeCurve>("sine", CrossfadeCurve.Sine),
        new KeyValuePair<string, CrossfadeCurve>("sinusoidal", CrossfadeCurve.Sinusoidal),
        new KeyValuePair<string, CrossfadeCurve>("unknown", CrossfadeCurve.Unknown),
    ];

    public static CrossfadeCurve UnknownValue => CrossfadeCurve.Unknown;
}

internal sealed class CrossfadeModeWire : IWireEnum<CrossfadeMode>
{
    public static IReadOnlyList<KeyValuePair<string, CrossfadeMode>> WireNames { get; } =
    [
        new KeyValuePair<string, CrossfadeMode>("preload", CrossfadeMode.Preload),
        new KeyValuePair<string, CrossfadeMode>("stream", CrossfadeMode.Stream),
        new KeyValuePair<string, CrossfadeMode>("smart", CrossfadeMode.Smart),
        new KeyValuePair<string, CrossfadeMode>("unknown", CrossfadeMode.Unknown),
    ];

    public static CrossfadeMode UnknownValue => CrossfadeMode.Unknown;
}

internal sealed class ErrorSeverityWire : IWireEnum<ErrorSeverity>
{
    public static IReadOnlyList<KeyValuePair<string, ErrorSeverity>> WireNames { get; } =
    [
        new KeyValuePair<string, ErrorSeverity>("common", ErrorSeverity.Common),
        new KeyValuePair<string, ErrorSeverity>("suspicious", ErrorSeverity.Suspicious),
        new KeyValuePair<string, ErrorSeverity>("fault", ErrorSeverity.Fault),
        new KeyValuePair<string, ErrorSeverity>("unknown", ErrorSeverity.Unknown),
    ];

    public static ErrorSeverity UnknownValue => ErrorSeverity.Unknown;
}

internal sealed class EternalBoxJumpTypeWire : IWireEnum<EternalBoxJumpType>
{
    public static IReadOnlyList<KeyValuePair<string, EternalBoxJumpType>> WireNames { get; } =
    [
        new KeyValuePair<string, EternalBoxJumpType>("loop", EternalBoxJumpType.Loop),
        new KeyValuePair<string, EternalBoxJumpType>("jump", EternalBoxJumpType.Jump),
        new KeyValuePair<string, EternalBoxJumpType>("unknown", EternalBoxJumpType.Unknown),
    ];

    public static EternalBoxJumpType UnknownValue => EternalBoxJumpType.Unknown;
}

internal sealed class FadeTypeWire : IWireEnum<FadeType>
{
    public static IReadOnlyList<KeyValuePair<string, FadeType>> WireNames { get; } =
    [
        new KeyValuePair<string, FadeType>("volume", FadeType.Volume),
        new KeyValuePair<string, FadeType>("tape", FadeType.Tape),
        new KeyValuePair<string, FadeType>("scratch", FadeType.Scratch),
        new KeyValuePair<string, FadeType>("both", FadeType.Both),
        new KeyValuePair<string, FadeType>("unknown", FadeType.Unknown),
    ];

    public static FadeType UnknownValue => FadeType.Unknown;
}

internal sealed class IpBlockTypeWire : IWireEnum<IpBlockType>
{
    public static IReadOnlyList<KeyValuePair<string, IpBlockType>> WireNames { get; } =
    [
        new KeyValuePair<string, IpBlockType>("Inet4Address", IpBlockType.Inet4),
        new KeyValuePair<string, IpBlockType>("Inet6Address", IpBlockType.Inet6),
        new KeyValuePair<string, IpBlockType>("unknown", IpBlockType.Unknown),
    ];

    public static IpBlockType UnknownValue => IpBlockType.Unknown;
}

internal sealed class LoadTypeWire : IWireEnum<LoadType>
{
    public static IReadOnlyList<KeyValuePair<string, LoadType>> WireNames { get; } =
    [
        new KeyValuePair<string, LoadType>("track", LoadType.Track),
        new KeyValuePair<string, LoadType>("playlist", LoadType.Playlist),
        new KeyValuePair<string, LoadType>("search", LoadType.Search),
        new KeyValuePair<string, LoadType>("empty", LoadType.Empty),
        new KeyValuePair<string, LoadType>("error", LoadType.Error),
        new KeyValuePair<string, LoadType>("unknown", LoadType.Unknown),
    ];

    public static LoadType UnknownValue => LoadType.Unknown;
}

internal sealed class LyricsLoadTypeWire : IWireEnum<LyricsLoadType>
{
    public static IReadOnlyList<KeyValuePair<string, LyricsLoadType>> WireNames { get; } =
    [
        new KeyValuePair<string, LyricsLoadType>("lyrics", LyricsLoadType.Lyrics),
        new KeyValuePair<string, LyricsLoadType>("empty", LyricsLoadType.Empty),
        new KeyValuePair<string, LyricsLoadType>("error", LyricsLoadType.Error),
        new KeyValuePair<string, LyricsLoadType>("unknown", LyricsLoadType.Unknown),
    ];

    public static LyricsLoadType UnknownValue => LyricsLoadType.Unknown;
}

internal sealed class MeaningLoadTypeWire : IWireEnum<MeaningLoadType>
{
    public static IReadOnlyList<KeyValuePair<string, MeaningLoadType>> WireNames { get; } =
    [
        new KeyValuePair<string, MeaningLoadType>("meaning", MeaningLoadType.Meaning),
        new KeyValuePair<string, MeaningLoadType>("empty", MeaningLoadType.Empty),
        new KeyValuePair<string, MeaningLoadType>("error", MeaningLoadType.Error),
        new KeyValuePair<string, MeaningLoadType>("unknown", MeaningLoadType.Unknown),
    ];

    public static MeaningLoadType UnknownValue => MeaningLoadType.Unknown;
}

internal sealed class MixEndReasonWire : IWireEnum<MixEndReason>
{
    public static IReadOnlyList<KeyValuePair<string, MixEndReason>> WireNames { get; } =
    [
        new KeyValuePair<string, MixEndReason>("FINISHED", MixEndReason.Finished),
        new KeyValuePair<string, MixEndReason>("REMOVED", MixEndReason.Removed),
        new KeyValuePair<string, MixEndReason>("ERROR", MixEndReason.Error),
        new KeyValuePair<string, MixEndReason>("MAIN_ENDED", MixEndReason.MainEnded),
        new KeyValuePair<string, MixEndReason>("DESTROYED", MixEndReason.Destroyed),
        new KeyValuePair<string, MixEndReason>("CLEARED", MixEndReason.Cleared),
        new KeyValuePair<string, MixEndReason>("unknown", MixEndReason.Unknown),
    ];

    public static MixEndReason UnknownValue => MixEndReason.Unknown;
}

internal sealed class RoutePlannerClassWire : IWireEnum<RoutePlannerClass>
{
    public static IReadOnlyList<KeyValuePair<string, RoutePlannerClass>> WireNames { get; } =
    [
        new KeyValuePair<string, RoutePlannerClass>(
            "RotatingIpRoutePlanner",
            RoutePlannerClass.Rotating
        ),
        new KeyValuePair<string, RoutePlannerClass>("NanoIpRoutePlanner", RoutePlannerClass.Nano),
        new KeyValuePair<string, RoutePlannerClass>(
            "RotatingNanoIpRoutePlanner",
            RoutePlannerClass.RotatingNano
        ),
        new KeyValuePair<string, RoutePlannerClass>(
            "BalancingIpRoutePlanner",
            RoutePlannerClass.Balancing
        ),
        new KeyValuePair<string, RoutePlannerClass>("unknown", RoutePlannerClass.Unknown),
    ];

    public static RoutePlannerClass UnknownValue => RoutePlannerClass.Unknown;
}

internal sealed class TrackEndReasonWire : IWireEnum<TrackEndReason>
{
    public static IReadOnlyList<KeyValuePair<string, TrackEndReason>> WireNames { get; } =
    [
        new KeyValuePair<string, TrackEndReason>("finished", TrackEndReason.Finished),
        new KeyValuePair<string, TrackEndReason>("loadFailed", TrackEndReason.LoadFailed),
        new KeyValuePair<string, TrackEndReason>("stopped", TrackEndReason.Stopped),
        new KeyValuePair<string, TrackEndReason>("replaced", TrackEndReason.Replaced),
        new KeyValuePair<string, TrackEndReason>("cleanup", TrackEndReason.Cleanup),
        new KeyValuePair<string, TrackEndReason>("gapless", TrackEndReason.Gapless),
        new KeyValuePair<string, TrackEndReason>("crossfading", TrackEndReason.Crossfading),
        new KeyValuePair<string, TrackEndReason>("unknown", TrackEndReason.Unknown),
    ];

    public static TrackEndReason UnknownValue => TrackEndReason.Unknown;
}
