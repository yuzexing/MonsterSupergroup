# 克隆、检查与故障恢复

## 环境与首次打开

1. 安装 Git 与 Git LFS，在终端确认 `git --version`、`git lfs version` 可用。
2. 使用 `ProjectSettings/ProjectVersion.txt` 指定的 Unity，当前 **6000.3.21f1**。不要用 6000.3.17f1 打开后覆盖提交中的版本、包锁或插件设置。Windows 交付使用 Windows x64、Mono。
3. 在一个新目录克隆，等待 Git 和 LFS 全部完成后再打开 Unity。普通 Git 提交记录的是 LFS 指针；DLL、图片、字体、音频等实体需要从 LFS 存储下载。见 [GitHub 的 LFS 协作说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/collaboration-with-git-large-file-storage)。

```powershell
git lfs install
git clone https://github.com/yuzexing/MonsterSupergroup.git
cd MonsterSupergroup
git lfs pull --include="" --exclude="" origin
./Tools/Invoke-ProjectTool.ps1 -ToolId validate.checkout
```

`validate.checkout` 无需启动 Unity，检查当前工作区中的已跟踪输入、有效 Assets 的文件与 meta 配对、全部 LFS 实体大小及 SHA-256、关键 FMOD 导入标记、Rewired 固定定位 GUID，以及多余的未跟踪插件。报告为 `Logs/RepositoryIntegrity/checkout.json`。这是一项输入完整性检查，不等同于编译、构建或玩法验收；它不能证明将来新增但从未提交的资源一定存在。

若命令显示 `lfs-pointer-not-downloaded`，不要在 Unity 中重新安装插件；先解决 LFS 下载。404、权限、配额及网络失败应保留原始输出，不能将失败的 clone 当作完成。检查不会自动修复或覆盖任何资产。

4. 在 Unity Hub 中选择对应编辑器版本，打开项目，等待包解析和导入完成。UPM 仍需访问 Unity 包服务及 `Packages/manifest.json` 中的 Git 包；`Packages/packages-lock.json` 锁定解析版本。当前正式场景不需要外部 HellMaiden/Nordic 导出目录。
5. 打开 `Assets/_Project/Scenes/Boot.unity`，进入 Play Mode。Unity 首页通过“游戏”建房；本机多人可使用开发入口的 KCP 建房与加入。

`Library`、`Temp`、构建包和 `Assets/StreamingAssets` 中生成的 Addressables/FMOD 输出无需提交。FMOD 输入 bank 位于已提交的 `Assets/_Project/Audio/FMODBanks`。首次构建会从正式输入生成输出，不能拿旧电脑的 Library 或 StreamingAssets 缓存掩盖缺失输入。

## 编译、构建和开局检查

以下路径替换为本机的编辑器路径。统一执行器会验证版本；不接受以旧版本运行新项目。

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId validate.all -Unity '<安装目录>/Editor/Unity.exe'
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile menu-development -Unity '<安装目录>/Editor/Unity.exe'
./Tools/Invoke-ProjectTool.ps1 -ToolId test.local-room -Profile party -Port 7777
```

`menu-development` 带验收探针，便于自动验证；无测试代码的交付配置为 `player-development` / `player-release`。图形画面、真实键盘及 Steam 真实账号邀请需要另外核验。脚本结果和原始日志存于 `Logs/ProjectTools` 及各场景日志目录。

## 当前截图的排查顺序

### Rewired 找不到 Rewired_Core.dll

该错误可能来自文件缺失、LFS 未展开、错误导入配置或定位 GUID 被重建，不能仅凭报错认定缺少程序集上传。仓库中的路径是：

`Assets/_Project/Gameplay/Combat/Rewired/Internal/Libraries/Runtime/Rewired_Core.dll`

在提交 `c9d76c48` 中，实际 DLL 大小为 **2,064,384 字节**，SHA-256 为 `d721b87d59b028f677318d6a94f8086bab4024c13f7051d5b9cdf51af5b4d053`。约 100 多字节且以 `version https://git-lfs.github.com/spec/v1` 开头的是指针，不是程序集。

`Rewired/Internal.meta` 的定位 GUID 必须保留 `8cdcfdd141b51d5458f981aaf449fd9f`。文件夹已移动到本项目目录，仍须保留原 GUID。[Rewired 官方说明](https://guavaman.com/projects/rewired/docs/KnownIssues.html)解释了其按 Internal 文件夹 GUID 查找安装位置的行为。

### FMOD 同名插件冲突

仓库允许 Windows x64 的 `platforms/win/lib/x86_64/fmodstudioL.dll` 在 Windows Editor 中使用；`platforms/uwp/lib/x64/fmodstudioL.dll` 的 Editor 开关为 0，只供 UWP 使用。两者的 DLL 和 `.meta` 均已跟踪。

截图里的两个路径与仓库一致，但错误说明该机器的实际 Editor 导入状态产生冲突。先检查提交号、两个 DLL 的 `.meta` 差异、未跟踪目录及插件重装/升级记录。不要批量勾选所有 DLL 的 Any Platform 或 Editor，也不要靠删掉整个 UWP 插件目录掩盖导入配置差异。

### 保留证据后恢复

先关闭 Unity，在出错机器的项目目录收集：

```powershell
git rev-parse HEAD
git status --short
git lfs version
git lfs ls-files
git diff -- Assets/Plugins/FMOD Assets/_Project/Gameplay/Combat/Rewired ProjectSettings/ProjectVersion.txt Packages
./Tools/Invoke-ProjectTool.ps1 -ToolId validate.checkout
```

同时保留 `%LOCALAPPDATA%/Unity/Editor/Editor.log` 的首次错误及完整栈。`ArgumentNullException(path1)`、GUIClips、平台识别错误可能是前面插件初始化失败后的连带问题；需要完整栈确认，不应分别删代码处理。

最稳妥的恢复方式是在**新目录**完整克隆并使用正确编辑器打开，将出错副本保留以便对比。若必须修复原目录，先备份本机修改：补齐 LFS，再只恢复确认被错误重建的插件 `.meta`，把多余插件移到项目外的备份目录，最后重新导入。需要重建 Library 时同样先关闭 Unity；不要删除 Assets 的 `.meta`，不要使用无差别 `git clean -fdx`。

## 提交规范

- 正式输入提交 `Assets`、嵌入式 `Packages`、`manifest.json`、`packages-lock.json` 和 `ProjectSettings`，资源与原 `.meta` 一起提交。
- 新二进制遵循 `.gitattributes` 的 LFS 规则；push 的 LFS 上传必须成功。`git status` 干净不代表另一台机器已经下载了实体文件。
- 提交前运行 `validate.checkout`；升级编辑器后从不含 Library 的副本验证 `validate.all`、构建和开局。
- 不提交或分发 Library 作为修复；不通过重装不同版本插件替代恢复仓库中的确定版本。
- 本次实际远端克隆验证结果见 [2026-09-14 检查记录](repository-checkout-validation-2026-09-14.md)。
