using System.Runtime.InteropServices;

namespace Nyra.Internal;

internal sealed class ErrorThrottle
{
    internal const long WindowMilliseconds = 30_000;

    private const int ReportsPerWindow = 5;

    private readonly Lock _lock = new();

    private readonly Dictionary<Type, Window> _windows = [];

    private struct Window
    {
        public long Start;
        public int Reported;
        public int Suppressed;
    }

    internal bool TryAdmit(Exception exception, out int suppressed)
    {
        var now = Environment.TickCount64;
        lock (_lock)
        {
            ref var window = ref CollectionsMarshal.GetValueRefOrAddDefault(
                _windows,
                exception.GetBaseException().GetType(),
                out var known
            );

            suppressed = 0;
            if (!known || now - window.Start >= WindowMilliseconds)
            {
                suppressed = window.Suppressed;
                window = new Window { Start = now };
            }

            if (window.Reported < ReportsPerWindow)
            {
                window.Reported++;
                return true;
            }

            window.Suppressed++;
            return false;
        }
    }
}
