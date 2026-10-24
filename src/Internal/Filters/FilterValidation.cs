using System.Numerics;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Internal;

internal static class FilterValidation
{
    internal static void ValidateFilterBounds(FilterChain chain, bool nodeLink) =>
        ValidateFilterBounds(chain, null, nodeLink);

    internal static void ValidateFilterBounds(
        FilterChain chain,
        FilterChain? untouched,
        bool nodeLink
    )
    {
        if (FilterComparer.Touched(chain.Karaoke, untouched?.Karaoke))
            ValidateFilterBounds(chain.Karaoke!, nodeLink);
        if (FilterComparer.Touched(chain.Tremolo, untouched?.Tremolo))
            ValidateFilterBounds(chain.Tremolo!, nodeLink);
        if (FilterComparer.Touched(chain.Vibrato, untouched?.Vibrato))
            ValidateFilterBounds(chain.Vibrato!, nodeLink);
        if (FilterComparer.Touched(chain.ChannelMix, untouched?.ChannelMix))
            ValidateFilterBounds(chain.ChannelMix!, nodeLink);
        if (FilterComparer.Touched(chain.Echo, untouched?.Echo))
            ValidateFilterBounds(chain.Echo!, nodeLink);
        if (FilterComparer.Touched(chain.Reverb, untouched?.Reverb))
            ValidateFilterBounds(chain.Reverb!, nodeLink);
        if (FilterComparer.Touched(chain.HighPass, untouched?.HighPass))
            ValidateFilterBounds(chain.HighPass!, nodeLink);
        if (FilterComparer.Touched(chain.Chorus, untouched?.Chorus))
            ValidateFilterBounds(chain.Chorus!, nodeLink);
        if (FilterComparer.Touched(chain.Phaser, untouched?.Phaser))
            ValidateFilterBounds(chain.Phaser!, nodeLink);
        if (FilterComparer.Touched(chain.Flanger, untouched?.Flanger))
            ValidateFilterBounds(chain.Flanger!, nodeLink);
        if (FilterComparer.Touched(chain.Spatial, untouched?.Spatial))
            ValidateFilterBounds(chain.Spatial!, nodeLink);
        if (FilterComparer.Touched(chain.Compressor, untouched?.Compressor))
            ValidateFilterBounds(chain.Compressor!, nodeLink);
        if (FilterComparer.Touched(chain.Phonograph, untouched?.Phonograph))
            ValidateFilterBounds(chain.Phonograph!, nodeLink);
        if (FilterComparer.Touched(chain.Timescale, untouched?.Timescale))
            ValidateFilterBounds(chain.Timescale!, nodeLink);
        if (FilterComparer.Touched(chain.Tesseract, untouched?.Tesseract))
            ValidateFilterBounds(chain.Tesseract!, nodeLink);
        if (FilterComparer.Touched(chain.Rotation, untouched?.Rotation))
            ValidateFilterBounds(chain.Rotation!, nodeLink);
        if (FilterComparer.Touched(chain.Distortion, untouched?.Distortion))
            ValidateFilterBounds(chain.Distortion!, nodeLink);
        if (FilterComparer.Touched(chain.LowPass, untouched?.LowPass))
            ValidateFilterBounds(chain.LowPass!, nodeLink);
    }

    internal static void ValidateFilterBounds(IFilter filter, bool nodeLink)
    {
        switch (filter)
        {
            case KaraokeFilter karaoke:
                ThrowIfOutOfRange(karaoke.Level, 0.0f, 1.0f, "karaoke level");
                ThrowIfOutOfRange(karaoke.MonoLevel, 0.0f, 1.0f, "karaoke monoLevel");
                ThrowIfOutOfRange(karaoke.FilterWidth, 0.0f, null, "karaoke filterWidth");
                ThrowIfOutOfRange(karaoke.FilterBand, 0.0f, null, "karaoke filterBand");
                break;
            case TremoloFilter tremolo:
                ThrowIfOutOfRange(
                    tremolo.Frequency,
                    0.0f,
                    null,
                    "tremolo frequency",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(
                    tremolo.Depth,
                    0.0f,
                    nodeLink ? null : 1.0f,
                    "tremolo depth",
                    exclusiveMinimum: true
                );
                break;
            case VibratoFilter vibrato:
                ThrowIfOutOfRange(
                    vibrato.Frequency,
                    0.0f,
                    nodeLink ? null : 14.0f,
                    "vibrato frequency",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(
                    vibrato.Depth,
                    0.0f,
                    nodeLink ? 2.0f : 1.0f,
                    "vibrato depth",
                    exclusiveMinimum: true
                );
                break;
            case TimescaleFilter timescale:
                ThrowIfOutOfRange(
                    timescale.Speed,
                    0.0,
                    null,
                    "timescale speed",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(
                    timescale.Pitch,
                    0.0,
                    null,
                    "timescale pitch",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(
                    timescale.Rate,
                    0.0,
                    null,
                    "timescale rate",
                    exclusiveMinimum: true
                );
                break;
            case ChannelMixFilter channelMix:
                ThrowIfOutOfRange(channelMix.LeftToLeft, 0.0f, 1.0f, "channelMix leftToLeft");
                ThrowIfOutOfRange(channelMix.LeftToRight, 0.0f, 1.0f, "channelMix leftToRight");
                ThrowIfOutOfRange(channelMix.RightToLeft, 0.0f, 1.0f, "channelMix rightToLeft");
                ThrowIfOutOfRange(channelMix.RightToRight, 0.0f, 1.0f, "channelMix rightToRight");
                break;
            case EchoFilter echo:
                ThrowIfOutOfRange(
                    echo.DelayMilliseconds ?? 0.0f,
                    0.0f,
                    null,
                    "echo delay",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(echo.Mix ?? 0.0f, 0.0f, 1.0f, "echo mix", exclusiveMinimum: true);
                ThrowIfOutOfRange(echo.Feedback, 0.0f, 1.0f, "echo feedback");
                break;
            case ChorusFilter chorus:
                ThrowIfOutOfRange(
                    chorus.Rate ?? 0.0f,
                    0.0f,
                    null,
                    "chorus rate",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(
                    chorus.Depth ?? 0.0f,
                    0.0f,
                    1.0f,
                    "chorus depth",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(chorus.Mix, 0.0f, 1.0f, "chorus mix", exclusiveMinimum: true);
                CheckFinite(chorus.Feedback, "chorus feedback");
                CheckFinite(chorus.DelayMilliseconds, "chorus delay");
                break;
            case PhaserFilter phaser:
                ThrowIfOutOfRange(
                    phaser.Rate ?? 0.0f,
                    0.0f,
                    null,
                    "phaser rate",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(phaser.Depth, 0.0f, 1.0f, "phaser depth", exclusiveMinimum: true);
                ThrowIfOutOfRange(phaser.Mix, 0.0f, 1.0f, "phaser mix", exclusiveMinimum: true);
                CheckFinite(phaser.Feedback, "phaser feedback");
                CheckFinite(phaser.DelayMilliseconds, "phaser delay");
                CheckFinite(phaser.MinFrequency, "phaser minFrequency");
                CheckFinite(phaser.MaxFrequency, "phaser maxFrequency");
                ThrowIfOutOfRange(phaser.MinFrequency, 0.0f, null, "phaser minFrequency");
                ThrowIfOutOfRange(phaser.MaxFrequency, 0.0f, null, "phaser maxFrequency");
                if (
                    phaser.MinFrequency is { } minFrequency
                    && phaser.MaxFrequency is { } maxFrequency
                    && minFrequency > maxFrequency
                )
                    throw new ArgumentOutOfRangeException(
                        "phaser maxFrequency",
                        maxFrequency,
                        "Must be at or above minFrequency."
                    );
                break;
            case FlangerFilter flanger:
                ThrowIfOutOfRange(
                    flanger.Rate ?? 0.0f,
                    0.0f,
                    null,
                    "flanger rate",
                    exclusiveMinimum: true
                );
                ThrowIfOutOfRange(
                    flanger.Depth ?? 0.0f,
                    0.0f,
                    1.0f,
                    "flanger depth",
                    exclusiveMinimum: true
                );
                CheckFinite(flanger.Feedback, "flanger feedback");
                CheckFinite(flanger.DelayMilliseconds, "flanger delay");
                CheckFinite(flanger.Mix, "flanger mix");
                break;
            case TesseractFilter tesseract:
                ThrowIfOutOfRange(tesseract.RotationHz, 0.0, null, "tesseract rotationHz");
                if (tesseract.RotationHz is { } hz && hz > 0.0 && hz <= 0.001)
                    throw new ArgumentOutOfRangeException(
                        "tesseract rotationHz",
                        hz,
                        "NodeLink leaves anything at or below 0.001 off, use 0.0 to disable or a larger value."
                    );
                break;
            case RotationFilter rotation:
                CheckFinite(rotation.RotationHz, "rotation rotationHz");
                break;
            case DistortionFilter distortion:
                CheckFinite(distortion.SinOffset, "distortion sinOffset");
                CheckFinite(distortion.SinScale, "distortion sinScale");
                CheckFinite(distortion.CosOffset, "distortion cosOffset");
                CheckFinite(distortion.CosScale, "distortion cosScale");
                CheckFinite(distortion.TanOffset, "distortion tanOffset");
                CheckFinite(distortion.TanScale, "distortion tanScale");
                CheckFinite(distortion.Offset, "distortion offset");
                CheckFinite(distortion.Scale, "distortion scale");
                break;
            case LowPassFilter lowPass:
                CheckFinite(lowPass.Smoothing, "lowPass smoothing");
                CheckFinite(lowPass.TargetAlpha, "lowPass targetAlpha");
                CheckFinite(lowPass.LogSmoothing, "lowPass logSmoothing");
                break;
            case ReverbFilter reverb:
                ThrowIfOutOfRange(reverb.RoomSize, 0.0f, 1.0f, "reverb roomSize");
                ThrowIfOutOfRange(reverb.Damping, 0.0f, 1.0f, "reverb damping");
                ThrowIfOutOfRange(reverb.WetLevel, 0.0f, 1.0f, "reverb wetLevel");
                ThrowIfOutOfRange(reverb.DryLevel, 0.0f, 1.0f, "reverb dryLevel");
                ThrowIfOutOfRange(reverb.Width, 0.0f, 1.0f, "reverb width");
                ThrowIfOutOfRange(reverb.Mix, 0.0f, 1.0f, "reverb mix");
                break;
            case HighPassFilter highPass:
                ThrowIfOutOfRange(highPass.Smoothing, 0.0f, null, "highPass smoothing");
                CheckFinite(highPass.TargetAlpha, "highPass targetAlpha");
                break;
            case SpatialFilter spatial:
                ThrowIfOutOfRange(spatial.X, -1.0f, 1.0f, "spatial x");
                ThrowIfOutOfRange(spatial.Y, -1.0f, 1.0f, "spatial y");
                ThrowIfOutOfRange(spatial.Z, -1.0f, 1.0f, "spatial z");
                ThrowIfOutOfRange(spatial.Depth, 0.0f, 1.0f, "spatial depth");
                CheckFinite(spatial.Rate, "spatial rate");
                break;
            case CompressorFilter compressor:
                CheckFinite(compressor.Threshold, "compressor threshold");
                ThrowIfOutOfRange(compressor.Ratio, 1.0f, null, "compressor ratio");
                ThrowIfOutOfRange(compressor.Attack, 0.0f, null, "compressor attack");
                ThrowIfOutOfRange(compressor.Release, 0.0f, null, "compressor release");
                CheckFinite(compressor.Gain, "compressor gain");
                CheckFinite(compressor.MakeupGain, "compressor makeupGain");
                break;
            case PhonographFilter phonograph:
                CheckFinite(phonograph.Frequency, "phonograph frequency");
                ThrowIfOutOfRange(phonograph.Depth, 0.0f, 1.0f, "phonograph depth");
                ThrowIfOutOfRange(phonograph.Crackle, 0.0f, 1.0f, "phonograph crackle");
                ThrowIfOutOfRange(phonograph.Flutter, 0.0f, 1.0f, "phonograph flutter");
                ThrowIfOutOfRange(phonograph.Room, 0.0f, 1.0f, "phonograph room");
                ThrowIfOutOfRange(phonograph.MicAgc, 0.0f, 1.0f, "phonograph micAgc");
                ThrowIfOutOfRange(phonograph.Drive, 0.0f, 1.0f, "phonograph drive");
                break;
        }

        if (filter is IFiniteCheck { HasNonFiniteNumber: true })
            throw new ArgumentOutOfRangeException(
                nameof(filter),
                "A member is not a finite number."
            );
    }

    private static void CheckFinite<T>(T? value, string name)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (value is { } candidate && !T.IsFinite(candidate))
            throw new ArgumentOutOfRangeException(name, candidate, "Must be a finite number.");
    }

    internal static void ThrowIfOutOfRange<T>(
        T? value,
        T minimum,
        T? maximum,
        string name,
        bool exclusiveMinimum = false
    )
        where T : struct, IFloatingPointIeee754<T>
    {
        if (value is not { } candidate)
            return;
        var outOfRange =
            !T.IsFinite(candidate)
            || candidate < minimum
            || (exclusiveMinimum && candidate == minimum)
            || (maximum is { } limit && candidate > limit);
        if (!outOfRange)
            return;

        var lower = exclusiveMinimum ? $"greater than {minimum}" : $"{minimum} or greater";
        throw new ArgumentOutOfRangeException(
            name,
            candidate,
            maximum is { } upperLimit
                ? $"Must be {lower} and at most {upperLimit}."
                : $"Must be {lower}."
        );
    }
}
