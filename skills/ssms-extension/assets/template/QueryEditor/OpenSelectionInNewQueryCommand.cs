using Microsoft.VisualStudio.Shell;
using __ExtensionName__.Commands;
using __ExtensionName__.Ssms;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__.QueryEditor;

/// <summary>
/// Sample query editor command: copies the selection (or the whole script) of the active query
/// window into a new query window on the same connection. It appears in the Tools submenu and
/// in the editor's right-click menu, and shows the three things most editor commands need:
/// reading the text, reading the connection, and opening a window.
/// </summary>
internal static class OpenSelectionInNewQueryCommand
{
    public static Task InitializeAsync(AsyncPackage package) =>
        CommandRegistrar.AddAsync(package, PackageIds.OpenSelectionInNewQueryCommandId, Execute, UpdateStatus);

    private static void UpdateStatus(OleMenuCommand command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Runs every time a menu containing the command is about to open: keep it cheap.
        var isQueryWindow = QueryWindow.IsActive;
        command.Visible = isQueryWindow;
        command.Enabled = isQueryWindow;
    }

    private static void Execute(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var text = QueryWindow.SelectedText ?? QueryWindow.AllText;
        if (string.IsNullOrWhiteSpace(text))
        {
            QueryWindow.SetStatus("There is no text to copy.");
            return;
        }

        if (!QueryWindow.Open(text!, QueryWindow.ActiveConnection))
        {
            QueryWindow.SetStatus("A new query window could not be opened.");
        }
    }
}
