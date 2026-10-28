# 召唤师 Summoner Class

大型 RimWorld Mod 独立工程，开发目录为 `E:\Project\Summoner`。后续统一按[系统设计需求 v0.10.2](docs/References/召唤师流派系统设计需求v0.10.md)推进。

和朋友一起开发时，从[协作说明](CONTRIBUTING.md)开始：克隆仓库、配置各自的游戏目录，在功能分支修改，再通过 Pull Request 合并。可复制 `.build.local.example.json` 作为本机配置。

**当前为大型版入门流程开发版 `0.3.0-dev`。** 已接入觉醒、简版牢笼救援、德鲁伊三兽、契约编队、呼唤/召回/轮换、征召移动与基础攻击。升级三选一、突破、锁血和复活仍未实现；需求版本不代表已实现的程序版本。

## 核心玩法

觉醒 → 救援并收集契约兽 → 安排主兽与备用兽 → 战斗和工作养成 → 升级三选一 → 消耗灵质突破 → 扩充阵容。

- 契约位管理血脉编队，每槽 1 主兽与最多 3 备用兽；上场容量独立限制同时出场的战力。
- 每只兽有四阶成长，每阶 Lv1–40；普通属性与特殊变异共享成长选择。
- 兽升级反哺召唤师，提供熟练度与能力选择。
- 灵魂界承担撤回、轮换和沉睡；濒死锁血退回，冷却内再次濒死则真死，可消耗灵质复活。
- MVP 仅德鲁伊灵狼、灵熊、灵鹰，后续扩展亡灵、元素、机械、虫族与混沌影子。

详细游玩逻辑、成长公式和待收口规则见[玩法与成长体系](docs/GAMEPLAY.md)。

现在的试玩路径：买觉醒种子 → 殖民者吸收 → 打开附近灵兽牢笼 → 选择灵兽安排召唤师缔约 → 打开主人“契约编队” → 呼唤/召回/切换 → 征召灵兽移动与攻击。狼、熊、鹰逐步进入同一德鲁伊槽，初始只能派一只。熊、鹰暂用单视角过渡素材；完整救援任务、专属技能与成长循环继续开发。

## 当前工程状态

已移除旧版单狼契约、VPE 技能树、Lv1–5、羁绊进化、七天创伤与对应测试。保留可复用的原创灵狼四向贴图、美术源稿和通用占位图标，供大型版使用。

支持 **RimWorld 1.6、1.7 公测版 + Harmony**。两版分别编译为 net48、net10.0，已在无 DLC 的完整游戏环境各通过两轮保存/读档，见[双版本验证](docs/COMPATIBILITY.md)。当前工程不依赖 VPE、VEF 或 Royalty，后续 DLC 血脉按需接入。原项目历史保存在 Git 初始提交及 `E:\Project\RimMod\Summoner`，当前工作区只维护大型版。

| 路径 | 内容 |
|---|---|
| docs/GAMEPLAY.md | 大型版游玩循环、升级、突破、轮换及待收口问题 |
| docs/ROADMAP.md | 开发阶段、实施顺序和范围 |
| docs/STATUS.md | 当前实现状态与验证边界 |
| docs/COMPATIBILITY.md | 1.6 与 1.7 公测版的依赖、构建和验收范围 |
| docs/tasks/01_DATA_FOUNDATION.md | 交给 Cursor/Grok 的第一轮数据与存档基础任务 |
| docs/References/召唤师流派系统设计需求v0.10.md | 权威需求，正文版本 v0.10.2 |
| docs/References/SummonerClass.zip | 大型版历史交付骨架与资源参考，不参与编译 |
| docs/References/ | 历史需求和交付清单只供参考，不覆盖权威需求 |
| docs/MIGRATION.md | 工程独立与大型版重建记录 |
| Source/ | 大型版工程及 Mod 入口 |
| Mod/ | Mod 元数据、构建程序集及可复用资源 |
| ArtSource/SpiritWolf/ | 原创灵狼源稿与来源说明 |
| Tools/ | 构建、检查、打包与资源重建工具 |

## 构建与检查

双版本构建需要 .NET 10 SDK、分别保留的游戏依赖与对应版本 Harmony。脚本优先使用 `.tools/dotnet` 中的 SDK，也可使用系统 SDK。忽略的 `.build.local.json` 可配置 `gameRoots` 下的 `1.6`、`1.7` 路径；也可直接传参。构建缓存和程序集按版本隔离。

```powershell
./Tools/Build.ps1
./Tools/Verify-Package.ps1
./Tools/Package.ps1
# 双版本依赖位置：
./Tools/Build.ps1 -GameRoot16 'X:/RimWorld-1.6' -GameRoot17 'X:/RimWorld-1.7'
# 单版本：
./Tools/Build.ps1 -GameVersion 1.7 -GameRoot 'X:/RimWorld-1.7'
# 诊断回归与运行中游戏保护，可分别指定 1.6、1.7：
./Tools/Test-DataFoundation.ps1 -GameVersion 1.7 -Mode regression
./Tools/Test-DataFoundation.ps1 -GameVersion 1.7 -Mode live-guard
# 真正启动游戏，生成测试殖民地并执行两轮保存/读档：
./Tools/Test-GameArchive.ps1 -GameVersion 1.6
./Tools/Test-GameArchive.ps1 -GameVersion 1.7
# 觉醒、真实救援/契约/移动任务、三兽轮换与战斗，并进行两轮存读：
./Tools/Test-GameArchive.ps1 -GameVersion 1.6 -TestMode Gameplay
./Tools/Test-GameArchive.ps1 -GameVersion 1.7 -TestMode Gameplay
```

打包产物 `Dist/RimModSummoner-v0.3.0-dev-1.6-1.7.zip` 是入门流程开发包。共用资源放在根目录，`LoadFolders.xml` 只加载当前版本的 `1.6/Assemblies` 或 `1.7/Assemblies`；包内不附带游戏、Harmony 或测试程序集。测试结果见[三兽与契约结果](docs/tasks/03_DRUID_CONTRACT_RESULT.md)。

`Tests/ScribeHost` 保存诊断回归宿主，原 `full` 模式缺少完整游戏初始化，仍应返回失败；实际存读验收使用 `Tests/GameSupport`。游戏验证工具需要 PowerShell 7.4+，默认在 `C:\SummonerTests` 创建完整副本、独立 Mods/配置/存档与临时缓存，只启用 Core、Harmony、本 Mod 和测试模块。禁止放入系统临时目录，不使用指向原资源的目录链接。不会操作正式殖民档。证据摘要见 `docs/Evidence/dual-version`。
