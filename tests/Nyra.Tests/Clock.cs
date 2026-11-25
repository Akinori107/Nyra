namespace Nyra.Tests;

internal static class Clock
{
    private static long Milliseconds() =>
        TimeProvider.System.GetTimestamp()
        / Math.Max(1, TimeProvider.System.TimestampFrequency / 1000);

    internal static async Task NextTickAsync()
    {
        var start = Milliseconds();
        while (Milliseconds() == start)
            await Task.Delay(1).ConfigureAwait(false);
    }
}
