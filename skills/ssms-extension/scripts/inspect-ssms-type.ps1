<#
.SYNOPSIS
    Shows what a type in one of SSMS's assemblies really exposes in the installed version.
.DESCRIPTION
    SSMS's internal APIs are undocumented and change between releases. Use this before writing
    code against a type, instead of relying on memory or on another extension's source.
    Without -Type it lists the public types of the assembly that match -Filter.
.PARAMETER Assembly
    File name without extension, for example SqlWorkbench.Interfaces, SqlPackageBase,
    SQLEditors, Microsoft.SqlServer.RegSvrEnum, ConnectionDlg.
.PARAMETER Type
    Type name, with or without namespace. Wildcards are allowed: *INodeInformation
.PARAMETER Filter
    Wildcard for the type listing when -Type is omitted. Default: *
.PARAMETER NonPublic
    Also list non-public members (those are only reachable by reflection).
.PARAMETER SsmsIdeDir
    Common7\IDE folder of the SSMS install.
.EXAMPLE
    .\inspect-ssms-type.ps1 -Assembly SqlWorkbench.Interfaces -Type *INodeInformation
.EXAMPLE
    .\inspect-ssms-type.ps1 -Assembly SqlWorkbench.Interfaces -Filter *ObjectExplorer*
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Assembly,
    [string]$Type,
    [string]$Filter = '*',
    [switch]$NonPublic,
    [string]$SsmsIdeDir = (Join-Path $env:ProgramW6432 'Microsoft SQL Server Management Studio 22\Release\Common7\IDE')
)

$ErrorActionPreference = 'Stop'

# Most assemblies are in Common7\IDE; the editor ones are below Extensions\Application.
$file = Get-ChildItem -Path $SsmsIdeDir -Filter "$Assembly.dll" -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $file) {
    throw "$Assembly.dll was not found below '$SsmsIdeDir'."
}

$loaded = [Reflection.Assembly]::LoadFrom($file.FullName)
Write-Output "$($file.FullName)  (assembly version $($loaded.GetName().Version))"

try {
    $types = $loaded.GetTypes()
}
catch [Reflection.ReflectionTypeLoadException] {
    # Types whose dependencies cannot be loaded outside SSMS are skipped.
    $types = $_.Exception.Types | Where-Object { $_ }
}

if (-not $Type) {
    $types | Where-Object { $_.IsPublic -and $_.FullName -like $Filter } |
        Sort-Object FullName | ForEach-Object { $_.FullName }
    return
}

$found = @($types | Where-Object { $_.FullName -like $Type -or $_.Name -like $Type })
if (-not $found) {
    throw "No type matching '$Type' in $Assembly."
}

$flags = [Reflection.BindingFlags]'Public, Instance, Static, DeclaredOnly'
if ($NonPublic) {
    $flags = $flags -bor [Reflection.BindingFlags]::NonPublic
}

foreach ($t in $found) {
    Write-Output ''
    Write-Output "== $($t.FullName)"
    if ($t.BaseType -and $t.BaseType -ne [object]) {
        Write-Output "   base: $($t.BaseType.FullName)"
    }

    # An interface does not report the members of the interfaces it extends, so walk those too.
    $sources = @($t)
    if ($t.IsInterface) {
        $sources += $t.GetInterfaces()
    }

    foreach ($source in $sources) {
        if ($source -ne $t) {
            Write-Output "   -- from $($source.FullName)"
        }
        $source.GetMembers($flags) |
            Where-Object { -not ($_ -is [Reflection.MethodBase] -and $_.IsSpecialName) } |
            Sort-Object MemberType, Name |
            ForEach-Object { "   [$($_.MemberType)] $_" }
    }
}
