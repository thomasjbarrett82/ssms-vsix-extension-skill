<#
.SYNOPSIS
    Installs the built extension into SSMS, or removes it, using the VSIX installer that
    ships with SSMS.
.DESCRIPTION
    SSMS must be closed: it reads its extensions at startup, and the installer waits for
    running instances to exit. The installer refuses a version that is the same as or lower
    than the installed one, so increase Version in source.extension.vsixmanifest for every
    build you want to install over an existing one (or use -Reinstall).
.PARAMETER Configuration
    Which build output to install.
.PARAMETER Uninstall
    Remove the extension instead of installing it.
.PARAMETER Reinstall
    Uninstall first, then install. Lets you install the same version again.
.PARAMETER SsmsIdeDir
    Common7\IDE folder of the SSMS install, when it is not in the default location.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Uninstall,
    [switch]$Reinstall,
    [string]$SsmsIdeDir = (Join-Path $env:ProgramW6432 'Microsoft SQL Server Management Studio 22\Release\Common7\IDE')
)

$ErrorActionPreference = 'Stop'

$installer = Join-Path $SsmsIdeDir 'VSIXInstaller.exe'
if (-not (Test-Path $installer)) {
    throw "VSIXInstaller.exe was not found in '$SsmsIdeDir'. Pass -SsmsIdeDir with the Common7\IDE folder of your SSMS install."
}

if (Get-Process -Name 'SSMS' -ErrorAction SilentlyContinue) {
    throw 'SSMS is running. Close every SSMS window and run this script again.'
}

[xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'source.extension.vsixmanifest')
$extensionId = $manifest.PackageManifest.Metadata.Identity.Id

function Invoke-Installer([string[]]$Arguments, [string]$Action) {
    $process = Start-Process -FilePath $installer -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        # The reason is in the newest dd_VSIXInstaller_*.log in %TEMP%.
        throw "$Action failed with exit code $($process.ExitCode). See the newest dd_VSIXInstaller_*.log in $env:TEMP."
    }
}

if ($Uninstall -or $Reinstall) {
    Write-Host "Uninstalling $extensionId"
    try {
        Invoke-Installer @('/quiet', "/uninstall:$extensionId") 'Uninstall'
    }
    catch {
        if ($Uninstall) { throw }
        Write-Host 'Nothing to uninstall.'
    }
    if ($Uninstall) { return }
}

$vsix = Join-Path $PSScriptRoot "bin\$Configuration\net48\__ExtensionName__.vsix"
if (-not (Test-Path $vsix)) {
    throw "No VSIX at $vsix. Run .\build.ps1 -Configuration $Configuration first."
}

Write-Host "Installing $vsix"
Invoke-Installer @('/quiet', "`"$vsix`"") 'Install'
Write-Host 'Installed. Start SSMS and look for the extension under the Tools menu.'
