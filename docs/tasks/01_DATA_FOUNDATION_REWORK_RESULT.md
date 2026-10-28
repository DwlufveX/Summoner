# 第一轮返修结果 数据一致性与完整存读

日期：2026-10-14。工程版本：`0.2.0-dev`。原结果和审阅文件都还在。

本轮只改数据层的绑定、容量、奖励归属和存读报告。没有恢复旧灵狼，没有加入自动升级、技能、任务、锁血、沉睡治疗、复活、威胁补丁、玩家界面或美术，没有把 Mod 装进游戏，也没有打开殖民档。39 与 40 次奖励、同血脉能否同时上场、第二契约槽解锁、经验公式、四阶奖励、真死是否腾位、主人死亡，这些仍未决定。

## 修改文件

- `Source/SummonerArchive.cs`
- `Source/SoulRealmContainer.cs`
- `Source/DataFoundationCheck.cs`

隔离运行使用 gitignore 下的 `TestRuns/ScribeHost`。它不进 Mod 包。宿主在调用 `ThingOwner.NotifyAdded` 前跳过原方法，因为无头进程没有初始化 `ThingDefOf`，火焰附着通知会空引用。`TryAdd` 仍由原版把 Pawn 放进容器并写上 `holdingOwner`。这个跳过只存在于测试宿主，不在 Mod 里。

## R1 实体绑定

同一兽改绑另一只 Pawn 会在改记录和容器之前拒绝。同一只 Pawn 再绑一次直接返回成功，不会多出条目。持有失败时新条目不进容器。保存路径不再调用原来的 `ReleaseExternallyOwnedRealmPawns`，不会在写档时丢掉灵魂界里的唯一持有者。

一致性检查现在会指出：重复或空的界内兽 ID、空条目、界内 Pawn 和记录不是同一只、上场记录的 ID 仍被容器持有、条目没有对应兽记录。

`SoulRealmEntry.GetChildHolders` 改为调用 `ThingOwnerUtility.AppendThingHoldersFromThings`，界内 Pawn 会作为子持有者出现。

内存输出：

```text
binding reject different pawn; same pawn idempotent; holder walk includes realm pawn
```

这条覆盖了：重复界内绑定、改绑另一只 Pawn、把界内兽改绑到另一只场上 Pawn、同一只 Pawn 绑到另一头兽、拒绝后旧 Pawn 和条目数不变、重复 ID、空条目、记录与条目不一致、上场记录仍被容器持有，以及子持有者和递归遍历都能找到界内 Pawn。

未在这次运行里执行的部分：`BindFieldPawn` 的成功路径。隔离 Pawn 不是地图或世界 Pawn，接口按规则拒绝。检查输出写了 `BindFieldPawn success path not run`。场上兽引用的存读是直接写入引用后做的，见下面的实体加载。

## R2 容量

改体量或缩容量之前先算上场占用。超限则返回失败，体量、容量和占用都保持原值。界内备用兽不计入上场占用。死亡只把 `dead` 设为真；已经上场的死亡兽仍然计入占用，这次没有把死亡解释成腾出容量。

内存输出：

```text
capacity reject occupy and shrink; in-realm reserve excluded; dead on-field still counts
```

具体拒绝是：容量 1 时把上场兽改成占用 2，返回失败且占用仍是 1；占用已经是 2 时把容量从 2 收成 1，返回失败且容量仍是 2；界内备用兽改成占用 2 后上场占用不变。

## R3 奖励对象

入队前检查对象。召唤师奖励的对象必须是该召唤师自己的 ID。兽奖励的对象必须是这名召唤师名下已有的兽。失败不入队，也不消耗奖励 ID。读档后的一致性检查使用同一套规则。

内存输出：

```text
reward subject rejected cross-owner and missing target; next id stayed reward-1
```

另一名召唤师的队列不能收下这只兽，缺失兽 ID 被拒绝，召唤师奖励指向别人也被拒绝。三次失败后队列仍空，下一次成功 ID 是 `reward-1`。手工改成缺失对象或挪到别人的队列后，一致性报告能看出来。

## R4 存读报告

`TryRun` 分成四段：`IN_MEMORY`、`FIELDS`、`COMPLETE_RECORDS`、`ENTITY`。四段都通过才返回真。没有 `SCRIBE PASS`。字段段只走到 `LoadingVars`，然后 `Scribe.ForceStop()`，输出标明交叉引用还没解析。完整加载必须从 `FinalizeLoading` 返回；异常会先 `ForceStop` 再让该段失败，不再把 `DeepProfiler` 的空引用当成通过。

无头进程里 `Prefs.data` 为空时，测试在调用完整加载前补一个 `PrefsData`，让 `DeepProfiler.Start` 能继续。游戏里该字段已有值，不会被替换。这是测试环境准备，不是把异常吞掉。

两次进程的结果都是 `RESULT True`。同一次运行里的输出：

```text
field bytes=2914 (LoadingVars only, cross-refs not resolved)
id counter preserved beast-6
missing fields default tier=1 lockout=-1
FIELDS PASS
complete record load 1 returned
complete record load 2 returned
COMPLETE_RECORDS PASS
entity load 1 restored master SummonerIsolatedPawn8101, field SummonerIsolatedPawn8102, realm SummonerIsolatedPawn8103, bruise=1, comp=beast-1; next id beast-3
entity load 2 restored master SummonerIsolatedPawn8101, field SummonerIsolatedPawn8102, realm SummonerIsolatedPawn8103, bruise=1, comp=beast-1; next id beast-3
BindFieldPawn success path not run: the isolated field pawn is not a map or world pawn
ENTITY PASS
```

实体段在同一次 `TryRun` 里连续完整加载两次，第二次 ID 仍是 `beast-3`，Scribe 没有停在加载模式。接着又启动了一次新进程，结果同样是 `RESULT True`。

实体检查实际对上的内容：主人引用就是加载出来的那只 Pawn，场上兽引用是 `SummonerIsolatedPawn8102` 且 `lockoutEndTick` 为 45000，界内兽是 `SummonerIsolatedPawn8103`、在容器里、`sleepEndTick` 为 90000。主人和场上兽不在灵魂界容器里。子持有者遍历和 `GetAllThingsRecursively` 都能找到界内兽。组件外键加载后是 `beast-1`。伤病是一条 `SummonerIsolatedBruise`。加载后新兽 ID 为 `beast-3`。

`AddDirect` 在把这条伤病放进列表之后还有一次空引用。伤病已经在列表里，加载后的数量仍是 1，所以这次把它算作伤病往返通过。空引用本身没有单独修掉。

宿主日志里仍有 `GenTypes` 无法扫全 `Assembly-CSharp`，随后出现 “Could not find class … Trying to use … instead”。这是无头进程的类型目录问题，Scribe 退回到了期望类型，数值才对得上。不能把它当成游戏内类型查找已经验证。

## 构建与包检查

`Tools/Build.ps1`：Release，0 警告，0 错误。

`Tools/Verify-Package.ps1`：通过。玩法 Def 数量为 0。`RimModSummoner.dll` 的引用是 mscorlib、Assembly-CSharp、System.Core。

没有运行 `Tools/Package.ps1`。`Dist/RimModSummoner-v0.2.0-dev.zip` 不是本轮产物。

## 未验证

- 游戏内调试按钮没有点。有 `Game` 时实体段会拒绝往 Def 库注入测试定义，避免污染正在进行的对局。
- `BindFieldPawn` 对真正的地图或世界 Pawn 成功绑定。
- 正式殖民档的读写。
- 玩家进程里的 `GenTypes` 类型目录。
- CE 与多人。
- 年龄追踪等 Pawn 子系统的 `PostLoadInit` 没有逐项核对。这次核对的是 ThingID、引用、容器、外键、伤病记录和冷却字段。
