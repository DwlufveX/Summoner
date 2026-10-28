# 第一轮存档诊断修复结果

后续更新：双版本构建与真实游戏实体存读已于同日完成，见[双版本验证结果](02_DUAL_VERSION_RESULT.md)。本文保留首次诊断修复时的验证边界。

日期：2026-10-14。执行者：Codex。结论：**R4 的误报已修复，回归通过；完整游戏实体存读仍未验收。** R1–R3 修复保留，原有数据约束回归通过。

## 修改内容

| 文件 | 修改 |
|---|---|
| Source/ScribeDiagnostics.cs | 保存、加载与测试阶段临时监听 Log.Error；内部失败计入判定，结束后移除该次监听 |
| Source/DataFoundationCheck.cs | 返回成功前检查本阶段诊断；初始化与伤病设置失败不再按部分字段存在而忽略；测试伤病 ID 唯一化；运行中的游戏只执行内存约束，不注入测试 Def |
| Tests/ScribeHost/ | 把隔离测试宿主纳入源码，提供完整检查、误报回归和游戏状态保护三种模式；校验退出状态与诊断补丁清理 |
| Tools/Test-DataFoundation.ps1 | 可传入游戏、Workshop 路径，保存独立结果，测试失败会返回失败 |
| Tools/Build.ps1 | 游戏依赖缺失时明确报错，避免继续产生大量类型缺失编译错误 |
| .gitignore | 忽略测试宿主编译产物 |

正常 Mod 启动不安装诊断补丁；只在显式测试操作期间监听，使用独立补丁标识，结束时保留其他 Mod 的补丁。该监听不修改游戏保存逻辑，也不屏蔽原版记录的错误。

隔离宿主的类型扫描失败与回退到同一个类型的诊断单列为环境诊断，不作为游戏兼容证据。回退到不同类型、交叉引用失败、加载后初始化失败及其他错误会使测试失败。

## 验证结果

本轮使用保留的 1.6 游戏依赖，版本 1.6.9676.17735，路径和 SHA-256 见[双版本兼容目标](../COMPATIBILITY.md)。

| 验证 | 实际结果 |
|---|---|
| Mod Release 编译 | 0 警告、0 错误 |
| 测试宿主编译 | 0 警告、0 错误 |
| XML 与包检查 | 通过，玩法 Def 为 0 |
| PowerShell 工具语法 | 通过 |
| 方法正常返回但记录内部错误 | 两次连续回归均正确拒绝；下一操作不受旧错误影响 |
| 诊断回归模式 | 连续两次总体通过，宿主退出码 0 |
| 模拟已有 Game 的保护模式 | 内存约束通过，实体/序列化明确跳过；未注入测试 Def；宿主退出码 0 |
| 原完整实体用例 | 连续两次 ENTITY FAIL、TryRun=false，宿主退出码 1；每次加载有 9 条关键诊断 |
| 各次测试清理 | Scribe 为 Inactive，残留诊断补丁数量 0 |

完整实体用例的失败是验证门槛正确工作的负面测试，不记为完整实体存读通过。每次失败包括 6 条交叉引用失败及 3 条 Pawn 年龄追踪初始化失败，另有明确披露的测试 Pawn 组件初始化与伤病设置不完整。没有通过修改或跳过这些加载阶段取得成功。

模拟 Game 状态保护只验证入口行为，没有启动真实游戏。隔离宿主继续使用受限制的 NotifyAdded 通知补丁；它没有进入 Mod 或正常游戏。

## 可复现命令

```powershell
$game16 = 'E:/Project/RimMod/_raincheck/release-1.0.0-Vanilla-Rain/Game'
./Tools/Test-DataFoundation.ps1 -GameRoot $game16 -Mode regression
./Tools/Test-DataFoundation.ps1 -GameRoot $game16 -Mode live-guard
# 当前隔离实体环境不完整，下面的检查预期失败，结果不能用于放行游戏存读：
./Tools/Test-DataFoundation.ps1 -GameRoot $game16 -Mode full
```

本轮记录：`TestRuns/data-20261014-104255-553-regression/result.txt`、`TestRuns/r4-live-guard.txt`、`TestRuns/r4-full-current.txt` 及同目录日志。忽略目录不参与发布。

## 后续范围

误报修复与数据约束可以继续使用。真实地图/世界 Pawn 的绑定成功、完整游戏环境下的实体保存/读取、年龄与其他 Pawn 子系统初始化仍需下一阶段实测。

用户追加的兼容目标为 RimWorld 1.6 与 1.7 公测版。当前 Steam 已更新到 1.7.4977；本轮只完成 1.6 构建与隔离验证，1.7 编译和运行不能记为通过。当前未重新打包 Dist，也未安装 Mod、启动游戏或操作正式殖民档。
