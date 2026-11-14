# Probably Stolen mods

Seven complete mod build projects for [Probably Stolen](https://store.steampowered.com/app/4348910/Probably_Stolen/). Each project includes its C# source, manifest, build script, and required runtime resources. The build uses assemblies from your installed copy of the game.

本仓库包含七个 Mod 的完整构建工程：C# 源码、模组清单、构建脚本和运行所需资源。编译引用本机已安装游戏的程序集。

| Project | Version | Function | Steam Workshop |
|---|---|---|---|
| [F5ForceSave](F5ForceSave/) | 1.8.1 | F5 quick save / F5 存档 | [3828962908](https://steamcommunity.com/sharedfiles/filedetails/?id=3828962908) |
| [F5SnapshotSave](F5SnapshotSave/) | 0.8.1 | F5 quick save with more runtime state / F5 存档加强版 | [3831061477](https://steamcommunity.com/sharedfiles/filedetails/?id=3831061477) |
| [GunsmithExpansion](GunsmithExpansion/) | 0.7.6 | Gunsmith expansion / 枪匠拓展 | [3833272785](https://steamcommunity.com/sharedfiles/filedetails/?id=3833272785) |
| [GunsmithExpansion2077](GunsmithExpansion2077/) | 0.7.1 | Four additional guns for Gunsmith Expansion / 枪匠拓展 2077 附加包 | [3836151420](https://steamcommunity.com/sharedfiles/filedetails/?id=3836151420) |
| [KeptShotgun](KeptShotgun/) | 1.1.3 | Armed robbery and intimidation / 持枪抢劫与威胁议价 | [3830273949](https://steamcommunity.com/sharedfiles/filedetails/?id=3830273949) |
| [MentorNoteAutoFile](MentorNoteAutoFile/) | 1.1.1 | Automatically file mentor notes / 导师笔记自动收进手册 | [3828962954](https://steamcommunity.com/sharedfiles/filedetails/?id=3828962954) |
| [Moneylending](Moneylending/) | 1.5.1 | Moneylending / 放贷 | [3830603988](https://steamcommunity.com/sharedfiles/filedetails/?id=3830603988) |

The [open_beta](open_beta/) folder holds a variant of F5SnapshotSave for the game's open_beta branch. It is not published on the Workshop, has not been tested on open_beta and gets no timely support; see its README.

[open_beta](open_beta/) 文件夹存放为游戏 open_beta 分支准备的 F5SnapshotSave 变体：不上创意工坊，没有在 open_beta 上实测，不会得到及时的支持，详见其中的 README。

## Build / 构建

Validated with Windows, PowerShell 7, .NET SDK 9.0.318, and the rev13 game build 26525552. The scripts use the installed SDK's C# compiler, target the game's Mono assemblies, and restore no NuGet packages.

已验证环境为 Windows、PowerShell 7、.NET SDK 9.0.318、游戏 rev13（Steam build 26525552）。脚本调用 .NET SDK 的 C# 编译器，引用游戏的 Mono 程序集，无需还原 NuGet 包。

Run from the repository root, replacing the game path with yours:

在仓库根目录执行，游戏路径换成本机路径：

```powershell
pwsh -File ./build-mods.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\Probably Stolen' -OutDir 'C:\Temp\ProbablyStolenMods'
```

`OutDir` must be empty. The output contains seven ready-to-install mod folders. The aggregate build leaves the installed game and saves untouched. If `OutDir` is omitted, the script creates a new directory under the system temporary directory and prints its path.

`OutDir` 必须为空目录。输出包含七个可安装的 Mod 文件夹。总构建脚本仅写输出目录；省略 `OutDir` 时，会创建系统临时目录中的新文件夹并打印路径。

To build one mod, use its own script and specify an output directory:

单独构建一个 Mod：

```powershell
pwsh -File ./GunsmithExpansion/build.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\Probably Stolen' -OutDir 'C:\Temp\GunsmithExpansion'
```

The individual scripts default to installing into `<GameDir>/Mods/<ModID>` if `OutDir` is omitted. Close the game before installing. Use one installed copy of each Mod ID. GunsmithExpansion2077 requires GunsmithExpansion; choose either F5ForceSave or F5SnapshotSave for use in game.

单个 Mod 的脚本省略 `OutDir` 时，默认安装到 `<GameDir>/Mods/<ModID>`。安装前关闭游戏，每个 Mod ID 保留一份启用的副本。2077 附加包需要枪匠拓展；两种 F5 Mod 选择一种启用。

## Files / 文件

| Path | Build input |
|---|---|
| `<Mod>/src/*.cs` | C# source / C# 源码 |
| `<Mod>/manifest.xml` | Mod ID, version, capabilities, integrations / 标识、版本、权限与联动声明 |
| `<Mod>/build.ps1` | Compiler and resource packaging / 编译与资源打包 |
| `GunsmithExpansion/Textures/` | Runtime textures, including manual artwork / 运行时贴图与手册图片 |
| `GunsmithExpansion/Books/` | Editable in-game book definitions / 可编辑的游戏内手册定义 |
| `GunsmithExpansion2077/guns.tsv`, `Textures/` | Add-on gun data and textures / 附加包枪械数据与贴图 |
| `Moneylending/Textures/` | Runtime textures / 运行时贴图 |
| `F5SnapshotSave/tools/generate_snapshot_schema.ps1` | Snapshot field-schema generator / 快照字段表生成器 |
| `F5SnapshotSave/tools/generate_spec_fields.ps1` | Spec field-table generator, used by the GunsmithExpansion, Moneylending and KeptShotgun builds / 规范字段表生成器，枪匠拓展、放贷、持枪抢劫的构建使用 |

The checked-in C#, PNG, JSON and TSV files are sufficient to compile and package these versions. They can be edited directly. Game DLLs are read from the local game installation.

仓库中的 C#、PNG、JSON、TSV 文件可直接修改，足以编译和打包表中版本。游戏 DLL 从本机安装目录读取。

## F5 snapshot schema / F5 快照字段表

F5SnapshotSave generates `Data/snapshot-schema.json` at build time with Mono.Cecil from the game's `Unity.Burst.Cecil.dll`. Runtime field access uses the official `ModReflect` API. The schema records assembly module IDs, so rebuild it when the game or a supported integration DLL changes.

F5SnapshotSave 在构建时使用游戏提供的 Mono.Cecil 生成 `Data/snapshot-schema.json`。运行时通过官方 `ModReflect` 访问字段。字段表记录程序集模块标识，因此游戏或支持的联动 DLL 改变后需要重新生成。

GunsmithExpansion, Moneylending and KeptShotgun follow the F5 Quick Save Plus compatibility spec ([English](F5SnapshotSave/spec/SPEC.en.md), [中文](F5SnapshotSave/spec/SPEC.zh.md)). Each of their build scripts writes the field table of the DLL it has just compiled to `Data/f5-snapshot-fields.json` with `F5SnapshotSave/tools/generate_spec_fields.ps1`, so the Plus schema does not include these three mods. Since 0.8.0 F5SnapshotSave no longer supports CorpPresence, and its snapshot field-schema generator `F5SnapshotSave/tools/generate_snapshot_schema.ps1` leaves CorpPresence out. Build all mods with:

枪匠拓展、放贷、持枪抢劫遵守 F5 存档加强版兼容规范（[English](F5SnapshotSave/spec/SPEC.en.md)、[中文](F5SnapshotSave/spec/SPEC.zh.md)）。它们的构建脚本用 `F5SnapshotSave/tools/generate_spec_fields.ps1` 为刚编译的 DLL 生成字段表 `Data/f5-snapshot-fields.json`，因此加强版的字段表不包含这三个 Mod。加强版 0.8.0 起不再支持公司增强 CorpPresence，加强版的快照字段表生成器 `F5SnapshotSave/tools/generate_snapshot_schema.ps1` 不放入它。构建全部 Mod：

```powershell
pwsh -File ./build-mods.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\Probably Stolen' -OutDir 'C:\Temp\ProbablyStolenMods'
```

The individual F5SnapshotSave build script accepts `-IntegrationAssemblies` for explicit DLL paths; when omitted, it looks for the supported integrations in the game's `Mods` folder and in the Workshop folder of the same Steam library (`steamapps\workshop\content\4348910`). Rebuild the schema for the integration versions present when taking or restoring a snapshot.

单独构建加强版时，可用 `-IntegrationAssemblies` 指定 DLL 路径；省略时在游戏目录的 `Mods` 文件夹和同一 Steam 库的创意工坊文件夹（`steamapps\workshop\content\4348910`）中查找支持的联动。保存和恢复快照时使用的联动版本应与生成字段表时一致。

For Pen's Pipette, also pass `-PenPipetteDll 'C:\Path\Pen_mod_pipette.dll'` to the aggregate build. Version 1.1.3 is the integration target for this update. Third-party DLLs are supplied locally and are not bundled with the source project.

需要与 Pen 的移液枪联动时，总构建另传 `-PenPipetteDll 'C:\Path\Pen_mod_pipette.dll'`。本次更新的适配对象为 1.1.3。第三方 DLL 由本地提供，源码工程不附带这些 DLL。

For Vanilla Gun Expansion 0.3.7, pass `-VanillaGunExpansionDll 'C:\Path\VanillaGunExpansion.dll'` to include its dialogue callback fields in the Plus schema.

需要与原版枪械拓展0.3.7联动时，另传 `-VanillaGunExpansionDll 'C:\Path\VanillaGunExpansion.dll'`，将其顾客对话回调字段纳入加强版字段表。

Both F5 mods declare `files` for save-history access through the game's `SaveFiles` API. The mod code accesses the game's saves and history. See the [official modding guide](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md) for capability and Workshop verification requirements.

两种 F5 Mod 为游戏 `SaveFiles` API 的存档历史访问声明 `files` 权限。权限和创意工坊验证要求见[官方 Mod 指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)。
