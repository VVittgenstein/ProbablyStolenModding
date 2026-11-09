# Generate names/flags from the installed SDK at build time. The mod uses ModReflect for runtime access.
param(
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Probably Stolen',
    [string]$Out = (Join-Path $PSScriptRoot '..\Data\snapshot-schema.json'),
    [string[]]$ModAssemblies = @()
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameDir 'Probably Stolen_Data\Managed'
Add-Type -Path (Join-Path $managed 'Unity.Burst.Cecil.dll')
$files = @((Join-Path $managed 'Assembly-CSharp.dll')) + $ModAssemblies
if ($ModAssemblies.Count -eq 0) {
    foreach ($id in 'F5SnapshotSave','GunsmithExpansion','Moneylending','KeptShotgun') {
        $path = Join-Path $GameDir "Mods\$id\$id.dll"
        if (Test-Path -LiteralPath $path) { $files += $path }
    }
    $corp = Join-Path (Split-Path -Parent (Split-Path -Parent $GameDir)) 'workshop\content\4348910\3828913695\CorpPresence.dll'
    if (Test-Path -LiteralPath $corp) { $files += $corp }
}
$modules = [ordered]@{}
$fields = [ordered]@{}
foreach ($file in $files) {
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path -LiteralPath $file).Path)
    try {
        $name = $module.Assembly.Name.Name
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
