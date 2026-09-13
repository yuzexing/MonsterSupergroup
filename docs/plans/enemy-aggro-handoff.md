# 怪物目标转移与模拟交接

普通怪和精英怪在当前目标倒地或断线后，重新选择各自最近的在线存活角色，并将模拟交给该角色所属客户端。Boss 保持服务端模拟。房主作为目标时走 ClientOwner；不存在合格角色时 Frozen。当前 HP 仍采用 OwnerFinal，目标资格使用服务端 Ledger 已确认的 Alive。

## 运行机制

- `NetworkEnemySimulationWorld.RequestTargetChange` 是统一的服务端入口；强制换人和自动重选使用同一条交接路径。角色的 Avatar 必须已完成 Session.AttachAvatar，防止倒地重连时的临时初始化状态成为候选。
- `EnemyHandoffProgress` 只保留一个在途 Epoch 和一个最新目标意图。首个有效快照确认完成；密集请求合并。同目标不会增加 Epoch。首帧超时一秒进入 ServerFallback，客户端就绪后重试归还。
- `EnemySimulationHandoff` 将 Assignment 和恢复基线放在同一个可靠同步字段。恢复之前不开启导航。旧 Epoch、旧 Owner 和重复序号不能写入当前记录；未来 Epoch 的移动快照最多缓存一份；同 Epoch 尚未完成基线恢复时也等待。基线序号为 0，不占用新模拟者的首个移动序号。
- 快照包含位置、速度、朝向、攻击动作身份和截止时间，以及击退的轨迹/进度。可靠攻击阶段消息也带恢复数据。分包按实际序列化长度控制；超过非可靠 MTU 的单份快照使用可靠通道。
- 近战恢复不重放 FSM 进入回调；本次攻击的锁定方向、目标点和计时保持不变，过期阶段只跳过，不补伤害。池化实例与异步回收使用世代标记，移交时丢弃旧的延迟碰撞回调。
- 击退交接继承剩余轨迹和硬直。已确认但未执行的指令按原 CommandId 重投；接收去重记录随快照移交。已在本地执行但尚未确认的普通击退另保留短期回执，动作结束后仍可去重，交接不延长回执寿命。回执达到上限时由确认指令执行，不追加本地预测。
- 模拟采用 `EnemySimulationClock.Now`：客户端用 Mirror 预测时间减去估计的单向传输时间，避免用延迟后的观察插值时钟判断动作截止时间。位置观察仍保留独立插值缓冲。

本期不增加仇恨分值、限时嘲讽或正式技能。后续技能完成服务端判定后调用上述目标请求入口即可。

## 人工验收

1. 启动 Gameplay Host 与两个客户端，分别记录 Player Debug 摘要中的 `P1` 简称、`ParticipantId` 与 `Avatar netId`。
2. 在 Editor Play Mode 使用 `Tools/Network Combat/Show Handoff Test Controls`，或给验收包添加 `--enemy-handoff-controls`。控制窗口仅在 Host 显示。
3. 点击一只怪物，再点目标角色按钮。用 Enemy Debug 核对 Simulator / Target / Epoch、转移原因、请求/合并/完成计数及首帧时间。普通角色状态仍看 Player Debug。
4. 开启 1 / 5 / 20 Hz 循环切换。客户端窗口分别观察 ClientOwner 与 Replica 转换；停止后应停在最后请求的合格角色。
5. 在前摇、攻击、恢复及击退时切换，观察原动作继续完成，没有重复攻击实例、位置回到出生点或击退重新开始。
6. 将 A 放在当前目标位置，B 放得比房主更靠近怪物，再使 A 倒地。该怪物应转 B；原来追踪其他人的怪物不变。所有人倒地后怪物 Frozen。
7. A 倒地后断线重连，核对摘要中的 `P1` 简称和 `ParticipantId` 不变、`Avatar netId` 更新、仍为 Downed，不能被选择为新目标。持续按方向键与 Dash，A 的位置应保持不变；其他存活玩家仍能移动。

Enemy Debug 是只读面板。验收控制器只编入 Editor 或带 `MONSTER_ENEMY_HANDOFF_VALIDATION` 的验收构建，正式发布包不提供该控制入口。

## 可重复测试

构建使用真实 Boot 和 Gameplay，不生成或覆盖场景：

```powershell
& 'D:/RealSoftware/6000.3.17f1/Editor/Unity.exe' -batchmode -quit -projectPath . -executeMethod MonsterSupergroup.Gameplay.Tests.EnemyHandoffValidationBuild.Build -logFile Logs/EnemyHandoff-build.log
./Tools/Run-EnemyHandoffValidation.ps1 -Profile normal -Duration 120 -Port 7993
./Tools/Run-EnemyHandoffValidation.ps1 -Profile impaired -Duration 120 -Port 7994
```

每次三进程运行使用 Gameplay 的 `NetworkEnemyBase` 与 `NetworkEnemySkeleton`。Boss 使用现有 ServerAuthoritative 模式及骷髅动作验证保持服务端执行，不宣称覆盖尚未接入的全部 Boss 技能。场景包含：模拟交接环路、近战三个阶段、Boss 动作连续性、真实 Ultimate 击退、首帧超时与就绪归还、旧 Owner/旧 Epoch/重复/乱序/先到快照注入、存活断线重连、20 只×20 Hz×120 秒、100 只×5 Hz×120 秒、交接等待期间倒地后的最近目标选择、无关怪物不变、倒地重连、全员倒地及卸载。每组压力结束时还等待三个窗口应用相同的最终目标和 Epoch。`impaired` 使用单向 100ms、0–20ms 抖动、10% 非可靠丢包和 10% 乱序。

`-Duration 5` 只用于快速检查，不能替代 120 秒压力验收。日志、各端 Epoch 轨迹 CSV 和场景结果保存在 `Logs/EnemyHandoff/<时间>-<profile>/`。结果以新端实际产生且服务端接受的移动快照确认，不以字段变化代替完成。

设置构建进程环境变量 `ENEMY_HANDOFF_RELEASE=1` 可生成非 Development 验收包 `Builds/EnemyHandoffRelease/EnemyHandoff.exe`，通过脚本的 `-Executable` 指定。测试程序集只包含在验收包中。

单元与集成测试也可从 Unity Test Runner 运行。命令行重跑核心用例（运行测试时不要加 `-quit`）：

```powershell
& 'D:/RealSoftware/6000.3.17f1/Editor/Unity.exe' -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testFilter 'EnemyHandoffTests;EnemySimulationTests;EnemyKnockbackContractTests' -testResults Logs/EnemyHandoff-editmode.xml -logFile Logs/EnemyHandoff-editmode.log
& 'D:/RealSoftware/6000.3.17f1/Editor/Unity.exe' -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testFilter 'EnemyHandoffGameplayTests;EnemyNetworkKnockbackTests;NetworkEnemyDebugPanelTests;GameplayHealthHUDLoadingTests;OrdinaryHitKnockbackPlayModeTests;UltimateStartupKnockbackPlayModeTests;ProductEnemyBase_ClientOwnerRunsNavigationWithoutServerTransform;SkeletonMelee_ReplicaDamagesLocalPlayerButExpiredActiveDoesNot' -testResults Logs/EnemyHandoff-playmode.xml -logFile Logs/EnemyHandoff-playmode.log
```

第二条命令也包含真实普通怪客户端导航、Replica 实际接触伤害及过期窗口两个 Sandbox 回归用例。

## 验收结果

2026-09-11，Unity 6000.3.17f1，Windows，KCP，真实 Boot/Gameplay。自动化使用 Host＋两个客户端，检查实际动作及服务端接受的移动；全部进程以 PASS 正常退出，无未处理异常。

### 自动化与构建

- EditMode：67/67 通过，`Logs/EnemyHandoff-acceptance-editmode2.xml`。
- PlayMode：27 个不同用例通过。`Logs/EnemyHandoff-acceptance-playmode2.xml` 为 26/26；之后 `Logs/EnemyHandoff-death-race.xml` 为 3/3，其中两个是重复回归，一个新增“Ledger 已确认怪物死亡但实体尚未收到死亡批次”竞态。该竞态中移动及攻击被拒绝，交接被清理。
- Development 最终验收包构建成功；三进程全场景快速回归通过：`Logs/EnemyHandoff/20260911-132423-normal/`。快速回归的两组压力各为 3 秒，仅用于最终包检查。
- 非 Development 最终验收包构建成功；异常网络三进程全场景快速回归通过：`Logs/EnemyHandoff/20260911-132608-impaired/`（每组压力 3 秒）。两个最终包均检查 Enemy Debug 已启用。构建日志分别为 `Logs/EnemyHandoff-build-final-development.log` 和 `Logs/EnemyHandoff-build-final-release.log`。

### 固定压力测试

以下四组均持续 120 秒，结束时三个进程确认一致的目标、模拟者和 Epoch。正常与异常 profile 在同一台 24 逻辑处理器机器并行运行，各自独立三进程和端口。

| 网络 | 怪物数 × 每只请求频率 | 周期请求数 | 已完成交接 | 合并请求 | 最大有效快照间隔 | 最终收敛 |
|---|---:|---:|---:|---:|---:|---:|
| 正常 | 20 × 20 Hz | 48,000 | 47,620 | 420 | 0.145s | 0.067s |
| 正常 | 100 × 5 Hz | 60,000 | 60,200 | 0 | 0.134s | 0.050s |
| 100ms 单向延迟、0–20ms 抖动、10% 丢包/乱序 | 20 × 20 Hz | 48,000 | 11,262 | 26,052 | 0.454s | 0.536s |
| 同上 | 100 × 5 Hz | 60,000 | 49,624 | 10,576 | 0.468s | 0.502s |

已完成计数包含初始及最终分配，因此可以超过周期请求数；同目标请求不增加 Epoch，不要求每个被合并的请求都形成交接。正常网络倒地转移首帧为 0.050s，满足 0.2s；异常网络为 0.267s，最终收敛均满足 2s。

- 正常网络完整记录：`Logs/EnemyHandoff/20260911-131404-normal/`。
- 异常网络完整记录：`Logs/EnemyHandoff/20260911-131404-impaired/`。
- `results.txt` 保存逐阶段结论，`host/a/b.log` 保存进程结果，`host/a/b-trace.csv` 保存每 0.1s 的目标、Owner、Epoch、动作身份、交接耗时、快照间隔、位置修正及拒收计数。

拒收旧 Owner / 旧 Epoch 是预期行为。正常两组分别记录 40/0 和 11,400/0 次；异常两组分别为 20,444/9,832 和 162,510/690 次；这些旧消息未覆盖当前移动记录。全场景三个端的轨迹采样分别为 252,236 和 159,526 行，采样到的最大基线位置修正分别为 2.122 和 6.408 个世界单位。这是所有场景和 Replica 视图的最大值，包含断线与回退过程，不代表稳态移动误差；保留原始 CSV 作为后续视觉平滑优化基线，本期未设位置修正阈值。

压力结果对应最终核心交接实现；之后仅补充了死亡确认早于实体更新时的拒收保护、非 Development 验收包面板启用及其检查。死亡保护由上述新增 PlayMode 竞态单独验证，最终包再执行快速回归。

### 逐场景证据

| 场景 | 验证与结果 |
|---|---|
| 最近目标、影响范围 | 三进程 `downed-nearest`：两只怪物分别选择 B、Host；原追踪 B 的怪物 Epoch 不变。正常/异常均通过 |
| 候选与稳定选择 | EditMode 检查距离、ParticipantId 平局、无候选；Gameplay 检查已确认 Alive 与 Avatar 恢复资格，通过 |
| 断线、存活重连 | `alive-disconnect-reconnect`：实际断线触发交接，新 Avatar 恢复后不抢回怪物，通过 |
| 倒地及交接中失效 | `downed-nearest` 在新端首帧被阻断时使其倒地，立即废止等待；`downed-reconnect` 检查倒地重连不可选，通过 |
| 全员倒地、恢复 | 三进程 `all-frozen` 检查 Frozen；Gameplay 测试提供合格角色状态后恢复并接受移动，通过 |
| 普通怪、精英、Boss 环路 | `ownership-ring` 实际接受新模拟者移动，Boss 保持服务端；`boss-action-continuity` 检查动作身份和截止时间不重启，通过 |
| 前摇、攻击、恢复 | `action-Warning/Active/Recovery` 使用真实骷髅近战，保持动作 ID、方向、目标点及截止时间；PlayMode 检查实际接触伤害和过期窗口不补伤害，通过 |
| 击退、硬直 | `ultimate-knockback` 使用真实大招；PlayMode 反复恢复同一轨迹及硬直，检查原截止时间和导航锁；普通击退回执去重测试，通过 |
| 消息竞态 | `packet-races` 注入旧 Owner、旧 Epoch、重复、乱序、先到移动快照，覆盖观察端 Replica→Replica；ABA 及高频最新意图由状态测试和实际环路覆盖，通过 |
| 超时、组件就绪 | `timeout-recovery` 阻断新端发送，验证一秒回退后服务端实际产生移动，恢复发送后归还，通过 |
| 死亡、销毁、卸载 | 新增 PlayMode 验证 Ledger 死亡竞态；三进程销毁清除交接，三个端结束时卸载 Gameplay，通过 |
| 密集请求 | 上表四组持续产生有效移动，最后目标收敛；三个端确认相同最终分配，通过 |

界面自动化检查面板组件启用及数据生命周期；本轮三进程采用无图形模式，未把自动化 PASS 当作界面目测完成。窗口排版、动作观感及人工频繁点击可按“人工验收”步骤检查，无需 Inspector。Boss 覆盖现有服务端模拟模式与骷髅动作，不包含尚未接入的其他 Boss 技能。

### 人工反馈修复：身份标签与倒地重连移动

Player Debug 保留 `P1` 简称，并在在线/离线摘要明确标注 `ParticipantId` 和当前/上次 `Avatar netId`。原先只有 `P1`，不足以直观核对字段含义。

倒地重连原先只恢复健康数据，新角色的移动 FSM 仍从 `Moving` 开始。方向输入与普通移动没有检查存活状态，因此出现 HP 为 0 仍能移动。现在输入与物理移动均检查角色是否处于忙碌状态（包括 Downed），即使没有经过本地受伤/死亡 FSM 也不能移动；不会因载入存档重播死亡事件。

- 修复前复现：`Logs/Downed-reconnect-before.xml`，4 个断言用例均重现问题，其中两条倒地路径仍产生 `(5, 1)` 移动速度。
- 修复后回归：`Logs/Downed-reconnect-after.xml`，40/40 通过，包含 `PlayerDashMovementTests`、`PlayerDebugSnapshotTests`、`NetworkPlayerDebugPanelTests` 和 `EnemyHandoffGameplayTests`。
- 三进程脚本新增 `downed-owner-input-blocked`：真实断线重连后连续输入方向与 Dash，检查位置、速度保持不变，同时验证摘要中的稳定 ParticipantId 与新 Avatar netId。

正常网络三进程复跑通过：`Logs/EnemyHandoff/20260911-150145-normal/`。新阶段记录重连角色 `ParticipantId=3`、`Avatar netId=137`，输入拦截与可见标签检查通过。每组压力仅跑 3 秒，用于此次恢复和显示修复回归。

非 Development 验收包的异常网络三进程复跑通过：`Logs/EnemyHandoff/20260911-150346-impaired/`，同样包含真实重连后的输入拦截与身份标签检查。此次两次三进程均无未处理异常，三个进程全部 PASS。已更新项目内 `Builds/EnemyHandoff/EnemyHandoff.exe` 与 `Builds/EnemyHandoffRelease/EnemyHandoff.exe`；构建日志为 `Logs/Downed-reconnect-build-development.log`、`Logs/Downed-reconnect-build-release.log`。
