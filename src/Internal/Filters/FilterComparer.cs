using Nyra.Entities;

namespace Nyra.Internal;

internal static class FilterComparer
{
    internal static bool IsEmpty(FilterChain chain) =>
        chain
            is {
                Karaoke: null,
                Timescale: null,
                Tremolo: null,
                Vibrato: null,
                Distortion: null,
                Rotation: null,
                ChannelMix: null,
                LowPass: null,
                Echo: null,
                Reverb: null,
                HighPass: null,
                Chorus: null,
                Phaser: null,
                Flanger: null,
                Spatial: null,
                Compressor: null,
                Phonograph: null,
                Tesseract: null,
                PluginFilters: null,
                Equalizer: null or { Count: 0 },
            };

    internal static bool Touched<T>(T? current, T? applied)
        where T : class => current is not null && !ReferenceEquals(applied, current);
}
