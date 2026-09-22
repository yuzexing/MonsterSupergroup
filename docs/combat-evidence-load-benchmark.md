# 战斗证据负载基准

先运行短验证：

```powershell
./Tools/Invoke-CombatEvidenceBenchmark.ps1 -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe' -Smoke
```

完整默认阵列：

```powershell
./Tools/Invoke-CombatEvidenceBenchmark.ps1 -Unity 'D:\RealSoftware\6000.3.21f1\Editor\Unity.exe'
```

默认复用 `Logs/SteamNetworkValidation/project` 验证工程，可用 `-ProjectPath` 指定其他已建好的验证工程。必须先结束其他针对同一工程的 Unity 测试，不能同时运行两个进程。脚本不启动真实游戏、不连接 Steam，也不修改游戏画质。

阵列包含 50、200、500 个 `StatusController`，分别关闭日志、仅本地日志、本地日志加两个模拟端持续复制。每组默认 180 秒、重复 3 次，共 81 分钟加初始化、落盘和补传时间；`-Seconds`、`-Repeats`、`-CatchupSeconds` 可调整，`-Smoke` 设为 1 秒、1 次和最多 15 秒补传。每步按 144 Hz 真实时钟节拍运行，输入时间步按固定规则交替，每个控制器逐次执行，不累计后跳过调用。工作超时不会减少输入数量，而是延长本次实际时间并记录未按时完成的帧数。

其中四分之一的控制器附带两个来源的 DOT；每秒还提交一条真实 Gateway 战斗输入、应用 Replica 输出，每三个事件重复提交一次。每个被注册的业务引擎先保存初始检查点，此后每 10 秒及结束时保存整个检查点集合。每个组合都以同一份输入在关闭诊断时执行实际业务实现，比较最终状态和 DOT 执行顺序摘要。关闭、本地和复制三个模式不能因诊断改变最终结果。

默认与游戏中的 `NetworkCombatantAdapter` 一样，把状态控制器注册到共同的 `CanonicalWorldReplica`。控制器仍逐个推进、每个时间步仍完整记录，但检查点与回放引擎复用 Replica 根。`-IndependentEngines`（或环境变量 `COMBAT_EVIDENCE_BENCHMARK_INDEPENDENT_ENGINES=1`）可额外测试 500 个独立回放引擎的冷启动压力；它不代表正常怪物绑定方式。报告中的 `statusControllerBinding` 和 `replayEngineCount` 明确区分两种负载。

每次运行保存 `benchmark.json`，包括：

- 驱动线程每帧工作、记录入队、检查点的 P95/P99 和最大耗时。
- 驱动线程分配字节、进程堆大小、各代 GC 次数。开始前通过真实分配 32 KiB 检验 Mono 的线程分配计数是否有效；不支持时 `allocationMeasurementAvailable=false`，分配值留空，不能解释成零分配。
- 128 MiB 统一诊断预算峰值、各模拟端的 32 MiB 队列峰值、丢失记录、写盘故障与覆盖文件。
- 写盘、复制主线程和复制工作线程累计耗时，以及发送字节和事件文件体积。
- 补传是否结束、尚未收到的文件及字节范围。超时没有补齐时明确报告，不能解释为完整复制。
- 实际业务输出和独立关闭日志执行结果的摘要，断言不一致即失败。

脚本另外生成 `comparison.json`，对照同控制器数、同重复次数的关闭日志结果，给出 P95/P99 增量。日志目录和每次试验报告均保留，失败也不自动删除。

此基准只反映状态推进、Gateway/Replica、采集、写盘和模拟复制 CPU 负载。两个模拟端在同一进程内共享 128 MiB 预算；堆和 GC 数据包括模拟远端，不能等同一台机器上单个真实游戏的开销。它不测量 Unity 完整帧耗时、渲染/GPU、场景物理、真实 50/200/500 只怪物或 Steam 链路；这些结果必须另由同包双机测试提供。

测试默认被环境变量门控，常规 EditMode 不会意外执行长阵列。直接用 Unity 测试入口时设置 `COMBAT_EVIDENCE_BENCHMARK=1`，可选 `COMBAT_EVIDENCE_BENCHMARK_SECONDS`、`COMBAT_EVIDENCE_BENCHMARK_REPEATS`、`COMBAT_EVIDENCE_BENCHMARK_CATCHUP_SECONDS` 和绝对输出目录 `COMBAT_EVIDENCE_BENCHMARK_OUTPUT`，过滤 `MonsterSupergroup.NetworkCombat.Tests.CombatEvidenceLoadBenchmarks` 即运行全部九个组合。
