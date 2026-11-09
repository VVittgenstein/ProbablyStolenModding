<#
.SYNOPSIS
    Builds the seven mods into a separate output directory.
.PARAMETER GameDir
    Probably Stolen installation containing Probably Stolen_Data\Managed.
.PARAMETER OutDir
    Empty output directory. Defaults to a new directory under the system temporary directory.
.PARAMETER CorpPresenceDll
    Optional CorpPresence.dll to include in the F5 Quick Save Plus snapshot schema.
    Use the exact version you intend to run with the generated mods.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) ('ProbablyStolenMods-' + [Guid]::NewGuid().ToString('N'))),
    [string]$CorpPresenceDll
)

$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$managed = Join-Path $GameDir 'Probably Stolen_Data\Managed'
foreach ($file in 'Assembly-CSharp.dll', 'Unity.Burst.Cecil.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $managed $file) -PathType Leaf)) {
        throw "Missing $file under '$managed'."
    }
}
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) {
    if (-not (Test-Path -LiteralPath $OutDir -PathType Container) -or
        @(Get-ChildItem -LiteralPath $OutDir -Force).Count -ne 0) {
        throw "Output directory must be empty: $OutDir"
    }
}
$integrations = @()
if ($CorpPresenceDll) {
    $CorpPresenceDll = (Resolve-Path -LiteralPath $CorpPresenceDll -ErrorAction Stop).Path
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# Build integration DLLs before generating the Plus schema. No installed copy of these mods is required.
$mods = @('F5ForceSave', 'MentorNoteAutoFile', 'KeptShotgun', 'Moneylending',
          'GunsmithExpansion', 'GunsmithExpansion2077')
foreach ($mod in $mods) {
    & (Join-Path $PSScriptRoot "$mod\build.ps1") -GameDir $GameDir -OutDir (Join-Path $OutDir $mod)
}
foreach ($mod in 'GunsmithExpansion', 'Moneylending', 'KeptShotgun') {
    $integrations += Join-Path $OutDir "$mod\$mod.dll"
}
if ($CorpPresenceDll) { $integrations += $CorpPresenceDll }
& (Join-Path $PSScriptRoot 'F5SnapshotSave\build.ps1') -GameDir $GameDir `
    -OutDir (Join-Path $OutDir 'F5SnapshotSave') -IntegrationAssemblies $integrations

Write-Host "All seven mods built: $OutDir"
Write-Host 'To install, close the game and copy the required mod folders into its Mods directory.'
