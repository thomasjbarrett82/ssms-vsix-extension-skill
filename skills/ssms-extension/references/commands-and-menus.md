# Commands and menus

How commands are declared, placed in SSMS's menus and connected to code.

## Contents

- [How the pieces fit](#how-the-pieces-fit)
- [Adding a command](#adding-a-command)
- [Where commands can go](#where-commands-can-go)
- [Showing, hiding and disabling](#showing-hiding-and-disabling)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Icons](#icons)
- [When the package loads](#when-the-package-loads)
- [Calling SSMS's own commands](#calling-ssmss-own-commands)
- [Checklist when a command does not appear](#checklist-when-a-command-does-not-appear)

## How the pieces fit

Menus are declared, not created in code. The `.vsct` file is compiled into a binary command table
that SSMS merges into its menus at startup, **without loading the extension**. The extension's
code is loaded only when the user first invokes one of its commands (or earlier, on request; see
[When the package loads](#when-the-package-loads)).

A command is identified by a GUID (the command set, one per extension) plus a numeric id. The
hierarchy is always: **menu > group > button**. A button's parent is a group; a group's parent is
a menu. Groups are what draw the separator lines.

```text
Tools menu (IDM_VS_MENU_TOOLS, owned by the shell)
└── ToolsMenuGroup            (group, owned by the extension)
    └── ToolsSubMenu          (menu: "My Extension")
        ├── SubMenuCommandsGroup
        │   └── cmdidDoSomething     (button)
        └── SubMenuInfoGroup         (drawn below a separator)
            └── cmdidAbout           (button)
```

## Adding a command

Three places must agree.

**1. The `.vsct` file**: the button and its id symbol.

```xml
<Buttons>
  <Button guid="guidCommandSet" id="cmdidDoSomething" priority="0x0100" type="Button">
    <Parent guid="guidCommandSet" id="SubMenuCommandsGroup" />
    <Strings>
      <ButtonText>Do Something</ButtonText>
    </Strings>
  </Button>
</Buttons>
...
<GuidSymbol name="guidCommandSet" value="{...}">
  <IDSymbol name="cmdidDoSomething" value="0x0104" />
</GuidSymbol>
```

`priority` orders items within their parent; lower comes first. Ids must be unique within the
command set; commands, groups and menus share one number space, so keep them in separate ranges
(the template uses `0x01nn` for commands and `0x10nn` for groups and menus).

**2. `PackageIds.cs`**: the same number.

```csharp
public const int DoSomethingCommandId = 0x0104;
```

**3. A command class**, registered from the package's `InitializeAsync`.

```csharp
internal static class DoSomethingCommand
{
    public static Task InitializeAsync(AsyncPackage package) =>
        CommandRegistrar.AddAsync(package, PackageIds.DoSomethingCommandId, Execute);

    private static void Execute(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        // ...
    }
}
```

`CommandRegistrar` (in the template) wraps the handler in a try/catch that logs and shows the
error. Pass the handler as a method, as above. A lambda that calls a UI-thread method draws a
`VSTHRD010` warning, because the analyzer cannot tell which thread a lambda will run on.

**Then increase the command table version**: the number in
`[ProvideMenuResource("Menus.ctmenu", 1)]` on the package class. SSMS caches the merged menus; the
number tells it that the table changed. When a new command or shortcut does not show up after
reinstalling, this is the first thing to check.

A handler that needs to do slow work starts it on a background thread and returns:

```csharp
private static void Execute(AsyncPackage package)
{
    ThreadHelper.ThrowIfNotOnUIThread();
    var connection = QueryWindow.CreateConnection();   // read SSMS state on the UI thread
    if (connection is null) { return; }

    Background.Run(package, "Do Something", async () =>
    {
        await TaskScheduler.Default;                    // leave the UI thread
        using (connection)
        {
            await connection.OpenAsync();
            // ... query ...
        }

        await package.JoinableTaskFactory.SwitchToMainThreadAsync();
        // ... show the result ...
    });
}
```

`Background.Run` (in the template) logs any exception the work throws. `await
TaskScheduler.Default` needs `using Microsoft.VisualStudio.Threading;`.

## Where commands can go

A group's `<Parent>` decides where its commands appear. The shell's menus are declared in
`vsshlids.h` (hence `<Extern href="vsshlids.h" />`) under `guidSHLMainMenu`:

| Location | Parent |
| --- | --- |
| Tools menu | `guid="guidSHLMainMenu" id="IDM_VS_MENU_TOOLS"` |
| Help menu | `guid="guidSHLMainMenu" id="IDM_VS_MENU_HELP"` |
| Standard toolbar | `guid="guidSHLMainMenu" id="IDM_VS_TOOL_STANDARD"` |
| Right-click menu of any text editor | `guid="guidSHLMainMenu" id="IDM_VS_CTXT_CODEWIN"` |
| Right-click menu of a document tab | `guid="guidSHLMainMenu" id="IDM_VS_CTXT_EZDOCWINTAB"` |

The query window's context menus belong to SSMS. They are not in any header, so declare the
symbols yourself. These values are undocumented and are the ones used by current extensions:

```xml
<GuidSymbol name="guidSqlEditorMenus" value="{33F13AC3-80BB-4ECB-85BC-225435603A5E}">
  <IDSymbol name="IDM_SQLWB_SQLSCRIPT_CONTEXT" value="0x0050" />  <!-- query text editor -->
  <IDSymbol name="IDM_SQLWB_SQLRESMSG_CONTEXT" value="0x0060" />  <!-- Messages tab -->
  <IDSymbol name="IDM_SQLWB_SQLRESGRID_CONTEXT" value="0x0070" /> <!-- results grid -->
  <IDSymbol name="IDM_SQLWB_SHOWPLAN_CONTEXT" value="0x0080" />   <!-- execution plan -->
</GuidSymbol>
```

```xml
<Group guid="guidCommandSet" id="QueryEditorContextGroup" priority="0x0700">
  <Parent guid="guidSqlEditorMenus" id="IDM_SQLWB_SQLSCRIPT_CONTEXT" />
</Group>
```

**Object Explorer's right-click menu cannot be reached from the `.vsct` file.** SSMS builds it at
runtime. See [object-explorer.md](object-explorer.md).

A button has exactly one `<Parent>`. To show the same command in a second place, add a placement
(a sibling of `<Commands>`, not inside it):

```xml
<CommandPlacements>
  <CommandPlacement guid="guidCommandSet" id="cmdidDoSomething" priority="0x0100">
    <Parent guid="guidCommandSet" id="QueryEditorContextGroup" />
  </CommandPlacement>
</CommandPlacements>
```

A toolbar of the extension's own is a `<Menu type="Toolbar">` with groups and buttons below it;
users show it through View > Toolbars.

A top-level menu next to Tools is possible (`<Menu>` with a group parented to
`guid="guidSHLMainMenu" id="IDG_VS_MM_TOOLSADDINS"`), but a submenu under Tools is the
convention for extensions and keeps the menu bar uncrowded.

## Showing, hiding and disabling

By default a command is always visible and enabled. To control that at runtime, flag the button:

```xml
<CommandFlag>DynamicVisibility</CommandFlag>
<CommandFlag>DefaultDisabled</CommandFlag>   <!-- state before the package is loaded -->
```

and pass a status handler when registering:

```csharp
CommandRegistrar.AddAsync(package, PackageIds.DoSomethingCommandId, Execute, UpdateStatus);

private static void UpdateStatus(OleMenuCommand command)
{
    ThreadHelper.ThrowIfNotOnUIThread();
    command.Visible = command.Enabled = QueryWindow.IsActive;
}
```

The status handler runs whenever a menu containing the command opens, and more often for toolbar
buttons. It must be fast: no database access, no parsing of the whole script.

Until the package is loaded, no code runs, so the status handler cannot run either. A command in
a context menu that should be hidden outside query windows before first use needs either the
package loaded at startup, or a `<VisibilityConstraints>` entry bound to a UI context. Showing the
command always and reporting "not applicable here" on click is the simplest robust choice.

## Keyboard shortcuts

```xml
<KeyBindings>
  <!-- Ctrl+K, Ctrl+T while a text editor has focus -->
  <KeyBinding guid="guidCommandSet" id="cmdidDoSomething" editor="guidVSStd97"
              key1="K" mod1="Control" key2="T" mod2="Control" />
</KeyBindings>
```

`<KeyBindings>` is a sibling of `<Commands>`. `editor="guidVSStd97"` makes the binding global.
Check that the combination is free in Tools > Options > Environment > Keyboard: SSMS and the
editor already use most single-chord Ctrl and Ctrl+Shift combinations, and a binding that
collides with a built-in one may simply not fire. Two-chord combinations starting with an unused
prefix are the safest. Users can rebind any command there; it is listed under the name derived
from its button text. Shortcuts are cached with the command table, so increase the
`ProvideMenuResource` version when changing them.

## Icons

Icons are optional. The straightforward way is a PNG strip in the project, 16x16 pixels per
image, 32-bit with alpha:

```xml
<Commands package="guidPackage">
  ...
  <Bitmaps>
    <Bitmap guid="guidImages" href="Resources\Commands.png" usedList="imgDoSomething" />
  </Bitmaps>
</Commands>
...
<GuidSymbol name="guidImages" value="{new GUID}">
  <IDSymbol name="imgDoSomething" value="1" />   <!-- 1-based position in the strip -->
</GuidSymbol>
```

and in the button: `<Icon guid="guidImages" id="imgDoSomething" />`.

## When the package loads

By default the package loads when one of its commands is first invoked. That is the right default:
it costs SSMS nothing at startup. Load earlier only when the extension must act before the user
invokes anything: hooking Object Explorer, listening for editor or document events, or updating
command visibility.

```csharp
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
```

SSMS starts without a solution, so the `NoSolution` context is active immediately and the package
loads right after startup. Always combine it with `BackgroundLoad` and
`AllowsBackgroundLoading = true` on `PackageRegistration`; synchronous auto-load delays startup
and the shell warns the user about it. Extensions that should also load when a solution is open
add a second attribute with `UIContextGuids80.SolutionExists`.

Whatever triggers the load, `InitializeAsync` should finish quickly: register commands, start
anything slow in the background, return.

## Calling SSMS's own commands

Built-in commands can be run by name through the automation model:

```csharp
var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(SDTE));
dte.ExecuteCommand("Query.Execute");
```

Names are the ones shown in Tools > Options > Environment > Keyboard, for example `Query.Execute`,
`Query.CancelExecutingQuery`, `Query.ResultsToGrid`, `File.NewQuery`, `Edit.SelectAll`. A command
that is unavailable in the current context throws, so wrap the call.

## Checklist when a command does not appear

1. Was SSMS restarted after installing?
2. Was the `ProvideMenuResource` version increased since the `.vsct` last changed?
3. Do `guid` and `id` on the `<Button>` have matching `<GuidSymbol>` and `<IDSymbol>` entries, and
   is the button's `<Parent>` a **group** (not a menu)?
4. Does that group's parent menu exist, with the right GUID and id?
5. Does the `package="..."` attribute on `<Commands>` name the symbol whose value equals the
   package class's `[Guid]`?
6. Does the `.csproj` contain `<VSCTCompile Include="....vsct" ResourceName="Menus.ctmenu" />`?
7. Is the button flagged `DynamicVisibility` with a status handler that hides it?
8. If the menu item appears but does nothing: the id in `PackageIds.cs` differs from the
   `.vsct`, the command class is not called from `InitializeAsync`, or the package failed to
   load. See the logs section in [build-install-debug.md](build-install-debug.md).
