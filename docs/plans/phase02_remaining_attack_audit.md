# Phase 2 源资产与实施前审计：Summon / Ultimate

审计日期：2026-09-09。**本文保留实施前的仓库证据与缺口分析快照；其中“当前”“未发现”“需要恢复”等措辞描述审计时状态。** Summon / Ultimate 随后已完成接入和验收，Phase 2 已结束并停止待审阅，未进入 Phase 3。实际实现、恢复参数、测试结果及仍有效的源资源限制，以 [Phase 2 验收记录](F:/UnityStore/MonsterSupergroup/docs/plans/phase02_implementation.md) 为准；不要将下文历史缺口视为当前未完成项。

目标项目：`F:/UnityStore/MonsterSupergroup`。源项目：`F:/DecomplieLatest/HellMaiden/ExportedProject`。以下源资产路径均相对源项目 `Assets`，当前脚本路径均相对目标项目 `Assets/_Project`。

## 已确认规则

- Summon 原资产的茧阶段为 **60 秒**。
- 用户已确认：角色断线暂时移除期间，Summon 茧阶段继续随对局时间推进；重连恢复剩余时间或已成熟状态，不从 60 秒重新开始。
- 继续现有混合权限模型：Owner 执行玩家攻击模拟，服务器核验许可并合并结果，Remote 重放表现。Host 只执行其本人拥有的攻击；server-only 不创建这些攻击表现。
- 保留现有 GAS、ModifierFactory、AttackSnapshot、服务器攻击登记与结果链。本轮不迁移正式 Ultimate UI、视频、全局 GameFlow 或旧 Loot 系统。

## Summon ID402：源资产证据

| 项目 | 已确认内容 |
|---|---|
| 定义 | `MonoBehaviour/WeaponData_Ovid_Summon.asset`，ID402，Damage24、Speed0.2、Duration1、ProjectileCount0 |
| 武器入口 | `GameObject/Ovid_Summon_Behaviour.prefab`，序列化 `cacoonStateTime: 60`；脚本默认10秒不是产品配置 |
| 三变体 | `Ovid_Summon_ButterflyAI.prefab`、`Ovid_Summon_ButterflyAIFire.prefab`、`Ovid_Summon_ButterflyAIPoison.prefab` |
| Positioning 配置 | stopDistance4.5、optimalAttackDistance7、minDetectionRadius3、maxDetectionRadius15 |
| 原始 clip 时长 | `Ovid_Butterfly_Cacoon.anim` 4秒；Birth3.8秒；Move1.0333334秒；Attack_Enter1秒；Attack_Loop0.33333334秒；Attack_Exit0.6333334秒 |
| 普通冷却 | Speed0.2对应5秒，攻击退出后开始；首次破茧动画结束也重设普通冷却 |

代码显示的流程为：60秒茧及跟随 → Birth → 等待普通冷却并定位 → Enter动画 → 按 DurationValue 持续攻击 → Exit动画 → 普通冷却。上述 clip 数字为原始片段时长，不能在缺失 ClipTransition 速度时保证有效播放时长。主攻击持续时间的基础值为1秒；Loop片段本身的循环标记为0，不能擅自修改为循环动画。

三份 Butterfly prefab 的根 `SummonAIBehaviour` 对 progressionScaler、三个模块和 Animancer 的 fileID 引用完整，Positioning 参数也完整。但是 **Idle、Attack、Mover 的组件记录仅保留 m_Script，缺少全部序列化字段**。需要恢复的内容包括：

- Idle 的 mover、idleAnimation、birthAnimation。
- Attack 的 mover、hitBox、三个 ClipTransition、sweepAccelerationCurve、beamSound，以及缺失的覆盖参数。
- Mover 的 Rigidbody2D、isoPivot、rotationPivot、加速度曲线、移动 ClipTransition。
- Animancer 组件来自旧 DLL GUID，需映射当前项目使用的包；源 Animator 无 Controller，不能凭此断言动画资源不存在。

应先从原层级、clip 曲线与现存组件恢复可确定的关联。无法恢复的曲线、速度或音频字段应在迁移记录中明确写出采用的值和依据，不将代码默认值描述为原 prefab 的实际设置。

## Summon 当前调用链与缺口

```text
Gameplay/Combat/Player/Attacks/SummonAttackBehaviour.Init
→ variants pool / SummonAIBehaviour.Init
→ OvidSummonIdleModule
→ OvidSummonPositioningModule
→ OvidSummonAttackModule
→ WeaponBehaviour.Damage
```

当前 Summon Init 和 Damage 仍进入已禁用的 legacy API，没有形成 Native 攻击闭环。具体修复目标：

1. `SummonAttackBehaviour.Update` 与 `SummonAIBehaviour.Update` 同时驱动 OnUpdate；改为单一 Owner 驱动。`new CheckCooldown` 绕开基础许可检查，必须接回现有 CanAttack/生命周期边界。
2. `SummonAttackBehaviour.LastAttackElapsedTime` 的 getter 从 Time.time/timestamp 计算，setter只写另一个字段，现有 RestoreCooldownRemaining 无法正确恢复。应与现有网络冷却截止时间保持一致。
3. `OvidSummonAttackBehaviour`、Idle、Positioning 使用 GameDirector.Instance.Player；改为绑定拥有该武器的玩家。
4. `Gameplay/Combat/Helpers/AIHelpers.cs` 使用 EnemyAIManager.Instance.EnemiesOnScreen 和 BaseEnemyController。使用现有网络敌人代理的轻量查询入口替代，不恢复旧 EnemyAIManager。Boss 的旧 `ID == -1` 如何映射当前定义尚待验证。
5. 每次实际扫射创建一个 Native AttackSnapshot/root；持续命中持有该快照。待机、跟随、茧生命周期与单次攻击root分开。
6. SummonAIBehaviour.Dispose 为空；Idle取消后仍可能执行 ExitInstant；Attack没有完整销毁清理。必须在换元素、归池、移除、失权和销毁时取消任务、HitBox回调、FMOD、速度与旧FSM通知，阻止跨Owner复用。
7. `Gameplay/Combat/Data/Cards/WeaponData.ValidateNativeGas` 要求 ProjectileCount至少1，而源为0。迁移应显式规范为“一个召唤物”，保留实际单宠物语义并记录差异。
8. 现有 PlayerRuntimeCheckpoint 不含茧进度。需保存与对局时钟一致的成熟截止时间；断线继续推进，重连不重播已结束的茧等待或旧攻击root。

最小表现契约应表达拥有者、武器、宠物生命周期身份、阶段、位置/朝向、单次攻击身份及终止。Remote 不运行找敌、伤害碰撞或 GAS。无需给每个宠物视觉节点添加 NetworkIdentity。

## Ultimate 当前状态和入口

`Gameplay/Combat/Player/PlayerMovement.cs` 目前只有 `_ultimateCharge` bool、GainUltimateCharge、ResetUltimateCharge 和 UltimateAction；输入已通过 BoundPlayer 找到本机玩家，但后续依赖全局事件和 ControllerManager。

未发现可直接复用的 Native PlayerUltimateRuntime、服务器 charge 许可或恢复字段。`NetworkCombat/Server/RunSession.cs` 的 PlayerRuntimeCheckpoint 当前保存 Build、Progression、Health、Statuses、WeaponCooldowns、LifeState，没有 Ultimate charge。

```text
Scenes/SceneLoaders/UltimateAttackLoader
→ PlayerHand.Instance / GameDirector.Instance.Player
→ UltimateAttackManager
→ FindFirstObjectByType<UltimateAttackController>
→ 全局输入切换、暂停、视频预热、Timeline
→ UltimateAttackWeaponBehaviour.Init(uint.MaxValue)
→ legacy Modifier / legacy Attack
```

旧 Manager 的场景卸载订阅没有对应解绑；Controller 等待视频预热没有取消边界；Dante 任务取消后不能保证全局慢动作、无敌及 Loot 拉取状态恢复。不能将这些类整体作为新多人入口启用。

## 最小 Ultimate 代表：Dante

| 项目 | 源资产或参数 |
|---|---|
| 定义 | `MonoBehaviour/UltimateData_Dante.asset`，旧ID0，Damage100、CritRate0、Duration1、ProjectileCount1 |
| 主行为 | `GameObject/Ultimate AttackDante.prefab`，DanteUltimateAttack |
| 主动画 | `AnimatorController/Ultimate AttackDante.controller`、`AnimationClip/DanteUltimate_BaseAnim.anim` |
| 两道伤害波 | `GameObject/Fire circle_Ultimate.prefab`、`AnimatorController/DanteUltimateRingController.controller`、`AnimationClip/DanteUltimateWave.anim` |
| 生成时间 | 主动画 t=0 和 t=2.1166666 分别 SpawnWave(1/2) |
| 波结束 | 每道波在2.5秒触发 onAttackAnimationEnd；clip总长2.5166667秒 |
| 主片段长度 | 4.0333333秒；根与波的 Animator 都为 m_UpdateMode2（UnscaledTime） |
| 其他玩法参数 | KnockbackRadius5、Invulnerability3秒、BurnStrength0.1、BurnDuration4秒、BurnRate0.5秒 |
| 本地旧效果 | SlowMo1秒、相机震动、全局Loot拉取暂停；这些全局调用不可迁入多人玩法 |

尾波约在主动画开始后4.6167秒结束，晚于主动画，也晚于旧代码3秒无敌定时完成时清除 isAttacking 的时刻。新攻击root必须等待所有波及派生攻击租约结束；不得用 isAttacking 或主片段长度提前注销。动画开始切换及暂停会影响实际墙钟时长，最终应按真实事件和租约验收。

Burn应使用现有 `GAS/Core/Concrete/OnHitBurnModifier.cs`，稳定ID `0x02000001`：chance1、damageMultiplier0.1、numberOfHits8、hitIntervalDuration0.5。两波重复施加沿用现有状态刷新语义，不另建Burn状态或假定两组tick简单相加。原 Knockback 直接调用 BaseEnemyController.BruteforceKnockBack，必须进入现有 Native/网络结果路径。

Dante 的资产引用比 Butterfly 完整。正式 UI `DanteUltimateAttackAnimations.prefab` 不属于本轮完整攻击闭环的前置。Ovid Ultimate 额外包含多次治疗、禁用PlayerHand、Rigidbody锁定、Shrine应用和NoMovementController，扩大为它会引入不必要的玩法/流程迁移。

## Ultimate 必要实施边界

- 增加最小 per-player ability/charge 状态与服务器 grant/consume/恢复许可，复用已有GAS实例化及服务器AttackRegistry。此能力不占用四格普通武器手牌，也不建立另一套Buff或CombatPipeline。
- 当前 NetworkWeaponCombatAdapter 准入按普通手牌槽位/武器定义检查；需要清晰的能力准入入口和稳定非零网络Ability ID。旧定义ID0和所有Ultimate共用uint.MaxValue不能不加区分地沿用。
- Owner 从统一请求入口触发，服务器拒绝无charge、重复消费、旧连接epoch、非法生命周期请求。消耗后的重连不能退款或重复执行旧波。
- 无敌使用服务器认可的独立原因/期限，避免大招结束清除升级、复活等其他无敌来源；中断、失权、销毁均能释放本次许可。
- Native 波使用固定快照和幂等租约结束；Remote 重放实际扩张波和阶段事件，永远不运行碰撞伤害。Dedicated Server 不实例化Animator、VFX或UI。
- 测试可通过服务器grant入口授予一次charge；UltimateItem、完整掉落、正式UI/视频/全局暂停与GameFlow留在既定后续阶段。

## Phase 2 验收准备

Summon：真实三变体资产、单次Owner驱动、茧/出生/攻击/冷却完整流程、真实持续碰撞→Native GAS→服务器结果、Remote阶段与姿态、换元素/Owner/失权清理、离线茧时钟和普通冷却恢复。

Ultimate：一次合法charge产生两道真实波、Burn沿用现有状态链、结果均来自正确Owner及root；无charge/重放/旧epoch被拒绝；尾波结束前许可仍有效；中断后无旧root/无敌残留；重连保持已消耗charge。

两族都需要 Boot → Host + 独立Client，以及 server-only + 两Client 的进程验收。server-only Windows Development Player 不等同于 UNITY_SERVER 平台构建或 Steam 双账号实测，报告中应区分。
