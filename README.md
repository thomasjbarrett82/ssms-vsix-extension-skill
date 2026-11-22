# SSMS extension skill

An [Agent Skill](https://agentskills.io) that teaches an AI coding agent how to build extensions
for **SQL Server Management Studio 22**: packaged as VSIX files, with menu commands, Object
Explorer context-menu items, query editor commands, tool windows and options pages.

SSMS extensibility is thinly documented. An SSMS extension is a Visual Studio package, but the
useful parts (Object Explorer, query windows, connections) go through SSMS's internal assemblies,
and the details that decide whether an extension installs and loads are easy to get wrong. This
skill collects the patterns that working open-source SSMS extensions have in common, and adds a
project template that builds on the first try.

## What is in it

```text
skills/ssms-extension/
├── SKILL.md                      Workflow and the rules that must not be broken
├── references/
│   ├── project-setup.md          Project file, manifest, SSMS assembly references
│   ├── commands-and-menus.md     .vsct command tables, menu locations, shortcuts
│   ├── object-explorer.md        Context-menu items, reading the selected node
│   ├── query-editor.md           Connections, script text, new windows, execution
│   ├── tool-windows-and-options.md
│   ├── build-install-debug.md    Install, logs, debugging, troubleshooting table, CI
│   └── legacy-versions.md        SSMS 21 and the 32-bit versions 18-20
├── scripts/
│   ├── new-extension.ps1         Creates a project from the template
│   └── inspect-ssms-type.ps1     Shows what an SSMS internal type really exposes
└── assets/template/              The project template
```

The template is an SDK-style `net48` project that builds with `dotnet build` (Visual Studio is
not required) and produces a `.vsix` containing only the extension's own assembly. Optional
parts, chosen when scaffolding:

| Feature | What you get |
| --- | --- |
| (always) | Package, **Tools** submenu, About command, Output-pane logging, query window helper |
| `ObjectExplorer` | A submenu on the right-click menu of Object Explorer nodes |
| `QueryEditor` | A command in the query editor's right-click menu |
| `ToolWindow` | A dockable, theme-aware WPF tool window |
| `Options` | A page under **Tools > Options** |

## Requirements

- Windows
- SQL Server Management Studio 22 (the build compiles against its assemblies)
- .NET SDK 8 or later
- An agent that supports Agent Skills, such as Claude Code

## Install the skill

Copy the `skills/ssms-extension` folder into your agent's skills directory. For Claude Code:

```powershell
git clone https://github.com/<owner>/<repo>.git
# for every project (personal skill):
Copy-Item -Recurse .\<repo>\skills\ssms-extension "$env:USERPROFILE\.claude\skills\"
# or for one project only:
Copy-Item -Recurse .\<repo>\skills\ssms-extension .\.claude\skills\
```

Then ask for what you want, for example:

> Create an SSMS extension that adds a "Show row counts" item to the right-click menu of
> database nodes in Object Explorer.

## Use the template without an agent

```powershell
.\skills\ssms-extension\scripts\new-extension.ps1 -Name RowCounts -DisplayName "Row Counts" `
    -Publisher "Your Name" -Description "Row counts for every table." `
    -OutputDir C:\src -Features "ObjectExplorer,Options"

cd C:\src\RowCounts
.\build.ps1        # -> bin\Release\net48\RowCounts.vsix
.\install.ps1      # close SSMS first
```

## Status and limits

- Every combination of template features is checked to compile without warnings against
  SSMS 22. Compiling is not the same as working: test your extension inside SSMS.
- Microsoft does not support third-party SSMS extensions. The internal APIs used here are the
  ones existing extensions rely on, and an SSMS update can change them.
- SSMS 22 is the target. Notes for SSMS 21 and 18-20 are included but less exercised.

## Acknowledgements

The patterns were worked out by studying these open-source SSMS extensions. No code was copied
from them; if you want to see the techniques at full scale, they are the places to look.

- [AxialSqlTools](https://github.com/Axial-SQL/AxialSqlTools)
- [DBA Dash](https://github.com/trimble-oss/dba-dash)
- [MSSQLTOOL](https://github.com/PuLuShen/MSSQLTOOL)
- [Performance Studio](https://github.com/erikdarlingdata/PerformanceStudio)
- [SQL 4 CDS](https://github.com/MarkMpn/Sql4Cds)
- [SQL Server Compact & SQLite Toolbox](https://github.com/ErikEJ/SqlCeToolbox)
- [SQL Database Project Power Tools](https://github.com/ErikEJ/SqlProjectPowerTools)
- [SQLExtended](https://github.com/JamTheRadar/SQLExtended)
- [SqlServer.Rules](https://github.com/ErikEJ/SqlServer.Rules)
- [SSMS EnvTabs](https://github.com/Blake-goofy/SSMS-EnvTabs)
- [SSMS Executor](https://github.com/tkwj/ssms-executor)
- [SSMS Helper](https://github.com/emretor/ssms-helper)
- [SSMS Object Explorer Menu](https://github.com/brink-daniel/ssms-object-explorer-menu)
- [SSMS Schema Folders](https://github.com/nicholas-ross/SSMS-Schema-Folders)
- [Statistics Parser](https://github.com/BrentOzarULTD/StatisticsParserExtension)

## Contributing

Corrections are welcome, especially where an SSMS update has changed an API described here.
When changing the template, scaffold and build it with all features and with `-Features None`
before opening a pull request; both must build with no warnings.

## License

[MIT](LICENSE)
