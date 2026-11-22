using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__;

/// <summary>
/// Diagnostics for the extension. Messages go to a "__DisplayName__" pane in the Output window
/// (View > Output); errors also go to the SSMS activity log, which is written when SSMS is
/// started with "SSMS.exe /log". Safe to call from any thread, and never throws.
/// </summary>
internal static class Log
{
    private static readonly Guid PaneGuid = new Guid("__OutputPaneGuid__");
    private static IVsOutputWindowPane? s_pane;

    public static void Info(string message) => Write(message, isError: false);

    public static void Error(string message, Exception? exception = null) =>
        Write(exception is null ? message : $"{message}{Environment.NewLine}{exception}", isError: true);

    private static void Write(string message, bool isError)
    {
        // Fire and forget: logging must not block or fail its caller. Not Background.Run,
        // which needs a package and reports its own failures here. The analyzer warning is
        // about tasks that outlive the package; a lost log line at shutdown is acceptable.
#pragma warning disable VSSDK007
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(() => WriteAsync(message, isError));
#pragma warning restore VSSDK007
    }

    private static async Task WriteAsync(string message, bool isError)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (isError)
            {
                ActivityLog.TryLogError(PackageIds.DisplayName, message);
            }

            Pane()?.OutputStringThreadSafe($"{DateTime.Now:HH:mm:ss} {(isError ? "ERROR " : string.Empty)}{message}{Environment.NewLine}");
        }
        catch
        {
            // Nothing sensible is left to do when logging itself fails.
        }
    }

    private static IVsOutputWindowPane? Pane()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (s_pane is null && Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow output)
        {
            var guid = PaneGuid;
            if (output.GetPane(ref guid, out s_pane) != VSConstants.S_OK || s_pane is null)
            {
                output.CreatePane(ref guid, PackageIds.DisplayName, fInitVisible: 1, fClearWithSolution: 0);
                output.GetPane(ref guid, out s_pane);
            }
        }

        return s_pane;
    }
}
