# Project setup

How the project file and the manifest of an SSMS 22 extension are put together, and why. The
template in `assets/template` implements everything described here.

## Contents

- [Project file](#project-file)
- [Package versions](#package-versions)
- [Referencing SSMS assemblies](#referencing-ssms-assemblies)
- [The manifest](#the-manifest)
- [Version handling](#version-handling)
- [What belongs in the VSIX](#what-belongs-in-the-vsix)
- [Third-party dependencies](#third-party-dependencies)
- [Splitting logic from the shell](#splitting-logic-from-the-shell)
- [Old-style project files](#old-style-project-files)

## Project file

An SDK-style project (`<Project Sdk="Microsoft.NET.Sdk">`) is the simplest form that works. It
builds with `dotnet build`, without Visual Studio and without its extension development workload.

| Property | Value | Why |
| --- | --- | --- |
| `TargetFramework` | `net48` | SSMS 22 is a .NET Framework 4.8 process. `net472` also loads. |
| `VSSDKBuildToolsAutoSetup` | `true` | Makes the `Microsoft.VSSDK.BuildTools` package import the VSIX packaging targets into an SDK-style project. Without it no `.vsix` is produced. |
| `GeneratePkgDefFile` | `true` | Turns the package's registration attributes into the `.pkgdef` file SSMS reads at startup. |
| `UseCodebase` | `true` | Registers the assembly by path, which a non-GAC extension needs. |
| `CreateVsixContainer` | `true` | Produces the `.vsix`. |
| `IncludeAssemblyInVSIXContainer` | `true` | Puts the extension's DLL in the `.vsix`. |
| `DeployExtension` | `false` | Deployment after build targets Visual Studio's experimental instance, not SSMS. |
| `GenerateDependencyFile` | `false` | Keeps a useless `.deps.json` out of the output. |
| `UseWPF` / `UseWindowsForms` | as needed | WPF for tool windows and dialogs; WinForms for Object Explorer menu items. |

Two items complete it:

```xml
<None Include="source.extension.vsixmanifest" SubType="Designer" />
<VSCTCompile Include="MyExtensionPackage.vsct" ResourceName="Menus.ctmenu" />
```

`ResourceName="Menus.ctmenu"` must match the string in `[ProvideMenuResource("Menus.ctmenu", 1)]`.

An SDK-style project does not reference these .NET Framework assemblies by default, and the VS SDK
needs them: `System.Design` and `System.ComponentModel.Composition`.

## Package versions

```xml
<PackageReference Include="Microsoft.VisualStudio.SDK" Version="17.14.40265" ExcludeAssets="runtime" />
<PackageReference Include="Microsoft.VSSDK.BuildTools" Version="18.5.40034" PrivateAssets="all" />
<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
```

- `Microsoft.VisualStudio.SDK` 17.14 is what current SSMS 22 extensions compile against. SSMS 22
  carries the 18.x assemblies and redirects the 17.x references to them at runtime.
  `ExcludeAssets="runtime"` is essential: without it the build copies the Visual Studio
  assemblies into the output and the VSIX.
- `Microsoft.VSSDK.BuildTools` supplies the packaging targets and the VSCT compiler. Before
  changing the version, check that the new one is a listed version on nuget.org.
- `Microsoft.NETFramework.ReferenceAssemblies` lets the project build on a machine without the
  .NET Framework 4.8 targeting pack.

When these versions are years old, check nuget.org for current ones, change one package at a time,
and rebuild after each.

## Referencing SSMS assemblies

SSMS's own assemblies are not on NuGet. Reference them from the local install, compile-time only:

```xml
<PropertyGroup>
  <SsmsIdeDir Condition="'$(SsmsIdeDir)' == ''">$(ProgramW6432)\Microsoft SQL Server Management Studio 22\Release\Common7\IDE</SsmsIdeDir>
</PropertyGroup>
<ItemGroup>
  <SsmsReference Include="SqlWorkbench.Interfaces" />
  <SsmsReference Include="SqlPackageBase" />
  <Reference Include="@(SsmsReference)">
    <HintPath>$(SsmsIdeDir)\%(Identity).dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

`<Private>false</Private>` keeps them out of the output, so the host's copies are used at runtime.

| Assembly | Location under `Common7\IDE` | Gives access to |
| --- | --- | --- |
| `SqlWorkbench.Interfaces` | root | `IObjectExplorerService`, `INodeInformation`, `IScriptFactory`, `ScriptType` |
| `SqlPackageBase` | root | `ServiceCache` (static access to SSMS services) |
| `Microsoft.SqlServer.RegSvrEnum` | root | `UIConnectionInfo` |
| `ConnectionDlg` | root | `UIConnectionInfoUtil` (database name, core connection info) |
| `Microsoft.SqlServer.ConnectionInfo` | root | `SqlConnectionInfo`, `ServerConnection` |
| `Microsoft.SqlServer.Management.Sdk.Sfc` | root | `Urn` (parsing Object Explorer node paths) |
| `Microsoft.SqlServer.Smo` | root | SMO object model (scripting, metadata) |
| `Microsoft.Data.SqlClient` | root | The SQL client SSMS itself uses |
| `Microsoft.SqlServer.GridControl` | root | The results grid control |
| `SQLEditors` | `Extensions\Application` | Query editor internals (`SqlScriptEditorControl`, results panes) |
| `Microsoft.SqlServer.TransactSql.ScriptDom` | `Extensions\Application` | T-SQL parser |

Reference only what the code uses. For an assembly outside the root folder, give its own
`HintPath`.

Consequences to plan for:

- **The build needs SSMS installed.** A hosted CI runner does not have it. See the CI section of
  [build-install-debug.md](build-install-debug.md) for the options.
- **These are Microsoft's binaries.** Do not commit them to a public repository or put them in the
  VSIX. Some extensions keep copies in a `lib` folder to make CI possible; that is a redistribution
  decision for the project owner, not something to do silently.
- **Warning MSB3277** (conflicting versions of Visual Studio assemblies) appears because SSMS's
  assemblies reference 18.x while the SDK package is 17.14. SSMS unifies them at runtime. Downgrade
  that one warning to a message: `<MSBuildWarningsAsMessages>$(MSBuildWarningsAsMessages);MSB3277</MSBuildWarningsAsMessages>`.

## The manifest

`source.extension.vsixmanifest`, annotated:

```xml
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"
                 xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity Id="MyExtension.6f0c..." Version="1.0.0" Language="en-US" Publisher="Contoso" />
    <DisplayName>My Extension</DisplayName>
    <Description xml:space="preserve">What it does, in a sentence.</Description>
    <!-- Optional; each file must also be in the project with IncludeInVSIX=true:
    <License>LICENSE.txt</License>
    <Icon>Resources\Icon.png</Icon>
    <MoreInfo>https://github.com/owner/repo</MoreInfo> -->
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,23.0)">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
    <InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,23.0)">
      <ProductArchitecture>arm64</ProductArchitecture>
    </InstallationTarget>
  </Installation>
  <Dependencies>
    <Dependency Id="Microsoft.Framework.NDP" DisplayName="Microsoft .NET Framework"
                d:Source="Manual" Version="[4.8,)" />
  </Dependencies>
  <Prerequisites>
    <Prerequisite Id="Microsoft.VisualStudio.Component.CoreEditor" Version="[17.0,)"
                  DisplayName="Visual Studio core editor" />
  </Prerequisites>
  <Assets>
    <Asset Type="Microsoft.VisualStudio.VsPackage" d:Source="Project"
           d:ProjectName="%CurrentProject%" Path="|%CurrentProject%;PkgdefProjectOutputGroup|" />
  </Assets>
</PackageManifest>
```

- **`Identity Id`** identifies the extension to the installer for upgrades and uninstalls. Choose
  it once (name plus a GUID is the convention) and never change it.
- **`InstallationTarget Id`** must be `Microsoft.VisualStudio.Ssms`. The version range is the SSMS
  version: `[22.0,23.0)` means every 22.x. An open range `[22.0,)` also installs on future major
  versions, where the internal APIs may have changed; prefer the closed range and widen it after
  testing. `[22.6,)`-style lower bounds are for extensions that need an API added in a point
  release.
- **`ProductArchitecture`** is required for 64-bit hosts. One target each for `amd64` and `arm64`
  covers both SSMS 22 builds; the managed `AnyCPU` assembly is the same for both. Drop `arm64` if
  the extension ships native x64 binaries.
- **`Prerequisite`** on the core editor component is required by the VSIX format (every v3 VSIX
  needs at least one prerequisite); the range `[17.0,)` is satisfied by SSMS 22.
- **Asset `VsPackage`** points at the generated `.pkgdef`. Add
  `<Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project" d:ProjectName="%CurrentProject%" Path="|%CurrentProject%|" />`
  only when the extension exports MEF parts (editor classifiers, completion sources, text view
  listeners). Without that asset MEF exports are silently ignored.
- **`<Installation AllUsers="true">`** installs for all users under the SSMS folder and needs
  elevation. The default is per user and needs none; keep the default unless there is a reason.

## Version handling

The installer compares the manifest's `Identity Version` with the installed one and refuses equal
or lower versions. Two more places usually carry the version: the assembly attributes and
`[InstalledProductRegistration]` (the text in Help > About). When they are edited by hand they
drift apart.

The template stores the version only in the manifest. The project file reads it at build time:

```xml
<Version>$([System.Text.RegularExpressions.Regex]::Match($([System.IO.File]::ReadAllText('$(MSBuildProjectDirectory)\source.extension.vsixmanifest')), '&lt;Identity\b[^&gt;]*\bVersion="([^"]+)"').Groups[1].Value)</Version>
```

and a small target writes `BuildInfo.Version` as a constant, usable in attributes and at runtime.

## What belongs in the VSIX

After every change to references or packages, list the package (a `.vsix` is a zip file):

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::OpenRead((Resolve-Path 'bin\Release\net48\MyExtension.vsix')).Entries.FullName
```

Expected: `extension.vsixmanifest`, `<Name>.dll`, `<Name>.pkgdef`, `<Name>.pdb`,
`[Content_Types].xml`, `manifest.json`, `catalog.json`, plus the extension's own dependencies and
content files. Anything beginning with `Microsoft.VisualStudio.`, `Microsoft.SqlServer.`,
`Microsoft.Data.SqlClient`, `EnvDTE`, `Newtonsoft.Json`, `MessagePack` or `System.` (for the
`System.*` packages SSMS ships, such as `System.Memory` or `System.Text.Json`) is a host assembly
that leaked in: find the reference that brought it and add `ExcludeAssets="runtime"` or
`<Private>false</Private>`.

Content files (license, icon, templates) need both of these to be packaged:

```xml
<Content Include="Resources\Icon.png">
  <IncludeInVSIX>true</IncludeInVSIX>
</Content>
```

## Third-party dependencies

Everything loads into one process shared with SSMS and every other extension, with one copy of
each assembly identity. Therefore:

- Before adding a NuGet package, check whether SSMS already ships that assembly (search the install
  folder). If it does, compile against the package but exclude its runtime assets, and accept the
  version SSMS has.
- For a dependency SSMS does not ship, include it in the VSIX and add `[ProvideBindingPath]` to the
  package class, so the extension's folder is on the assembly probing path. Without it, dependencies
  can fail to resolve, or resolve to another extension's copy.
- Prefer few and small dependencies. Each one is a possible conflict with another extension that
  ships a different version of the same assembly.
- For heavy or conflicting work (modern .NET libraries, large UI toolkits), run a separate process
  and talk to it over a pipe, standard input/output or temporary files. The extension stays a thin
  launcher.

## Splitting logic from the shell

The VSIX project cannot be unit tested without SSMS. Projects that stay maintainable put parsing,
generation and other logic in a `netstandard2.0` class library with no Visual Studio or SSMS
references, tested by an ordinary test project on modern .NET, and keep the VSIX project to wiring:
commands, reading the editor, showing results. `netstandard2.0` is the highest target that both
net48 and modern .NET can consume.

## Old-style project files

Many existing extensions use the old project format (`ToolsVersion`, `ProjectTypeGuids`
`{82b43b9b-a64c-4715-b499-d71e9ca2bd60}`, explicit `<Compile Include>` items and an import of
`$(VSToolsPath)\VSSDK\Microsoft.VsSDK.targets`). It works and needs MSBuild from Visual Studio
with the extension development workload instead of `dotnet build`. Do not convert a working
project as a side effect of another task. When maintaining one, the same rules apply: `net472` or
`net48`, `ExcludeAssets="runtime"` on the SDK, `<Private>False</Private>` on SSMS references,
`DeployExtension` false, and every new source file needs its own `<Compile Include>`.
