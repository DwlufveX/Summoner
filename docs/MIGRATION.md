# 独立工程迁移记录

日期：2026-10-13。

- 新开发目录：`E:\Project\Summoner`，单独 Git 仓库。
- 历史目录：`E:\Project\RimMod\Summoner`，保留源码、发行包和完整测试存档；后续开发使用新目录。
- 迁移 `Source/`、`Mod/`、`Tools/`、`docs/`、`ArtSource/`，排除旧编译缓存、发行目录和游戏测试目录。
- 从 `E:\Project\RimMod\_review\summoner` 保留大型版需求、旧版需求、交付清单和骨架 ZIP 到 `docs/References/`。
- 保留 v0.1.2 最终游戏测试日志到 `docs/Evidence/v0.1.2/Player.log`；完整历史测试存档仍在旧目录。
- 修复美术重建默认来源，改为项目内源稿；构建、打包、游戏测试脚本统一支持游戏根目录和 Workshop 路径参数。
- 修复旧工程已失效的 Harmony `Current` 路径，优先解析当前安装的 `1.6/Assemblies/0Harmony.dll`，兼容旧 `Current` 布局；可用 MSBuild `HarmonyDll` 属性覆盖。
- 保留 `rimmod.summoner` 包标识、`RimModSummoner` 程序集名与现有 Def 名，游戏逻辑源码和运行资源原样迁移。

## 迁移验证

- 在新目录运行 `Tools/Package.ps1`：Release 编译成功，0 警告、0 错误；生成 `Dist/RimModSummoner-v0.1.2.zip`。
- `Verify-Package.ps1`：XML 可解析，23 个类型内唯一 Def，本地图标与灵狼四向贴图引用完整，运行目录只有项目自身 DLL。
- 对比 27 个游戏逻辑源码、运行资源及美术源文件的 SHA-256：全部与旧目录一致（工程配置文件和构建产物另计）。
- 所有 PowerShell 工具通过语法解析；`Source/` 和 `Tools/` 没有对旧 RimMod 工程目录或外部生成图像目录的引用。
- ZIP 检查：32 项，只有项目自身 DLL，不包含设计参考 ZIP、历史测试日志、源码或美术源稿。
- Git 保存初始基线，构建产物与新测试目录被忽略；当前不配置远程仓库。

本次未启动游戏或更新游戏安装副本。游戏逻辑未改动，历史行为测试只作为既有 v0.1.2 证据，大型版的新行为仍须重新验收。
