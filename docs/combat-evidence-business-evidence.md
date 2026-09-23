# 战斗输入、判定与输出统计证据

本文描述日志格式 2 中补充的业务证据。日志按实际调用顺序写入；`EventId`、`RootEventId` 和状态实例编号提供关联，时间戳只用于展示。本文中的回归夹具是自动化生成的合成输入，不代表补回了旧 Steam 包中缺失的现场。

本阶段测试必须在物理快照中执行并保留源码 SHA、XML、前后输入哈希；入口见 [续作验证合同](combat-evidence-next-validation.md)。下文的覆盖项描述用例意图，只有对应新运行结果才能证明通过；合成分歧夹具不能计入“真实业务故障已复现并修复”。

## 伤害和属性计算

| 阶段 | 输入与结果 | 关联及回放边界 |
| --- | --- | --- |
| `owner.attack_stats` | `baseStats`、`globalMultipliers`、按实际应用顺序的 `staticModifiers` / `dynamicModifiers`、属性映射 `remaps`；`after` 是实际冻结的 `AttackStatsSnapshot` | 攻击根事件；`weapon_stats / Rebuild` 重新创建允许的实际修饰器类型并执行属性公式 |
| `owner.damage_calculation` | 冻结属性 `stats`、本目标累加器 `targetMultipliers`、实际 `criticalRoll`、目标修饰器描述；`after` 含 `baseDamage`、暴击阈值和抽样、暴击倍率、最终请求伤害 | **具体伤害事件**，`ParentEventId` 为具体命中事件；同一攻击打中多个目标时各有独立记录。`damage / Calculate` 调用实际业务共用公式 |
| `owner.modifier_roll` | 修饰器类型、ID、数值参数、实际抽样、实际阈值；目标当时的存活状态和可用生命值 | 本次伤害或预测致死事件；`RollBelowChance` / `RollAtOrAboveChance` 解释是否执行效果 |
| `owner.hit` | 请求伤害，处理前后的生命、存活和版本，实际预测扣血 | 与具体伤害事件相同；`Applying` 和 `Applied` 能区别进入接收器与实际处理完成 |

原生属性修饰器当前支持 Damage、Size、Speed、Duration、CritRate、CritMultiplier、ProjectileCount 和 Knockback。回放通过固定允许列表创建实际业务类，不根据日志中的任意 CLR 类型名加载代码。ID、类型名、参数数量必须匹配。

目前生产 GAS 路径没有 `DynamicOnDamageModifier` 派生实现；仓库里 HellMaiden 的同名旧类型属于已禁用的旧运行路径。未知静态、动态或目标条件修饰器会报告 `UnsupportedModifier` / `UnsupportedDynamicModifier` / `UnsupportedTargetModifier`，不会把日志里的累加结果作为修饰器计算结果回填。未来接入新条件修饰器时，必须同时增加其实际读取的目标条件、参数恢复和回放允许项；仅增加日志字段不等于支持回放。

伤害公式回放的输入边界是已经冻结的攻击属性。查属性构建错误时必须连同同根 `owner.attack_stats` 记录回放，不能只用一次伤害计算证明上游属性正确。当前公式仍按原先顺序取伤害上整、暴击比较、暴击伤害截断和整数上限处理，没有改变伤害规则或随机抽样次数。

## DOT 与多人来源

`status.tick` 将 `statusInstanceId`、`applicationRevision`、`tickIndex`、来源玩家和目标实体提升到顶层。输入保留完整 `tick`，以及 `immediate`、层数、执行权限。

- 到期且允许执行：`Executed / ExecutionPolicyAccepted`。
- 立即结算：`Executed / ImmediateSettlement`。
- 到期但没有执行权限：`Ignored / ExecutionPolicyRejected`，不会调用伤害接收器。
- `owner.dot_damage` 使用真正提交的独立伤害事件编号，并关联同一个状态实例、应用版本和 Tick；记录请求伤害、前后生命、实际扣血和不扣血原因。

离线提取的 `expectedTicks` 只收集真正 `Executed` 的 Tick，不把权限拒绝记录视为执行。回放仍恢复实际 `StatusController` 的检查点并逐步推进时间，不合并时间步。一个状态输入被多个来源应用时，各实例及其来源保持独立。

## 输出统计

`stats.damage` 同时记录原始伤害事件、增量、统计前后值，并为武器统计和玩家总统计建立独立 `output_stats` 引擎。

| `input.metric` | 当前真实口径 |
| --- | --- |
| `ComputedDamage` | 武器界面统计使用的计算伤害；包含暴击累计 |
| `PlayerComputedDamage` | 玩家累计输出接收到的伤害增量 |

`output_stats / ApplyDamage` 调用界面计数器共用的累加代码。它保持现有行为，不主动去重、不强制采用 Canonical 扣血，也不把过量伤害自动删掉。查询应分别展示计算伤害、预测扣血、服务器扣血和界面计数。缺少具体伤害上下文时，原因标记为 `MissingDamageEventContext`。

`stats.hit` 是接触计数，发生在具体命中／伤害子事件创建前，所以关联的是攻击根事件。它不能当作和伤害事件一一对应的计数。存档分数合并、最高分比较、统计重置属于其他业务边界；跨越这些未接入回放的边界时，应从之后的完整检查点重新提取，不能把缺失统计输入解释成伤害丢失。

## 发射、窗口和物理边界

- `owner.attack_started` 记录实际根攻击创建时刻、冷却已过去的时间以及冻结的速度和持续时间。
- `owner.attack_gate` 仅在 `Ready`、`CoolingDown`、`OwnerBlocked` 三种状态变化时记录，避免每帧反复写相同冷却判定。
- 通用 `BasePlayerAttack` 将根事件绑定到命中盒。`owner.attack_window`、`owner.contact` 记录窗口绑定／开关、回调是否存在、首次接触、接触去重以及持续接触的计划／实际触发时刻。
- `owner.hit_filter` 标注目标已死、免疫、Owner 当前不能攻击、缺少原生接收器等拒绝边界。

命中盒和碰撞候选还保留本进程 `GetInstanceID()`，这些编号**不是跨端实体编号**；能够直接找到 `CombatantBehaviour` 时同时记录实体 ID。部分旧效果、召唤物或特殊攻击没有通过通用根绑定入口，记录会明确显示 `MissingAttackContext`，不能据此宣称已经具备完整跨端关联。完全没有 Unity 碰撞回调时，日志证据止于已知发射／窗口边界，需用 PlayMode 物理场景复现；现有回放不重新模拟 Unity 物理。

## 自动化与夹具

`CombatDamageEvidenceTests` 覆盖：暴击阈值两侧、实际原生修饰器和全局层、未知修饰器拒绝、同根多目标具体伤害 ID、多人 DOT 在检查点后继续执行且不重复旧 Tick、重复界面统计、错误计算断言的首处分歧与缩减、玩家总统计来源，以及开／关日志的结果和随机调用次数一致。

`CombatContactEvidenceTests` 使用独立 2D 物理场景实际触发进入／离开碰撞，而非手工调用触发函数。它验证重新接触同一目标只执行一次伤害回调并记录去重，以及关闭碰撞体时保留窗口关闭证据但不编造碰撞记录。

测试执行后自动在验证项目的 `Logs/CombatEvidenceBusinessFixtures/` 输出：

- `damage-formula-divergence.json`：合成错误断言，定位到 `$/requestedDamage`。
- `damage-formula-matched.json`：同一输入配正确断言，通过实际公式回放。
- `multiplayer-dot-resume.json`：两名来源的 DOT 从检查点继续执行第三 Tick。
- `output-statistics-duplicate.json`：重复统计输入与断言不符，定位到第二条记录。

这些是可执行的回归夹具和能力验证，不能替代同构建双机 Steam 运行时的性能、场景覆盖和真实输入验收。测试实际结果以对应 Unity XML 和日志为准。
