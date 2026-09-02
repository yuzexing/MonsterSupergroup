# MonsterSupergroup 与 HellMaiden GAS 渐进合并方案

> [!WARNING]
> **2026-09-02 实施状态更新**：本文保留为方案决策记录。其中 `PlayerHand` / `PlayerHandSlot` / `WeaponDefinition` / `StraightProjectileBehaviour` 指向一套已删除的中间原型，不再是可实施入口。当前真实链路是 `PlayerBuildRuntime -> HellMaiden WeaponBehaviour -> WeaponRuntimeBehaviour -> New GAS`；请以 [GAS 与联机模块总览](MonsterSupergroup_GAS与联机模块总览.md) 和 [联机模块详细使用指南](MonsterSupergroup_联机模块详细使用指南.md) 为当前事实来源。

> 方案日期：2026-08-25
>
> 本文是后续代码实施路线，不表示这些迁移已经完成。
>
> 已确认决策：以当前 `MonsterSupergroup.GAS` 为唯一 Combat Core，采用渐进兼容层，不建立第三套平行 GAS。

## 1. 最终结论

不应把 HellMaiden 的 `GameDirector + PlayerHand + WeaponBehaviour + EnemyStatusResolver` 整套接到 Mirror，也不应删除当前 GAS 后重新围绕旧代码开发。

推荐方式：

```text
保留：
  MonsterSupergroup.GAS.Core
  Gameplay.Combat.Runtime
  NetworkCombat Contracts / Gateway / Ledger / Replica

从 HellMaiden 提取：
  PlayerStats 字段与公式
  Equipment / Perk 内容
  Weapon 攻击形态和几何
  Projectile / Melee / Area / Explosion / Summon 行为
  Burn / Poison / Bleed / Slow / Weaken 规则
  Dash 与特殊角色行为

接入当前管线：
  PlayerAttributeSet
  AttackSnapshot
  CombatPipeline
  StatusController / StatusInstance
  PredictedLethalHit
  ClientCombatCollector
  Server Canonical World
```

当前系统负责“正确边界、事件身份、网络收敛和可测试生命周期”，HellMaiden 负责提供“丰富玩法内容和原始数值语义”。

## 2. 为什么不能直接合并源码

### 2.1 HellMaiden 不是独立 GAS 插件

HellMaiden 的 GAS-like 系统是项目业务代码的组合，不是一个可单独复制的 package。它同时依赖：

- `GameDirector.Instance`
- `ControllerManager.Instance`
- `GameDataManager.Instance`
- `ProgressionManager.Instance`
- `RuntimeDB`
- `PoolManager.Instance`
- `BaseEnemyController`
- 旧 `IDamageable`
- FMOD、Animancer、UI、旧场景和角色控制器

即使所有文件能编译，以上全局前提也仍然假设“游戏里只有一个当前 Player”。这和每个 Client 拥有各自 Owner Player 的联网模型冲突。

### 2.2 当前项目已经有更适合联网的核心

当前 Core 已经解决了旧系统最难在联网后补救的问题：

- 稳定的 Event/Root/Parent Identity。
- AttackSnapshot 与命中分离。
- 每目标 DynamicOnDamage 局部计算。
- PredictedLethalHit 与 ConfirmedKill 分离。
- Build Chain Guard。
- Status Source、ExecutionAuthority、Version 和 Canonical/Predicted 分层。
- CombatResult Batch、幂等和轻量 Server Validation。
- Core 与 Mirror/插件隔离。

若退回旧 WeaponBehaviour 作为伤害核心，这些边界都要重新实现一次，并会形成两套互相竞争的 HP、Status 和 Kill 语义。

## 3. 两套系统的区别

| 维度 | 当前 MonsterSupergroup | 移植的 HellMaiden | 合并决策 |
| --- | --- | --- | --- |
| Core 形态 | 纯 C#、无 Unity 引用 | MonoBehaviour/业务单例深度耦合 | 保留当前 Core。 |
| Player 数量假设 | 每个 Owner 独立身份 | 全局唯一 `GameDirector.Player` | 改为每玩家 Runtime Context。 |
| PlayerState | 尚未形成完整产品状态 | 静态流程查询 facade | 不当作 GAS/网络 Player Data。 |
| PlayerStats | 当前主要覆盖武器 Stats | HP、移动、Dash、XP、卡牌、复活等大容器 | 按 Authority 拆分，不整体复制。 |
| Hand | 4 Slot、每槽 3 Equipment、干净生命周期 | 同样 4/3，但还拥有 Perk、Shrine、Card Pool 和全局事件 | 保留当前 Hand，外围补 Loadout/Run 服务。 |
| Weapon | 当前仅完整支持 Projectile 垂直切片 | 大量 Projectile/Melee/Area/Beam/Summon/Ultimate | 提取攻击形态，伤害仍走当前 Pipeline。 |
| Attack Stats | 分层重算并冻结 Snapshot | 武器对象持有可变 Stats，部分跨攻击共享 | 使用当前 Stats/Snapshot。 |
| DynamicOnDamage | 每 Target 局部 accumulator | 旧实现存在跨 Target 累积风险 | 使用当前实现。 |
| Modifier ID | 显式稳定 ID + 生成 Registry | 反编译数据/类型映射依赖旧项目结构 | 为每个迁移类型分配稳定 ID。 |
| OnHit | 当前 Stage + Trigger Guard | 丰富内容，但读取全局 Player | 迁移效果，改读 Context/AttributeSet。 |
| OnKill | 已解释为 Predicted Lethal 兼容 Stage | 同时承担 Build 和击杀语义 | Gameplay → Predicted；奖励 → Confirmed。 |
| Status | 每 Combatant Controller、Source/Version/Authority | EnemyStatusResolver 按状态维护 Handler/Enemy 字典 | 保留当前 Controller，迁移公式与 VFX。 |
| Status 网络 | Canonical Registry + 全 Client Replica | 原项目无多人 Canonical Registry | 使用当前网络实现。 |
| Projectile | 本地池化、携带 Snapshot | PoolManager、旧 Player/Enemy 引用、FMOD | 保留几何/表现，换掉伤害和全局依赖。 |
| Enemy HP | 本地预测 + Server Ledger Canonical | 直接调用 BaseEnemyController | 使用 Combatant Adapter/Ledger。 |
| Debug | Event Trace、Metrics、自动测试 | 主要依赖运行场景和日志 | 保留当前 Trace/Test。 |
| 内容广度 | 小而完整的联机切片 | 大量武器、状态、角色和 Perk | 分批迁移 HellMaiden 内容。 |

## 4. 当前已经出现的同名概念

目标项目中同时存在：

```text
MonsterSupergroup.Gameplay.Combat.PlayerHand
AstralShift.HellMaiden.Combat.Hand.PlayerHand

MonsterSupergroup.Gameplay.Combat.ProjectileAttackBehaviour
AstralShift.HellMaiden.Player.Attacks.ProjectileAttackBehaviour

MonsterSupergroup.GAS.DamageInfo
AstralShift.HellMaiden.Player.Attacks.DamageInfo
```

Namespace 能避免编译冲突，但不能避免开发者选错类型。迁移期间应遵守：

- 新代码禁止用模糊的双 Namespace `using` 后依赖 IDE 自动选择。
- Legacy 代码继续留在 `AstralShift.HellMaiden.*`。
- 新 Combat 代码只使用 `MonsterSupergroup.GAS` 和 `MonsterSupergroup.Gameplay.Combat`。
- 兼容 Adapter 必须在文件和类型名中带 `Legacy` 或 `HellMaiden`，并有明确移除条件。

## 5. 类级迁移表

| HellMaiden 类/模块 | 当前职责 | 目标职责 | 处理 | 原因 |
| --- | --- | --- | --- | --- |
| `PlayerState` | 查询 Busy、Leveling、Quest、特殊 Controller | 本地 Owner 的 UI/流程查询 | **拆分 + 临时 facade** | 它不是属性集，也不能代表 Server World。 |
| `PlayerStats` | HP、移动、Dash、XP、卡牌、复活和 Modifier | Combat Attribute、Run Progression、Reward State | **拆分迁移** | 字段 Authority 不同，不能作为一个同步对象。 |
| Legacy `PlayerHand` | Weapon、Equipment、Perk、Shrine、Card Pool | 当前 Hand 只管 Weapon；外部 Loadout 管 Perk/Run | **不保留为核心** | 当前 Hand 生命周期更清晰且已联网测试。 |
| Legacy `PlayerHandSlot` | 从 RuntimeWeaponData 实例化旧 WeaponBehaviour | 当前 Slot 装备 WeaponDefinition | **提取数据语义** | 不能继续从 `GameDirector.Player.AttacksParent` 获取 Owner。 |
| `WeaponBehaviour` | Stats、伤害、Crit、Modifier、Enemy 条件、FMOD | 当前 `WeaponRuntimeBehaviour` 继续负责数值；攻击组件负责几何 | **拆分** | 旧类职责过多且重复 CombatPipeline。 |
| Legacy `WeaponBehaviourStats` | 多层武器数值 | 当前 `WeaponBehaviourStats` | **逐公式迁移** | 当前版本支持 Snapshot/每 Target 计算。 |
| Legacy `ProjectileAttackBehaviour` | 定时发射和 Pool Variant | 当前 Projectile Attack 形态 | **改写接入** | 保留发射语义，不保留旧 Damage/全局 Player。 |
| `ProjectileAttack` / Movement | 飞行、Homing、Boomerang、碰撞、FMOD | 本地 Projectile Strategy/表现 | **分形态迁移** | 不 Network Spawn，只在命中输出当前 Combat Event。 |
| `MeleeAttackBehaviour` | 池化 Hitbox | 通用 Weapon Attack 生命周期实现 | **改写接入** | Hitbox 命中必须走 `ResolveHitDetailed`。 |
| Area/Beam/Persistent | 持续命中和范围行为 | 本地 Attack Strategy + 每 Hit Event | **改写接入** | 事件不能无语义地合并。 |
| Summon/Clone | 生成攻击单位 | 预测 Gameplay 对象 + 必要的共享结果 | **按是否共享拆分** | 表现/攻击可本地；共享 AI 实体需 Server 注册。 |
| Ultimate/Dash Attack | Controller、无敌、移动、FMOD、攻击混合 | Player Ability State + Attack Strategy + Cue | **后置迁移** | Authority 和生命周期复杂，不适合作为首个切片。 |
| Legacy `RuntimeEquipmentModifiers` | 五阶段 Modifier 容器 | 当前 Runtime Container | **删除重复实现** | 当前版本已有排序、Handle、Dispose 和 Guard 测试。 |
| Static/Dynamic Modifier | 修改武器数值 | 当前同名 Stage | **逐个重写** | 公式可复用，基类和 Context 不复用。 |
| OnHit Modifier | 状态、吸血、连锁、生成物 | 当前 `OnHitModifier` | **逐个重写** | 移除 `GameDirector`/PoolManager 直接依赖。 |
| OnKill Modifier | 爆炸、召唤、变形、磁铁 | `OnPredictedLethalHitModifier` | **改语义迁移** | Build 不等待 Server，也不等于奖励。 |
| Player Perk Modifier | 移动、HP、Dash、XP 等 | `PlayerAttributeSet` 或 Run/Reward 服务 | **按字段迁移** | 不能全部塞进 Weapon Modifier。 |
| `EnemyStatusResolver` | 五个 Handler、Tick、Transfer、Consume | 当前 `StatusController` + Cue Adapter | **不保留 Runtime** | 当前实现已经支持 Canonical/Predicted 和单 Executor。 |
| `EnemyStatusData` | Power、Tick、Duration 等数据 | `StatusDefinition` + `StatusApplication`/Authoring | **数据转换** | 需要 Source、Instance、Authority、Version。 |
| `BaseEnemyController` | Enemy HP/状态/AI/死亡大类 | Combatant、Server AI、Presentation 分层 | **Adapter 后淘汰** | 共享事实必须进入 Ledger。 |
| 旧 `IDamageable` | 接收旧 Damage | `ICombatTarget` | **替换** | 统一当前 Pipeline 和 Status Receiver。 |
| `RuntimeDB` | 全局卡池、武器、Perk 数据访问 | 可注入 Content Catalog/Run Service | **Adapter 后淘汰** | 不能从每把 Weapon 访问全局 MonoBehaviour DB。 |
| `PoolManager` | 全局对象池 | 各攻击/表现的局部池服务 | **按用途适配** | Pool 不应决定 Gameplay Authority。 |
| FMOD/Animancer/VFX | 音画表现 | Cue/Presentation 层 | **保留表现，不进 Core** | Dedicated Server 和 Core Test 不应加载插件。 |

## 6. PlayerState 与 PlayerStats 的正确拆分

### 6.1 `PlayerState` 实际是什么

当前移植的 `PlayerState` 只有静态查询：

- 当前 Controller 是否 Busy。
- 是否正在升级。
- 是否在 Quest。
- 是否处于特殊 Ultimate Controller。

它通过 `ControllerManager.Instance`、`ProgressionManager.Instance` 等全局对象推断流程状态。因此：

- 它不是 GAS AttributeSet。
- 它不应该整体同步。
- 它不能被 Server EnemySpawner/Wave 当作“所有玩家是否 Busy”的依据。
- 每个客户端只有一个本地 Owner 时，可以暂时作为 UI 兼容 facade。

### 6.2 推荐目标类型

后续新增的最小职责：

```text
PlayerAttributeSet
  Owner-client gameplay attributes
  resolved MaxHP, move, dash, combat multipliers, local cooldown resources

CombatantBehaviour
  the single runtime CurrentHealth store
  applies resolved MaxHP changes and submits owner-final health

IPlayerFlowState
  Per-owner UI/input/ability-flow queries
  Busy, Leveling, InQuest, special local controller state

PlayerRunProgressionState
  Cards, rerolls, banishes, revive count and run selections

Server Reward/Run Ledger
  Confirmed Kill, base loot entitlement, EXP, Gold, Wave progress
```

`PlayerAttributeSet` 应是纯 C# 每玩家实例，通过现有 `CombatRuntimeServices` 注入 Weapon，不允许 Weapon 再调用 `GameDirector.Instance.Player`。

### 6.3 PlayerStats 字段 Authority

| 字段组 | 推荐 Authority | 同步方式 |
| --- | --- | --- |
| Current HP | `CombatantBehaviour` 作为唯一运行时存储；Owner-final，Server 保存 | 现有 `PlayerHealthReport`，未来补来源/治疗语义 Trace。 |
| MaxHP 基础值与倍率 | Owner Client 的 `PlayerAttributeSet` 计算 | 通过明确的 MaxHP 更新策略应用到 `CombatantBehaviour`，不能再保存第二份 Current HP。 |
| MoveSpeed | Owner Client | Transform 同步结果；其他端只需表现。 |
| Dash Distance/Speed/Cooldown/Charges | Owner Client | 必要时同步 Dash 表现状态，不同步每帧公式。 |
| Attack 全局倍率 | Owner Client | 进入 AttackSnapshot/CombatResult，不让 Server 重算。 |
| Pull Radius | Owner Client 交互表现 | 真正共享掉落拾取由 Server 最终结算。 |
| XP/Gold Modifier | Owner Client Build State | ConfirmedKill 后生成 Owner Reward Result，Server 幂等提交 Canonical Progression。 |
| Cards/Rerolls/Banishes | Run State | 选择请求和最终库存由 Server/Run Ledger 记录。 |
| Revive Count | Shared Run Fact | Server Canonical；Client 可预测复活动画。 |

未来奖励链推荐：

```text
Server ConfirmedKill
  -> create one Reward Entitlement keyed by CauseEventId
  -> Owner applies its local reward modifiers
  -> submit one idempotent PlayerRewardResult
  -> Server commits EXP/Gold/loot ownership
```

这不要求 Server 保存玩家完整 Combat Build，也不会让尸爆等即时 Gameplay 等待奖励链。

### 6.4 渐进兼容 facade

迁移期间可保留旧静态 `PlayerState`，但必须满足：

- 只解析本机 Owner Player。
- 只允许 UI、输入和本地流程代码调用。
- 添加弃用说明和使用点清单。
- Server、Enemy AI、Wave、Reward、Combat Core 禁止调用。
- 每迁走一个旧调用点就减少 facade 职责，最终删除。

当前约十个调用点集中在 Intercom、Loot、Transition、BossSpawner 和 UI。前四类必须按“本地 UI”或“Server Shared Flow”分别迁移，不能统一替换成另一个全局单例。

## 7. PlayerHand 与 Loadout 的合并

### 7.1 保留当前 PlayerHand

两套系统都采用四个 Slot、每槽最多三个 Equipment。当前实现还具备：

- Candidate 成功后才替换旧 Weapon。
- Activate/Deactivate/Shutdown 对称。
- Modifier Runtime 正确清理。
- 动态 Weapon 自动继承 CombatRuntimeServices。
- 100 次装备/卸载无累积状态的 PlayMode 测试。

因此当前 Hand 是目标实现。

### 7.2 不把所有 Run State 塞回 Hand

HellMaiden Hand 还管理 Perk、永久/临时 Shrine、Card Pool、签名武器和 UI 事件。目标中应拆为：

```text
PlayerHand
  Weapon Slot + Equipment lifecycle only

PlayerLoadoutRuntime
  Per-player Perk/Shrine collection
  applies/removes modifiers to PlayerAttributeSet and Hand slots

RunDraft/Reward Service
  card pool, choices, reroll, banish, server-confirmed grants

Presentation Adapter
  converts Hand/Loadout events to UI
```

`PlayerLoadoutRuntime` 是每玩家实例，不是 `PlayerHand.Instance`。

## 8. WeaponBehaviour 的推荐合并方式

### 8.1 保留什么

从旧 Weapon/Attack 中保留：

- 发射间隔和攻击方向规则。
- Projectile 数量、散射、Spawn Radius/Offset。
- Homing、Boomerang、Curved、Orbit 等运动轨迹。
- Melee、Beam、Persistent Area、Explosion 的 Hitbox 几何。
- HitCount、Pierce、Duration、Size、Knockback 语义。
- Animation、FMOD、VFX 的触发时机。

### 8.2 丢弃什么

- `GameDirector.Instance.Player` 查询。
- 旧 `WeaponBehaviour.CalculateDamage` 作为最终伤害入口。
- 对 `BaseEnemyController` 的直接类型判断。
- 旧全局 Pool/RuntimeDB 对 Gameplay Authority 的控制。
- 旧 OnKill 同时发 Build 和 Reward 的语义。
- 在 Projectile 命中时读取武器“当前”可变 Stats。

### 8.3 一个形态的标准接入

```text
Owner PlayerHand equips WeaponDefinition
  -> instantiate attack behaviour
  -> initialize WeaponRuntimeBehaviour from current GAS authoring
  -> attack behaviour chooses target/direction/geometry
  -> WeaponRuntimeBehaviour.BeginAttack creates AttackSnapshot
  -> local attack object carries that snapshot
  -> each valid hit calls ResolveHitDetailed(snapshot, ICombatTarget)
  -> GAS emits predicted gameplay events
  -> existing Collector submits shared outcomes
```

### 8.4 何时增加通用攻击抽象

当前 `WeaponDefinition` 和 `PlayerHandSlot` 直接引用 `ProjectileAttackBehaviour`，足够支持第一个 Projectile 代表性切片。

在迁移第二种非 Projectile 形态时，再引入最小的 `WeaponAttackBehaviourBase`，统一：

- `WeaponRuntimeBehaviour Weapon`
- `Configure(...)`
- `Activate()`
- `Deactivate()`
- 清理仍在运行的攻击对象

然后让当前 Projectile 实现和新的 Melee/Area 实现继承该基类，并把 Slot 字段从具体 Projectile 类型改为基类。不要在只有一种攻击形态时提前构建复杂 Ability Graph。

### 8.5 建议迁移顺序

1. 普通直线 Projectile。
2. 多弹/散射/Pierce。
3. Homing/Boomerang/Curved。
4. Melee Hitbox。
5. Explosion/Chain Lightning。
6. Persistent Area/Beam。
7. Summon/Clone。
8. Dash/Ultimate。

每一类先完成一把代表武器并通过双客户端测试，再批量转同类数据。

## 9. Modifier 的合并规则

### 9.1 阶段映射

| HellMaiden Stage | 当前 Stage | Authority |
| --- | --- | --- |
| Static Stat | `StaticStatModifier` | Owner Client |
| Dynamic Stat | `DynamicStatModifier` | Owner Client |
| Dynamic On Damage | `DynamicOnDamageModifier` | Owner Client，每 Target 局部 |
| OnHit | `OnHitModifier` | Owner Client |
| OnKill Gameplay | `OnPredictedLethalHitModifier` | Owner Client |
| Kill Credit/Reward | 不属于 Equipment Modifier | Server ConfirmedKill/Reward Service |
| Player HP/Move/Dash Perk | `PlayerAttributeSet` Modifier | Owner Client |
| XP/Gold/Run Perk | Player Loadout + Reward Result | Server 最终记录 |

### 9.2 稳定 ID

每个迁移类型必须：

1. 分配非 0、不可复用的显式 ID。
2. 保存“旧类型/旧 ID → 新稳定 ID”的迁移表。
3. 创建强类型 Parameters。
4. 标记正确的 Modifier Type Attribute。
5. 重建 Generated Registry。
6. 运行 Asset Validator。
7. 用序列化 Round-trip 测试确认资产引用稳定。

不得把旧类型的 namespace、文件路径或 `Type.GetHashCode()` 当作长期 ID。

### 9.3 Build Chain

会生成新攻击/伤害的 Modifier 必须创建 Child Context：

```text
Attack #100
  ProjectileHit #101
    CritExplosion #102
      Damage #103
        PredictedLethalHit #104
          CorpseExplosion #105
```

并显式选择：

- `AllowSelfTrigger`
- `OncePerRootEvent`
- `OncePerTargetPerRootEvent`
- `InternalCooldown`

默认 Guard 上限是 ChainDepth 32、每 Root 256 次触发。不要通过增大上限掩盖循环设计错误。

## 10. Status 的合并规则

### 10.1 保留当前 StatusController

旧 `EnemyStatusResolver` 为 Slow/Burn/Poison/Bleed/Weaken 分别维护 Handler 和 Enemy 字典，并在 MonoBehaviour Update 中使用 `Time.time` Tick。目标不保留该 Runtime。

迁移内容只包括：

- 状态 ID。
- Power/伤害公式。
- Stack/Refresh/HighestPriority 规则。
- Tick Interval/Duration。
- Consume/Transfer 行为。
- VFX/Cue 时机。

### 10.2 目标状态结构

每个 Gameplay Status 都必须拥有：

```text
InstanceId
DefinitionId
SourcePlayerId
SourceEntityId
TargetEntityId
Stack
StartTime
Duration
ExecutionAuthority
Version
Source CombatContext
```

Server Registry 按 Instance 保存 Canonical Add/Remove/Stack/Duration/Version；所有 Client 持有 Replica。

### 10.3 初始状态映射

| 状态 | HellMaiden 核心语义 | 目标 ExecutionAuthority |
| --- | --- | --- |
| Burn | DOT、最高优先级覆盖 | SourceClient |
| Poison | DOT、条件 Build、最高优先级覆盖 | SourceClient |
| Bleed | 可多层、DOT/消费 | SourceClient |
| Slow | 移动倍率 | Server 负责 AI 数值，Client 预测表现 |
| Weaken | Enemy 输出伤害倍率 | Server 负责共享 AI/伤害规则 |
| Fragile | 目标承伤条件 | 由设计选定唯一计算者；当前未迁内容 |
| Stun | 停止 Enemy AI | Server |

### 10.4 多来源状态是必需项

多人下必须同时区分：

```text
Poison(Source=A, Target=Enemy123)
Poison(Source=B, Target=Enemy123)
```

当前网络 Registry/StatusInstance 已能保存不同 Source；但在迁移 HellMaiden 的 HighestPriority 刷新规则前，还要补一个**最小的来源感知聚合规则**：刷新键至少包含 `DefinitionId + SourcePlayerId`，而跨来源的 Effective 值再按该状态规则聚合。

不能用“全局只有一个 Poison Instance”换取旧单机兼容，否则 Test 5、跨玩家 Build 和断线接管都会失真。

### 10.5 Cue 与 Gameplay 分离

```text
Status Added/Updated/Removed
  -> Presentation Adapter
  -> pooled VFX / UI icon / audio

Status Tick with execution authority
  -> gameplay damage/control
  -> Combat Event / Canonical result
```

FMOD、Particle、Animator 不能放进 `StatusController` 或 Server Registry。

## 11. 当前过渡债务

### 11.1 PlayerLoader 仍引用旧 ControllerManager

当前 `Gameplay.Local.PlayerLoader.Load()` 在完成 Hand 初始化后调用：

```text
ControllerManager.Instance?.OverrideGameController<PlayerController_HMD>()
```

这使 Local Adapter 仍依赖 HellMaiden 控制器生态。正式联机前应：

1. 保留 PlayerLoader 的“Reset → Initialize Hand → Equip → Activate”纯生命周期。
2. 把 Controller Override 移到 Owner-only Legacy Compatibility Component。
3. 产品输入系统准备好后删除该兼容组件。
4. 添加 Assembly Fence，确保 `Gameplay.Combat.Runtime` 和未来产品 Player Runtime 不引用旧 ControllerManager。

### 11.2 NetworkEnemyServerDriver 仍复用 LocalEnemyChase

该组件足够验证“AI 只在 Server”与 Canonical Death，但不是最终 AI 架构。正式场景应让产品 Enemy AI：

- 只在 Server Tick。
- 查询 Canonical/Effective Stun、Slow、Weaken。
- 不读取客户端预测 HP 决定 Network Destroy。
- 只在 Canonical Dead 后停止并销毁/回池。

### 11.3 Network Sandbox 由 Local Prefab 生成

当前生成器适合回归测试。正式 Player/Enemy Prefab 应成为产品资产，不继续由 Sandbox Builder 覆盖。

## 12. 渐进实施阶段

### Merge Phase A：锁边界和基线

目标：不改变 Gameplay 行为，先防止两套系统继续互相渗透。

- 将 `AstralShift.HellMaiden.*` 明确标记为 Legacy Source/Compatibility。
- 记录当前测试、代表武器数值和 Prefab 引用。
- 为新增代码增加 Namespace/asmdef 依赖规则。
- 禁止新代码引用旧 `GameDirector`、`PlayerHand.Instance` 和旧 DamageInfo。
- 把 PlayerLoader 的 Controller Override 移出纯加载生命周期。

验收：现有 88 EditMode、31 PlayMode 继续通过；Core/Runtime 无新增 Legacy/Mirror 引用。

### Merge Phase B：每玩家 Attribute 与 Loadout

- 新增每 Owner 的 `PlayerAttributeSet`。
- 将第一批字段迁入：MaxHP 公式、MoveSpeed 和影响代表武器的全局倍率；Current HP 继续只由 `CombatantBehaviour` 保存。
- 通过 `CombatRuntimeServices` 注入 Weapon。
- 新增 `PlayerLoadoutRuntime`，不使用静态 `PlayerHand.Instance`。
- PlayerState 只保留 UI 兼容 facade。

验收：Host 与 Remote Client 的属性实例互不污染；换装/重生/断线无 Modifier 泄漏。

### Merge Phase C：代表性 Projectile Weapon

- 选择一把 HellMaiden 普通 Projectile Weapon。
- 迁入发射、方向、速度、HitCount 和必要表现。
- 数值只走当前 WeaponRuntime/CombatPipeline。
- Projectile 保持本地池化并携带 AttackSnapshot。
- 迁入 Crit 和一个 Static/Dynamic Modifier。

验收：Owner 本地立即命中；Server 使用提交伤害收敛 HP，不重算 Weapon。

### Merge Phase D：Burn 与预测致死 Build

- 迁入 Burn 公式和 Cue。
- 完成来源感知 Status 聚合。
- 迁入一个旧 OnKill Explosion，改为 PredictedLethalHit。
- Server 只产生一次 ConfirmedKill。
- 增加 Reward Stub，只记录 ConfirmedKill，不发正式经济奖励。

验收：200ms RTT 下 Burn/Explosion/连锁不等待网络；A/B 同时预测致死都触发 Build，但只确认一次击杀。

### Merge Phase E：状态与 Modifier 批量迁移

- Poison、Bleed、Slow、Weaken、Fragile、Stun。
- Enemy/Player 条件 Modifier。
- Consume、Transfer、Detonate 语义分别建模。
- 所有类型使用稳定 ID 和迁移报告。

验收：A/B 来源实例可区分；B 可查询 A 的 Poison；DOT/AI Control 只执行一次。

### Merge Phase F：丰富攻击形态

- 引入最小 `WeaponAttackBehaviourBase`。
- 按 Projectile 变体 → Melee → Area/Beam → Summon → Dash/Ultimate 迁移。
- 表现层适配 FMOD/Animancer/VFX；Dedicated Server 不加载表现逻辑。

验收：每种形态至少一把武器具备 EditMode 公式测试、PlayMode Hit 测试和 Host/Remote Client 测试。

### Merge Phase G：正式 Run 与联机场景

- 用产品 Player/Enemy Prefab 替换 Sandbox 生成物。
- Server Wave/AI/Spawn/Destroy。
- ConfirmedKill → Reward Entitlement → Canonical EXP/Gold/Loot。
- 卡牌选择、复活、Wave 和场景切换。
- 多进程、Dedicated Server、IL2CPP、长时间压力测试。

验收：正式场景不包含 HUD/Sandbox Spawner；2～4 人完整跑完一局并最终收敛。

## 13. 第一个代表性切片

首个切片固定为：

```text
一个 Owner Player
  -> PlayerAttributeSet 的 Damage/Speed/Crit 子集
  -> 一把 HellMaiden 风格直线 Projectile Weapon
  -> 一个 Damage Modifier
  -> OnHit Burn
  -> 一个 PredictedLethal Explosion
  -> Server ConfirmedKill Reward Stub
  -> 两个 Network Enemy
```

明确不在首切片加入：

- Ultimate/Dash。
- Summon AI。
- 正式卡池/UI。
- 全部 HellMaiden Modifier。
- 正式 Loot/经济系统。
- 每个 Projectile 的网络同步。

这个范围足以证明 Player Attribute、Weapon、Status、Build、Network Gateway 和 Reward Boundary 能正确连接。

## 14. 必须通过的联网验收

### Test 1：单人命中

A 攻击 Enemy；A 本地立即 Hit、Damage、VFX，Server 最终 HP 一致。

### Test 2：双人同时伤害

A/B 同时攻击同一 Enemy；Server 按收到的独立 Event 顺序结算，最终 HP 正确收敛。

### Test 3：双预测致死

A/B 都触发 PredictedLethalHit，各自 Build 都运行；Server ConfirmedKill 只能一次。

### Test 4：尸爆连锁

PredictedLethalHit → Corpse Explosion → 三个 Enemy Lethal → 再 Explosion；攻击者 Client 不等待网络，且 Trigger Guard 阻止无限递归。

### Test 5：同状态不同来源

`Poison(Source=A)` 和 `Poison(Source=B)` 是两个可区分 Instance，Server 和所有 Client 都可查询。

### Test 6：跨玩家条件

B 的“目标存在 Poison → Lightning”能够读取 A 同步来的 Poison，并在 B 本地立即触发。

### Test 7：DOT 单执行者

Burn 在所有机器可见，但 Gameplay Tick 只能由 Source A 执行一次；A 断线后 Server 只接管剩余 Tick。

### Test 8：Stun

Client 立即预测动画/VFX，Server 最终停止 Enemy AI；Observer 不重复执行 Stop。

### Test 9：恶劣网络

200ms Ping、Jitter、Packet Loss 下允许短期不同，最终必须收敛 HP、Alive/Dead 和 Status Registry，不做整条 Build Rollback。

### Test 10：规模

2～4 Client、100+ Enemy、大量 Projectile、Burn、Poison、Explosion 和 Build Chain；检查 GC、CPU、Bandwidth、Batch 数和 Command 数。

## 15. 风险与阻塞项

| 风险 | 当前状态 | 处理 |
| --- | --- | --- |
| Legacy 巨型程序集依赖插件和全局服务 | 已存在 | 只作为 Source/Compatibility，不让 Core/Runtime 反向引用。 |
| PlayerLoader 仍碰旧 ControllerManager | 已确认 | Merge Phase A 拆出 Owner-only 兼容组件。 |
| 当前 WeaponDefinition 只支持 Projectile 类型 | 已确认 | 第二种攻击形态迁移时引入最小公共基类。 |
| HighestPriority Status 仍需来源感知聚合 | 合并前必须处理 | Phase D 增加 per-source refresh/aggregation 测试。 |
| 正式 Reward/Run Ledger 未实现 | 尚未实现 | ConfirmedKill Reward Stub 后再扩展。 |
| Sandbox AI/Spawner 不是产品系统 | 已知限制 | Phase G 替换，不在 Sandbox 类上扩展。 |
| 当前工作树未提交 | 高回滚风险 | 每个 Phase 小提交，禁止一次性批量移动全部 Legacy 文件。 |
| HellMaiden 数据资产可能丢引用/默认值 | 反编译固有限制 | 每个参数标记来源；无法证明的值进入 Compatibility Profile/调参表。 |
| Rich Build 可能形成递归爆炸 | 已有 Guard，内容尚未验证 | 每个迁移 Modifier 显式策略 + Trace + 压力测试。 |

## 16. 每个 Phase 的固定汇报格式

每个 Merge Phase 完成后必须汇报：

1. 新增文件。
2. 修改文件。
3. Authority 变化。
4. 迁入的 HellMaiden 行为和有意修正的旧缺陷。
5. 当前调用链。
6. EditMode/PlayMode/Host/Remote 测试结果。
7. 新增稳定 ID 和资产迁移数量。
8. 尚未解决的风险。
9. 下一 Phase 的唯一入口。

不要在一个 Phase 中顺手重构无关 UI、FMOD、AI 或存档。

## 17. 合并完成标准

只有同时满足以下条件，才能认为“HellMaiden GAS 已合并”，而不是“文件已经复制”：

- 当前 GAS 仍是唯一 Damage/Status/Build Core。
- Combat Core 和 Gameplay Runtime 不依赖 Mirror 或 Legacy 全局服务。
- 每个 Owner 拥有独立 PlayerAttribute/Loadout 实例。
- 所有武器命中统一进入 AttackSnapshot/CombatPipeline。
- 所有 Gameplay Status 全局可查询且只有一个 Executor。
- 所有旧 OnKill 已分别映射到 Predicted Gameplay 或 Confirmed Reward。
- Server 只保存 Canonical HP/Death/Status/Reward，不运行玩家完整 Build。
- Projectile 保持本地池化，Build Chain 不等待网络。
- 正式场景替换 Sandbox Spawner/HUD，并完成 2～4 人多进程测试。
- 10 个联网验收场景全部通过。
- Legacy `GameDirector.Player`、静态 `PlayerHand.Instance` 和旧 Damage 入口不再被新产品 Combat 使用。

## 18. 推荐执行入口

下一次代码实施从 **Merge Phase A** 开始，只做边界清理、PlayerLoader 兼容依赖拆分和基线测试；不要直接批量迁移所有 Weapon/Modifier。

完成 Phase A 后，再按本文第 13 节建立代表性 Projectile 切片。现有网络组件的具体配置和 API 见 [联机模块详细使用指南](MonsterSupergroup_联机模块详细使用指南.md)。
