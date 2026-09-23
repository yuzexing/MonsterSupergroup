# 战斗证据续作：执行与验收记录合同

本文件规定怎样产生可核对的新结果；命令本身不代表验收通过。原始事故、失败结果和历史构建均保留。新结果单独写入 `Logs/CombatEvidenceNextValidation`，不覆盖 `Logs/SteamIncident-20260922` 或 `Logs/CombatEvidenceV2Validation`。

## 独立快照和结果身份

旧 `Logs/SteamNetworkValidation/project/Assets`、`Packages` 是指向主工程的 junction，不能作为独立源码快照。新入口使用 `Logs/CombatEvidenceNextValidation/project`，物理复制 Assets、Packages、ProjectSettings，以及统一构建使用的 `docs/editor-tools/catalog.json` 与 `steam_appid.txt`，拒绝链接和共享硬链接，保留该独立工程自己的 Library。同步时将已不存在的旧输入移到当次证据目录，不删除主工程文件。

从项目根目录运行：

```powershell
./Tools/Invoke-CombatEvidenceValidation.ps1 `
  -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -TestFilter 'MonsterSupergroup.NetworkCombat.Tests.CombatEvidenceRecoveryTests'
```

用 `-TestPlatform PlayMode -TestFilter '完整测试类名'` 选择物理场景测试。`-PrepareOnly` 只同步和归档，不启动 Unity。对同一个独立工程不得同时启动两次测试，也不得在 Unity 打开时同步。首次导入后若 Unity 补生成 `.meta`，本轮输入哈希检查必须失败并列明文件；补齐主工程的对应 `.meta` 后重新同步、重新测试，不能忽略哈希差异。源码或配置变更后的通过结论必须来自新快照。

验证历史实现时，可指定 `-OverlayDirectory '旧 C# 文件目录'`。目录内每个文件名必须唯一对应现有源码；入口保存旧文件副本、原 SHA、覆盖后 SHA，并在覆盖后固化受测工程 manifest。红测应确实在旧实现上失败，绿测必须去掉 overlay；不能用失败期望值的合成夹具代替产品实现的真实故障。

每次输出包括：

| 文件 | 用途 |
|---|---|
| `identity.json`、`source-before.json` | Unity 可执行文件 SHA、工程 Unity 版本、Git HEAD、全部输入及工具 SHA |
| `git-status.txt`、`dirty.patch`、`tool-sources/` | 未提交状态、跟踪文件的完整二进制 diff、执行工具的实际副本；未跟踪项目输入保存在物理快照并列入 manifest |
| `sync.json`、`validation-before.json` | 实際受测快照的 SHA、同步记录及红测覆盖信息 |
| `tested-code-and-config.zip` | 当次受测代码、场景/Prefab/序列化资源、meta及Packages/ProjectSettings；包含未跟踪源码及红测旧文件，复用工程下次同步后仍保留。大型二进制媒体由完整manifest标识，不能把此源码归档当完整游戏包 |
| `invocation.json` | 精确程序路径、参数数组、环境变量和启动时间 |
| `results.xml`、`unity.log`、`execution.json` | 实际测试与退出结果；空测试或没有任何通过项目不能当成功 |
| `source-after.json`、`validation-after.json`、`integrity.json` | 前后 SHA 与具体变化；工作区并行编辑与受测快照变化分别报告 |
| `artifact-manifest.json` | 本次全部证据文件的长度和 SHA |

`sourceUnchanged=false` 表示结果对应冻结快照，不能冒充后来修改的工作区；`validationInputsUnchanged=false` 表示测试过程中受测输入变化，本次结果不能成为稳定验收。主工程的版本、资源、既有修改不会由同步入口更改。

完成审计和基准比较使用本次 `tool-sources/Tools/` 内的归档脚本。它们与准备快照时的代码一致，避免工作区工具后续增加新的输入种类，使一次运行的前后 SHA 失去可比性。手工重算旧结果也应使用它自己的归档版本；使用新工具重算时另存结果并记录工具 SHA。

## CPU、存储与复制负载

主性能矩阵为 50 / 200 / 500 个状态控制器 × off / local / replicated × 3 次 × 180 秒，144 Hz 逐次执行，正常生产绑定为 `shared-replica-root`。复制模式是**总共两端**：primary 与一个模拟 remote；两者在一个进程内共享诊断预算。三端 Host 中继、来源去重、断线/ACK 丢失另作为正确性专项，不混入该性能矩阵。

```powershell
# 一秒短检查：只证明短检查本身
./Tools/Invoke-CombatEvidenceBenchmark.ps1 -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' -Smoke

# 正式阶段短测：每个组合五秒，三次重复，全部27份报告
./Tools/Invoke-CombatEvidenceBenchmark.ps1 -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' -Seconds 5 -Repeats 3

# 单次历史性能对照，不作为阶段门槛
./Tools/Invoke-CombatEvidenceBenchmark.ps1 -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' -Seconds 5 -Repeats 1

# 完整 CPU/存储矩阵
./Tools/Invoke-CombatEvidenceBenchmark.ps1 -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' -Seconds 180 -Repeats 3 -CatchupSeconds 45
```

不增加 warmup、不减少输入数量来美化与旧负载的比较。超时执行必须保留实际帧数、耗时、频率及 deadlineMisses。当前严格工具要求频率至少为目标 144 Hz 的 99%，作为时钟测量容差；原始实际频率始终报告。

补传期间继续按 144 Hz 调用复制 Tick，覆盖水位和目录检查约每 50 ms 一次；`drainCadence` 记录实际 Tick 频率和检查次数，不能把 20 Hz 测试调度造成的单块等待当作游戏 144 Hz 下的复制吞吐。45 秒截止、水位和零丢失要求保持不变。单次重复的断言失败在同组合全部重复完成后汇总，仍保存后续重复的报告。

`comparison.json` 中 CPU/存储 gate 必须同时满足：

- 每个数量、模式、重复编号恰好一份报告，存在匹配的 off 基线，实际逐帧样本数与输入帧数一致，计划及实际负载覆盖满足要求。
- 每组对应 off 的 P95 增量 ≤ 1 ms、P99 增量 ≤ 3 ms；无 NaN、缺测或缺基线被当成零。
- 关键记录丢失为零。本轮正常负载进一步要求总丢失为零且每端覆盖有限区间 `[1, producedRecords]`：`produced >= flushed >= 请求尾`、gaps 为空、无 failure、recoveryPending=false、queuedBytes=0。来源仍打开时允许未知未来尾部，但 `coverageInterval.wholeSourceClosed=false`；不能以“保留的文件已复制完”为完整采集，也不能要求有限完整区间必须等到整个来源关闭。
- 统一诊断预算峰值 ≤ 128 MiB、每端队列 ≤ 32 MiB；写盘无故障、业务最终状态与独立关闭日志的执行一致。
- 结束负载后在配置的补传窗口内追平，最大窗口 45 秒，缺失文件/字节清单为空。

`allocationMeasurementGatePassed` 单独报告。Mono 探针不可用时，分配为 null/unknown，短 CPU gate 仍可用于排查；最终内存分配验收保持未完成。`-RequireCompleteMeasurements` 将缺少有效分配测量也变成脚本失败。预算计数、实际分配、进程堆/GC分别呈现，不能互相代替。

`-IndependentEngines` 仍可用于额外冷启动调查，其绑定不同，严格正常性能矩阵不会因此通过。不要把该结果替代生产绑定的结果。`fullDurationMatrix=false` 的短检查、模拟两端数据、每秒观测窗口都不能证明真实游戏完整帧/GPU/物理或 Steam 通过。

离线重算基准：

```powershell
python Tools/CompareCombatEvidence.py --benchmark-root '当次输出/cases' --minimum-seconds 180 --minimum-repeats 3 --catchup-limit 45 --output '当次输出/comparison.json'
python -m unittest discover -s Tools -p 'test_*combat_evidence*.py' -v
```

## 真实来源、复现和修复必须分开

`docs/evidence/combat-evidence-next/real-fixture-source-audit.json` 保存两个现有真实输入夹具的文件 SHA、原始文件/物理行及构建关联审计。它们来自 Build GUID `27fcaf967626475aa37d7d29dcc599f2` 的本地 KCP 单人运行，分别为 gateway 260 步与 replica 1,217 步，均在首个已知缺口之前。它们不是旧 Steam 双人故障现场；审计未执行回放。

```powershell
python Tools/CombatEvidenceValidation.py audit-fixtures `
  --provenance Logs/CombatEvidenceV2Validation/fixtures/provenance.json `
  --build-manifest Logs/CombatEvidenceBuilds/20260922-073148-873/combat-build.json `
  --output Logs/CombatEvidenceNextValidation/real-fixture-source-audit.json
```

统一入口可显式执行现有 `.json` 夹具，不依赖只匹配 `*.fixture.json` 的目录扫描：

```powershell
./Tools/Invoke-CombatEvidenceValidation.ps1 `
  -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -Mode Replay `
  -FixturePath @('Logs/CombatEvidenceV2Validation/fixtures/incident-20260922-gateway-1.json', `
                 'Logs/CombatEvidenceV2Validation/fixtures/incident-20260922-replica-3.json') `
  -Output 'Logs/CombatEvidenceNextValidation/real-fixtures-current'
```

这会在当次输出复制输入夹具、保存 `fixture-sources.json` 与 SHA，再调用 `MonsterSupergroup.NetworkCombat.Editor.CombatReplayBatch.RunDirectory`。详细结果和汇总在当次 `fixtures/replay-results.json`、`fixtures/replay-summary.json`。必须正好执行所列两份夹具，逐份 `executed > 0`、`reliable=true`、`passed=true`，且进程成功退出；空目录、无法读取、缺步骤、不可靠回放不能算通过。需要回放历史实现时使用另一个独立工程或明确的源码 overlay，并保存历史实现 SHA；当前实现通过不等于已经验证旧版本的同一行为。`-ExecuteMethod` 可显式写出上述方法，入口拒绝替换成宽松的回放入口。

真实输入的完整性、实际业务回放、错误定位、修复后通过是四项不同证据。业务闭环必须保留未经篡改的原始证据、产生错误的版本、独立可判定的业务断言、最早分歧记录/字段、旧实现失败与修复实现通过。合成错误期望值只验证工具检错能力。

2026-09-22 已有真实故障中，记录器过载有结构化证据，可用保持真实调用顺序、载荷与速率的压力夹具闭环。旧 Steam 对局虽然有两端发送失败，却没有该场结构化事件，不能补造 EventId 或拿 KCP 回放通过宣称怪物瞬移/阴影已修复。若要求一个真实伤害/DOT/同步业务错误闭环，需等新同包双机采集给出充分证据，再固定业务断言。

## 同包 Steam 双机操作清单

先通过自动化和性能门槛，再交用户进行真实两台机器、两个账号测试；不使用 computer-use。主工程已打开时，从物理验证工程调用统一构建服务，不在主工程启动第二个 Unity：

```powershell
# 固定为完整 product / Test / Steam / Direct / Evidence，Development=false
# 使用全新 Output；与测试、基准串行运行
./Tools/Invoke-CombatEvidenceValidation.ps1 `
  -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' `
  -Mode Build -TimeoutSeconds 14400 `
  -Output 'Logs/CombatEvidenceNextValidation/steam-evidence-build'
```

该入口使用 `MonsterSupergroup.EditorTools.ProjectToolRunner.Batch`，其精确参数见 `invocation.json`，不是仅编译脚本。已核对方法、参数、配方及工件路径，并通过 PowerShell 语法检查；命令尚未运行前不能宣称完成新包。若走 Steam 测试分支，另用统一工具的 `-Distribution Steam` 构建并由用户上传、更新及从 Steam 启动；当前固定的 Direct 入口不会上传。

构建成功以当次 `build-result.json` 的 `success=true`、`pending=false`、退出码 0 和完成工件共同判定。`artifacts[0]` 是实际 EXE，`artifacts[1]` 是 BuildInfo；服务会在要求的输出路径下再加唯一构建子目录，不能按预设文件名查找旧包。可这样查看实际路径：

```powershell
$buildResult = Get-Content 'Logs/CombatEvidenceNextValidation/steam-evidence-build/build-result.json' -Raw | ConvertFrom-Json
if (-not $buildResult.success -or $buildResult.pending) { throw 'Build did not complete successfully.' }
$buildResult.artifacts
```

复制 `artifacts[0]` 所在的完整目录给两台机器，仍需双方登录 Steam。包内必须保留 `<EXE名>_Data/StreamingAssets/BuildInfo.json`、`combat-build.json`、`build-complete.json`、`steam_appid.txt`；开发机本次输出保留 `combat-replay-sources.zip`、快照与 SHA。BuildInfo 应记录 `profile=product`、`kind=test`、`network=Steam`、`distribution=Direct`、`diagnostics=Evidence`、`development=false`、`testAssemblies=false`、`developmentTools=false`、`evidence=true`；完成标记应与 BuildInfo 一致，`combat-build.json` 的 BuildId 应与它一致，协议及源码哈希位于其中的 `manifest` JSON 字符串。`combat-build.json.result` 在构建后置回调中可能仍为 Unknown，不能单独用它断定构建成败。

记录两端实际加载路径、BuildId、Build GUID、协议、资源/源码哈希；仅版本字符串相同不够。不能据历史 `Builds/SteamEvidence` 名称猜包，也不能把 Steam 重启到安装目录的旧包当作新包。独立工程的统一工具详细日志位于 `project/Logs/ProjectTools`，构建失败时保留这些日志及本次 `unity.log`、`execution.json`、`integrity.json`。

两端同一场运行使用相同模式，启动参数为：

未填测量值的参数清单模板位于 `docs/evidence/combat-evidence-next/steam-capture-template.json`；必须另存到当次 Host/Client 证据目录并填入实际值，不能把模板当已完成记录。

| 模式 | 参数 |
|---|---|
| off | `--no-combat-evidence --network-diagnostics` |
| local | `--combat-evidence-local-only --network-diagnostics` |
| replicated | `--combat-evidence --network-diagnostics` |

默认自动保存；可为网络指标另指定 `--network-diagnostics-output=<本次独立目录>`。off 无战斗记录属于预期，独立性能记录仍必须存在。`Start-SteamDiagnostics.ps1` 可准备包/硬件与网络采集，但它不会替用户加入房间，也不自动归档整个 CombatDiagnostics；需要在测试后完整保留该目录。

每次双方记录 Host/Client、场景、画质、分辨率、帧率目标、模式、重复编号、计划怪物数量、实际存活怪物分布、RunId、CaptureId和起止时间。正常游玩不能稳定覆盖目标数量时，将缺组合列为未完成，不能用“计划 500”代替实际覆盖。另验证高输出清场、多来源 DOT、权限交接、死亡/销毁、断线恢复与负载结束后的补传。

出现异常时记下所见对象/场景和大致时间，正常结束并分别保存两端：

- `CombatDiagnostics` 整个目录，以及 `Player.log`、`Player-prev.log`。
- 当次 `NetworkDiagnostics` 性能文件、启动参数、包 manifest、硬件和画质设置。
- 按 Host/Client 分目录；不合并覆盖，不清理原始日志，不把传输成功当成远端业务应用成功。

开发侧随后导入、先评估 coverage，再查询事件/玩家输出/DOT/连接，提取完整区间交实际 Unity 适配器执行。覆盖未知、缺引用、不支持或没有可靠业务断言时，报告限制并继续采证，不给出“已修复真实故障”的结论。

## 两台机器的包校验与退出后归档

`Tools/Scenarios/Export-SteamEvidence.ps1` 复用现有 `Tools/ProjectTools.psm1` 的显式包目录解析，并通过 `Tools/SteamEvidenceIdentity.psm1` 只读校验明确指定的 PID；与会启动/附加游戏的 `Start-SteamDiagnostics.ps1` 分开。本脚本只读取实际包、校验身份、复制选定日志，不启动/关闭/操控游戏，不添加游戏命令或测试入口。给另一台机器同时复制这三个工具文件，并保留原相对目录结构。

先在 Host 校验实际成功包目录；这里的路径必须替换成 `build-result.json.artifacts[0]` 所在目录：

```powershell
./Tools/Scenarios/Export-SteamEvidence.ps1 `
  -PackageDirectory 'D:\本次成功包的完整目录' `
  -ArtifactDirectory 'D:\本次证据\Host-before' -Role Host -Mode replicated
```

复制同一整个包给 Client，也把 Host 的 `machine-proof.json` 复制过去。Client 指向自己的实际目录并比对 Host 的 proof，允许两机安装路径不同：

```powershell
./Tools/Scenarios/Export-SteamEvidence.ps1 `
  -PackageDirectory 'E:\本次成功包的完整目录' `
  -ArtifactDirectory 'E:\本次证据\Client-before' -Role Client -Mode replicated `
  -ExpectedProof 'E:\本次证据\Host-machine-proof.json'
```

脚本从实际 Unity `_Data` 目录找到 EXE 和 BuildInfo，校验完成标记、BuildId、GUID、协议、源码哈希、v2 格式、Test/Evidence/非 Development 配置；记录 EXE 与整个包的逐文件 SHA。它不会自动搜索历史 Builds 目录，也不会按包名推断新旧。`-ExpectedProof` 要求这些字段和包内容均一致。输出为 `machine-proof.json`、`package-files.json`、包身份文件副本和采集工具版本。只校验包时判定是 `PackageIdentityOnly`，不能当作游戏运行证据。

双方正常结束游戏后分别执行，显式选择本次日志和全量 CombatDiagnostics 目录。不要指向仍在写入的共享目录：

```powershell
./Tools/Scenarios/Export-SteamEvidence.ps1 `
  -PackageDirectory 'D:\本次成功包的完整目录' `
  -ArtifactDirectory 'D:\本次证据\Host-after' -Role Host -Mode replicated `
  -ExpectedProof 'D:\本次证据\Host-before\machine-proof.json' `
  -LogPath @('D:\本场采集\Player.log', 'D:\本场采集\metrics', '实际持久目录\CombatDiagnostics')
```

手动正常启动游戏也可验证正在运行的包。游戏运行时，从任务管理器“详细信息”取得该游戏的 PID，再用新输出目录运行；下例 `12345` 必须替换成实际 PID：

```powershell
./Tools/Scenarios/Export-SteamEvidence.ps1 `
  -PackageDirectory 'D:\本次成功包的完整目录' `
  -ArtifactDirectory 'D:\本次证据\Host-running' -Role Host -Mode replicated `
  -ExpectedProof 'D:\本次证据\Host-before\machine-proof.json' -GameProcessId 12345
```

这只查询指定 PID 的 `ExecutablePath`、`CreationDate` 和 `ProcessId`，不枚举其他进程命令行、不调试附加游戏。PID 不存在、可执行路径不同、路径/创建时间读不到均失败。成功时 `runtime.liveProcess` 保存实际路径、创建时间与观察时间；退出后另行归档日志，运行中不要复制仍在变化的日志。包内容 SHA 仍是采集时测量，不证明进程启动以来文件从未替换。

如本场通过既有 `Start-SteamDiagnostics.ps1` 采集，原 `-CaptureManifest '本场目录/capture.json'` 继续兼容。脚本核对其中实际 EXE 路径、PID、开始时间与已有命令行，并保留该证据；同时给出 PID 时还检查两种证据的 PID/创建时间一致。如果两者均未提供，`runtime.attributionVerified=false`，不能把“校验了某个包”说成“游戏实际运行了它”。`Role`/`Mode` 是本次人工标签，不证明角色/参数实际正确；需对照记录与日志。当前日志的 `process.start` 也会保存程序自身读取的 `executablePath`、`processId`、`executablePathVerified`，供事后与 machine proof 交叉核对，本脚本不将这些日志文本直接升级为独立进程验证。

每个选定文件复制前后校验 SHA，变化或空目录使本次失败，保留失败 proof 和已复制文件；重新采集必须使用新输出目录。包不一致、日志复制成功、业务 coverage 完整、回放通过分别判定，脚本不把它们混为一次 Steam 验收通过。

## 2026-09-23 断流接手记录：清理前缀完整性

本轮在 `F:\UnityStore\MonsterSupergroup`、`master`、HEAD `cb276527a3be1e3789fae05bf5a714d641a7aa0d` 接手。原任务 `01a0cc3b-af76-7491-8566-423fb95bce29` 的 cwd 也是此目录；这是原任务的主 checkout，`.git` 与 common git dir 均为 `.git`，没有切换到另一 worktree。`F:\UnityStore\MonsterSupergroup_validation_exit_6000_3_21` 是另一项退出验证的 detached worktree，本轮未使用。

接手时有 52 个已跟踪文件修改、83 个未跟踪文件，暂存差异为空。已检查工作区及暂存差异；完整初始 patch、状态、worktree 列表与 135 个既有变动文件 SHA 位于 [接手档案](../Logs/CombatEvidenceNextValidation/resume-20260923-163707/takeover-before.json)。未执行 reset、clean、暂存或提交，也未覆盖构建迁移等已有改动。

### 状态核对

| 类别 | 本轮核对结论 |
|---|---|
| 已经实现 | 当前代码已有直接登记采集缺口的可选接口、来源序号、故障/恢复历史、主线程恢复检查点、统一 Python 区间判断和严格批次回放；已有有界数值 gap、共享不可变输入、阶段计时及编码优化。这里仅表示代码存在，不代表全部验收通过。 |
| 已经验证（本轮） | 仅下述 Python 清理前缀红/绿测试与相关 110 项回归；有限区间可提取非空夹具、严格 CLI 返回码、跨来源隔离和副本补齐均有实际断言。没有运行 Unity 业务适配器。 |
| 尚未完成 | 当前工作区的 Unity 集成基线、最新优化后的完整五秒三次矩阵、三十秒检查点覆盖、180 秒阵列、真实业务失败夹具及 Steam 双机验收。AdvanceBlock 池化仍只有 `advance-pool-review.md` 草案。 |
| 无法确认 | 旧快照通过能否迁移到当前源码；历史红测是否满足稳定输入门槛；短 Profiler 窗口能否代表持续负载分配；旧 Steam 故障的逐事件因果链。不得据此填“通过”。 |

实际读取的历史证据如下，**没有作为本轮重跑结果**：

- `integrity-green-07`：XML 100/100，退出 0，前后输入哈希稳定；`allocation-workload-01`：XML 3/3，退出 0，九组合约一秒的 Profiler 分配测量。两者来源均为 `frozen-source-pre-build-migration` 组合副本。接手审计时与工作区有 60 项输入差异（18 修改、38 新增、4 缺失），含构建工具/资源和 4 个 NetworkCombat 文件。
- `real-fixture-compatibility`：历史 Gateway/Replica 夹具实际执行 260/1,217 步且可靠通过，退出及输入稳定检查成功；仍是 KCP 真实输入兼容基线，不是 Steam 业务失败证据。上述三次运行的关键归档文件 SHA 本轮核对与各自 manifest 相符，这只确认历史工件未变。
- `integrity-green-06`：退出 1、无 XML，曾因构建类型重复定义未能编译，且受测输入新增 `.meta`。当前代码看似已消除重复定义，但本轮未启动 Unity，不能确认环境阻塞已解除。
- `storage-red`、`recovery-epoch-red` 有实际失败 XML，但 `sourceUnchanged` 和 `validationInputsUnchanged` 均为 false，不能当作稳定版本的红测验收。
- `baseline-5s-02`：历史失败基线保留。`optimized-5s-3`：XML 7/9，退出 2，只有 23/27 份报告；500 控制器 local/replicated 的重复 2、3 缺失，已有记录丢失、CPU 超阈值和复制追平失败，严格 CPU/存储及分配 gate 均 false。它运行时工作区也发生变化。后续优化尚无新的完整矩阵结果。

### 本轮唯一推进项及结果

审计复现阶段一遗漏：`retention.json` / `retention.local.json` 声明已清理序号 4 之前的记录时，默认判定从最早剩余的检查点 4 开始，错误地把 4..6 的完整性当作整份来源通过。另一个入口 `extract(first=1,last=6)` 会以检查点 4 缩窄检查范围，掩盖请求头缺失。因此先关闭此误报路径，暂缓原任务中断前计划的五秒矩阵。

本轮只修改 `Tools/CombatEvidence.py` 的区间/提取判断，新增 `Tools/test_combat_evidence_retention.py`，并更新计划与格式文档：

- 按 capture/run/round 匹配来源清理标记；未显式指定起点时保守返回 `SourcePrefixPruned`，保留原清理文件引用。不能把跨轮次递增的来源序号一律当作从 1 开始。
- 损坏/矛盾的清理边界返回 `InvalidRetention`，不通过完整性判定。
- 显式区间继续检查合并后的记录及依赖，保留 4..6 的完整提取；另一副本补齐 1..6 后，显式完整区间仍可通过。默认整份判定仍保守，因为清理标记不提供原始起点证明。
- 提取同时检查用户请求起点与选中检查点，禁止悄悄跳过缺失的请求前缀。未修改伤害、死亡、同步或 C# 诊断实现。

验证存入全新目录 `Logs/CombatEvidenceNextValidation/resume-20260923-163707`：

| 本轮实际命令 | 原始证据 | 结果 |
|---|---|---|
| `python -B -m unittest -v test_combat_evidence_retention`（cwd 为 Tools，修复前） | [retention-red/execution.json](../Logs/CombatEvidenceNextValidation/resume-20260923-163707/retention-red/execution.json) | 12 个测试方法，4 通过、8 失败；含子用例共 22 个失败断言，退出 1。 |
| 同一命令（修复后） | [retention-green/execution.json](../Logs/CombatEvidenceNextValidation/resume-20260923-163707/retention-green/execution.json) | 12/12，退出 0。 |
| `python -B -m unittest discover -s Tools -p 'test_*combat_evidence*.py' -v`（仓库根目录） | [python-regression/execution.json](../Logs/CombatEvidenceNextValidation/resume-20260923-163707/python-regression/execution.json) | 110/110，退出 0，包含上面 12 项；不重复累计为 122 项。 |

每次均保存准确命令、cwd、stdout/stderr、受测 Python 源码/测试副本及前后 SHA，输入未变化。绿测同时保存工具脚本副本与 Python 版本。全轮 Git 身份及初始差异在父目录接手档案中。本轮未启动 Unity，未重跑真实输入夹具或性能矩阵，未使用 computer-use，未生成或发布游戏包。

最终逐文件核对：原有 135 个变动文件中，只有本轮范围内的 Python 实现及三个文档变化；其余 SHA 保持一致，无缺失文件。仅新增上述测试文件，暂存区仍为空。本轮范围内 `git diff --check` 通过；[takeover-after.json](../Logs/CombatEvidenceNextValidation/resume-20260923-163707/takeover-after.json) 和 `this-turn-code.patch` 分别保存保护核对与相对接手版本的代码增量。清理边界的非法数值已实测；`firstRetainedFile` 与序号矛盾的分支本轮只做代码核查，未单独加入运行用例。

### 剩余问题与下一步

1. 下一项最小工作：为 `retention.jsonl` 的整局清理记录建立独立红测，核对“某局已全部删除、另一局仍完整”时顶层 coverage 是否误报完整，并检查有限区间不受无关清理影响。本轮只发现该路径尚未参与判定，尚未实际复现或修复；不能宣称全部清理路径已经关闭。
2. 阶段一门槛核对后，再用当前工作区新快照补 Unity 6000.3.21f1 集成基线、PlayMode、实际 Gateway/Replica 回放。归档工具的 `TOOL_FILES` 清单未包含后续新增的全部 Python 测试，需要补齐版本追踪；本轮用独立归档保存了本轮全部测试依赖，不把旧清单当作完整。
3. 基线有效后续跑既有入口 `-Seconds 5 -Repeats 3`，再决定三十秒测试与优化；不沿用旧 frozen 快照通过，也不使用 Smoke 替代。真实业务故障保持待采集/待复现，完整阵列和 Steam 双机保持未完成。

本轮结论：**诊断完整性尚未整体验收；高负载未通过；真实业务故障未复现**。仅上述清理前缀最小修复已完成本轮验证。

## 2026-09-23 整局清理执行记录

按已批准的下一项计划完成 `retention.jsonl` 整局清理误报修复，未推进 Unity、性能或业务实现。工作目录仍为 `F:\UnityStore\MonsterSupergroup` 的 `master` 主 checkout，HEAD 仍为 `cb276527a3be1e3789fae05bf5a714d641a7aa0d`，暂存差异为空。本轮开始时记录 136 个既有变动文件的 SHA、完整未暂存/暂存差异和 worktree 列表，保存在 [workspace-before.json](../Logs/CombatEvidenceNextValidation/run-retention-20260923-165615/workspace-before.json) 及同目录。

### 实现与实际验证

| 状态 | 本轮结果 |
|---|---|
| 已实现 | 根审计统一产生 `scopeGaps`，参与覆盖、查询/历史、提取的完整性结论；覆盖入口 run/round 过滤生效；无 capture 的序号范围返回结构化错误和退出 3。 |
| 已验证 | 已清理局与完整剩余局共存、只剩审计、未知/非法 run、损坏/截断、原始引用合并与重复导入、过滤作用域、有限区间及副本补齐、查询/提取退出码。最终相关 Python 回归 130/130，包含清理专项 32 项。 |
| 尚未完成 | 验证工具归档清单补全、当前工作区 Unity 6000.3.21f1 集成基线、实际 Gateway/Replica 回放、最新五秒三次矩阵及后续性能阶段、真实业务失败夹具和 Steam 双机验收。 |
| 无法确认 | 没有导入清理审计时能否发现删除事实；C# 当前先删目录、后写审计之间的崩溃窗口。未改动或验证这条写入路径，不能据 Python 结果声称所有清理路径安全。 |

代码仅修改 `Tools/CombatEvidence.py`，测试扩展现有 `Tools/test_combat_evidence_retention.py`；另更新本记录、计划和格式文档。整局审计缺少 capture/round/序号清单，因此缺口只放在范围层，不伪造来源区间。已知 run 的审计只影响无法排除该 run 的整体请求；未知 run 保守影响整体请求。显式有限来源区间和由真实检查点加显式末尾确定的提取区间继续按实际证据评估。

复核中额外确认两条同范围误报，均先记录失败再修复：损坏文件重复导入会留下旧 metadata 行，不能借用它的 runId 排除其他请求；旧数据库的 `Truncated cleanup audit` 是解析前标记，不证明 runId 可信。新版解析成功但无换行采用独立标记，旧标记及其他解析错误仍保持未知范围。详情见 [v2 格式与操作说明](combat-evidence-v2.md)。

所有本轮工件使用全新目录 `Logs/CombatEvidenceNextValidation/run-retention-20260923-165615`，未覆盖之前输出：

| 阶段与目录 | 实际执行 | 结果 |
|---|---|---|
| `red-isolated-audits` | 从修复前冻结源码运行清理测试；每个损坏审计子用例使用独立 DB | 29 个测试：原 12 项通过，新 17 项失败或报错；含子用例共 29 failures、8 errors，退出 1。 |
| `reimport-red` | 首轮修复后补重复导入回归 | 30 项中 1 项失败，退出 1；证实旧行 runId 掩盖新损坏。 |
| `legacy-red` | 补旧数据库标记兼容及新版已知 run 隔离 | 32 项中 1 项失败，退出 1；证实旧截断标记不能作为已解析证明。 |
| [verified-regression](../Logs/CombatEvidenceNextValidation/run-retention-20260923-165615/verified-regression/execution.json) | `python -B -m unittest discover -s Tools -p 'test_*combat_evidence*.py' -v` | 130/130，退出 0，耗时 9.881 秒；其中清理专项 32 项为同次执行子集，不重复累计。 |

初始 `red` 及中间 `green/regression/final-*` 结果也原样保留；最新实现以 `verified-regression` 为准。原有 110 项与新增 20 项均在本轮最终回归中实际执行。红/绿测试均保留命令、环境、stdout/stderr、受测实现及测试/helper 副本、前后 SHA；输入哈希保持一致。后续绿测及补充红测还保存全部顶层 Python/PowerShell 工具副本，避免现有验证清单漏收新增 Python 测试。原始红测可由其 `command.txt` 重跑，后续归档入口为同目录的 `run-python-check.py`，输出目录必须取新名称。

### 改动保护与下一步

最终范围核对见 [workspace-after.json](../Logs/CombatEvidenceNextValidation/run-retention-20260923-165615/workspace-after.json)：本任务写入两个 Python 文件和三个文档；核对期间还检测到范围外并行变化，包括 `docs/BuildProfiles.md` 内容更新、新文件 `docs/build-profiles-validation.md` 及 EditorTools 构建输入记录源码。详细清单及数量以带时间戳的快照为准。本任务未写入这些范围外文件，原样保留并归档观察到的副本，不能声称工作区所有其他文件均未变化。开始时 136 个既有变动文件均未缺失，暂存区保持为空。受测 Python 源码和工具依赖仍与最终回归的前后哈希完全一致。前两次保护核对发现变化的结果保存在 `protection-first-pass` 和 `protection-second-pass`；`this-turn.patch` 仅保存相对本轮开始时的五文件增量，`artifact-manifest.json` 保存工件哈希。范围内 `git diff --check` 及新增行空白检查通过。

本项到此停止。下一步先补齐验证归档清单，再从当前工作区生成独立物理快照并运行 Unity 基线；基线有效后才执行 `-Seconds 5 -Repeats 3`，保留失败结果。C# 审计崩溃窗口另列待核查；真实业务故障仍待采集/待复现。**诊断完整性未整体验收、高负载未通过、真实业务故障未复现**，本轮通过只适用于以上 Python 判定与回归范围。
