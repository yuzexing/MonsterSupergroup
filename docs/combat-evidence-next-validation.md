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
| `tool-before.json`、`tool-after.json` | 五个固定工具、自动发现的 `test_*combat_evidence*.py` 及完整 golden 夹具目录的路径/长度/SHA；复制后核对，执行后再次核对，保留相对目录以便从归档重跑 Python 回归 |
| `sync.json`、`validation-before.json` | 实際受测快照的 SHA、同步记录及红测覆盖信息 |
| `tested-code-and-config.zip` | 当次受测代码、场景/Prefab/序列化资源、meta及Packages/ProjectSettings；包含未跟踪源码及红测旧文件，复用工程下次同步后仍保留。大型二进制媒体由完整manifest标识，不能把此源码归档当完整游戏包 |
| `invocation.json` | 精确程序路径、参数数组、环境变量和启动时间 |
| `results.xml`、`unity.log`、`execution.json` | 实际测试与退出结果；空测试或没有任何通过项目不能当成功 |
| `source-after.json`、`validation-after.json`、`integrity.json` | 前后 SHA 与具体变化；工作区、受测 Unity 工程和归档工具分别报告，审计失败不能保留成功结论 |
| `fixture-inputs-before.json`、`fixture-inputs-after.json` | Replay 模式额外记录实际执行的冻结夹具与清单前后 SHA，不以来源文件复制时的一次核对替代 |
| `artifact-manifest.json` | 本次全部证据文件的长度和 SHA |

`sourceUnchanged=false` 表示结果对应冻结快照，不能冒充后来修改的工作区；`validationInputsUnchanged=false` 表示受测 Unity 工程变化，`toolInputsUnchanged=false` 表示归档工具、测试或 golden 夹具变化，均不能作为稳定验收。无法完成审计时对应状态为 null/未知，并保留原因；最终成功要求受测工程与工具都明确稳定、审计完成。辅助工具在封存工件清单前将审计结论合入 `execution.json`，不把原测试失败改成通过。主工程的版本、资源、既有修改不会由同步入口更改。

历史目录没有 `tool-before.json` 时，不能追认工具稳定；新工具复查返回未知和非零退出码，另存 `*-recheck.json`，不改历史 execution、integrity、manifest 或前后清单。旧结果仍应优先使用自己的归档工具解释。

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

最终 Steam 验收仍须满足自动化和性能门槛。2026-09-24 用户另行确认：允许先进行探索性双机业务采证，暂缓200／500控制器尾延迟及分配／GC缺口，但不改判这些门槛；游戏操作由用户完成，不使用 computer-use。主工程已打开时，只能在独立物理工程准备构建，源输入并行变化必须记录并重新封存。

下面的 `-Mode Build` 命令是历史入口示例：当前构建工具已迁移到原生 Build Profile，该入口仍传入已停用业务参数，不能直接作为本轮可执行命令。当前入口为 `NativeBuildEntry.Batch`，显式选择符合要求的 Profile，并沿用 `full-v1` 的 prepare／finish 完整审计；本轮封存阻塞及下一步见文末探索性采证记录。不能因为原生构建返回成功而略过外层审计。

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

## 2026-09-23 归档修复与当前版本基线执行记录

本轮按阶段二小交付执行，证据目录为 [baseline-20260923-174123](../Logs/CombatEvidenceNextValidation/baseline-20260923-174123/baseline-summary.json)。没有运行五秒性能矩阵，没有修改 C# 诊断、伤害、死亡、同步实现或 Unity 配置。

接手时仍为原主 checkout `F:\UnityStore\MonsterSupergroup`、`master`，HEAD 为 `cb276527a3be1e3789fae05bf5a714d641a7aa0d`，记录了 141 个既有变动文件的哈希及完整差异。随后外部提交将 HEAD 推进到 `4e473e28f483105426a3df532a560aa13c622ad6`，本任务没有提交、切换分支或覆盖该提交；过程见 `workspace-before.json` 与 `external-head-change.json`。三次 Unity 调用都从新 HEAD 加本轮工具修改封存，源、受测项目和工具的准备前指纹分别相同，没有混合不同版本的结果。

### 本轮实现与测试

归档工具保留五个固定生产输入，自动发现当前全部六个 Python 测试/helper，并纳入三个 golden 目录文件，共 14 份工具输入。复制后校验和执行后校验共用输入发现规则；Git 差异另外使用相同范围的 pathspec，保留准备前已删除的跟踪输入。新增工具稳定性与审计错误状态，审计异常也会让最终 execution 失败；工件清单记录最终 execution 的实际哈希。旧档案缺少工具前清单时只另存复查结果，保留原文件。Replay 入口补充实际执行的两个冻结夹具及清单的前后哈希。

| 实际执行 | 原始结果 | 本轮验收结论 |
|---|---|---|
| `archive-red`：归档专项，修复前 | 13 个测试方法，含子用例为 7 failures、6 errors，退出 1；输入稳定 | 归档遗漏、复制变化、工具变更及旧档案未知状态的红测已保留。 |
| `archive-green`：首轮修复后 | 13/13，退出 0；输入稳定 | 中间结果，最终以下面全量回归为准。 |
| `archive-review-red`：补准备前删除与审计异常 | 15 项中 1 failure、1 error，退出 1；输入稳定 | 两项复核发现均先失败后修复。 |
| `python-regression`：从归档目录执行完整 discover 命令 | **141/141**，退出 0，18.114 秒；源码、工具和夹具前后哈希一致 | 归档修复实际通过；包含原有 130 项及新增 11 项，归档专项 15 项是其中子集。 |
| `editmode`：15 个指定类 | **100/100**，39.069 秒，Unity 退出 0，无缺类/跳过/失败 | 受测工程输入改变，最终 execution 失败，不能作为稳定基线通过。 |
| `playmode`：CombatContactEvidenceTests | **2/2**，0.240 秒，Unity 退出 0 | 同一输入改变，不能作为稳定基线通过。 |
| `replay`：明确列出的两份真实 JSON | Gateway **260** 步、Replica **1,217** 步；均 reliable/passed，summary 为 Matched，Unity 退出 0；夹具与清单哈希稳定 | 同一工程输入改变，最终 execution 失败；不能交付稳定版本回放基线，也不属于真实业务缺陷复现。 |

三个 Unity 入口最终 launcher 均退出 1，`execution.json.success=false`、`inputAuditPassed=false`，没有因 XML 或 Replay Passed 忽略输入变化。每次 `sourceUnchanged=true`、`toolInputsUnchanged=true`，而 `validationInputsUnchanged=false`。详细结果、逐类清单和相同版本指纹见 [baseline-summary.json](../Logs/CombatEvidenceNextValidation/baseline-20260923-174123/baseline-summary.json)。来源审计实际执行成功，原日志、物理行号、构建关联和夹具哈希均匹配；审计输入副本与前后哈希保存在 `provenance-inputs` 和 `fixture-audit-execution.json`。

### 当前阻塞与下一项最小工作

三次受测工程都删除了 `Assets/AddressableAssetsData/link.xml` 及其 `.meta`。已安装 Addressables 2.9.1 的启动清理代码会删除这类 Player 构建临时文件；主工程 XML 与 Library 构建副本的 SHA 完全一致，两个 Assets 文件均未跟踪并匹配 Git 忽略规则。此定位由包源码、哈希和启动阶段日志支持，没有逐文件删除调用栈。详见 [阻塞定位报告](../Logs/CombatEvidenceNextValidation/baseline-20260923-174123/blocker-report.md)；两个源文件、Library 副本、包源码及忽略规则已另行归档，主工程原文件仍保留。

| 状态 | 结论 |
|---|---|
| 已实现并实际验证 | 归档输入补全、工具稳定性审计、异常保守失败、旧结果保护；141 项 Python 回归通过。 |
| 已实际执行但未通过版本验收 | 当前快照的 EditMode、PlayMode、两份真实输入回放；原始执行匹配，但三次输入漂移都使基线无效。历史编译阻塞本轮没有重现。 |
| 尚未完成 | 无输入漂移的当前版本基线、五秒三次及后续性能矩阵、真实业务故障夹具、Steam 双机验收。 |
| 无法确认 | 生成物处理后的新快照能否稳定通过；清理审计的 C# 崩溃窗口仍未核查。 |

下一项只处理上述两个已确认的忽略生成物：确认无活跃 Unity/构建进程，先归档并核对身份，再将其移出 Assets 并保留恢复路径；重新封存新源身份，重跑同一套正确性基线。不能屏蔽这两个路径的哈希检查，也不能追认本轮结果有效。本轮没有实施该清理。

本轮只写入归档辅助工具、PowerShell 入口、配套 Python 测试和两个进度/操作文档。最终逐文件核对、暂存区状态及工具与受测副本的一致性见 `workspace-after.json`、`delivery-checks.json`；`this-turn.patch` 保存相对接手版本的增量，`artifact-manifest.json` 保存全部本轮证据哈希。**本项到此停止：归档修复通过，当前版本正确性基线未通过；诊断完整性、高负载、真实业务故障三个总门槛仍未完成。**

## 2026-09-23 生成物隔离与稳定正确性基线执行记录

本轮完成阶段二的“移出已确认生成物，重建稳定正确性基线”交付。新证据目录为 [baseline-clean-20260923-182031](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/baseline-summary.json)。实际目录仍是原主 checkout `F:\UnityStore\MonsterSupergroup`，分支 `master`，HEAD `4e473e28f483105426a3df532a560aa13c622ad6`。开始时五处未提交改动、完整未暂存/暂存差异、worktree 列表及逐文件哈希均已封存；暂存区为空。

### 生成物处理与恢复

执行前重新检查主工程、独立验证工程和相关 Unity/构建进程，没有活跃进程；未自动结束进程。两个目标均为普通、单一硬链接文件，未被 Git 跟踪，命中现有 `.gitignore:91`。其 SHA 与上轮阻塞报告相同，XML 也与主工程 Library 的构建副本一致。独立验证工程及 Assets、Packages、ProjectSettings、Library 均为物理目录，未使用旧 Steam junction 工程。

仅处理 `Assets/AddressableAssetsData/link.xml`（4,051 字节）和 `link.xml.meta`（158 字节）：先复制到 `cleanup/backup` 并校验长度/SHA，再通过 PowerShell `Move-Item -LiteralPath` 移入 `cleanup/quarantine`。移动前目标路径不存在，移动后原路径不存在，隔离文件哈希正确。源输入清单从 21,538 项变为 21,536 项，差异恰好是这两条删除；没有其他输入变化，见 [清理核对](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/cleanup/verification.json)。

[恢复映射](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/cleanup/recovery-map.json) 保存原路径、备份、隔离位置、长度及 SHA。移动成功后未自动放回。若将来需要恢复，只能写入仍不存在的原路径；若原位置已有新文件，保留双方，禁止覆盖。没有修改忽略规则，也没有处理整个目录或清理 Library。

### 本轮实际验证

复用现有验证入口、独立物理验证工程及 Library，使用 Unity `6000.3.21f1_c02631ffc030`、Python `3.14.7`，不启用 overlay。所有输出指向新目录；三个 Unity 调用严格串行。生产验证工具、测试集合、Addressables 包、Unity 配置和 C# 均未改动。

| 实际执行 | 本轮结果 | 原始证据 |
|---|---|---|
| 从新归档副本执行 `python -B -m unittest discover -s Tools -p 'test_*combat_evidence*.py' -v` | **141/141**，17.904 秒，退出 0；cwd 为冻结工具目录，未导入工作区副本，冻结及工作区工具输入稳定 | [Python execution](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/python-regression/execution.json) 与同目录 stdout/stderr、前后 SHA |
| EditMode 原定 15 类 | **100/100**，38.9524255 秒，Unity 和入口均退出 0；逐类、逐用例核对无漏跑、失败、跳过或无法判定 | [EditMode XML](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/editmode/results.xml) |
| PlayMode `CombatContactEvidenceTests` | **2/2**，0.2283423 秒，Unity 和入口均退出 0 | [PlayMode XML](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/playmode/results.xml) |
| 重新审计来源并用显式清单回放两份 JSON | 恰好两份：Gateway **260** 步、Replica **1,217** 步；均 `reliable=true`、`passed=true`、`Matched`，分歧/不可验证均为 0，Unity 和入口均退出 0 | [来源审计](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/real-fixture-source-audit.json)、[回放结果](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/replay/fixtures/replay-results.json) |

Python 和 Unity 的实际用例全名集合与上轮相同，以上数量来自本轮原始输出及 XML，没有沿用历史 Passed。EditMode 15 类的数量依次为：NetworkEvidence 6、StatusBoundaryCache 5、CombatDamageEvidence 11、CombatEvidence 17、CombatEvidenceIntegrity 5、CombatReplayBatch 7、CombatEvidenceRecovery 4、CombatEvidenceRecoveryHistory 7、CombatEvidenceReplicationV2 1、CombatEvidenceReplicationFault 3、SharedEvidencePayload 8、CanonicalSharedEvidence 5、CombatEvidenceSharedStorage 1、EvidenceBlockEncoding 8、CombatEvidenceOverload 12。未选择负载基准或分配压力专项。

三次 `execution.json` 均为 `success=true`、`inputAuditPassed=true`；每次 `sourceUnchanged`、`validationInputsUnchanged`、`toolInputsUnchanged` 都为 true，变化列表和 `auditErrors` 为空。准备前指纹跨三轮完全一致：

| 清单 | 项数 | SHA256 |
|---|---:|---|
| 主工程源码/配置及工具 | 21,536 | `5c3f1c246f01fde5a5c00eb04ad5e5d461005ff221f8cd50602b561d9103a1ec` |
| Unity 受测项目输入 | 21,522 | `9a7940982db153df2f7d63211a2b69173f087db92edaa67ae693be93bc1bc5e9` |
| 工具、测试/helper、golden 夹具 | 14 | `92675aae6ef9b8c33404ab875803afe8f4c19240dfb40cb41848651a84c57eea` |

两个生成物在三轮源及受测清单的前后版本中均不存在。实际回放的冻结夹具与来源审计 SHA 相符，夹具及显式清单前后不变。`baseline-summary.json` 的 `sameFrozenVersion`、`frozenBaselinePassed`、`workspaceMatchedAtEveryFinish`、`strictBaselinePassed` 均为 true；检查包含精确用例集合、执行步数、可靠性和输入稳定性。此次通过只证明所列测试覆盖的冻结版本；上轮因输入漂移失败的结果保持失败，不追认有效。

### 改动保护、剩余事项与停止

本轮在五处既有未提交改动上，只追加本记录与 `docs/diagnostic.md` 的进度说明；三个已修改的工具/测试文件保持开始时的 SHA，14 份受测工具输入也保持一致。完整当前输入清单再次与清理后封存清单比较，工作区受测源码/配置/工具仍匹配该版本。改动保护见 [delivery-checks.json](../Logs/CombatEvidenceNextValidation/baseline-clean-20260923-182031/delivery-checks.json)、`workspace-after.json` 和 `this-turn-docs.patch`；暂存区保持为空，没有提交或切换分支。新子档案与上轮已列工件逐文件复算长度/SHA，最终根 `artifact-manifest.json` 在 execution、摘要和文档副本落盘后生成；历史失败档案原样保留。

| 状态 | 本轮结束时的结论 |
|---|---|
| 已完成的处理 | 两个已确认生成物完成备份、定点隔离和恢复映射；没有新增实现或修改完整性契约。 |
| 本轮实际验证 | Python 141 项、EditMode 100 项、PlayMode 2 项及两份真实 KCP 输入回放全部成功，输入与工件稳定；可交付当前受测版本的**稳定正确性基线**。 |
| 尚未完成 | 五秒三次短测、三十秒检查点覆盖、完整性能阵列、真实业务失败夹具及 Steam 双机验收；阶段一整体门槛仍需单独核对。 |
| 无法确认 | C# 先删除后写清理审计的崩溃窗口仍未核查；现有 KCP 兼容夹具不能证明旧 Steam 事故或任何真实业务缺陷已复现。 |

本项到此停止。下一项使用既有入口执行 `-Seconds 5 -Repeats 3`，保留实际失败结果，再据证据决定性能修复与三十秒测试；本轮没有运行性能矩阵。**稳定正确性基线通过；诊断完整性未整体验收、高负载未通过、真实业务故障未复现，Steam 双机未完成。**

## 2026-09-23 当前版本五秒三次负载基线

按已批准的下一项计划执行，证据目录为 [short5s-20260923-192051](../Logs/CombatEvidenceNextValidation/short5s-20260923-192051/analysis/report.md)。本轮交付**完整、稳定、可追溯的失败基线**，没有优化实现、修改验证工具/C#/负载/阈值，也没有进入三十秒测试。

执行前重新确认原主 checkout、`master`、HEAD `4e473e28f483105426a3df532a560aa13c622ad6`；五处既有修改、完整差异、暂存区及逐文件哈希已经归档。无活跃 Unity/构建进程；物理验证工程和 Library 保留，两个隔离生成物未重新出现。当前 21,536 项输入与稳定正确性基线相同，源指纹仍为 `5c3f1c246f01fde5a5c00eb04ad5e5d461005ff221f8cd50602b561d9103a1ec`。

### 实际执行与联合验收

复用 Unity 6000.3.21f1 和原 Benchmark 入口，显式参数为 `-Seconds 5 -Repeats 3 -CatchupSeconds 45 -TimeoutSeconds 14400 -RequireCompleteMeasurements`；输出在新目录 `benchmark`，分析位于同级 `analysis`。没有使用 Smoke、IndependentEngines 或 overlay。

| 项目 | 本轮实际结果 |
|---|---|
| 执行覆盖 | NUnit 9 项实际执行，7 通过、2 失败、无跳过/无法判定；每项内部三次，共 27 个唯一组合，无缺报告。 |
| 原始逐帧数据 | 27 份 CSV，每份连续 720 帧，共 19,440 帧；独立重算均值/最大值/P95/P99及对应 off 增量，与 JSON 一致。 |
| 退出码与门槛 | 外层实际进程退出 1，Unity 退出 2；冻结比较器独立复算退出 2，与原 comparison 完全一致。CPU/存储门槛 false，分配门槛 false。分析脚本退出 0 只表示失败基线核对成功。 |
| 输入及工件稳定性 | 源、受测项目、工具前后均稳定，auditErrors 为空；准备指纹与稳定正确性基线一致。分析输入前后 SHA 相同。 |
| 时长及频率 | XML 测试区间 208.4216222 秒；外层含准备/审计 370.589245 秒。27 段实际主循环累计 134.9735967 秒，单段 4.9932199–5.0059544 秒；实际 143.8287–144.1955 Hz，全部满足 142.56 Hz 门槛。 |
| 业务及预算 | 27 组业务/DOT 摘要均与独立关闭采集执行一致。总预算账峰 108.0198 MiB，每端队列最大约 28.0068 MiB，未越过 128/32 MiB；这不是实际持有对象内存验收。 |
| 复制/排空 | 全部在 45 秒窗口内完成，最长 14.6031 秒，缺失文件清单为空；已丢事件仍使两个来源区间不完整。 |
| CPU | 200/500 的 local/replicated 共 12 组 P95 增量超 1 ms，其中 11 组 P99 增量超 3 ms；最大增量分别 4.3687、5.8113 ms。50 控制器三模式本轮 CPU/存储门槛通过。 |
| 分配 | 27 次探针均为 `CounterDidNotObserveProbeAllocation`，实际分配保留 null，未使用零值或历史 Profiler 数据补足。 |

[联合摘要](../Logs/CombatEvidenceNextValidation/short5s-20260923-192051/analysis/summary.json)、[逐组合表](../Logs/CombatEvidenceNextValidation/short5s-20260923-192051/analysis/case-table.csv)、[XML 审计](../Logs/CombatEvidenceNextValidation/short5s-20260923-192051/analysis/xml-audit.json) 和 [CSV 审计](../Logs/CombatEvidenceNextValidation/short5s-20260923-192051/analysis/csv-audit.json) 保存全部实际结果。现有 Benchmark 的比较失败不一定改写 `execution.success`，本轮联合使用外层、Unity、比较器退出码及稳定性判定，没有仅凭某一字段通过。

### 已定位的关键丢失与下一项

200 local 第 1 次丢失 16,446 条关键记录，9 段 QueueOverload，首缺口 44556..52818；队列峰恰为 28 MiB，总预算约 52.005 MiB。500 replicated 第 1 次丢失 17,788 条关键记录，51 段 QueueOverload，首缺口 330587..347106；主队列峰 28 MiB + 7,168 字节，总预算约 108.020 MiB。两端是同一来源副本，不重复累计丢失数；两个独立 case 合计缺失 34,234 条。

当前推进块每次预留 64 KiB，在 32 MiB 队列中为其他关键记录保留 4 MiB。队列达到推进块的 28 MiB 准入边界、总预算未满且无 writer failure，是队列短时触顶的直接证据；共享 QueueOverload 原因未记录每次具体拒绝分支，因此不能精确归因全部拒绝。结束检查点分别建立 290460、726060 的恢复起点，但之前缺口仍在，`complete=false` 正确保留，严格比较拒绝完整性通过。

累计编码、压缩及写盘/刷新数据还不能确定磁盘、GC、调度或块处理中的哪项造成瞬时峰值。500 复制第 2 次零丢失，存储/刷新累计却高于失败次，不能据累计值直接宣称磁盘根因。完整事实、推断边界及原始引用见 [定位报告](../Logs/CombatEvidenceNextValidation/short5s-20260923-192051/analysis/report.md)。

下一项建议限定为队列拒绝和消费停顿的有界观测：补首次拒绝 guard/序号/水位，固定容量分窗生产消费、队列峰及服务/flush 停顿，取得有效分配/GC 证据，再以相同五秒三次负载复验。确定支配因素后才做单点修复；不扩大预算、不丢关键、不拉长负载时间，不推进三十秒。

### 保护核对与未完成项

本轮对跟踪文件只追加本记录和 `docs/diagnostic.md`；三个既有工具/测试文件与全部受测输入保持原样。最终核对见 `workspace-after.json`、`delivery-checks.json`、`historical-integrity.json`；`this-turn-docs.patch` 保存本轮文档增量。新运行及分析的原始输出、准确命令、退出码、前后哈希和文档副本封存后，最后生成根工件清单；暂存区保持为空，历史档案未覆盖。

本项到此停止。已执行：全部 27 组及独立核验。已通过：执行覆盖、输入稳定、CSV一致性、业务摘要、达成频率、预算账及补传窗口。失败：关键零丢失、CPU尾延迟及严格分配门槛。未测量：有效实际分配、诊断实际持有内存、十秒周期检查点性能。未运行：三十秒及长测、真实业务故障复现、Steam 双机。之前稳定正确性基线保持有效，但本轮没有重跑其 141/100/2 项或两份回放。**诊断完整性未整体验收，高负载未通过，真实业务故障未复现。**

## 2026-09-23 有界队列观测与同负载归因

本项经用户批准，交付默认关闭的有界观测、同版本五秒三次复验及定位报告；没有实施性能优化，预算、关键输入保留要求及逐次时间步保持原样，没有进入三十秒测试。实际关键丢失如下列明。独立档案为 [queue-observation-20260923-210717](../Logs/CombatEvidenceNextValidation/queue-observation-20260923-210717/analysis/report.md)。仍在原 `master` 主 checkout，HEAD `4e473e28f483105426a3df532a560aa13c622ad6`。开始时 21,536 项输入匹配上一稳定基线；除原五处修改外发现两份已有构建文档修改，七处均已归档保护。两个隔离生成物保持缺席。

已实现 `-ObserveQueue`：每个 Store 先从原 128 MiB 预算预留 512 KiB，生产/消费各 1,024 个 100 ms 固定窗口；分别记录首次总体及各入口/guard 拒绝、来源/序号/阶段/水位、限额、逻辑记录/工作项/计费字节、队列峰、服务和刷新跨度。保留原准入顺序及 32/4 MiB 队列设置，不写普通证据队列；所有消费者退出后才导出。溢出、未排空或计数不平衡不能宣称观测完整。

### 实际执行与稳定性

最终 `revision3` 的正确性与三个矩阵采用同一源/工程/工具指纹，前后审计均稳定；精确用例集合、回放来源及冻结夹具已独立核对。原始命令、环境、源码/工具副本、XML、日志、外层及 Unity 退出码、前后清单均独立归档，详细指纹见 [最终摘要](../Logs/CombatEvidenceNextValidation/queue-observation-20260923-210717/analysis/summary.json)。

| 项目 | 本次实际结果 |
|---|---|
| Python | 从归档工具副本执行 141/141；前后稳定，与最终三组矩阵工具指纹一致。没有沿用旧任务的通过数。 |
| 最终正确性 | EditMode 126/126（原 100 + 观测 26）；PlayMode 9/9（接触 2 + 测量契约 7）；Gateway/Replica 260/1,217 步可靠匹配。 |
| 观测关闭对照 | 27 份报告、CSV 帧数集合 [720]；NUnit {'Passed': 9}；外层/Unity/比较器退出 1/0/2。 |
| 观测开启 | 27 份报告、CSV 帧数集合 [720]；NUnit {'Passed': 8, 'Failed': 1}；外层/Unity/比较器退出 1/2/2。 |
| 分配/GC | Profiler 实际 27 组，执行步数集合 [720]；独立有效分配 27/27、GC 计数 27/27、目标线程 GC 区间 18/27、时钟锚点 27/27。未知暂停时长仍为 null。 |
| 关键与总丢失 | 主来源只计一次：对照 0/0，观测 17253/17253。实际首拒绝侧车 1 个；不以本次未出现拒绝关闭旧失败。 |
| 严格门槛 | 对照 CPU/存储=False、分配=False；观测 CPU/存储=False、分配=False。Profiler 证据不会改写普通计数器 null 或比较器失败。 |

初始观测接口红测按预期失败并保留原 XML/日志。首次编译缺少测试命名空间的失败、首版 Profiler 的 139 线程枚举误判与默认整组超时、revision2 的实时 coverage 文件替换竞争全部保留。读取器修正保持固定容量；实时轮询修正只影响新增测量脚本，在原 45 秒截止内等待真实水位。空值不确认刷新、语法破损 JSON 不被 IO 重试吞掉。红测外层退出 1 的记录属于事后工具会话转录，未伪装为当时自动生成的 execution，详见档案 note。

### 归因、剩余项与停止

逐组合原始数据及分析见 [完整报告](../Logs/CombatEvidenceNextValidation/queue-observation-20260923-210717/analysis/report.md)、[队列分窗审计](../Logs/CombatEvidenceNextValidation/queue-observation-20260923-210717/revision3/analysis-queues/queue-audit.json)、[分配/GC 审计](../Logs/CombatEvidenceNextValidation/queue-observation-20260923-210717/revision3/analysis-allocations/allocation-audit.json)、[配对开销与同 case 相关性](../Logs/CombatEvidenceNextValidation/queue-observation-20260923-210717/revision3/analysis-correlation/observation-correlation.json)。阶段累计和嵌套时长不能相加为墙钟，也不能替代逐帧 P95/P99。工作项大小不同，不能仅凭完成项数断言消费快于生产。

| 状态 | 结论 |
|---|---|
| 已实现 | 有界拒绝/分窗/队列/服务与刷新观测，实际线程分配校准与原始 Profiler 保存，严格独立分析；不改业务或 v2 证据协议。 |
| 实际验证 | 上列全部正确性、矩阵、回放和组件核验；三个最终矩阵来自同一冻结版本，各重复均保留。 |
| 失败/尚未通过 | 最终观测组 500/local/第 1 次关键丢失 17,253；常规 CPU 尾延迟及严格分配计数器门槛；Profiler 仍因部分 GC 暂停时长无法确认而不全通过。历史关键丢失仍保持未关闭。 |
| 无法确认/未测量 | 未捕获拒绝时不能确定其支配原因；GC 进程计数与目标线程 marker 范围/边界不同，已有 marker 并集不是全进程暂停；复制导入 I/O 细分、实际持有内存、补传分配及周期检查点性能未测。 |
| 未运行 | 三十秒、长测、真实业务失败夹具及 Steam 双机；C# 清理审计崩溃窗口仍未处理。 |

本轮首拒绝已定位：500/local/第 1 次在原点后 380.3608 ms，`TryWriteAdvance / QueueLimit` 拒绝序号 52,972 的完成记录；produced/written/flushed 为 52,972/1,539/1。pending 29,344,192 B 加申请 65,536 B 超过 28 MiB 准入线 49,600 B，当时全局预算仅使用 55,039,000 B。此前 0.8657–232.7578 ms 有一次 231.8921 ms 服务停顿；后续窗口消费字节落后，最终触发准入限制。总队列峰约 28.092 MiB，仍在原 32 MiB 上限内。对应 Profiler 运行无拒绝，不能跨运行拼接 GC 时间线或据此认定磁盘/GC/编码根因。

下一项优先定位这次长服务对应的具体工作项，继续以固定容量区分初始检查点/元数据/文件创建、编码/构块及等待，再用相同五秒三次负载复验；现存 raw 的 GC 标记及窗口边界可只读补证。确认支配因素后才提出单点修复，不增加预算、不丢关键、不拉长负载。当前没有实施性能优化。本项到此停止：**诊断完整性整体未验收、高负载未通过、真实业务故障未复现。**

本项之外的既有工具、测试以初始 SHA 核对保持不变。最终核对发现 `docs/BuildProfiles.md`、`docs/build-profiles-validation.md` 在任务期间发生额外并行修改；保留当前文件并归档差异，不回退或覆盖，也不宣称七处既有文件全部未变。两份本任务进度文档的原有正文保留，受测输入仅含预期变更且仍匹配最终封存。历史档案逐项复核，初次保护检查的 false 原样保留，最终清单在 execution、摘要、最终文档副本及并行变更说明落盘后生成，见 [最终交付核对](../Logs/CombatEvidenceNextValidation/queue-observation-20260923-210717/delivery-final/checks.json)。暂存区保持为空，没有提交或切换分支。


## 2026-09-24 首个 Gateway 检查点服务拆分与同负载复验

本项交付默认关闭的有界服务观测及三个同版本五秒三次测量组。**旧 231.8921 ms 长服务未再现，支配因素未确认，未实施条件性能修复；本项到此停止。** 完整证据见 [归因报告](../Logs/CombatEvidenceNextValidation/gateway-service-20260924-101722/analysis-final/report.md) 和 [机器摘要](../Logs/CombatEvidenceNextValidation/gateway-service-20260924-101722/analysis-final/summary.json)。

仍为原主 checkout、`master`、HEAD `4e473e28f483105426a3df532a560aa13c622ad6`。初始23处已有修改/未跟踪输入均先行保存；构建脚本及两份构建文档的并行变化另列保护，不覆盖。使用 Unity 6000.3.21f1、原独立物理验证工程及 Library，两个隔离生成物保持缺席。六次最终 Unity 调用的源/项目/工具指纹一致，全部前后稳定，无审计错误；源指纹 `3703242b01a8108e70198d6d0dbc5caff0a6117775c2d3c7a44886204ce13bec`。

| 状态 | 本次实际结果 |
|---|---|
| 已实现 | FIFO工作编号和原记录身份、推进块封口范围、拒绝关联、49个内部阶段、独占/包含/最长跨度、队列外上下文；线程CPU独立预检和边界差值；启动/初始化/负载的分配与GC分相。固定布局501,768 B，沿用512 KiB预留。 |
| 红测 | EditMode新增观测契约3项先失败；PlayMode新增锚点契约先失败、其他9项通过。原始XML、日志、退出码及哈希保留。 |
| 正确性实际通过 | 冻结Python141/141；EditMode152/152（原126+服务观测21+集成5）；PlayMode15/15（接触2+测量契约13）；KCP两份输入260/1,217步可靠匹配。数量均来自本轮实际执行。 |
| 普通关闭/开启组 | 各27报告、27 CSV，每份连续720帧；NUnit各9通过，逐帧分位数独立复算一致。各组Unity/外层/比较器退出0/1/2，严格性能门槛仍失败。 |
| 本轮负载通过项 | 两组关键及总丢失均0，业务/DOT摘要一致；频率均≥142.56 Hz，预算账≤128 MiB、队列≤32 MiB，补传均在45秒内完成且缺失清单为空。 |
| 仍失败 | 关闭/开启组最大P95增量5.4484/5.0766 ms、P99增量7.3966/6.2507 ms；普通分配计数器各27次均无效，继续为null。Profiler负载用例因部分GC不可确认而失败。 |
| 分配/GC证据 | Profiler27组合、720步均实际执行；启动和初始化各27组分配/时钟/GC区间有效，负载27组分配/时钟/进程GC计数有效、18组目标线程GC区间有效。9个off组合发生GC但无对应原始区间，保留未知。 |
| 无法确认 | 旧长服务内部支配操作；精细CPU/等待分解；全部保留工作GC关联（405/459完整覆盖，其余有边界或负载后缺口）；观测无干扰及真实持有内存。 |
| 未运行 | 条件性能修复后的第二版本、三十秒/长测、真实业务失败夹具和Steam双机；C#清理审计崩溃窗口未处理。 |

旧事件已通过FIFO源码和原始序号重建为首个 `gateway.1 / replay.engine_checkpoint / seq=1` 写入；旧侧车无直接workId和内部阶段，重建边界见 [证据链](../Logs/CombatEvidenceNextValidation/gateway-service-20260924-101722/old-service-reconstruction.json)。新普通观测及Profiler各直接捕获18次同身份工作，分别为6.7173–24.3427 ms和6.0995–27.0073 ms，均无自然拒绝/重试。两组各17/18次首服务CPU低于有效粒度；15.625 ms观察粒度及约31.25 ms经验误差不能支持精细等待归因。Profiler这18次首工作均在有效锚点覆盖内，没有与目标线程GC标记重叠；这不能解释另一运行的旧事件。

两组全局最长53.2905/58.8393 ms属于负载结束时seq726060、engine=*的完整检查点，身份不同于原首Gateway工作，单列后续候选。未把该序列化耗时替代原问题，也未因本轮零丢失关闭历史拒绝。原顺序、预算、持久化水位和逐次时间步保持不变，未增加预热或拉长负载。

全部逐组合结果、分阶段分配、内部耗时和观察开关统计差值在归因报告中索引。离线服务审计的文件匹配错误、初始化审计的LoadEnd计数边界错误均保存失败版本；后者有独立红/绿分析测试。修正只涉及本次分析脚本，未改受测C#或重跑负载。最终 [保护核对](../Logs/CombatEvidenceNextValidation/gateway-service-20260924-101722/delivery-checks.json) 与 [工件清单](../Logs/CombatEvidenceNextValidation/gateway-service-20260924-101722/artifact-manifest.json) 在文档及执行状态落盘后完成。

**诊断完整性整体未验收；高负载未通过；真实业务故障未复现。** 旧长服务保持待自然再次捕获、待归因；CPU尾延迟另列后续问题，不在本项追加优化或三十秒测试。


## 2026-09-24 生成物分类修正与主线程尾延迟定位

本项完成验证策略修正、有界主线程观测、当前冻结版本正确性和三个五秒三次矩阵。**具体操作未满足归因门槛，未实施性能优化；本项到此停止。** 完整证据见 [交付报告](../Logs/CombatEvidenceNextValidation/main-tail-20260924-134215/analysis-final/report.md) 和 [机器摘要](../Logs/CombatEvidenceNextValidation/main-tail-20260924-134215/analysis-final/summary.json)。

仍在原 master 主 checkout，HEAD `4e473e28f483105426a3df532a560aa13c622ad6`。开始时32处已有修改／未跟踪输入归档保护。原物理验证工程与 Library、Unity6000.3.21f1、无overlay。用户确认关闭主工程后，三个负载组启动前均重新核对无活跃 Unity／构建进程。

| 状态 | 本轮事实 |
|---|---|
| 已实现：工具 | 封存 full-v1／editor-generated-v1 策略。仅 Tests／Replay 的稳定版本判断精确排除 Addressables/link.xml 及其 .meta；完整哈希／变化列表、内容仍保存，Build／Python默认严格。旧档案保持原结果。没有移动或删除生成物。 |
| 已实现：观测 | 默认关闭，固定top36及当前帧、16阶段和调用数。原1,024分窗不变，实际布局521,560 B在512 KiB内。业务执行、准入顺序、持久化水位及时间步保持原样。 |
| 红绿与正确性 | 工具新增10项先失败，专门25/25与冻结Python151/151通过；工具契约阶段EditMode152、PlayMode20通过。新增观测3项API先红，另16项行为首次绿；最终EditMode171/171、PlayMode20/20，两份KCP输入260／1,217步可靠匹配。后者仅兼容基线。 |
| 稳定版本 | 最终正确性、两个普通矩阵、Profiler及本轮raw离线读取共7次Unity调用，源／项目／工具指纹一致；完整及稳定输入、工具均前后不变，无审计错误。稳定源SHA `cf64676305ca6c83b278cc8b35596af594f60cea03ceddc679570dbffcfe7d9f`。 |
| 实际负载 | 普通关闭／开启各9项实际执行并通过、27报告与27份连续720帧CSV；独立重算分布一致。两组Unity／外层／比较器退出均0／1／2，不能以NUnit通过代替严格门槛。Profiler27组合均实际执行，Unity／外层2／1。 |
| 通过项 | 两普通组关键与总丢失均0，业务及DOT摘要一致，有限前缀完整刷新，频率≥142.56Hz，预算账／队列及45秒补传通过。历史拒绝不因本次未再现而关闭。 |
| 失败项 | 200／500的local／replicated共12次记录组合，两组均P95/P99失败；最大增量关闭5.5253／6.4639ms、开启7.8312／8.9437ms。普通分配探针各27次无效并保持null。 |
| 分配与GC | Profiler有效分配27/27；启动／初始化分配、时钟、GC各27/27；负载GC区间18/27，9个off未知。旧版和本版各27×720 Step已独立导出，不拼接时间线。 |
| 未知／未测 | 具体支配操作、观测自身净成本、细分CPU／等待、诊断实际持有内存与10秒周期检查点性能。完整收支不等于具体操作覆盖。 |
| 未运行 | 条件优化及其第二冻结版本、三十秒与长测、真实业务失败夹具、Steam双机；清理审计崩溃窗口未处理。 |

GasWrapper 是普通与Profiler各18个case的top36累计最大独占阶段；普通保留墙钟占比30.68%–61.50%，但它仍包含多种操作及观测开销，不能直接认定一个生产热点。两组各自减同组off后，开启观测的P95增量差+0.0927～+3.0691ms；500控制器约+2.28～+3.07ms，干扰不能排除。Profiler648个保留主帧都有同case GC区间覆盖，257帧可能重叠，只能说明整帧相关，不能归因到内部操作。具体操作未产生两次重复的数值候选。

旧231.8921ms首Gateway服务仍未再现：本轮普通18次6.9495–23.3577ms、Profiler18次6.4717–27.2108ms。当前最长服务属于结束全检查点，身份不同。旧事件仍未归因、未修复。

下一项最小工作应先在现有预算内约束观测成本／误差，再进一步拆分GasWrapper宽泛剩余；先完成容量核算，取得具体操作重复占优且观测干扰可解释的证据，才讨论一次修复。本次不追加调参或新负载轮次。

本轮分析脚本对checkpoint分布计数的初次误判保留，实际schema回归7项先红后绿，修正结果另存v2，未改原始测量。最终 [保护核对](../Logs/CombatEvidenceNextValidation/main-tail-20260924-134215/delivery-checks.json) 和 [工件清单](../Logs/CombatEvidenceNextValidation/main-tail-20260924-134215/artifact-manifest.json) 在状态、文档及副本落盘后生成；现有其他修改保留，暂存区未改，无提交或切换分支。

**诊断完整性整体未验收；高负载未通过；真实业务故障未复现。**

## 2026-09-24 探索性 Steam 采证准备与封存阻塞

本项按用户新顺序优先准备真实双机现场，不作为阶段五最终验收。档案为 [steam-exploratory-20260924-164917](../Logs/CombatEvidenceNextValidation/steam-exploratory-20260924-164917/report.md)。执行开始仍是原 `master` 主 checkout，HEAD `4e473e28f483105426a3df532a560aa13c622ad6`，暂存区为空；已保存41处既有修改／未跟踪输入的字节副本、完整差异、源码清单、路径属性及环境。没有重置、清理、提交或切换工作树。

| 状态 | 本轮实际结果 |
|---|---|
| 已实现 | 启动脚本增加显式off／local／replicated模式及独立输出、附加进程限制、请求／应用状态区分；保留Default。六份双端配置与操作说明已生成，但均标记readyToRun=false。归档内离线分析复用冻结解析器，分别导入两端及合并副本，核查有限区间、v2物理引用与依赖并重新提取。 |
| 实际验证通过 | 冻结完整Python回归151项、Steam工具19项（启动11＋身份导出8）、离线分析13项合成测试，均退出0、无跳过且输入前后稳定。同一最终启动测试对原脚本失败，对新脚本通过；早期环境／测试桩失败全部保留。离线分析首次绿测不冒称先红。 |
| 封存失败 | 准备源码物理副本时，主工程新增Unity进程65012，两个Addressables生成物在清单捕获后消失，随后ProjectSettings也变化。相对执行初始清单，另有其他任务对ProjectBuildWindow.cs的修改；已保存差异，未覆盖。 |
| 审计结论 | 副本本身 `validationInputsUnchanged=true`、工具稳定，但准备未完成 `generated-inputs-before.json`，`sourceUnchanged=false`，`inputAuditPassed=false`、`acceptanceInputAuditPassed=false`。不能因副本之后未变而补判准备成功。 |
| 未执行 | 本次Unity构建、Direct配置派生、包交付、双端运行身份、Player落盘、真实日志导入／有限提取／Unity回放和场景对照。只有工具合成测试，没有Steam现场结果。 |
| 独立保留 | 200／500控制器尾延迟失败、普通分配探针无效、部分GC未知；阶段一整体门槛及清理审计崩溃窗口、真实业务缺陷复现和最终Steam验收未完成。未运行性能矩阵或三十秒测试。 |

构建路径还存在历史接口不匹配：旧 `Invoke-CombatEvidenceValidation.ps1 -Mode Build` 仍传 `-toolProfile/-toolBuildKind/...`，当前原生入口明确拒绝。下一次应复用现有 `NativeBuildEntry.Batch`，在完整物理源码副本封存前将所选Evidence Profile的Distribution从Steam派生为Direct，保留精确差异；主工程Profile不变，C#不变，已有full-v1完整审计不变。本次准备先失败，该派生及原生构建均未执行，不能宣称已得到包。

静止窗口确认后使用全新目录重新封存，不修补或复用失败档案。构建返回成功只是第一项：还须完整输入及工具审计通过、包内容／BuildId／GUID／源码清单与派生Profile一致，才交付用户。源码ZIP不覆盖完整资源或全部Profile身份，必须保留完整物理来源供同版回放。

用户及另一端操作者按 [操作说明](../Logs/CombatEvidenceNextValidation/steam-exploratory-20260924-164917/operator-instructions.md) 完成本地链路、复制链路，再进行off→local→replicated场景对照；当前须先解决封存阻塞。先审计各端，再合并，固定有限水位；正常退出不是刷盘或复制追平证明。没有复现故障可如实报告，只有独立业务断言连续三次稳定失败才能称为已复现。

工具测试子进程清除了继承的PowerShell 7 `PSMODULEPATH`，避免Windows PowerShell加载不兼容模块；同时清除 `PYTHONPATH`，显式使用冻结副本，未修改系统环境。测试命令、原始输出、实际退出码及前后哈希均在独立档案。最终保护核对及工件清单在本次状态和文档落盘后生成。
