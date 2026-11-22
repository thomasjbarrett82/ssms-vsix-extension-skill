---
name: ssms-extension
description: Build, extend, package, install and debug extensions (VSIX packages) for SQL Server Management Studio 22. Use whenever the user wants to create an SSMS extension, plugin or add-in, add menu commands, Object Explorer context-menu items, query editor or results-grid commands, tool windows or options pages to SSMS, or fix an SSMS extension that fails to build, install or load. Includes a project template and scaffold script. Also covers porting to SSMS 21 and the legacy 32-bit versions (18-20).
---

# SSMS 22 extensions

An SSMS extension is a Visual Studio package (VSIX) that SSMS loads into its own process. SSMS 22
is built on the Visual Studio 2026 shell, so the Visual Studio SDK applies, with SSMS-specific
differences that this skill covers. Most of what makes an SSMS extension useful (Object Explorer,
query windows, connections) is reached through SSMS's internal assemblies, which Microsoft neither
documents nor supports for third parties. The patterns here are the ones that working open-source
extensions have converged on.

## Rules that are not negotiable

Breaking any of these produces an extension that builds but does not install or load.

1. **Target .NET Framework 4.8 (`net48`).** SSMS 22 is a .NET Framework process. An assembly built
   for modern .NET cannot be loaded. Logic that should be testable or shared goes in a separate
   `netstandard2.0` library.
2. **Target SSMS in the manifest**: `<InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,23.0)">`
   with `<ProductArchitecture>amd64</ProductArchitecture>` (and a second target for `arm64`). A
   Visual Studio target id (`Microsoft.VisualStudio.Community`) will not install into SSMS.
3. **Never ship assemblies that SSMS already has.** Reference the Visual Studio SDK with
   `ExcludeAssets="runtime"` and SSMS's own DLLs with `<Private>false</Private>`. The VSIX should
   contain the extension's own DLLs only. A bundled copy of `Microsoft.Data.SqlClient`, SMO or
   ScriptDom conflicts with the host's copy, and has broken extensions on SSMS updates.
4. **Never let an exception escape into SSMS.** Command handlers, event handlers and background
   work run in SSMS's process; an unhandled exception can close SSMS and lose the user's unsaved
   queries. Catch at every entry point, log, and carry on.
5. **Call shell and SSMS services on the UI thread; do slow work off it.** Switch with
   `await JoinableTaskFactory.SwitchToMainThreadAsync()`. Never run a database query, file scan or
   network call on the UI thread: SSMS freezes for its duration.
6. **Increase the version for every build a user installs.** The installer refuses a version that
   is equal to or lower than the installed one.

## Workflow

### 1. Check the machine

Windows with SSMS 22 installed (the build compiles against its assemblies) and the .NET SDK 8 or
later (`dotnet --version`). Visual Studio is optional. SSMS's install folder is normally
`C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE`; the preview
channel uses `Preview` in place of `Release`.

### 2. Scaffold

For a new extension, run the scaffold script instead of writing the project by hand. It copies
`assets/template`, generates fresh GUIDs and removes the parts that were not asked for.

```powershell
powershell -ExecutionPolicy Bypass -File "<skill folder>\scripts\new-extension.ps1" `
    -Name IndexAdvisor -DisplayName "Index Advisor" -Publisher "Contoso" `
    -Description "Suggests missing indexes." -OutputDir "C:\src" -Features "ObjectExplorer,Options"
```

`-Name` is the project, assembly and namespace name (a C# identifier). Pass `-Features` as one
comma-separated string, as shown. The project is created in `<OutputDir>\<Name>`.

| Feature | Adds | Read before changing it |
| --- | --- | --- |
| (always) | Package, Tools submenu, About command, logging, `Ssms/QueryWindow.cs` helper | [commands-and-menus.md](references/commands-and-menus.md) |
| `ObjectExplorer` | Submenu on Object Explorer nodes' right-click menu | [object-explorer.md](references/object-explorer.md) |
| `QueryEditor` | Command in the query editor's right-click menu | [query-editor.md](references/query-editor.md) |
| `ToolWindow` | Dockable WPF window | [tool-windows-and-options.md](references/tool-windows-and-options.md) |
| `Options` | Page under Tools > Options | [tool-windows-and-options.md](references/tool-windows-and-options.md) |

Ask the user which features they need if it is not clear; `-Features None` gives the bare package.
The sample commands in the template are placeholders that show each API in use. Replace them with
the user's functionality rather than leaving them in.

For an existing extension, do not scaffold. Read its `.csproj`, `source.extension.vsixmanifest` and
`.vsct` first, and compare them with [project-setup.md](references/project-setup.md).

### 3. Implement

Adding a command touches three places that must agree, which is the most common source of "my
command does not appear":

1. `.vsct`: a `<Button>` with a parent group, and an `<IDSymbol>` for its id.
2. `PackageIds.cs`: the same id as a constant.
3. A command class that registers a handler, called from the package's `InitializeAsync`.

Then increase the number in `[ProvideMenuResource("Menus.ctmenu", N)]`. Details and menu locations
are in [commands-and-menus.md](references/commands-and-menus.md).

### 4. Build

```powershell
.\build.ps1          # or: dotnet build -c Release
```

Output: `bin\Release\net48\<Name>.vsix`. The build must finish with no warnings; the template does.
Treat `VSTHRD` and `VSSDK` analyzer warnings as real defects, since they flag threading mistakes
that deadlock or crash the host.

### 5. Install and test

Installing changes the user's SSMS, so ask before doing it. SSMS must be closed.

```powershell
.\install.ps1              # install
.\install.ps1 -Reinstall   # same version again, during development
.\install.ps1 -Uninstall
```

Then have the user start SSMS and check **Tools > (display name) > About**. A build that compiles
proves little: only running inside SSMS shows that the package loads and the menus appear. Say so
plainly when reporting, and do not claim an extension works on the strength of a build.

If something is wrong, go to [build-install-debug.md](references/build-install-debug.md), which
has a symptom table.

## Reference files

| File | Contents |
| --- | --- |
| [project-setup.md](references/project-setup.md) | Project file, manifest, package versions, referencing SSMS assemblies, version handling, what belongs in the VSIX |
| [commands-and-menus.md](references/commands-and-menus.md) | `.vsct` structure, menu and context-menu locations in SSMS, visibility, shortcuts, icons, package loading |
| [object-explorer.md](references/object-explorer.md) | Context-menu injection, reading the selected node, node kinds, connections |
| [query-editor.md](references/query-editor.md) | Active connection, reading and writing script text, new query windows, executing, results grid, own database connections |
| [tool-windows-and-options.md](references/tool-windows-and-options.md) | Tool windows, theming, options pages, storing settings |
| [build-install-debug.md](references/build-install-debug.md) | Installing, uninstalling, where extensions live, logs, attaching a debugger, CI and releases, troubleshooting table |
| [legacy-versions.md](references/legacy-versions.md) | SSMS 21, and the 32-bit versions 18-20: what differs and how to support several versions |

## Working with SSMS internals

`SqlWorkbench.Interfaces.dll`, `SqlPackageBase.dll`, `SQLEditors.dll` and their neighbours are
internal to SSMS. Assume less than you would about a documented API:

- Look at what the installed version really exposes before writing code against a type or member:
  `scripts\inspect-ssms-type.ps1 -Assembly SqlWorkbench.Interfaces -Type *INodeInformation`
  prints a type's members from the installed SSMS (add `-NonPublic` for members that need
  reflection; omit `-Type` and pass `-Filter *Explorer*` to search for types).
- Prefer the public interfaces (`IObjectExplorerService`, `INodeInformation`, `IScriptFactory`)
  over reflection into private members. Where reflection is unavoidable, check every step for null
  and degrade to "feature unavailable" with a log entry.
- Tell the user when a feature depends on an undocumented member, because an SSMS update can break
  it without notice.
