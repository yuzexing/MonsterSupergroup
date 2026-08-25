# MonsterSupergroup 联机模块详细使用指南

> 适用快照：2026-08-25 当前工作树
>
> Unity：6000.3.17f1
>
> Mirror：96.0.1
>
> 当前范围：开发沙盒和接入规范；项目尚无正式联机 Gameplay Scene。

## 1. 先记住六条规则

1. **本地先打，网络后收敛。** Owner Client 的攻击、Projectile、命中、暴击、Build 和预测致死不等待 Server。
2. **Server 接收结果，不重算玩家 GAS。** 网络上传的是 `CombatResult`，不是“请 Server 再执行一次攻击”。
3. **Enemy HP/Death 是 Server Canonical。** 客户端预测只用于即时反馈和继续 Build Chain。
4. **PredictedLethalHit 不等于 ConfirmedKill。** Build 用前者，Loot/EXP/Gold/Kill Credit 用后者。
5. **Status 全局可见，但 Gameplay 只能执行一次。** `Replication` 和 `ExecutionAuthority` 是两个独立维度。
6. **Projectile 不联网生成。** 不要给普通 Projectile 添加 `NetworkIdentity`，也不要每发子弹调用 `NetworkServer.Spawn`。

如果只想了解模块全貌，先读 [GAS 与联机模块总览](MonsterSupergroup_GAS与联机模块总览.md)。

## 2. 当前可以直接使用什么

### 2.1 开发场景和资产

| 用途 | 路径 |
| --- | --- |
| 本地自动攻击 Gameplay | `Assets/Scenes/Gameplay.unity` |
| 纯 GAS 开发垂直切片 | `Assets/_Project/Scenes/Development/GASVerticalSlice.unity` |
| 联机验证沙盒 | `Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity` |
| 本地战斗资产 | `Assets/_Project/Content/LocalCombat` |
| 联机验证资产 | `Assets/_Project/Content/NetworkCombat` |

### 2.2 三类入口的区别

| 入口 | 用于验证 | 不用于验证 |
| --- | --- | --- |
| `GASVerticalSlice` | Core Stats、Modifier、Status、手动/自动攻击和 Debug UI | Mirror Authority、网络收敛 |
| `Gameplay` | `PlayerLoader → PlayerHand → Projectile` 本地自动战斗 | Host/Client、Canonical World |
| `NetworkCombatSandbox` | Mirror Host/Client、批处理、Canonical HP/Status、120 Enemy | 正式 Wave、Loot、卡牌和关卡流程 |

不要因为 `Gameplay.unity` 能自动攻击，就认为它已经是联网场景；也不要把 Sandbox Spawner 搬进产品场景后就称为正式波次系统。

## 3. 第一次运行 NetworkCombatSandbox

### 3.1 准备

1. 等待 Unity 完成脚本编译，Console 中不能有编译错误。
2. 打开 `Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity`。
3. 在 Hierarchy 中确认存在：
   - `Network Runtime`；
   - `NetworkCombatWorld` 实例；
   - 四个 `Player Start`；
   - `Main Camera`。
4. 进入 Play Mode。

### 3.2 启动 Host

1. 在 Mirror HUD 点击 **Host (Server + Client)**。
2. Server 应生成一个 Owner Player 和 120 个 Enemy。
3. Owner Player 的 `NetworkPlayerBootstrap.OnStartAuthority()` 会调用 `PlayerLoader.Load()`。
4. `PlayerLoader` 在 Slot 0 装备初始 Weapon，PlayerHand 随即自动寻找最近 Enemy 并攻击。
5. 你应看到 Enemy 被本地 Projectile 命中，并最终由 Canonical State 收敛 HP/Death。

Host 模式同时包含 Server 和一个 Client，适合验证完整调用链，但不能替代真正的 Remote Client 测试。

### 3.3 启动额外 Client

一个 Unity Editor 进程不能同时充当多个独立 Client。验证 2～4 人时，使用以下任一方式：

- 一个 Editor 启动 Host，另外启动包含 Sandbox 的 Development Build 作为 Client；
- 使用项目副本/多 Editor 工具启动额外 Client；
- 运行 Dedicated Server，再启动多个 Client Build。

当前 Sandbox 故意未加入正式 Build Profile。若要制作临时联机测试包：

1. 新建仅用于开发的 Build Profile。
2. 把 `NetworkCombatSandbox.unity` 加入该 Profile。
3. 不要修改产品 Build Profile 来长期携带 Sandbox。
4. Client 使用 HUD 的 Network Address 连接 Host；同机测试使用 `localhost`。

### 3.4 当前网络模拟参数

Sandbox 由生成工具配置：

| 参数 | 当前值 |
| --- | ---: |
| Transport | KCP，外包 `LatencySimulation` |
| 单向 Latency | 100 ms |
| 近似 RTT | 200 ms |
| Jitter | 0.05 |
| Jitter Speed | 2 |
| Unreliable Loss | 5% |
| Unreliable Scramble | 2% |
| NetworkManager Send Rate | 60 |
| Max Connections | 4 |

Combat Batch 使用可靠 Command，因此 Unreliable Loss 主要用于模拟其他不可靠流量；本地战斗反馈无论如何都不等待该 Command。

### 3.5 正确停止

1. 先在 HUD 停止 Host/Client，或退出 Play Mode。
2. 确认 `MirrorNetworkCombatBridge.OnStopAuthority()` 已 Dispose Collector。
3. Source 断线时，Server 会把其尚未结束的 SourceClient DOT 切换为仅剩余 Tick 的 Server 接管；不会重跑原玩家 Build。

## 4. 重新生成开发资产

### 4.1 本地战斗资产

菜单：

```text
Tools > MonsterSupergroup > Gameplay > Rebuild Local Auto Combat
```

该命令会重建并验证：

- `LocalProjectile.prefab`
- `StarterProjectileWeapon.prefab`
- `StarterProjectileWeapon.asset`
- `LocalPlayer.prefab`
- `LocalEnemy.prefab`
- `Assets/Scenes/Gameplay.unity`

### 4.2 GAS 垂直切片

菜单：

```text
Tools > MonsterSupergroup > Gameplay > Rebuild GAS Vertical Slice
```

该命令用于重建纯 GAS 开发 Scene、Equipment Set 和 Perk Set。

### 4.3 联机验证资产

菜单：

```text
Monster Supergroup > Network Combat > Build Validation Sandbox
```

输入：

```text
Assets/_Project/Content/LocalCombat/LocalPlayer.prefab
Assets/_Project/Content/LocalCombat/LocalEnemy.prefab
```

输出：

```text
Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab
Assets/_Project/Content/NetworkCombat/NetworkEnemy.prefab
Assets/_Project/Content/NetworkCombat/NetworkCombatWorld.prefab
Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity
```

### 4.4 生成工具的重要警告

`Build Validation Sandbox` 不是普通“补组件”命令，而是开发内容生成器。它会按代码重新保存上述三个 Prefab 和 Scene。

正确流程：

1. 先保存或提交你希望保留的手工修改。
2. 修改作为来源的 LocalPlayer/LocalEnemy 或 Builder 代码。
3. 执行生成菜单。
4. 检查 Git Diff，确认没有覆盖产品资产。
5. 运行 EditMode 和 PlayMode 测试。

不要把只存在于生成出来的 Network Prefab 上的手工修改当作长期数据源；下次生成会丢失它。

## 5. GAS Authoring：创建可装备的 Projectile Weapon

### 5.1 创建 Projectile Prefab

最低组件建议：

- `Rigidbody2D`：Kinematic、Gravity Scale 0、Continuous Collision。
- Trigger `Collider2D`。
- `StraightProjectileBehaviour`。
- 可选 Sprite/VFX；这些只负责表现。

禁止添加：

- `NetworkIdentity`
- `NetworkTransform`
- 每颗 Projectile 的 NetworkBehaviour

`StraightProjectileBehaviour` 会携带发射时的 `AttackSnapshot`。即使玩家之后换装或下一次攻击刷新 Stats，这颗在途 Projectile 仍使用自己的快照。

### 5.2 创建 Weapon Prefab

Weapon Prefab 当前需要：

- `WeaponRuntimeBehaviour`
- `ProjectileAttackBehaviour`

`ProjectileAttackBehaviour` 有 `[RequireComponent(typeof(WeaponRuntimeBehaviour))]`。由 PlayerHand 动态创建时，Slot 会把 `InitializeOnAwake` 关闭并显式初始化，避免 Awake 使用错误配置。

### 5.3 创建 WeaponDefinition

在 Project 窗口执行：

```text
Create > Monster Supergroup > Gameplay > Projectile Weapon
```

逐项配置：

| 字段 | 要求 |
| --- | --- |
| Combat ID | 非 0；用于 Ability/Weapon 身份，不要随意复用。 |
| Base Stats | Damage、Crit、Speed、Size、Duration、Projectile Count 等基础值。 |
| Weapon Prefab | 上一步的 `ProjectileAttackBehaviour` Prefab。 |
| Projectile Prefab | 带 `StraightProjectileBehaviour` 的本地 Prefab。 |
| Starting Equipment | 0～3 个 Equipment Modifier Set。 |
| Perk Modifier Set | 可空；当前用于武器全局 Perk 数值。 |
| Target Range | 必须为有限正数。 |
| Projectile Speed | 必须为有限正数。 |
| Projectile Hit Count | 至少 1。 |
| Spawn Radius | 不能为负。 |
| Spawn Offset | 发射点本地偏移。 |
| Rotate To Movement | Projectile 是否朝移动方向旋转。 |

`WeaponDefinition.Validate()` 会拒绝 0 Combat ID、缺 Prefab、超过三个 Starting Equipment、空 Equipment、非法范围/速度等配置。

### 5.4 创建 Equipment 和 Perk

菜单：

```text
Create > Monster Supergroup > GAS > Equipment Modifier Set
Create > Monster Supergroup > GAS > Perk Modifier Set
```

在 Inspector 的 Modifier 列表中选择已注册类型并设置参数。当前第一批可用类型包括：

- Damage Stat Modifier
- On Hit Burn
- Weapon Speed Perk

同一个 Modifier ID 可以在一个资产中出现多次；Runtime 使用 `ModifierHandle` 区分实例。不要用运行时类型名或反射哈希充当持久 ID。

### 5.5 新增一种 Modifier 类型

1. 在 `GAS.Core` 中创建参数类，继承正确的 `EquipmentModifierParameters` 或 `PerkModifierParameters`。
2. 创建 Runtime Modifier，选择正确阶段：
   - `StaticStatModifier`
   - `DynamicStatModifier`
   - `DynamicOnDamageModifier`
   - `OnHitModifier`
   - `OnPredictedLethalHitModifier`
3. 为类型声明稳定的显式 ID Attribute。
4. 参数构造与 Runtime 构造必须验证 NaN、Infinity、范围和空引用。
5. 执行：

```text
Tools > MonsterSupergroup > GAS > Rebuild Registry
Tools > MonsterSupergroup > GAS > Validate All
```

6. 检查 `GeneratedModifierRegistry.g.cs` 只发生预期变更。
7. 为公式、执行顺序、ID 和序列化补 EditMode 测试。

禁止在 Runtime 使用 `Assembly.GetTypes()` 或 Modifier 类名哈希恢复注册表。

### 5.6 OnHit Status 的实现模式

以 `OnHitBurnModifier` 为参考：

- 在 `ApplyEffect(OnHitModifierArgs args)` 中计算本次状态参数。
- 使用 `args.Context.SourcePlayerId`、`SourceEntityId`、`TargetEntityId` 和完整 `sourceContext`。
- `SourceClient` DOT 指定 `StatusExecutionAuthority.SourceClient`。
- Stun 等真正控制 Server AI 的状态指定 `StatusExecutionAuthority.Server`。
- 调用 `args.Target.ApplyStatus(...)`；不要在 Modifier 中写 Mirror Command。

当前 Burn 的关键模式如下，省略了参数校验：

```csharp
args.Target.ApplyStatus(new StatusApplication(
    definition,
    tickDamage,
    numberOfHits,
    hitInterval,
    priority,
    args.DamageInfo.Id,
    sourcePlayerId: args.Context.SourcePlayerId,
    sourceEntityId: args.Context.SourceEntityId,
    targetEntityId: args.Context.TargetEntityId,
    executionAuthority: StatusExecutionAuthority.SourceClient,
    sourceContext: args.Context));
```

只要目标的 `StatusController` 已由 `NetworkCombatantAdapter` 观察，预测变化会自动成为 `StatusMutation`。

## 6. Player Prefab 的配置与生命周期

### 6.1 本地 Gameplay 组件

Player 至少需要：

- `CombatantBehaviour`
- `CombatTeamBehaviour`，Team 为 Player
- `NearestEnemyTargetProvider`
- `PlayerHandBehaviour`
- `PlayerLoader`
- `LocalPlayerMovement` 或未来产品移动组件
- 一个作为武器实例父节点的 Attacks Root

引用关系：

```text
PlayerLoader
  -> PlayerHandBehaviour
  -> CombatantBehaviour
  -> Initial WeaponDefinition

PlayerHandBehaviour
  -> Attacks Root
  -> NearestEnemyTargetProvider
  -> Owner CombatTeamBehaviour
```

### 6.2 Network Player 组件

在上述本地组件之外增加：

| 组件 | 配置 |
| --- | --- |
| `NetworkIdentity` | Player 的 Mirror 身份。 |
| `NetworkTransformReliable` | `syncDirection = ClientToServer`。 |
| `MirrorNetworkCombatBridge` | 默认 0.05s Flush；开发 Trace 可开启。 |
| `NetworkWeaponCombatAdapter` | 引用 Bridge 和 PlayerHand；动态武器无需逐个手工设置网络字段。 |
| `NetworkCombatantAdapter` | `EntityKind = Player`，`Authority = OwnerFinal`。 |
| `NetworkPlayerBootstrap` | 引用 PlayerLoader 和 Owner-only Movement。 |

### 6.3 Authority 启动顺序

```text
NetworkIdentity gains authority
  -> MirrorNetworkCombatBridge.OnStartAuthority
     -> create EventId source, Trace and ClientCombatCollector
     -> request late-join Canonical snapshot
  -> NetworkWeaponCombatAdapter.OnStartAuthority
     -> create CombatRuntimeServices
     -> inject source IDs, Event Sink, Guard and Time Source into PlayerHand/Weapon
  -> NetworkPlayerBootstrap.OnStartAuthority
     -> enable owner movement
     -> PlayerLoader.Load(spawnPosition)
     -> initialize PlayerHand
     -> equip initial weapon in slot 0
     -> activate weapons
```

Mirror 不保证不同组件回调以 Inspector 顺序执行，因此 `NetworkWeaponCombatAdapter` 同时处理两种情况：订阅 `OwnerCollectorReady`，以及发现 Collector 已存在时立即绑定。已有 Weapon 也会在注入 Runtime Services 后重新初始化。

### 6.4 装备和卸下武器

PlayerHand 必须先初始化：

```csharp
PlayerHandBehaviour hand = playerLoader.PlayerHand;

bool equipped = hand.TryEquipWeapon(1, secondWeaponDefinition);
PlayerHandSlot slot = hand.Hand.GetSlot(1);
bool equipmentAdded = slot.TryAddEquipment(equipmentModifierSet);
bool equipmentRemoved = slot.TryRemoveEquipment(equipmentModifierSet);
bool weaponRemoved = hand.Hand.TryUnequipWeapon(1);
```

约束：

- Slot 索引范围是 0～3。
- 每个 Slot 最多三个 Equipment Set。
- 重复加入同一个 Set 返回 `false`。
- 装备过程先完整创建 Candidate，成功后才替换旧 Weapon；初始化异常不会留下半配置武器。
- 卸下时会 Deactivate、Shutdown、Destroy 实例并清空 Equipment。
- `PlayerHand.Shutdown()` 会释放四个 Slot 和所有动态 Weapon。

### 6.5 自动攻击流程

```text
ProjectileAttackBehaviour.Update
  -> interval = 1 / resolved Weapon Speed
  -> NearestEnemyTargetProvider.TryGetNearest
  -> WeaponRuntimeBehaviour.BeginAttack
  -> freeze AttackSnapshot
  -> spawn one or more local pooled projectiles
  -> StraightProjectileBehaviour trigger hit
  -> WeaponRuntimeBehaviour.ResolveHitDetailed(snapshot, target)
```

一次 Volley 的所有 Projectile 当前共享同一 AttackSnapshot；每个命中会创建自己的 Hit/Damage 子事件。

## 7. Enemy Prefab 的配置

### 7.1 本地组件

- `CombatantBehaviour`
- `CombatTeamBehaviour`，Team 为 Enemy
- Collider/Physics 组件
- `LocalEnemyChase` 或产品 Enemy AI 适配器

### 7.2 Network Enemy 组件

| 组件 | 配置 |
| --- | --- |
| `NetworkIdentity` | Enemy 的 Canonical Entity ID 来源。 |
| `NetworkTransformReliable` | `syncDirection = ServerToClient`。 |
| `NetworkCombatantAdapter` | `EntityKind = Enemy`，`Authority = ServerCanonical`。 |
| `NetworkEnemyServerDriver` | 只在 Server 启用追踪；Canonical Dead 后 `NetworkServer.Destroy`。 |

不要保留 `LocalEnemyDeathBehaviour`，否则本地预测死亡可能抢先销毁 Network Object，破坏 Server Canonical 生命周期。

### 7.3 由 Server 生成

正式 Wave/Spawner 必须只在 Server：

```csharp
GameObject enemy = Instantiate(enemyPrefab, position, rotation);
NetworkServer.Spawn(enemy);
```

并确保 Enemy Prefab 已加入 NetworkManager 的 Spawn Prefabs。`NetworkEnemySandboxSpawner` 仅用于开发，正式系统应替换它，而不是在它上面堆 Wave、Loot 和关卡逻辑。

## 8. 正式联机场景的接入清单

当前没有正式联机场景。未来创建时按以下顺序配置。

### 8.1 场景级对象

1. 放置并激活一个、且只能一个 `NetworkCombatWorld`。
2. 确保它在玩家和 Enemy Spawn 前完成 Awake；该组件已有 `DefaultExecutionOrder(-10000)`。
3. 配置一个产品 `NetworkManager` 和 Transport。
4. 将 NetworkPlayer 设为 Player Prefab。
5. 将所有 Network Enemy/共享对象加入 Spawn Prefabs。
6. 放置 2～4 个 `NetworkStartPosition`，或由产品 Spawn 服务提供位置。
7. 只在 Server 启动 Wave、Enemy AI、共享掉落和场景推进。

### 8.2 不应搬入产品场景的 Sandbox 内容

- `NetworkManagerHUD`
- `LatencySimulation` 的固定测试参数
- `NetworkEnemySandboxSpawner`
- 固定 120 Enemy 网格
- 开发 Camera/临时 Debug UI

产品可以保留可配置的延迟模拟开关，但不要把测试默认值硬编码为发行设置。

### 8.3 场景切换

在切换前应做到：

1. 停止生成新的本地攻击。
2. 对 Owner Bridge 调用一次 `Flush()`，把已排队的共享结果发出。
3. 由 Server 结束当前 Wave/Scene Canonical 生命周期。
4. 释放 PlayerHand Projectile、Status 订阅和临时表现。
5. 确保新场景只有一个 `NetworkCombatWorld`；若设计为跨场景常驻，则禁止新场景再创建第二个。

当前项目尚未实现完整产品切场流程，本节是接入约束，不代表已有自动场景迁移。

## 9. 一次攻击如何进入网络

### 9.1 本地阶段

`CombatPipeline.ResolveHitDetailed`：

1. 生成 Hit Context。
2. 应用每目标 DynamicOnDamage。
3. 计算 Direct Damage/Crit。
4. 调用目标的预测 `ReceiveDamage`。
5. 发布包含 requested/resolved 与 predicted-applied 值的 `DamageResolved`。
6. 执行 OnHit Modifier。
7. 若 Direct Damage 或 OnHit Effect 使目标从 Alive 预测为 Dead，发布 `PredictedLethalHit` 并执行对应 Modifier。

`DamageResolved` 先进入 Collector，但 Bridge 只会在后续 Flush 时发送 Batch；当前 Root 下的 OnHit 和 PredictedLethal Build 仍会在本地同步完成，不会等待网络。

### 9.2 收集阶段

`ClientCombatCollector` 实现 `ICombatEventSink`。它只收集满足以下条件的 Damage：

- Event Kind 是 `DamageResolved`；
- Damage 大于 0；
- `Context.SourcePlayerId` 等于本地 Owner Player ID。

因此：

- Observer 不会替别人的攻击提交 Damage。
- Trace 可以记录 Hit/PredictedLethal，但网络只提交改变共享世界所需的结果。
- 不应在 WeaponBehaviour 中手工构造 Mirror Command。

### 9.3 Batch 与提交

`MirrorNetworkCombatBridge` 默认每 0.05 秒：

1. 从 Collector Drain 最多 256 Result、128 Status Mutation、8 Player Report。
2. 给 Batch 分配递增 `BatchSequence`。
3. 通过可靠 `[Command]` 发送。

Collector 总容量默认 4096。若一帧 Build Chain 超过容量，会明确抛出“先 Flush”异常，而不是静默丢结果。

### 9.4 Server 验证

Gateway 默认上限：

| 内容 | 每 Batch 上限 |
| --- | ---: |
| Combat Results | 512 |
| Status Mutations | 256 |
| Player Health Reports | 16 |

它检查：

- Sender 和 Connection Identity。
- Event ID 中的 Source Slot、Connection Epoch 和 Sequence。
- Source 是否属于 Sender。
- Target 是否存在、是否 Canonical Alive。
- Damage/Health/Status 数值是否合法。
- Event 是否已经处理。
- Batch Sequence 是否明显非法。
- Server 硬规则，如 Absolute Invulnerable。

它不会检查玩家是否真的拥有某张卡、暴击率是否正确或 Projectile 是否真的碰撞。后续若增加合作 PvE 的异常检测，应保持“轻量、可观测、不造成普通 Ping 延迟”的原则。

### 9.5 Canonical 回写

```text
ServerCombatGateway
  -> CombatLedger / ServerStatusRegistry
  -> CanonicalWorldBatch
  -> NetworkCombatWorld ClientRpc/TargetRpc
  -> CanonicalWorldReplica.Apply
  -> NetworkCombatantAdapter.ApplyCanonicalHealth
  -> StatusController.UpsertCanonical / RemoveCanonical
```

客户端不会回滚已经发生的 Explosion、Summon 或整条 Build Chain；它只用新 Canonical State 修正未来行为。

## 10. 查询 Canonical World

### 10.1 查询 Entity

```csharp
using MonsterSupergroup.NetworkCombat;

NetworkCombatWorld world = NetworkCombatWorld.Instance;
if (world != null &&
    world.Replica.TryGetEntity(enemyNetId, out CanonicalEntityState state))
{
    int canonicalHealth = state.Health;
    bool canonicalAlive = state.Alive;
    uint stateVersion = state.StateVersion;
}
```

`CombatantBehaviour.CurrentHealth` 可以包含本地预测；需要奖励、Wave 或共享 UI 的最终事实时使用 Replica/Server Ledger。

### 10.2 监听 Entity/Status 变化

```csharp
private CanonicalWorldReplica replica;

private void OnEnable()
{
    NetworkCombatWorld world = NetworkCombatWorld.Instance;
    if (world == null) return;

    replica = world.Replica;
    replica.EntityChanged += OnEntityChanged;
    replica.StatusChanged += OnStatusChanged;
}

private void OnDisable()
{
    if (replica == null) return;
    replica.EntityChanged -= OnEntityChanged;
    replica.StatusChanged -= OnStatusChanged;
    replica = null;
}
```

订阅者必须对 `OnEnable/OnDisable` 或 Start/Stop 生命周期对称处理，避免场景切换后重复回调。

## 11. Status 的正确使用

### 11.1 查询有效状态

`StatusController` 同时保存 Canonical Stack 和本地 Predicted Delta。Gameplay 条件通常查询 Effective State：

```csharp
StatusController statuses = targetCombatant.StatusController;

bool poisoned = statuses.Has(EnemyStatusID.Poison);
bool poisonedByPlayerA = statuses.HasFromSource(EnemyStatusID.Poison, playerAId);
int effectiveStacks = statuses.GetStackCount(EnemyStatusID.Poison);
int canonicalStacks = statuses.GetCanonicalStackCount(EnemyStatusID.Poison);
int predictedDelta = statuses.GetPredictedStackDelta(EnemyStatusID.Poison);
IReadOnlyList<StatusInstance> instances =
    statuses.GetInstances(EnemyStatusID.Poison);
```

跨玩家 Build 只要在目标已注册后，也可以使用：

```csharp
bool hasPoison = NetworkCombatWorld.Instance.Replica.HasStatus(
    targetNetId,
    EnemyStatusID.Poison);
```

### 11.2 为什么必须按 Source 分实例

以下两个状态不能合并成一个来源不明的 Poison：

```text
Poison Instance A: Definition=Poison, SourcePlayer=A, Target=Enemy123
Poison Instance B: Definition=Poison, SourcePlayer=B, Target=Enemy123
```

它们可以同时出现在所有客户端的 Replica 中，但 DOT Gameplay 分别由 A、B 的 Owner Client 执行。这样才能正确处理跨玩家 Build、断线接管和 Kill Attribution。

### 11.3 ExecutionAuthority

| 值 | Gameplay Executor | 示例 |
| --- | --- | --- |
| `SourceClient` | `SourcePlayerId` 对应 Owner | Burn、Poison、玩家 Build 相关 DOT |
| `Server` | Server | Stun、共享 AI Stop、Server 环境伤害 |
| `TargetOwnerClient` | Target Player Owner | 只适合明确由受击玩家最终执行的 Player 状态 |

所有机器仍然持有 Replica。没有执行权只意味着“不 Tick Gameplay”，不意味着看不见或不能查询。

### 11.4 Canonical 与 Predicted

例：Server Canonical Poison 为 7，A 本地再施加 3 层。

```text
CanonicalStack = 7
PredictedDelta = +3
EffectiveStack = 10
```

A 可以立即触发“Poison >= 10”的毒爆，同时 Collector 提交 `StatusMutation +3`。Server 回写 Canonical 10 后，本地 Delta 被清零，不会再次触发同一预测 Gameplay。

### 11.5 不要广播每次 DOT Tick

SourceClient DOT 的 Tick 通过现有路径产生普通 `DamageResolved`，再进入 Batch。Observer 只接收 Canonical HP/Status 更新。不要给每个 Observer 发一个“播放并执行 DOT” RPC，否则 DOT 会被多台机器重复结算。

## 12. PredictedLethalHit 与 ConfirmedKill

### 12.1 本地 Build

尸爆、召唤、冷却重置、临时 Buff 和 Build Chain 应实现为 `OnPredictedLethalHitModifier`。旧 HellMaiden `OnKillModifier` 在当前 Core 中只是兼容基类，其实际执行阶段也是 Predicted Lethal。

这保证：

- A/B 同时预测击杀时，两边各自 Build 都能立即发生。
- 200ms Ping 不会让尸爆等待 Server。
- Server 不需要保存和运行玩家完整 Build。

### 12.2 Client 监听 ConfirmedKill

只用于表现或只读提示：

```csharp
private CanonicalWorldReplica replica;

private void OnEnable()
{
    NetworkCombatWorld world = NetworkCombatWorld.Instance;
    if (world == null) return;
    replica = world.Replica;
    replica.KillConfirmed += OnKillConfirmed;
}

private void OnDisable()
{
    if (replica != null)
        replica.KillConfirmed -= OnKillConfirmed;
    replica = null;
}

private void OnKillConfirmed(ConfirmedKill kill)
{
    // UI/提示可以在这里读取 KillerPlayerId 和 TargetEntityId。
    // 不要在每个 Client 直接发放 Gold/EXP。
}
```

### 12.3 Server 奖励接入

产品 Reward Service 应只在 Server 订阅 `ServerCombatGateway.ConfirmedKillProduced`，或消费 `NetworkCombatWorld.ServerCanonicalBatchProduced` 中的 `ConfirmedKills`。

一次 ConfirmedKill 包含：

- `CauseEventId`
- `KillerPlayerId`
- `TargetEntityId`
- `TargetStateVersion`

Reward Service 还应按 `CauseEventId/TargetEntityId` 自身保持幂等，避免服务重启或未来持久化重放导致重复奖励。

## 13. Combat Trace 与诊断

### 13.1 开启方式

`MirrorNetworkCombatBridge` Inspector：

- `Enable Combat Trace = true`
- `Combat Trace Capacity` 默认 4096，最小 64

只在 Owner Authority 建立后，`bridge.Trace` 才可用。Release/性能测试可以关闭。

### 13.2 Trace 内容

- Event/Root/Parent ID
- Source Player/Entity
- Target Entity
- Ability/Build
- Tags
- Damage
- Target StateVersion
- ChainDepth
- PredictedLethalHit
- Status Change
- ConfirmedKill

Trace 是有界环形缓冲，不会无限增长。

### 13.3 建议的排错顺序

```text
AttackStarted
  -> HitResolved
  -> DamageResolved
  -> optional PredictedLethalHit
  -> ClientCombatCollector pending result
  -> Server Gateway accepted/rejected metrics
  -> CanonicalEntityState version increment
  -> optional ConfirmedKill
```

如果本地能看到命中但 Server HP 不变，先查 Collector/Gateway；不要先怀疑 Projectile VFX。

## 14. 常见问题

| 现象 | 最可能原因 | 检查/修复 |
| --- | --- | --- |
| `NetworkCombatWorld is required...` | World 缺失或 Player 先于 World 创建 | 场景放置且激活唯一 World，确保 Spawn 前完成 Awake。 |
| `Only one NetworkCombatWorld may be active` | 场景和常驻对象各有一个 | 选择 Scene-owned 或 Persistent-owned 生命周期，只保留一个。 |
| Owner Player 不移动 | Movement 没交给 Owner 或 Bootstrap 引用丢失 | 检查 `NetworkPlayerBootstrap`、`isOwned` 和 Movement 字段。 |
| Owner 不自动攻击 | PlayerLoader/Hand/Initial Weapon/Target Provider 引用缺失 | 从 `PlayerLoader.Load()` 开始逐项检查，确认 Enemy Team/Range。 |
| 本地掉血但 Canonical HP 不变 | Weapon 未注入 Collector、Target 没有 netId 或 Result 被拒绝 | 检查 `NetworkWeaponCombatAdapter`、CombatContext IDs、Gateway Metrics。 |
| Result 被 `InvalidSequence` 拒绝 | 手工伪造 Event ID、Epoch 不匹配或 Batch 超限 | 使用 Bridge 提供的 Event ID Source，不要自行拼 Sequence。 |
| 同一伤害结算两次 | 绕过 Gateway 或复用了不同 Event ID | 所有共享结果只走 Gateway；同一逻辑事件必须保持同一 Event ID。 |
| Status 所有人都 Tick | Adapter/ExecutionPolicy 未配置，或用了离线默认 Policy | Network Combatant 必须注册 `StatusExecutionScope`；核对 ExecutionAuthority。 |
| Status 看得见但 Stun 不停 AI | Stun 只做了客户端表现 | Stun 使用 Server Authority，并由 Server AI 消费状态。 |
| Late Join 看不到已有 Status | Combatant 未向 Replica 注册 StatusController | 检查 `NetworkCombatantAdapter.OnStartClient`。Replica 会在晚注册时回灌快照。 |
| A/B 同时尸爆被认为是 Bug | 把 Predicted Build 当成 Kill Credit | 两边 Build 允许发生；Server ConfirmedKill 只能一次。 |
| 奖励发了两次 | 奖励监听 Predicted Lethal 或每个 Client 都发放 | 奖励只在 Server 消费 ConfirmedKill，并自行幂等。 |
| Projectile 数量导致网络爆炸 | 给 Projectile 添加了 NetworkIdentity/Spawn | 改回本地池化，只提交最终 CombatResult。 |
| Collector buffer full | 单次链超过 4096 且未及时 Flush | 检查无限 Chain，降低 Batch 延迟或在安全边界 Flush；不要静默扩成无限列表。 |
| 反复生成后 Prefab 修改消失 | 修改的是生成物 | 把修改移到 Local 来源 Prefab 或 Setup Utility。 |

## 15. 测试方法

### 15.1 Unity Test Runner

打开：

```text
Window > General > Test Runner
```

先运行 EditMode，再运行 PlayMode。

重点测试目录：

```text
Assets/_Project/Tests/EditMode/GAS
Assets/_Project/Tests/EditMode/NetworkCombat
Assets/_Project/Tests/PlayMode/GAS
Assets/_Project/Tests/PlayMode/Gameplay
```

### 15.2 修改不同模块后至少运行什么

| 修改 | 最低测试 |
| --- | --- |
| Stats/Modifier/Status Core | 全部 GAS EditMode |
| Event Identity/Trigger Guard | `CombatContextTests` + Gateway Tests |
| Weapon/PlayerHand/Projectile | Gameplay PlayMode + PlayerHand Auto Combat |
| Gateway/Ledger/Status Registry | 全部 NetworkCombat EditMode |
| Mirror Prefab/Scene | NetworkCombat Sandbox PlayMode + 手工 Host |
| Authority 或断线行为 | Host + Remote Client，多进程验证 |
| 大量 Enemy/Build | Phase 7 压力测试，再做产品内容 Profiler 测试 |

### 15.3 当前已记录基线

```text
EditMode: 88 / 88 Passed
PlayMode: 31 / 31 Passed
```

日志位于：

```text
Logs/codex-resume-editmode.xml
Logs/codex-resume-playmode.xml
```

新增正式场景或迁入 HellMaiden 逻辑后，必须生成新的测试记录；不要永久引用这份旧快照当作新版本通过证明。

## 16. 扩展时禁止的做法

- 在 GAS Core 中引用 Mirror 类型或 Attribute。
- 在 Weapon/Modifier 内直接调用 `[Command]`。
- 再创建一套 `DamageSystem2`、`StatusSystem2` 或 `CombatSystem2`。
- 让 Server 重跑玩家 Crit、Build 和 Projectile。
- 让普通攻击等待 Server 才显示 Hit/Damage。
- 把 Enemy Predicted HP 当成奖励事实。
- 让 `ConfirmedKill` 驱动即时尸爆。
- 把所有 Status Tick 同时运行在 Source、Server 和 Observers。
- 因为 TargetStateVersion 落后一版就拒绝普通合法伤害。
- 对大规模 Build Chain 做完整 Combat Rollback。
- 为每个 Combat Event 单独发送 RPC。
- 把多个有独立语义的 Damage Event 无条件合并成一个总伤害。

## 17. 下一步

在创建正式联机场景前，先按照 [HellMaiden GAS 合并方案](MonsterSupergroup_HellMaiden_GAS合并方案.md) 完成一个代表性联机切片：

```text
PlayerAttribute 子集
  + 一把 HellMaiden Projectile Weapon
  + Crit
  + Burn
  + PredictedLethalHit Build
  + Server ConfirmedKill Reward Stub
```

这个切片应继续复用本文全部网络组件；只扩展现有 GAS 和 Weapon Runtime，不另建平行系统。
