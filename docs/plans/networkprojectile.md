# Dante 武器弹道联机表现同步

  ## Summary

  目标仓库为 F:\UnityStore\MonsterSupergroup。当前 NetworkPlayer 只装备 ID=2 的 _Dante_SlowProjectile，Owner 端通过
  PlayerBuildRuntime → ProjectileAttackBehaviour → ProjectileAttack 生成弹体；远端 Player 不创建武器 Runtime，因此看不
  到弹道。

  本期复用现有 Enemy AttackPresentation 的“可靠阶段边 + 本地表现重放”模式，但扩展现有 NetworkWeaponCombatAdapter，不把
  Player 弹体塞入带有 Enemy Authority/Epoch 语义的 NetworkEnemySimulationWorld，也不为每颗弹体创建 NetworkIdentity。

  ## Implementation Changes

  ### 1. 建立与网络无关的投射物表现事件

  在 Gameplay Combat 中增加：

  - ProjectilePresentationKey
      - AttackEventId
      - ProjectileIndex

  - ProjectilePresentationStats
      - Damage/Speed/Size/Duration 的表现缩放参数
      - EffectiveSpeed
      - Duration
      - 当前与基础 ProjectileCount

  - ProjectilePresentationSpawn
      - Weapon ID、Key、实际世界起点、实际方向、Element、旋转方式和冻结后的表现参数

  - ProjectilePresentationTermination
      - Key、最终位置、Hit / Expired / Cancelled

  - PlayerBuildRuntime 对外提供 ProjectilePresentationSpawned 和 ProjectilePresentationTerminated 事件。

  ProjectileAttackBehaviour 在本地弹体成功初始化后发布 Spawn；每颗弹体在命中上限、过期或被回收时只发布一次
  Termination。所有参数直接取 Owner 已冻结的 AttackSnapshot 和实际生成结果，不让观察端重新计算方向、数量或 Modifier。

  ### 2. 增加绝对安全的 Presentation-only 弹体模式

  扩展 BasePlayerAttack、ProjectileAttack 和 ProjectileAttackBehaviour：

  - 增加 InitPresentation/PlayPresentation 路径，不创建或持有 AttackSnapshot。
  - HitBox 不注册伤害回调，并在 OnHit、ResolveDamage 等入口再次阻止 Presentation-only 实例进入 GAS。
  - 不执行 OnHit、Status、Knockback、CombatResult 或 HitEffectResolver 中可能包含的 Gameplay。
  - 仍复用原 Dante Projectile Prefab、Element Variant、ProgressionScaler、动画、Particle 和安全音效。
  - Owner 与 Replica 共用 Pool 时，每次取出必须重置 Gameplay/Presentation 模式，防止 Replica 状态污染下一颗 Owner 弹
    体。

  - Termination 在远端播放安全的命中/过期/取消结束表现，但不运行 Gameplay Hit Effect。
  - Dante 当前没有自定义 PM_Base，远端按收到的实际起点、方向和 EffectiveSpeed 做直线重放，并用网络时间补偿初始位移。

  ### 3. 扩展现有 Mirror Player 适配器

  新增 NetworkProjectilePresentationEdge 和 Batch，字段包括：

  - Source Player/Weapon ID
  - AttackEventId + ProjectileIndex
  - Spawn / Hit / Expired / Cancelled
  - Owner 事件网络时间
  - 实际 Position/Direction
  - Element、EffectiveSpeed、Duration、旋转与表现缩放参数

  扩展 NetworkWeaponCombatAdapter：

  1. Owner 订阅 PlayerBuildRuntime 的表现事件。
  2. 每帧将事件按最多 32 条分批，通过 Reliable Command 提交。
  3. Server 验证连接所有者、SourcePlayerId、Batch Sequence、合法 Phase、非零 ID 和有限数值。
  4. Server 不计算武器、弹道或伤害，直接通过 Reliable ClientRpc 转发。
  5. Owner/Host 跳过自己的 RPC，避免本地弹体重复生成。
  6. Observer 使用 Batch Sequence 去重并丢弃旧包。
  7. 暴露只读诊断计数：发送、接收、播放、终止、拒绝数量。

  这条表现通道与 CombatSubmissionBatch 分离，避免视觉事件影响 Canonical Combat Ledger。

  ### 4. 远端 Replica 生命周期

  - 通过 NetworkPlayerBootstrap 暴露的共享 RuntimeDB 解析 Weapon ID。
  - 每个远端 NetworkPlayer/Weapon ID 缓存一个禁用自动攻击 Update 的 Presentation Weapon Proxy。
  - Proxy 使用同一 Dante WeaponPrefab 和三个 Element Variant，只负责生成表现弹体。
  - Spawn 到达后，根据 NetworkTime.time - SpawnNetworkTime 快进直线位移；超过 2 秒的明显陈旧 Spawn 不再重放。
  - Termination 根据 Key 定位远端弹体，修正到 Owner 最终位置后结束。
  - Player despawn、场景退出或 Adapter 销毁时回收全部 Replica 和缓存 Proxy。
  - 不缓存历史弹体给 Late Join Client。

  ## Test Plan

  - EditMode：
      - Contract 字段、Phase、有限数值、Source 校验及 Batch Sequence 去重。
      - NetworkPlayer 仍只有一个 NetworkWeaponCombatAdapter，无需新 NetworkBehaviour。
      - Dante ID=2、三个 Variant 和直线移动约束保持有效。

  - Gameplay PlayMode：
      - Dante Owner 攻击发布正确的起点、方向、Element、EventId 和冻结 Stats。
      - Modifier 在发射后移除，不改变已发布的表现参数。
      - Hit/Expired/Cancelled 只终止一次。
      - Replica 弹体回池后再次供 Owner 使用时，Owner 命中仍只结算一次。
      - 现有 DanteNativeGasRuntimeTests 全部继续通过。

  - 两进程 BootGameplay 验证：
      - Host 和 Client 都检测到非 Owner Player 的远端 Projectile Spawn。
      - Owner 端没有重复 Replica。
      - 两端原有伤害与 Canonical HP 收敛继续通过。
      - 扩展 BootGameplayProcessValidationBootstrap 的 PASS 条件并运行现有 Run-BootGameplayProcessValidation.ps1。

  ## Acceptance Criteria and Assumptions

  - 本期只覆盖当前正式装备的 _Dante_SlowProjectile，但事件和 Adapter 边界允许其他武器后续接入。
  - 同步 Spawn 与 Termination，不同步逐帧 Transform。
  - 使用 Reliable、逐帧批处理；不 NetworkSpawn 每颗弹体。
  - Owner 继续独立执行 Projectile Hit、New GAS、CombatResult 和伤害。
  - Observer 弹体始终是无 Gameplay 能力的 Ghost。
  - 不同步 Particle 随机种子的微观差异，但位置、方向、数量、速度、大小、Element 和结束时机来自 Owner。
  - 不处理追踪、曲线、回旋镖、复杂 HitEffect 和 Late Join 活跃弹体恢复。
  - 保留仓库当前与本任务无关的未提交修改。