namespace Nyra.Internal;

/// <summary>
/// Raises native C# events safely: an exception thrown by one handler never prevents the
/// remaining handlers from running. Without an error callback, the first handler exception
/// is rethrown.
/// </summary>
internal static class EventExtensions
{
    public static void Raise<TArgs>(
        this EventHandler<TArgs>? handler,
        object sender,
        TArgs args,
        Action<Exception>? onError = null
    )
        where TArgs : System.EventArgs
    {
        if (handler is null)
            return;

        foreach (EventHandler<TArgs> invocation in handler.GetInvocationList())
        {
            try
            {
                invocation(sender, args);
            }
            catch (Exception exception)
            {
                if (onError is null)
                    throw;

                onError(exception);
            }
        }
    }
}
