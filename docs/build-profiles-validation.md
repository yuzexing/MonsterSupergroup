# Build Profiles 接续验收记录

## 2026-09-23 接手核对

计划：[BuildProfiles.md](BuildProfiles.md)。本记录区分代码存在、历史证据和本轮执行结果；当前尚无本轮真实 Player 构建成功结论。

### 工作目录与已有改动

- 当前实际目录／Git 根目录：`F:\UnityStore\MonsterSupergroup`；分支 `master`；HEAD `cb276527a3be1e3789fae05bf5a714d641a7aa0d`；Git 目录 `.git`。
- 原任务“规划原生 Build Profile 构建迁移”（`01a0cd0c-dd5e-72c0-a517-572c4a9b82ac`）的任务元数据和构建命令均指向此目录，因此本次位于原任务主工作区。另一处 `F:\UnityStore\MonsterSupergroup_validation_exit_6000_3_21` 是 detached worktree，HEAD `9cacc56dd4ea86c95f989749623cec421a161036`，本轮未使用。
- 已检查 `git status --short`、`git diff` 和 `git diff --cached`：暂存区为空；未提交内容同时包含 Build Profiles 迁移、战斗取证及其他既有改动。未执行 reset、clean、切分支、暂存、提交或大规模重构。
- 已读原计划、构建指南、迁移清单、原任务中断记录及 `Logs/BuildProfilesImplementation`。构建指南原先链接的本验收记录尚不存在，本轮补建；旧 `build-unification-validation.md` 不作为本轮证明。
- 原任务中保留了用户经诊断任务传达的“暂缓 Unity 测试、构建和高负载验证，先验证诊断”安排。本轮没有启动 Unity，也未改动 `Logs/CombatEvidenceNextValidation` 的冻结输入。

### 状态分类

| 分类 | 核对结果 | 证据与边界 |
|---|---|---|
| 已经实现 | 业务子资产、7 种用途规则、符号准备与原生设置适配；目录中现有 11 个规范模板，旧 `Window-dev`／`Window-test` 不在当前目录 | 已阅读 `MonsterBuildSettings`、`NativeBuildProfileSettings`、`ProjectBuildResolver`、`ProjectBuildTemplates` 及模板清单；仅确认落盘，不代表本轮验证了重启持久化与设置效果 |
| 已经实现 | 原生按钮、自定义窗口、命令行已接入共享服务；Profile GUID 结果指针、旧入口迁移错误已有实现 | 已核对入口、Service／Identity／Guard、ToolRunner、PowerShell 差异；三个入口的真实行为一致性未在本轮验证 |
| 已经实现 | 唯一暂存目录、外层清理、TMP 快照恢复、包能力校验、失败隔离、成功发布及完成后运行已有代码 | 代码存在不等于所有失败路径都正确；此前实际 KCP 构建未发布 |
| 已经验证（本轮） | PowerShell 结果读取与参数回归；自动选择 schema／摘要拒绝，显式历史包选择保留 | 两种 PowerShell 各 44 项通过，见下方命令与日志。测试 EXE 是合成文件，只验证选择逻辑，未启动 Player |
| 尚未完成 | 当前 EditorTools 全套重新通过；一次成功的原生 Profile KCP CLI 包；随后三个入口一致性及产品 Test／Evidence／Profiler／Wisp／专项 Test 代表包 | 不能沿用旧报告中的单项通过数作为当前工作区通过；Shipping 成功还要求包含改造的干净提交 |
| 尚未完成 | 原生覆盖／继承效果、宏切换与域重载、Editor 取证行为、真实 Player 与篡改 BuildInfo、取消／回调异常／非空 TMP／恢复及发布失败／启动失败矩阵 | 本轮未执行 Unity 测试或真实构建；Steam 双账号体验仍为人工验收 |
| 无法确认 | KCP 构建期间 `ProjectSettings.asset` 究竟哪一项变化，是否与 Unity／插件序列化或回调相关 | 历史记录只有文件摘要，没有失败时原始文件内容；不能认定为无害变化并排除该文件 |
| 无法确认 | 当前 C# 修订是否全部通过编译和 Unity 测试；TMP 非空缓存及内存恢复是否可靠 | 旧测试之后已有源码修改，且旧构建日志含 TMP 重导入一致性警告；本轮未重跑 |

### 复核到的历史证据（未重新执行）

- `Logs/BuildProfilesImplementation/editmode.xml`：2026-09-23 07:37:31–07:37:55 UTC，70 项，69 通过、1 失败。失败用例 `BuildIdentityTests.ScriptsOnlyFailsBeforeAnyBuildOutput` 期待 `InvalidOperationException`，实际为迁移入口的 `BuildFailedException`。当前源码已经由原任务修改了预期异常，但没有对应的新 Unity 测试结果。
- `Logs/BuildProfilesImplementation/kcp-cli.json` 和 `kcp-cli-2.json` 均为 `success=false`，原因是构建期间配置／工程输入变化；不能作为成功包。
- 第二次失败目录：`Builds/ProfileValidation/KcpCli/.failed/MonsterSupergroup-v0.0.1-dev-20260923T074454804Z-85f2791f`。本轮比较其中 `build-plan.json` 和 `build-plan-after.json`：`contentHash` 相同，依赖摘要无差异，`inputHash` 不同；输入列表仅 `ProjectSettings/ProjectSettings.asset` 变化：`3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59` → `64027d29de779234b5c861a3b70a415605c9e87b6759d7a09c7e7d69c92629af`。
- 本轮读取的 `ProjectSettings.asset` 字节 SHA-256 又与前值相同。换行归一化及有限字段候选检查没有复现后值；不据此推断具体原因。比较结果保存为本轮日志目录的 `historical-build-audit.json`。

### 本轮唯一推进项：自动结果选择的格式边界

对应原计划第二阶段“按 Profile GUID 读取新结果，保留显式历史包”。鉴于 Unity 实测仍暂缓，本轮只关闭可用独立 PowerShell 回归验收的这一处缺口。

原实现比较指针与包内摘要是否相等，但未要求 schema 3 或摘要本身有效；同步缺失／损坏的元数据可能通过相等比较。新增用例首先证实缺少 schema 的包仍被自动选中，随后作定点修复：

- `Tools/ProjectTools.psm1`：自动结果读取要求整数 schema 3，`contentHash`／`inputHash` 必须为完整 64 位十六进制字符串，继续沿用既有身份一致性检查。
- `Tools/Tests/Test-ProjectBuildResolution.ps1`：增加 18 项检查，覆盖缺失／旧版／未知／字符串 schema、两个摘要同步为空／长度错误／非法字符／尾部换行／数字，以及显式选择 schema 2 历史 EXE 和目录。修正测试读取中文 catalog 时未指定 UTF-8，避免 Windows PowerShell 5.1 按系统编码误读。
- `docs/BuildProfiles.md`：追加接续进度；新增本记录，补齐指南已有的验收记录链接。

本轮未改 Unity C#、Profile、正式资源、版本号或战斗取证逻辑；没有尝试忽略输入摘要变化或发布旧失败产物。

### 本轮实际验证

证据目录：`Logs/BuildProfilesResume/20260923-165233-517/`（本机日志目录，被 Git 忽略）。含修改前副本、继承差异、失败／成功输出、历史快照比较和本轮差异；文件哈希见 `verification.json`。

| 执行 | 结果 | 日志／说明 |
|---|---|---|
| 修改前执行 `pwsh -File Tools/Tests/Test-ProjectBuildResolution.ps1` | 26 项通过 | 本轮命令输出；合成夹具 `Logs/ProjectTools/NativeBuildResolution-20260923-165208-716` |
| 新增用例、尚未修复读取器时执行同一脚本 | 退出 1，复现缺失 schema 被接受 | `regression-before-fix.txt`，`Expected rejection: automatic selection requires native schema 3` |
| 修复后 PowerShell 7.6.5 执行同一脚本 | 44 项通过，退出 0 | `regression-pwsh-final.txt` |
| Windows PowerShell 5.1.22000.2538 首次执行 | 退出 1，测试读取中文 catalog 编码错误 | `regression-windows-powershell.txt`；未把该次计为通过 |
| 测试显式使用 UTF-8 后，`powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Test-ProjectBuildResolution.ps1` | 44 项通过，退出 0 | `regression-windows-powershell-final.txt`；之后重跑 PowerShell 7 最终版本同样通过 |
| 两个 PowerShell 文件语法解析、本轮增量 diff 核对 | 无语法错误；仅结果检查、测试及 UTF-8 读取调整 | 相对于接手时副本检查，没有把继承差异归为本轮改动 |

### 剩余问题与下一步

1. 恢复 Unity 实测后，先保存 `ProjectSettings.asset` 在构建前、构建返回和清理后的原始字节及时间点；定位历史输入漂移的具体字段和回调。继续拒绝真实输入变化，不能通过跳过整个文件或直接覆盖当前设置来消除失败。
2. 对该单一原因完成最小修复后，重跑当前 EditorTools EditMode 测试及一次 Windows-Dev-Kcp CLI 构建，核对成功目录、身份、实际编译能力与结果指针。当前记录不宣称这些已完成。
3. 此单包门槛通过后，再按原计划补原生／自定义入口一致性、非空 TMP 和失败事务、其他代表包。未授权自动提交，Shipping 干净提交条件保持不变。

## 2026-09-23 输入漂移诊断准备

本次按用户确认的“补齐漂移诊断”计划实施，范围止于采集组件、共享服务接入和离线回归。仍在原 `master` 主工作区，HEAD 未变，暂存区为空；没有启动 Unity、提交、修改正式设置或扩大代表包验收。

### 实现与证据读取方式

新增纯 .NET `ProjectBuildInputEvidence`，仅监听 `ProjectSettings/ProjectSettings.asset`。每次进入原生 Profile 共享构建服务时创建 `Logs/BuildProfilesResume/input-<UTC>-<GUID>/`，保存 Profile GUID／路径；生成构建身份后绑定本次 BuildId，提前拒绝的尝试不伪造 BuildId。

| 阶段 | 采集位置／语义 |
|---|---|
| `initial-plan` | 初始计划计算输入摘要时，直接接收该次 `ReadAllBytes` 的字节 |
| `before-unity` | 只读校验及构建准备完成、即将调用 `BuildPipeline.BuildPlayer` |
| `after-unity` | 调用的 `finally`，覆盖正常返回和抛错 |
| `after-identity-cleanup` | 身份清理尝试结束后，清理异常仍由原错误集合处理 |
| `after-tmp-restore` | TMP 事务恢复尝试结束后；没有创建事务时保持未执行 |
| `final-plan` | 原流程确实执行最终计划解析时，直接接收该次摘要使用的字节；此前失败使解析跳过时保持未执行 |

每个已捕获阶段写入 `<阶段>.asset` 原始字节，使用独占新建，保留原编码／BOM／换行。`reports/NNNN-manifest.json` 与对应 `diff.txt` 是不可覆盖的阶段报告，状态为 `Collecting`。采集器结束时只写一次根目录 `manifest.json` 和 `diff.txt`；进程意外中断或最终报告写入失败时保留已有阶段报告，不能据此宣布证据齐全。

最终清单含时间、文件长度、SHA-256、阶段状态、相邻／首尾比较和最早已观察到的差异区间。阶段状态分为 `Captured`、`Missing`、`ReadFailed`、`WriteFailed`、`NotReached`。比较缺少任一快照时为 `Unknown`，不等同于无变化。全部阶段捕获且没有采集／报告错误才是 `Complete`，其余为 `Incomplete`；**Complete 仅表示文件证据齐全，不表示构建成功或没有漂移**。

差异报告使用带行号和上下文的文本片段；换行或带 BOM 编码造成的同文异字节标为 `SameTextDifferentBytes`，无法严格解码的差异标为 `Undecodable`，均保留原始字节。中途改变后恢复仍留下两段变化及中间快照。只能据此定位变化区间，不能直接指认某个回调为根因。

公开 `Build`／`Resolve`／`InputFiles` 签名、BuildInfo schema、结果指针和摘要算法保持原样，新增内部重载传递采集上下文。普通 Resolver／预览不创建采集器。诊断 I/O 错误独立记录并报告；报告回调抛错也被隔离，不替换原构建异常或进入构建错误集合，不跳过原有清理，不改变成功发布条件。

### 本轮实际验证

证据目录：`Logs/BuildProfilesResume/input-diagnostics-20260923-171954-158/`。修改前副本／哈希、最终源码副本及增量 patch 均保存在此目录。离线运行编译实际生产组件的冻结副本，使用本机 .NET SDK 9.0.313、C# 9，不加载 Unity，也没有 NuGet 包依赖。

重跑入口：

```powershell
./Tools/Tests/Test-BuildInputEvidence.ps1
./Tools/Tests/Test-ProjectBuildResolution.ps1
```

第一个脚本默认生成全新日志目录；显式 `-OutputDirectory` 必须指定不存在的目录。`execution.json` 保存准确命令、SDK 版本、退出码和受测文件前后 SHA；`Fixtures With Spaces/results.json` 保存用例与断言统计。夹具项目及其中的原始快照都是合成输入，不是真实 Unity 构建证据。

| 执行／证据子目录 | 实际结果 |
|---|---|
| `offline-01` | 18/19，退出 1；快照写失败用例未读到最终报告状态 |
| `offline-02` | 17/19，退出 1；进一步记录到 Windows 替换旧报告文件失败，导致旧报告仍停留在 Collecting；未把这两轮算通过 |
| `offline-03` | 阶段报告改为不可覆盖的新文件后，19/19，300 个断言，退出 0 |
| `offline-final` | 补验中断前报告留存后，**20/20，306 个断言，退出 0**；受测源码前后 SHA 一致 |
| `resolution-pwsh.txt` | PowerShell 7：既有结果选择回归 **44 项通过** |
| `resolution-windows-powershell.txt` | Windows PowerShell 5.1：同一回归 **44 项通过** |

离线覆盖：原文件不变、字段变化、改变后恢复、CRLF／UTF-8 BOM／UTF-16／UTF-32 字节差异、源文件缺失与独占锁读取失败、快照／报告／目录写入失败、未执行阶段、不可覆盖的中间报告、含空格路径与多次尝试隔离、同一次哈希读取的字节、非目标输入忽略、无法解码的字节、重复阶段保护，以及诊断写入／日志回调失败不替换原始异常且模拟清理序列继续。对已捕获原始文件重新计算长度和 SHA，与清单逐项核对。

Unity 接入仅做静态核对：确认两个计划采集点沿用同一字节缓冲，调用后的采集处于 `finally`，两项清理后的采集不替代既有 `Try`，预览路径不传入采集器；输入文件枚举、排序及 SHA 拼接规则未改变。上述 .NET 测试没有执行实际 Unity 服务、回调或 TMP 恢复。

### 当前结论与下一步

- **已实现／离线已验证**：可还原合成夹具的阶段变化，区分字节未变、同文异字节及证据不完整。
- **尚未经过 Unity 编译及生命周期实测**：原生服务接入、真实回调时序、TMP 恢复。历史 `ProjectSettings.asset` 漂移原因仍无法确认；本轮没有复现或发布 KCP 包。
- 当前正式 `ProjectSettings.asset` SHA-256 仍为 `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`。既有结果读取模块和 44 项测试源码未改；保护核对见本轮 `verification.json`。
- 本任务到此停止扩展。恢复 Unity 实测后，先确认编译和采集接入，再用 Windows-Dev-Kcp CLI 复现一次，对照原始字节、阶段报告与 Unity 日志；只有获得具体字段变化后，才实施对应原因的定点修复。暂不扩展三个入口或其他代表包。

## 2026-09-23 Unity 编译与测试门槛实测

本轮执行用户确认的“Unity 编译与 KCP 构建生命周期复现”计划。Unity 暂缓安排已解除；计划明确要求其他测试失败时先交付定位，停止后续构建。本轮在这一门槛阻塞，**复现任务尚未完成**，不能以编译通过或测试阶段文件未变代替生命周期验收。

### 本轮基线及保护

- 原主工作区 `F:\UnityStore\MonsterSupergroup`，分支 `master`，HEAD `4e473e28f483105426a3df532a560aa13c622ad6`，Git 目录 `.git`。未使用另一个 detached worktree，暂存区开始及结束均为空。
- 开始时仅有 5 项既有诊断改动：`Tools/CombatEvidenceValidation.py`、`Tools/Invoke-CombatEvidenceValidation.ps1`、`Tools/test_combat_evidence_validation.py`、`docs/combat-evidence-next-validation.md`、`docs/diagnostic.md`。完整继承差异保存为 `inherited.patch`，这些文件未被本轮覆盖或修改。
- 上轮 `input-diagnostics-20260923-171954-158/verification.json` 的 9 项文件哈希逐项匹配。本轮另冻结 91 个文件及副本，涵盖 EditorTools 源码／测试、构建身份、Profile、设置、相关工具及记录。Unity 退出后 91 项哈希均与启动前一致；文档追加是此核对之后的本轮增量。
- Profile：`Assets/Settings/Build Profiles/Windows-Dev-Kcp.asset`，GUID `b441d96b1c4f7a3469a6a2f3fc2fb88e`，Dev／Kcp／Direct／Normal。开始时没有 Unity 或诊断负载进程；本轮只启动一个 Unity Editor 进程，隐藏窗口、串行、1800 秒超时。

证据根目录：[unity-lifecycle-20260923-202642-841](../Logs/BuildProfilesResume/unity-lifecycle-20260923-202642-841)。`baseline.json`、`before-hashes.json`、`prior-source-match.json`、`before/` 和 Git 差异保存启动基线；日志被 Git 忽略，当前交付只修改两份进度文档，不提交。

### 实际命令与进程结果

通过归档的 `run-editmode.ps1` 调用现有 `Start-ProjectProcess`，实际 Unity 参数如下；未使用 `test.editor-tools` 包装脚本，没有 `-quit`：

```powershell
& 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -batchmode -nographics `
  -projectPath 'F:\UnityStore\MonsterSupergroup' `
  -activeBuildProfile 'Assets/Settings/Build Profiles/Windows-Dev-Kcp.asset' `
  -runTests -testPlatform EditMode `
  -testFilter MonsterSupergroup.EditorTools.Tests `
  -testResults 'F:\UnityStore\MonsterSupergroup\Logs\BuildProfilesResume\unity-lifecycle-20260923-202642-841/editmode.xml' `
  -logFile 'F:\UnityStore\MonsterSupergroup\Logs\BuildProfilesResume\unity-lifecycle-20260923-202642-841/editmode-unity.log'
```

准确参数数组、启动器和真实进程结果以 `editmode-command.json`、`run-editmode.ps1`、`editmode-execution.json`、`editmode-processes.jsonl` 为准。

| 记录 | 本轮实际值 |
|---|---|
| Unity | 6000.3.21f1，文件版本 6000.3.21.12592689 |
| PID | 23272 |
| 进程开始 UTC | 2026-09-23T12:27:13.3788735Z |
| 进程结束 UTC | 2026-09-23T12:28:08.7973840Z |
| Unity 真实退出码 | **2**，日志同时记录 `Test run completed. Exiting with code 2 (Failed)` |
| 超时／强制清理 | 未超时，`CleanupRequested=false`，退出后没有残留 Unity 进程 |
| XML | 实际 46 项，45 通过、1 失败、0 跳过、0 无法判定；测试区间 32.4749194 秒 |

执行命令 `pwsh -NoProfile -File Logs/BuildProfilesResume/unity-lifecycle-20260923-202642-841/audit-results.ps1` 独立核对 XML、日志、受测文件、编译响应文件及外部快照，退出 0。这个 0 只表示分析完成，不能覆盖 Unity 的退出码 2。

### 编译、测试与失败定位

`editmode-unity.log` 第 312、323 行记录 EditorTools 与测试程序集的实际 Csc 编译，第 527 行记录本次脚本编译时间；没有 `error CS`。`compiled/MonsterSupergroup.EditorTools.rsp` 明确包含 `ProjectBuildInputEvidence.cs` 及所需序列化程序集引用。对应 DLL／PDB／响应文件副本和哈希保存在 `compiled/`、`compiled-hashes.json`。因此**本轮 Unity 编译兼容性已验证**；采集器在真实构建生命周期中的执行仍未验证。

| XML 测试类 | 执行 | 通过 | 失败 |
|---|---:|---:|---:|
| `BuildIdentityTests` | 23 | 22 | 1 |
| `BuildRecipeTests` | 15 | 15 | 0 |
| `ProjectToolTests` | 8 | 8 | 0 |
| 合计 | **46** | **45** | **1** |

唯一失败是 `BuildIdentityTests.ApplyUsesLiveVersionAndIsIndependentOfBuild`。该用例临时设置版本，调用 `ProjectBuildWindow.ApplyVersion`，最后恢复版本并保存。首条失败调用栈指向 `NativeBuildProfileSettings.UpdateVersion` 第 57 行 `AssetDatabase.SaveAssets()`；XML 将未预期的 Unity Error 日志判为失败：

```text
File ProjectSettings/ProjectSettings.asset couldn't be written.
Because moving F:/UnityStore/MonsterSupergroup/Temp/UnityTempFile-0654d76c8a45da449a50d35e2941ffea
to F:/UnityStore/MonsterSupergroup/ProjectSettings/ProjectSettings.asset failed.
```

日志第 727、777、832、880 行分别记录四个临时文件替换失败，调用栈覆盖版本保存及测试清理保存；原始日志、XML、`test-audit.json` 与 `log-excerpts.txt` 保留详情。失败路径没有调用输入采集组件，也没有创建新的 `input-*` 证据目录。本轮没有修改 C#、测试断言、构建参数、摘要、恢复或发布规则，也没有重复运行来绕过该失败。

事实与未确认部分分开记录：

- **事实**：Unity 在版本写入／保存路径报告替换失败。测试结束后源文件属性不是 ReadOnly；读取到的文件／目录 ACL 含 Modify 和 FullControl 授权，原始 SDDL 已归档。
- **事实**：`editmode-before.asset`、`editmode-after.asset` 与测试结束后的源文件 SHA-256 均为 `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`，字节长度及哈希见 `io-and-input-audit.json`。正式 Profile 与全部冻结输入也未改变。
- **无法确认**：日志未提供底层 Win32 错误码，未捕获失败时的句柄占用和有效访问状态，故不能把文件锁、权限、安全软件或 Unity 内部行为中的任何一项认定为根因；退出后 ACL 可读不能证明失败时替换应当成功。
- **不能推断**：只有测试前后快照，没有版本测试中间快照；不据此宣称整个测试期间未发生变化，也不将此次写入错误等同于历史 KCP 构建输入漂移。

### 三项结论与停止边界

| 验收维度 | 本轮结论 |
|---|---|
| 编译／测试 | 编译通过；完整测试 **45/46，失败**，Unity 退出 2，构建门槛未通过 |
| 生命周期采集完整性 | **未执行／未完成**。没有本轮 BuildId、六阶段构建快照、初始／最终构建计划；不适用 `Complete`，不以旧证据补齐 |
| 构建交付 | **未尝试／未完成**。按计划停止 KCP CLI 调用；没有本轮交付包，也没有“构建被正确拒绝”的验收结论；未启动 Player |

本轮未重跑离线 20/20 或两种 PowerShell 的 44 项结果选择回归，因为受测实现未修改；这些仍是上轮证据，不写作本轮通过。尚未进入真实构建，当前字体状态的正常 TMP 清理路径也未在本轮执行，更不代表完整非空 TMP／故障注入矩阵已验收。其他入口和代表包保持后续范围。

### 下一项

下一项先取得 `ProjectSettings.asset` 临时文件替换失败的系统 I/O 结果和占用／权限证据，区分环境限制与 Unity 保存路径问题，再依据证据做最小处理；保留失败日志断言，不静默忽略保存失败，也不直接覆盖正式设置来制造通过。解除阻塞后，重跑完整 EditorTools EditMode，要求所有用例执行通过且进程退出 0；之后才恢复既定的一次 Windows-Dev-Kcp CLI 构建、外部启动前／退出后快照和内部六阶段核验。

历史漂移字段与原因仍无法确认。后续取得真实阶段变化后再制定定点修复；本轮到失败定位和记录交付为止，不扩展根因修复或代表包验收。

## 2026-09-23 文件替换探针与失败单测复现

执行随后确认的“定位文件替换失败，再完成一次 KCP 生命周期复现”计划。新档案：[file-replace-20260923-210518-132](../Logs/BuildProfilesResume/file-replace-20260923-210518-132)。仍为原主工作区 `master / 4e473e28`，暂存区为空；开始时已有 7 项改动，包括前一轮追加的两份 Build Profile 记录，本轮增量不把这些继承差异归为新改动。

### 隔离文件探针：实际完成的部分

`probe-files.ps1` 通过 P/Invoke 调用 `CreateFileW(CREATE_NEW)`、`MoveFileExW`（不覆盖／覆盖）及 `ReplaceFileW`（保留备份、flags=0），在每次失败后立即保存 `GetLastWin32Error`。不根据翻译后的错误文本推断错误码。

探针位置为本轮日志内的含空格目录和 `ProjectSettings/BuildProfilesIoProbe-<唯一标识>` 子目录。替换目标来自正式设置的字节副本，替换来源添加独立探针标记；原正式文件没有被试探性覆盖或删除。分别采用继承 ACL 和复制源文件 ACL，记录实际 ACL、属性、路径、时间、操作返回值、前后长度及 SHA。

下表每行包含两个位置 × 两种 ACL，共 4 组；总计 **52 组操作，28 次原生成功、24 次原生拒绝**。这里的成功／拒绝是操作返回值，不是“52 项产品测试通过率”。

| 操作 | 关闭全部句柄 | 允许删除共享的读句柄 | 禁止删除共享的读句柄 | 只读副本 |
|---|---|---|---|---|
| 新建 | 4 次成功 | 不适用 | 不适用 | 不适用 |
| 改名到不存在的目标 | 4 次成功 | 4 次成功 | 4 次拒绝，Win32 **32** | 4 次成功 |
| 覆盖移动 | 4 次成功 | 4 次拒绝，Win32 **5** | 4 次拒绝，Win32 **5** | 4 次拒绝，Win32 **5** |
| 替换并保留备份 | 4 次成功 | 4 次成功 | 4 次拒绝，Win32 **32** | 4 次拒绝，Win32 **5** |

所有 16 组关闭句柄的基本操作成功。允许删除共享时，覆盖移动与 `ReplaceFileW` 的表现不同，这只是本机受控探针事实；Unity 日志没有说明其底层 API、访问参数或实际错误码，不能据此将业务保存替换成另一 API。普通 ACL、源文件 ACL、源字节副本也不能完整代表正式文件的运行时状态。

探针结果见 `probe-results.json` 和每组 `probe-cases/*/result.json`；160 份前后原始文件快照的长度和 SHA 经 `analysis-02/probe-snapshot-audit.json` 逐项复算一致。全部探针句柄已关闭，两个本轮临时目录已通过绝对路径边界核对后清理，清理记录均成功；随后才启动 Unity。

### 单测与系统跟踪的实际结果

执行命令保存在归档脚本和 `focused-01/command.json`：

```powershell
pwsh -NoProfile -File Logs/BuildProfilesResume/file-replace-20260923-210518-132/probe-files.ps1
pwsh -NoProfile -File Logs/BuildProfilesResume/file-replace-20260923-210518-132/run-validation.ps1 -Label focused-01 -Filter MonsterSupergroup.EditorTools.Tests.BuildIdentityTests.ApplyUsesLiveVersionAndIsIndependentOfBuild -Trace
pwsh -NoProfile -File Logs/BuildProfilesResume/file-replace-20260923-210518-132/analyze.ps1
```

Unity 参数继续使用 6000.3.21f1、Windows-Dev-Kcp、EditMode、隐藏窗口、1800 秒超时，没有 `-quit`。本轮只有这一次单测 Unity 进程，没有完整套件重跑或构建。

| 项目 | 实际结果 |
|---|---|
| 探针脚本 | 退出 0，52 组结果齐全，无未处理探针异常，临时输入清理成功 |
| WPR 启动 | `-start FileIO -start Minifilter -filemode -recordtempto <本轮目录>`；退出 **-984068079**，错误 **0xc5585011**：`Failed to enable the policy to profile system performance.` |
| WPR 工件 | 未开始记录、无 ETL；启动前／单测结束后 `wpr -status` 均为未记录，未取消或停止其他会话 |
| Unity PID／时间 | **34576**；2026-09-23T13:08:25.2959563Z → 13:09:04.6623158Z；未超时、未强制终止 |
| 单测结果 | **1 项执行，0 通过、1 失败、0 跳过，Unity 真实退出 2**；XML 测试时长 16.2676614 秒 |
| 失败证据 | `focused-01/unity.log` 第 529、579、634、682 行再次出现四次临时文件替换失败；前两次位于 `UpdateVersion` 第 57 行保存，后两次位于测试第 32 行清理保存 |
| 独立分析 | 首次因 WPR stdout 为空而脚本取字符串失败，退出 1，版本和部分工件保留；改为同时读取 stdout/stderr 后退出 0，结果另存 `analysis-02`。没有重跑 Unity 或改变测试结果 |

`focused-01/processes.jsonl`、`execution.json`、`tests.xml`、`unity.log` 保存 Unity 的准确参数、真实退出码及原始失败。WPR 的 stdout、stderr、命令和独立进程退出码单独保存，不能把采集器失败等同于 Unity 文件替换错误。

### 保护核对、结论与下一步

单测启动前／退出后的设置副本均为 **22323 字节**，与正式源文件逐字节一致，SHA-256 均为 `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`。开始冻结的 91 个文件在单测后均未变；本轮文档追加发生在这次核对之后。

工作期间新增了非本轮创建的 `CombatEvidenceQueueObservationTests.cs` 及其 `.meta`，位于战斗取证测试目录，文件创建时间为 13:08:22 UTC，早于本轮 Unity 启动约 3 秒、晚于冻结基线。已将观察到的副本、时间及哈希另行归档并保留原文件；**91 个冻结文件稳定不等于整个仓库没有并发新增**，后续全套验收需重新冻结实际输入。本轮未将它们归入自己的实现差异。

| 交付维度 | 本轮结论 |
|---|---|
| 替换失败定位程度 | 受控原生操作与错误码已采集；Unity 单测失败已再次复现。实际 Unity 失败操作的原生状态、句柄及调用关联仍缺失，根因未确认，无依据修复尚未实施 |
| 完整测试结果 | 未重新执行；本轮只执行失败单测且仍失败。前一轮 45/46 仍是历史结果，不能写成本轮完整测试结论 |
| 生命周期采集完整性 | 未执行；没有本轮 BuildId、六阶段构建快照或构建计划对照 |
| 构建交付结果 | 未尝试 KCP 构建，未发布包或启动 Player；本轮总体未完成 |

本轮命中了计划明确的停止条件：WPR 无法启动，普通权限探针与单测已完成，但仍不足以确认原因。没有自动提升权限、安装工具、更改系统防护或 ACL、杀其他进程、放宽断言、添加盲目重试，也未修改正式 C#／PowerShell 工具源码。离线采集组件测试和结果选择回归未重跑。

下一项先让**采集器**在能够启用系统性能跟踪策略的会话中运行；Unity 保持原权限和原 Profile，避免通过提升 Unity 权限改变失败条件。捕获目标限定为正式设置和对应 `Temp/UnityTempFile-*` 的失败操作、返回状态、进程／线程及关联占用，保留原始 ETL；发现丢事件或缺返回状态时仍标证据不完整。取得原因后再做最小处理，随后完整 EditorTools 测试全部通过且进程退出 0，才继续一次 KCP CLI 生命周期复现。历史构建漂移、正常 TMP 构建清理及完整故障矩阵仍未验收。

## 2026-09-23 双会话采集准备

本节更新至 **2026-09-24**，包含管理员实际采集、自检、一次带跟踪单测和停止结论。证据目录：[io-trace-20260923-233528-332](../Logs/BuildProfilesResume/io-trace-20260923-233528-332)，有效自检及本次单测位于其 `retry-02/`。当前仍为原主工作区 `master / 4e473e28`，暂存区为空；没有提交、重置、清理、修改正式工具接口或调整系统策略。

### 新基线和并发边界

准备开始时发现诊断任务在另一个项目副本运行 Unity 基准（PID 39408）及其 Python 调度进程，记录见 `processes-preparation.json`。本任务先准备脚本，没有同时启动采集或 Unity，也未终止这些进程。

诊断负载退出后执行 `freeze-inputs.ps1`，退出 0；两次完整枚举／哈希一致，冻结 **4165 项**，清单为 `inputs-before.json`。范围沿用构建输入的源文件／程序集／Packages／ProjectSettings 枚举，额外纳入全部原生 Profile 文件和相关执行／回归工具，包含当前未跟踪文件，后续新增、删除与修改均可被检测。外部清单不改变构建服务的摘要算法。

首次冻结时间为 **2026-09-23T15:43:33.3807192Z**，runId 为 `0f8e84d5e35147c4a78a4c74e777e2af`。第二次冻结为 **15:49:21.1593431Z**，runId 为 `4cc12ec3a9a24b948e05dc3b5b886e2a`，仍为 4165 项。两份清单 SHA-256 均为 `2befd47f412c2d0b6fe785e0c7ad7486f8424ae25ff25278c4dba18435887cad`。正式设置 SHA 仍为 `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`。自检只触碰日志夹具，源码检查与夹具编译／准备移至录制窗口外；Unity 单测仍在请求前、采集就绪后和退出后重算输入，均未变化。

### 已准备的采集流程

所有脚本仅位于本轮日志目录，不修改正式 C# 或 PowerShell 工具：

- `collector-admin.ps1`：要求管理员会话，仅启动 WPR／logman；拒绝已有／无法确认的 WPR 会话和诊断负载。每段使用唯一命名实例，后续 status／stop 均带同一实例名，不执行全局 stop/cancel。保存准确命令、stdout／stderr、PID、时间和真实退出码。
- `controller-normal.ps1`：拒绝管理员身份；校验冻结基线和带本次 runId／阶段／实例身份的就绪记录。先做日志内正常改名和禁止删除共享的受控失败操作；保存调用 PID／TID、时间、Win32 结果。缺少独立自检验收时拒绝 focused-01，不启动 Unity。
- `export-trace.ps1`：调用本机 tracerpt 输出事件 XML、摘要和报告。第二次尝试增加最终 ETL 长度检查，实际拒绝了超限单测 ETL。导出成功不会自动写自检验收或放行单测。
- 状态使用独立、不可覆盖文件发布，请求／就绪／停止／失败分开；采集器的 5 分钟计时与目录大小检查已实现，但**实时大小检查在实测中未拦住超限**。不能宣称 1 GiB 上限已得到执行验证；最终大小门槛阻止误验收。只停止本轮实例，没有接管其他会话。

自检验收仍要求从真实事件中关联两个探针操作的路径、PID／TID、时间及原生完成状态，核对丢事件情况并绑定原始工件哈希；保留 NTSTATUS 与 Win32 各自的原始类型，不因 ETL 存在或导出退出 0 就生成通过结论。

### 本轮实际检查

| 检查 | 实际结果与边界 |
|---|---|
| 初次 `test-gates.ps1` | 退出 1：本轮脚本的输入比较函数少一个括号；未启动 WPR／Unity，原版本与错误记录保留 |
| 修正后 `test-gates.ps1` | 退出 0，**19/19**；验证就绪身份／状态拒绝、时间／字节边界、输入新增／删除／修改、状态不可覆盖、真实退出码优先于 PASS 字符串、仅终止本轮超时子进程及脚本语法 |
| 空数组序列化修正后 | 增加空数组／null 用例，**21/21**；首次实际控制器错误原样保留。`retry-02/gate-tests-6c3bfb531861439591d3ffb373b0c48e` 在缩短自检录制窗口、增加最终大小门槛后再次执行 **21/21** |
| 负向实际启动 | **3/3**；普通权限运行采集器、未验收自检时运行 focused-01、无停止记录导出分别被拒绝，三个实际子进程退出 1；没有启动 WPR／Unity |
| 管理员 WPR 启停 | 两次管理员会话 PID 38008／34012，实际 FileIO／Minifilter 启动成功；只停止各自 runId 的命名实例，均已结束 |
| 冻结输入 | 两次枚举一致，4165 项；相关源码、Profile 和设置副本另存 `frozen/` |

准确脚本命令、结果与进程记录保存在 `gate-tests-7b54ee471eb74d71a2fc06913cbabcdc`、`negative-launch-checks`、`frozen.json` 等工件中。上述脚本测试不代替 Unity 编译、EditorTools 全套测试或文件系统跟踪自检，也没有重跑上轮 52 组探针。

### 实际采集与自检验收

用户分别在管理员终端执行了根目录及 `retry-02` 的 `collector-admin.ps1`，普通控制器始终未提升权限。第二次的实际命令为：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\UnityStore\MonsterSupergroup\Logs\BuildProfilesResume\io-trace-20260923-233528-332\retry-02\collector-admin.ps1"
```

普通会话使用已记录于 config.json 的 PowerShell 7 可执行文件，工作目录为仓库根目录，依次执行：

```powershell
pwsh -NoProfile -File Logs/BuildProfilesResume/io-trace-20260923-233528-332/retry-02/controller-normal.ps1 -Phase smoke-01
pwsh -NoProfile -File Logs/BuildProfilesResume/io-trace-20260923-233528-332/retry-02/export-trace.ps1 -Phase smoke-01
python Logs/BuildProfilesResume/io-trace-20260923-233528-332/retry-02/inspect-smoke-events.py
python Logs/BuildProfilesResume/io-trace-20260923-233528-332/retry-02/verify-smoke.py
pwsh -NoProfile -File Logs/BuildProfilesResume/io-trace-20260923-233528-332/retry-02/controller-normal.ps1 -Phase focused-01
```

| 段落 | 实际结果 |
|---|---|
| 首次 smoke-01 | WPR 启动成功；控制器在空差异数组写 JSON 时 `GetBytes(null)` 失败，没有执行探针／Unity。只修正档案内序列化，原文件和失败工件保留 |
| 首次 ETL／导出 | ETL **1,827,667,968** 字节，超限；tracerpt PID 39628 在 300 秒超时后仅终止自身，退出 -1，保留不完整 XML。不用于验收 |
| retry-02 smoke-01 | 普通权限 PID **16520**、TID **25012**；两个调用控制符合预期，源码前后无变化，控制器退出 0 |
| 自检采集 | 命名实例 `bp-4cc12ec3a9a2-smoke-01`，WPR 启停退出 0；最终 ETL **424,673,280** 字节，未超限 |
| 自检导出 | tracerpt PID **39656**，15:56:17.026429Z → 15:58:39.6692139Z，退出 **0**；5,307,018 条事件，汇总丢事件 0，完整 XML 6,611,028,965 字节 |
| 原生状态核对 | 正常 `Rename → OperationEnd` 为 **NTSTATUS 0x00000000**；禁止删除共享时 `Create → OperationEnd` 为 **NTSTATUS 0xc0000043（3221225539）**，调用端即时 **Win32 32** 单独保存 |
| 独立时间核对 | 本机 xperf 重新解析相同 ETL，核对 IRP／路径／PID／TID 与原始相对纳秒；用 ETL 头部 FILETIME 计算完成时刻，均落在 probe.json 的调用前后区间内 |

自检 XML 有 181,359 条解析错误、172,298 条仅二进制内容，不能称全文件完全解码；已审查的 Create／Rename／OperationEnd 链及其两个调用区间没有这些错误。tracerpt 的时间文本写出 `+07:59`，与本机记录的 `+08:00` 不一致，未直接信任或改写该字符串，而用头部原始时间加 xperf 原始相对纳秒验证。`smoke-01/event-audit/verified-chain.json`、原始 XML、xperf 窗口及 `acceptance.json` 绑定哈希。ETL SHA：`1054633d686dc8bb7d28103e2d4892f4ae811f6aa9a1e7cf2ff6421266394d05`；XML SHA：`b4d03fcd3d5773ccf15831dc3ac82eeb5881d66d2c2e5bb85919cbee35f0a7bb`。

对应语义按 Microsoft 的 [FileIo OperationEnd](https://learn.microsoft.com/en-us/windows/win32/etw/fileio-opend) 及 [NTSTATUS 定义](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/596a1078-e883-4972-9bbc-49e60bebca55) 区分，不把 NTSTATUS 直接写成 Win32 错误码。

### 带跟踪失败单测：直接状态已取得，完整验收受阻

Unity 使用 6000.3.21f1，Windows-Dev-Kcp、EditMode、仅 `ApplyUsesLiveVersionAndIsIndependentOfBuild`；隐藏窗口，1800 秒超时，没有 `-quit`。参数原件见 `retry-02/focused-01/unity-command.json`。

| 项目 | 实际结果 |
|---|---|
| Unity 进程 | **PID 21800**，2026-09-23T16:09:00.8883686Z → 16:09:51.4995758Z；真实退出 **2**，未超时或强制终止 |
| 测试 XML | **1 执行、0 通过、1 失败、0 跳过**；日志第 624、674、764、812 行为四次设置替换失败；未放宽断言 |
| 输入保护 | 请求前／就绪后／退出后 4165 项输入未变；设置前后均 22323 字节，SHA 与冻结一致；Profile 及源码稳定 |
| 跟踪停止 | WPR 正常停止，停止前显示时长 56 秒、丢事件 0；管理员采集器在 16:10:42.3157411Z 结束 |
| 跟踪体积 | 最终 ETL **6,573,522,944 字节（约 6.12 GiB）**；实时长度门槛未触发，但最终大小核验实际拒绝导出为完整验收工件。**本段不完整** |

为了保留已有直接信息，未再次启动 Unity，而用本机 xperf 从保留 ETL 导出相对 36–56 秒的 FileIo 事件；该分析仅用于定位，不改变超限结论。xperf PID 38768，退出 0，用时约 97 秒，原始窗口 CSV 与工具解析警告分别保留。第一次统计脚本错用 `Operation/NtStatus` 字段得到空统计，原件保留；随后按实际 CSV 头部 `Type/Status` 独立核对，结果位于 `incomplete-trace-analysis/verified-operations.json` 和 `verified-operation-pairs.json`。

四个临时文件均与本次 Unity 错误中的文件名精确匹配：

| 临时文件后缀 | 匹配的 Rename／OpEnd 对数 | 原生返回 |
|---|---:|---|
| d76b4666568cb584bb4da3658f2b07e0 | 100 | NTSTATUS `0xc0000022` |
| b215e7267e021294db88824a4c9c096e | 100 | NTSTATUS `0xc0000022` |
| 84543c8fa74f3014d938b2e346ab4945 | 100 | NTSTATUS `0xc0000022` |
| 263bbc06a0f5d2046a3b82bbe89e4c1e | 100 | NTSTATUS `0xc0000022` |

共 **400 对**均为 Unity PID 21800／线程 26880，`FileRenameInformation`，按 IRP、FileObject、源路径及先后顺序配对。首对原始窗口行号 **2425736 → 2425884**，相对时间 **38.2475382 → 38.2476012 秒**，NTSTATUS `0xc0000022`／十进制 **3221225506**，语义为 `STATUS_ACCESS_DENIED`。这里没有直接采得 Unity 的即时 Win32 错误值，不能写成“实测 Win32 5”。目标正式路径来自同名临时文件对应的 Unity 错误日志，不冒称失败 RenamePath 事件给出了目标路径。

打开事件可读取 Options／Attributes／ShareAccess，但该 [FileIo Create schema](https://learn.microsoft.com/en-us/windows/win32/etw/fileio-create) 不包含完整 DesiredAccess。第一条失败前观察到的 Unity 自身正式文件读取已出现 Close；这不能排除其他早先句柄，也不能用来认定项目句柄泄漏。当前只导出了有限时间窗，过滤驱动参与并不证明它拒绝了请求，仍缺完整访问权限／全程占用和调用关联。**根因未确认，不修改业务保存代码、不增加重试、不改防护或 ACL。**

### 本轮结论与下一步

| 交付维度 | 结论 |
|---|---|
| 采集链路 | 管理员启动能力及小型自检已验证；长段体积控制未通过，单测 ETL 超限，不能称完整链路验收通过 |
| 替换失败定位 | 四个临时文件的 400 对改名失败原生状态已直接取得；根因／责任方仍未确认，无产品修复 |
| 完整测试 | 本轮未执行；带跟踪单测仍 0/1，退出 2。历史 45/46、离线组件 20/20 和两种 PowerShell 各 44 项均未当作本轮通过 |
| KCP 生命周期及交付 | 未启动构建，没有本轮 BuildId 或六阶段证据，没有发布或启动 Player；本轮总体未完成 |

下一项先处理并验证采集体积控制：记录实时统计使用的实际文件路径／属性／长度与停止前后大小，查明漏计或停止合并增量，保留改名完成状态和关联事件所需配置；若改变采集配置，先重新自检，再进行有界单测跟踪。完整 DesiredAccess／句柄关联的采集能力须明确验证，不用小型自检覆盖不到的字段宣称已具备。取得有依据的处理后重跑单测、完整 EditorTools；全套通过、退出 0 且输入稳定，才继续一次 KCP CLI 生命周期。其余入口、代表包和完整 TMP／故障注入矩阵继续保留后续。

## 2026-09-24 最小启动对照与完整测试恢复

用户收缩范围：停止扩张通用采集框架和无关矩阵，以少量受控对照判断故障层次；打包不要求前后 hash 一致。本节替代上节下一步。主工作区 `F:\UnityStore\MonsterSupergroup`、`master / 4e473e28`，暂存区为空。其他任务的诊断源码改动继续保留，没有切换 worktree、提交、reset 或 clean。

证据目录：`Logs/BuildProfilesResume/save-contrast-20260924-105656-392`。保存接手差异、相关源码／Profile／设置副本、准确参数、PID／时间／退出码、XML、日志及各次设置原始字节。本轮没有新增 ETL、下载工具或扩充采集器；设置哈希仅用于记载状态，不作为打包放行条件。

### 历史与方法变更的关系

1. 直接读取 `Logs/BuildProfilesImplementation/editmode.xml:40`：`ApplyUsesLiveVersionAndIsIndependentOfBuild` 在 **2026-09-23 07:37:31–32 UTC（15:37 北京时间）为 Passed**。当时全套 69/70 的失败是另一用例，不能把那次整体失败理解为版本更新也失败。
2. 旧实现（`d257fc33`）是 `PlayerSettings.bundleVersion = ...; AssetDatabase.SaveAssets()`；当前实现经 `NativeBuildProfileSettings.UpdateVersion` 写全局序列化设置，再调用 `SaveAssets()`。方法确实改过，但发生过改动本身不是失败因果证据。
3. 历史 07:44 KCP 计划记录的 `ProjectBuildWindow.cs`／`BuildIdentityTests.cs` SHA 与当前相同。其 `NativeBuildProfileSettings.cs` SHA 为 `331238e5820dbd762643ddfca0bc91240a150aa57d643ce470540a82cf7221eb`，与 17:19 诊断实施前归档源码吻合；和当前的差异仅为 `InputFiles` 采集接入，版本保存方法未变。这是邻近时点源码证据，不冒称已冻结 07:37 单测瞬间的全部源码。
4. 最近失败时归档及本轮的 `NativeBuildProfileSettings.cs` 均为 `a4c31b826deae52b5e64886692f1a7cdcfc930b6468b68587a3ae8b29d54b3c3`；当前代码未修保存方法便重新通过。故不能认定新方法会稳定触发拒绝。

### 本轮实际执行

统一使用 Unity `D:\RealSoftware\6000.3.21f1\Editor\Unity.exe`、Windows-Dev-Kcp、`-batchmode -runTests -testPlatform EditMode`，不加 `-quit`，普通权限、隐藏窗口、串行执行、1800 秒超时。旧成功命令没有 `-nographics`，最近失败命令有，故只用这个差别做一组启动参数对照。

| 执行／目录 | PID | 进程起止 UTC | XML／真实退出码 |
|---|---:|---|---|
| 当前保存方法，不加 `-nographics`／`current-graphics` | 53416 | 02:57:22.9787960 → 02:57:51.7979571 | **1/1 通过、0 跳过，退出 0** |
| 同一方法，加 `-nographics`／`current-nographics` | 59024 | 02:58:13.9270715 → 02:58:33.2652078 | **1/1 通过、0 跳过，退出 0** |
| 摘要门槛调整后完整 EditorTools／`full-editor-tools` | 57580 | 02:59:34.3120433 → 03:00:07.3097276 | **46/46 通过、0 跳过，退出 0** |

完整测试包含 **BuildIdentityTests 23、BuildRecipeTests 15、ProjectToolTests 8**，没有通过改数量或忽略错误放行。日志第 298 行记录重新编译 `MonsterSupergroup.EditorTools.dll`，无 C# 编译错误。三次运行均无设置替换失败；每次设置退出后均恢复到启动前字节。这仅描述测试恢复结果，不要求真实构建前后设置相同。

准确执行脚本在本轮档案内，三条命令为：

```powershell
& '.\Logs\BuildProfilesResume\save-contrast-20260924-105656-392\run-focused.ps1'
& '.\Logs\BuildProfilesResume\save-contrast-20260924-105656-392\run-focused.ps1' -Label current-nographics -NoGraphics
& '.\Logs\BuildProfilesResume\save-contrast-20260924-105656-392\run-focused.ps1' -Label full-editor-tools -NoGraphics -TestFilter MonsterSupergroup.EditorTools.Tests
```

每个子目录的 `execution.json` 给出实际 Unity 参数和退出码。未修改保存方法、正式设置、ACL／防护、缓存、测试断言或正式 PowerShell 工具；未重跑离线组件与 PowerShell 选择回归，历史通过数不计入本轮。

### 结论与最小下一步

- **故障层次**：当前方法在原路径的两种启动参数下都可成功，`-nographics` 假设未得到支持。项目保存实现的确定性回归缺乏证据；优先考虑运行环境或临时项目／路径状态，二者尚不能定责。旧记录的普通用户／中等完整性与本轮相同，也不足以证明两轮全部运行限制或句柄状态相同。
- **当前定位程度**：历史成功过、后来连续拒绝过、现在未修改保存实现又通过；历史 `STATUS_ACCESS_DENIED` 仍成立，原因未确认。本轮没有正在复现的失败，因此不继续扩充探针或替换实现来制造“修复”。
- **唯一产品改动**：`ProjectBuildService` 在构建前后摘要不同时继续写 `build-plan-after.json`，将拒绝发布的异常改为警告。摘要计算和初始身份数据不变；Unity 结果、清理及包一致性校验继续执行。此分支已编译，完整 EditMode 通过；**尚未执行真实构建验证这一发布行为**。
- **后续执行**：下一项仅一次 Dev／Kcp／Direct／Normal CLI 构建，使用新输出和结果路径，核对六阶段变化与包内一致性，不要求前后摘要相同，不启动 Player。若保存拒绝再次出现，先比较同一命令从普通终端和自动化环境启动；若都失败，再以独立进程比较旧直接保存与当前适配保存。仅在两种保存均失败时考虑副本路径对照，避免同时变更方法、启动环境和路径。

本轮未执行 KCP、没有新 BuildId 或交付包，未宣称历史漂移已经解决。旧超限 ETL 保留，但其体积控制不再阻塞下一条诊断信息。

## 2026-09-24 一次 KCP CLI 构建与生命周期核验

**本轮验收完成：一次主构建成功、六阶段证据完整、包核验通过；未启动 Player。** 没有设置替换失败，因此未触发普通终端追加构建或旧／新保存对照，没有重跑全套测试、WPR 或外围自检。

### 基线与实际命令

原主工作区 `F:\UnityStore\MonsterSupergroup`，`master / 4e473e28f483105426a3df532a560aa13c622ad6`，暂存区为空。启动前相关构建源码、Profile 和设置与上一轮 46/46 归档一致。新档案保存 77 项相关文件及未跟踪源码副本、继承差异、暂存差异、源码哈希；构建退出后这 77 项均未改变。两份计划各含 4141 项实际输入，不把局部冻结清单冒称完整工程清单。

档案：`Logs/BuildProfilesResume/kcp-once-20260924-120526-189-3aa35027`。实际执行档案中的 `run-build.ps1`，内部命令为：

```powershell
& .\Tools\Invoke-ProjectTool.ps1 `
  -ToolId build.player `
  -BuildProfile 'Assets/Settings/Build Profiles/Windows-Dev-Kcp.asset' `
  -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -Output 'Builds/ProfileValidation/KcpLifecycle-20260924-120526-189-3aa35027/MonsterSupergroup.exe' `
  -ResultPath 'Logs/BuildProfilesResume/kcp-once-20260924-120526-189-3aa35027/result.json' `
  -TimeoutSeconds 1800
```

Unity **PID 59120**，04:05:46.6463421 → 04:07:25.6351758 UTC（北京时间 12:05:46 → 12:07:25），真实退出 **0**，没有超时或清理终止。CLI 报告 `success=true`、`pending=false`，用时约 99.25 秒；Unity 内部结果也为成功。沿用隐藏窗口，实际命令包含 `-nographics`，不含构建后运行／清缓存选项。

### 六阶段与进程退出后的变化

BuildId：`20260924T040614035Z-ba480bf3`；Profile GUID：`b441d96b1c4f7a3469a6a2f3fc2fb88e`。本次内部目录为 `input-20260924T040608998Z-d8b11c84b5bc4457a8311b6000875ed7`，已完整复制进档案的 `input-evidence/`，没有选用历史工件。

| 阶段 | UTC 时间 | 状态／字节数 | 设置 SHA 分组 |
|---|---|---|---|
| 初始计划 | 04:06:13.8583017 | Captured／22323 | A |
| Unity 调用前 | 04:06:22.5370292 | Captured／22323 | A |
| Unity 返回后 | 04:07:12.0182088 | Captured／22395 | B |
| 身份清理后 | 04:07:12.0242573 | Captured／22395 | B |
| TMP 恢复后 | 04:07:12.3293795 | Captured／22395 | B |
| 最终计划 | 04:07:24.7265342 | Captured／22395 | B |

- A：`3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`。
- B：`64027d29de779234b5c861a3b70a415605c9e87b6759d7a09c7e7d69c92629af`。
- 外部启动前和退出后快照均为 A。初始、调用前相同；返回后到最终计划均为 B；恢复到 A 发生在最终计划读取至进程退出后的外部采样之间。
- 六份快照长度和 SHA 全部独立复算一致，时间顺序正确，采集无错误，状态 **Complete**。初始／最终快照分别匹配各自计划的 ProjectSettings.asset 输入条目。

`analyze-lifecycle.py` 独立比较相邻、首尾及外部快照，输出 `lifecycle-audit.json` 和带行号的 `independent-diff.txt`。唯一文本变化是 `preloadedAssets` 新增第 157 行：

```yaml
  - {fileID: 11400000, guid: 99f9c9493070a9d4c979a8fec7c5a8d3, type: 2}
```

**事实**：该 GUID 来自本机 Behavior 包 `Authoring/Behavior App UI Settings.asset.meta`；构建日志第 13989 行也列出此资产。当前两次设置 SHA 与历史 KCP 失败记录的前后 SHA 完全一致，本次确实取得了对应的具体字段变化，而非“未复现漂移”。

**源码支持的推断**：本机 App UI 包 `AppUIBuildSetup` 第 59–63 行会将当前 AppUISettings 加入预加载资产，后处理的 `Revert` 第 163 行移除 AppUISettings；逻辑与此次新增／退出后恢复相符。没有专门记录该回调的实际调用和磁盘刷新时间，不能单凭这些快照认定精确写回时点，也不能据此解释历史 NTSTATUS 拒绝。相关源码及资产／meta 已归档到 `referenced-source/`。

### 摘要规则与实际包核验

本次 contentHash 始终为 `1981aa3f64db6a92df96d476e1758eeb190d31cdbcc772469743247043dfb06e`；inputHash 从 `9fec6c7438666578225636be417618352cecf66ba3bb887f20e1ff3dfdf91898` 变为 `f5e2b0ebb9e5bed21dfa9f307463b89c4391e91264857a3fc3d6741f8fe7c287`。4141 项输入中仅设置文件不同。日志第 14258 行报告警告，14273 行报告成功，证明“摘要不同不单独拒绝发布”的分支本次实际运行并完成交付。

实际包目录：`Builds/ProfileValidation/KcpLifecycle-20260924-120526-189-3aa35027/MonsterSupergroup-v0.0.1-dev-20260924T040614035Z-ba480bf3`。

- EXE 存在且为 **667136 字节**。BuildInfo schema 3，版本 0.0.1，Dev／Kcp／Direct／Normal、Development=true、tools=true、evidence=false、testAssemblies=false。
- BuildInfo、`build-complete.json` 内容一致；Profile 身份、初始 contentHash／inputHash、BuildId 与初始计划及本次成功指针一致。构建后计划独立保留，没有把其不同 inputHash 强行写回初始身份。
- 使用本机 Cecil 只读解析包内 `MonsterSupergroup.Build.dll`：CompiledKind=0（Dev）、ToolsCompiled=1、EvidenceCompiled=0、CompiledNetwork="Kcp"。未执行 DLL 或启动 Player。
- 未携带项目测试程序集、`steam_appid.txt` 或 `combat-build.json`，符合本次用途。实际请求 cleanBuildCache=false、runAfterBuild=false；暂存产物已移至成功目录，无本次失败隔离目录。
- `verify-package.ps1` 的 16 项包核对均为 true，结果在 `package-audit.json`；CLI／服务日志、进程原始记录、计划、BuildInfo、完成标记和指针副本均已归档。

以上两段离线分析的实际命令为：

```powershell
python .\Logs\BuildProfilesResume\kcp-once-20260924-120526-189-3aa35027\analyze-lifecycle.py
& .\Logs\BuildProfilesResume\kcp-once-20260924-120526-189-3aa35027\verify-package.ps1
```

### 停止结论

| 维度 | 本轮结论 |
|---|---|
| 构建交付 | 成功，真实退出 0，包与结果指针核验通过 |
| 生命周期证据 | 六阶段齐全，独立核验通过，捕获中途变化及退出后恢复 |
| 文件替换拒绝 | 本次未复现；历史原因未确认，条件对照未触发 |
| 代码与范围 | 本轮不修改产品源码，只更新日志工件及进度；没有追加构建、系统跟踪、全套测试或 Player 运行 |

本轮按验收终点停止。当前字体状态下的清理成功不能代替非空 TMP／故障注入验证；其他入口、代表包及 Player 运行仍属后续。已定位的摘要变化无需作为当前包失败或额外重试的理由。

## 2026-09-24 三入口验收启动与界面阻塞

**本轮尚未完成：已建立基线并打开可见 Editor，因自定义窗口位于屏幕外而等待用户移回。没有执行新的构建。**

档案：`Logs/BuildProfilesResume/kcp-entries-20260924-131415-902-2c273891`。主工作区 `F:\UnityStore\MonsterSupergroup`，`master / 4e473e28f483105426a3df532a560aa13c622ad6`，暂存区为空；保留既有及其他任务改动。`baseline.json`、`source-before.json`、`before/` 保存 108 项相关文件及哈希；`inherited.patch`、`staged.patch` 和 Git 状态保存继承改动。CLI 成功指针另存 `cli-baseline-pointer.json`。

可见 Editor 由档案内 `open-editor.ps1 -Label native` 启动，准确参数记录在 `native/execution.json`：

```powershell
& 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -projectPath 'F:\UnityStore\MonsterSupergroup' `
  -logFile 'F:\UnityStore\MonsterSupergroup\Logs\BuildProfilesResume\kcp-entries-20260924-131415-902-2c273891\native\unity.log'
```

本次未使用 batchmode、executeMethod 或 activeBuildProfile 参数。PID **45688**，开始于 **2026-09-24 05:14:36.4947618 UTC**。本记录写入时仍在运行，结束时间／退出码为 null，不能记作退出 0。启动脚本保留等待进程退出及保存设置后快照的逻辑。

界面操作：原生 Build Profiles 窗口中 Windows-Dev-Kcp 带 Active 标记；随后从“MonsterSupergroup → 构建与验收 → 构建配置…”打开自定义窗口。辅助功能树确认 `MonsterSupergroup.EditorTools.ProjectBuildWindow` 存在，但窗口恢复在屏幕左侧之外，工具点击最大化返回 `point (-138, 300) is outside window bounds { originX: 0, originY: 0, width: 1920, height: 1040 }`。关闭了遮挡的原生窗口；没有点击任何构建、配置保存、版本更新或 Profile 激活按钮。当前截图和界面树保存为 `ui-current-0.png`、`ui-current.txt`。已请求用户仅移回窗口，不代为执行构建。

`preview-before.json` 保存 Editor 加载后、选择自定义 Profile 前的 49 项 Profile／设置哈希；`preview-pending.json` 和 `pre-preview-check.json` 的差异为零。这只是当前状态核对，浏览 KCP → Windows-Test-Steam → KCP 还未发生，不能据此认定预览只读通过。

| 检查 | 沿用 CLI 证据 | 本轮原生按钮 | 本轮自定义按钮 |
|---|---|---|---|
| 实际构建 | 成功，BuildId `20260924T040614035Z-ba480bf3` | 未执行 | 未执行 |
| 实际包／编译能力 | 上轮已核验 Dev／Kcp／Direct／Normal | 无新包，未核验 | 无新包，未核验 |
| 六阶段 | 上轮完整 | 未执行 | 未执行 |
| 跨入口一致性 | 仅有比较基准 | 未完成 | 未完成 |

没有启动 Player、WPR、全套测试或追加 CLI；没有修改公共接口、BuildInfo、产品源码或设置。已准备独立输出位置 `Builds/ProfileValidation/KcpEntries-20260924-131415-902-2c273891/Native/` 和 `Custom/`，目前均无包。

**接续点**：用户将“构建配置”移回可见区域后，先完成实际预览序列并复核字节，再点击原生 Build；成功即归档其指针和包，正常关闭并重开 Editor 检查配置恢复，最后点击自定义构建按钮。沿用单次 1800 秒及首次入口失败即停止的边界。此次界面阻塞不是产品入口缺陷证据，不提出产品修复结论。

### 用户操作预览完成，等待原生 Build

用户要求接管全部 computer-use 操作，本轮后续不再自动点击窗口。用户报告已完成预览序列，KCP 显示“当前已激活”，Test-Steam 显示“尚未激活”。独立读取磁盘核对原 `preview-before.json` 的 49 项文件，文件数量仍为 49，所有 Profile／设置 SHA 一致，无新增、删除或内容差异；108 项相关冻结文件中仅此前本任务更新的两份进度文档变化，构建源码未变化。

本次**预览文件字节与活动状态检查通过**：界面操作／状态来自用户报告，文件结果来自本轮实际计算；不将其表述为自动录屏确认。证据为 `manual-preview/audit.json`、`files-after.json` 和设置原始副本。读取源码确认“刷新只读计划”调用 `ProjectBuildResolver.Resolve(profile, true)`，读取有效配置和输入摘要、显示符号差异，不调用保存、Profile 激活或构建。后续自定义构建会比较预览与启动时的摘要，过期时要求重新刷新；这与允许构建前后摘要不同是两个检查时点。

进程接续已核实：最初 PID 45688 于 **05:50:51.4140971 UTC 退出 0**；用户从 Hub 打开的当前 Editor 为 **PID 64304**，开始于 **07:42:43.900581 UTC**，未传 `-activeBuildProfile` 或 batchmode。Hub 命令中的凭据不纳入档案。日志来源改为 `%LOCALAPPDATA%/Unity/Editor/Editor.log`；`native-manual/` 已保存点击构建前的设置和原 CLI 指针，`observe-manual-editor.ps1` 只等待当前进程结束、记录退出码和退出后设置，不启动或终止进程。

截至本检查，Native 输出目录为空，Profile 结果指针仍是 CLI BuildId `20260924T040614035Z-ba480bf3`，没有新增按钮构建。下一步由用户点击原生 **Build** 并选择既定 Native 目录，完成后保持 Editor 打开供归档；成功归档后再做正常重开和自定义入口。用户本次提前重开不能替代原生构建后的持久化验证。

### 原生 Build 成功交付，待关闭重开检查

用户随后报告“构建完成”。实际日志的调用栈包含 `BuildProfileWindow.OnBuildButtonClicked → BuildProfileModuleUtil.CallInternalBuildMethods → NativeBuildEntry.Build → ProjectBuildService.Build`，确认执行的是原生按钮入口。Editor PID **64304**；服务本次尝试 **07:57:56.1804133–07:59:08.7662570 UTC**，约 72.59 秒，未超过 1800 秒；用户点击的精确时间未单独记录，不能把服务时间冒充按钮时间。Unity 内部报告 `Build Finished, Result: Success.`，服务报告 `[ProjectBuild] Success`；Editor 仍在运行，退出码继续标为未取得。

BuildId：**`20260924T075801388Z-66f2461b`**。包目录：`Builds/ProfileValidation/KcpEntries-20260924-131415-902-2c273891/Native/MonsterSupergroup-v0.0.1-dev-20260924T075801388Z-66f2461b/`。EXE 为 667136 字节。`native-manual/result-pointer.json` 已独立归档，后续自定义构建覆盖工作区指针不会丢失本次证据。

包核验实际执行 `native-manual/verify-package.ps1`，**16 项全部 true**：EXE、schema 3／BuildId、Dev／Kcp／Direct／Normal、Profile 身份、初始计划摘要、BuildInfo 与完成标记、结果指针、清缓存／运行请求、测试程序集与分发文件、暂存移出／无本次隔离目录，以及实际 DLL 的四项编译能力。Cecil 只读解析为 CompiledKind=Dev、ToolsCompiled=true、EvidenceCompiled=false、CompiledNetwork=Kcp；没有执行 DLL 或 Player。请求 `cleanBuildCache=false`、`runAfterBuild=false`，核查时无 MonsterSupergroup Player 进程。

| 比较项 | CLI（沿用上轮成功证据） | 原生 Build（本轮） | 自定义按钮 |
|---|---|---|---|
| BuildId | `20260924T040614035Z-ba480bf3` | `20260924T075801388Z-66f2461b` | 未执行 |
| Profile／版本／用途 | KCP GUID `b441d96b1c4f7a3469a6a2f3fc2fb88e`／0.0.1／product | 相同 | 待验 |
| 业务配置 | Dev／Kcp／Direct／Normal | 相同 | 待验 |
| 场景顺序／原生设置／符号 | 归档初始计划 | 全部字段相同 | 待验 |
| contentHash | `1981aa3f64db6a92df96d476e1758eeb190d31cdbcc772469743247043dfb06e` | 相同 | 待验 |
| 实际编译能力 | Dev、Tools=true、Evidence=false、Kcp | 相同 | 待验 |
| 包交付及内部六阶段 | 上轮通过 | 本轮通过 | 待验 |

`native-manual/analyze-native.py` 独立比较初始计划，除 `inputHash`／`inputFiles` 外的所有字段与 CLI 均一致。CLI 到原生的工程输入有 **10 项诊断／测试源码差异**，具体路径和前后哈希在 `lifecycle-audit.json.cliToNativeInputDifferences`；这些是工作区其他改动，不代表入口内容配置差异，也不宣称两个包所有 DLL 字节相同。原生构建内初始／最终输入清单则只有 ProjectSettings.asset 变化。

本次内部证据目录：`input-20260924T075756176Z-28750a35d9e14f258d536c12ef74c864`，完整副本在 `native-manual/input-evidence/`，manifest 的 Profile GUID 和 BuildId 匹配本次包。

| 阶段 | UTC 时间 | 状态／字节数 | 设置 SHA 分组 |
|---|---|---|---|
| 初始计划 | 07:58:01.1915220 | Captured／22323 | A |
| Unity 调用前 | 07:58:09.8982868 | Captured／22323 | A |
| Unity 返回后 | 07:58:55.6739831 | Captured／22395 | B |
| 身份清理后 | 07:58:55.6813064 | Captured／22395 | B |
| TMP 恢复后 | 07:58:56.0352289 | Captured／22395 | B |
| 最终计划 | 07:59:08.6775882 | Captured／22395 | B |

A 为 `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`，B 为 `64027d29de779234b5c861a3b70a415605c9e87b6759d7a09c7e7d69c92629af`。六份长度／SHA、时间顺序及初始／最终计划输入条目均复算一致，Complete 且 errors 为空。独立行差异仍为 `preloadedAssets` 第 157 行增加 GUID `99f9c9493070a9d4c979a8fec7c5a8d3`。外部构建前为 A，构建完成且 Editor 未退出时为 B；**退出后快照尚未取得**，没有用 CLI 的退出快照补齐。差异保存在 `independent-diff.txt`。

日志归档为 `native-manual/unity-after-build-shared-read.redacted.log`，Hub 凭据已遮蔽。首次普通读取因 Editor 正占用日志失败，初次空日志／不完整 capture 记录保留但不作为证据；改用共享读取取得 1733290 字节，副本哈希与采样时间见 `capture-complete.json`。第 3929、18500 行记录 TMP Fallback 资产 `generated inconsistent result`，第 18473 行为 Unity 成功，第 18533 行为发布成功；保留该导入问题，不把它掩去，也不扩展为 TMP 故障矩阵或本轮修复。设置替换拒绝本次未观察到。

本轮没有修改产品源码或重新运行 CLI／全套测试。原冻结相关源码未变；设置变化为上述已知字段，两份文档为本任务进度增量。下一步仅由用户正常退出并从 Hub 重开，核对 KCP 活动状态和配置恢复；我补核对真实退出码及退出后设置，再放行一次自定义按钮构建。三个入口验收仍未完成。

### 原生构建后正常重开：设置已恢复，KCP 仍活动

用户已正常关闭并重新打开项目。观察脚本取得原 Editor PID 64304 的真实退出码 **0**，结束于 **08:10:15.5831295 UTC**。`native-manual/settings-after-exit.asset` 为 22323 字节，SHA 恢复为 A（`3c2d8d83…`），与外部构建前一致；构建后且未退出时的副本为 B。独立生命周期分析已纳入退出后副本，原来尚缺退出快照的报告和差异保留为 `lifecycle-audit-before-exit.json`、`independent-diff-before-exit.txt`。因此本次原生入口也取得 A → B → A 的完整外部恢复过程，没有借用 CLI 的退出证据。

当前 Editor PID **53588**，由 Hub 于 **08:10:18.974755 UTC** 启动，实际参数不含 `-activeBuildProfile`。用户截图确认 Windows-Dev-Kcp 仍有 **Active** 标记；截图归档为 `restart-check/kcp-active-user.png`。磁盘再次核对 49 项 Profile／设置哈希均与预览前一致；设置／宏文件持久化和活动 Profile 恢复已有证据。自定义窗口里的 Dev／Kcp／Direct／Normal 显示尚待用户查看，暂不记为全部界面检查通过。

自定义窗口的准确菜单为 **MonsterSupergroup → 构建与验收 → 构建配置…**，对应 `ProjectBuildWindow.cs` 的 MenuItem。后续继续由用户操作窗口，我只核对文件和日志；自定义按钮构建尚未执行，当前 Profile 结果指针仍为本次原生成功包。

### 原生 Inspector 业务字段展示：尚未实现

用户指出展示目标是原生构建 Profile 的 Inspector。实际代码中，`MonsterBuildSettings.cs:82` 只注册业务子资产自身的 CustomEditor，`ProjectBuildWindow.cs:51-52` 由独立窗口显式绘制它；没有原生 Build Profiles 内置面板集成。KCP 资产内已有完整业务子资产，故这不是字段数据缺失，也不能将独立窗口等同于用户要求的原生展示。

本机 6000.3.21f1 的 BuildProfileModule 程序集经 Cecil 只读核对，`IBuildProfileSettingsProvider` 和 `AddSettingsDataProvider` 非公开，设置提供器静态列表只含 Unity 内置项目；官方 6000.3 参考源码一致。`CreateComponent<T>()` 保证嵌入 ScriptableObject 子资产，不保证自动绘制其 Inspector。准确结论是：**数据接入已实现，用户期望的原生面板展示未实现；该版本的原生面板扩展也不能直接依赖现成公开注册接口。** 这不是“Unity 完全不能显示自定义 Inspector”的证明。

核查依据、官方链接及本机类型信息见本轮档案 `native-inspector-gap.md`、`native-inspector-source-hashes.json`。此项作为剩余界面工作记录，不掩盖为已通过；本轮未对此实施猜测性补丁、替换原生编辑器或运行额外构建。CLI／原生按钮的实际交付证据仍有效，自定义按钮验收继续标为未执行。

### 持久化检查通过，自定义按钮构建准备完成

用户随后明确报告：重开后 KCP 仍为 Active，自定义窗口仍显示 **Dev／Kcp／Direct／Normal**。本轮再次读取 49 项 Profile／设置，全部哈希与预览前一致，分支／HEAD 仍为 `master / 4e473e28`，暂存区为空。结合前述正常退出 0 和重开参数记录，**现有配置持久化检查通过**；界面部分为用户操作及确认，磁盘部分为实际独立核对，记录于 `restart-check/ui-confirmation.json`。

当前 Editor 仍为 PID **53588**。档案 `custom-manual/` 已冻结构建前设置原始字节（SHA 为 A）、KCP Profile、108 项相关文件哈希和原生成功指针副本；Custom 输出目录仍为空。只读观察脚本 `observe-custom-editor.ps1` 等待该进程结束并保存真实退出码及退出后设置，不启动／终止 Editor。

下一步由用户在自定义窗口选择 KCP、刷新只读计划，关闭“清理构建缓存”和“完成后运行”，填写 `Builds/ProfileValidation/KcpEntries-20260924-131415-902-2c273891/Custom/MonsterSupergroup.exe`，只点击一次“按此计划构建”。完成后先保持 Editor 打开，归档本次指针、六阶段和包，再正常退出。当前没有自定义构建结果；原生 Inspector 展示问题不在本轮自动修复。

### 自定义按钮：包交付成功，发布后布局错误已复现

用户点击一次“按此计划构建”后报告 `EndLayoutGroup: BeginLayoutGroup must be called first.`。本轮没有再次构建。日志实际先完成构建并成功发布，随后才出现该布局错误，故分别核验交付和界面，不能将它误报为包构建失败，也不能忽略界面缺陷。

Editor PID **53588**；BuildId **`20260924T083722193Z-933b24a4`**。服务尝试 **08:37:16.9188248–08:38:16.6193253 UTC**，约 59.70 秒，未超过 1800 秒；按钮的精确点击时间未独立记录。实际包目录为 `Builds/ProfileValidation/KcpEntries-20260924-131415-902-2c273891/Custom/MonsterSupergroup-v0.0.1-dev-20260924T083722193Z-933b24a4/`，EXE 为 667136 字节。

`custom-manual/` 已保存脱敏日志、独立结果指针、计划／BuildInfo／完成标记／请求、副本快照及核验脚本。`verify-package.ps1` 实际 **16 项全部通过**；只读 Cecil 确认 Dev、Tools=true、Evidence=false、Kcp，未运行 DLL 或 Player。BuildInfo、初始计划、完成标记及指针身份和摘要一致，清缓存／运行请求均为 false，暂存包已移出，无本次失败隔离目录；因此保留这个有效成功指针，不因发布后的 GUI 错误撤销有效交付。

内部目录 `input-20260924T083716916Z-0a235497f7c64834883136696e150a3a` 的 BuildId／Profile 与包匹配，副本在 `custom-manual/input-evidence/`。独立执行 `analyze-custom.py` 验证全部六份长度和 SHA、时间顺序、首尾计划的输入条目，状态 Complete、errors 为空：

| 阶段 | UTC 时间 | 状态／字节数 | 设置 SHA 分组 |
|---|---|---|---|
| 初始计划 | 08:37:21.9763135 | Captured／22323 | A |
| Unity 调用前 | 08:37:31.1899395 | Captured／22323 | A |
| Unity 返回后 | 08:38:03.6004015 | Captured／22395 | B |
| 身份清理后 | 08:38:03.6085304 | Captured／22395 | B |
| TMP 恢复后 | 08:38:03.9578977 | Captured／22395 | B |
| 最终计划 | 08:38:16.5344711 | Captured／22395 | B |

A、B 与前述两次构建相同；唯一阶段文本变化仍为 `preloadedAssets` 增加已知 GUID。外部构建前 A、构建完成但 Editor 未退出时 B。**当前尚无自定义 Editor 的退出码或退出后快照**，报告对此标为 NotYetCaptured，不以原生或 CLI 证据补齐。

实际执行 `compare-entries.py` 得到三入口比较表（完整数据在 `three-entry-comparison.json`）：

| 比较项 | CLI（沿用上轮） | 原生按钮（本轮） | 自定义按钮（本轮） |
|---|---|---|---|
| BuildId | `20260924T040614035Z-ba480bf3` | `20260924T075801388Z-66f2461b` | `20260924T083722193Z-933b24a4` |
| 实际包交付 | 成功 | 成功 | 成功 |
| 业务配置／场景顺序／原生选项／受管理符号 | 比较基准 | 相同 | 相同 |
| contentHash | `1981aa3f…` | 相同 | 相同 |
| 实际 DLL 能力 | Dev／Tools／非 Evidence／Kcp | 相同 | 相同 |
| 初始 inputHash | `9fec6c74…` | `bd26665c…` | 与原生相同 |
| 六阶段 | 上轮完整 | 本轮完整 | 本轮完整 |
| 退出后设置恢复 | 上轮通过 | 本轮通过 | 本轮通过，见最终收尾 |
| 窗口异常 | 不适用 | 未观察到布局错误 | 发布后布局错误，未修复 |

两次按钮构建初始计划完全一致；与 CLI 除 inputHash／inputFiles 外所有字段一致。与 CLI 的 10 项其他诊断／测试源码差异此前已归档，不要求不同次工程输入摘要相同。原生／自定义包的实际编译能力均由本轮实际解析取得，CLI 能力检查明确沿用其既有归档，未再次运行 CLI。

**布局错误定位**：`custom-manual/unity-after-build.redacted.log` 第 15321 行为 Unity Success，第 15429 行为服务发布 Success，第 15470 行才出现 EndLayoutGroup 错误。错误栈在 `GUILayoutUtility.EndLayoutGroup → GUIView.EndOffsetArea → HostView.InvokeOnGUI`；成功调用栈表明完整构建在 `ProjectBuildWindow.OnGUI` 中同步执行。项目窗口第 36 行仍有 ScrollViewScope，第 78–81 行直接调用服务。本机 Unity 6000.3.21f1 原生按钮 IL 则使用 `EditorApplication.delayCall`，已存 `unity-native-button-il.txt`，与官方 6000.3 源码一致。

**有依据的下一项建议，尚未实施**：将自定义按钮的构建移到当前 OnGUI 结束后的单次回调，捕获点击时 Profile／请求／摘要，防重复排队，保留实际执行时的配置和就绪校验；不吞错误，不盲目补 Begin/End，不改共享构建与发布规则。同步构建破坏布局上下文是首要假设，具体引擎内部失配过程仍未完全证明，需要该最小处理后的真实按钮验证。官方依据与可复现步骤在 `custom-manual/layout-issue.md`。

本轮三个入口的**包配置与编译能力一致性已通过**，预览及现有配置持久化已通过；自定义 GUI 异常和原生 Inspector 展示缺口保持未完成，不能宣称界面无缺陷。当前停止继续构建和修复，仅待用户正常关闭本轮 Editor，补齐最终退出证据后收尾。没有提交、清理缓存、启动 Player 或追加故障矩阵。

### 本轮最终收尾结论

用户正常关闭 Editor 后，观察脚本取得 **PID 53588 于 08:44:59.7545403 UTC 退出 0**。`custom-manual/settings-after-exit.asset` 为 22323 字节、SHA A（`3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`），与构建前完全相同；与最终计划 B 的独立文本差异是已知预加载条目的移除。未把构建前后相同作为发布前置条件，这是额外的退出恢复证据。

最终实际验证：

- 重新执行 `custom-manual/analyze-custom.py`，补入真实退出快照；六阶段及初始／最终计划核验仍通过，A → B → A 过程完整。退出前报告保留为 `lifecycle-audit-before-exit.json`、`independent-diff-before-exit.txt`。
- 更新并执行 `compare-entries.py`：三组入口配对的有效计划字段差异均为空，实际编译能力均相同；两个按钮 Editor 的真实退出码均为 0，退出后设置均恢复。此前待退出的比较报告另存 `three-entry-comparison-before-exit.json`。
- 49 项 Profile／ProjectSettings 文件数量与 SHA 均与预览前一致；本次自定义构建前冻结的 108 项相关文件在退出后仅两份进度文档变化。没有覆盖其他任务改动，不把该相关文件清单冒称全仓库不变。
- 当前 Profile 指针仍为已归档的自定义成功包；原生和 CLI 指针副本分别保留。核查时无 Unity 或 MonsterSupergroup Player 进程，未发起运行请求。
- 最终日志保存在 `custom-manual/unity-after-exit.redacted.log`，原始采样长度 1332338 字节，源日志 SHA 和脱敏说明见 `final-capture.json`；全日志中 EndLayoutGroup 记录为 **1 次**，并未因退出码 0 忽略该问题。

| 交付维度 | 最终结论 |
|---|---|
| 真实按钮及包交付 | 原生／自定义各一次，均成功；CLI 使用上轮成功包 |
| 配置及编译能力一致性 | 三入口通过，工程 inputHash 差异如实记录 |
| 六阶段及退出恢复 | 两次按钮证据均完整、独立复算通过，退出后均恢复 |
| 预览及现有配置持久化 | 通过；用户窗口操作／截图与独立文件核对结合 |
| 自定义窗口界面 | 发布后布局异常已复现，未修复，不能算界面无缺陷 |
| 原生 Inspector 业务字段 | 尚未实现，单独保留缺口 |

本轮范围到此收尾。没有额外构建、修复或 Player 运行；只更新证据和两份进度文档。最小后续建议是单独修正自定义窗口构建调度并验证该布局错误消失，原生 Inspector 展示另列接入任务。真实 Player 身份验收、代表 Profile、非空 TMP／必要故障对照、旧入口迁移和人工 Steam 验收继续按原计划分批推进。

## 2026-09-24 自定义窗口延后构建修复

用户随后授权处理布局错误。本轮采用最小改动，产品代码仅改 `Assets/_Project/EditorTools/Editor/ProjectBuildWindow.cs`；不混入原生 Inspector 展示功能或修改共享构建规则。档案：`Logs/BuildProfilesResume/layout-dispatch-20260924-165242-984-2128ffae`，原主工作区 `master / 4e473e28`，暂存区为空，108 项相关输入／继承差异已保存。

### 修改及边界

点击“按此计划构建”时创建独立请求，捕获当时的 Profile 引用、输出路径、清缓存／运行开关与 contentHash／inputHash；注册一次 `EditorApplication.delayCall`，结束当前 OnGUI 后才执行 `ProjectBuildService.Build`。使用实例待执行回调阻止重复排队，并在排队／执行期间禁用窗口操作；OnDisable 移除回调，回调身份检查防止取消后仍执行旧委托。完成或异常后通过 finally 清除待执行状态并重绘。没有增加自动重试，也没有吞掉布局错误或添加无依据的 Begin/End。

服务自身继续在真正调用时执行 AssertReady、解析、符号及摘要检查，因此延后执行不能绕过活动 Profile、忙碌状态、未保存输入或过期计划的限制。BuildInfo、结果指针、摘要算法、恢复及发布条件均未修改。队列／取消代码已静态核对，未把这些分支描述为已做单独故障矩阵。

### 本轮实际编译与回归

实际命令（档案 `run-tests.ps1` 是已有运行脚本的本轮副本）：

```powershell
& .\Logs\BuildProfilesResume\layout-dispatch-20260924-165242-984-2128ffae\run-tests.ps1 `
  -Label editor-tools -NoGraphics -TestFilter 'MonsterSupergroup.EditorTools.Tests'
```

脚本直接启动 Unity 6000.3.21f1，传 `-batchmode -nographics -activeBuildProfile "Assets/Settings/Build Profiles/Windows-Dev-Kcp.asset" -runTests -testPlatform EditMode` 及上述过滤器，**没有 `-quit`**，隐藏窗口，超时 1800 秒。PID **55612**，08:53:09.7255679–08:53:55.5226663 UTC，真实退出 **0**，未超时。

XML 实际 **46/46 通过、0 失败、0 跳过**：BuildIdentityTests 23、BuildRecipeTests 15、ProjectToolTests 8。没有新增测试或沿用历史通过数。XML、完整日志、进程参数和实际退出码在 `editor-tools/`；汇总 `test-audit.json`。此轮用于确认修改后编译及现有规则回归，不等同于窗口真实按钮验收。

受测窗口源码 SHA-256：`4a8afe1027d7e5c4bcc2515fdf8a078f6966dba0d73594ed6bf941085de63253`，副本为 `ProjectBuildWindow-tested.cs`。相关源码哈希、受测程序集哈希分别在 `tested-source-hashes.json`、`tested-assemblies.json`。108 项相关文件在测试后只有窗口源码变化；设置前后字节一致、SHA 为 A（`3c2d8d83…`）。本轮没有改保存实现，没有设置替换故障。

### 真实按钮回归待执行

自动测试通过后，已用本轮 `open-editor.ps1 -Label button-regression` 打开可见 Editor **PID 65012**，不传 activeBuildProfile 或 executeMethod；准确命令与进程状态在 `button-regression/execution.json`，日志独立写入 `button-regression/unity.log`。该进程目前仍在运行，退出码未取得；用户继续负责所有窗口操作。

下一步：选择 KCP、刷新只读计划，关闭清缓存／运行选项，输出填写 `F:\UnityStore\MonsterSupergroup\Builds\ProfileValidation\KcpLayoutFix-20260924-165242-984-2128ffae\MonsterSupergroup.exe`，点击一次“按此计划构建”。需要确认成功回到窗口后无 EndLayoutGroup 错误，并检查实际包、六阶段及退出恢复。当前尚无此修复后的 BuildId／包，**布局问题只完成实现及编译回归，最终修复效果未确认**。不重跑 CLI 或原生入口，不启动 Player。

### 真实按钮回归完成，正常退出待补

用户随后完成一次构建并报告“已构建，无报错”。本轮 BuildId **`20260924T090326117Z-7d442108`**，包位于 `Builds/ProfileValidation/KcpLayoutFix-20260924-165242-984-2128ffae/MonsterSupergroup-v0.0.1-dev-20260924T090326117Z-7d442108/`。既有全套测试通过数不冒充本次按钮测试；本节是新增的实际窗口回归。

截至 **09:14:35.6443607 UTC**，完整采样日志为 1134640 字节，源 SHA `1a67b07442a68d97c2c33ae3f4ae01b2f2a0bb71566c3a7cb8afc7994fbcb7ae`。脱敏副本、精确成功调用栈和统计在 `button-regression/unity-after-build.redacted.log`、`success-call-stack.txt`、`build-capture.json`。只有一次服务发布成功；布局错误 **0**、C# 编译错误 **0**。成功栈包含 `ProjectBuildWindow.QueueBuild` 及 `EditorApplication.Internal_CallDelayFunctions`，不含 OnGUI，证明此轮构建确实通过延后调度执行。结合用户反馈，**本场景的布局问题回归通过**；未单独实测的取消／重复点击分支仍仅作静态核对。

实际执行以下既有审计脚本的本轮副本（只替换 BuildId 和包路径），两条命令均退出 **0**：

```powershell
& .\Logs\BuildProfilesResume\layout-dispatch-20260924-165242-984-2128ffae\button-regression\verify-package.ps1
python .\Logs\BuildProfilesResume\layout-dispatch-20260924-165242-984-2128ffae\button-regression\analyze-custom.py
```

- **交付核验 16/16**：实际 EXE 存在；BuildInfo schema 3／BuildId／Profile／初始计划摘要与指针一致；完成标记内容正确；请求中清缓存／运行均关闭；无测试程序集或 Steam 专属文件；暂存目录已移出、没有本次隔离失败包。实际解析 `MonsterSupergroup.Build.dll` 确认 Dev、Kcp、工具启用、Evidence 关闭。报告及产物 SHA 在 `package-audit.json`，指针独立留存在 `result-pointer.json`。
- **六阶段完整**：证据来自 `input-20260924T090320922Z-83252c932f454a97a1b5721994b252a9`，副本在 `input-evidence/`，BuildId／GUID／时间对应本次进程。6 份均 Captured、记录无错误；独立复算长度和 SHA 全部一致，时间顺序正确，初始／最终快照分别匹配各自计划中的设置输入条目。`Complete` 仅表示采集完整。
- **字段变化**：初始计划和 before-unity 为 A（22323 字节）；after-unity 至 final-plan 为 B（22395 字节）。首次观察区间仍是 before-unity → after-unity，第 157 行增加已知 `preloadedAssets` GUID `99f9c9493070a9d4c979a8fec7c5a8d3`。外部启动前快照等于初始计划，当前构建后快照等于最终计划。独立逐阶段差异在 `independent-diff.txt`；没有要求前后摘要相同。
- **配置与源码**：与既有 CLI 除 inputHash／inputFiles 外全部计划字段一致，contentHash 仍为 `1981aa3f64db6a92df96d476e1758eeb190d31cdbcc772469743247043dfb06e`。CLI 到本包有 11 项输入差异，含已归档其他源码差异及本轮窗口源码；逐项清单在 `lifecycle-audit.json`。本轮受测窗口 SHA 仍为 `4a8afe10…63253`；相对本轮测试后的 108 项相关清单，仅两份进度文档及上述设置字节变化，相关构建源码／Profile 未变。

**其余日志如实保留**：TMP Fallback 资产在启动／构建后仍有导入不一致提示。发布成功后，Worker0 报 `Assets/AddressableAssetsData/link.xml` 的 SourceAssetDB 时间与磁盘缺失状态不一致（Import Error Code 4；消息内文件查询 error code 2）；原文和行号在 `build-capture.json`。这些记录不能写成“所有日志无错误”，也不作为 EndLayoutGroup 复发或本次包失败的证据。尚未做单独因果定位，不扩展本轮修复。

**当前停止点**：本次真实按钮构建与布局回归已通过，包交付和内部六阶段证据均通过；PID 65012 仍在运行，真实退出码及退出后设置恢复尚未取得。下一步仅由用户正常关闭 Editor 后补档，不重开、不追加构建、不启动 Player。原生 Inspector 展示缺口继续单列。

### 调度修复最终收尾

用户确认正常关闭后，原进程观察脚本记录 **PID 65012 于 2026-09-24 09:16:44.5880766 UTC 退出 0**，即北京时间 17:16:44。结束时间是观察脚本取得进程结束结果的时间。核查时没有 Unity 或 MonsterSupergroup Player 进程。本轮没有重新启动 Editor 或追加构建。

本次补档实际命令如下，均退出 **0**：

```powershell
python .\Logs\BuildProfilesResume\layout-dispatch-20260924-165242-984-2128ffae\finish-audit.py
python .\Logs\BuildProfilesResume\layout-dispatch-20260924-165242-984-2128ffae\button-regression\analyze-custom.py
```

退出后设置为 **22323 字节、SHA-256 `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`**，与启动前相同，当前正式文件与退出快照一致。独立差异确认最终计划到退出后仅移除已知 preloadedAssets 条目，完整过程为 A → B → A。初始／最终计划仍分别对应其自身阶段快照，恢复结果未用作构建发布门槛。退出前报告保留为 `lifecycle-audit-before-exit.json` 和 `independent-diff-before-exit.txt`；最终报告纳入真实 `settings-after-exit.asset`。

最终日志原始采样 **1138249 字节**，SHA-256 `d9590c6596269c9be2581bdf03cb543596a72b0c7baaae9c7a1c948479b0dede`，脱敏副本为 `unity-after-exit.redacted.log`。全日志布局错误 **0**、C# 编译错误 **0**，仍只有一次服务发布成功。此前记录的 TMP／Addressables 导入问题保留，没有据此宣称整个日志没有错误。结果指针与构建后归档完全相同；受测窗口源码 SHA 未变。冻结的 108 项相关文件在退出后仅两份进度文档变化，源码、所选 Profile 和设置已核对；这不是全仓库没有并发改动的声明。证据汇总 `final-capture.json`，清单 `source-after-exit.json`。

| 交付维度 | 本次最终结论 |
|---|---|
| 修改后编译与完整 EditorTools | 本轮实际 46/46、无跳过、退出 0；此次收尾未重复测试 |
| 布局修复效果 | 一次真实自定义按钮构建成功，延迟调用栈得到确认，完整日志没有布局错误 |
| 包交付 | 16/16 核验通过，BuildId `20260924T090326117Z-7d442108`；没有启动 Player |
| 生命周期与退出 | 六阶段完整且独立复算通过，正常退出 0，退出后设置恢复，成功指针保留 |
| 剩余范围 | 原生 Inspector 业务字段展示、其他导入记录及原计划后续验收分别保留；取消／重复点击分支未单独实测 |

本项到此完成，不追加构建或修复。下一项可独立推进原生 Inspector 业务参数展示；Player 身份、代表 Profile、非空 TMP／必要事务失败对照及迁移收尾仍分批安排。不提交、reset、clean，保留既有和并发改动。

## 2026-09-24 Profile 资产 Inspector 业务参数接入

用户已选择“Profile 资产普通 Inspector，允许直接编辑”。本项追加到资产 Inspector 标题区域，保留 Unity 原生编辑器；不是原生 Build Profiles 窗口右侧内置面板的接入。使用公开 `Editor.finishedDefaultHeaderGUI`，不反射或调用 Unity 内部界面。官方依据：[Inspector 回调](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Editor-finishedDefaultHeaderGUI.html)、[原生窗口实现](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Modules/BuildProfileEditor/BuildProfileWindow.cs)。

### 基线与实现

原主工作区 `master / fb28b76e9588e01006debe0e4f8277f94634fa2d`，暂存区为空；`docs/combat-evidence-next-validation.md`、`docs/diagnostic.md` 的继承改动未覆盖。档案为 `Logs/BuildProfilesResume/profile-inspector-20260924-180837-627-460e4ef4`；`baseline.json`、`inherited.patch`、`source-before.json` 及 before 副本保存本轮基线，清单覆盖 5689 项所选源码、Packages、ProjectSettings、Profile 和文档文件。

产品代码仅新增 `BuildProfileBusinessInspector.cs` 及 meta，并在 `MonsterBuildSettings.cs` 抽取内部字段绘制和提示文字。新增回调直接取得原 Profile 的业务子资产，使用 SerializedObject 编辑，沿用 Undo／Redo；没有额外配置存储。保存按钮通过 delayCall 延后执行，捕获点击目标，重新检查资产存在、schema 和编辑器状态，只调用当前 Profile／业务子资产的 SaveAssetIfDirty；异常显示并记录 Console。多选、缺组件、schema 不支持不自动修改资产。普通查看不创建组件、保存、应用宏、激活、计算完整输入摘要或构建。新增类型和辅助方法均为内部接口。

### 本轮实际编译与规则回归

实际命令：

```powershell
& .\Logs\BuildProfilesResume\profile-inspector-20260924-180837-627-460e4ef4\run-tests.ps1 `
  -Label editor-tools -NoGraphics -TestFilter 'MonsterSupergroup.EditorTools.Tests'
```

直接运行 Unity 6000.3.21f1，Windows-Dev-Kcp 活动参数，`-batchmode -nographics -runTests -testPlatform EditMode`，没有 `-quit`；隐藏窗口，超时 1800 秒，仅管理本轮进程。PID **51008**，10:10:23.0533522–10:11:04.5945642 UTC，真实退出 **0**、未超时。

XML 实际 **46/46 通过、0 失败、0 跳过**：BuildIdentityTests 23、BuildRecipeTests 15、ProjectToolTests 8。没有新增测试或借用历史通过数。正式设置启动前／退出后均为 A（22323 字节、SHA `3c2d8d83…bd59`）；与基线比较，原清单仅 MonsterBuildSettings.cs 变化，新增 Inspector 源码／meta 单独留存。`test-audit.json`、`tested-source-hashes.json`、受测源码副本及 `editor-tools/` 中的 XML／日志／执行参数构成实际证据。

上述测试确认编译与既有业务规则，**不宣称 Inspector 回调已在实际界面显示、Undo／Redo 或保存持久化已通过**。

### 真实 Inspector 验收待执行

完整测试通过后准备两个临时资产，均位于正式模板根目录之外的 `Assets/BuildProfileInspectorValidation-460e4ef4/`：`Kcp-Inspector-Edit.asset` 用于编辑、撤销、保存；`No-Business-Settings.asset` 用于缺组件提示。文件 GUID 独立，正式 KCP 模板未改变；初始副本／哈希在 `fixtures-before/` 和 `fixtures.json`。49 项正式 Profile／项目设置的查看前清单为 `preview-before.json`。

已通过 `open-editor.ps1 -Label inspector-manual` 启动可见 Editor **PID 59360**，不传 activeBuildProfile／executeMethod；准确命令、日志和最终退出结果由 `inspector-manual/` 记录，exec session 65436 等待正常关闭。窗口操作继续由用户执行。当前依次待完成：只读浏览与原生设置可见、多选／缺组件提示、临时副本 Network 从 Kcp 改 Steam 的 Undo／Redo／保存、自定义窗口读取相同值，以及正常关闭重开后的持久化与单次注册。

完成后才清理本轮临时资产并归档最终输入、日志及文档增量；不打包或启动 Player。不将本次界面验收转成宏切换、构建或故障注入矩阵。完整手动步骤在档案 `MANUAL-CHECK.md`，当前状态为实现及编译回归完成、真实界面待验收。

### 显示确认与显式保存澄清

用户报告“显示正常，继续任务”。10:19:45 UTC 独立检查时，49 项正式 Profile／项目设置中只有 Windows-Dev-Kcp.asset 变化；新增扩展与共享绘制源码均与受测版本一致，两份临时资产尚未变化。日志布局错误 0、C# 编译错误 0。Unity PID 46252／25180 是本轮 Editor PID 59360 的 AssetImportWorker 子进程，没有据此误判为并行 Editor。记录在 `preview-audit.json`、`inspector-manual/preview-log-audit.json` 和脱敏日志。

用户随后明确操作为：**修改用途 → 保存 Profile → 改回用途 → 再保存 Profile**。文件唯一差异是 `m_HasScriptingDefines: 0 → 1`，用途已回到 product，其余业务参数及符号数组不变。本机 UnityEditor.CoreModule.dll 的 BuildProfile.ValidateDataConsistency 中存在“符号列表非空且标记为 false 时置 true”的逻辑，OnEnable 调用该方法；与 [Unity 6000.3 官方源码](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/BuildProfile/BuildProfile.cs) 一致。反编译片段、原始保存后资产与独立差异在 `inspector-manual/unity-validate-data-consistency.il.txt`、`kcp-after-user-save.asset`、`kcp-user-save.patch`。

结论：**显示正常由用户确认；本次包含显式编辑／保存，不计为只读浏览通过，也不能把标记落盘认定为新增回调自动应用宏。** 用户保存结果保留，不修改产品代码、不覆盖正式 Profile；后续正式文件比较以 `preview-recheck-before.json` 的 49 项用户保存后快照为起点，原始基线和第一次变化仍保留。活动 Profile 状态、纯浏览、Undo／Redo、临时副本保存和重开持久化继续按原计划核对。正式 KCP 相对任务初始基线的这一项用户保存变更，在最终交付中单独报告。

### 只读复核与临时副本编辑保存通过，重开待验

用户确认后续 **“1–4 显示正常”**，对应补做的正式 Profile 只读浏览、KCP 仍 Active、多选提示、缺组件提示、临时副本 Kcp → Steam 的 Undo／Redo／明确保存，以及自定义窗口显示 Dev／Steam／Direct／Normal。Undo／Redo 的中间状态和活动状态来自用户界面确认，不冒称由磁盘快照独立重建。

10:31:44 UTC 的独立核验结果归档在 `edit-check/audit.json`、`fixture-diff.patch`、副本资产及 meta：

- 49 项正式 Profile／项目设置相对 `preview-recheck-before.json` **数量和 SHA 全部一致**，本轮受测 EditorTools 源码未变化。最初正式 KCP 那一项用户显式保存的标记变更仍保留，没有回滚或悄悄忽略。
- 临时 Kcp-Inspector-Edit.asset 实际 **2508 字节**，SHA `503009f74192618c57c1e57ef41900f26e082e83708491c773ca3b99148fdb7b`。业务字段仅 Network 从 1（Kcp）改为 0（Steam）；宏列表完全未变。额外一个原生字段是保存时将已由 Unity 加载一致性逻辑置位的 `m_HasScriptingDefines` 从 0 持久化为 1。独立逐字节比较准确匹配这两项变化，其他字段不变。
- 缺业务组件的临时资产及两份 meta 与创建时相同，未自动添加组件或重写 GUID。
- 采样日志 58621 字节，原始 SHA `8a7cbc3b386d7949f720971aaa158f77c5bbf1d702a028a86595f2bc8e571498`；布局错误 0、C# 编译错误 0、扩展保存失败消息 0。脱敏副本和原始采样摘要在 `edit-check/unity.redacted.log`、`log-audit.json`。

本轮 Editor PID 59360 仍运行，退出观察脚本仍存活。PID 46252／25180 是其导入 worker；另发现 PID 51512 为其他任务在 `Logs/BuildHashValidation/20260924-182544-407/project` 启动的独立 batchmode 项目，不是本轮 Editor 或受测项目根目录，不接管、不终止，也不使用其结果。

目前已完成编译回归、真实显示、只读复核、提示以及编辑撤销／保存检查；剩余正常关闭、重开后的持久化和区域不重复检查，以及本轮临时副本清理。没有新增产品改动、重复测试、构建或运行 Player。

### 关闭请求后的等待状态

用户报告“已关闭”后，执行记录仍为 endedUtc／exitCode=null，Editor **PID 59360** 和本轮导入 worker 仍存活；观察脚本仍在等待真实进程结束。日志最后写入时间为 **10:34:16 UTC**，末尾包含 Input System ShutdownInProgress／Shutdown，但这不等同于 Editor 退出。持续观察期间未取得进程退出结果，未启动第二个同项目 Editor、未强制结束进程，也未移除本轮临时资产。已向用户询问是否还存在保存或退出提示窗口；不据此推断 Inspector 扩展导致关闭停滞。

关闭请求后的文件检查归档在 `inspector-manual/close-request-audit.json`：已保存临时副本与 edit-check 快照完全一致，ProjectSettings 与启动前一致；相对编辑检查，正式 Sandbox Profile 新出现 `m_HasScriptingDefines: 0 → 1`，其余业务参数、符号和场景未变。该标记变化与此前核查的 Unity 数据一致性行为相符，但本次 Sandbox 的具体保存触发没有独立证据，故只记录事实并保留变化，不自动恢复。原始副本和差异为 `sandbox-during-close.asset`、`sandbox-during-close.patch`。

`settings-after-close-request.asset` 仅表示关闭请求后的采样，**不是已退出快照**。正常退出和重开后的持久化／单次业务区验证仍未完成；已有显示、编辑保存与测试结果继续有效，不能用它们替代退出证据。下一步先取得真实退出结果，再恢复原定重开检查，不扩大为通用退出故障诊断。

用户随后要求等待另一个构建测试任务结束。已等待任务“制定Hash校验排除方案”完成；其构建结果仅作为等待条件，不计入本项验证。**10:48:30 UTC** 本轮 PID 59360 及其导入子进程 46252／25180 仍未退出，日志仍为 60524 字节、SHA `419223c32f3525d623a68e2ceb3b859c02a4704d3d7afae43c4b831a2209b559`，布局错误 0。确切进程身份、创建时间和日志已留存在 `inspector-manual/shutdown-pending.json` 及脱敏日志。已请求用户选择是否仅结束本轮残留进程并重开；选择未到达前不执行。即使之后恢复成功，也不能将本次强制结束记为正常退出通过。

### 用户授权结束残留进程并重开

用户明确选择“结束本轮残留进程并继续重开检查”。执行前核对程序名、项目路径、创建时间及导入 worker 的父子关系，打开进程句柄以避免 PID 重用；仅处理本轮 59360／46252／25180。操作时间 **10:49:55.6343889–10:49:56.1643981 UTC**，记录在 `inspector-manual/forced-recovery.json`。原观察脚本取得 Editor 真实退出时间 **10:49:55.9277440 UTC**、退出码 **-1**；导入 worker 记录退出 0，不能据此将本轮 Editor 认定为正常退出。本次关闭停滞原因未确认，不归因于 Inspector 改动或另一任务。

**11:00:23 UTC** 重新核对并保存 `post-force-audit.json`：46 项受测源码 SHA 均一致；正式 ProjectSettings 与退出快照均为 22323 字节、SHA `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`；临时编辑副本仍为已保存的 2508 字节、SHA `503009f74192618c57c1e57ef41900f26e082e83708491c773ca3b99148fdb7b`。49 项正式文件相对 `preview-recheck-before.json` 仅已知 Sandbox 标记变化，精确匹配关闭等待时归档；正式 KCP 的用户保存结果继续保留。当时没有 Unity 进程残留，未覆盖资产。当前正式文件另存为 `restart-before.json`，用于这次重开比较。

重开实际命令：

```powershell
& .\Logs\BuildProfilesResume\profile-inspector-20260924-180837-627-460e4ef4\open-editor.ps1 -Label inspector-restart
```

Unity 6000.3.21f1 可见 Editor **PID 55716**，开始时间 **11:00:28.5779007 UTC**；仅传项目路径及独立日志路径，不传 `-activeBuildProfile`、构建或测试参数。记录在 `inspector-restart/execution.json` 和 `unity.log`，观察会话 25604 等待用户最后正常关闭。本次重开后的配置显示、单次业务区域及 KCP Active 正在等待用户核对；尚不能宣称持久化验收已完成。既有编译及 46/46 回归不重复运行，其他任务的新改动保留且不计入本项结果。

### 重开后的持久化与界面确认通过

用户报告“已确认无误，窗口以打开”，确认三项：临时 Kcp-Inspector-Edit 仍显示 **Dev／Steam／Direct／Normal**；项目业务配置只出现一次，原生设置仍可见；正式 Windows-Dev-Kcp 仍为 Active。界面结果来源于用户确认，没有使用磁盘文件冒充界面证据。

**11:09:01.8246934 UTC** 独立检查记录在 `inspector-restart/confirmed/audit.json`：49 项正式 Profile／设置与 `restart-before.json` 一致，46 项受测源码与测试归档一致；临时编辑副本仍为 SHA `503009f74192618c57c1e57ef41900f26e082e83708491c773ca3b99148fdb7b`，其 meta、缺组件副本及 meta 也均与预期一致。四个临时文件、正式设置和脱敏日志已另存。重开日志采样 37482 字节、原始 SHA `65570db742bc7fa617cf468a1f4851611b74d122b5233843b090a3c0517c3d41`；EndLayoutGroup 错误 0、C# 编译错误 0。正式 KCP／Sandbox 的既有原生标记变化仍保留，没有新增业务配置变化或自动应用符号。

至此，真实显示、只读浏览、多选／缺组件提示、Undo／Redo、显式保存、共享配置读取及重开后的保存值和单次业务区检查均通过。本次持久化恢复发生在首次 Editor 异常结束之后，不能代替“首次正常关闭并重开”的原始验收条件。已请用户正常关闭当前 PID 55716，尚待取得其真实退出结果及清理本轮临时副本；不追加测试、构建或启动 Player。

### 最终退出与临时副本清理完成

用户报告“已关闭”。观察脚本取得重开 Editor **PID 55716** 的真实结束时间 **11:13:31.6227111 UTC**、退出码 **0**；未强制结束该进程。11:19:54 UTC 独立核验时无 Unity 进程，保存结果在 `final/exit-audit.json`：

- 正式设置、重开启动前和退出后原始快照均为 **22323 字节**、SHA `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`。
- 49 项正式 Profile／设置与重开前基线一致；46 项受测源码与实际测试归档一致，清单分别为 `final/formal-after.json`、`final/source-after.json`。这不表示全仓库没有其他任务改动。
- 临时编辑副本及其余三个文件均与用户确认时的预期保存版本一致。最终原始副本保存于 `final/fixtures/`，目录 meta 也独立归档。
- 完整重开日志 **42475 字节**，原始 SHA `5b2d1c59c954f86cb8c5ee1f9f7a68af23ddfa99171fb63b8740704214537aa8`；EndLayoutGroup 错误 **0**、C# 编译错误 **0**。原始日志保留在 `inspector-restart/unity.log`，脱敏副本为 `final/unity-after-exit.redacted.log`，没有将这两个定向检查扩大为所有日志无错误。

清理前重新确认没有 Unity 进程，解析并核对目标绝对路径恰为 `F:\UnityStore\MonsterSupergroup\Assets\BuildProfileInspectorValidation-460e4ef4`，排除目录／文件重解析点及额外文件，并复算每个源文件与归档副本的 SHA。**11:20:33.1283685–11:20:33.1565730 UTC** 使用 `Remove-Item -LiteralPath` 逐项删除四个已归档文件、空目录及对应目录 meta，没有递归清理；目标均已不存在。确切路径、归档位置及哈希在 `final/cleanup.json`。正式模板、源码及其他任务文件均保留。

| 验收维度 | 本轮最终结果 |
|---|---|
| 编译与完整 EditorTools | 修改后实际 46/46、0 跳过、Unity 退出 0；收尾没有重复运行或借用其他任务结果 |
| 普通 Profile Inspector | 用户确认业务字段正确、原生设置可见、多选和缺组件提示正常；未接入 Build Profiles 内置面板 |
| 只读、编辑与保存 | 只读复核文件不变；Undo／Redo、显式保存与自定义窗口共享配置通过；不自动应用符号或激活 |
| 重开与持久化 | 保存的 Dev／Steam／Direct／Normal 保留，业务区一处，正式 KCP Active；来源为用户确认及独立文件核对 |
| 退出与清理 | 重开 Editor 正常退出 0，临时副本完成归档和清理；首次 Editor 仍记录为异常退出 -1 |
| 保留限制 | 首次正常关闭再重开的原始链路没有按原条件通过，关闭停滞原因未确认；不据恢复成功宣称原因已修复 |

正式 KCP 和 Sandbox 相对任务初始基线各保留一项原生 `m_HasScriptingDefines: 0 → 1` 变化，业务值、宏数组及场景未变。KCP 来自用户明确的编辑／恢复／保存操作；Sandbox 的具体保存触发未独立确认。这两项均没有自动覆盖或迁移。其余并发诊断源码、工具和文档改动原样保留。

产品实现、使用指南、功能验收及清理到此完成，结果汇总在档案 `RESULTS.md`；本次未打包、启动 Player、扩大故障矩阵或提交代码。下一项仍可按原计划单独安排成功包的 Player 身份与篡改拒绝验证，其他 Profile、TMP／必要恢复对照和迁移收尾继续分批推进。

## 2026-09-24 KCP Player 身份与单字段修改拒绝

### 已有包、用户操作与证据来源

复用布局修复回归成功包 `20260924T090326117Z-7d442108`，Profile GUID `b441d96b1c4f7a3469a6a2f3fc2fb88e`，配置为 product／Dev／Kcp／Direct／Normal、版本 0.0.1。原包位于 `Builds/ProfileValidation/KcpLayoutFix-20260924-165242-984-2128ffae/MonsterSupergroup-v0.0.1-dev-20260924T090326117Z-7d442108/`，同级 `KcpIdentity-Tampered/` 是用户实际运行的独立修改副本。收到日志时副本已经存在，没有重建、重复修改或追认由本次自动化创建。

用户保存并提供 `C:\Users\ADMIN\AppData\LocalLow\YStarGaming\MonsterSupergroup\normal-player.log` 和 `tampered-player.log`。本次接续只核对并归档，不启动 Unity／Player、不重新打包、不重复 EditorTools 测试。仓库仍为原主工作区 `master / fb28b76e9588e01006debe0e4f8277f94634fa2d`，既有及并发改动保留，暂存区为空。

档案：`Logs/BuildProfilesResume/player-identity-20260924-204742-160-98fa4781`。原始日志、事后清单、实际受测包记录、源码副本、差异、用户确认和核验脚本均已保存。日志原始哈希如下：

| 日志 | 字节数 | SHA-256 |
|---|---:|---|
| normal-player.log | 56636 | `bcf825552cce9cd8d6f661c9e1426ed156688853a5699237800a5d7994f0e4c3` |
| tampered-player.log | 28347 | `79a420a451fa84f79ec24e09b80f451fa01f9971151cbc3554b3566074b83120` |

### 实际核验与最小对照结果

执行命令（退出 **0**）：

```powershell
python .\Logs\BuildProfilesResume\player-identity-20260924-204742-160-98fa4781\audit.py
```

结果在 `audit.json`、`log-events.json`、`original-files.json`、`tampered-files.json`、`BuildInfo.diff`。脚本实际比较每份包 **482 个文件**的路径、长度及 SHA，文件集合相同，唯一差异为 `MonsterSupergroup_Data/StreamingAssets/BuildInfo.json`。原始字节精确等于将唯一的 `"gameVersion": "0.0.1"` 替换为 `"gameVersion": "0.0.2"`；不存在换行归一化或其他字段变更，其他 481 个文件相同。两个包中 contentHash／inputHash 也相同，拒绝并非由重算这两个摘要触发。

原包 EXE、BuildInfo、MonsterSupergroup.Build.dll、UnityPlayer.dll、初始／最终计划和完成标记共 **7 项**，重新计算的长度及 SHA 全部匹配成功构建时的归档。没有测试前全包 482 项清单，故只对上述历史覆盖范围宣称原包未变；本次全包清单是事后采样。Profile 指针与历史退出归档逐字节一致，仍指向原成功包，没有把修改副本发布为新结果；见 `pointer-audit.json`。

| 验收项 | 正常包 | 修改副本 |
|---|---|---|
| 日志中的实际加载路径 | 原成功包目录 | KcpIdentity-Tampered 目录 |
| BuildInfo.gameVersion | 0.0.1 | 0.0.2 |
| 实际 Player 版本 | 0.0.1 | 0.0.1 |
| 编译能力日志 | Dev／Kcp／tools=True／evidence=False／development=True | 与正常包相同 |
| 身份结果 | `valid=True`（第 96 行） | `valid=False`（第 94 行），第 83 行记录版本或构建配置不一致 |
| 界面身份日志 | v0.0.1-dev 与原 BuildId（第 244 行） | BuildInfo invalid · 请重新安装完整游戏包（第 242 行） |
| 本地联机证据 | 第 388 行出现本地房间 ready | 用户确认点击“创建本地主机”后出现无效包提示，未进入房间 |

两份日志的 BuildId、Profile 身份及 JSON 内容均与各自磁盘文件一致。用户明确回复 **“已点击，出现提示且未进入房间”**，保存在 `ui-confirmation.json`。当前本地入口在 `StartLocalRoom` 中先执行 `RuntimeBuildAvailable`，失败后返回给菜单显示提示，不会进入 KCP 准备与 StartHost／StartClient；该拒绝分支没有独立 Debug.Log。因此界面拒绝以用户确认作为直接证据，不用“日志里没有 ready”替代点击及拒绝事实。

### 校验原理与证据限制

当前运行时实际执行的是 **BuildInfo 元数据与 Player 实际信息的一致性检查**：gameVersion 对比 Application.version，development 对比 Debug.isDebugBuild，kind／工具／取证／网络与编译后的能力比较。错误保存在 RuntimeBuildInfo.Error，非 Editor 的 CanConnect 随之为 false；菜单与联机入口拒绝继续。不会因为身份无效而要求进程自动退出。

contentHash 来自构建时的有效配置，inputHash 来自工程输入与依赖摘要。运行时对此只检查 schema 所要求的摘要长度等结构条件，**没有重算整个安装包或验证两个摘要值对应的实际文件，也没有数字签名认证**。构建／结果读取阶段使用它们做计划与工件记录的一致性核对；本次离线 SHA 则用于证明对照包只改了一个文件。三者用途分开，本次验收不宣称任意篡改均可被发现。

`source-hashes.json` 保存当前源码与包内初始计划的对应关系：BuildInfo、联机校验、本地房间流程及菜单等 5 份运行时源码全部匹配；Resolver 亦匹配，PowerShell 结果读取源码另存当前 SHA，其路径不在该包初始输入清单中。未修改上述实现。

手动运行未同步记录准确启动命令、PID、进程起止时间及真实退出码，统一记为未知；日志中的 Shutdown 与文件修改时间不代替退出 0。正常日志存在栈行交错／截断，保留原文，仅据完整身份行及房间 ready 行记录实际信息，不宣称完整双进程／双账号联机时序已验证。核查时无 Unity 或 MonsterSupergroup 进程，未终止任何进程。

**最终结论**：正常 Player 身份检查、独立副本单字段版本不一致拒绝，以及用户实际点击后的本地联机拒绝均通过。该最小对照完成，两个包保留，不修复或发布修改副本。下一项按原计划推进代表 Profile：产品 Test → Profiler／Evidence → Wisp／专项 Test；非空 TMP／必要事务恢复和迁移收尾仍未完成。本次仅新增证据与文档，没有源码修复、构建、提交、reset 或 clean。

## 2026-09-24 Windows-Test-Steam 代表包构建

### 范围、基线与实际执行

按用户要求推进产品 Test 代表包，以现有 CLI 入口执行一次 Windows-Test-Steam；不清缓存、不安装模板、不自动运行 Player 或上传 Steam，不修改产品源码及发布条件。当前为原主工作区 `master / fb28b76e9588e01006debe0e4f8277f94634fa2d`，暂存区为空；旧 KCP 包、指针及全部既有改动保留。

档案：`Logs/BuildProfilesResume/test-steam-20260924-211326-666-7a238c6b`。启动前清单覆盖与服务输入算法相同类别的源码／程序集定义／响应文件／DLL、Packages、ProjectSettings，再加入正式 Profile 与执行工具，共 **4171 项**；新增／删除也参与前后比较。`before/` 保存正式设置、Profile、EditorTools、Build 运行时及执行工具副本。46 项构建工具相关源码与上次已执行的 Inspector 回归版本一致；本次只做新 Profile 编译及构建，**没有重跑完整 EditorTools 测试**。

准确构建命令在 `run-build.ps1`：

```powershell
& .\Tools\Invoke-ProjectTool.ps1 `
  -ToolId build.player `
  -BuildProfile 'Assets/Settings/Build Profiles/Windows-Test-Steam.asset' `
  -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -Output 'Builds/ProfileValidation/TestSteam-20260924-211326-666-7a238c6b/MonsterSupergroup.exe' `
  -ResultPath 'Logs/BuildProfilesResume/test-steam-20260924-211326-666-7a238c6b/result.json' `
  -TimeoutSeconds 1800
```

工具实际使用 `-batchmode -nographics -activeBuildProfile ... -executeMethod MonsterSupergroup.EditorTools.NativeBuildEntry.Batch`，隐藏窗口，无 RunAfterBuild／清缓存参数。Unity **PID 60792**，开始 **13:14:20.4286306 UTC**、结束记录 **13:17:41.3199838 UTC**，真实退出 **0**、CleanupRequested=false；工具报告耗时 **201.213 秒**。日志、原始参数及进程记录均复制到 `tool-logs/`，汇总为 `execution-audit.json`。本轮只启动该 Unity，没有启动 Player。

### 实际产物与编译能力

BuildId **`20260924T131532148Z-d824d3a7`**，Profile GUID **`4fecdf51023441945ba7a87276c9a4c5`**。实际 EXE：

```text
F:\UnityStore\MonsterSupergroup\Builds\ProfileValidation\TestSteam-20260924-211326-666-7a238c6b\MonsterSupergroup-v0.0.1-test-20260924T131532148Z-d824d3a7\MonsterSupergroup.exe
```

执行 `verify-package.ps1`，退出 0，**19 项包核对全部为 true**，这是产物检查数，不是 Unity 单元测试数：

| 维度 | 本轮实际结果 |
|---|---|
| 业务身份 | product／Test／Steam／Steam／Normal，版本 0.0.1，schema 3 |
| 原生计划 | Windows x86_64，Development／调试／Profiler／深度分析／等待调试均关闭，压缩值 3（LZ4HC） |
| 场景顺序 | Boot → MainMenu → Gameplay |
| 实际 BuildFeatures DLL | CompiledKind=Test、CompiledNetwork=Steam、ToolsCompiled=false、EvidenceCompiled=false |
| 程序集／分发文件 | 无 MonsterSupergroup.*Tests*.dll；steam_api64.dll 存在；无 steam_appid.txt、combat-build.json |
| 发布一致性 | EXE、BuildInfo、初始计划、完成标记及本次 Profile 指针一致；暂存目录已移除，没有本次失败隔离目录 |
| 请求 | cleanBuildCache=false、runAfterBuild=false |

`package-audit.json` 保存实际 DLL 的 Cecil 指令、文件长度及 SHA；`package-records/` 保存包内计划、请求、身份与标记。不是仅依赖 JSON 自证。现有 `Resolve-ProjectBuildExecutable` 能按 Test-Steam／Steam／Normal 选中本包，附加 RequireDevelopmentTools 时正确拒绝；结果在 `result-selection-audit.json`。没有重跑两种 PowerShell 的完整 44 项回归，因为正式执行／结果逻辑未修改。

`compilation-audit.json` 与两个 `.rsp` 副本确认：Editor 和 Player 编译均仅含业务宏 `MONSTER_BUILD_TEST`，没有旧 Dev／Tools／KCP 宏；Player 没有 DEVELOPMENT_BUILD、UNITY_INCLUDE_TESTS，Editor 的 UNITY_INCLUDE_TESTS 只用于 Editor 测试环境，不属于随包测试程序集。实际裁剪后 Build DLL 与交付 DLL 的 SHA 相同。Library/BuildProfileContext 缓存也从三个 KCP Dev 宏切换为 MONSTER_BUILD_TEST。这里只验证批处理编译与宏，不扩大为交互式 Editor PlayMode、所有插件行为或 GUI 重开后的活动 Profile 持久化均已通过。

### 六阶段与设置恢复

内部采集目录由本次 BuildId／Profile GUID 唯一匹配为 `input-20260924T131525785Z-f0cb581268734399921190b341de2691`，完整副本在档案 `input-evidence/`。执行 `analyze-lifecycle.py`，退出 0，六阶段全部 Captured、最终 Complete，无采集错误；长度、SHA、时间顺序及初始／最终计划输入条目均独立核对通过。

- 初始计划与调用 Unity 前：22323 字节，SHA `3c2d8d83b25bb5bb93e96437ca2ae1ca4bc20ad792e14cf00a35cd64e912bd59`。
- Unity 返回至最终计划：22395 字节，SHA `64027d29de779234b5c861a3b70a415605c9e87b6759d7a09c7e7d69c92629af`。
- 独立文本差异仅为 preloadedAssets 增加 GUID `99f9c9493070a9d4c979a8fec7c5a8d3`；首次变化区间为调用 Unity 前→Unity 返回。身份清理、当前字体状态下的 TMP 恢复阶段没有额外设置差异。不能据阶段区间直接归因具体回调。
- 进程退出后恢复到启动前原始字节，完整过程 A→B→A。所有正式 Profile 与设置均无本轮新增磁盘变化；已有 KCP／Sandbox 原生标记变化保留。

contentHash 首尾同为 `a2303516fa63979b791952eb823b7a563cd754b28068ebee209cfaa39b7e394a`；inputHash 从 `f90d7c914b85d9fa5467f0738ea19b21b6bf1393dc63d8023f70a0551fb63c92` 变为 `0a6c061f09ba60fd989f00b183331c6b12594aab2bb7551d7d18552ccffd7858`，服务报告警告并保存构建后计划，未以不相等拒绝发布。完整比较在 `lifecycle-audit.json`、`independent-diff.txt`。

### 并发输入变化、日志保留项与结论

输入前后比较发现：`Assets/_Project/Tests/EditMode/NetworkCombat/CombatEvidenceIntegrityTests.cs` 在 **13:15:25.6969093 UTC** 发生并发变化，早于初始计划快照 **13:15:31.8250001 UTC**。启动前 SHA 为 `caa90d178fde7159494ec261ff55d93add0c1f4ef8548e06d51397bfc80cb7ce`，初始／最终计划及退出后均为 `dad2f5a5b642766ee2351969f73b3d73be11b4070bddbab66dac2c11f41410ce`。其 asmdef 限定 Editor，该测试程序集未进入 Player；源码及 asmdef 副本已留存，未覆盖该改动。

13:18:06 UTC 的退出后全输入采样中，除该测试源码外，其余 4170 项与启动前一致。收尾期间又观察到 **13:20:53 UTC** 的 CombatEvidence.cs／CombatEvidenceRuntime.cs 改动，均晚于 Unity 退出；它们已与本包输入清单不同，证据为 `concurrent-source-observation.json`。因此，**本包的交付和实际能力通过，不等同于严格启动前冻结输入的完整回归，也不能作为后续并发源码已验证的基线**。下一次构建必须重新冻结输入；本轮不自动重复构建追赶其他任务。

完整 Unity 日志 **1792085 字节**，SHA `e37bab43353c2a932f93dc180b3f9e547a6ed1c4dde2fbd9dd996deb17bdbf0d`。C# 编译错误 0、布局错误 0、服务成功发布一次；保留两条 TMP Fallback 导入不一致，以及启动时许可握手／访问令牌更新错误，后续构建正常完成。原始和脱敏日志分开归档，不把退出 0 写成日志无任何错误，也不扩展到授权或字体问题修复。

**本轮交付结论**：一次 Test-Steam 构建成功、19 项包核对通过、六阶段证据完整、进程退出 0、设置恢复及 KCP 旧指针保留。**尚未验证**：Steam 渠道启动与真实 Player、双账号体验、交互式 Editor 运行、非空 TMP／必要失败恢复，以及后续并发源码；这些没有借用 KCP 或历史测试结果补齐。本轮未提交、reset、clean 或修改产品接口。下一批待并发改动稳定后建立新基线，再推进 Profiler／Evidence，避免直接沿用本包覆盖当前变化。

## 2026-09-24 Windows-Test-Steam 双账号人工验收

用户在收到双账号验收步骤后回复 **“已完成验收”**。前述步骤包括两端有效身份及 Steam 初始化、A 建房与 B 加入同一局、实际交互同步、退出房间及重新加入；按用户整体确认记为 **人工验收通过**，不虚构逐项截图或双端日志。证据目录：`Logs/BuildProfilesResume/test-steam-manual-20260924-214701-c6b9ddf9`。

实施者随后只读取、归档本机 `C:\Users\ADMIN\AppData\LocalLow\YStarGaming\MonsterSupergroup\Player.log`，没有启动客户端、追加构建或上传 Steam。原始日志 54746 字节，SHA-256 `368b3c12059086af9224f646dbc218ce5b9e73a0343900566db7dc6bb0c72284`，文件最后修改时间为 2026-09-24 13:45:29 UTC。`manual-acceptance.json` 保存用户原话、采样时间、日志哈希与独立核对的记录：

- BuildId `20260924T131532148Z-d824d3a7` 与本次成功 Test-Steam 包一致；版本 0.0.1，kind=Test、network=Steam、tools=False、evidence=False、development=False、valid=True。
- 运行后端为 Steam，transport=FizzySteamworks；Steam AppID 4886160 初始化成功，并有 Hosting Lobby 记录。
- 双端入房、同步与退出重入的结论来源于用户人工确认。没有取得对端日志及 BuildId、完整双端时序、准确启动命令／PID／进程起止时间／退出码，也没有核对 Steam 上传或分支发布记录；不以 Shutdown 字样推定退出 0。

原始日志另有磁盘／分区信息查询错误，按原文保留，不扩大为“所有日志无错误”，也不在本轮追查。当前仓库仍为主工作区 `master / fb28b76e`，暂存区为空，其他任务的并发改动原样保留。本项仅验收上述已归档成功包，不覆盖构建后源码变化；先前六阶段和 19 项包检查引用其已执行记录，没有重新执行或重复计数。

**当前结论**：Windows-Test-Steam 构建与包核对通过，生命周期证据完整，真实 Steam 双账号人工验收由用户确认通过。后续先为稳定后的当前源码重新建立基线，再依次推进 Windows-Test-Profiler、Windows-Test-Evidence；交互式 Editor 行为、非空 TMP／必要事务恢复、迁移收尾和 Shipping 干净提交仍分项保留。本轮到此停止，没有产品代码修改、提交、reset 或 clean。
