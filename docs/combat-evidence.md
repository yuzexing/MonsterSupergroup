# 自动战斗证据与回放

从正常 Boot 启动后自动记录；没有 F8、现场保存按钮、问题标记广播或云端上传。使用 `steam-evidence` 构建配置时，非 Development 包也默认开启。Steam 房间协议为 **6**，参与联机的所有玩家必须更新。

## 给测试者

制作测试包：

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile steam-evidence
```

照常通过 Steam 创建、加入房间。日志默认在：

```text
%USERPROFILE%/AppData/LocalLow/DefaultCompany/Monster Supergroup/CombatDiagnostics/
```

断线后本地记录继续，连接恢复后自动继续复制仍保留的文件。退出后直接读取目录即可。连接尚未恢复、复制尚未追上本地产生速度时，一台机器的目录可能没有其他玩家的全部记录；`replication.json` 会保留已知端的状态和待传范围。

开发和对照测试可用启动参数：

| 参数 | 行为 |
|---|---|
| `--combat-evidence` | 在其他构建或 Editor 自动启用 |
| `--combat-evidence-local-only` | 写本地，不复制 |
| `--no-combat-evidence` | 关闭战斗证据采集 |
| `--combat-evidence-output=绝对路径` | 指定日志根目录 |
| `--network-diagnostics` | 在关闭战斗证据的基线中仍记录原有性能数据 |

全端复制只接入 Steam Sockets。KCP 不会伪装成已经完成跨端复制；自动化复制验证使用可断线、丢弃消息的传输适配器。

## 文件与完整性

```text
CombatDiagnostics/
  replication.json
  retention.jsonl
  RunId/Round/
    manifest.json
    sources/CaptureId/
      events-00000000000000000001.jsonl
      inputs/<sha256>.json.gz
      checkpoints/<sha256>.json.gz
      coverage.json
      recovery.json
      retention.json
      retention.local.json
```

原始记录身份为 `CaptureId + RecordSequence`。转发不重编号，离线导入会合并多台机器上的相同副本；同一身份内容不同会标为 `ConflictingCopy`。所有 `ulong` 序列化为字符串。一个进程跨多轮对局时记录序号仍单调递增，`RunId/round` 隔离业务。

`coverage.json` 记录 produced、written、flushed、缺口、异常和结束状态。`complete` 不代表已收到所有端的日志；复制范围必须另外检查。`tailUnknown` 表示异常尾部或仍在采集。即使尾部未知，也可以提取之前有完整检查点、连续记录、成对输入输出的有限区间。

本机清理远端副本写 `retention.local.json`，不会覆盖原始来源的清理声明。来源的清理记录是 `retention.json`。复制遇到已经被接收端清理的事件区间会记录 `prunedFiles`，不将它伪装成成功保存，也不反复补回旧事件文件。

约束与失败处理：

- 每个 Run 在每台机器上默认 8 GiB，涵盖各轮、本地记录和远端副本；根目录默认 32 GiB。全局优先清理已结束的旧 Run。
- JSONL 每 32 MiB 或 60 秒滚动；完整检查点组另起一个区间。清理只以已有且通过哈希校验的检查点为界，并删除不再引用的内容文件。不能安全腾出空间时，留下容量失败/缺口，停止接受相应记录，不能宣称该区间完整。
- 写入队列按保守内存估算限制为 32 MiB，其中 4 MiB 留给关键记录。复制待发送缓冲限制为 2 MiB，目录采用 512 项分页；记录序列化有 16 MiB 上限，避免无限扩张。128 MiB 是新增采集缓存的设计预算，不是 Unity 进程总内存限制；实际堆、GC 和帧耗时仍须用目标机器测量。
- 文件和压缩工作在后台；每秒刷新文件和水位。接收确认发生在落盘刷新之后。Windows 元数据替换的短暂占用会在后台有限重试。退出最多额外等待两秒；未排空的尾部仍按未知处理。
- 重启读取时忽略并截去不完整尾行，留下 `recovery.json`。写入失败、序列化失败、队列满都不能无限阻塞战斗。
- Unity 错误/异常保留首次栈及每个汇总窗口的次数、起止时间，限制异常种类和文本大小，不逐命中打印调用栈。

缺少记录、尚未复制、已经清理、采集失败是不同情况。分析时不得把其中任何一种解释为“该端没有执行”。

## 事件与处理链

主要字段为 `role/stage/outcome/reason`，以及 `input/before/after`。同一条记录同时供人类时间线与 JSON 查询使用。时间包含 UTC、本机单调时间、网络时间、帧与物理步；跨端顺序以事件、提交批次、Canonical 序号和收发关系为依据，不按两台机器的 UTC 强行排序。

| 记录阶段 | 内容 |
|---|---|
| `owner.hit`、`owner.damage_calculation` | 命中输入、随机暴击值、构筑数据与本地应用结果 |
| `collector.enqueue`、`collector.drain` | 入队/忽略原因、队列容量及实际提交批次 |
| `network.submit` | 客户端发送、服务端收到或因连接/世界不可用忽略 |
| `gateway.batch`、`gateway.decision` | 批次和逐事件处理分支，包括重复事件、重复/较旧批次 |
| `ledger.apply`、`gateway.canonical_link` | 生命账本变化、多个事件合并到同一 Canonical 输出的关系 |
| `network.canonical`、`replica.entity`、`replica.status` | 接收、应用、旧版本/移除水位/Tick 进度忽略原因 |
| `authority.movement`、`authority.handoff` | 新旧 Owner/Epoch、移动输入、处理原因与交接前后位置 |
| `movement.submit`、`movement.receive` | 实际移动批次和接收、延后、忽略情况 |
| `death.report`、`death.receipt` | 首次提交、重发、确认以及确认延迟 |
| `entity.spawn/destroy/death_presentation` | 对象生命周期、死亡表现完成 |
| `status.tick` | 实际执行的 DOT Tick |
| `observation.snapshot` | 每秒的角色、位置、本地与 Canonical 存活、死亡完成、渲染开关与透明度 |
| `performance.snapshot` | 帧分布、主线程/GPU、GC、Steam 队列以及原有业务性能计数 |

Host 的 Server、Owner、Replica 分开标记。实体的本机出生记录引用保存在 `entityGeneration`；跨端仍以对局、轮次、NetId 和出生记录关联。观察快照中的本地生命、Canonical 存活与显示状态分开保存。

`replay.input/output` 为逻辑模块入口与结果。Gateway 的 Ledger、状态注册表、攻击注册表与接收缓存归属于同一个 Gateway 引擎；Replica 管理的 StatusController 归属于相应 Replica，避免把一次内部调用误认为另一次独立业务输入。

排队前复制会被业务继续修改的网络数组；后台线程不读取仍在变化的场景对象。检查点捕获不推进时间、不执行 Tick、不调用缓存淘汰。

## Steam 复制

使用 Steam Sockets lane 1，可靠且独立排序；原有战斗流继续在 lane 0。诊断优先级低于战斗流。每进程发送预算为 2 MiB/s；单连接 256 KiB 在途限制同时检查 Steam 实际可靠队列，战斗积压时诊断也会让路。

传输流程为来源写盘 → Host 接收保存 → Host 向其他在线客户端转发。每个方向最多四个文件传输，32 KiB 数据块，gzip 压缩、SHA-256 校验、落盘后确认、丢失确认后恢复偏移。文件复制先写 `.partial`，完成已声明区间后再替换可读文件。复制本身只输出汇总与故障，避免递归记录每个诊断包。

日志格式、复制协议、回放格式目前均为 1，与房间协议 6 分开版本化。传输适配器是 `IDiagnosticReplicationTransport`，业务入口是 `IDiagnosticSink.TryWrite`。

## 离线查询

Python 工具仅依赖标准库，逐行导入 SQLite；不把整局 JSONL 一次载入内存。可把两台机器的目录一起导入同一库：

```powershell
python Tools/CombatEvidence.py --db combat.sqlite import D:/Host/CombatDiagnostics D:/Client/CombatDiagnostics
python Tools/CombatEvidence.py --db combat.sqlite coverage
python Tools/CombatEvidence.py --db combat.sqlite query --event 281479271677953 --resolve-inputs --output event.json
python Tools/CombatEvidence.py --db combat.sqlite view --event 281479271677953 --resolve-inputs --output timeline.html
python Tools/CombatEvidence.py --db combat.sqlite query --entity 100 --run RUN_ID --output entity.json
python Tools/CombatEvidence.py --db combat.sqlite query --reason WrongEpoch --output old-owner.json
python Tools/CombatEvidence.py --db combat.sqlite locate --monster-type Imp --after 2026-09-22T10:00:00Z --before 2026-09-22T10:02:00Z --output candidates.json
python Tools/CombatEvidence.py --db combat.sqlite compare --entity 100 --run RUN_ID --output comparison.json
```

`view` 生成可离线打开、可筛选的 HTML；原始 JSON 与文件/行号在每条记录内。`compare` 按同一 Run、Round、实体和状态版本比较服务端与 Replica 的 Canonical 值，给出首个分歧和缺少服务端证据的条目；不把预测状态与显示插值当成 Canonical 不一致。`--build` 可用 Build GUID 或源码/配置哈希筛选。查询有数量上限，达到时明确返回 `truncated`，可继续缩小时间段或实体范围。

统一目录中的原有性能数据可以导出，继续使用已有分析脚本：

```powershell
python Tools/CombatEvidence.py --db combat.sqlite performance --capture CAPTURE_ID --output performance.jsonl
python Tools/Analyze-NetworkDiagnostics.py performance.jsonl --output performance-summary.json --timeline-csv performance.csv
```

## 回放与回归夹具

支持 Gateway→Ledger、Replica+StatusController、权限注册表。使用实际业务方法，不直接套用原日志的接受/拒绝结果，也不重模拟整个 Unity 物理世界。

检查点保留账本独立权限标志、事件去重到期队列、批次水位、身份与来源归属、攻击元数据、DOT Tick/预测完成状态、移除水位、死亡与奖励去重、Replica 版本及事件分配进度。首次接入引擎写初始检查点，每十秒写一次全部有效引擎的检查点组；上下文变化重新捕获。

```powershell
python Tools/CombatEvidence.py --db combat.sqlite extract --capture CAPTURE_ID --engine gateway-1 --first 120 --last 400 --output issue.fixture.json
& 'D:/RealSoftware/6000.3.21f1/Editor/Unity.exe' -batchmode -nographics -projectPath PROJECT_PATH -executeMethod MonsterSupergroup.NetworkCombat.Editor.CombatReplayBatch.Run --combat-fixture=D:/issue.fixture.json --combat-result=D:/replay-result.json -logFile D:/replay.log
```

提取会校验引用、序列连续性、输入/输出配对、采集故障与版本。`--first` 选择其前最近的检查点，`--last` 限定结束序号；未指定时使用该引擎已记录范围。需要保留对局边界时，使用明确的序号范围。

结果为 `Matched`、`OutputDivergence`、`StateDivergence` 或 `CannotReliablyReplay`，带原始记录引用、操作及预期/实际值。退出码分别为 0、一致性失败 2、无法可靠回放 3。

增加 `--combat-minimize` 可在复现分歧后缩减输入，并写出 `replay-result.json.fixture.json`。缩减必须保留相同失败记录、操作、分歧类型和首个差异字段路径。夹具是回归输入，可由 NUnit 调用 `CombatReplay.Run`；缺失数据、未知操作或未知执行策略不能得到“通过”。

拾取世界不属于 Gateway 回放范围。Gateway 实际调用拾取服务时记录外部收据事实；回放校验相同调用参数后提供该事实，再重新执行 Gateway 和 Ledger 的判断。场景物理、动画系统、UI 与任意外部回调不在回放范围内，不能用该工具声称完整复现所有世界行为。

构建会生成 `combat-build.json`，并在开发机 `Logs/CombatEvidenceBuilds/<构建时间>/combat-replay-sources.zip` 归档本地修改后的项目代码、程序集定义及配置，用于固定产生日志的实现。源码归档留在开发机；随包携带构建清单和哈希，场景依赖另有哈希。生成清单进入 `source.start`，不会要求测试者手动记录版本。

## 验证与真实 Steam 验收

自动化覆盖：精确状态序列化、重复事件与批次水位、DOT 恢复、旧 Owner、Replica 乱序、日志开关不改变结果、三端转发/断线/确认丢失、队列过载、序列化失败、截断尾行、容量清理、后台写盘前的数组隔离、离线副本去重与输入提取。原始运行证据和样例位于 `Logs/CombatEvidenceValidation/`。

本次验证结果（2026-09-22）：

- 新增故障与回放测试 17 项通过（16 项整组及新增磁盘故障恢复测试），包含旧对局复制积压时切换新对局；Python 离线工具测试 6/6 通过。
- 开启本地详细采集的 PlayMode 测试 35/35 通过，37 个来源正常结束，无已记录采集缺口。
- 从这些运行日志提取的 Gateway、Replica、权限注册表共 92 份完整夹具全部回放一致。
- 非 Development Windows Player 脚本编译通过。广泛回归首次为 832/834，另外两项分别是验证工程 Steam AppID 不一致和场景测试顺序影响，修正验证环境后独立复测通过。
- `event-timeline.html` 和 `event-query.json` 展示 EventId `562954248388613` 的完整链路；批次通过实际事件成员关联，不仅依靠可重复的批次序号。

回放检查曾定位到日志共用数组被后续命中特效位置更新的问题；已在入队时复制相关数组，并添加回归测试。原始失败证据仍保留，便于比较修复前后的采集行为。

真实 Steam 验收仍需要两台机器运行同一 `steam-evidence` 包，分别做三组：

1. `--no-combat-evidence --network-diagnostics`：业务基线。
2. `--combat-evidence-local-only`：本地采集开销。
3. 不加参数：全端采集与复制。

维持相同画质、分辨率和测试路线，覆盖第一波、50/200/500 只怪、高输出清场、Owner 交接、Client 断线重连、Host 不可达。每组记录两端帧耗时分布、GC、Steam 队列、待确认死亡和诊断队列。主线程 P95 增量 ≤1 ms、P99 ≤3 ms 是待实测的目标，不能由无界面单元测试推导达标。

目前的真实输入回放验证来自 Unity 自动运行日志。要完成“此次 Steam 故障的同一输入在旧代码失败、修复后通过”的验收，还需要更新后的 Steam 包产生该故障日志；现有旧包未采集这类证据，不能追溯补造。
