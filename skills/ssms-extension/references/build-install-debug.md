# Build, install, debug, release

## Contents

- [Build](#build)
- [Install and uninstall](#install-and-uninstall)
- [Where things are on disk](#where-things-are-on-disk)
- [The development loop](#the-development-loop)
- [Logs](#logs)
- [Debugging](#debugging)
- [Troubleshooting](#troubleshooting)
- [Continuous integration](#continuous-integration)
- [Releasing](#releasing)

## Build

```powershell
dotnet build -c Release          # or .\build.ps1 in a scaffolded project
```

produces `bin\Release\net48\<Name>.vsix`. Requirements: .NET SDK and SSMS 22 on the machine.
Visual Studio is not needed for an SDK-style project. When SSMS is somewhere else:

```powershell
dotnet build -c Release -p:SsmsIdeDir="D:\SSMS22\Release\Common7\IDE"
dotnet build -c Release -p:SsmsChannel=Preview      # template only: the preview channel folder
```

The first build restores the Visual Studio SDK packages, which takes a few minutes and a few
hundred megabytes.

## Install and uninstall

Installing modifies the user's SSMS. Ask before doing it on their behalf, and never while they
have SSMS open with unsaved work: the installer waits for SSMS to exit.

**By hand**: close SSMS and double-click the `.vsix`. The VSIX Installer lists SSMS 22 as a
target; confirm, wait, start SSMS.

**From a script**: use the installer that ships with SSMS, so the right product is targeted.

```powershell
$installer = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe'
& $installer /quiet "C:\path\MyExtension.vsix"                       # install or upgrade
& $installer /quiet "/uninstall:MyExtension.6f0c..."                 # the Identity Id from the manifest
```

`VSIXInstaller.exe` returns immediately when started from a console; use
`Start-Process -Wait -PassThru` and check `ExitCode`, as the template's `install.ps1` does. A
non-zero exit code is explained in the newest `dd_VSIXInstaller_*.log` in `%TEMP%`.

Installer behaviour to plan around:

- **A version equal to or lower than the installed one is refused.** For a new build either
  increase the manifest version or uninstall first (`install.ps1 -Reinstall`).
- The install is per user and needs no elevation, unless the manifest says
  `<Installation AllUsers="true">`.
- SSMS reads extensions at startup only. A change is never visible in a running instance.

**Uninstall** through the script above, or SSMS's own extension manager. Uninstalling is also
the first step when an installation is in a confused state.

## Where things are on disk

| What | Path |
| --- | --- |
| SSMS 22 program folder | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE` |
| Per-user extensions | `%LocalAppData%\Microsoft\SSMS\22.0_<id>\Extensions\<random folder>` |
| All-users and built-in extensions | `<program folder>\Extensions` |
| MEF component cache | `%LocalAppData%\Microsoft\SSMS\22.0_<id>\ComponentModelCache` |
| Activity log | `%AppData%\Microsoft\SSMS\22.0_<id>\ActivityLog.xml` |
| Installer logs | `%TEMP%\dd_VSIXInstaller_*.log` |

`<id>` is an instance id such as `22.0_590b657d`; there is one folder per installed SSMS 22
instance. To find where an extension was installed:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\Extensions" -Recurse -Filter MyExtension.dll
```

## The development loop

The honest cycle is: build, close SSMS, reinstall, start SSMS, try it. Two ways to shorten it:

- **Replace the DLL in place.** When only code changed (not the `.vsct`, the manifest, or the
  package's registration attributes), close SSMS and copy the new `<Name>.dll` and `.pdb` over
  the installed ones (find the folder with the command above). This skips the installer.
  Anything that changes the generated `.pkgdef` or the command table needs a real reinstall.
- **Keep SSMS-independent logic in a separate library with unit tests**, so most changes are
  verified without SSMS at all.

After changing the `.vsct`, increase the `ProvideMenuResource` version (see
[commands-and-menus.md](commands-and-menus.md)).

What an AI agent can and cannot verify: it can build, inspect the VSIX contents and install. It
cannot see SSMS's window. After installing, tell the user exactly what to click and what they
should see, and ask them to report back (the Output pane text or `ActivityLog.xml` when it
fails). Do not report a feature as working on the basis of a successful build.

## Logs

**The extension's own log.** The template's `Log` class writes to a pane named after the
extension in View > Output. Log at feature boundaries (registered, hooked, failed and why). It is
the first thing to ask a user for.

**The activity log.** Package load failures happen before any extension code runs, so they only
appear here. Start SSMS with logging on:

```powershell
& 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe' /log
```

then open `%AppData%\Microsoft\SSMS\22.0_<id>\ActivityLog.xml` and search for the extension's
name or package GUID. Typical entries: "SetSite failed for package", a `FileNotFoundException`
or `FileLoadException` naming an assembly (a missing or conflicting dependency), or an exception
thrown from `InitializeAsync`.

```powershell
Select-String -Path "$env:APPDATA\Microsoft\SSMS\22.0_*\ActivityLog.xml" -Pattern 'MyExtension' -Context 0,6
```

## Debugging

**Attach to the running process.** Start SSMS, then in Visual Studio use Debug > Attach to
Process, pick `SSMS.exe`, and select the "Managed (.NET Framework 4.x)" code type. Breakpoints
bind once the extension's DLL is loaded, which for an on-demand package is after its first
command is used. The installed DLL must be the one just built, with its `.pdb` beside it (the
template includes the symbols in the VSIX).

**Start SSMS under the debugger.** In an SDK-style project add `Properties\launchSettings.json`:

```json
{
  "profiles": {
    "SSMS": {
      "commandName": "Executable",
      "executablePath": "C:\\Program Files\\Microsoft SQL Server Management Studio 22\\Release\\Common7\\IDE\\SSMS.exe",
      "commandLineArgs": "/log"
    }
  }
}
```

F5 then starts SSMS with the debugger attached. It debugs whatever is installed, so install the
build first. Unlike Visual Studio extensions, there is no experimental instance: the extension
always runs in the user's real SSMS.

**Break at a known point** when attaching in time is awkward (startup code):

```csharp
#if DEBUG
System.Diagnostics.Debugger.Launch();
#endif
```

Remove it before building a release.

## Troubleshooting

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Build: "SSMS 22 assemblies were not found" | SSMS not installed, or not in the default folder | Install it, or pass `-p:SsmsIdeDir=...` |
| Build: no `.vsix` in the output | `VSSDKBuildToolsAutoSetup` or `CreateVsixContainer` missing; `Microsoft.VSSDK.BuildTools` not referenced | See [project-setup.md](project-setup.md) |
| Build: `VSTHRD010` warnings | A shell service is used without proving the code is on the UI thread | Add `ThreadHelper.ThrowIfNotOnUIThread()` to the method, or switch with `SwitchToMainThreadAsync` first; pass handlers as methods, not lambdas |
| Build: VSCT error "undefined ... symbol" | A `guid` or `id` used in the `.vsct` has no `GuidSymbol` / `IDSymbol` | Add it to `<Symbols>` |
| Installer: "This extension is not installable on any currently installed products" | `InstallationTarget` id, version range or `ProductArchitecture` does not match the installed SSMS | Use `Microsoft.VisualStudio.Ssms`, `[22.0,23.0)`, `amd64` and `arm64` |
| Installer: "already installed" | Same or lower version than the installed one | Increase the version, or uninstall first |
| Installed, but no menu item | Command table not merged, or the button has no valid parent | Checklist in [commands-and-menus.md](commands-and-menus.md) |
| Menu item appears; clicking does nothing, or "package did not load correctly" | The package threw during load | Activity log; usually a dependency that could not be loaded or an exception in `InitializeAsync` |
| `FileNotFoundException` / `FileLoadException` for a dependency at runtime | The dependency is not in the VSIX, or not on the probing path | Include it in the VSIX and add `[ProvideBindingPath]` to the package |
| `MissingMethodException`, `TypeLoadException`, `InvalidCastException` between identical-looking types | The extension shipped its own copy of an assembly that SSMS or another extension already loaded | Remove it from the VSIX; compile against the host's copy only |
| Worked, then broke after an SSMS update | An internal API changed, or a bundled assembly now conflicts | Rebuild against the updated SSMS; inspect the changed type with `inspect-ssms-type.ps1` |
| MEF exports (editor features) are ignored | No `MefComponent` asset in the manifest, or a stale MEF cache | Add the asset; close SSMS and delete the `ComponentModelCache` folder |
| Old menus or shortcuts remain after an update | Cached command table | Increase the `ProvideMenuResource` version and reinstall |
| Object Explorer items missing | The package is not auto-loaded, or the `Tree` property was not found | Check `ProvideAutoLoad`; read the extension's Output pane |
| SSMS freezes when a command runs | Slow work on the UI thread, or `.Result` / `.Wait()` on a task that needs the UI thread | Move the work off the UI thread; never block on async code, use `JoinableTaskFactory.Run` if a synchronous wait is unavoidable |
| SSMS closes or shows an unhandled exception dialog | An exception escaped from a handler, timer or `async void` method | Catch at every entry point; route fire-and-forget work through `Background.Run` |

When SSMS will not start properly because of an extension, uninstall it with `VSIXInstaller.exe
/uninstall:<id>`, or delete its folder below `Extensions` while SSMS is closed.

## Continuous integration

The VSIX compiles against assemblies from an SSMS install, and hosted CI runners do not have
SSMS. The options, in the order projects usually choose them:

1. **CI runs the unit tests of the SSMS-independent library only**; the VSIX is built on a
   developer machine and attached to the release. Simple, and enough for most small projects.
2. **A self-hosted Windows runner with SSMS installed** builds and packages on every push.
3. **Reference assemblies kept in the repository** (a `lib` folder used as `SsmsIdeDir`). It
   makes hosted builds possible, but it redistributes Microsoft's binaries; that is a licensing
   decision for the project owner to make knowingly, and the files must be refreshed when SSMS
   changes.

In a workflow, build with `dotnet build -c Release` on a `windows-latest` (or self-hosted
Windows) runner and upload `**/bin/Release/net48/*.vsix` as the artifact.

## Releasing

1. Increase `Version` in `source.extension.vsixmanifest`.
2. Build Release, check the VSIX contents (see [project-setup.md](project-setup.md)).
3. Install that exact file into SSMS and run through the features by hand.
4. Publish the `.vsix` as a release asset (for an open-source project, a GitHub release tagged
   with the version) with notes on what changed and which SSMS versions it was tested on.

Tell users in the README how to install (close SSMS, double-click the file), that updates are
manual unless the extension checks for them itself, and that third-party SSMS extensions are not
supported by Microsoft and can stop working after an SSMS update.
