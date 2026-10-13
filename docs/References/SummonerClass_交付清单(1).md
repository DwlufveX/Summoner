# 召唤师流派 Mod —— 交付给 Codex 的产物清单（v0.10.2）

> 用途：把"已经做出了什么"一次性说清，方便直接转交给 Codex（或任何编码 Agent）开工。
> 配套权威文档： **`召唤师流派系统设计需求v0.10.md`** （当前为 v0.10.2）。旧文件 `召唤师流派系统设计需求v0.2.md` 仅作为历史版本保留，不再使用。

---

## A. 设计文档（唯一权威需求，交 Codex 时一并给）

| 文件 | 说明 |
|---|---|
| `召唤师流派系统设计需求v0.10.md` | 完整系统需求 v0.10.2。含契约位=血脉槽位（roster slot）+ 备用兽容量口径（主兽不计入 `reservePerSlot`）、重写的成长公式（普通属性只在 3选1 时成长，上限 `base×(3+2×tier)`，§4.2 为"分配式"算例——普通属性与特殊变异共享 40 次选择预算）、**沉睡冷却作用域明确为单只兽（槽位仍可换备用兽顶主兽）**、锁血死亡路径状态机、MVP 经济数值、MVP 三兽基础属性与强度区间、威胁点补丁设计。**Codex 实现以本文件为准。** |

---

## B. Mod 工程结构（`SummonerClass/`）

可直接作为 RW 1.5 的 mod 骨架。Codex 在此之上接着写逻辑。

| 路径 | 类型 | 用途 | 状态 |
|---|---|---|---|
| `About/About.xml` | XML | 模组元数据（名称/适用版本/加载顺序） | 可用 |
| `About/Manifest.xml` | XML | 依赖声明（MVP 原版即可；机械血脉 Phase 2 需 Biotech） | 可用 |
| `Defs/ThingDefs/Races_SummonBeasts.xml` | XML | 18 只兽的 `RaceDef`；v0.10 魂缚契约兽模板：`HumanlikeAdult`、`<intelligence>Humanlike</intelligence>`、`<foodType>OmnivoreHuman</foodType>`，移除动物字段；顶部注释已写入威胁点/仿生/护甲/需求/影子 isColonist 规则 | 已对齐 18 张图 |
| `Defs/PawnKindDefs/SummonBeasts_PawnKinds.xml` | XML | 18 只兽的 `PawnKindDef`（生成用） | 已对齐 |
| `Defs/HediffDefs/SummonHediffs.xml` | XML | 灵魂虚弱/锁血护盾 等 HeDiff | 占位 |
| `Defs/AbilityDefs/SummonAbilities.xml` | XML | 呼唤/召回/复活 能力 | 占位 |
| `Defs/IncidentDefs/SummonIncidents.xml` | XML | 觉醒事件 | 占位 |
| `Languages/English/Keyed/SummonerClass_Keyed.xml` | XML | 多语言 keyed 文本 | 可用 |
| `Source/SummonerComp.cs` | C# | 召唤师核心组件（栏位/熟练度） | 骨架 |
| `Source/BoundSummonComp.cs` | C# | 契约兽组件（锁血退回/召回/升级触发3选1） | 骨架 |
| `Source/ChaosComp.cs` | C# | 混沌影子组件（Phase 3） | 骨架 |
| `Source/SummonAbilities.cs` | C# | 呼唤/召回/复活 Ability 实现 | 骨架 |
| `Source/SummonHarmony.cs` | C# | Harmony 补丁：威胁点计入（§16.1）/ BoundArmor / 需求关闭等 | 设计已确定，骨架待实现 |
| `Source/SummonBalance.cs` | C# | 平衡数值常量（软重置/锁血 CD 等） | 骨架 |
| `Source/SummonRewards.cs` | C# | 3选1 队列与奖励生成 | 骨架 |
| `Source/SummonIncidents.cs` | C# | 觉醒事件逻辑 | 骨架 |
| `Source/SummonEnums.cs` | C# | 枚举（血脉/定位/状态） | 可用 |
| `Source/SummonerMod.cs` | C# | Mod 入口 | 可用 |
| `Source/SummonerClass.csproj` | C# | 工程文件（**HintPath 需指向你本机 RW 的 dll 才能编译**） | 待本地化 |

---

## C. 贴图资产（18 张，256×256 PNG，透明单帧）

路径：`SummonerClass/Textures/Things/Pawn/<DefName>.png`
`texPath` 与 DefName 在 `Races_SummonBeasts.xml` 中已严格对齐（已用脚本验证 18 个一一对应）。

v0.9 已**全部重新生成并抠图**：去掉了棋盘格/水印/不透明背景，alpha 通道正确。每张图约 18~43% 不透明像素（主体），55~82% 透明像素。

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
1. 全部为**单帧 256×256 透明 PNG**（AI 重生成 + 自适应色键抠图）。**不是四向图**——正式进游戏需补 `_south/_north/_east/_west` 四张让朝向自然（影子类可只做两向）。
2. 已去除水印/棋盘格/不透明底色；边缘可能残留少量背景色，后续可人工精修。
3. **英雄兽粗边 + 脚底光圈 PostDraw 尚未实现**（§12 待做），目前只是普通贴图。
4. 主体辨识度够，但 AI 生成的细节（如武器/护甲贴合度）是占位级，可后续人工精修。

---

## D. 转交 Codex 时必须强调的边界

1. **编译前**：`SummonerClass.csproj` 的 `HintPath` 要指向你机器的 `Assembly-CSharp.dll` / `UnityEngine.dll` / `0Harmony.dll`；C# 目前是带 TODO 的骨架，不能直接跑。
2. **BodyDef**：MVP 德鲁伊用 `Wolf`/`Bear`/`Bird`；Phase 2 用 `Human`/`Scyther`/`Centipede`/`Lancer`/`Megascarab`/`Megaspider`。加载前请按你本机 RW 1.5 实际 BodyDefs 核对，必要时替换为可用名称。
3. **MVP 范围**：Phase 1 只做**德鲁伊 3 兽 + 觉醒/契约/召唤/召回/沉睡/锁血/升级变异3选1 + 灵质复活 + 存读档**；**威胁点补丁（§16.1）设计已确定，C# 仍是骨架，MVP 内必须实现**；血脉阶、仿生反馈、其余血脉移 Phase 2/3。仿生安装依赖玩家订阅的仿生 mod，本 mod 不提供部件也不限制。
4. **数值是初值待实测**：锁血触发条件、锁血 CD、沉睡 CD、灵质稀有度、经验曲线、继承系数(0.35) 都在文档 §21 列了，等你能跑起来再回调。
5. **两处 v0.10.2 收口的口径（务必照此实现，别被旧摘要误导）**：
   - **成长选择预算**：每升 1 级只给 **1 次 A 类 3选1**，普通属性与特殊变异**共享 40 次预算**——想拿 3 层体型就得少投普通属性，不能"40 次普通 + 3 次体型"同时满额（§4.1 选择预算 / §4.2 情景 A/B/C）。
   - **沉睡冷却作用域**：冷却**只绑在该兽身上**（该兽 3 日内不可召回）；**槽位不被锁**，同槽其他备用兽可随时顶上主兽（切换主兽无 CD）。`reservePerSlot` 只计备用兽，主兽不计入（§2.1 / §3.2 / §3.4 / §14）。

---

## E. 建议 Codex 的 Phase 1 实现顺序

1. 配好 csproj 引用 + About，先让空 mod 能在 RW 加载（验证 3 只德鲁伊兽能刷出、贴图能显示）。
2. `SummonerComp` + `BoundSummonComp`：契约位（血脉槽位）/备用兽列表/上场容量/召唤/召回/沉睡/主兽切换。
3. 魂缚契约兽规则：玩家控制；按血脉差异化需求（德鲁伊关心情、留吃饭睡觉）；普通兽不可绑架；混沌影子为 `isColonist` 殖民者型、可被绑架、可远程召回（§11）。
4. 锁血退回（"即将倒地/死亡"触发）+ 状态机（Active/Retreated/Sleeping/Lockout/Dead）+ 真死亡判定（§7.3）。
5. 升级经验 + 3选1 队列（A 变异每级、B 技能/工作每4级）+ 软重置公式（§4）。
6. 灵质矿（ThingDef 矿物）+ 采矿 + 复活 + 突破消耗。
7. 威胁点 Harmony 补丁（§16.1）+ 存读档（ExposeData）。
8. UI（gizmo 栏位/阶位/经验 + 未决奖励弹窗 + 沉睡/切换操作，§17）。
