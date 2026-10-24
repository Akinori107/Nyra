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
        if (handler is null)
            return;

        foreach (AsyncEventHandler<TArgs> invocation in handler.GetInvocationList())
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
