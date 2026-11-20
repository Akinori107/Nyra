namespace Nyra.Queue;

/// <summary>What a <see cref="GuildQueue"/> repeats when a track ends.</summary>
public enum LoopMode
{
    /// <summary>Nothing repeats, the queue ends when its last track ends.</summary>
    Off,

    /// <summary>The current track repeats until it is skipped.</summary>
    Track,

    /// <summary>Tracks that ended go back to the end of the queue.</summary>
    Queue,
}
