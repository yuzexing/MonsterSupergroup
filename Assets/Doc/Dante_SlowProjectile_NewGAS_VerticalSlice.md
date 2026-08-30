# Dante Slow Projectile → New GAS Native Runtime 迁移说明

## 1. 当前结论

`Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset` 已完成第一条真实 HellMaiden Weapon Vertical Slice：

- 原 `Dante_SlowProjectile_Behaviour.prefab`、Projectile、Impact、Particle、Material、Shader、FMOD 字段和动画组件继续负责表现、生成几何与碰撞。
- Attack Stats、8 类纯数值 Modifier、Crit、Attack Snapshot、Damage、OnHit 与 PredictedLethalHit 统一由 New GAS 执行。
- Dante 的 Native 路径不会调用 Legacy `CalculateDamage`、Legacy Static/Dynamic Modifier、Legacy OnHit/OnKill 或 `LegacyCombatExecution.ResolvePrecomputedHit`。
- Legacy Modifier 的哈希 ID 只存在于 Editor 迁移程序集；生成后的 Runtime 资产只保存 New GAS stable ID 与 typed parameters。
- `NetworkPlayer.prefab` 上已有一个 `PlayerBuildRuntime`；每个 Player 实例拥有自己的 Weapon/Modifier 容器，不共享玩家级状态。

这条实现可作为后续 34 个 Equipment Modifier、29 个 Perk Modifier 和其他 Weapon 的迁移模板。

## 2. 正式 Runtime 调用链

```text
每个 Mirror Player
└─ PlayerBuildRuntime
   ├─ per-player Perk multipliers
   └─ per-weapon RuntimeEquipmentModifiers
      └─ WeaponRuntimeBehaviour
         ├─ BeginAttack(tags)
         ├─ immutable AttackSnapshot
         │  ├─ frozen stats / crit inputs
         │  ├─ frozen CombatContext + CombatId
         │  └─ leased hit-stage modifiers
         └─ ResolveHitDetailed(snapshot, target)
            └─ New GAS CombatPipeline
               ├─ Crit once
               ├─ Damage once
               ├─ OnHit once
               └─ PredictedLethalHit once

Legacy ProjectileAttackBehaviour / ProjectileAttack
├─ 读取 frozen Snapshot 决定 projectile count、size、speed、duration
├─ 继续生成旧 Projectile、播放动画/VFX/FMOD
└─ 碰撞后构造 NativeGasHit
   └─ EnemyHurtbox → EnemyController.ResolveNativeGasHit
      └─ CombatantBehaviour → CombatEvent / CombatResult → Server CombatLedger
```

旧表现组件只是 Snapshot 的消费者，不再是数值或战斗规则的第二个执行者。

## 3. 使用方法

### 3.1 为某个 Player 装备 Dante

```csharp
PlayerBuildRuntime build = player.GetComponent<PlayerBuildRuntime>();
WeaponData dante = /* WeaponData_Dante_SlowProjectile.asset */;

WeaponBehaviour runtimeWeapon = build.EquipWeapon(dante);
```

必须通过 `PlayerBuildRuntime.EquipWeapon` 装备 Native Weapon。若把该 WeaponData 交给旧 `PlayerHandSlot.AddWeapon`，代码会主动抛错，防止同一武器同时运行 Legacy GAS 与 New GAS。

### 3.2 添加/移除 Equipment

```csharp
NativeGasEquipmentDefinition damage = /* NativeGasEquipment_Damage.asset */;

PlayerBuildEquipmentHandle handle = build.AddEquipment(
    runtimeWeapon,
    damage,
    levelIndex: 0);

build.RemoveEquipment(handle);
```

Handle 属于创建它的 `PlayerBuildRuntime`。不要跨 Player 使用 Handle，也不要直接把 Runtime Modifier 实例共享给另一个 Player。

### 3.3 添加/移除 Perk

```csharp
PlayerBuildPerkHandle handle = build.AddPerk(perkDataModifier);
build.RemovePerk(handle);
```

Perk multiplier 容器是 per-player；该 Player 已装备的每把 Native Weapon 都会重新刷新，但其他 Player 不受影响。

### 3.4 发起攻击

正式 Dante 表现层会自行调用：

```csharp
AttackSnapshot attack = weapon.NativeRuntime.BeginAttack(
    weapon.NativeDefinition.AttackTags);
```

每个 Projectile 通过 `AttackSnapshot.Retain()` 持有自己的 lease；发射者释放 owner reference 后，Snapshot 仍存活，直到最后一个 Projectile/Impact 释放 lease。命中时禁止重新读取共享 mutable Weapon Stats。

## 4. Stable ID 与参数映射

| Gameplay intent | Legacy hash ID（仅 Editor） | New GAS stable ID | Typed parameters |
|---|---:|---:|---|
| Damage | `1120648` | `0x01000001` | `DamageStatModifierParameters` |
| Speed | `3809246214` | `0x01000002` | `SpeedStatModifierParameters` |
| Size | `1050114896` | `0x01000003` | `SizeStatModifierParameters` |
| Duration | `19982737` | `0x01000004` | `DurationStatModifierParameters` |
| Crit Rate | `3443713987` | `0x01000005` | `CritRateStatModifierParameters` |
| Crit Multiplier | `2717296302` | `0x01000006` | `CritMultiplierStatModifierParameters` |
| Projectile Count | `1251233216` | `0x01000007` | `ProjectileCountStatModifierParameters` |
| Knockback | `3977118250` | `0x01000008` | `KnockbackStatModifierParameters` |

Runtime registry 由 `ModifierRegistryGenerator` 生成，不使用 `Assembly.GetTypes()`、`Activator.CreateInstance()` 或 Legacy reflective factory。

## 5. 已生成的正式资产

目录：`Assets/_Project/Content/HellMaiden/NativeGAS/Dante`

- `NativeGasWeapon_Dante_SlowProjectile.asset`
- `NativeGasEquipment_Damage.asset`
- `NativeGasEquipment_Speed.asset`
- `NativeGasEquipment_Size.asset`
- `NativeGasEquipment_Duration.asset`
- `NativeGasEquipment_CritRate.asset`
- `NativeGasEquipment_CritMultiplier.asset`
- `NativeGasEquipment_ProjectileCount.asset`
- `NativeGasEquipment_Knockback.asset`

旧 `Assets/MonoBehaviour/StatRaise_*.asset` 仍保留在工程内，作用仅是迁移输入、数值依据和行为对照。正式 Build/Loadout 应引用上述 Native 资产。

## 6. Legacy 行为对照结果

### 保持一致

- 8 类 Modifier 的执行阶段仍为 Static Stats，默认 Priority 为 `1`。
- 相同 Stat 的多个 Modifier 在同一层内相加，再套用原有 multiplier formula。
- Damage 最终使用向上取整；Crit Damage 维持截断行为。
- Crit Rate、Crit Multiplier、Projectile Count 维持 additive 语义。
- 负 multiplier 继续使用 `1 / (1 + abs(value))` 的旧语义。
- Dante Base Stats 保持：Damage `15`、Crit Multiplier `1.3`、Crit Rate `0.09`、Speed `0.4`、Size `1`、Duration `1`、Projectile Count `1`、Knockback Distance `1`。
- Dante 原资产的 `DamageType` 是 `Normal`，因此 Native tags 为 `Attack | Projectile`，不会因为视觉是火球而静默增加 `Fire`。
- `StatRaise_KnockbackRaiseEquipment` 每级实际同时包含 Knockback 与 Speed；Native 资产保留了这两个 Modifier，而不是只迁移名称中的 Knockback。

### 明确保留的限制

- Dante 源 `modifierFlags = 247`，不包含 `Duration` bit。因此 Duration Modifier 已被完整迁移为 New GAS Native Modifier，但 Dante 会按旧规则拒绝装备它。此项不是迁移遗漏。
- 本 Slice 未迁移 Chain Lightning、Explosion Build、Clone/Summon、复杂 Status Proc、Reward/XP/Gold 或跨 Slot 行为。
- 本 Slice 没有把旧 Equipment 的 multi-slot runtime 带入 New GAS；Editor 转换器遇到 multi-slot 会明确拒绝，而不会静默丢失语义。

## 7. Editor 迁移流程

### 源 HellMaiden 工程

菜单：

```text
Tools/HellMaiden Migration/Export Dante Native GAS Slice
```

输出：

```text
GASMigrationExport/HellMaiden_Dante_SlowProjectile_NativeSlice.unitypackage
```

Exporter 只导出 Weapon、8 个 Equipment 与表现依赖；明确排除 `.cs`、`.dll`、`.asmdef`、`.asmref` 和 Scene。

### MonsterSupergroup 工程

1. 导入上述 unitypackage。
2. 执行：

```text
Tools/HellMaiden Migration/Rebuild Dante Native GAS Assets
```

该 Editor 工具会：

- 把旧 managed-reference assembly 名从 `Assembly-CSharp` 规范化到恢复后的 `MonsterSupergroup.Gameplay.Combat`，仅用于读取迁移输入。
- 把旧 Animancer DLL 内的 `AnimancerComponent` 序列化引用迁移到官方 package 脚本，同时保留 `_Animator`、`_Transitions`、`_ActionOnDisable` 数据。
- 读取 Legacy ID、参数与 level。
- 生成只含 stable ID/typed parameters 的 Native 资产。
- 绑定 `WeaponData.nativeGasDefinition`。
- 确保 `NetworkPlayer.prefab` 上只有一个 `PlayerBuildRuntime`。

这两个 Assembly/GUID 规范化步骤只属于 Editor conversion boundary，不进入正式 Combat Runtime。

## 8. 验收结果（2026-08-30）

| 验收 | 结果 |
|---|---:|
| Unity 6 编译 | 通过，0 个 C# error |
| Dante/New GAS 专项 EditMode | `10 / 10` |
| 真实 Dante 双 Player PlayMode | `1 / 1` |
| 全部 GAS EditMode 回归 | `77 / 77` |
| 全部 Gameplay PlayMode 回归 | `42 / 42` |
| NetworkCombat EditMode 回归 | `41 / 41` |

专项测试覆盖：

- Legacy parameter → New typed parameter conversion。
- 8 个 Legacy ID → stable ID conversion。
- 8 个数值 Modifier 的公式、stack/order。
- immutable Snapshot freeze。
- Snapshot/Projectile lease 生命周期。
- Equipment Handle add/remove。
- 两个 PlayerBuildRuntime 的 Modifier state 隔离。
- 真实 Dante WeaponData、旧 Weapon Prefab 与 Native Definition 绑定。
- Dante/Projectile/Impact Prefab 无 Missing MonoBehaviour。
- Native 资产中无 Legacy hash ID。
- Native Weapon 调用 Legacy `CalculateDamage` 会被拒绝。

测试结果文件位于项目根目录 `TestResults`，日志位于 `Logs`。

## 9. 后续 Weapon/Modifier 的标准迁移步骤

1. 读取一个 Legacy Modifier 的 intent、参数、formula、stage、priority、stack 与上下文依赖。
2. 在 New GAS 中找到现有阶段；若缺少能力，只增加最小且可复用的 New GAS 扩展点。
3. 分配稳定且不可复用的 ID，定义 typed parameters 与 Native Runtime Modifier。
4. 重新生成 registry。
5. 在 Editor converter 中增加 Legacy ID → stable ID 映射；Legacy ID 不得进入 Runtime asset。
6. 将旧 WeaponBehaviour 限定为表现/几何/碰撞消费者，并为每个攻击实例持有 immutable Snapshot。
7. 明确阻断同一 Weapon 的 Legacy stats、crit、OnHit、OnKill 与 Damage 路径。
8. 增加 converter、公式、stack/order、freeze、Handle、双 Player 隔离和真实资产测试。
9. 记录所有行为差异；不能为了通过测试静默改变旧语义。

下一批应优先选择仍是纯数值或简单条件型的 Modifier；复杂 Build Chain、Status Proc 与跨 Slot 行为应分别建立新的 Vertical Slice，不应重新启用 Legacy Modifier Runtime。
