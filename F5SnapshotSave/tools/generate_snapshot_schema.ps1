# Generate names/flags from the installed SDK at build time. The mod uses ModReflect for runtime access.
param(
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Probably Stolen',
    [string]$Out = (Join-Path $PSScriptRoot '..\Data\snapshot-schema.json'),
    [string[]]$ModAssemblies = @()
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameDir 'Probably Stolen_Data\Managed'
Add-Type -Path (Join-Path $managed 'Unity.Burst.Cecil.dll')
# Assemblies of the mods that follow the compatibility spec (0.8.0, T-029): they hand over their own field table at run
# time (generate_spec_fields.ps1), so they are never put into this table, also when a caller passes them.
$specAdopters = @('VVittgenstein.GunsmithExpansion', 'GunsmithExpansion', 'Moneylending', 'KeptShotgun')
# CorpPresence was an integration up to F5 Quick Save Plus 0.7.1; 0.8.0 no longer supports it (T-029 plan section 14,
# yzz 2026-11-12), so it is left out as well, also when a caller passes it.
$removedIntegrations = @('CorpPresence')
$files = @((Join-Path $managed 'Assembly-CSharp.dll')) + $ModAssemblies
if ($ModAssemblies.Count -eq 0) {
    foreach ($id in 'F5SnapshotSave','Pen_mod_pipette') {
        $path = Join-Path $GameDir "Mods\$id\$id.dll"
        if (Test-Path -LiteralPath $path) { $files += $path }
    }
    $pipette = Join-Path (Split-Path -Parent (Split-Path -Parent $GameDir)) 'workshop\content\4348910\3829409725\Pen_mod_pipette.dll'
    if (Test-Path -LiteralPath $pipette) { $files += $pipette }
    foreach ($path in @(
        (Join-Path $GameDir 'Mods\vanilla_gun_expansion\VanillaGunExpansion.dll'),
        (Join-Path $GameDir 'Mods\VanillaGunExpansion\VanillaGunExpansion.dll'),
        (Join-Path (Split-Path -Parent (Split-Path -Parent $GameDir)) 'workshop\content\4348910\3834135415\VanillaGunExpansion.dll')
    )) {
        if (Test-Path -LiteralPath $path) { $files += $path }
    }
}
$modules = [ordered]@{}
$fields = [ordered]@{}
foreach ($file in $files) {
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path -LiteralPath $file).Path)
    try {
        $name = $module.Assembly.Name.Name
        if ($specAdopters -contains $name) {
            "Left out $name (follows the compatibility spec; ships its own field table): $file"
            continue
        }
        if ($removedIntegrations -contains $name) {
            "Left out $name (no longer supported by F5 Quick Save Plus; it can follow the compatibility spec): $file"
            continue
        }
        if ($modules.Contains($name)) { continue }
        $modules[$name] = $module.Mvid.ToString('N')
        foreach ($type in $module.GetTypes()) {
            if ($type.Name -eq '<Module>') { continue }
            $key = $name + ':' + $type.FullName.Replace('/', '+')
            $rows = @(foreach ($field in $type.Fields) {
                if ($field.IsLiteral) { continue }
                [ordered]@{ Name=$field.Name; Static=$field.IsStatic; ReadOnly=$field.IsInitOnly; ValueType=$field.FieldType.IsValueType; FieldType=$field.FieldType.FullName.Replace('/', '+') }
            })
            $fields[$key] = $rows
        }
    }
    finally { $module.Dispose() }
}
$document = [ordered]@{ Format=2; Modules=$modules; Fields=$fields }
$parent = Split-Path -Parent ([IO.Path]::GetFullPath($Out))
New-Item -ItemType Directory -Path $parent -Force | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Out), ($document | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
"Schema: $($fields.Count) types in $($modules.Count) assemblies; $Out"
