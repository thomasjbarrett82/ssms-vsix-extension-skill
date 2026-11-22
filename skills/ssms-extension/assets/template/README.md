# __DisplayName__

__Description__

An extension for SQL Server Management Studio (SSMS) 22.

## Requirements

- Windows with SSMS 22 installed (the build compiles against its assemblies)
- .NET SDK 8 or later

## Build

```powershell
.\build.ps1
```

The package is written to `bin\Release\net48\__ExtensionName__.vsix`.

## Install

Close SSMS, then either double-click the `.vsix` file or run:

```powershell
.\install.ps1              # install the Release build
.\install.ps1 -Reinstall   # replace an installed build of the same version
.\install.ps1 -Uninstall   # remove the extension
```

Start SSMS. The extension's commands are under **Tools > __DisplayName__**.

## Releasing a new version

Increase `Version` on the `Identity` element in `source.extension.vsixmanifest`. It is the only
place the version is stored, and the installer will not replace an installed copy with the same
or a lower version.

## Troubleshooting

- Messages from the extension appear in **View > Output**, in the **__DisplayName__** pane.
- Start SSMS with `SSMS.exe /log` to have load failures written to
  `%AppData%\Microsoft\SSMS\22.0_<id>\ActivityLog.xml`.

Third-party SSMS extensions are not officially supported by Microsoft, and SSMS updates can
change the internal APIs this extension uses.
