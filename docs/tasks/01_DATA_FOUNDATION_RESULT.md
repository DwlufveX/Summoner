# 第一轮结果 数据与存档基础

日期：2026-10-13。执行范围：`docs/tasks/01_DATA_FOUNDATION.md`。未提交。

## 完成内容

建立了每局一份的召唤师档案，不恢复旧灵狼玩法，也不把未收口的规则写成自动行为。

- 召唤师记录：唯一主人引用、熟练度、上场容量、契约位容量、契约槽列表、未决奖励列表。新建时上场容量和契约位容量都是 1，并带一个空契约槽。
- 契约槽：稳定 ID、血脉标识、主兽 ID、最多 3 个备用兽 ID。主兽不占用备用名额。
- 兽记录：稳定 ID、主人、阶位 1–4、阶内等级 1–40、经验、体量 1 或 2、普通属性投入、特殊变异、技能与工作记录。
- 兽状态分成四项，而不是一个状态枚举：位置（在场 / 界内）、休整（无 / 沉睡）、锁血冷却截止 tick、死亡标记。主兽或备用身份由契约槽列表重建。
- 未决奖励：稳定 ID、所属对象、类型、来源阶位、来源等级，以及可保存的候选项和选择下标。本轮没有任何玩法会生成或发放奖励。
- 灵魂界容器：游戏级 `ThingOwner` 持有界内 Pawn。在场 Pawn 和召唤师主人只保存引用。
- 兽组件只保存兽 ID。成长、槽位和冷却以档案为准，加载后写回组件。
- 开发者动作 `Summoner / Data foundation roundtrip` 会做一次临时往返。它不挂在开局或普通玩家操作上。

## 文件

| 文件 | 作用 |
|---|---|
| `Source/SummonerRecords.cs` | 限制常量、状态枚举、奖励、契约槽、兽和召唤师记录 |
| `Source/SoulRealmContainer.cs` | 界内 Pawn 的持有者 |
| `Source/SummonerArchive.cs` | `GameComponent`、登记、容量校验、一致性检查和存档 |
| `Source/CompSummonerBeastLink.cs` | 兽 Pawn 上的 ID 外键 |
| `Source/DataFoundationCheck.cs` | 显式数据往返入口 |
| `Tools/Verify-Package.ps1` | 按 UTF-8 读取 XML，避免无 BOM 的中文 About 被系统代码页拆坏 |

`Source/SummonerMod.cs` 未改。没有新增玩法 Def，没有改贴图，没有安装到游戏目录，没有读写玩家存档。

## 数据所有权

| 数据 | 唯一来源 | 其他位置 |
|---|---|---|
| 召唤师、契约槽、奖励、兽成长和状态 | `SummonerArchive` 内的记录 | 无第二份可改副本 |
| 槽内主兽和备用兽名单 | `ContractSlotRecord` | 兽上的槽位 ID 和身份在加载后按槽位重建 |
| 界内 Pawn 实体 | `SoulRealmEntry` 的 `ThingOwner<Pawn>` | 兽记录只在加载后指向它 |
| 在场 Pawn、召唤师主人 | 地图或世界 | 档案使用 `Scribe_References` |
| 兽 ID 索引 | 档案里的稳定 ID | `CompSummonerBeastLink.beastId` 加载后按档案覆盖 |
| ID 计数器 | 档案内的整数，保存后继续递增 | 不使用名字、列表下标或对象哈希 |

同一只兽不能进入两个槽，也不能属于两个召唤师。同一个主人 Pawn 只会有一份召唤师记录。上场占用只累加位置为在场的兽的体量；留在界内的备用兽不计入。死亡只把 `dead` 设为真，不改槽位名单，也不改位置。

保存前如果界内容器里的 Pawn 已经在地图或世界上，或者同时又被记成在场引用，档案会放弃这份深度保存并记一条错误，避免同一个实体写两遍。这个保护没有拿到真实 Pawn 上跑过。

## 保存方式

`Game.FillComponents` 会构造 `SummonerArchive(Game)`，再由游戏组件列表深度保存。嵌套记录实现 `IExposable`。缺失字段使用默认值：容量 1、阶位 1、等级 1、冷却 tick 为 -1、列表为空。不读取已经删除的旧灵狼存档。

稳定 ID 形如 `summoner-1`、`slot-1`、`beast-1`、`reward-1`。计数器随档案保存。若计数器节点缺失，加载时会抬到已有 ID 之后，避免重新从 1 开始。

兽 ThingDef 以后把 `CompProperties_SummonerBeastLink` 放进 `comps` 即可带上外键。当前没有兽 Def，所以组件还不会出现在任何 Pawn 上。

## 构建与测试证据

`Tools/Build.ps1`：Release 成功，0 警告、0 错误，输出 `Mod/Assemblies/RimModSummoner.dll`。

程序集引用只有 `mscorlib`、`Assembly-CSharp`、`System.Core`。没有 VPE、VEF、Royalty，也没有把 Harmony 或游戏程序集复制进 `Mod/Assemblies`。

`Tools/Verify-Package.ps1`：`PASS: XML parse, 0 unique typed definitions, local texture references and dependency-free assembly package.` 随后说明仍是开发骨架，没有玩法 Def。

`DataFoundationCheck.TryRun` 在加载了游戏程序集的独立进程里跑过，不读写玩家存档：

- 内存检查通过：初始容量 1、第二契约槽被拒绝、第 4 个备用兽被拒绝、三个界内备用兽不增加上场占用、体量 2 会计入上场占用、死亡后的兽仍留在备用名单、同一主人不会建成两份档案、跨主人入槽被拒绝、主兽重复进入备用名单会被一致性检查发现。
- 存档往返通过：临时 XML 2914 字节；读回后身份、槽位顺序、奖励候选项、冷却 tick 和成长记录一致；下一只兽 ID 为 `beast-6`。删掉 `<tier>2</tier>` 和 `<lockoutEndTick>60000</lockoutEndTick>` 后再读，阶位回到 1，锁血截止 tick 回到 -1。

这次往返没有使用真实 Pawn。脱离游戏进程时 `GenTypes` 扫类型失败，每条记录都出现一次 “Could not find class … Trying to use … instead”，回退类型与字段类型相同，数值比对仍然通过。`ScribeLoader.FinalizeLoading` 在 `DeepProfiler.Start` 处因没有游戏运行时而空引用；数值已经在 `LoadingVars` 阶段读入。因此不能把这次结果写成游戏内殖民档存读已经通过，也不能写成界内 Pawn 的深度保存已经通过。

游戏内复现：开开发者模式，读入一个可以丢弃的测试档，在调试动作里运行 `Data foundation roundtrip`。它使用全局 Scribe 写临时文件，不修改当前殖民档；存档进行中会拒绝执行。要验证界内 Pawn，需要先有一只未生成、也不在世界 Pawn 表里的兽，再调用 `BindRealmPawn` 后保存。本轮没有做这一步。

## 待决定规则

本轮只留字段，没有把下面任何一条写成正式行为：

- 每阶 39 次还是 40 次 A 类奖励，以及 B 类和反哺按等级还是按升级次数触发。
- 同一血脉的多只兽能否同时在场。容量只限制体量之和，不强制每槽同时只能有一只在场。
- 第二契约位是达到熟练度就增加，还是只取得资格。档案不会因熟练度自动加槽。
- 经验公式、取整和工作计量单位。档案只保存经验数值。
- 四阶满级之后是否继续给奖励。
- 真死是否让出契约槽，槽满时如何复活。死亡记录保留 ID、成长和槽位名单。
- 主人死亡、被俘或离开阵营后，兽和灵魂界如何处理。

血脉标识两边都非空且不同时，入槽会被拒绝。有一边还是空的，则允许写入，等以后的契约动作决定何时写上血脉。

## 未完成

- 没有兽种、觉醒、救援、召唤、召回、沉睡执行、锁血、复活、经验增长、奖励生成、突破、灵质、威胁点或玩家 UI。
- 没有在游戏进程里保存和读取殖民档。
- 没有深度保存一只真实界内 Pawn，也没有验证在场 Pawn 的交叉引用。
- 没有把本轮程序集重新打进 `Dist` 压缩包。
