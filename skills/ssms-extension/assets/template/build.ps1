<#
.SYNOPSIS
    Builds the extension and prints the path of the resulting .vsix file.
.PARAMETER Configuration
    Debug or Release. Release is what you hand to users.
.PARAMETER SsmsIdeDir
    Common7\IDE folder of the SSMS install to compile against, when it is not in the
    default location.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$SsmsIdeDir
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '__ExtensionName__.csproj'

$buildArgs = @('build', $project, '-c', $Configuration, '-nologo')
if ($SsmsIdeDir) {
    $buildArgs += "-p:SsmsIdeDir=$SsmsIdeDir"
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "The build failed with exit code $LASTEXITCODE."
}

$vsix = Join-Path $PSScriptRoot "bin\$Configuration\net48\__ExtensionName__.vsix"
if (-not (Test-Path $vsix)) {
    throw "The build succeeded but no VSIX was found at $vsix."
}

Write-Host "VSIX: $vsix"
