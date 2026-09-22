# 战斗证据 v2：查询与性能比较

本次继续使用自动落盘与在线复制，不增加测试者的保存步骤。日志目录仍是 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Monster Supergroup\CombatDiagnostics`。原始日志不要改写；分析数据库和报告另存到 `Logs`。

## 格式与原始身份

日志格式和回放夹具为 2，离线工具同时读取旧版 1。游戏协议仍为 6；诊断复制协议为 2，版本不兼容时本地采集继续，复制失败必须出现在完整性信息中。

每个原始记录以 `CaptureId + RecordSequence` 唯一标识。复制副本不会产生新的业务执行。JSONL 的一行现在可能包含一个紧凑块，因此查询报告同时给出**文件、物理行、原始来源、原始序号**；不能把物理行数当作事件数。

支持的编码：

| 编码 | 内容与保证 |
|---|---|
| 普通 JSON 记录 | v1/v2，检查点与不适合打包的记录仍可独立存在 |
| `gzip-jsonl-v1` | SHA-256 校验的压缩 JSONL；相邻的输入与空完成结果可折叠，展开后保留两次原始序号和时间 |
| `advance-columns-v1` | 时间推进的引擎/边界字典与有序行；每次实际时间步、输入/完成/异常、帧和物理步分别保存，绝不把时间步求和 |
| `advance-binary-v1` | 相同语义的二进制有序行，减少逐数字 JSON 编码；解压后校验头、字典、行数、索引、序号和剩余字节 |

列式块的行顺序是 `[sequence, utcTicks, monotonic, network, frame, fixedStep, phase, delta, engineIndex, boundaryIndex]`。`phase` 为 0 输入、1 正常完成、2 异常。UTC 使用 .NET ticks 字符串，离线展开保留 7 位小数。64 位事件与状态实例编号继续使用字符串。

二进制块使用同样的外层身份、范围、SHA-256 和 gzip 包装。解压后为 little-endian：`uint32 magic=0x32445641`、三个 `uint16` 行数/引擎数/边界数（均最多 128），随后是引擎表、边界表和行。引擎的角色、引擎 ID、方法名各使用 `uint16` UTF-8 长度，`0xffff` 表示空值，每项最多 1,024 字节。边界 flags 保存 eventIds/supported/executeAll/offline/server/idsPresent 六个标志；可选编号进度为 slot/epoch/next，再保存 localPlayer/targetOwner。行依次为 `uint64,int64,double,double,int32,int32,byte,float32,uint16,int16`，最后的 -1 表示无边界。每个 float32 的原始值独立保留；离线转 JSON 使用 Unity/Mono Single 的往返表示，不附加双精度转换噪声。不能添加未声明尾部字节，也不能用异常/完成行携带输入边界。

离线读取会验证压缩大小、内容哈希、格式、行数及序号顺序。截断尾行、损坏块和缺失引用分别报告，不能把损坏块中的一部分当成完整证据。

不可变击退配置等可用精确单键对象 `{"$evidenceRef":"inputs/<sha256>.json.gz"}` 引用。读取时递归展开，校验哈希与路径，限制递归深度及展开预算，并拒绝循环引用。依赖未复制到本机或已缺失时，查询显示 `missingPayloads`，提取的夹具不标为完整。清理保留区间时也必须保留这些间接依赖。

## 导入与查询

以下命令从项目根目录运行。`CAPTURE`、`RUN`、`EVENT`、`PLAYER`、`STATUS` 都需替换成实际编号。首次使用 v2 查询时，建议建立新数据库；如果沿用旧数据库，重新导入日志目录以补齐身份索引。

```powershell
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite import "Logs/SteamIncident-20260922/Client/CombatDiagnostics" "Logs/SteamIncident-20260922/Host/CombatDiagnostics"
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite coverage
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite query --event EVENT --run RUN --resolve-inputs --output Logs/event.json
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite view --event EVENT --run RUN --resolve-inputs --output Logs/event.html
```

如果 Host 只有 `Player.log`，不要把它冒充结构化日志目录。先只导入实际存在的目录；旧包从未采集的业务证据无法补回。

同一事件的相关阶段、实际批次成员、权威状态输出以及传输包成员会关联起来。包关联使用对局、来源、业务批次和消息内容身份；不会只凭相近时间或同一个批次数字判定是同一条消息。

新增入口：

```powershell
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite player-output --player PLAYER --run RUN --output Logs/player.json
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite dot --status STATUS --run RUN --output Logs/dot.json
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite connection --capture CAPTURE --connection 2 --output Logs/connection.json
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite connection --capture CAPTURE --steam-connection 123456 --output Logs/steam-socket.json
```

这三种查询都支持 `--html` 生成同一证据的离线时间线，也支持 `--after` / `--before`、`--round` 和 `--limit`。默认返回至多 2,000 条，最大 10,000 条；达到上限会显示 `QueryLimitReached`，此时应缩小对局/时间范围。跨端不能用 UTC 排序替代真实因果关系；各来源内部始终按原始序号排列。

| 定位入口 | 应检查的证据 | 不应作出的推断 |
|---|---|---|
| 玩家输出 | `owner.attack_stats` 属性构建；`owner.damage_calculation` 每次实际伤害的参数与计算；命中/过滤；`stats.damage` 入账前后值 | 计算伤害、预测扣血、Canonical 扣血、界面统计必须相等 |
| DOT 实例 | 来源、目标、实例 ID、应用版本、TickIndex、叠层和立即结算标记；关联的独立伤害 EventId | 相同 Tick 在不同端或不同处理阶段出现就是重复扣血 |
| 实体/状态 | 预测、Canonical、显示状态分别查看；权限 Epoch、状态版本、死亡与销毁 | 单个端缺少死亡记录就证明服务器没有执行死亡 |
| 连接 | `network.connection` 状态迁移与连接映射；`network.message` 入队/接收；`network.transport` 实际发送结果；每秒网络指标 | Steam handle 等于 Mirror connectionId；发送成功等于远端已应用 |

Steam handle 和 Mirror connectionId 都是本进程的编号。跨端相同数字不代表同一连接，建议连接查询始终指定 `--capture`。`MirrorConnectionAssigned` 记录给出本机两种编号的映射；未分配的 Mirror ID 为 -1。

输出中的 `flow` 统一呈现输入、判断、前后状态和原始引用。`evidenceGaps` 包含缺失引用、来源缺口、未知尾部和查询截断；完整的清理与复制水位仍在 `coverage`。`firstObservedFailure` 是已有记录中的失败分支，不能自动解释为业务错误或跨端最早故障。

没有任何碰撞回调时，证据只能到达攻击发射/窗口边界。工具明确标记 `PhysicsBoundaryNotReplayable`，需用 PlayMode 场景验证物理碰撞，不能声称重放了 Unity 物理世界。

## 从证据到可执行回归

```powershell
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite extract --capture CAPTURE --engine ENGINE --first 1000 --last 3000 --output Logs/regression-input.json
```

提取器从完整检查点开始，保留实际输入顺序和每次推进。`owner.damage_calculation`、`owner.attack_stats` 和 `stats.damage` 本身就是一条完整的语义输入/输出，不再要求额外记录一对通用调用日志。新 `status.tick` 包装中的 Tick 单独进入重放断言，叠层/立即结算等上下文仍留在原始证据中。

夹具必须交给 Unity 中的实际业务回放适配器执行。查询工具不会用 Python 重新实现一套伤害规则，也不会直接把原日志里的最终判定当作执行结果。缺失输入、异常中止、缺失检查点或不支持的操作应返回不可可靠重放。

拿到 Unity 回放结果后，可把首个分歧接回来源文件：

```powershell
python Tools/CombatEvidence.py --db Logs/incident-v2.sqlite player-output --player PLAYER --run RUN --replay-report Logs/replay-result.json --output Logs/player-with-divergence.json
```

只有回放报告明确声明可靠且给出原始记录编号时，`firstDivergence` 才显示业务回放的分歧及原文件引用。没有回放结果时，字段保持空并说明缺少验证依据；这不代表已经证明业务一致。

## 三种模式的性能比较

用**同一个构建**、相同画质、分辨率和帧率目标分别运行：

- 关闭战斗日志：`--no-combat-evidence --network-diagnostics`
- 仅本地记录：`--combat-evidence-local-only --network-diagnostics`
- 全端复制：默认启用的 Steam evidence 包，或 `--combat-evidence --network-diagnostics`

50、200、500 只怪物的稳定负载各运行三次，每次至少三分钟。真实 Steam 双机由测试者操作。运行前核对实际 EXE 路径、Build GUID 和协议；直接启动新目录后被 Steam 重启到旧安装包的记录，不能作为新实现的联机结果。

`Tools/CompareCombatEvidence.py` 接受一个清单，每个测试可指定导入后的 SQLite capture，或关闭日志时独立生成的性能 JSONL。相对路径以清单所在目录为基准：

```json
{
  "minimumSeconds": 180,
  "minimumRepeats": 3,
  "enemyCounts": [50, 200, 500],
  "cases": [
    {"name":"off-50-1","mode":"off","enemyCount":50,"repeat":1,"performanceFile":"off-50-1.jsonl"},
    {"name":"local-50-1","mode":"local","enemyCount":50,"repeat":1,"db":"evidence.sqlite","capture":"CAPTURE_LOCAL","run":"RUN_LOCAL"},
    {"name":"replicated-50-1","mode":"replicated","enemyCount":50,"repeat":1,"db":"evidence.sqlite","capture":"CAPTURE_STEAM","run":"RUN_STEAM"}
  ]
}
```

```powershell
python Tools/CompareCombatEvidence.py --manifest Logs/performance-matrix.json --output Logs/performance-comparison.json
```

报告包含持续时间、帧数、实际存活怪物分布、Build GUID、显示设置、内存峰值、GC、网络队列、缺口和未完成的测试组合。`enemyCount` 是测试计划标签；实际覆盖程度必须对照 `alive` 的分布，不能只看标签。

`sinkMs`、`writerMs`、`checkpointMs`、复制主线程/后台毫秒数是累计计数。工具先求相邻观察的差值，再除以时间间隔，得到每秒耗时；这些是已观测工作段的耗时，不是 CPU 利用率。发生重置时记录缺口，不计算负开销。

报告中的 P95/P99 若来自每秒观察，明确表示**观察窗口分布**。它们不是所有游戏帧的主线程分位数。全局帧直方图只能给出粗粒度区间，工具不会平均每秒 P95 后宣称全局 P95，也不会用累计耗时证明主线程 P95 增量 ≤1ms / P99 ≤3ms。精确目标需要逐帧主线程测量或受控压测中的独立计时结果。

现有事故基线可由 `Logs/CombatEvidenceV2Validation/performance-baseline-manifest.json` 重算。它是约 69 秒、最多 46 只怪物的本地 KCP 记录，包含日志丢失；与旧 Steam 包的真实联机卡顿是不同证据来源。它可证明旧记录器积压，不能代替同包双机三分钟验收。

## 工具回归

```powershell
python -m unittest discover -s Tools -p "test_*combat_evidence.py" -v
```

用例覆盖 v1/v2、精确时间推进、异常完成、损坏块、64 位 DOT ID、多机副本去重、跨目录共享输入、批次/包关联、单条语义记录提取、回放分歧溯源，以及累计性能计数的正确差分。实际测试结果以本次运行输出为准；工具通过不等同于 Steam 双机或 GPU 性能通过。
