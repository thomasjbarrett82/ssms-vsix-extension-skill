using Microsoft.VisualStudio.Shell;
using __ExtensionName__.Commands;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__.Options;

/// <summary>Tools > __DisplayName__ > Options... opens the extension's page in Tools > Options.</summary>
internal static class OptionsCommand
{
    public static Task InitializeAsync(AsyncPackage package) =>
        CommandRegistrar.AddAsync(package, PackageIds.OptionsCommandId, Execute);

    private static void Execute(AsyncPackage package) => package.ShowOptionPage(typeof(GeneralOptionsPage));
}
