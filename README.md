# 召唤师 / Summoner — 独立大型 Mod 项目

独立开发目录：`E:\Project\Summoner`，拥有单独 Git 仓库。2026-10-13 从 RimMod 工作区提取，作为下一个大型召唤师 Mod 的开发入口。

当前可运行基线是 **v0.1.2「灵狼契约」**：RimWorld 1.6 / Vanilla Psycasts Expanded 附属 MOD，运行包是 `Mod/`，单人测试版。大型版本的系统需求见 [设计需求 v0.10.2](docs/References/召唤师流派系统设计需求v0.10.md)（文件名保留 v0.10），实施阶段和规则差异见 [大型版本路线](docs/ROADMAP.md)。需求版本号不代表已实现的程序版本。

源码、XML、美术源稿与工具均在本项目中；RimWorld、Harmony、VEF、VPE 是外部安装依赖。旧目录 `E:\Project\RimMod\Summoner` 保留历史代码、发行包和测试存档，后续改动只在新目录进行。

## 安装与游玩

1. 备份要使用的存档。将运行包文件夹 `RimModSummoner` 放入游戏的 `Mods` 目录。
2. 加载 Harmony、Core、Royalty、Vanilla Expanded Framework、Vanilla Psycasts Expanded，然后加载本 MOD。依赖不包含在安装包中。
3. 进入游戏，给已有灵能的小人解锁「召唤师」Path，并学习「灵魂契约」。普通小人仍需要先获得 VPE 灵能能力。
4. 学习后先收到遥远感应；约1–2天后，地图内会出现由3–6名敌对部落成员守卫的灵狼营地。击败或驱离仍有战斗力的守卫，靠近灵狼12格施放灵魂契约。候选限三天，仅对应召唤师可签约。
5. 签约会自动完成驯服、顺从和释放，不需要训练或维护顺从度。选中灵狼即可使用原版征召按钮，直接下达移动和攻击命令。契约立即提供移速+10%、近战伤害+15%、攻击间隔-10%、承伤-15%。
6. 按正常动物照顾其食物、睡眠和伤病。学习后续「灵魂呼唤」「灵魂链接」。选中灵狼查看「灵魂契约」页与猎杀指令。
7. 经验到Lv5且羁绊至少75，让双方清醒且站在12格内，点击灵狼的进化按钮。它会成为巨牙灵狼，保持同一生物和伤病。
8. 灵狼死后主人有七天灵魂创伤；结束后自动寻找新狼。主人死亡后狼失魂一天，然后恢复野性。

没有候选、没有有效契约且没有创伤时，可对主人自身施放灵魂契约来重新寻找。候选消失/死亡的重试间隔为一天。呼唤只移动同地图实体，不能跨商队、跨地图或复活。

## 项目文件

| 路径 | 内容 |
|---|---|
| docs/Summoner_v0.1_Design.md | 范围、完整规则、数值与验收条件 |
| docs/TEST_REPORT.md | 实际测试结果和仍需人工验证的部分 |
| Source/Contracts.cs | 契约记录、事件、死亡与世界状态保存 |
| Source/SpiritWolf.cs | Lv1–5、羁绊、进化、动物指挥和状态页 |
| Source/Abilities.cs | 三个VPE技能与经验物品 |
| Source/Patches.cs | 战斗/分摊/治疗、贸易/繁殖/驯服保护、心情 |
| Source/SelfTest.cs | 显式启动参数启用的隔离游戏集成测试 |
| Mod/Defs | Path、能力、生物、成长、状态和商品XML |
| Mod/Patches/Traders.xml | 异乡商队与轨道珍奇商人经验商品 |
| Mod/Textures | 原创绿光灵狼四向贴图、几何UI占位图标和资源来源说明 |
| ArtSource/SpiritWolf | 图像生成得到的原创三视图源稿；安装包不需要此目录 |
| Tools | 编译辅助、占位资源生成、隔离测试和打包 |
| docs/References | 大型版本设计稿、历史设计及交付骨架 ZIP；不参与当前编译 |
| docs/ROADMAP.md | 大型版本阶段与当前实现的区别 |
| docs/MIGRATION.md | 独立迁移范围、旧目录位置和验证记录 |

## 构建与测试

需要 .NET SDK 和本地 RimWorld 1.6、Harmony、VEF、VPE 安装。脚本默认使用本机 `D:\STEAM` 安装路径；其他机器可向构建、打包和测试脚本传入 `GameRoot`（游戏根目录）及 `Workshop`。

```powershell
./Tools/Build.ps1
./Tools/Verify-Package.ps1
./Tools/Package.ps1
# 覆盖本机依赖位置：
./Tools/Build.ps1 -GameRoot 'X:/RimWorld' -Workshop 'X:/steamapps/workshop/content/294100'
# 游戏行为测试，按需显式运行：
./Tools/Test-Game.ps1
```

测试在项目的 `TestRuns/` 下建立独立设置与存档，启动独立的隐藏游戏进程，不更改正常游戏启用列表。脚本会将本项目运行包覆盖安装到所选游戏的 `Mods/RimModSummoner`。日志出现 `[SummonerTest] ALL TESTS PASSED` 表示集成测试完成。测试主动在独立存档中造成死亡；不要对正式存档添加 `-summoner-selftest` 参数。

`Source/obj`、`Source/bin`、`Mod/Assemblies`、`Dist`、`TestRuns` 不纳入 Git；DLL 由源码重新构建。灵狼贴图重建默认读取项目内 `ArtSource/SpiritWolf/SpiritWolf_SourceSheet.png`。

## 当前边界

界面主要为简体中文；English仅提供状态页键名回退。灵狼已使用原创的深色狼身、翠绿色发光灵纹四向贴图；巨牙形态本轮仍共用该外观，通过名称、契约页与健康状态区分。未进行多人同步、Combat Extended或用户完整整合包兼容认证。跨地图呼唤明确不支持。所有数值是可调的第一版初值。

成年灵狼的物理体型为1.0、绘制尺寸为2.2；原版森林狼分别约为0.85和1.95，因此灵狼在画面和体型判定上都略大。CE弹道防护尚未写死：当前通用承伤-15%会保留，CE专用锐器装甲/灵体偏转等待实战数值确定后再加。

v0.1.2 不包含三专精、召唤师熟练度、第二契约位、灵魂负荷、多分支进化、融合、敌对召唤师。大型版本的后续范围以 `docs/ROADMAP.md` 和其中引用的需求稿为准。

API适配参考本机已安装依赖及上游源码：https://github.com/Vanilla-Expanded/VanillaPsycastsExpanded 。发行包不包含这些依赖或第三方美术。
