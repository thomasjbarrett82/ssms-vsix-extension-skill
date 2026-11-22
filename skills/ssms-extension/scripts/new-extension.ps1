<#
.SYNOPSIS
    Creates a new SSMS 22 extension project from the skill's template.
.DESCRIPTION
    Copies assets\template into <OutputDir>\<Name>, generates fresh GUIDs, fills in the
    names, and removes the code of the features that were not asked for.
    Works in Windows PowerShell 5.1 and PowerShell 7.
.PARAMETER Name
    Project, assembly and namespace name. A C# identifier, for example MySsmsTool.
.PARAMETER DisplayName
    Name shown to users (menus, Extensions list). Defaults to Name.
.PARAMETER Description
    One sentence shown in the Extensions list.
.PARAMETER Publisher
    Author or company shown in the Extensions list.
.PARAMETER OutputDir
    Folder in which the project folder is created. Defaults to the current folder.
.PARAMETER Features
    Optional parts to include. Any of: ObjectExplorer, QueryEditor, ToolWindow, Options.
    Defaults to all of them. Pass -Features None for only the package and an About command.
    Accepts a list (-Features ObjectExplorer, Options) or one comma-separated string
    (-Features "ObjectExplorer,Options"); use the string form with "powershell -File".
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File new-extension.ps1 -Name IndexAdvisor -Features "ObjectExplorer,Options"
.EXAMPLE
    .\new-extension.ps1 -Name IndexAdvisor -DisplayName 'Index Advisor' -Publisher 'Contoso' `
        -Description 'Suggests missing indexes.' -Features ObjectExplorer, Options
#>
# Positional binding is off so a stray argument fails instead of landing in another parameter.
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
    [string]$Name,

    [string]$DisplayName,

    [string]$Description = 'An extension for SQL Server Management Studio.',

    [string]$Publisher = $env:USERNAME,

    [string]$OutputDir = (Get-Location).Path,

    [string[]]$Features = @('ObjectExplorer', 'QueryEditor', 'ToolWindow', 'Options')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$allFeatures = @('ObjectExplorer', 'QueryEditor', 'ToolWindow', 'Options')

# Accept both a list and a comma-separated string, in any letter case.
$requested = @($Features | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Features = @()
foreach ($item in $requested) {
    if ($item -eq 'None') { continue }
    $known = $allFeatures | Where-Object { $_ -eq $item }
    if (-not $known) {
        throw "Unknown feature '$item'. Valid values: $($allFeatures -join ', '), None."
    }
    if ($Features -notcontains $known) { $Features += $known }
}
if (($requested -contains 'None') -and $Features.Count) {
    throw "'None' cannot be combined with other features."
}
if (-not $DisplayName) { $DisplayName = $Name }

$template = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\template'
if (-not (Test-Path $template)) {
    throw "Template folder not found: $template"
}

$target = Join-Path $OutputDir $Name
if (Test-Path $target) {
    throw "The folder already exists: $target"
}

function New-GuidText { [guid]::NewGuid().ToString('D').ToLowerInvariant() }

# Raw values. They are escaped per file type below.
$textTokens = @{
    '__DisplayName__' = $DisplayName
    '__Description__' = $Description
    '__Publisher__'   = $Publisher
}
$plainTokens = @{
    '__ExtensionName__'  = $Name
    '__PackageGuid__'    = New-GuidText
    '__CommandSetGuid__' = New-GuidText
    '__ToolWindowGuid__' = New-GuidText
    '__OptionsPageGuid__' = New-GuidText
    '__OutputPaneGuid__' = New-GuidText
    '__VsixIdGuid__'     = New-GuidText
}

function ConvertTo-Escaped([string]$Value, [string]$Extension) {
    switch ($Extension) {
        '.cs' { return $Value.Replace('\', '\\').Replace('"', '\"') }
        { $_ -in '.vsct', '.vsixmanifest', '.xaml', '.csproj' } {
            return [System.Security.SecurityElement]::Escape($Value)
        }
        default { return $Value }
    }
}

# Keeps or drops the lines between "#if Feature" / "#else" / "#endif" markers. The markers are
# ordinary comments ("// #if X" in C#, "<!-- #if X -->" in XML) and are always removed.
function Resolve-FeatureBlocks([string[]]$Lines, [string]$File) {
    $marker = '^\s*(?://|<!--)\s*#(if|else|endif)\b\s*(\w*)\s*(?:-->)?\s*$'
    $result = New-Object System.Collections.Generic.List[string]
    $inBlock = $false
    $keep = $true
    foreach ($line in $Lines) {
        if ($line -match $marker) {
            switch ($Matches[1]) {
                'if' {
                    if ($inBlock) { throw "Nested #if in $File" }
                    if ($allFeatures -notcontains $Matches[2]) { throw "Unknown feature '$($Matches[2])' in $File" }
                    $inBlock = $true
                    $keep = $Features -contains $Matches[2]
                }
                'else' {
                    if (-not $inBlock) { throw "#else without #if in $File" }
                    $keep = -not $keep
                }
                'endif' {
                    if (-not $inBlock) { throw "#endif without #if in $File" }
                    $inBlock = $false
                    $keep = $true
                }
            }
            continue
        }
        if ($keep) { $result.Add($line) }
    }
    if ($inBlock) { throw "Unclosed #if in $File" }
    return , $result.ToArray()
}

Copy-Item -Path $template -Destination $target -Recurse

# Folders that belong to one feature only.
foreach ($feature in $allFeatures) {
    if ($Features -notcontains $feature) {
        $folder = Join-Path $target $feature
        if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$textExtensions = '.cs', '.csproj', '.vsct', '.vsixmanifest', '.xaml', '.ps1', '.md', '.txt'

foreach ($file in Get-ChildItem -Path $target -Recurse -File) {
    if ($textExtensions -notcontains $file.Extension) { continue }

    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    $lines = Resolve-FeatureBlocks $lines $file.Name
    $content = ($lines -join "`r`n") + "`r`n"

    foreach ($token in $textTokens.Keys) {
        $content = $content.Replace($token, (ConvertTo-Escaped $textTokens[$token] $file.Extension))
    }
    foreach ($token in $plainTokens.Keys) {
        $content = $content.Replace($token, $plainTokens[$token])
    }

    [System.IO.File]::WriteAllText($file.FullName, $content, $utf8NoBom)

    $newName = $file.Name.Replace('__ExtensionName__', $Name)
    if ($newName -eq 'gitignore.txt') { $newName = '.gitignore' }
    if ($newName -ne $file.Name) { Rename-Item -Path $file.FullName -NewName $newName }
}

$leftover = Get-ChildItem -Path $target -Recurse -File |
    Select-String -Pattern '__[A-Za-z]+__' -List |
    Select-Object -ExpandProperty Path
if ($leftover) {
    throw "Unreplaced template tokens remain in: $($leftover -join ', ')"
}

$featureText = if ($Features.Count) { $Features -join ', ' } else { 'none' }
Write-Host "Created $target"
Write-Host "Features: $featureText"
Write-Host ''
Write-Host 'Next steps:'
Write-Host "  cd `"$target`""
Write-Host '  .\build.ps1        # produces the .vsix (needs SSMS 22 installed)'
Write-Host '  .\install.ps1      # installs it into SSMS (close SSMS first)'
