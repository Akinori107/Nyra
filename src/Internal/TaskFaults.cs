namespace Nyra.Internal;

internal static class TaskFaults
{
    internal static void ObserveFaults(this Task task, Action<Exception>? onFault = null) =>
        task.ContinueWith(
            static (faulted, handler) =>
            {
                var exception = faulted.Exception!;
                ((Action<Exception>?)handler)?.Invoke(exception);
            },
            onFault,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
}
