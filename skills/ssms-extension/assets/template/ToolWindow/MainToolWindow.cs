using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace __ExtensionName__.ToolWindow;

/// <summary>
/// A dockable window, like Object Explorer or Properties. SSMS creates it on demand and
/// restores its position between sessions. The pane only hosts the WPF control; behaviour
/// lives in <see cref="MainToolWindowControl"/>.
/// </summary>
[Guid("__ToolWindowGuid__")]
public sealed class MainToolWindow : ToolWindowPane
{
    public MainToolWindow() : base(null)
    {
        Caption = PackageIds.DisplayName;
        Content = new MainToolWindowControl();
    }
}
