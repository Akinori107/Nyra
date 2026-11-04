namespace Nyra.Internal;

internal static class TimeProviderExtensions
{
    internal static long GetMilliseconds(this TimeProvider provider) =>
        provider.GetTimestamp() / Math.Max(1, provider.TimestampFrequency / 1000);
}

internal sealed class TimeoutLink : IDisposable
{
    private readonly CancellationTokenSource _timeout;
    private readonly CancellationTokenSource? _linked;

    internal TimeoutLink(TimeProvider time, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _timeout = new CancellationTokenSource(timeout, time);
        if (cancellationToken.CanBeCanceled)
            _linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _timeout.Token
            );
    }

    internal CancellationToken Token => (_linked ?? _timeout).Token;

    internal bool IsCancellationRequested => (_linked ?? _timeout).IsCancellationRequested;

    public void Dispose()
    {
        _linked?.Dispose();
        _timeout.Dispose();
    }
}
