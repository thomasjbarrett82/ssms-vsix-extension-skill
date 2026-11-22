using System;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__.Commands;

/// <summary>
/// Tools > __DisplayName__ > About. Besides showing the version, it is the quickest proof
/// that the package loaded inside SSMS and its commands are wired up.
/// </summary>
internal static class AboutCommand
{
    public static Task InitializeAsync(AsyncPackage package) =>
        CommandRegistrar.AddAsync(package, PackageIds.AboutCommandId, Execute);

    private static void Execute(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var host = Process.GetCurrentProcess().MainModule?.FileVersionInfo;
        var message =
            $"{PackageIds.DisplayName} {BuildInfo.Version}{Environment.NewLine}" +
            $"{PackageIds.Description}{Environment.NewLine}{Environment.NewLine}" +
            $"Host: {host?.ProductName} {host?.ProductVersion}{Environment.NewLine}" +
            $"Runtime: .NET Framework {Environment.Version} ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")})";

        VsShellUtilities.ShowMessageBox(
            package,
            message,
            PackageIds.DisplayName,
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
