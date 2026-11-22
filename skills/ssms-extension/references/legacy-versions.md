# Other SSMS versions

The skill and its template target SSMS 22. This file covers what changes for SSMS 21 and for the
older 32-bit versions, for users who ask to support them or who maintain an older extension.

Everything here is drawn from extensions that support several versions. It has been exercised
less than the SSMS 22 path, so build and test on the actual version before promising anything,
and tell the user that support for an old version is a separate piece of work with its own
testing, not a manifest tweak.

## Contents

- [Version overview](#version-overview)
- [SSMS 21](#ssms-21)
- [SSMS 18, 19 and 20](#ssms-18-19-and-20)
- [One code base, several versions](#one-code-base-several-versions)
- [Recommendation](#recommendation)

## Version overview

| SSMS | Process | Shell | Default install folder | Extension install |
| --- | --- | --- | --- | --- |
| 22 | 64-bit (x64, ARM64) | Visual Studio 2026 (18.x) | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE` | VSIX installer |
| 21 | 64-bit | Visual Studio 2022 (17.x) | `C:\Program Files\Microsoft SQL Server Management Studio 21\Release\Common7\IDE` | VSIX installer |
| 20 | 32-bit | Visual Studio 2017 isolated shell (15.x) | `C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE` | Copy a folder into `Extensions` |
| 19 | 32-bit | same | `C:\Program Files (x86)\Microsoft SQL Server Management Studio 19\Common7\IDE` | Copy a folder into `Extensions` |
| 18 | 32-bit | same | `C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE` | Copy a folder into `Extensions` |

All of them run on .NET Framework, so `net472` or `net48` applies throughout. The package model
(`AsyncPackage`, `.vsct`, `.pkgdef`) is the same; what differs is the installer, the
architecture, the version of the VS SDK to compile against, and the versions of SSMS's own
assemblies.

## SSMS 21

SSMS 21 is close to 22: 64-bit, installed and updated through the Visual Studio Installer, and
it installs extensions from `.vsix` files.

**Manifest.** Target version range `[21.0,22.0)` with `ProductArchitecture` `amd64`. A single
manifest can declare both versions with `[21.0,23.0)`, and several extensions that use only the
VS SDK and the long-stable SSMS interfaces ship one VSIX that way.

**One VSIX or two?** The assemblies an extension compiles against have different versions in 21
and 22 (`SqlWorkbench.Interfaces`, SMO, `Microsoft.Data.SqlClient`), and there are no binding
redirects between them. A DLL compiled against 22's assemblies can therefore fail to load its
references in 21. Which applies depends on what the extension touches:

- Only VS SDK types, `EnvDTE`, and members reached by reflection or late binding: one VSIX with
  `[21.0,23.0)` can work.
- Strong references to SSMS assemblies whose versions differ: build once per version, each
  against that version's install folder, and ship two VSIX files with the same `Identity Id`.
  Each SSMS version keeps its own list of installed extensions.

To build the template for SSMS 21, compile against its folder and change the manifest's range:

```powershell
dotnet build -c Release -p:SsmsVersion=21      # uses ...\Management Studio 21\Release\Common7\IDE
```

and set `Version="[21.0,22.0)"` on the installation targets (remove the `arm64` target). For a
project that ships both, generate the second manifest at build time instead of editing by hand,
and give the two outputs different file names and output folders.

**Differences seen in practice:** internal members of `SQLEditors.dll` (results grid access)
differ between 21 and 22; the documented command ids and the editor context-menu ids are the
same. Verify with `inspect-ssms-type.ps1 -SsmsIdeDir <21's folder>`.

## SSMS 18, 19 and 20

These are 32-bit applications on the Visual Studio 2017 isolated shell. Compared with 22:

- **Architecture and manifest.** `ProductArchitecture` is `x86`, and the version range of
  `Microsoft.VisualStudio.Ssms` is `[15.0,21.0)` in manifests that cover 18 to 20 (the shell
  version, not the SSMS marketing version). A native dependency must be 32-bit.
- **No VSIX installation.** These versions do not install `.vsix` files into SSMS. Extensions
  are distributed as a zip of the build output (DLL, `.pkgdef`, `extension.vsixmanifest`), which
  the user extracts into a new folder under `<install folder>\Extensions`. That folder is under
  `Program Files (x86)` and needs administrator rights. Downloaded zip files must be unblocked
  (file Properties > Unblock) before extracting, or the assemblies are refused as coming from
  the internet.
- **VS SDK version.** Compile against an SDK the 15.x shell can satisfy
  (`Microsoft.VisualStudio.SDK` 15.0.x). Anything introduced in VS 2019 or later is unavailable:
  check each API used against that version.
- **SSMS assemblies.** Different versions per release, sometimes with different members. The SQL
  client is `System.Data.SqlClient` in the older releases and `Microsoft.Data.SqlClient` in the
  newer ones, which changes the type of every connection object.
- **Object Explorer internals** differ per version. Extensions that go beyond the public
  `IObjectExplorerService` interface have needed a separate implementation for each of 18, 19,
  20, 21 and 22.

## One code base, several versions

Patterns used by extensions that do this, from least to most effort:

1. **Shared source, one project per SSMS version.** A shared project (`.shproj`/`.projitems`) or
   linked files hold the code; each version has a small project with its own manifest, its own
   references and a conditional compilation symbol (`SSMS21`, `SSMS22`) for the few places that
   differ. Clear and easy to reason about; one build output per version.
2. **One project, a build property selects the version.** `-p:SsmsVersion=21` switches the
   reference folder, output folder, VSIX name and manifest range. Less duplication, more MSBuild.
3. **One package, version-specific assemblies chosen at runtime.** The package project references
   one small implementation assembly per SSMS version (each compiled against that version's
   interfaces, referenced with `extern alias`), reads the file version of
   `SqlWorkbench.Interfaces.dll` at startup, and instantiates the matching implementation behind
   a shared interface. One download serves all versions, at the cost of the most moving parts.
   Compiling the per-version assemblies needs each version's SSMS assemblies available at build
   time.

## Recommendation

- Start with SSMS 22 only. It is the current version, and its updates are delivered through the
  installer, so users move to it quickly.
- Add SSMS 21 when users ask, as a second build, after checking which case in
  [One VSIX or two?](#ssms-21) applies.
- Support 18 to 20 only for a concrete need. It means a different distribution mechanism, an
  older SDK, 32-bit constraints and a machine with those versions installed for testing. Many
  maintained extensions have dropped them and point users of old SSMS versions to an older
  release of the extension instead; that is a reasonable policy to suggest.
