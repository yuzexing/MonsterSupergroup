# M3 普通命中击退恢复

本次保留 Enemy 单一 SimulationOwner、Mirror 权限、正式 Boot → Gameplay、GAS、PlayerBuildRuntime 与 Modifier。M4–M6 未变更。

## 复现证据

正式 Gameplay 的 `NetworkEnemyBase` 设置 `productMovementOnly: 1`，`InitNetworkMovementOnly` 不创建旧 FSM。原普通命中路径是 Circling → OvertimeHitBox → BasePlayerAttack → WeaponBehaviour.OnNativeGasHit → EnemyController.ResolveNativeGasHit → ApplyKnockBackCore；最后一个方法在 FSM 为空时返回。

`Logs/M3Knockback/baseline-5.xml` / `baseline-5.log` 记录了修复前的真实 Boot、单球 Physics2D、Native GAS、Mirror 结果：同一 root 五个独立事件，local/canonical HP 均由 10000 降至 9940，击退位移为 0；位移断言失败。该用例没有复现此前 local/canonical HP 长时间不一致，不能据此宣布那个问题已修复。

早期夹具遇到缺失 FMOD bank，以及暂停导航后原 Prefab 重力导致的下落。夹具仅复制内存中的 Circling 音效配置并移除副本的 FMOD 触发，保留真实动画、Collider、HitBox、GAS 和网络路径；隔离位移时将测试 Rigidbody 重力设为 0，正式 Prefab 未改。`baseline-4` 的位移包含下落，不作为击退恢复证据。

联机验收还发现了既有接管位置问题：`Dedicated-20260910-111730-810/server.log` 中最后快照为 `(6.06, 8.61)`，接管后的 Rigidbody/Transform 却回到出生点 `(5, 5)`。`SnapServerSimulationTo` 原先只写 Rigidbody，紧随其后的 Kinematic → Dynamic 切换会恢复尚未同步的 Transform 位置。现于同一接管入口同步两者，仍使用服务器已接纳的最后快照，并清零速度；不新增位移预测、接力或同步协议。多进程夹具增加了接管后、下一次攻击前的位置连续性断言，避免只检查最终收敛而漏掉跳位。

## 最终调用链与权限

- Native 命中完成后，EnemyController 保留飘字/闪烁并发送无网络依赖的同步通知。网络管理的 Enemy 不再进入旧普通击退 FSM；致死命中仍由原死亡流程处理。
- Agent 将可选 OrdinaryHitKnockback 信息附到 Collector 中同一个 DamageContext.EventId 的 CombatResult。单球连续接触保留同一 root，但每次命中独立。
- 当前模拟者先记住消费身份，再立即调用现有移动协程。非模拟者只保留本地反馈，不移动根节点或碰撞体。
- 服务器仍通过原 Gateway/AttackRegistry/Ledger 接纳伤害。只有接纳的普通命中才触发击退路由；非法、过期或旧 epoch 附加信息不改变伤害接纳规则。周期伤害不获得普通击退。
- 服务器在攻击 root 准入时复制武器预设，并随 root 退休/断线释放；不执行攻击者 GAS，不接收客户端曲线或最终坐标。
- 通用击退命令复用可靠 Endpoint，显式区分 OrdinaryHit 与 Ultimate。普通命令携带伤害事件身份和冻结倍率；本地快速执行、Host 服务器分支、回包和重发共用消费记录。
- 正在击退或免疫时忽略新击退，该事件不能在恢复后补执行。切换/冻结/禁用/死亡取消当前协程并清理速度；旧 epoch 指令不重路由、不接力剩余位移。
- Observer 沿用既有快照和插值，不新增 Transform 同步器或位置回滚。

普通距离保持 `preset.distance * (1 + snapshot.KnockbackMultiplierSum) * enemy.KnockBackMultiplier`。Circling 原预设 distance=0.2、speedMultiplier=10、staggerTime=0 未改；Ultimate 保留原 BruteForce 附加倍率 1。网络位移自然结束时清零残留速度，再恢复击退前的导航状态，避免暂停导航时继续漂移。

## 改动边界

生产改动集中于 EnemyController 的普通命中出口、现有移动入口、CombatResult/Collector/Gateway、AttackRegistry，以及现有 Enemy World/Agent/Endpoint；另修复上述同一 Agent 内的接管位置写入。新增代码不创建新 Scene/Prefab，不改变怪物仇恨目标或模拟权分配。

CombatResult 新增可选字段，命令增加类型和伤害事件身份，收发双方需要使用同一构建。没有存档或玩法数据迁移。带宽估算增加这部分名义字段长度；Mirror 当前使用变长整数，实际报文长度不等于该估算。

旧 `NetworkCombatPhase7Tests` 对默认武器仍断言 ID 2，已依据当前正式 Prefab 改为 ID 6；未更改玩家默认武器资产。

| 文件组 | 改动 |
| --- | --- |
| `EnemyController.cs`、`EnemySimulationAuthority.cs` | Native 命中通知、普通倍率入口、移动结束清零速度、识别网络管理 |
| `CombatResult.cs`、`EnemyKnockbackContracts.cs`、`CombatBandwidthEstimator.cs`、`ClientCombatCollector.cs` | 可选击退信息、命令原因与命中身份、带宽估算、同批附加 |
| `NetworkWeaponCombatAdapter.cs`、`ServerAttackRegistry.cs`、`ServerCombatGateway.cs` | root 预设冻结与释放、仅在既有伤害接纳后通知路由 |
| `NetworkEnemySimulationWorld.cs` / `.Knockback.cs`、`NetworkEnemySimulationEndpoint.cs` | 服务器校验和当前模拟者投递，复用有界等待与可靠 Endpoint |
| `NetworkEnemySimulationAgent.cs` / `.Knockback.cs` | 本端即时消费、跨端消费去重、生命周期清理、接管位置连续性 |
| `OrdinaryHitKnockbackContractTests.cs`、`OrdinaryHitKnockbackPlayModeTests.cs`、既有击退测试 | 合同、真实单球、倍率、去重与清理回归 |
| `OrdinaryKnockbackProcessProbe.cs`、`OrdinaryKnockbackValidationBuild.cs`、`Tools/Run-OrdinaryKnockbackProcessValidation.ps1` | 仅测试程序集中的正式 Boot 多进程夹具、构建、退出码与截图记录 |

## 自动验证与实际结果

以下是实际测试结果；动态观感的人工验收单列，不以自动通过代替。

| 项目 | 当前结果 |
| --- | --- |
| 修复前真实单球用例 | baseline-5：五次独立命中，HP 一致，位移 0，预期失败 |
| EditMode 合同与既有网络规则 | contracts-3.xml：336/336，通过；包括新增可选信息、事件身份、序列化和预设生命周期 |
| PlayMode 定向回归 | targeted-4.xml：29/29，通过；包括普通连续命中、倍率、重叠/重复、旧 epoch、冻结/禁用清理及原 Ultimate、M2 相机回归；Unity 退出码 0 |
| Host + Client（补充连续性断言前） | Host-20260910-110812-756：通过，退出码均 0；后来新增的接管连续性断言在旧写入方式下失败，不能仅以该轮通过判定完成 |
| 延迟/丢包（补充连续性断言前） | Dedicated-20260910-110812-756：三进程通过且退出码均 0；新增连续性断言随后复现了接管跳位，修复后结果见下 |
| 最终 server-only + 两个 Client，含延迟/丢包 | Dedicated-20260910-112202-614：全部通过，三进程退出码均 0；接管最后快照及实体位置均为 `(6.10, 8.72)` |
| 最终无图形 Host + Client | Host-20260910-112306-150：全部通过，两进程退出码均 0，与可见测试使用同一 build-5 构建 |
| 最终可见 Host + Client | Host-20260910-112202-614：两端玩法断言全部通过，接管位置一致；Host 退出码 0，但 Client 退出码 `-1073741819 / 0xC0000005`，整轮运行器判为失败 |

早期失败已分开记录：`Dedicated-20260910-110041-270` 的接管阶段先于客户端接收新 epoch，首个旧 epoch 请求按规则被丢弃；夹具已等待新 epoch 再开始下一用例。`Host-20260910-111505-986` 在开局读写阶段文件时发生共享冲突；已改为写完临时文件再原子发布标记。两者未通过放宽伤害准入、重发旧命中或放宽验收阈值解决。

复现修复后的单球用例记录五次实际击退、累计位移约 0.80，local/canonical HP 均为 9940；没有服务器回包造成的额外击退。

进程夹具覆盖本端、跨端、双方同时命中、原模拟者攻击中断线、ServerFallback、新 Avatar 重连和 ServerAuthoritative。最后一阶段恢复正式导航和 Prefab 原重力，确认每次击退后能恢复实际追逐，再暂停导航测量静止收敛。每阶段记录事件 ID、反馈次数、执行次数、HP、模拟者与 epoch；停止运动后的快照误差要求两秒内不超过 0.02 世界单位。网络扰动使用已安装 Mirror LatencySimulation：单向附加延迟 100ms、jitter 0.02、非可靠快照丢包和乱序各 5%。

最终延迟组本端/跨端/接管/ServerAuthoritative 各为 5 次命中、5 次击退；同时攻击为 10 次命中、10 次击退。可见 Host 组同时攻击为 10 次命中、5 次击退，符合到达时间重叠时忽略新击退的规则。各阶段 Observer 满足两秒内误差 ≤ 0.02，最终 local/canonical HP 均为 9628。

已查看可见运行目录中的 `self-hit-client.png`、`cross-hit-host.png`、`fallback-hit-host.png` 与 `authoritative-settled-client.png`：真实单球、敌人及 Debug 的模拟者/epoch/HP 可观察。静态截图不能证明运动过程没有抖动，相关人工项目仍待确认。初版 1280×720 采样受 Debug 面板遮挡，现仅在测试运行器使用 1920×1080，并等待现有相机跟随稳定后采样，正式相机配置未改。

可见运行的 Windows 应用程序错误定位到 `D3D12Core.dll`，偏移 `0x264831`，发生于双方均打印玩法 PASS 后的进程退出阶段。原始事件保存在该运行目录的 `windows-native-exit.json`。本次模块与 M2 文档曾记录的 AppUINativePlugin/UnityPlayer 不同，不能仅凭相同异常码认定同一根因；未修改渲染后端或吞掉非零退出码。

2026-09-10 后续诊断将故障范围缩小到退出时保存 D3D12 管线缓存。仅隔离缓存目录不足以解决，复制旧缓存仍复现。启动器现提供可选 `-ForceD3D11`：同一构建三轮可见 Host + Client 和一轮 server-only + 两 Client（含网络扰动）均通过，九次进程退出码为 0。正式图形 API 设置未改，D3D12 内部问题仍未根治；证据、对照及继续测试命令见 [Player 原生退出诊断](native-player-exit-diagnosis.md)。

## 人工步骤

1. 从正式 Boot 启动 Host 与独立 Client，进入 Gameplay，确认各自 Owner 镜头及 Enemy Debug 信息正常。另跑 server-only + 两个 Client。
2. 选择同一只 Enemy，由它的模拟者用 Circling 持续接触，再由另一位玩家接触；观察飘字、闪烁、击退方向及重复命中。非模拟者不得先移动根节点再被快照拉回。
3. 两人同时攻击，允许正在击退时忽略重叠击退；每个独立事件不能产生两次位移，停止攻击后各端位置收敛。
4. 原模拟者攻击中断线：旧击退取消；等待接管完成后，另一玩家的新命中由服务器执行。重连后不能恢复旧球、旧击退或旧目标。
5. 验证实际导航开启时，击退结束后追逐恢复，无持续漂移/抖动；使用原 Ultimate 释放验证原启动击退、伤害波次和本端震屏未回归。
6. 完全停止并重新进入 Gameplay，确认无旧 Enemy、订阅或击退残留。音效 bank 缺失、画面体验和进程原生退出异常需单独记录，不以参数断言替代人工验收。

运行入口：`Tools/Run-OrdinaryKnockbackProcessValidation.ps1`。默认无图形测试；`-Dedicated -ImpairedNetwork` 验证服务器与双 Client 网络扰动；`-CaptureFrames -VisibleWindows` 用于可观察的实际画面采样。构建入口是 `OrdinaryKnockbackValidationBuild.Build`，场景仍只有正式 Boot/Gameplay。

## 回退与未验证项

M3 以独立提交回退，恢复本次命中出口、合同、路由及接管位置写入变更，不覆盖 M1/M2 或用户改动。Unity 测试/构建自动改写的性能测试资源、预加载设置和 URP 运行时列表不属于交付修改。

代码和自动玩法验证已交付；M3 尚未标为验收完成。两种联机组合的人工动态观感（方向、恢复追逐、持续抖动、Ultimate 震屏）仍待确认。原生退出异常已有经过本机重复验证的 D3D11 启动绕过措施，可继续人工测试；正式 D3D12 路径的问题仍保留。此前 Circling HP 差异未在本轮有效用例复现，也未绕过伤害准入；它没有阻塞本轮击退断言，但根因仍未确认。
