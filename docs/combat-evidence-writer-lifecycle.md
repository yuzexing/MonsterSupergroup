# 战斗证据 writer：退出、诊断保全与入队规则

本文对应当前源码行为，补充 [战斗证据说明](combat-evidence.md)。这里的实现和测试范围不代表新的 Steam 双机包已经通过实机验收；实机结论仍需同版包、两端运行身份及各端导出的证据核验。

## 正常退出

Runtime 注册 `Application.wantsToQuit`。首次退出请求会完成异常队列的收尾、记录一次 `process.stop`、停用证据生产入口及复制组件，然后调用 `Store.RequestClose()`。请求关闭会禁止新的入队并唤醒 writer；已经接受的工作仍按队列顺序处理。

这次 `wantsToQuit` 返回 `false`，让 Unity 继续运行。随后 `Update` 每帧非阻塞检查 writer，等待期间显示保存界面。正常路径不在退出回调中执行 2 秒的阻塞 join。

Standard 配置沿用 `Stopwatch` 单调时钟的 **30 秒**排空期限。到期仍未 join 时记录 `WriterNotJoined`，尝试保存 partial 诊断，然后允许退出。Standard 的清理及诊断文件写入仍包含同步操作，因此 30 秒不是其整个退出过程的绝对墙钟上限。

Standard 的 `OnApplicationQuit`、`OnDestroy` 保留原有 **2,000 ms** 有界兜底。Diagnostic 不在这些回调上阻塞等待或写文件；平台强制销毁时尽力异步记录未完成状态。重复回调不会再次生产 `process.stop`。强制结束操作系统进程无法保证这些回调或导出一定执行。

## Diagnostic 配置与完整优先退出

通过 `--combat-evidence-profile=diagnostic` 显式选择。未指定时为 Standard；未知或重复配置不能静默回退为小缓存。通用 Steam launcher 使用 `-EvidenceProfile Standard|Diagnostic`，新 operator-kit 的 `Host-Local.ps1` 与 `Client-Local.ps1` 默认 Diagnostic，继续支持无路径的校验及导出入口。

| 配置 | 队列 | critical 预留 | 诊断总计费预算 | 队列观测窗口 |
|---|---:|---:|---:|---:|
| Standard | 32 MiB | 4 MiB | 128 MiB | 1024 × 100 ms |
| Diagnostic | 512 MiB | 4 MiB | 768 MiB | 1024 × 1 s |

普通记录与 Advance 的 Diagnostic 上限为 508 MiB。预算按需计费，不在启动时预分配全部容量，也不是进程真实内存的硬上限。共享载荷、编码工作区、运行时缓存及观测器继续使用同一总预算；磁盘的 session/total 限制不变。`process.start` 的 `evidenceConfiguration` 保存实际配置，运行校验不能仅依赖启动命令行。

Diagnostic 停止生产后，等待 writer 完成，再由后台 finalizer 检查各上下文的内存与已发布 coverage、导出观测、持久化最终结果。界面显示待处理缓存、等待时间及写入/刷新/导出阶段。队列字节是计费数，不等于未落盘文件大小。连续 30 秒未见进展只提示，不自动退出。

队列归零和 writer join 不代表完整：失败任务同样会释放缓存。必须同时核对 dropped、failure、gap、恢复历史、tailUnknown、produced/written/flushed 及最终导出状态。本次捕获的前缀或整个上下文被容量清理后，即使最新水位完整，也须判为不完整；清理无关历史数据不影响当前捕获。成功才自动退出；失败则保留“记录不完整”界面，由用户明确退出。这里检查 writer 发布的完整性状态；离线验收仍须核验事件、附件及哈希，不等于全游戏逻辑或物理回放已通过。

用户可确认“放弃未保存记录并退出”。主动放弃标记异步写入，写盘无法响应时也不会无限延长退出；标记可能未能落盘，因此缺少最终成功结果不能推断保存完成。不提供可补回已释放失败任务的重试承诺。

Diagnostic 的完整性目标是最低配置 Client 连续 10 分钟战斗和正常等待保存结束。内存缓存不能保全断电、强杀时尚未落盘的数据。新包仍需双机实测，不把扩容等同于提高消费吞吐量。

## 长局与内存观测

Diagnostic 仍使用固定 1024 个窗口和 512 KiB 观察器预留，1 秒粒度覆盖 17 分 04 秒。窗口满后如实记录 overflow，全程累计继续工作；不会覆盖旧窗口。菜单前事件单独计数，不能将其导致的 `observationComplete=false` 误当成战斗记录丢失。

既有每秒性能快照补充 `systemAvailableMemoryBytes`、`evidenceQueuedBytes`、`evidencePeakQueuedBytes`、`evidenceBudgetBytes`、`evidencePeakBudgetBytes`；不可用为 -1。Windows 系统可用内存使用系统 API，与 Unity 分配量、进程 working set/private bytes 分开解读。验收需检查队列峰值不超过 508 MiB 的 75%，系统可用内存没有连续 10 秒低于 1 GiB，并结合用户实际卡顿反馈。计费上限不能替代这些真实内存测量。

## 诊断文件与重试

队列观察需显式启用 `--combat-evidence-observe-queue`；当前 `Tools/Scenarios/Start-SteamDiagnostics.ps1` 的开关名为 `-ObserveEvidenceQueue`。使用 operator-kit 时还须遵守该交付包自己的 case 范围限制。未启用时，writer 的正常关闭流程仍执行，但不会生成这些观察文件。该选项不自动安装主线程计时器，也不执行 CPU 校准探针。

文件位于本机证据根目录，名称中的 `{capture}` 为本次 capture ID：

| 文件 | 含义 |
|---|---|
| `writer-observation-{capture}.json` | producer 已停止且 writer 已 join 后的完整诊断导出 |
| `writer-observation-partial-{capture}.json` | writer 尚未 join 时取得的一次有界进度快照 |
| `writer-observation-status-{capture}.json` | 当前导出状态及文件引用 |
| `writer-observation-status-{capture}.json.first-failure.json` | 成功恢复前保存的首次失败 status 原始字节 |
| `shutdown-status-{capture}.json` | Diagnostic 后台最终保存结果及逐上下文评估 |
| `shutdown-aborted-{capture}.json` | 主动放弃或强制销毁的未完成标记；存在时优先于其他成功状态 |

`Exported` 仅说明完整诊断文件成功导出。Standard 路径的证据完整性仍为 `NotEvaluated`；Diagnostic 另附退出完整性评估。观察文件中的 `observationComplete`、窗口覆盖和计数平衡也必须分别读取。writer join、诊断导出成功、战斗证据无缺口是不同的判断。

full 与 partial 都先写独立临时文件，完成 JSON/文本 flush 和 `FileStream.Flush(true)`，成功关闭文件后才发布到最终路径。不会覆盖已有最终观察文件。写入、关闭或发布失败时，仅清理本次未发布的临时文件，并保留观察器数据供后续重试。完整导出成功后才释放观察器预算和引用；partial 不释放观察器，也不停止尚在进行的主线程观察。

首次失败 status 一旦存在，后续失败尝试不改写它。partial 首次导出失败后可以再次尝试；重试成功生成 partial 时，首次失败 status 仍保持原样，因此还应检查约定的 partial 文件路径。

若 writer 稍后完成，并且进程仍有后续生命周期回调或显式重试机会，完整导出可以恢复：先将旧 status 原始字节保存在 `.first-failure.json`，再把 canonical status 更新为 `Exported`，保留原 partial。若数值文件已成功、仅 status 写入失败，同一 Store 的重试会复用已成功的文件，只补写 status。成功的 status 和观察文件不会因重复回调而覆盖。进程退出之后不会自动继续这些重试。

## partial 的读取边界

partial 使用独立原子计数镜像，保存生产请求/拒绝计数、首个拒绝 guard 和上下文、pending bytes、已出队/已完成工作、当前工作身份、当前阶段，以及 49 个 service stage 的 inclusive/exclusive 累计 ticks。当前工作身份仅通过短锁的 `TryEnter(0)` 复制；锁忙时写明身份不可用，不等待 writer 的服务或文件操作。

它不读取 writer 正在修改的详细窗口或服务数组。不同字段在不同时刻读取，所以明确标记：

- `writerJoined=false`、`observationComplete=false`、`windowCoverageComplete=false`。
- `snapshotAtomic=false`，`countsBalanced=null`；不能把计数字段相减当作守恒证明。
- 当前 stage 与工作身份不是一次原子联合采样。
- service 累计只包括已经结束的 scope；仍卡住的 open scope 不计入累计。嵌套 inclusive ticks 不可相加为总墙钟时间。

详细窗口不包含在 partial 中。它用于定位退出时仍在哪个工作/阶段以及已经完成的开销，不证明缺失记录的内容，也不替代完整战斗日志。

## checkpoint 的内存与队列计费

Standard 默认队列为 **32 MiB**，其中 **4 MiB** 留给 critical 记录；总计费预算为 **128 MiB**。Diagnostic 使用上表中的大缓存；下列两步准入和释放规则适用于两种配置。

只有带 capture factory 的 `replay.checkpoint` 和 `replay.engine_checkpoint` 采用两步准入：

1. 在 Store gate 内，先按 512 B 最小入口检查 stopping/队列空间，同时在 Memory 中预留完整的捕获预算。Runtime 当前为这两类捕获声明 **8 MiB**；Memory 不足时不会调用 factory。
2. factory 返回后，计算 `512 + RetainedBytes(input)`，退回预留差额，再按该 retained-size 估算值检查实际队列上限。超出捕获预留则记 capture failure；超出队列空间则拒绝，并释放全部本次预留。只在通过后增加 pending bytes 并入队。

因此，小 checkpoint 可以在队列剩余空间不足 8 MiB 时进入，但仍必须通过完整内存预留和捕获后的队列检查。`RetainedBytes` 是既有的保守计费估算，不是 GC 堆实测或磁盘压缩字节数。拒绝和捕获异常仍留下 dropped/gap；队列观察会区分 `QueueLimit`、`BudgetReservation` 与 `captureFailed`。

checkpoint 仍写入 `checkpoints/<sha256>.json.gz`。恢复边界仍要求有效的完整 checkpoint set、捕获时对应的 failure epoch，以及已经 durable flush 到 checkpoint 的序列；后来的新缺口会使较早排队的 checkpoint 失去该次恢复资格。成功恢复只更新可靠区间起点，保留历史 gap 和 dropped，不能将此前缺失改写为完整。单个 engine checkpoint 不单独认证整个 source 已恢复。

## movement payload 的落盘位置

只将 `movement.submit` 的 input 内联阈值提高至 **16,384 UTF-8 字节**：等于阈值仍内联，超过才外置。判断对象是完成共享引用处理后序列化的 input，不是整个事件大小、字符串字符数或队列保留量。其他普通 stage 仍以 4,096 UTF-8 字节为阈值；有 input 的完整 checkpoint、engine checkpoint 和 `owner.attack_stats` 始终外置。

显式共享 payload 和 `KnockbackSettings` 依赖仍先写入带哈希的外部 blob，再以引用进入事件；依赖文件在事件 durable flush 前已经写完。显式 `inputRef` 保持引用，不因 movement 阈值而重新复制一份。内联记录沿用原事件分块、校验、刷盘及恢复机制，队列准入和共享 lease 释放也不因此放宽。

## 验证范围

相关 EditMode 测试位于 `CombatEvidenceCheckpointAdmissionTests`、`CombatEvidencePayloadPlacementTests`、`CombatEvidencePartialObservationTests`、`CombatEvidenceShutdownTests` 及既有 `CombatEvidenceQueueObservationTests`。它们分别覆盖两步准入/预算归还/恢复历史、UTF-8 边界/共享依赖持久化/重开读取、partial 与释放边界、退出期限和失败重试。

本文不记录尚未完成的测试通过数量。当前源码的实际测试结果、构建身份及双机 Steam 验证结果应附在对应交付档案中。
