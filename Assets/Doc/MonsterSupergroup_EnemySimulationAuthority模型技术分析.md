# MonsterSupergroup Enemy Simulation Authority 模型技术分析

> 审计项目：F:\UnityStore\MonsterSupergroup
> 审计日期：2026-08-27
> 文档状态：代码审计与接入设计分析；尚未实施 Enemy Simulation Authority
> 事实来源：当前工作树，包括尚未提交的用户修改
> 本阶段约束：不修改 Combat Authority、Player HP、Status Tick、Knockback / Pull / Physics、Boss Simulation

## 1. 结论摘要

目标模型可以接入当前项目，但不能通过简单切换 Mirror 的 NetworkIdentity Authority 或把 NetworkTransformReliable 改成 ClientToServer 来完成。

当前仓库实际上存在三条彼此不同的 Enemy 路径：

1. **NetworkCombatSandbox 路径已经联网可运行**：NetworkEnemy 由 Server Spawn，Server 上的 LocalEnemyChase 追踪最近玩家，NetworkTransformReliable 从 Server 同步 Transform。它没有完整的 HellMaiden Enemy 攻击、状态机和 Pathfinding。
2. **EnemyBase 产品路径拥有完整 HellMaiden 行为，但尚未联网接入**：EnemyController、EnemyAIManager、EnemyDefaultMovement、EnemyAILerpMovement、EnemyAttack 和 PlayerDamageInteraction 都按单机假设运行；EnemyBase 没有 NetworkIdentity、NetworkCombatantAdapter 或网络 Transform 驱动。
3. **Assets/Script 下仍有一套旧的 Mirror Server-authoritative Enemy**：Enemy、EnemySpawner、EnemyChase、Projectile、Health 直接在 Server 模拟和扣血。这条路径与当前 GAS / CombatLedger 架构平行，不应作为新模型的基础。

因此，推荐方向是：

~~~text
Server 保持 NetworkIdentity Authority
    ↓ 可靠分配
AggroTarget Player + SimulationOwner Player + SimulationEpoch
    ↓
SimulationOwner Client 独占运行普通/Elite Enemy 的 AI、Movement、Attack progression
    ↓ 不可靠高频 Snapshot + 可靠状态边沿
Server 校验、缓存、转发
    ↓
Observer Client 只做 Transform 插值和攻击/动画表现
~~~

Combat 继续保持：

~~~text
攻击者 Owner Client
    → 本地 Projectile / Hit / GAS / PredictedLethalHit
    → CombatResultBatch
    → ServerCombatGateway
    → CombatLedger Canonical Enemy HP / Alive
    → CanonicalWorldReplica / ConfirmedKill
~~~

最大的接入风险不是 Mirror API，而是 EnemyBase 同时带有旧 EnemyStats.Health 和新 CombatantBehaviour，当前没有唯一 HP 来源；同时旧 PlayerDamageInteraction 会缓存 GameDirector.Instance.Player，无法正确处理多玩家本地受击。这两个问题必须在迁移产品 Enemy 的 Combat/Attack 前解决。

---

## 2. Current Reality

### 2.1 场景与 Prefab 的真实状态

[EditorBuildSettings.asset](../../ProjectSettings/EditorBuildSettings.asset) 当前仅启用：

- Assets/Scenes/Boot.unity
- Assets/Scenes/MainMenu.unity
- Assets/Scenes/Gameplay.unity

[NetworkCombatSandbox.unity](../_Project/Scenes/Development/NetworkCombatSandbox.unity) 不在 Build Settings 中，因此目前没有正式联机场景。

当前两个关键 Enemy Prefab：

| Prefab | 当前用途 | 网络能力 | AI / Attack 能力 |
| --- | --- | --- | --- |
| [NetworkEnemy.prefab](../_Project/Content/NetworkCombat/NetworkEnemy.prefab) | 联机开发沙箱 | NetworkIdentity、ServerToClient NetworkTransform、NetworkCombatantAdapter | 仅 LocalEnemyChase，无完整攻击 |
| [EnemyBase.prefab](../_Project/Content/LocalCombat/EnemyBase.prefab) | HellMaiden 产品 Enemy 恢复基线 | 无 NetworkIdentity、无网络快照组件 | 完整 EnemyController、两种 Movement、Attack、Animancer、旧 Status |

EnemyBase 当前同时存在 EnemyController / EnemyStats 与 CombatantBehaviour。这表示“旧 HellMaiden HP”和“新 GAS/网络 HP”是两个独立状态，尚未建立单向绑定。

### 2.2 当前沙箱 Enemy 调用链

代码证据：

- [NetworkEnemySandboxSpawner.cs](../_Project/NetworkCombat/Mirror/NetworkEnemySandboxSpawner.cs)
- [NetworkEnemyServerDriver.cs](../_Project/NetworkCombat/Mirror/NetworkEnemyServerDriver.cs)
- [LocalEnemyChase.cs](../_Project/Gameplay/Local/LocalEnemyChase.cs)

真实流程：

~~~text
Server: NetworkEnemySandboxSpawner.OnStartServer
    → Instantiate NetworkEnemy
    → NetworkServer.Spawn
    → NetworkCombatantAdapter.OnStartServer
        → CombatLedger.RegisterEntity(ServerCanonical)
    → NetworkEnemyServerDriver.OnStartServer
        → 启用 LocalEnemyChase
        → 从 CombatTeamBehaviour.ActiveTeams 选择最近的存活 Player
        → LocalEnemyChase.FixedUpdate / Rigidbody2D.MovePosition
    → NetworkTransformReliable(ServerToClient)
        → Observer Transform

Owner Client Player Attack
    → 本地 Projectile / Hit / CombatEvent
    → CombatResult
    → Server CombatLedger
    → Canonical Enemy HP
    → HP <= 0 时 ConfirmedKill
    → NetworkEnemyServerDriver 收到 Canonical Dead
    → NetworkServer.Destroy
~~~

当前沙箱 Enemy：

- 没有完整 Enemy AI State。
- 没有 Warning / Attack / Recovery。
- 不会攻击 Player。
- Spawner 按网格批量生成 120 个 Enemy，并非按某个 Player 周围生成。
- Server 每 0.25 秒重新选择最近 Player，已经隐含动态 Retarget，但没有 SimulationOwner 概念。

### 2.3 当前 HellMaiden EnemyBase 调用链

代码证据：

- [EnemyFactory.cs](../_Project/Gameplay/Combat/AI/Enemy/EnemyFactory.cs)
- [EnemyController.cs](../_Project/Gameplay/Combat/AI/Enemy/EnemyController.cs)
- [EnemyAIManager.cs](../_Project/Gameplay/Combat/AI/EnemyAIManager.cs)
- [BaseEnemyMovement.cs](../_Project/Gameplay/Combat/AI/Enemy/BaseEnemyMovement.cs)
- [EnemyDefaultMovement.cs](../_Project/Gameplay/Combat/AI/Enemy/EnemyDefaultMovement.cs)
- [EnemyAILerpMovement.cs](../_Project/Gameplay/Combat/AI/Enemy/EnemyAILerpMovement.cs)
- [EnemyAttack.cs](../_Project/Gameplay/Combat/AI/Enemy/EnemyAttack.cs)

真实流程：

~~~text
EnemySpawnParams.AttackTarget
    → EnemyFactory.CreateEnemy
    → 从 Pool 获取 EnemyController
    → EnemyController.Target = AttackTarget
    → EnemyController.Init
        → 初始化 EnemyStats / EnemyStatus
        → 建立 StateMachine
        → 注册到 EnemyAIManager

EnemyAIManager.Update（分批）
    → EnemyController.UpdateDestination
    → EnemyController.RunUpdate
    → Moving.TryAttack

EnemyAIManager.FixedUpdate
    → EnemyController.RunFixedUpdate
    → EnemyDefaultMovement 或 EnemyAILerpMovement
    → Rigidbody2D / Transform

EnemyAIManager.LateUpdate
    → EnemyController.RunLateUpdate
    → Warning / Attacking / Recovery Tick
    → EnemyAnimator
~~~

EnemyController 的真实状态包括：

- Moving
- Warning
- Attacking
- Recovery
- Knockback
- Dead
- InstantDead
- Deactivating
- Deactivated

这是一套完整的内部模拟状态机。Observer 不需要复制整个 StateMachine、寻路路径、stuck 检测、rubberband tracker 或 Coroutine；Observer 只需要足以恢复表现和本地攻击命中窗口的 replicated presentation state。

### 2.4 当前 Enemy Attack 与 Player 受击

EnemyAttack 不是单纯由 Unity Animation Event 推进：

- Warning、Attack、Recovery 使用 Time.time 记录开始时间。
- 阶段时长等于配置时间加 Animancer Clip 长度。
- EnemyAttackMelee 在 Warning 创建攻击 Prefab，在 AttackEnter 启用 DamageInteraction / HitBox。
- 状态结束由每帧比较经过时间触发。

[PlayerDamageInteraction.cs](../_Project/Gameplay/Combat/Interactions/PlayerDamageInteraction.cs) 在 Awake 中缓存 GameDirector.Instance.Player。即使触发碰撞的是另一个 PlayerHitbox，DamagePlayer 仍然伤害这个全局 Player。

因此当前代码不能直接满足：

~~~text
每台 Client 只根据本地 Player 与 Enemy Damage Area 的碰撞
决定自己的 Player 是否受击
~~~

这不是 Snapshot 层能自动解决的问题。至少需要把受击目标从“全局 Player”改为“本次碰撞对应的本地 Owner Player”，但该修改应放在后续 Enemy Attack 联网阶段，不属于第一阶段 Movement vertical slice。

### 2.5 当前 Combat / HP / Death 调用链

代码证据：

- [CombatResult.cs](../_Project/NetworkCombat/Contracts/CombatResult.cs)
- [MirrorNetworkCombatBridge.cs](../_Project/NetworkCombat/Mirror/MirrorNetworkCombatBridge.cs)
- [ServerCombatGateway.cs](../_Project/NetworkCombat/Server/ServerCombatGateway.cs)
- [CombatLedger.cs](../_Project/NetworkCombat/Server/CombatLedger.cs)
- [NetworkCombatantAdapter.cs](../_Project/NetworkCombat/Mirror/NetworkCombatantAdapter.cs)

Enemy HP：

~~~text
Owner Client 本地计算攻击结果
    → ClientCombatCollector
    → 每 0.05 秒 CombatSubmissionBatch
    → 玩家拥有的 MirrorNetworkCombatBridge.Command
    → ServerCombatGateway 轻量校验
    → CombatLedger.Apply
    → CanonicalEntityState
    → ClientRpc
    → CanonicalWorldReplica
    → NetworkCombatantAdapter.ApplyCanonicalHealth
~~~

关键事实：

- Enemy 使用 CombatEntityAuthority.ServerCanonical。
- Server 不重新计算攻击力、暴击、Build 或 Projectile Hit。
- 重复 EventId、非法 Source、无目标、CanonicalDead、绝对无敌等会被拒绝。
- Client 的 TargetStateVersion 过旧不会自动导致伤害被拒绝。
- PredictedLethalHit 在攻击者本地触发 Build。
- ConfirmedKill 只由 Server CombatLedger 产生一次。

Player HP：

- Player 使用 CombatEntityAuthority.OwnerFinal。
- Player 本地 CombatantBehaviour.HealthChanged 产生 PlayerHealthReport。
- Server 只接受该 Player Owner 提交的、版本更新的最终 HP。
- 普通 CombatResult 不能直接修改 OwnerFinal Player；CombatLedger 会以 WrongAuthority 拒绝。

所以 CombatResult 与 PlayerHealthReport 不是同时竞争修改同一个 Player HP：

~~~text
Enemy Canonical HP ← CombatResult
Player Final HP    ← Owner Client 的 PlayerHealthReport
~~~

本次 Enemy Simulation Authority 不应改变这一边界。

### 2.6 当前 Status

新 GAS Status 已有：

- 所有 Client 可查询的 Canonical Status Replica。
- SourceClient、Server、TargetOwnerClient 三种 ExecutionAuthority。
- ServerStatusRegistry 维护 Add / Remove / Stack / Duration / Version。
- SourceClient 断线时，Server 可把仍需 Tick 的 SourceClient Status failover 为 Server。

但 HellMaiden EnemyBase 仍带有另一套 EnemyStatus / EnemyStatusResolver：

- Slow 会直接修改 EnemyStats.SpeedMultiplier。
- Burn / Poison / Bleed 会直接调用旧 EnemyController.Damage。
- Status VFX 和旧 HP、Movement 紧密耦合。

因此“新 GAS Canonical Status”和“旧 EnemyStatus”尚未合并。SimulationOwner 改造如果直接关闭 Observer EnemyController 或 EnemyAIManager，可能同时改变旧 Slow、Burn、Poison 的运行语义。

### 2.7 当前 Knockback / Pull / Physics

EnemyController.ApplyKnockBack 和 BruteforceKnockBack 会：

- 转入 Knockback 状态。
- 取消或保留当前 Attack。
- 停止普通 Movement。
- 通过 BaseEnemyMovement Coroutine 持续写 Rigidbody2D.linearVelocity。
- 完成后返回 Moving、原攻击状态或 Death。

这不是纯表现效果，而是会覆盖 Transform、AIState 和攻击进度。多个 Client 若各自执行 Knockback，同时 SimulationOwner 又持续上报 Snapshot，将形成多个 Transform writer。

当前没有：

- Knockback EventId 排序。
- SimulationOwner movement-effect inbox。
- Server 对同一 Enemy 多个位移效果的收敛。
- Snapshot 对 Knockback 的明确优先级。

所以本阶段必须保持 Knockback 不变且不接入第一阶段；正式接入前需要用户确认语义。

### 2.8 当前 Disconnect / Late Join / Pooling

当前断线处理：

- MirrorNetworkCombatBridge.OnStopServer 会注销 Source、Client Event Identity，并调用 Status source disconnect failover。
- Mirror 默认会销毁断开连接拥有的 Player NetworkIdentity。
- NetworkEnemy 仍是 Server Authority，不会随 Player Authority 自动销毁。
- 没有 SimulationOwner assignment、Enemy Snapshot cache 或 Enemy reassignment。

当前 Late Join：

- CombatGateway 可以向新 Client 发送 Canonical HP / Status Snapshot。
- 没有 Enemy Transform、AI presentation state、AggroTarget 或 SimulationOwner 的 Late Join Snapshot。

当前 Pooling：

- HellMaiden EnemyPool 复用同一个 EnemyController GameObject。
- EnemyController ID 不是 NetworkIdentity.netId。
- Pool Return / Reuse 会保留同一个 Unity InstanceID，但表示新的 gameplay lifetime。
- 若没有 SpawnGeneration / SimulationEpoch，旧 Snapshot 可能被错误应用到复用后的 Enemy。

### 2.9 当前 Authority Map

| 系统 | 当前沙箱真实执行者 | EnemyBase 当前真实执行者 | 目标模型 |
| --- | --- | --- | --- |
| Player Movement | Owner Client | 单机本地 | Owner Client |
| Enemy Spawn | Server | 调用 EnemyFactory 的本地进程 | Server |
| AggroTarget | Server 选最近 Player | EnemySpawnParams / 本地脚本直接写 Transform | Server |
| Enemy AI | Server，仅轻量 Chase | 每个实例所在进程 | SimulationOwner Client |
| Enemy Movement | Server | 每个实例所在进程 | SimulationOwner Client |
| Enemy Attack progression | 尚未实现 | 每个实例所在进程 | SimulationOwner Client |
| Enemy Animation | 只随 Transform，无完整攻击 | 本地 AI 直接驱动 | Owner 驱动状态，Observer 播放表现 |
| Player Attack / Projectile | Owner Client 本地 | 新旧两套尚未合并 | Owner Client |
| Player → Enemy Hit | Owner Client | 本地旧 Hit | Owner Client |
| Damage / Crit / Build | Owner Client | 新旧两套尚未合并 | Owner Client |
| Enemy Canonical HP | Server CombatLedger | EnemyStats 与 CombatantBehaviour 双份 | Server CombatLedger |
| Predicted Enemy Death | 攻击者 Client | 旧 EnemyController 本地 Death | 攻击者 Client |
| ConfirmedKill | Server | 旧 OnKill 与新 ConfirmedKill 双语义 | Server |
| Enemy → Player Hit | 沙箱未实现 | PlayerDamageInteraction 的全局单 Player | 被攻击 Player 的本地 Owner Client |
| Player Final HP | Owner Client + PlayerHealthReport | 旧 Player.Damage | Owner Client + PlayerHealthReport |
| Status Registry | Server | 旧 EnemyStatus 本地 | 保持当前新 GAS 模型 |
| Status Tick | SourceClient / Server / TargetOwnerClient | 旧 Resolver 所在进程 | 保持当前新 GAS 模型 |
| Knockback | 未联网 | 调用者本地 Rigidbody/Coroutine | 本阶段不变，后续确认 |
| Enemy Despawn | Server Confirmed Dead 后 Destroy | 本地 Pool Return | Server 控制 Network lifetime |

---

## 3. Desired Model Mapping

### 3.1 现有系统迁移表

| 现有类 / 资源 | 当前职责 | 目标职责 | 动作 | 原因 |
| --- | --- | --- | --- | --- |
| NetworkIdentity | Mirror 对象身份与 Server Authority | 保持不变 | Keep | 不频繁 AssignClientAuthority |
| NetworkTransformReliable（Enemy） | Server → Client Transform | 移除普通/Elite 的 Transform 写入职责 | Replace | 与 SimulationOwner Snapshot 双写冲突 |
| NetworkEnemyServerDriver | Server Chase、Retarget、Canonical Death Destroy | Server lifetime + assignment；不再持续跑普通 Enemy AI | Split | AI/Movement 转给 SimulationOwner |
| NetworkEnemySandboxSpawner | Server 批量网格生成 | Server Spawn 时同时选择 AggroTarget 与 SimulationOwner | Modify | Owner 不允许为空 |
| LocalEnemyChase | Server 轻量 Chase | 仅可作为 Phase 1 验证用 Owner-side simulator | Move / Later Remove | 不是最终 HellMaiden AI |
| EnemyFactory / EnemySpawnParams | 本地 Pool、AttackTarget 注入 | 接收 Server assignment 后解析目标 | Modify | Transform 引用不能作为网络身份 |
| EnemyController | 完整 AI State 与攻击流程 | 只在 SimulationOwner 上推进；Observer 提供表现入口 | Split | Observer 不能独立产生第二套世界 |
| EnemyAIManager | 全局批处理所有 Enemy | 只 Tick 本机拥有 Simulation 权限的 Enemy | Modify | 不能简单禁用整个 Manager |
| EnemyDefaultMovement / EnemyAILerpMovement | Rigidbody / Pathfinding 写 Transform | 仅 SimulationOwner 运行 | Keep + Gate | 保留现有移动算法 |
| EnemyAnimator | AI 直接调用 Animancer | Owner 本地照常；Observer 根据 replicated presentation state 播放 | Modify | Observer 不需要完整 AI |
| EnemyAttack | Time.time 推进 Attack phases | Owner 推进；Observer 从 state sequence/start time 重建表现 | Split | 单个 AttackStart 到达时间不足以保持 timing |
| PlayerDamageInteraction | 缓存全局 Player 并扣血 | 只伤害触发碰撞的本地 Owner Player | Later Modify | 当前实现不支持多人 |
| CombatResult / CombatLedger | Enemy Canonical HP | 保持不变 | Keep | SimulationOwner 不拥有 HP |
| PlayerHealthReport | Player Owner-final HP | 保持不变 | Keep | Enemy Simulation 不改变 Player HP 权威 |
| ServerStatusRegistry | Canonical Status + executor | 保持不变 | Keep | Status 不属于本次修改 |
| EnemyStatus / Resolver | 旧 Slow/DOT/VFX | 后续与新 GAS 合并 | Defer | 不能在本阶段擅自改变 Tick ownership |
| BossController 系列 | Boss AI / Attack | Server Simulation 路径 | Keep separate | Boss 不参与 Client Simulation |
| Assets/Script/Enemy 等 | 旧 Server-authoritative 示例 | 不进入目标架构 | Remove later / Ignore now | 与当前 GAS 平行 |

### 3.2 推荐职责边界

~~~text
Gameplay / Enemy Core（不依赖 Mirror）
    EnemyController
    Enemy movement
    Enemy attack state
    Enemy presentation adapter
    Simulation role gate

NetworkCombat / Mirror Adapter
    Server assignment registry
    Snapshot receive / validate / cache / relay
    Observer interpolation
    Late join snapshot
    Disconnect handoff hook

GAS Core
    不感知 Mirror
    不感知 SimulationOwner

ServerCombatGateway / CombatLedger
    继续只处理 CombatResult、Canonical HP、Death、Status
~~~

SimulationOwner 消息必须通过“玩家拥有的 NetworkBehaviour”提交，或通过 Server-owned world object 上允许并验证 sender 的 Command 提交。不能假设 Client 能在 Server-owned Enemy NetworkIdentity 上直接调用 Command。

---

## 4. EnemySimulationSnapshot 建议

### 4.1 Assignment 与 Snapshot 应分离

SimulationOwner、AggroTarget 和 Epoch 属于低频、可靠的 Authority assignment；Position 属于高频、可丢弃的 Snapshot。将它们全部塞进一个可靠消息会增加带宽和队头阻塞。

推荐两个契约。

#### EnemySimulationAssignment（可靠、有序）

| 字段 | 是否需要 | 原因 |
| --- | --- | --- |
| EnemyEntityId | 必须 | 使用 NetworkIdentity.netId 关联对象 |
| SimulationOwnerPlayerId | 必须 | Gameplay owner 身份 |
| AggroTargetPlayerEntityId | 必须 | Client 解析实际 Transform |
| SimulationEpoch | 必须 | 每次重新分配递增，拒绝旧 Owner Snapshot |
| AssignmentSequence | 建议 | 丢弃乱序 assignment |
| EffectiveServerTime | 建议 | Handoff / attack timeline 对齐 |
| EnemyKind / SimulationMode | 建议 | Normal、Elite、Boss 的明确边界 |

SimulationOwnerPlayerId 应是 gameplay Player entity ID；Server 另行维护 PlayerId → NetworkConnection 映射。不能把 connectionId 直接暴露为长期 gameplay identity。

#### EnemySimulationSnapshot（不可靠、有序或自带序号）

| 字段 | 是否需要 | 当前代码依据 |
| --- | --- | --- |
| EnemyEntityId | 必须 | 定位 Enemy |
| SimulationEpoch | 必须 | 防止旧 Owner / 旧 lifetime 写入 |
| Sequence | 必须 | 丢弃重复和乱序 Snapshot |
| SampleNetworkTime 或 SimulationTick | 必须 | Observer 插值与 attack timing |
| Position | 必须 | DefaultMovement / AILerp 都会改变 Transform |
| Facing | 必须 | EnemyAnimator 依赖方向；不能只靠位置差恢复停住时朝向 |
| Velocity | 建议 | Rigidbody Movement、短期外推、动画速度 |
| PresentationState | 必须 | Moving / Warning / Attacking / Recovery / Knockback / Dead 等表现 |
| StateSequence | 必须 | 识别同一状态的再次进入，例如连续两次 Attack |
| StateStartNetworkTime | 必须 | Observer 不以消息到达时刻作为 Attack 起点 |
| AggroTargetPlayerId | 可选 | assignment 已有；仅用于诊断或动态切换冗余校验 |
| Discontinuity / Teleport | 建议 | Spawn、rubberband、handoff、强制位移时禁止普通插值 |
| AttackVariantId | 条件需要 | 仅当一个 Enemy 在同一 state 中可能选择多个攻击 |

不应放入常规 Simulation Snapshot：

- HP、MaxHP、Alive：来自 CombatLedger CanonicalEntityState。
- ConfirmedKill：来自 Server CombatGateway。
- Status Registry：来自 ServerStatusRegistry。
- Build、Crit、Projectile：由攻击者本地 GAS 处理。
- 完整 A* Path、StateMachine 对象、Coroutine 进度：Observer 不需要。

### 4.2 Replicated AIState 的边界

推荐复制“表现状态”，而不是完整 AI 内部状态。

第一版 PresentationState 可直接映射当前 EnemyController 的可见阶段：

~~~text
Moving
Warning
Attacking
Recovery
Knockback
Dead
Deactivating
Deactivated
~~~

InstantDead 可以作为 Dead 的一个 presentation flag；Stuck、PathPending、Rubberband tracking、A* cursor 不复制。

Canonical Dead 的优先级永远高于 Snapshot PresentationState。Snapshot 中的 Dead 只能帮助即时表现，不能产生 ConfirmedKill、奖励或 Network Destroy。

### 4.3 推荐传输策略

- Assignment、Attack state 边沿、Teleport/Handoff 使用可靠有序消息。
- Transform Snapshot 初始建议 15～20 Hz、不可靠传输，再根据 100+ Enemy 压测调整。
- Observer 在约 2 个 Snapshot 的小缓冲区内插值；短暂缺包可限时外推，超时后冻结而不是无限漂移。
- Server 只接受当前 SimulationEpoch、当前 owner connection、递增 Sequence 的 Snapshot。
- Server 缓存每个 Enemy 最新已接受 Snapshot，供 Late Join 和未来 Handoff 使用。
- Snapshot 批量发送，不能每个 Enemy 每帧一个 Command/Rpc。

---

## 5. Consequence Analysis

### 5.1 Enemy Transform authority 冲突

NetworkEnemy 当前的 NetworkTransformReliable 是 ServerToClient。若再由 Observer Snapshot 插值和 SimulationOwner 本地 AI 同时写 Transform，会产生抖动和回拉。

普通/Elite Enemy 必须只有一个 Transform writer：

- SimulationOwner：AI / Rigidbody 写 Transform。
- Observer：SnapshotInterpolator 写 Transform。
- Server：只缓存 Snapshot，不在每帧运行 Movement。
- Boss：继续由 Server 写 Transform。

### 5.2 Client → Server Snapshot 上报

Enemy 仍是 Server-owned NetworkIdentity，所以 Client 不能依赖 Enemy 上的 authority Command。推荐由每个 Player 已拥有的网络桥批量提交其负责 Enemy 的 Snapshot；Server 根据 sender 与 Assignment Registry 校验。

### 5.3 Snapshot frequency / interpolation

DefaultMovement 直接使用 Rigidbody 速度，AILerp 依赖 PathInterpolator。Observer 不需要运行这两套算法，只需要 Position、Facing、可选 Velocity。

初始 15～20 Hz 是工程起点，不是最终值。必须用 100+ Enemy、2～4 Client、200 ms Ping/Jitter/Loss 测量带宽与误差后调整。

### 5.4 Observer AI 必须停止

如果 Observer 仍注册进 EnemyAIManager，它会继续：

- UpdateDestination。
- RunUpdate 推进攻击。
- RunFixedUpdate 写 Rigidbody。
- Pathfinding SearchPath。
- Stuck / Rubberband。

这会形成永久分叉。需要按 Enemy role gate，而不是仅靠禁用 NetworkEnemyServerDriver。

### 5.5 AttackStart Event timing

当前 Attack progression 使用 Time.time 和 Clip.Length。Observer 收到 AttackStart 时已经包含网络延迟，如果从收到消息的本地 Time.time 重新开始，Damage Area 会整体滞后。

必须同步 StateStartNetworkTime 和 StateSequence。Observer 根据 NetworkTime 计算 normalized phase；单独一个无时间戳的 AttackStart Event 不足够。

### 5.6 本地 DamageArea 与网络延迟

Observer 的 DamageArea 会比 SimulationOwner 晚创建。合作 PvE 可接受小偏差，但必须定义：

- 是否从当前 normalized attack time 快进动画。
- 若消息到达时攻击窗口已经结束，是否跳过伤害窗口。
- 是否允许短暂延长本地防御宽限。

当前 PlayerDamageInteraction 还会伤害全局 Player，必须在 Attack 联网前改为碰撞对象对应的本地 Owner Player。

### 5.7 Enemy HP 与 local predicted state

Simulation Snapshot 不携带 HP。攻击者仍可本地预测 Enemy HP 和 PredictedLethalHit；Server CombatLedger 维护 Canonical HP。

SimulationOwner 不一定是攻击者，因此不能把 SimulationOwner 的 EnemyStats.Health 当作 Canonical HP。

### 5.8 PredictedDeath 与 Snapshot 竞争

Client B 预测 Enemy 死亡后会隐藏、停止 Targeting 或播放死亡表现；SimulationOwner A 可能继续发送 Alive/Moving Snapshot。

推荐优先级：

~~~text
Server Canonical Dead
    > 本地 PredictedDead presentation suppression
    > Enemy Simulation Snapshot
~~~

PredictedDead 期间仍可缓存 Snapshot，但不应把本地尸体拉回 Moving。若 Server 后续仍判定 Alive，使用 Loose Reconciliation 恢复后续状态，不回滚已经触发的 Build Chain。

### 5.9 Status 对 Movement / AIState 的影响

新 GAS Status 的 executor 可能是 SourceClient、Server 或 TargetOwnerClient；Enemy Simulation 则由 SimulationOwner 执行 Movement。

Slow、Freeze、Stun 等会跨越两个 Authority：

- Status executor 决定 gameplay mutation。
- SimulationOwner 必须获知并应用对 AI/Movement 的结果。

当前没有该桥。第一阶段不得迁移或重新定义 Status；详见 REQUIRES USER CONFIRMATION。

### 5.10 Knockback 对 SimulationOwner Transform 的覆盖

旧 Knockback Coroutine 直接写 Rigidbody velocity。若攻击者 Client B 本地执行、Owner A 同时发送正常 Chase Snapshot，Observer 会在两个结果间跳变。

需要后续建立有 EventId / Epoch 的 movement effect 命令与明确优先级。本阶段不改。

### 5.11 多 Client 同时影响 Enemy Transform

两个玩家同时 Knockback / Pull 时，不能让两个 Client 都成为最终 Transform writer。按目标模型，SimulationOwner 必须接收、排序并产出最终 Snapshot；其他 Client 的本地位移只能是短期表现预测。

### 5.12 Owner Disconnect

Server 当前没有 Enemy snapshot cache 或 owner assignment。断线后 NetworkEnemy 不会自动销毁，但会停在最后 Server Transform；产品 Enemy 的 Client-only AI 则无人继续。

未来至少需要：

- Server 缓存 latest accepted snapshot。
- 递增 SimulationEpoch。
- 从存活 Player 中选择新 AggroTarget / Owner。
- 把 final snapshot + assignment 可靠发送给 New Owner。
- 拒绝 Old Owner 的迟到 Snapshot。

### 5.13 Late Join / Reconnect

现有 Canonical Snapshot 只有 HP / Status。Late Join 还需要一次可靠 Enemy world snapshot：

- 当前活跃 Enemy identity。
- Assignment / Epoch。
- latest Transform snapshot。
- current presentation state 与 state start time。

Late Join 不应等待下一次随机 Transform 包才显示正确 Enemy。

### 5.14 Boss 与普通 Enemy 两套路径

普通 Enemy 已有 BaseEnemyController.isElite；Boss 使用独立 BossController 类型和多套 Behavior Graph / Attack Controller。

因此分类可行，但目前没有统一的 Network Enemy SimulationMode 字段。推荐在网络 authoring 中显式声明 Normal / EliteClientSim / BossServerSim，而不是依赖 Prefab 名称。

### 5.15 Host 特殊情况

Host 同时是 Server 和一个 Client。若代码使用 NetworkServer.active 判断是否运行 Enemy，会导致 Host 上 Server 路径和 Owner Client 路径同时 Tick。

运行判断必须基于明确 role：

~~~text
IsSimulationOwner
IsObserver
IsBossServerSimulator
~~~

不能把 isServer、isClient、isOwned 中任何一个单独当作 Enemy simulation 条件。

### 5.16 Dedicated Server

Dedicated Server 没有本地 Client，因此普通/Elite Enemy 必须等到有可用 Player connection 后再 Spawn/Assign。若所有 Player 断线，目标“SimulationOwner 不为空”与“Enemy 继续存在”无法同时满足，需要明确冻结、Despawn 或临时 Server fallback 策略。

Boss 不受影响，继续 Server Simulation。

### 5.17 Message ordering / duplicate / stale

每个 Snapshot 至少用 EnemyEntityId + SimulationEpoch + Sequence 判定：

- Epoch 小于当前 assignment：拒绝。
- Sender 不是当前 owner：拒绝。
- Sequence 不递增：丢弃。
- Unknown / CanonicalDead Enemy：丢弃。
- 数值 NaN / Infinity 或明显越界：拒绝并记录诊断。

Transform Snapshot 可以丢包；Assignment、Attack state edge、Despawn/Handoff 不能只依赖不可靠流。

### 5.18 Scene unload / Enemy despawn

Server 必须先注销 assignment 和 snapshot cache，再 NetworkServer.Destroy。Observer 插值队列需要在 OnStopClient / scene unload 清空，避免旧 Snapshot 写到已销毁或复用对象。

### 5.19 Enemy pooling

NetworkIdentity 的动态 Network Spawn/Destroy 与 HellMaiden 本地 Pool Return 不是同一生命周期。不能直接把已 Network Spawn 的对象仅 SetActive(false) 后本地复用。

第一版建议先禁用网络 Enemy pooling，以正确性为先；之后再采用 Server 统一控制的 network pooling，并以 SimulationEpoch / SpawnGeneration 隔离旧包。

### 5.20 当前测试假设

[NetworkCombatPhase7Tests.cs](../_Project/Tests/EditMode/NetworkCombat/NetworkCombatPhase7Tests.cs) 明确断言：

- Player NetworkTransform 是 ClientToServer。
- Enemy NetworkTransform 是 ServerToClient。
- Enemy 带 NetworkEnemyServerDriver。
- Projectile 不含 NetworkBehaviour。

引入目标模型后，Enemy Transform 和 Driver 相关断言必须更新。CombatGateway、Status、PredictedLethalHit、ConfirmedKill、Projectile local-only 的测试应保持不变。

---

## 6. Ambiguities

### 6.1 EnemyBase 的唯一 HP

**REQUIRES USER CONFIRMATION**

**Question:**
产品 Enemy 接入后，EnemyStats.Health 与 CombatantBehaviour.CurrentHealth 哪一个是唯一 gameplay HP？

**Why it matters:**
当前 EnemyController.Damage / Kill 看 EnemyStats，网络 CombatLedger 和新 GAS 看 CombatantBehaviour。两份 HP 会造成一台机器死亡、另一台仍存活，或者重复触发 OnKill。

**Possible options:**

A. CombatantBehaviour / CombatLedger 为唯一 HP；EnemyStats.Health 只作为旧表现读取或移除写入。
B. EnemyStats 为本地 HP，再双向同步 CombatantBehaviour。
C. 保留两套并以事件桥接。

**Recommended default:**
A。网络游戏中 Enemy Canonical HP 只能来自 CombatLedger；旧 EnemyController 应逐步适配到 CombatantBehaviour，不能双向同步。

### 6.2 AggroTarget 强绑定与非目标 Player 受击

**REQUIRES USER CONFIRMATION**

**Question:**
Enemy 的 AggroTarget=A、SimulationOwner=A 时，攻击区域碰到 B，是否允许 B 本地判定受击但不改变 AggroTarget / SimulationOwner？

**Why it matters:**
需求示例写了 Enemy Owner=A、正在攻击 B；这可能表示 AoE/冲撞碰到 B，也可能表示临时攻击目标已经变成 B。两种语义对 Handoff 完全不同。

**Possible options:**

A. 攻击意图仍指向 A；B 只是进入 DamageArea，可本地受击。
B. 只要攻击 B，Server 必须先把 AggroTarget/Owner 切到 B。
C. AggroTarget 与 SimulationOwner 不再严格绑定。

**Recommended default:**
A。保持强绑定，同时允许范围攻击伤害非 AggroTarget Player。

### 6.3 Attack timing 的真实来源

**REQUIRES USER CONFIRMATION**

**Question:**
联网版本是继续以 EnemyAttack 的 Time.time + Clip.Length 为 gameplay 时间轴，还是把命中窗口迁移为明确 Animation Event？

**Why it matters:**
当前代码并不是由 Animation Event 完整驱动。只同步 AttackStart 后，各 Client 的 Clip、暂停、帧率和到包延迟会改变碰撞窗口。

**Possible options:**

A. 网络时间驱动状态阶段，Animation Event 只打开本地命中窗口。
B. 全部继续用本地 Time.time，允许较大偏差。
C. 全部由动画事件推进。

**Recommended default:**
A。同步 state start network time；保留动画事件用于本地 Hitbox 表现，但不让动画播放结束决定网络状态。

### 6.4 Status 对 AI / Movement 的约束

**REQUIRES USER CONFIRMATION**

**Question:**
Slow、Freeze、Stun 等 Status 的 executor 与 Enemy SimulationOwner 不同时，谁把最终 movement restriction 交给 SimulationOwner？

**Why it matters:**
Status Tick ownership 明确不能改变，但只有 SimulationOwner 可以持续写 Enemy Transform。直接让 SourceClient 修改 Enemy Rigidbody 会被下一帧 Snapshot 覆盖。

**Possible options:**

A. Status executor 提交 canonical mutation，SimulationOwner 查询 replica 并应用移动约束。
B. Server 把 movement-control directive 可靠发给 SimulationOwner。
C. Status executor 直接发送 Transform effect。

**Recommended default:**
Slow 采用 A；Stun/强控制采用 B。第一阶段不实现，只预留接口。

### 6.5 Knockback / Pull / Physics 的收敛规则

**REQUIRES USER CONFIRMATION**

**Question:**
攻击者 Client 本地 Knockback 与 SimulationOwner Snapshot 冲突时，哪个结果最终覆盖 Transform？

**Why it matters:**
当前需求希望各 Client 重放 Knockback，同时要求 SimulationOwner 负责排序/收敛；若没有明确优先级，会持续抖动或重复位移。

**Possible options:**

A. 只有 SimulationOwner 执行最终位移；攻击者只做短期视觉预测。
B. 所有 Client 重放，但 SimulationOwner 接收全部 effect、排序并用后续 Snapshot 最终收敛。
C. Server 执行最终 Knockback。

**Recommended default:**
B，符合宽松客户端权威体验；但 effect 必须带 EventId、Epoch、顺序和可去重身份。本阶段不实现。

### 6.6 Owner Disconnect 的临时策略

**REQUIRES USER CONFIRMATION**

**Question:**
完整 Handoff 尚未实现的第一阶段，SimulationOwner 断线后 Enemy 应冻结、Despawn，还是临时回退 Server Simulation？

**Why it matters:**
“Owner 不允许为空”和“本阶段不实现完整 Handoff”在断线窗口内无法同时满足。

**Possible options:**

A. 冻结并保留 latest snapshot，开发环境记录错误。
B. Server 临时接管 AI。
C. Despawn 该 Owner 关联 Enemy。

**Recommended default:**
A，最不容易把临时行为固化成另一套 Server AI；正式场景上线前必须完成 reassignment。

### 6.7 Network Enemy Pooling

**REQUIRES USER CONFIRMATION**

**Question:**
第一版是否允许暂时不对 Network Enemy 使用 HellMaiden EnemyPool？

**Why it matters:**
Network Spawn/Destroy、netId、Observer 列表与本地 SetActive Pool 生命周期不同；过早合并会增加 stale snapshot 风险。

**Possible options:**

A. 第一版 Instantiate / NetworkServer.Spawn / NetworkServer.Destroy。
B. 第一版同时实现 Server-controlled network pool。
C. 复用现有本地 Pool。

**Recommended default:**
A。先验证 Authority，再单独实现网络 Pool。

### 6.8 一个 Client 未来拥有两个本地 Player

**REQUIRES USER CONFIRMATION**

**Question:**
未来本地双人是“一条 NetworkConnection 对应两个 Network Player entity”，还是两个独立 Client 进程？

**Why it matters:**
SimulationOwner 以 PlayerId 表示，但网络鉴权以 connection 表示。一个 connection 多 Player 时，Owner 消息必须额外验证该 connection 拥有对应 PlayerId。

**Possible options:**

A. 当前只支持一 connection 一 Player，接口预留一对多。
B. 第一阶段直接支持一 connection 多 Player。
C. 本地双人各用独立 Client。

**Recommended default:**
A。Assignment 保存 PlayerId，Server Registry 保存 connection → PlayerIds，避免未来破坏协议。

### 6.9 没有可用 Player 时是否允许 Spawn

**REQUIRES USER CONFIRMATION**

**Question:**
Dedicated Server 启动后尚无 ready Player，是否允许生成普通/Elite Enemy？

**Why it matters:**
需求规定 SimulationOwner 不允许为空。

**Possible options:**

A. 没有 eligible Player 时不生成普通/Elite Enemy。
B. 允许生成但冻结，等待 assignment。
C. 临时 Server Simulation。

**Recommended default:**
A。Boss 可按 Server 规则单独生成。

---

## 7. Risk Classification

### Blocking

- EnemyBase 的唯一 HP 尚未确定；EnemyStats 与 CombatantBehaviour 当前会形成双状态。
- EnemyBase 还不是 Network Prefab，完整产品 Enemy 与当前 NetworkEnemy 沙箱尚未结合。
- Server 缺少稳定的 PlayerId → NetworkConnection → Player Transform Registry。
- EnemyAIManager 当前会 Tick 全部注册 Enemy，没有 Simulation role gate。
- 在迁移 Enemy Attack 前，PlayerDamageInteraction 的全局 Player 引用必须消除。

### High Risk

- NetworkTransformReliable 与 SnapshotInterpolator 双写 Transform。
- AttackStart 到包延迟导致 Hitbox timing 漂移。
- PredictedDead 被后续 Alive Snapshot 拉回。
- 旧 EnemyStatus 的 Slow / DOT 与新 Status Registry 双轨运行。
- Knockback / Pull 与 Owner Snapshot 相互覆盖。
- Network pooling 接收上一 lifetime 的 stale Snapshot。
- Host 同时进入 Server 与 Owner simulation 分支。

### Medium Risk

- 15～20 Hz Snapshot 在高速 Dash Enemy 上误差过大。
- AILerp path switch 与 Observer 线性插值产生可见切角。
- Late Join 在 assignment 和 snapshot 到达顺序不同的短窗口显示错误。
- Scene unload 后队列未清理。
- Elite 通过 isElite bool 分类，但网络 authoring 可能漏配。

### Low Risk

- Facing 量化精度。
- Idle/Moving 动画轻微不同步。
- 非关键 VFX 在丢包后的短暂差异。
- Snapshot debug 字段增加少量开发期带宽。

### Future / Handoff-only

- 动态 Aggro 切换。
- Old Owner final snapshot → Server → New Owner。
- Owner disconnect 自动重新分配。
- Reconnect 恢复原 Owner。
- Server-controlled Network Enemy Pool。

---

## 8. Recommended First Implementation Goal

### 8.1 Phase 0：产品 Enemy 联网前置基线

目的不是实现 Authority，而是消除无法安全接入的双状态：

1. 明确 CombatantBehaviour / CombatLedger 为 Enemy 唯一 HP。
2. 为 EnemyBase 建立不依赖 GameDirector.Instance.Player 的目标注入入口。
3. 保留 EnemyController、EnemyDefaultMovement、EnemyAILerpMovement 的现有 gameplay 行为。
4. 暂不迁移 Attack、Status、Knockback。
5. 为 Normal / Elite / Boss 增加显式 SimulationMode authoring；Boss 默认 Server。

Phase 0 如果不完成，建议 Phase 1 只在 NetworkEnemy 轻量沙箱上验证 Snapshot，不要宣称 EnemyBase 已完成联网。

### 8.2 Phase 1：SimulationOwner + Movement Snapshot vertical slice

最小范围：

~~~text
只支持普通 Enemy / Elite Enemy
只迁移 Target-following + Movement
Server Spawn 时一次性确定 AggroTarget 与 SimulationOwner
不做动态 Aggro switch
不做完整 Handoff
不做 Enemy Attack 联网
不改 Combat / Player HP / Status / Knockback
不改 Boss
~~~

建议新增 plain-data 类型：

- EnemySimulationMode
- EnemyPresentationState
- EnemySimulationAssignment
- EnemySimulationSnapshot
- EnemySimulationRole

建议新增运行组件：

- ServerEnemySimulationRegistry：Server 保存 assignment、epoch、latest snapshot。
- NetworkEnemySimulationBridge：通过当前 Player/World 网络桥批量提交与转发 Snapshot。
- EnemySimulationGate：决定本机是 Owner simulator、Observer 还是 Boss server simulator。
- EnemySnapshotInterpolator：Observer Transform 插值、超时冻结、Teleport 处理。
- EnemyTargetResolver：按 Server 分配的 Player entity ID 解析本地 Transform。

建议优先放入现有边界：

- Transport-neutral contracts 放在 Assets/_Project/NetworkCombat/Contracts。
- Mirror registry / bridge 放在 Assets/_Project/NetworkCombat/Mirror。
- Enemy role gate / interpolation 放在 Gameplay 层，并保持不直接依赖 Mirror；Mirror adapter 只向它注入 assignment/snapshot。

第一阶段可能修改：

- NetworkEnemyServerDriver：停止普通/Elite 的持续 Server Chase，保留 canonical lifetime。
- NetworkEnemySandboxSpawner：Spawn 时选择 Player 并建立 assignment。
- NetworkEnemy.prefab：移除普通 Enemy 的 ServerToClient NetworkTransform writer，增加 simulation bridge/gate。
- EnemyAIManager / EnemyController：仅在进入 EnemyBase vertical slice 时增加 role gate，不顺手重构状态机。
- NetworkCombatPhase7Tests：替换“Enemy 必须使用 ServerToClient NetworkTransform”的旧断言。

### 8.3 明确 Non-goals

- 不让 Client 获得 Enemy NetworkIdentity Authority。
- 不修改 CombatResult、CombatLedger 伤害公式或 validation。
- 不修改 PlayerHealthReport。
- 不把 Status Tick 交给 SimulationOwner。
- 不实现 Knockback / Pull / Physics 收敛。
- 不实现完整 AttackStart / DamageArea 联网。
- 不实现动态 Aggro / Authority Handoff。
- 不实现 Boss Client Simulation。
- 不把每个 Projectile Network Spawn。
- 不恢复 Assets/Script 下旧 Server-authoritative Combat。

### 8.4 Tests Required

EditMode：

1. Assignment 必须包含非零 Enemy、Owner、Target、Epoch。
2. Server 只接受当前 Owner connection 的 Snapshot。
3. 旧 Epoch、重复 Sequence、乱序 Sequence 被丢弃。
4. NaN / Infinity Position、未知 Enemy、CanonicalDead Enemy 被拒绝。
5. Snapshot batch 编解码保持字段一致。
6. Interpolator 在两个 Snapshot 间正确插值。
7. Discontinuity 立即 Teleport 并清空旧 buffer。
8. Canonical Dead 优先于 Alive/Moving Snapshot。
9. Boss 不进入 Client Simulation role。
10. GAS Core assembly 仍不引用 Mirror。

PlayMode：

1. Host + Remote Client：Server 为 Enemy 分配 AggroTarget=A、SimulationOwner=A。
2. A 只在本机推进 Enemy；B 不运行完整 AI。
3. B 通过 Snapshot 看到 Enemy 平滑移动。
4. Host 是 Owner 时 Enemy 每帧只 Tick 一次。
5. B 仍可本地攻击该 Enemy，CombatResult 经 Ledger 改变 Canonical HP。
6. A/B 同时攻击时 ConfirmedKill 仍只产生一次。
7. Snapshot 丢包/乱序时 Observer 不倒退。
8. Enemy Canonical Dead 后停止接收 Snapshot并由 Server Despawn。
9. Scene unload 后 assignment、cache、interpolation buffer 清空。
10. 100+ Enemy 的 Snapshot 使用 batch，Command/Rpc 数量不按 Enemy×Frame 增长。

### 8.5 Phase 1 Acceptance Criteria

- 普通/Elite Enemy 的 NetworkIdentity 仍为 Server Authority。
- 每个已生成普通/Elite Enemy 都有非空 AggroTarget 与 SimulationOwner。
- 只有 SimulationOwner Client 运行 Enemy target-following 和 Movement。
- Observer 不运行 Enemy AI / Pathfinding / Rigidbody simulation，只插值 Snapshot。
- Server 校验并缓存 latest snapshot，再转发给 Observer。
- 旧 Owner、重复和乱序 Snapshot 不会覆盖新状态。
- Enemy 不再同时被 NetworkTransformReliable 和 SnapshotInterpolator 写 Transform。
- Client B 攻击 Owner=A 的 Enemy 不需要 A 确认命中。
- Enemy HP / ConfirmedKill 继续由 CombatLedger 决定。
- Player HP 继续由 Owner Client + PlayerHealthReport 决定。
- Status、Knockback、Pull、Physics 和 Boss 行为没有 Authority 变化。
- Unity 编译通过，现有 GAS / NetworkCombat 测试继续通过，新测试全部通过。

---

## 9. 建议的实施顺序

~~~text
用户确认 Blocking / Authority 语义
    ↓
Phase 0：EnemyBase 单一 HP + Target 注入基线
    ↓
Phase 1A：NetworkEnemy 沙箱验证 Assignment / Snapshot / Observer interpolation
    ↓
Phase 1B：EnemyBase 的 AIManager / Movement 接入 SimulationGate
    ↓
Phase 2：Attack state / timing / 本地 Player hit
    ↓
单独设计 Status movement restriction
    ↓
单独设计 Knockback / Pull convergence
    ↓
Handoff、Disconnect、Late Join、Network Pool
~~~

在完成 Phase 1 前，不应删除当前 Server Chase 路径；应把它保留为可回退的开发实现，但不能与 SimulationOwner 路径同时启用。每完成一层，都需要用 Authority 日志明确显示 EnemyEntityId、OwnerPlayerId、TargetPlayerId、Epoch、Role 与 last accepted Sequence，方便定位 Host 双 Tick、旧包和错误 assignment。
