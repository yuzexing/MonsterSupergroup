# GitHub 干净检出验证：2026-09-14

## 结论与证据边界

验证提交：`c9d76c48ed14c0ab0210253476a5c277c4c5f935`，与开始核查时 GitHub `master` 一致。

该提交的 FMOD / Rewired DLL 及对应 `.meta` 均存在于 Git；远端 LFS 实体可下载，干净检出可在 Unity 6000.3.21f1 中编译、构建并通过本机三进程开局测试。**本轮没有发现需要补传的运行必需文件。**

使用同一提交进行 6000.3.17f1 的空 Library 对照，导入、编译及正式资源校验也成功，没有复现截图中的插件错误。因此版本不一致需要纠正，但不能据此认定它就是本次 FMOD / Rewired 错误的原因。17f1 对照只用于诊断；它改写了副本的版本文件和包锁，主项目仍使用 21f1，之前定位的 Windows 退出崩溃修复也继续依赖 21f1。

截图能确定的是：发生冲突的两个 FMOD DLL 被判为可供 Editor 使用；Rewired 的编辑器安装定位/导入链未成功找到核心 DLL。究竟是另一台机器使用不同提交、LFS 未完成、插件目录被重装/移动，还是 `.meta` 被重新生成，需要该机器的实际文件与 Editor.log 才能进一步区分。本机通过不能冒充已在故障机器完成修复。

截图中的 UWP 路径为 `Assets/Plugins/FMOD/platforms/uwp/lib/x64/fmodstudioL.dll`，与仓库一致；该提交的 Editor 标记明确为 0，需要核对故障机器的实际导入状态。排查早期误读为 `x86_64`，重新查看原图后已纠正，不将“路径不同”作为诊断证据。

## 验证方式与结果

| 检查 | 结果 |
| --- | --- |
| GitHub 新 clone | 成功；未复用原工作区 Git/LFS 缓存 |
| LFS 下载与实体哈希 | 1,926 个文件，约 1,016.59 MiB；全部大小、SHA-256 一致 |
| Git 对象检查 | `git fsck --no-reflogs` 成功 |
| 有效 Assets 文件与 meta | 检查 6,363 个导入文件，无漏交 meta；按 Unity 规则排除隐藏文件和 bundle 内部文件 |
| 21f1 空 Library 首次导入、编译、validate.all | 成功，进程退出码 0；含包解析约 464 秒 |
| 17f1 空 Library 对照 | 编译、validate.all 成功，进程退出码 0；不作为发布版本验收 |
| 截图中的七类错误关键词 | 两个版本导入日志均为 0：FMOD 同名、Rewired DLL 缺失、平台识别失败、PluginImporter 缺失、ArgumentNullException、GUIClips、C# 编译错误 |
| 21f1 menu-development 完整构建 | 成功，退出码 0，约 140 秒；无原包或 Player 构建缓存 |
| KCP Host + A + B，端口 7988 | `test.local-room -Profile party` 成功，三进程均 PASS 且退出码 0 |
| 联机测试内容 | 三个唯一席位、选择/准备/开局、实际移动/攻击、连续重开、回大厅再开局、倒地重连、成员离开；日志记录服务端接受的怪物移动帧 |
| 21f1 副本源文件变化 | 导入与构建后 `git status --short` 为空；没有靠修改玩法/插件/翻译资产通过 |
| 新增检出检查工具 | 9 项通过：正常检出、统一入口自定义报告、LFS 指针未展开、同大小内容损坏、meta 缺失、UWP 错误启用 Editor、Any Platform 错误、Rewired GUID 错误、多余插件 |
| 真实键盘与窗口复测 | 已启动新包，Windows 安全中心/防火墙弹窗尚待用户手动处理；未对该弹窗执行操作，也未将窗口核验计为通过 |

这不是所有历史测试与所有第三方样例的全面验收；非 Development 新构建、跨机器网络及 Steam 真账号邀请不在本次复测范围。

首次包解析曾记录 `packages.unity.com` 的 `ECONNRESET`，后续解析成功：21f1 约 330 秒、17f1 约 390 秒。网络下载失败与插件导入配置错误应分别诊断，不能通过提交 Library 解决。

一次 PowerShell 构建入口因路径前缀相同，把另一份正在运行的 17f1 对照目录误判为当前项目已打开；该次在构建前被阻止，不计构建通过。确认两个目录独立后，使用相同 `ProjectToolRunner.Batch` / `ProjectBuildService` 和相同 `menu-development` 配置显式指定 21f1 副本，实际构建成功。未绕过资源校验或修改构建实现。

## 实际文件与记录

- 21f1 独立 clone：`F:/UnityStore/MonsterSupergroup_clean_git_20260914`。
- 17f1 对照：`F:/UnityStore/MonsterSupergroup_clean_git_20260914_unity17`，从本次远端 clone 的相同提交创建独立 worktree，共用这次下载的 Git/LFS 对象，但没有 Library。
- 新验收包：21f1 clone 下 `Builds/MenuDevelopment/MonsterSupergroup.exe`。
- 原工作区汇总：`Logs/RepositoryIntegrity/` 中的 `remote-checkout.json`、`version-comparison.json`、`import21-result.json`、`import17-result.json`、`build-result.json`、`local-party-result.json`。
- 21f1 完整导入日志：clone 下 `Logs/ProjectTools/20260914-121325-928-validate.all/unity.log`。
- 17f1 完整导入日志：对照目录下 `Logs/VersionComparison/unity.log`。
- 完整构建日志：clone 下 `Logs/CleanCheckoutBuild/unity.log`。
- 三进程日志：clone 下 `Logs/PreparationMenu/20260914-122506-local-party-p7988/`。

本轮新增检查脚本和说明只修改当前工作区工具/文档，不是上述远端提交通过验证的前提。未自动提交或推送。换电脑的直接操作步骤见 [克隆、检查与故障恢复](project-checkout.md)。

真实窗口运行前保存了用户原画面设置，临时使用 1280×720 窗口模式；等待弹窗处理期间已恢复注册表中的原始设置字节。游戏窗口继续保留待核验，进程结束后还需核对退出结果。等待状态见原工作区 `Logs/RepositoryIntegrity/work-state.json`，不把这一待完成阶段混入已通过的三进程测试。
