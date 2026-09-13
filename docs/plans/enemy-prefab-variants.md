# 联网敌人 Prefab Variant 迁移

## 资产关系

```text
NetworkEnemyBase.prefab
└─ NetworkEnemySkeleton.prefab
   └─ NetworkEnemySkeletonExample.prefab
```

三个资产均在 `Assets/_Project/Content/NetworkCombat`。Skeleton 由原 `Enemy_Skeleton` 迁入后的现有联网资产转换，没有重新导入旧敌人。父资产不引用骷髅专属动画或近战资源。

| 资产 | GUID | Mirror assetId | 默认接触伤害 | 主动攻击 |
| --- | --- | --- | --- | --- |
| Base | `55ff35e83fff4c74ea6f8f2e82883aa2` | 2503929215 | 开 | 空 |
| Skeleton | `e9b6d6b03e4b1e341a17dcc0618c749f` | 2821354368 | 关 | EnemyAttackMelee |
| SkeletonExample | `c3ef8ee2a3373da498d6d7c2c6d014db` | 3884460939 | 关 | 继承 Skeleton |

Base 与 Skeleton 原路径、GUID 和网络生成身份保留。Skeleton 根对象 local fileID 从 `642612462740718118` 变为 `3963200209637973719`，已重绑 Boot 注册、Sandbox 注册和 Sandbox 骷髅夹具引用共 3 处；不能仅凭 GUID 不变判断引用安全。

## 行为与覆盖

公共 EnemyController、生命/GAS、移动、网络、动画播放器与绑定入口来自父资产。Skeleton 增加 EnemyAttackMelee、NetworkEnemyMeleeReplica，以及自己的调色、阴影、音效和挂点；保留必要物理、属性、动画覆盖。

- Base：生命 100，伤害 100，速度 2；`productMovementOnly=true`，不创建攻击 FSM。
- Skeleton：生命 2000，伤害 10，速度 6，攻击距离 1.75，冷却 0.5 秒；刚体质量 20、线性阻尼 10；`productMovementOnly=false`。
- Skeleton 有效阶段时长仍为预警 0.57 秒、生效 0.08 秒、恢复 0.29 秒，继续使用现有脚本时间与动画时长的计算方式。
- Example 仅额外配置基础生命 2400、Sprite 视觉缩放为 Skeleton 的 1.1 倍。它有自己的名称与生成身份，碰撞、近战和接触伤害设置继承 Skeleton。
- Unity 自动保存的根 Transform 默认 Override 不代表增加了一套行为配置。

Skeleton 当前有 130 条序列化属性 Override，主要是根名称和生成身份、属性与攻击配置、刚体及碰撞形状、动画/音效/Renderer 引用、骨骼视觉与挂点，以及关闭接触伤害。新增近战与远端适配组件属于 Variant 的 Added Components。公共控制器、生命/GAS 和网络组件没有复制。完整逐条记录在 `Logs/EnemyVariants/variant-overrides.json`。

Example 只有 16 条：Unity 根 Transform 默认项 10 条、名称 1 条、独立 assetId 1 条、生命 1 条、Sprite 缩放 3 条。

## 接触伤害

`EnemyContactDamage` 是普通组件，不继承 EnemyAttack，也不持有生命或 FSM。它引用父资产的 AttackCollider / PlayerDamageInteraction，绑定当前实例的 EnemyStats，并控制该专用节点。父资产移除了 ThornsEnemyAttack 的组件实例，主动攻击槽为空；旧 Thorns 类仍保留供其他资产使用。

`Contact Enabled` 是配置开关；网络初始化、存活状态和有效模拟分配是运行门控。观察客户端仍只检测自己的玩家，Dedicated 不执行玩家接触判定。关闭时先丢弃待处理碰撞，不触发停用补结算。该开关不受 `hasAttackAnimation` 或 `alwaysAttacking` 决定。

直接停用整个敌人时，不依赖父子组件 `OnDisable` 的调用顺序：接触交互自身也丢弃待处理碰撞。主动近战交互保留原来的末帧结算行为，避免攻击窗口结束时漏掉合法命中。

Skeleton 原先没有常驻接触伤害，所以正式资产默认关闭。需要接触＋主动攻击的变体时，打开 Contact Enabled 即可，其他攻击组件继续沿用。不同机制合法命中仍遵守玩家原有无敌与受击间隔；同一交互结算批次按玩家所有者去重。

## 制作与工具

1. 制作同逻辑骷髅：从 Skeleton 创建 Prefab Variant，修改属性、视觉或 EnemyAnimator 中的动画引用，保留公共组件继承。
2. 制作其他敌人：从 Base 创建 Variant，配置表现和物理；需要主动攻击时添加现有适用的 EnemyAttack 组件，绑定 EnemyController.attackScript，按对应联网实现配置模拟开关与适配组件。
3. 接触伤害使用 EnemyContactDamage 的开关，不通过停用 HurtBox、Collider 或整个敌人来关闭。
4. 网络敌人必须有独立的 Prefab GUID/assetId，并加入生成注册列表；每个实例只能有一个 NetworkIdentity、EnemyController 和 CombatantBehaviour。
5. 新远程攻击联网、Boss 阶段与多技能选择属于后续任务；本次没有将旧攻击脚本自动变成可联网攻击。

`Migrate Enemy Prefab Variants` 和原 `Migrate Enemy Simulation Prefabs` 入口共用迁移实现。已完成迁移的资产不再重建或覆盖有效动画、属性、物理配置；只补齐缺失的必要绑定。现有 Boot/Gameplay、Sandbox 设置入口保留已配置场景，只校验关系及增补敌人注册，保留其他配置与覆盖。

工具不会替用户保存其他资源；存在未保存的场景时，在开始迁移前停止并提示先保存场景，避免切换场景丢失内容。

## 修改清单

| 范围 | 文件与改动 |
| --- | --- |
| 接触能力 | 新增 `EnemyContactDamage.cs`；仅管理接触交互节点和当前实例属性绑定 |
| 生命周期与门控 | `EnemyController.cs` 支持空主动攻击槽；`NetworkEnemySimulationAgent.cs` 独立门控接触能力 |
| 回调去重 | `PlayerDamageInteraction.cs` 按本地玩家所有者合并同批回调 |
| 资产 | 修改 Base、Skeleton；新增 SkeletonExample 及 meta；仅修改 Boot/Sandbox 中涉及的注册与引用 |
| 迁移与设置 | 新增 `EnemyPrefabVariantMigration.cs` 及验证/构建部分；更新原迁移器和设置工具，保留原菜单入口 |
| 自动验证 | 新增 EditMode / PlayMode Variant 测试；测试程序集增加 Editor 引用；交接夹具和运行器增加样例选择，夹具显式直达 Gameplay |

原 Thorns 类、GAS、伤害计算、主动近战时序、闪白/数字系统和模拟权协议均未替换。本轮未修改 Enemy_LustSinner 或 Boss 的业务实现。

## 验证记录

本轮产物目录：`Logs/EnemyVariants`。

- 迁移前 Boot/Gameplay 与交接 PlayMode：4/4 通过。
- 迁移后相关 EditMode：39/39 通过，含父配置传播、直接父资产、动画绑定、身份、缺失绑定修复不覆盖有效属性、未保存场景保护，以及重复执行工具后文件内容不变。最终复跑结果为 `editmode-release.xml`。
- 迁移后相关 PlayMode：27/27 通过，含接触/近战组合、关闭能力/停用整个敌人/死亡时的待处理碰撞取消、重复回调、空攻击槽、实例隔离、闪白、数字、击退、状态及交接。结果为 `playmode-accepted.xml`。
- 原 Skeleton 与迁移后 Skeleton：719 项序列化属性及引用比较通过，差异为 0；对象 local fileID 变化按节点路径、组件类型和序号映射记录。
- Unity Windows Development 构建通过：`Builds/EnemyVariants/EnemyVariants.exe`，最终日志 `build-accepted.log`。构建包含正式 Boot、Gameplay 及测试夹具；Host/Client 使用同一份构建。最终构建及运行时输入哈希为 `build-manifest.json`。

| 独立进程场景 | 实际结果 | 日志目录 |
| --- | --- | --- |
| 基础敌人 Host/Client | 通过；闪白、数字、DOT 和击退回归 | `Logs/M3Knockback/Host-20260912-164557-803-p8001` |
| Skeleton Host＋双客户端 | 通过；生成、移动、攻击各阶段交接、击退、断线重连、倒地/冻结及多敌人压力测试 | `Logs/EnemyHandoff/20260912-164557-normal` |
| Example 延迟网络 Host＋双客户端 | 通过；同一套交接回归，100ms 延迟、抖动及不可靠包丢失/乱序 | `Logs/EnemyHandoff/20260912-164704-impaired` |
| Dedicated＋双客户端 | 通过；闪白、数字、DOT、击退及来源断线接管 | `Logs/M3Knockback/Dedicated-20260912-164726-085-p8004` |

上述四轮所有进程退出码均为 0，逐端 PASS 标记存在，无未处理异常或断言失败；机器可读汇总为 `standalone-results.json`。交接压力测试每个阶段实际运行 8 秒，未宣称完成长时间稳定性测试。主动攻击交接夹具会延长阶段并临时提高生命以稳定抓取交接窗口；原始生命及 0.57/0.08/0.29 秒时序由资源比较、EditMode 和真实 FSM PlayMode 验证。

独立进程均以无画面模式运行，本轮未进行人工游戏画面/音效验收，也未保存有效游戏截图；不把这些运行作为人工视觉通过的证据。既有 FMOD 无声输出、ComputeBuffer 退出清理等警告仍可能出现在无画面日志中，未当作零警告运行。

早期迁移辅助脚本遍历序列化内部字段时出现 Editor 原生崩溃，相关失败日志保留。已改为仅进入数据容器、跳过 Unity 内部绑定，并在替换资产前释放旧内容。最终验收只以修正后执行结果为准。

其余失败尝试也保留：初次构建缺少现有菜单测试所需编译标记，修正测试构建定义后通过；首次交接进程等待 Gameplay 玩家超时，原因是当前准备房流程与旧夹具不匹配，夹具显式直达 Gameplay 后通过；新增场景保护测试曾因 Unity 不允许在未命名场景旁新建附加场景而失败，修正测试场景创建方式后 39 项全部通过。没有通过忽略异常来放行这些运行。

最后补充的生命周期测试复现了接触交互在整个敌人停用时补结算一次伤害（500 → 490）。原因是子交互可能先于父能力收到 `OnDisable`。已为接触交互关闭该补结算路径，主动近战保持默认行为；修复后完整相关 PlayMode 27/27 通过，失败记录为 `playmode-lifecycle.xml`。

`before/` 保存本轮开始时的相关文件；`baseline-files.json` 为哈希清单。`prefabs-before.json`、`prefabs-after.json` 记录数值、时间和身份，`references.json` 记录外部对象引用映射。

`changed-files.json` 和 `task.diff` 只记录本轮 25 个文件（含新增 meta）相对任务开始状态的变更，便于与工作区原有改动区分。Boot 场景仅有骷髅根引用重绑、样例注册两处改动；Sandbox 仅有两处骷髅引用重绑及样例注册，Gameplay 未修改。
