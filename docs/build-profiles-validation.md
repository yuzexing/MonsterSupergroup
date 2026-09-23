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
