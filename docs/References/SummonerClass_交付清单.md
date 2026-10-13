# 召唤师流派 Mod —— 交付给 Codex 的产物清单

> 用途：把"已经做出了什么"一次性说清，方便直接转交给 Codex（或任何编码 Agent）开工。
> 配套权威文档：`召唤师流派系统设计需求v0.2.md`（文件名未改，内容已是 **v0.8**）。

---

## A. 设计文档（唯一权威需求，交 Codex 时一并给）

| 文件 | 说明 |
|---|---|
| `召唤师流派系统设计需求v0.2.md` | 完整系统需求 v0.8。含术语三层拆解、数值总表、软重置公式与唯一计算顺序、真死亡条件、灵质来源、humanlike Pawn 地基、MVP/Phase 划分、UI 章、调参待定项。**Codex 实现以本文件为准。** |

---

## B. Mod 工程结构（`SummonerClass/`）

可直接作为 RW 1.5 的 mod 骨架。Codex 在此之上接着写逻辑。

| 路径 | 类型 | 用途 | 状态 |
|---|---|---|---|
| `About/About.xml` | XML | 模组元数据（名称/适用版本/加载顺序） | 可用 |
| `About/Manifest.xml` | XML | 依赖声明（Biotech 必装、Royalty 选装） | 可用 |
| `Defs/ThingDefs/Races_SummonBeasts.xml` | XML | 18 只兽的 `RaceDef`（texPath/drawSize/体量/需求） | 已对齐 18 张图 |
| `Defs/PawnKindDefs/SummonBeasts_PawnKinds.xml` | XML | 18 只兽的 `PawnKindDef`（生成用） | 已对齐 |
| `Defs/HediffDefs/SummonHediffs.xml` | XML | 灵魂虚弱/锁血护盾/灵魂疲劳 等 HeDiff | 占位 |
| `Defs/AbilityDefs/SummonAbilities.xml` | XML | 呼唤/召回/复活/互换 四个能力 | 占位 |
| `Defs/IncidentDefs/SummonIncidents.xml` | XML | 觉醒事件、财富分级守卫任务 | 占位 |
| `Languages/English/Keyed/SummonerClass_Keyed.xml` | XML | 多语言 keyed 文本 | 可用 |
| `Source/SummonerComp.cs` | C# | 召唤师核心组件（栏位/熟练度/血脉阶/仿生重算） | 骨架 |
| `Source/BoundSummonComp.cs` | C# | 契约兽组件（锁血退回/召回/升级触发3选1/仿生） | 骨架 |
| `Source/ChaosComp.cs` | C# | 混沌影子组件（Phase 3） | 骨架 |
| `Source/SummonAbilities.cs` | C# | 四个 Ability 实现 | 骨架 |
| `Source/SummonHarmony.cs` | C# | 威胁点注入 + 仿生反馈 Harmony 补丁 | 骨架 |
| `Source/SummonBalance.cs` | C# | 平衡数值常量（锁血阈值/经验曲线等） | 骨架 |
| `Source/SummonRewards.cs` | C# | 3选1 队列与奖励生成 | 骨架 |
| `Source/SummonIncidents.cs` | C# | 觉醒/守卫事件逻辑 | 骨架 |
| `Source/SummonEnums.cs` | C# | 枚举（血脉/定位/状态） | 可用 |
| `Source/SummonerMod.cs` | C# | Mod 入口 | 可用 |
| `Source/SummonerClass.csproj` | C# | 工程文件（**HintPath 需指向你本机 RW 的 dll 才能编译**） | 待本地化 |

---

## C. 贴图资产（18 张，256×256 PNG，单帧占位）

路径：`SummonerClass/Textures/Things/Pawn/<DefName>.png`
`texPath` 与 DefName 在 `Races_SummonBeasts.xml` 中已严格对齐（已用脚本验证 18 个一一对应）。

| 文件名 | DefName | 血脉 | 定位 | 阶段 | 备注 |
|---|---|---|---|---|---|
| `SpiritWolf.png` | SpiritWolf | 德鲁伊 | 战士 | **MVP** | 灵狼，已含粗边需求待补 |
| `SpiritBear.png` | SpiritBear | 德鲁伊 | 肉盾 | **MVP** | 灵熊 |
| `SpiritHawk.png` | SpiritHawk | 德鲁伊 | 远程 | **MVP** | 灵鹰 |
| `BoneWarrior.png` | BoneWarrior | 亡灵 | 战士 | Phase 2 | 骨骸战士 |
| `SkeletonGuardian.png` | SkeletonGuardian | 亡灵 | 肉盾 | Phase 2 | 骸骨卫士 |
| `BoneArcher.png` | BoneArcher | 亡灵 | 远程 | Phase 2 | 骨弓手 |
| `FireElemental.png` | FireElemental | 元素 | 战士 | Phase 2 | 火元素 |
| `StoneElemental.png` | StoneElemental | 元素 | 肉盾 | Phase 2 | 岩石元素 |
| `IceElemental.png` | IceElemental | 元素 | 远程 | Phase 2 | 冰元素 |
| `MechBrawler.png` | MechBrawler | 机械 | 战士 | Phase 2 | 机甲战士 |
| `MechFortress.png` | MechFortress | 机械 | 肉盾 | Phase 2 | 机甲堡垒 |
| `MechGunner.png` | MechGunner | 机械 | 远程 | Phase 2 | 机甲炮手 |
| `BladeBug.png` | BladeBug | 虫族 | 战士 | Phase 2 | 刃虫 |
| `CarapaceBeetle.png` | CarapaceBeetle | 虫族 | 肉盾 | Phase 2 | 甲虫 |
| `AcidBug.png` | AcidBug | 虫族 | 远程 | Phase 2 | 酸虫 |
| `ShadowWarrior.png` | ShadowWarrior | 混沌 | 战士 | Phase 3 | 影之战士 |
| `ShadowTank.png` | ShadowTank | 混沌 | 肉盾 | Phase 3 | 影之盾 |
| `ShadowRanged.png` | ShadowRanged | 混沌 | 远程 | Phase 3 | 影之弓 |

**⚠️ 贴图现状（务必告诉 Codex）：**
1. 全部为**单帧 256×256 透明 PNG**（AI 生成，环世界平涂画风）。**不是四向图**——正式进游戏需补 `_south/_north/_east/_west` 四张让朝向自然（影子类可只做两向）。
2. **英雄兽粗边 + 脚底光圈 PostDraw 尚未实现**（§12 待做），目前只是普通贴图。
3. 主体辨识度够，但 AI 生成的细节（如武器/护甲贴合度）是占位级，可后续人工精修。

---

## D. 转交 Codex 时必须强调的边界

1. **编译前**：`SummonerClass.csproj` 的 `HintPath` 要指向你机器的 `Assembly-CSharp.dll` / `UnityEngine.dll` / `0Harmony.dll`；C# 目前是带 TODO 的骨架，不能直接跑。
2. **BodyDef** 已修正为 core 真实存在的（`Wolf`/`Thrumbo`/`Chicken`/`Human`/`Scyther`/`Centipede`/`Lancer`/`Megascarab`/`Megaspider`），加载前请核对 RW 1.5 实际 defName。
3. **MVP 范围**：Phase 1 只做**德鲁伊 3 兽 + 觉醒/契约/召唤/召回/锁血/升级变异3选1 + 存读档**，先跑手感；其余血脉/影子/任务按 Phase 2/3 顺序。
4. **数值是初值待实测**：锁血阈值(15%)、灵质稀有度、经验曲线、仿生比例(0.4)、继承系数(0.35) 都在文档 §21 列了，等你能跑起来再回调。

---

## E. 建议 Codex 的 Phase 1 实现顺序

1. 配好 csproj 引用 + About，先让空 mod 能在 RW 加载（验证 3 只德鲁伊兽能刷出、贴图能显示）。
2. `SummonerComp` + `BoundSummonComp`：栏位/契约/召唤/召回。
3. 锁血退回 + 锁血 CD + 真死亡判定（§10）。
4. 升级经验 + 3选1 队列（变异每级、技能每4级）+ 软重置公式（§4）。
5. 反哺 + 血脉阶（解锁/启用分离，§6.1）。
6. 灵质矿（ThingDef 矿物）+ 采矿 + 复活。
7. 存读档（ExposeData）。
8. UI（gizmo 栏位/阶位/经验 + 未决奖励弹窗，§17）。
