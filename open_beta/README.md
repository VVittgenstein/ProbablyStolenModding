# open_beta

Everything in this repository that is made for the open_beta branch of [Probably Stolen](https://store.steampowered.com/app/4348910/Probably_Stolen/) is in this folder. It is organized like the repository root: one folder per mod, each a complete build project. Nothing here is published on the Steam Workshop. You build it yourself.

本仓库中为游戏 open_beta 分支准备的内容全部放在本文件夹，组织方式与仓库根目录相同：每个 Mod 一个文件夹，各为完整的构建工程。这里的内容不在创意工坊发布，由使用者自行编译。

**Not tested on open_beta. No timely support.** Nothing in this folder has been run on any open_beta build of the game. The variant was tested only on the public build 26525552, with the schema mismatch simulated. Problems on open_beta may stay unanswered for a long time. The Workshop versions and the other folders of this repository follow the game's public branch.

**没有在 open_beta 上实测，不会得到及时的支持。** 本文件夹的内容没有在任何 open_beta 构建上运行过，变体只在正式版构建 26525552 上以模拟的字段表不一致做过测试。open_beta 上的问题可能长时间得不到处理。创意工坊版本与本仓库的其他文件夹跟随游戏的正式版分支。

| Project | Version | Function |
|---|---|---|
| [F5SnapshotSave](F5SnapshotSave/) | 0.6.0-openbeta | Variant of F5 Quick Save Plus for open_beta / F5 存档加强版的 open_beta 变体 |

## Files / 文件

| Path | Content |
|---|---|
| `README.md` | This notice / 本说明 |
| `F5SnapshotSave/src/*.cs` | C# source of the variant / 变体的 C# 源码 |
| `F5SnapshotSave/manifest.xml` | Mod ID, version, capabilities, integrations / 标识、版本、权限与联动声明 |
| `F5SnapshotSave/build.ps1` | Compiler and field-schema generation / 编译与字段表生成 |
| `F5SnapshotSave/tools/generate_snapshot_schema.ps1` | Snapshot field-schema generator / 快照字段表生成器 |

## What the variant changes / 变体与 0.6.0 的差异

Based on F5SnapshotSave 0.6.0 in the repository root. The Mods panel and the log show the version as `0.6.0-openbeta`.

Snapshots need a field schema (`Data/snapshot-schema.json`) that is generated for one game build. In 0.6.0, when the game is a different build, every press of the save key shows "Save failed, see Player.log.". This variant then saves the way F5 Quick Save (F5ForceSave) does:

- Customers waiting in line are stored by ID and created again on load; their budgets and goods are rolled again. If a waiting customer cannot be created again by ID, saving is refused.
- While a customer is at the counter, saving is refused and a notice says so.
- A save that contains a snapshot is loaded with its customers created again by ID.

While the schema matches the game, the variant behaves like 0.6.0. A build made against your own game matches until the game is updated; build again after a game update to get snapshots back.

以仓库根目录的 F5SnapshotSave 0.6.0 为基础。Mod 面板与日志中的版本显示为 `0.6.0-openbeta`。

快照需要字段表（`Data/snapshot-schema.json`），字段表按一个游戏构建生成。0.6.0 在游戏是另一个构建时，每次按存档键都提示“存档失败，详见 Player.log。”。这个变体在这种情况下改按「F5 存档」（F5ForceSave）的方式存档：

- 排队的顾客按 ID 存下，读档时重新生成，预算和物品重新随机。排队顾客中有不能按 ID 重新生成的，拒绝存档。
- 柜台有顾客时拒绝存档，并给出提示。
- 读取含快照的存档时，顾客按 ID 重新生成。

字段表与游戏一致时，变体的行为与 0.6.0 相同。按自己的游戏编译得到的字段表在游戏下次更新前是一致的；游戏更新后重新编译，快照功能即可恢复。

## Build / 构建

Windows, PowerShell 7 and a .NET SDK are required (validated with .NET SDK 9.0.318 on the public game build). Run from the repository root and replace both paths with yours:

需要 Windows、PowerShell 7 与 .NET SDK（已验证的环境为 .NET SDK 9.0.318 与正式版游戏）。在仓库根目录执行，两个路径换成本机路径：

```powershell
pwsh -File ./open_beta/F5SnapshotSave/build.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\Probably Stolen' -OutDir 'C:\Temp\F5SnapshotSave'
```

The output folder contains `F5SnapshotSave.dll`, `manifest.xml` and `Data/snapshot-schema.json`. Close the game and copy the folder to `<GameDir>/Mods/F5SnapshotSave`. Keep one installed copy of this Mod ID: unsubscribe from the Workshop version first. Then enable the mod in the Mods panel and follow the game's prompts for the `files` capability. If `OutDir` is omitted, the script installs directly into `<GameDir>/Mods/F5SnapshotSave`.

输出文件夹包含 `F5SnapshotSave.dll`、`manifest.xml` 与 `Data/snapshot-schema.json`。关闭游戏后把它复制为 `<GameDir>/Mods/F5SnapshotSave`。同一个 Mod ID 只保留一份：先取消订阅创意工坊版本。然后在 Mod 面板中启用，并按游戏的提示确认 `files` 权限。省略 `OutDir` 时，脚本直接安装到 `<GameDir>/Mods/F5SnapshotSave`。

The schema covers the supported integration mods found in your game installation when you build. Build again when one of them is updated.

字段表包含编译时在游戏安装目录中找到的受支持联动 Mod。其中任何一个更新后，需要重新编译。
