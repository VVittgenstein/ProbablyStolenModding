# F5 Quick Save Plus compatibility spec v1: writes the field table of one mod assembly.
# Run it after every build of a mod that follows the spec, on the DLL that is shipped, and ship the output
# (Data/f5-snapshot-fields.json) next to the DLL. F5 Quick Save Plus accepts the table only when the module
# version id recorded in it equals the one of the DLL the game loaded, so a table from another build is ignored.
# Only names and flags are read (Mono.Cecil from the game's Managed folder); nothing is loaded or run.
param(
    [Parameter(Mandatory = $true)][string]$Dll,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Probably Stolen'
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameDir 'Probably Stolen_Data\Managed'
$cecil = Join-Path $managed 'Unity.Burst.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecil)) { throw "Unity.Burst.Cecil.dll not found under '$managed'. Pass -GameDir." }
Add-Type -Path $cecil
$path = (Resolve-Path -LiteralPath $Dll).Path
$module = [Mono.Cecil.ModuleDefinition]::ReadModule($path)
try {
    $name = $module.Assembly.Name.Name
    $modules = [ordered]@{ $name = $module.Mvid.ToString('N') }
    $fields = [ordered]@{}
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
$document = [ordered]@{ Format=2; Modules=$modules; Fields=$fields }
$full = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force | Out-Null
[IO.File]::WriteAllText($full, ($document | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
"Spec field table: $($fields.Count) types of $name (MVID $($modules[$name])); $full"
