# Tool windows and options

Dockable windows, dialogs, the Tools > Options page, and where to keep settings. Template code:
`ToolWindow/` and `Options/`.

## Contents

- [Tool windows](#tool-windows)
- [Theming](#theming)
- [Dialogs and messages](#dialogs-and-messages)
- [Options page](#options-page)
- [Other places to store data](#other-places-to-store-data)

## Tool windows

A tool window is a dockable pane like Object Explorer. Three parts:

**1. The pane**: a class with its own GUID that hosts a WPF control.

```csharp
[Guid("...")]
public sealed class MainToolWindow : ToolWindowPane
{
    public MainToolWindow() : base(null)
    {
        Caption = "My Extension";
        Content = new MainToolWindowControl();   // a WPF UserControl
    }
}
```

**2. Registration** on the package class.

```csharp
[ProvideToolWindow(typeof(MainToolWindow), Style = VsDockStyle.Tabbed,
    Orientation = ToolWindowOrientation.Right, DockedWidth = 360, DockedHeight = 480)]
```

| Setting | Effect |
| --- | --- |
| `Style = VsDockStyle.Tabbed` | Docked as a tab at the edge given by `Orientation`, the first time it is shown |
| `Style = VsDockStyle.MDI` | Opens in the document area, like a query window. Suits wide content |
| `Style = VsDockStyle.Float` | Floating window |
| `Window = "{guid}"` | Dock next to an existing tool window, identified by its GUID |
| `MultiInstances = true` | Several instances, told apart by the `id` passed when showing |
| `Transient = true` | Not reopened when SSMS restarts. Use it when the content cannot be restored, for example a window bound to one connection |

The placement settings apply only the first time; after that SSMS remembers where the user put
the window.

**3. A command that shows it.**

```csharp
await package.ShowToolWindowAsync(typeof(MainToolWindow), id: 0, create: true, package.DisposalToken);
```

A command handler is synchronous, so the template starts this through `Background.Run`.

Points that cause trouble:

- SSMS can create the pane at startup, to restore a window that was open in the last session,
  before the package's `InitializeAsync` has finished. The pane's constructor and the control's
  constructor must not depend on anything `InitializeAsync` sets up. Load data in the control's
  `Loaded` event or on demand.
- The control's event handlers run on the UI thread. Fetch data in the background and come back
  to update the UI.
- A window that follows the active query window needs to know when that changes. Polling on a
  `DispatcherTimer` is crude but robust; the event-based route is `IVsMonitorSelection` /
  `IVsSelectionEvents` (`SEID_DocumentFrame` changes).
- XAML files in an SDK-style project with `UseWPF` are picked up automatically. The `x:Class`
  value must match the code-behind's namespace and class name.
- Avoid third-party WPF control libraries where possible. A library that another extension also
  ships, in a different version, loads once for the whole process and breaks one of the two. If
  one is needed, add `[ProvideBindingPath]` to the package.

## Theming

SSMS 22 has light and dark themes. A window with hard-coded colours is unreadable in one of them.
Bind colours to the shell's theme resources, which update when the theme changes:

```xml
<UserControl ...
    xmlns:vsshell="clr-namespace:Microsoft.VisualStudio.Shell;assembly=Microsoft.VisualStudio.Shell.15.0"
    Background="{DynamicResource {x:Static vsshell:VsBrushes.ToolWindowBackgroundKey}}"
    Foreground="{DynamicResource {x:Static vsshell:VsBrushes.ToolWindowTextKey}}">
```

`VsBrushes` covers the common cases (`ToolWindowBackgroundKey`, `ToolWindowTextKey`,
`WindowKey`, `WindowTextKey`, `GrayTextKey`, `ToolWindowBorderKey`). Standard WPF controls
(`Button`, `TextBox`, `ComboBox`, `DataGrid`) keep their default light appearance unless styled;
set their `Foreground`, `Background` and `BorderBrush` from the same resources, and check the
window in both themes before calling it done.

## Dialogs and messages

- **Message box**: `VsShellUtilities.ShowMessageBox(package, message, title, OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST)`.
  It is parented to the SSMS window correctly, which a bare `MessageBox.Show` is not.
- **Status bar**, for results that do not need acknowledging:
  `((IVsStatusbar)Package.GetGlobalService(typeof(SVsStatusbar))).SetText("...")`.
  Prefer it over a message box for "done" and "nothing to do" messages.
- **Custom dialog**: derive the window from `Microsoft.VisualStudio.PlatformUI.DialogWindow`
  instead of `Window` and call `ShowModal()`. It is centred on and owned by the SSMS main window
  and behaves correctly with the shell's modal state.
- **Output pane**, for logs and long text: the template's `Log` class.

## Options page

A page under Tools > Options is a class with public properties:

```csharp
[Guid("...")]
[ComVisible(true)]
public sealed class GeneralOptionsPage : DialogPage
{
    [Category("General")]
    [DisplayName("Rows for SELECT TOP")]
    [Description("How many rows generated SELECT TOP scripts ask for.")]
    [DefaultValue(100)]
    public int TopRowCount { get; set; } = 100;
}
```

registered on the package:

```csharp
[ProvideOptionPage(typeof(GeneralOptionsPage), "My Extension", "General", 0, 0, true)]
```

The two strings are the category (the node in the Options tree) and the page name below it.
SSMS shows the properties in a property grid and stores them in its own settings store per user.
No file handling is needed.

Reading the settings, on the UI thread:

```csharp
var options = (GeneralOptionsPage)package.GetDialogPage(typeof(GeneralOptionsPage));
```

Opening the page from a command: `package.ShowOptionPage(typeof(GeneralOptionsPage))`.

Details:

- Property types need a `TypeConverter` to and from string: `bool`, numbers, `string`, enums
  work as they are. A list or a custom class needs a converter, or is better kept in a file (see
  below) with a button on a custom page.
- Initialise properties to their defaults and repeat the value in `[DefaultValue]`.
- Validate in the setter (clamp a number to a sane range) instead of trusting the stored value.
- Read settings when they are needed instead of caching them at startup, so a change takes effect
  without a restart. `GetDialogPage` returns a cached instance, so this is cheap.
- For a custom layout instead of the property grid, override `Window` (WinForms) or derive from
  `UIElementDialogPage` and override `Child` (WPF).
- To group the page under an existing SSMS category, pass that category's name, for example
  `"SQL Server Object Explorer"`.

## Other places to store data

| Data | Where |
| --- | --- |
| A handful of simple settings | `DialogPage` properties, as above |
| Lists, templates, history, caches | Files below `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)\<ExtensionName>` |
| Settings the user should be able to copy between machines | A JSON or XML file below `SpecialFolder.ApplicationData` (roaming), with export and import commands |
| Secrets | Not in a settings file. Use Windows Credential Manager or DPAPI (`ProtectedData`) |

Do not write into the extension's install folder: it is replaced on every update and may be
read-only. Create data folders on first use, tolerate a missing or corrupt file by falling back
to defaults, and never let a settings problem stop the package from loading.
