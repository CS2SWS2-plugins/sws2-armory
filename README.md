# sws2-armory

基于 SwiftlyS2 的私人 CS2 服务器免费饰品插件，使用 `sws2-cs2menus` 提供统一菜单。按 SteamID64 保存个人选择，出生、拾取武器及换图后重新应用。饰品只在当前服务器显示，不向 Steam 库存添加物品。

目前为首版开发测试包；已进行源码构建和离线回归检查，客户端显示、动画与声音需要实服测试。

## 饰品范围

安装包内置离线目录，服务器运行时无需访问 GitHub：

| 类别 | 目录范围 |
| --- | --- |
| 普通枪械 | 34 种枪械，1,449 款皮肤，包含 AK-47、AWP、M4A1-S、USP-S 等 |
| 刀具 | 20 类刀具，576 个饰面选项，包含 Kukri 和各刀具原版饰面 |
| 手套 | 8 类手套，94 款皮肤 |
| 探员 | 63 名，T / CT 分别保存，模型及语音设置 |
| 音乐盒 | 100 款，包含 MVP 事件及计数 |
| 印花 | 11,134 款，枪械独立保存 0–4 共五个槽位 |
| 挂件 | 78 款，每把枪一个挂件 |
| 自定义 | Float 0–1、Seed 0–1000、StatTrak 开关/计数、名称标签 |

目录由 [AstraSkins](https://github.com/Ayrton09/AstraSkins) 的 MIT 数据转换，精确来源提交与校验值见 `resources/catalog/manifest.json`。游戏后续新增的饰品需更新目录；本版本不包含 Zeus 电击枪、手雷、C4、涂鸦和收藏品库存界面。刀具不支持印花或挂件，手套不支持 StatTrak。

## 安装

1. 安装 SwiftlyS2 **1.5.3 或更新版本**，以及兼容的 [sws2-cs2menus](https://github.com/XBDJ504764827/sws2-cs2menus)。插件使用该仓库提交 `3c0ec3b6aca0016d82f365f32acddeba338ae439` 的共享合约构建。其他套件插件无需安装。
2. 在 `csgo/addons/swiftlys2/configs/core.jsonc` 将 **`FollowCS2ServerGuidelines` 设为 `false`**，然后重启服务器。SwiftlyS2 的默认值 `true` 会阻止本插件写入饰品属性；本插件不自动修改宿主配置。
3. 将测试包或 Release ZIP 完整解压到 `csgo/addons/swiftlys2/plugins/`，保留 `sws2-armory/` 目录。不要只复制 DLL，也不要丢弃目录、翻译或 `resources/exports/`。
4. 先加载 menus，再加载 armory；首次安装建议完整重启或换图，使探员模型完成预缓存。

```text
sw plugins load sws2-cs2menus
sw plugins load sws2-armory
sw_armorystatus
```

运行目录：

```text
csgo/addons/swiftlys2/
├── plugins/sws2-armory/
│   ├── sws2-armory.dll
│   ├── Sws2Cs2Menus.Contracts.dll
│   ├── sws2-armory.deps.json
│   ├── resources/{catalog,exports,templates,translations}/
│   ├── licenses/AstraSkins-MIT.txt
│   └── THIRD-PARTY-NOTICES.md
├── configs/plugins/sws2-armory/config.jsonc
└── data/sws2-armory/players/<SteamID64>.json
```

`csgo/` 可能位于 `game/csgo/`。菜单沿用 menus 的 HUD 或中心 HTML 渲染，不需要 armory 专属 Workshop addon；自定义 HUD 的部署按 menus 文档操作。menus 不可用时，直接设置命令仍可使用。

## 玩家命令

聊天默认使用 `!` 或 `/`，控制台使用 SwiftlyS2 的 `sw_` 命令前缀。

| 命令 | 用途 |
| --- | --- |
| `!armory` / `!ws` | 打开饰品菜单 |
| `!armory 二西莫夫` | 搜索中英文名称、分组或目录 ID |
| `!knife` / `!gloves` / `!agents` / `!music` | 对应分类；可追加搜索词 |
| `!armory equip weapon/weapon_ak47:801` | 直接选择目录内的 AK-47 二西莫夫，无需菜单 |
| `!armory equip knife/weapon_knife_karambit:0` | 选择原版爪子刀；其他 ID 从目录读取 |
| `!wear 0.123456` | 调整已选皮肤的手持武器磨损 |
| `!wear 0.06 glove` | 调整手套磨损；末尾也可用 `knife` 指定刀具 |
| `!seed 661` / `!seed 661 knife` | 设置图案模板，范围 0–1000；支持 `glove` |
| `!stattrak` | 切换手持武器 StatTrak |
| `!stattrak on` / `!stattrak off` / `!stattrak 100 knife` | 设置 StatTrak 开关或计数；敌方击杀自动增加，最高 999999 |
| `!nametag 名称` / `!nametag clear` | 名称标签；最多 64 字符且不超过 160 个 UTF-8 字节 |
| `!stickers` | 手持枪械的印花槽位菜单 |
| `!stickers 0 关键词` | 搜索第 0 槽印花；范围 0–4 |
| `!stickers 4 clear` | 移除第 4 槽印花；搜索词也可以是完整印花目录 ID |
| `!charms 关键词` / `!charms clear` | 选择或移除手持枪械挂件；支持完整目录 ID |
| `!armoryrefresh` | 重试并重新应用个人饰品 |
| `!armoryreset` | 打开重置确认；无菜单时使用 `!armoryreset confirm` |
| `!armorystatus` | 查看版本、总饰品数、菜单及探员预缓存状态 |

皮肤按枪械类型保存，刀具与手套各保存一组；探员按 T / CT 分开保存。Float 和 Seed 的默认值来自目录，已手动设置的值在换皮肤后继续保留。印花及挂件也可直接应用到未选择涂装的普通枪械。

选择生效时会重新切换手持武器以刷新客户端；不赠送枪械、不补充弹药。若仍显示旧刀具、贴图或手套，请重新装备或重生。重置会清空插件偏好并移除插件饰品，**重生后完整恢复 Steam 库存原始外观**。插件卸载后同样以重生/换图恢复为准。

## 配置与保存

首次加载生成 `configs/plugins/sws2-armory/config.jsonc`，修改该文件时保留最外层节点：

```json
{
  "sws2-armory": {
    "Enabled": true,
    "Weapons": true,
    "Knives": true,
    "Gloves": true,
    "Agents": true,
    "Music": true,
    "Stickers": true,
    "Charms": true,
    "CommandCooldownSeconds": 1.0
  }
}
```

各分类免费向所有真人玩家开放，不要求管理员权限。开关热重载后应用到在线玩家；禁用某类别保留其已保存选择，以便再次开启。印花与挂件还要求 `Weapons` 开启。命令冷却限制为 0–30 秒，抑制连续刷新。

每次修改会生成不可变 JSON 快照，由单写入队列按顺序保存，并先写临时文件再原子替换。断线及卸载会等待队列完成；损坏或越界的存档会拒绝加载并记录日志，不自动覆盖原文件。修复存档后重连；数据目录需要写权限，升级前应备份 `data/sws2-armory/`。

探员只能在模型预缓存后应用。热加载插件时已错过当前地图的预缓存阶段，探员选择会保存并在下一张地图生效。目录更新需要重载插件并换图，不能只替换在线使用的 JSON 后期待自动刷新。

## 开发、CI 与发布

- 默认分支 `main`，功能开发与提交在 `develop`。实服测试后由维护者手动创建/合并 PR；不自动合并。
- 每次推送 `develop`、以及发往 `main` / `develop` 的 PR，都运行 **Code checks** 和 **Test package**。
- 代码检查包含格式/analyzer 检查、将编译警告视为错误、饰品目录/输入/存档回归检查、翻译及目录来源校验。
- 每次成功构建在 Actions Artifacts 生成 `sws2-armory-v1.0.0-dev.<run>.zip`；PR 使用 `-pr.<PR号>.<run>`。进入该运行记录下载 artifact，再取内部安装 ZIP。保留 30 天。
- PR 合并至 `main` 后自动构建正式包、创建版本标签及 GitHub Release。首次 `1.0.0`，以后按稳定标签自动增加 patch；手动 Release workflow 可选择 minor/major。
- 自动版本写入构建用 `VersionInfo.cs`、程序集及 ZIP 名称，不额外向分支写入版本提交。预览标签不参与稳定版递增。

本地需要 .NET 10 SDK 和 Python 3。将 menus 仓库放在同级 `../sws2-cs2menus`；CI 在项目内检出同一提交，项目文件同时兼容这两种目录。SDK 包固定为 SwiftlyS2 1.5.3，共享合约随安装包导出，不打包宿主程序集。

```bash
dotnet restore sws2-armory.csproj
dotnet format sws2-armory.csproj --verify-no-changes --no-restore --severity warn
dotnet build sws2-armory.csproj -c Release --no-restore -warnaserror
dotnet run --project tests/ArmoryChecks/ArmoryChecks.csproj -c Release
dotnet publish sws2-armory.csproj -c Release --no-build --no-restore
python3 tools/check_package.py build/sws2-armory.zip
```

输出为 `build/sws2-armory.zip`。更新目录：显式检出并审核参考仓库版本，然后运行 `python3 tools/import_catalog.py /path/to/AstraSkins`；转换器会记录提交与输入/输出 SHA-256。提交数据与 manifest 时一并检查许可证。

[实服验收步骤](docs/TESTING.md)覆盖换枪、丢枪、重生、换图、重连、禁用模块和菜单依赖重载。离线检查不模拟 CS2 引擎，不能替代这些验收步骤。
