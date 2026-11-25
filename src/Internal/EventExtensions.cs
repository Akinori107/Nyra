namespace Nyra.Internal;

internal static class EventExtensions
{
    public static async Task RaiseAsync<TArgs>(
        this AsyncEventHandler<TArgs>? handler,
        object sender,
        TArgs args,
        Func<Exception, System.EventArgs?, Task> onError
    )
        where TArgs : System.EventArgs
    {
        foreach (var invocation in Delegate.EnumerateInvocationList(handler))
            try
            {
                await invocation(sender, args).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                try
                {
                    await onError(exception, args).ConfigureAwait(false);
                }
                catch { }
            }
    }
}
