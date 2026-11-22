using Microsoft.VisualStudio.Shell;
using __ExtensionName__.Commands;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__.ToolWindow;

/// <summary>Tools > __DisplayName__ > Show __DisplayName__ Window.</summary>
internal static class ShowToolWindowCommand
{
    public static Task InitializeAsync(AsyncPackage package) =>
        CommandRegistrar.AddAsync(package, PackageIds.ShowToolWindowCommandId, Show);

    private static void Show(AsyncPackage package)
    {
        // Creating the window is asynchronous; the command handler itself is not.
        Background.Run(package, "Showing the tool window", () =>
            package.ShowToolWindowAsync(typeof(MainToolWindow), id: 0, create: true, package.DisposalToken));
    }
}
