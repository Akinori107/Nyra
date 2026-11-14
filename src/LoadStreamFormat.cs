namespace Nyra;

/// <summary>The container format of a track audio stream.</summary>
/// <remarks>
/// Any other value throws <see cref="System.ArgumentOutOfRangeException"/>.
/// </remarks>
public enum LoadStreamFormat
{
    /// <summary>Raw PCM, 16-bit 48 kHz stereo, no header.</summary>
    Raw = 0,

    /// <summary>
    /// The same PCM with a 44-byte RIFF WAV header prepended, playable by WAV readers without setup.
    /// </summary>
    /// <remarks>
    /// The header of an unknown-length stream carries a placeholder size, players show a wrong duration on it.
    /// NodeLink 3.10.0 and newer.
    /// </remarks>
    Wav = 1,
}
