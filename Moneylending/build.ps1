<#
.SYNOPSIS
    Compiles Moneylending.dll against the installed game's assemblies and installs the mod
    (manifest.xml, DLL and Textures\) into <GameDir>\Mods\Moneylending.

.DESCRIPTION
    Uses the C# compiler (csc.dll) of the newest .NET SDK found through `dotnet --list-sdks`.
    No NuGet packages are restored: the build references mscorlib, Harmony and the game code
    from <GameDir>\Probably Stolen_Data\Managed. Close the game before installing over a copy
    the game has loaded.

.PARAMETER GameDir
    Game installation directory.

.PARAMETER OutDir
    Folder that receives manifest.xml, the DLL and Textures\. Defaults to <GameDir>\Mods\Moneylending.
#>
param(
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Probably Stolen',
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$modName = 'Moneylending'

$managed = Join-Path $GameDir 'Probably Stolen_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) {
    throw "Assembly-CSharp.dll not found under '$managed'. Pass -GameDir."
}
if (-not $OutDir) { $OutDir = Join-Path $GameDir "Mods\$modName" }

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$sdkLine = & $dotnet --list-sdks | Select-Object -Last 1
if ($sdkLine -notmatch '^(\S+) \[(.+)\]$') { throw "Unexpected 'dotnet --list-sdks' output: $sdkLine" }
$csc = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
if (-not (Test-Path -LiteralPath $csc)) { throw "csc.dll not found at '$csc'." }

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("$modName-build-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $dll = Join-Path $staging "$modName.dll"
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($opt in '-nologo', '-nostdlib+', '-target:library', '-langversion:7.3',
                     '-optimize+', '-deterministic+', '-debug-') { $lines.Add($opt) }
    $lines.Add("-out:`"$dll`"")
    Get-ChildItem -LiteralPath $managed -Filter '*.dll' | Sort-Object Name | ForEach-Object { $lines.Add("-r:`"$($_.FullName)`"") }
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $lines.Add("`"$($_.FullName)`"") }
    $rsp = Join-Path $staging 'csc.rsp'
    [System.IO.File]::WriteAllLines($rsp, $lines, (New-Object System.Text.UTF8Encoding($false)))

    & $dotnet $csc -noconfig "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "csc exited with code $LASTEXITCODE." }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $OutDir "$modName.dll") -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.xml') -Destination (Join-Path $OutDir 'manifest.xml') -Force
    # Textures\Items\*.png: item pictures the game's ModLoader loads into this mod's sprites (the neon sign).
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Textures') -Destination $OutDir -Recurse -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutDir "$modName.dll")).Hash
    Write-Host "Installed $modName to $OutDir"
    Write-Host "SHA256 $modName.dll: $hash"
}
finally {
    if (Test-Path -LiteralPath $staging) {
        $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (!$resolvedStaging.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path -Leaf $resolvedStaging) -notmatch ('^' + [regex]::Escape($modName) + '-build-[0-9a-f]{32}$') -or
            ((Get-Item -LiteralPath $resolvedStaging).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected build cleanup path' }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
