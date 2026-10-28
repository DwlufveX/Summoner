# 第一轮返修复查结果

日期：2026-10-14。结论：**R1、R2、R3 的修复确认；R4 仍需修正测试判定。** 当前数据代码可以保留，完整实体存读验收尚未通过。

## 修复确认

| 项目 | 复查结果 |
|---|---|
| R1 同一兽改绑不同实体 | `SameBoundPawn` 在写入前拒绝替换；界内条目与记录增加双向一致性检查。隔离宿主的绑定与遍历回归通过 |
| R2 容量与体量变更 | 当前 DLL 的独立回归通过：超限修改返回 false，旧容量与体量保留 |
| R3 奖励对象归属 | 当前 DLL 的独立回归通过：跨主人奖励被拒绝；失败不消耗 ID，下一份有效奖励仍为 reward-1 |
| 子持有者遍历 | `SoulRealmEntry.GetChildHolders` 已调用原版追加持有者方法，宿主能遍历到界内 Pawn |

Release 构建成功，0 警告、0 错误；包检查通过。没有修改运行代码，没有启动游戏或安装 Mod。

## R4 仍有误报

`LoadEntity` 现在确实调用 `FinalizeLoading`，也不再吞掉外层异常。但游戏的交叉引用和加载后初始化器可能在内部捕获异常、记录 `Log.Error` 后继续返回。仅检查函数返回、Scribe 为 Inactive 和部分字段相等，仍不足以证明完整加载成功。

使用刚构建的 Mod DLL，重新构建并运行 `TestRuns/ScribeHost`，实际结果如下：

| 项目 | 结果 |
|---|---:|
| 宿主退出码 | 0 |
| 原始测试输出 | RESULT True、ENTITY PASS |
| Could not resolve cross refs | 12 条 |
| Could not do PostLoadInit | 6 条 |

具体诊断包含 `Verse.Thing.ExposeData`、`Verse.HediffSet.CacheMissingPartsCommonAncestors` 的空引用，以及 `Verse.Pawn_AgeTracker` 加载后初始化失败。每轮实体加载都有这些失败，不能将整体标为通过。

以上错误发生在使用不完整测试 Def 和 Harmony 通知补丁的隔离宿主中，**不据此判定游戏内数据实现已经损坏**。已经确认的问题是测试判定误报成功；实体加载应标为部分验证、未通过或环境阻塞。

本次原始输出位于 `TestRuns/review-20261014-host.txt`，逐条诊断位于 `TestRuns/scribe-log.txt`。两者在忽略的测试目录中，不参与运行包。首次关键诊断分别位于后者第 138 行和第 180 行。

## 待完成的收尾

仅修正 R4 的判定与验证方式，不重做 R1–R3。见[存档验证收尾任务](01_SCRIBE_VALIDATION_FIX.md)。

下一阶段仍需真实地图/世界 Pawn 的 `BindFieldPawn` 成功路径和完整游戏环境下的保存/读取证据；当前宿主直接设置在场记录，因此没有覆盖该接口成功路径。
