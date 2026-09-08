# 三选一升级入口：实现与验收记录

记录日期：2026-09-08。目标是最小 CardPickMenu 与逐玩家的服务器权威升级流程。
本记录区分已经执行的验证、代码检查和 Steam 环境的剩余人工步骤。

当前主流程已经通过 Host 与独立 Client 的实际 Boot → Gameplay 测试。
最新选择入口可用状态通知、组合停用恢复及已确认的 2 → 1 → 0 候选池规则均已通过双进程验证，两个进程正常退出。PlayMode 46/46、网络 EditMode 68/68 通过。
Steam 双账号连接仍需人工验证。此前有窗口测试的玩法断言全部通过，但进程退出时发生了原生插件崩溃，不能记为完整进程测试通过。

## HellMaiden 参考审计

参考根目录为 `F:/DecomplieLatest/HellMaiden/ExportedProject/Assets`。
只读取以下 prefab 与脚本，没有导入该目录的升级数据、美术资源或旧单机控制器。

| 参考文件 / 组件 | 追踪到的职责 |
| --- | --- |
| `GameObject/CombatUI.prefab` 中 `Card Pick Menu` | 三个候选位置、菜单容器与开关结构。 |
| `Scripts/Assembly-CSharp/AstralShift/HellMaiden/Player/Attacks/Leveler.cs` | 升级排队、`ProcessLevelUp`、发出选择触发、关闭后 `EvalLevelUp` 推进队列。 |
| `.../UI/CombatUIManager.cs` | 订阅 `ShowOfferingsScreen`，经 `LevelUpLogic` 打开菜单。 |
| `.../UI/Menus/UICardPickMenuView.cs` | 打开菜单、取得三选一数据、绑定卡片、选择与关闭。 |
| `.../Combat/Hand/CardPool.cs` | `GetCardsDrop` 和原候选池规则，属于玩法数据。 |
| `.../UI/Cards/CardVisualsFactory.cs` | 创建卡片视觉与输入处理器，属于展示。 |
| `.../UI/Cards/UIEquipmentCardViewHandler.cs` | `Equip` 回调到手牌槽。 |
| `.../UI/Menus/Hand/PlayerHandSlotView.cs` | `PickCard` 与槽位操作；`AddEquipment` 最终修改旧 Hand。 |
| `.../Controllers/CardPickMenuController.cs` | 旧菜单控制，包含全局 `PauseGame` / `ResumeGame`。 |

旧调用链为：

```text
Leveler 升级队列
→ ProcessLevelUp
→ GameEvents.ShowOfferingsScreen
→ CombatUIManager.OpenCardPickMenu
→ UICardPickMenuView.Open
→ Leveler.CardPool.GetCardsDrop
→ CardVisualsFactory / 卡片输入处理器绑定 RuntimeCardData
→ PlayerHandSlotView.PickCard / UIEquipmentCardViewHandler.Equip
→ PlayerHandSlotView.AddEquipment
→ 关闭菜单
→ Leveler.EvalLevelUp
```

菜单、按钮、文字与输入回调是展示；候选池、等级、槽位和应用结果是玩法状态。
本次只参考三项展示及逐次关闭/推进方式；不复用旧单例、全局事件总线或暂停行为。

## MonsterSupergroup 接入依据

`PlayerBuildRuntime` 是当前网络玩家的原生 GAS Build。
`PlayerHand` 是旧单例；原生 `PlayerHandSlot` 的修改入口已经要求调用所属 Build。
仓库没有 `PlayerHandBehaviour` 类，旧 `PlayerLoader` 也不是当前 NetworkPlayer 初始化入口。
实际初始化由 `NetworkPlayerBootstrap` 执行，因此选择结果应用在现有 Build 上。

Boot 已引用 `NativeGasEquipmentDB` 的八张已转换 Equipment，以及只有初始 Dante 的 `NativeGasWeaponDB`。
Equipment 等级中已有稳定 Modifier ID 和类型化 Parameters；原 `LegacyEquipmentModifierConverter` 仍只在编辑器使用。
本次没有新增运行时 ID 映射，也没有调用会初始化旧存档/UI 的完整 `RuntimeDB.Init()`。
当前配置没有额外可选择的未拥有武器，因此候选来自已有 Equipment 的新增与下一等级。

当前网络玩家没有配置旧 `Leveler` 和 XP 曲线。
新增服务器入口使用服务器确认的敌人击杀及现有 `enemy.stats.XP`；XP 与等级保存在各玩家的网络组件。
NetworkPlayer 的 `experiencePerLevel` 暂配为 2，与当前场景每玩家一个 2 XP 敌人的最小入口相符。
这不是完整数值曲线迁移。服务器也可调用 `ServerGrantExperience(amount)` / `ServerQueueUpgrades(count)`；客户端没有任意加 XP 的 Command。

## 当前数据与应用流程

```text
服务器确认击杀 / ServerGrantExperience
→ 对应 NetworkPlayer 的 XP、Level、PendingUpgradeCount
→ EquipmentModifierOfferProvider.Generate(build)
→ 服务器记录本轮 EventId 与一至三个 OptionId
→ TargetRpc 只向该玩家 Owner 发送卡 ID / 等级 / 槽位
→ Owner 从本地 EquipmentDB 解析定义
→ ModifierSelectionController.ReceiveOffers
→ 本地 CardPickMenu 显示对应标题和按钮，隐藏空余位置
→ Select(index) / SelectOffer(optionId)
→ Command(eventId, index)
→ 服务器验证所有权、本轮身份、索引、未消费、当前 Build 适用性
→ PlayerBuildRuntime.AddEquipment / UpgradeEquipment
→ Owner 收到权威 Build 状态
→ 关闭本轮菜单，必要时进入下一轮
```

`NetworkModifierSelection` 的实例字段保存服务器候选、事件序号、初始武器引用、Build revision 和待处理数量。
事件与选项身份包含玩家 `netId` 和本玩家的递增序号。没有跨玩家共享的静态候选。
网络消息只有数值身份与等级/槽位，不发送 ScriptableObject、GameObject 或 UI 引用。

Host 与远端 Owner 都提交同一 Command，并进入同一个服务器验证及应用方法。
服务器为各玩家保留现有 `PlayerBuildRuntime`，远端玩家的服务器武器关闭自动执行，但原生 Modifier Runtime 仍存在。
远端 Owner 按权威槽位/卡片/等级状态通过现有 Build API 对齐；Host 共享服务器 Build，跳过重复应用。
其他观察客户端不接收候选，也不创建该玩家的执行 Build。

最终效果链保持为：

```text
PlayerBuildRuntime
→ ApplyEquipmentToWeapon
→ EquipmentDataModifier.CreateRuntime
→ RuntimeModifierFactory
→ GeneratedModifierRegistry
→ RuntimeEquipmentModifier
→ RuntimeEquipmentModifiers.Add
→ RefreshStats / CombatPipeline
```

升级使用已创作的下一等级替换旧等级，不把完整等级叠加。
Build 先尝试创建新等级，失败沿用回滚，成功后移除旧等级并消费旧 handle。
容量和应用顺序保留；原 Modifier 工厂、注册表、执行语义及 CombatPipeline 没有重写。

## 候选、展示与生命周期

候选提供器根据当前初始武器 Build 筛选，再均匀抽取至多三个不同卡片；只剩一至两个时全部提供。
检查数据库/卡 ID、等级、注册信息、精确参数类型、武器 `Supports`、槽位、容量及拥有状态。
未拥有卡使用第一级；已拥有卡使用下一等级；满级卡排除。
当前入口排除不支持的依赖或多槽位定义，避免绕过规则。组合卡保留该等级的全部 Modifier。
生成候选不创建 Runtime Modifier。

`ModifierSelectionController` 只维护候选展示与提交状态，不随机、不修改 Build。
UI 与开发键盘 1/2/3 共同调用 `Select` / `SelectOffer`。
`IsRequestPending` 阻止重复输入，成功返回表示“请求已提交”，实际完成以服务器状态为准。
候选名称优先使用 `GetTitle()`，未解析出文本时回退到现有 `Equipment.Title`，没有复制另一份显示数据库。

`LocalPlayerUIBinder` 只绑定 `NetworkClient.localPlayer` 且具有所有权的玩家。
CardPickMenu 使用本项目已有 TMP 字体/材质、纯色 Image 和三个 Button；显示绑定与 HP HUD 独立。
按钮与菜单状态不做网络同步。事件解绑、菜单停用、组件销毁会清理展示引用与监听。

最新代码增加 `IsPresentationReady`、`PresentationReady` / `CancelRequested` 通知及服务器 `ownerCanSelect`。
菜单实际绑定并订阅后调用 `NotifyPresentationReady()`，明确告知服务器此时可接收候选；绑定前新得到的升级暂缓生成，玩家保持常态。
菜单解绑/停用时调用 `CancelOffer()` 清除就绪标记并通知服务器取消当前候选、解锁，再解除展示订阅。
恢复网络组件或 Selection facade 时会携带实际就绪值请求状态，不能在菜单仍停用时自行重新锁人。
入口不可用期间新得到的待处理升级暂缓生成；菜单重新绑定并发出就绪通知后继续。
最新双进程日志分别记录三类组件的 `disabled-xp-resume-verified`，包含菜单保持停用时重启另外两个组件的组合路径。
Build 的 `Bind` / `Unbind` 不代表菜单就绪；主动关闭展示入口应调用 `CancelOffer()`，重新开放由菜单绑定调用 `NotifyPresentationReady()`。

有效选择清理本轮身份后再回执/进入下一轮。
失去所有权、断线、Build/初始武器失效、选择组件停用、菜单停用和场景销毁都安排了取消与解锁路径。
取消不额外添加或移除已有 Equipment。连续升级通过每玩家一个待处理数量依次出牌，仅使用一个菜单。

### 有限池边界：已确认按有效数量展示

现有初始武器仅有三个 Equipment 容量位。三个已拥有卡中出现满级后，即可能不足三个适用候选，尽管还有其他卡可升级。
用户已明确确认：不足三个时允许展示 1–2 个有效选项。没有有效候选时，当前实现保留待处理升级且保持玩家可行动。
不重复补齐、不展示无效项、不发明新奖励。零候选时重试适用性；如果 Build 或数据始终不变，升级会一直等待。

该后续确认明确调整了原“每次恰好三个”的边界：充足时三个，不足时按实际有效数量展示，零候选时不打开菜单。
Provider、Selection facade、网络消息验证和菜单空闲按钮已同步修改。最新单元测试及真实入口的 `reduced-pool-2-1-0-verified` 已覆盖该规则。

## 选择期间的逐玩家限制

`PlayerMovement.SetUpgradeSelectionLocked` 清空方向/速度、冻结该玩家刚体位置，阻止移动、冲刺、终极技与交互。
武器攻击入口读取相同标记；解除时恢复此前刚体约束。
`CombatantBehaviour` 的独立选择无敌状态阻止直接/状态伤害，并保留其他无敌来源。

服务器 `CombatLedger` 保存独立的选择标记，拒绝选择者新提交的攻击和状态施加，保护生命报告并发送权威修正。
选择期间被拒绝的战斗事件会记为已处理，避免解锁后重放。
`NetworkPlayerTransformReliable` 保持 Mirror 协议解析，但选择期间丢弃 Owner 位置更新及服务器缓冲快照。
投射物展示适配器阻止新的生成边缘事件，仍接收终止事件。

限制不修改 `Time.timeScale`，不暂停世界或其他玩家。
已存在状态、敌人和世界时间继续推进；选择者在此期间到达的命中提交也会被服务器拒绝。

## 文件变更清单

以下路径均相对 `F:/UnityStore/MonsterSupergroup`。新增脚本均附带 Unity `.meta`。

| 类型 | 文件 |
| --- | --- |
| 新增本地菜单 | `Assets/_Project/Gameplay/Combat/UI/CardPickMenu.cs` |
| 新增网络选择与移动限制适配 | `Assets/_Project/NetworkCombat/Mirror/NetworkModifierSelection.cs`、`NetworkPlayerTransformReliable.cs` |
| 新增测试 | `Assets/_Project/Tests/EditMode/NetworkCombat/UpgradeSelectionCombatGateTests.cs`；`Assets/_Project/Tests/PlayMode/Gameplay/CardPickMenuTests.cs`、`EquipmentUpgradeRuntimeTests.cs`、`UpgradeSelectionLockTests.cs` |
| 新增记录 | 本文件及 `.meta` |
| 修改候选/Build/接口 | `Assets/_Project/Gameplay/Combat/Native/EquipmentModifierOfferProvider.cs`、`ModifierOffer.cs`、`ModifierSelectionController.cs`、`PlayerBuildRuntime.cs`、`DebugModifierSelectionInput.cs`、`ModifierSelection.md` |
| 修改本地玩法限制 | `Assets/_Project/Gameplay/Combat/Player/PlayerMovement.cs`、`Player/Attacks/WeaponBehaviour.cs`、`Player/Attacks/ProjectileAttackBehaviour.cs`、`Runtime/CombatantBehaviour.cs` |
| 修改网络绑定/生命周期/展示 | `Assets/_Project/NetworkCombat/Mirror/LocalPlayerUIBinder.cs`、`NetworkPlayerBootstrap.cs`、`NetworkCombatWorld.cs`、`NetworkCombatantAdapter.cs`、`NetworkWeaponCombatAdapter.cs` |
| 修改服务器验证 | `Assets/_Project/NetworkCombat/Server/CombatLedger.cs`、`ServerCombatGateway.cs`、`ServerStatusRegistry.cs` |
| 修改 prefab 与程序集配置 | `Assets/_Project/UI/CombatUI/CombatUI.prefab`、`Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab`、`Assets/_Project/NetworkCombat/MonsterSupergroup.NetworkCombat.asmdef` |
| 修改测试/运行工具/说明 | `Assets/_Project/Tests/PlayMode/Gameplay/ModifierSelectionTests.cs`、`ModifierSelectionProcessProbe.cs`、`PlayerHealthHUDTests.cs`；`Assets/_Project/Tests/EditMode/NetworkCombat/NetworkCombatPhase7Tests.cs`；`Tools/Run-ModifierSelectionProcessValidation.ps1`；`Assets/_Project/NetworkCombat/README.md` |

HP 原子树保留。回归验证发现原 prefab 开启了缺少组件/动画片段的闪烁动画，因此改用已有颜色闪烁路径；最小填充值改为 0，确保死亡为空条。
HP 测试分别验证即时状态、订阅清理和动画上下层收敛。

已有未提交的 `GameDirector.cs.meta`、`Player.meta`、`Pooling.meta`、`QTI.meta` 状态保留，未纳入本次实现。
Unity 测试生成的四个 PerformanceTestRun 资源已恢复原内容，没有作为功能改动保留。

## 已执行验证

Unity 版本：6000.3.17f1。测试项目使用 `F:/UnityStore/MonsterSupergroup_clone_0`，Assets/ProjectSettings 指向主工作区，Library 独立。
最新功能源码通过下列自动测试和构建；有窗口输入记录保留为早期人工交互证据，未把它计作最新完整进程结果。

| 验证 | 可查证结果与范围 |
| --- | --- |
| PlayMode：`Logs/UpgradeSelection-PlayMode.xml` | 46/46 通过，结束于 2026-09-07 17:25:11 UTC（本地 09-08 01:25）。覆盖 Selection 意图、候选、Build 升级/失败回滚、按钮、锁与 HP 回归、既有 Dante GAS；包括最新 1–2 项 Provider/菜单与就绪状态测试。 |
| EditMode：`Logs/UpgradeSelection-EditMode.xml` | 4/4 通过。覆盖服务器位移丢弃、攻击/状态拒绝与重放、生命修正、玩家独立性和其他无敌来源保留。 |
| 网络回归：`Logs/UpgradeSelection-NetworkRegression.xml` | 68/68 通过，结束于 2026-09-07 17:26:04 UTC（本地 09-08 01:26）。旧 Phase7 断言已按仓库现有场景路径、Base 敌人 prefab 和嵌套起始点结构修正；生产场景没有为测试改动。 |
| 构建：`Logs/UpgradeSelection-Build.log` | Unity Development / IncludeTestAssemblies 构建 `Build Finished, Result: Success`，退出码 0；结束于本地 09-08 01:30。 |
| 最新自动双进程：`Logs/ModifierSelectionProcess/20260908-013035/host.log`、`client.log` | 两端 `result=PASS`，运行器退出码 0。实际 Boot → Gameplay、真实 Button 回调/RPC、同帧独立候选、两轮连续选择、拒绝非法/旧/外人/重复事件、生产敌人击杀获 XP、三类组件停用后 XP 暂存/恢复、组合停用恢复、候选 2 → 1 → 0、实际网络命中与 Client 重连。 |
| 有窗口双进程：`Logs/ModifierSelectionProcess/20260907-203313/host.log`、`client.log` | 实际鼠标点击与键盘 2/3 已执行，界面显示可读卡名。两端全部玩法断言 `PASS`，Client 命中记录实际伤害 20，验证伤害卡从基础 15 生效。进程退出阶段原生 AppUINativePlugin 崩溃，运行器为非零退出，不能记为进程测试整体通过。 |
| 首次复跑诊断：`Logs/ModifierSelectionProcess/20260908-012755/host.log`、`client.log` | 三类中断检查通过后，夹具中的闲置 Host 被敌人击杀，后续等待超时。调整中断/候选耗尽时段的测试保护后，以上最新双进程完整通过；此失败运行不计为验收通过。 |

原生效果测试验证伤害等级按绝对值 20 → 23 → 30 更新，并调用 `BeginAttack` / `ResolveHitDetailed` 检查命中。
组合卡两个 Modifier 保留；非法注册导致升级失败时不残留替换效果。
自动双进程测试使用真实 Gameplay 场景和现有 KCP 开发后端，而不是直接实例化 UI prefab 绕过加载入口。

双进程夹具仅在包含测试程序集的构建启用。
为了让两端加载速度不影响同步断言，等待开始期间对测试玩家提供保护，并把背景测试敌人的生命提高；另生成使用生产数据的敌人验证击杀 XP。
中断与 2 → 1 → 0 候选池检查期间也临时保护闲置 Host，完成该时段后移除；伤害/无敌恢复由独立用例验证，不能用这一测试保护作为恢复验证的证据。
这些设置不修改生产场景资产。过程中的选牌与效果校验走实际项目组件、网络和 Build。

日志仍包含既有生产敌人击退 FSM 空引用、缺失 FMOD 事件以及材质问题；有窗口运行还触发了退出时原生插件故障。
这些不是本次新建升级数据或美术资产所引入，也不在本轮迁移范围。
选牌与伤害断言通过不能推导为整个 Gameplay 无异常。

## 18 项验收对应证据

| 项 | 结论与证据 |
| --- | --- |
| 1. 最小三选一 prefab | 已实现，prefab 结构测试及有窗口实际点击通过。 |
| 2. 服务器候选数量 | 正常三个候选路径通过；用户已批准不足时显示 1–2 项。Provider/菜单测试及真实入口 2 → 1 → 0 流程通过，零项保留待处理且不锁人。 |
| 3. 使用本项目数据 | Provider 读取现有 DB；没有导入 HellMaiden 卡数据。 |
| 4. 服务器生成 | 只在服务器网络组件调用随机生成，Owner 解析收到的 ID。 |
| 5. 各玩家独立 | 双进程同时升级验证独立 EventId、候选、Build。 |
| 6. 仅 Owner 显示 | TargetRpc + 本机 Owned Binder；双进程菜单绑定检查及实际窗口检查。 |
| 7. 世界继续运行 | 运行测试检查时间推进，服务器门禁用例检查其他玩家继续战斗。 |
| 8. 不能移动/攻击 | 本地输入/刚体/武器测试与服务器位移、攻击拒绝测试通过。 |
| 9. 选择期间无敌 | 本地直接/状态伤害与服务器生命报告修正测试通过。 |
| 10. 权威验证结果 | 同一 `ServerSelect` 检查连接、本轮身份、索引、当前资格。 |
| 11. 重复/旧/非法不能获益 | 双进程拒绝测试、Selection 单元测试及服务器重放门禁通过。 |
| 12. 正确现有 Build | 原生升级测试、Owner 状态对齐及实际网络命中通过。 |
| 13. 恢复常态 | 有效选择、组件中断、就绪状态用例通过；最新双进程通过三类入口停用后 XP 暂存/恢复及菜单关闭时组合重启的检查。直接/状态受伤恢复由独立限制用例验证。 |
| 14. Host/Client 同一逻辑 | 都经过 Command → `ServerSelect`；Host 仅省略副本重复应用。 |
| 15. 无全局暂停 | 改动代码未引入 timeScale 赋值/全局暂停，世界推进断言通过。 |
| 16. 无第二套 Build | 扩展现有 Build 的查询/升级入口，效果仍进入原 GAS 链。 |
| 17. 无不必要源资源迁移 | 新 UI 仅项目已有字体、Image、Button；变更清单无源美术/数据导入。 |
| 18. 保留其他功能/未提交改动 | 已有 HP/Dante 测试及网络全套 68 项回归通过，用户四个 meta 状态保留，生成测试资源已恢复。GAS Core、既有内容数据与 Boot/Gameplay 场景文件没有本次改动。 |

## 剩余验证与人工步骤

1. 使用两个已登录的 Steam 账号，经正常 Boot 入口建房/加入；双方各杀敌升级，并在同时升级时分别选择，检查 Owner 菜单、限制、伤害同步、断线重连。
2. 在 Steam 路径核对每端只有一个 CombatUI，正常选择后恢复行动；选择过程中断开、重建玩家及退出 Gameplay 不残留 UI/限制。
3. 若要复核实体输入，使用运行器的 `-Keyboard` 模式按提示点击按钮或按 1/2/3；已知原生插件退出故障仍应单独排查，不能将玩法断言通过视为该故障已修复。

Steamworks.NET / FizzySteamworks 的后端选择及既有传输保持不变。KCP 的通过结果覆盖相同 NetworkBehaviour/Command/TargetRpc 逻辑，但不能替代 Steam 登录、大厅发现与 Steam 传输的双账号验证。
