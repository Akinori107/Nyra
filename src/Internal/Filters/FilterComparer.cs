using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Internal;

internal static class FilterComparer
{
    internal static bool FiltersEqual(FilterChain left, FilterChain right) =>
        VolumeEqual(left, right)
        && EqualIgnoreTransition(left.Karaoke, right.Karaoke)
        && EqualIgnoreTransition(left.Timescale, right.Timescale)
        && EqualIgnoreTransition(left.Tremolo, right.Tremolo)
        && EqualIgnoreTransition(left.Vibrato, right.Vibrato)
        && EqualIgnoreTransition(left.Distortion, right.Distortion)
        && EqualIgnoreTransition(left.Rotation, right.Rotation)
        && EqualIgnoreTransition(left.ChannelMix, right.ChannelMix)
        && EqualIgnoreTransition(left.LowPass, right.LowPass)
        && EqualIgnoreTransition(left.Echo, right.Echo)
        && EqualIgnoreTransition(left.Reverb, right.Reverb)
        && EqualIgnoreTransition(left.HighPass, right.HighPass)
        && EqualIgnoreTransition(left.Chorus, right.Chorus)
        && EqualIgnoreTransition(left.Phaser, right.Phaser)
        && EqualIgnoreTransition(left.Flanger, right.Flanger)
        && EqualIgnoreTransition(left.Spatial, right.Spatial)
        && EqualIgnoreTransition(left.Compressor, right.Compressor)
        && EqualIgnoreTransition(left.Phonograph, right.Phonograph)
        && EqualIgnoreTransition(left.Tesseract, right.Tesseract)
        && BandsEqual(left.Equalizer, right.Equalizer)
        && PluginFiltersEqual(left.PluginFilters, right.PluginFilters);

    private static bool PluginFiltersEqual(
        IReadOnlyDictionary<string, JsonElement>? left,
        IReadOnlyDictionary<string, JsonElement>? right
    )
    {
        if (right is null)
            return true;
        if (left is null)
            return false;
        foreach (var (name, value) in right)
            if (!left.TryGetValue(name, out var other) || !JsonElement.DeepEquals(value, other))
                return false;
        return true;
    }

    private static bool EqualIgnoreTransition<T>(T? left, T? right)
        where T : class, IStripTransition<T> =>
        left is null
            ? right is null
            : right is not null
                && EqualityComparer<T>.Default.Equals(
                    left.WithoutTransition(),
                    right.WithoutTransition()
                );

    private static bool VolumeEqual(FilterChain left, FilterChain right) =>
        left.Volume is null || right.Volume is null || left.Volume == right.Volume;

    private static bool BandsEqual(
        IReadOnlyList<EqualizerBand>? left,
        IReadOnlyList<EqualizerBand>? right
    )
    {
        if (left is not { Count: > 0 })
            return right is not { Count: > 0 };

        return right is { Count: > 0 } && left.SequenceEqual(right);
    }

    internal static bool Touched<T>(T? current, T? applied)
        where T : class => current is not null && !ReferenceEquals(applied, current);
}
