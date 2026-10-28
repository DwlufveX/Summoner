# 一起开发 Summoner

当前工程版本为 0.3.0-dev，采用大型版设计，同时支持 RimWorld 1.6 和 1.7。开始工作前先看 [当前状态](docs/STATUS.md)、[玩法逻辑](docs/GAMEPLAY.md)和[实施路线](docs/ROADMAP.md)。设计需求版本不代表所有玩法已经实现。

## 第一次配置

1. 接受仓库协作邀请，用自己的 GitHub 账号克隆项目。
2. 安装 Git、.NET 10 SDK；游戏测试工具需要 PowerShell 7.4 或以上。
3. 自行准备合法的 RimWorld 游戏文件和对应版本的 Harmony。双版本验证需要分别保留 1.6 和 1.7 副本。
4. 将根目录 `.build.local.example.json` 复制为 `.build.local.json`，填写自己的两个游戏目录。这个本机配置不会提交。
5. 在项目根目录运行以下命令。Workshop 目录与默认值不同时传入自己的路径。

```powershell
Copy-Item .build.local.example.json .build.local.json
# 编辑 .build.local.json 后构建；以下 X:/... 需要换成自己的真实目录。
./Tools/Build.ps1 -GameVersion Both -Workshop 'X:/Steam/steamapps/workshop/content/294100'
./Tools/Verify-Package.ps1
```

只准备了一个游戏版本时，可以先运行 `./Tools/Build.ps1 -GameVersion 1.7 -GameRoot 'X:/Games/RimWorld-1.7' -Workshop 'X:/Steam/steamapps/workshop/content/294100'`。包检查与发布包要求两版程序集都已构建。

仓库提供源码、定义、素材和验证工具；游戏程序、Harmony DLL、SDK、编译缓存、测试副本及发布压缩包不进 Git。克隆后要自行构建，不能直接把源码目录当作已编译的发布包使用。

## 日常协作

- 每次开始前先拉取最新 main，从 main 新建自己的功能分支，不直接覆盖对方的改动。
- 一个分支完成一件事；涉及同一文件时先协调分工，减少冲突。
- 完成后提交并推送分支，发起 Pull Request，说明改动、验证结果和未完成部分，互相看过后再合并。
- 调整玩法规则时同步更新 docs/GAMEPLAY.md；更新实现范围时同步 docs/STATUS.md。
- 美术放入 Mod/Textures，原稿与来源记录放入 ArtSource；替换过渡素材时更新 Mod/Textures/ASSET_NOTES.md。

例如首次参与者可以这样开始：

```powershell
git clone https://github.com/DwlufveX/Summoner.git
cd Summoner
git switch -c feature/my-change
# 完成修改并验证后：
git add <本次修改的文件>
git commit -m "Describe the change"
git push -u origin feature/my-change
```

不要共享 GitHub 密码或令牌。每个人用自己的账号，通过仓库协作者权限访问。

## 验证

涉及游戏行为或存档时，分别验证两版。以下命令均可用 `-GameRoot`、`-Workshop` 指定本机依赖：

```powershell
./Tools/Test-DataFoundation.ps1 -GameVersion 1.6 -Mode regression
./Tools/Test-DataFoundation.ps1 -GameVersion 1.7 -Mode regression
./Tools/Test-GameArchive.ps1 -GameVersion 1.6 -TestMode Archive
./Tools/Test-GameArchive.ps1 -GameVersion 1.7 -TestMode Archive
./Tools/Test-GameArchive.ps1 -GameVersion 1.6 -TestMode Gameplay
./Tools/Test-GameArchive.ps1 -GameVersion 1.7 -TestMode Gameplay
```

完整游戏测试默认在 C:/SummonerTests 创建独立完整副本和存档，需预留空间。不要放进系统临时目录，不要用指向原游戏资源的目录链接。结果以本次实际运行的日志为准，仓库内的历史证据不能替代新版本验收。

构建和检查两版后，可以运行 `./Tools/Package.ps1` 生成 Dist 下的开发包，通过 GitHub Release 附件分享编译产物。
