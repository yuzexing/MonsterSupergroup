# 模拟快照与 DOT 确认重放修复

日期：2026-09-12。接续 `enemy-hit-flash.md` 中记录的两个独立问题。

## 修复前复现

使用修复前独立程序的实际 Managed 程序集，固定时间调用生产类，输出保存在 `Logs/SnapshotDotFix/before-reproduction.txt`：

| 输入 | 修复前结果 |
| --- | --- |
| 时间 2.0 的可靠检查点后，提交时间 2.0、序号递增的服务器移动快照 | 抛出 `Invalid server simulation snapshot` |
| 三次 DOT 的第一次 tick 后收到初始确认 | 索引 `1,1,2,3`，共四次 |
| 三次 DOT 的第一次 tick 后收到重复确认 | 索引 `1,1,2,3`，共四次 |
| 三次 DOT 已本地到期，随后收到初始确认 | 索引 `1,2,3,1,2,3`，共六次 |

原先独立 Host 运行 `Logs/M3Knockback/Host-20260912-122034-725-p7988` 有六次模拟快照异常；旧夹具允许本地预测集合与服务器确认集合取并集，不能排除多余预测伤害。

## 根因与实现

`ServerEnemySimulationRegistry` 原来用最新交接快照的时间校验移动包。可靠动作检查点会先写入同一帧时间，随后正常移动包便被误判时间未递增。现在移动流单独保存最后接受的时间和序号；交接基线继续保留较新的数据，同时间优先检查点。切换 Epoch 只重置移动流的校验记录。真实非法包仍拒绝；服务器异常包含原因、前后时间、序号和 Epoch。

`StatusController` 原来每次确认都会重设完成次数和计时余量，本地到期后也没有完成记录。现在用 `ApplicationRevision` 区分真正刷新与同一次应用的确认。相同轮次取较大的完成次数，本地进度不落后时保留当前 tick 的计时余量；服务端进度领先时跳过已完成 tick 并重新计剩余间隔。完成记录保留到目标/状态运行时销毁或重置，迟到确认不会复活已经结束的轮次。

轮次贯穿 `StatusInstance`、`StatusMutation`、`CanonicalStatusState`、周期伤害 `CombatEvent`/`CombatResult`，以及复制、转移、保存、恢复路径。普通伤害的状态身份保持空值。客户端副本缓存过滤旧轮次、旧版本、进度回退和已移除状态；新轮次才重启计时并得到新的 tick 预算。同轮次的不同确认版本共享服务器预算。来源断线时，接管进度取时间进度与本轮已接受 tick 数的较大值，再撤销来源的伤害提交许可。

保留 `movementOnly`、Prefab 配置、伤害数值、刷新规则、闪白时长和死亡立即销毁行为。网络字段发生变化，Host/Client 必须使用本次相同版本。

## 回归结果

| 验证 | 结果与证据 |
| --- | --- |
| EditMode | 147/147，`Logs/SnapshotDotFix/edit-final.xml` |
| PlayMode | 25/25，`Logs/SnapshotDotFix/play-final.xml` |
| 最终独立程序构建 | 成功、退出码 0，`Logs/SnapshotDotFix/build-final.log`；程序集哈希在 `final-build-hashes.json` |
| 普通 Host/Client | PASS，双方退出码 0、异常 0；`Logs/M3Knockback/Host-20260912-132104-885-p7988` |
| 延迟网络 Host/Client | PASS，双方退出码 0、异常 0；`Logs/M3Knockback/Host-20260912-132243-882-p7989` |
| Dedicated + 两客户端 | PASS，三个进程退出码 0、异常 0；`Logs/M3Knockback/Dedicated-20260912-132430-449-p7990` |

三种最终运行均完成七个 DOT 阶段。普通三次状态的六个阶段各有五次直接命中和三次 DOT，来源客户端恰好八次本地预测，两端各八次闪白。注入日志确认迟到投递前已经执行至少一次本地 tick；到期投递前恰好完成三次且本地状态已经移除。接管阶段预设四次 DOT，服务器只执行剩余次数；观察端有五次闪白（一次直接伤害与四次 DOT），重连来源端历史闪白为零。最终敌人血量均为 9190。延迟网络使用 `LatencySimulation` 的 `latency=100`、`jitter=.02`、`unreliableLoss=5`、`unreliableScramble=5`。

全部七个独立进程的最终退出码为 0，异常日志为 0。汇总见 `Logs/SnapshotDotFix/host-final-summary.json`、`impaired-final-summary.json`、`dedicated-final-summary.json`。旧 Host 日志按新运行器规则检查会命中六次异常，证据在 `before-log-check.txt`，因此先前带异常的结果不会再通过验收。

EditMode 包含燃烧、中毒、流血的初始确认迟到、重复确认、到期后确认；旧确认/移除晚于刷新、两来源独立进度、同版本进度前推、同轮次共享预算、新轮次新预算、断线接管、轮次与伤害身份序列化往返。PlayMode 包含真实产品 Enemy 同帧检查点与移动写入后第二只敌人继续广播、Renderer 的实际 MaterialPropertyBlock 白黑交替、模拟权交接、击退、真实武器击杀及经验宝石拾取。

## 独立进程验收约束

夹具通过 Boot → Gameplay 运行真实 Native 攻击、KCP 伤害批次与真实 Enemy。新增定向确认注入阶段：燃烧确认延迟超过一个 tick、中毒重复确认、流血确认延迟超过整个状态寿命。注入只暂停来源端 StatusController 对确认的应用，实际服务器准入、血量和闪白批次继续运行；恢复时重复投递保留的确认并重新绑定副本缓存。每个注入必须实际执行，否则夹具失败。

每个普通 DOT 阶段严格要求三个唯一的 `(InstanceId, ApplicationRevision, TickIndex)`，三次本地扣血和三次确认。预测事件必须属于服务器确认集合；两端闪白必须恰好等于确认集合。同一事件二次闪白、额外预测、伤害结果被拒绝或血量不收敛均失败。服务器接管阶段只结算剩余 tick，重连快照不能重播历史闪白。

运行器同时验证每个进程退出码、PASS 标记及异常/断言日志；不能仅凭夹具 PASS 放过模拟快照异常。渲染验证读取真实 Renderer 的材质属性，隐藏窗口截图不作为画面证据。

修复后的首次普通 Host 运行 `Host-20260912-131350-319-p7988` 在到期确认阶段被夹具判失败：当时检查的是所有操作共用的 `InvalidStatus` 指标，包含到期移除请求的拒绝；实际该阶段为三次 DOT，双方各八次闪白（另有五次直接命中）。夹具现改为严格核对伤害结果接收数与接受数的差值，同时保留三个唯一 tick、预测必须全部确认、精确闪白集合和异常日志检查。该失败记录保留，不计入最终通过结果。中途编译和包长断言错误也保存在 `edit-2.log`、`edit-4.xml`；包长估算采用未压缩字段大小，Mirror 变长编码按字段序列化往返验证。

```powershell
./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Executable Builds/EnemyHitFlash/EnemyHitFlash.exe -ValidateHitFlash -CaptureFrames -ForceD3D11 -Port 7988
./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Executable Builds/EnemyHitFlash/EnemyHitFlash.exe -ValidateHitFlash -CaptureFrames -ForceD3D11 -ImpairedNetwork -Port 7989
./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Executable Builds/EnemyHitFlash/EnemyHitFlash.exe -ValidateHitFlash -CaptureFrames -ForceD3D11 -Dedicated -Port 7990
```

保留修复前记录、测试 XML、构建与独立进程日志；本次基线文件位于 `Logs/SnapshotDotFix/baseline`，用于区分已有工作区改动。
