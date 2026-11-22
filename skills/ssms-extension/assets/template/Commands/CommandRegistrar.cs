using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__.Commands;

/// <summary>
/// Connects a command id from the .vsct file to its handler. Handlers run inside SSMS's
/// process, so an unhandled exception would take the whole application down: every handler
/// registered here is wrapped so a failure is logged and shown instead.
/// </summary>
internal static class CommandRegistrar
{
    /// <param name="package">The owning package.</param>
    /// <param name="commandId">The command's id in <see cref="PackageIds"/>.</param>
    /// <param name="execute">
    /// Runs on the UI thread when the command is invoked. Pass a method (not a lambda that calls
    /// one), so the threading analyzers can see that it starts on the UI thread.
    /// </param>
    /// <param name="queryStatus">
    /// Optional. Runs on the UI thread right before the command is displayed; set Visible and
    /// Enabled there. It needs the DynamicVisibility flag in the .vsct, runs often, and must
    /// be fast.
    /// </param>
    public static async Task AddAsync(AsyncPackage package, int commandId, Action<AsyncPackage> execute, Action<OleMenuCommand>? queryStatus = null)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            throw new InvalidOperationException("The menu command service is not available.");
        }

        var command = new OleMenuCommand(
            (_, _) => Guarded(package, () => execute(package), showError: true),
            new CommandID(PackageIds.CommandSet, commandId));

        if (queryStatus is not null)
        {
            command.BeforeQueryStatus += (sender, _) => Guarded(package, () => queryStatus((OleMenuCommand)sender), showError: false);
        }

        commandService.AddCommand(command);
    }

    private static void Guarded(AsyncPackage package, Action action, bool showError)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("A command failed.", ex);
            if (showError)
            {
                VsShellUtilities.ShowMessageBox(
                    package,
                    ex.Message,
                    PackageIds.DisplayName,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}
