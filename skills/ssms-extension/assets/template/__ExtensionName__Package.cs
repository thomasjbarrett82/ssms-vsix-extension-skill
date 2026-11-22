using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using __ExtensionName__.Commands;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__;

/// <summary>
/// The extension's entry point. SSMS creates it the first time one of its commands is used,
/// or at startup when a ProvideAutoLoad attribute asks for that.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(PackageIds.DisplayName, PackageIds.Description, BuildInfo.Version)]
// The number is the command table version. Increase it whenever the .vsct changes, or SSMS
// may keep serving the cached menus and key bindings of the previous build.
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageIds.PackageGuidString)]
// #if ObjectExplorer
// Object Explorer menu items are added at runtime, so the package has to load at startup.
// SSMS never has a solution open at startup, so the NoSolution context is active as soon as
// the shell is up.
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
// #endif
// #if ToolWindow
[ProvideToolWindow(typeof(ToolWindow.MainToolWindow), Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Right, DockedWidth = 360, DockedHeight = 480)]
// #endif
// #if Options
[ProvideOptionPage(typeof(Options.GeneralOptionsPage), PackageIds.DisplayName, "General", 0, 0, true)]
// #endif
public sealed class __ExtensionName__Package : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Each feature registers on its own, so one failure does not take the others down.
        await RegisterAsync("About command", () => AboutCommand.InitializeAsync(this));
        // #if QueryEditor
        await RegisterAsync("Query editor command", () => QueryEditor.OpenSelectionInNewQueryCommand.InitializeAsync(this));
        // #endif
        // #if ToolWindow
        await RegisterAsync("Tool window command", () => ToolWindow.ShowToolWindowCommand.InitializeAsync(this));
        // #endif
        // #if Options
        await RegisterAsync("Options command", () => Options.OptionsCommand.InitializeAsync(this));
        // #endif
        // #if ObjectExplorer
        ObjectExplorer.ObjectExplorerMenu.Start(this);
        // #endif
    }

    private static async Task RegisterAsync(string feature, Func<Task> register)
    {
        try
        {
            await register();
        }
        catch (Exception ex)
        {
            Log.Error($"{feature} could not be registered.", ex);
        }
    }
}
