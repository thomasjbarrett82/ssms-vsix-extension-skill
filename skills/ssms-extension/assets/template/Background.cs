using System;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__;

/// <summary>
/// Starts asynchronous work from synchronous code (command handlers, event handlers) without
/// blocking it. This is the one place that does fire-and-forget, so it is also the one place
/// that guarantees a failure is logged instead of crashing SSMS.
/// </summary>
internal static class Background
{
    /// <summary>
    /// Runs <paramref name="work"/> and returns immediately. The work starts on the calling
    /// thread; use "await TaskScheduler.Default" inside it to leave the UI thread and
    /// "await package.JoinableTaskFactory.SwitchToMainThreadAsync()" to come back.
    /// </summary>
    public static void Run(AsyncPackage package, string description, Func<Task> work)
    {
        // The package's factory tracks the task, so SSMS waits for it when shutting down.
        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
                // SSMS is shutting down, or the work was cancelled on purpose.
            }
            catch (Exception ex)
            {
                Log.Error($"{description} failed.", ex);
            }
        });
    }
}
