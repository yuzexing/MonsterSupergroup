# Boot to Gameplay Multiplayer Combat Loop

## 2026-09-10：M2 ProCamera2D 多人适配

M2 在正式 Gameplay 原相机上接入 ProCamera2D、Shake、Numeric Boundaries 和 Zoom To Fit Targets，沿既有 Owner 输入绑定生命周期清理与重绑。用户确认 Ground=100×100 为地图范围，并批准本地实际受伤触发 PlayerHit。配置映射、源场景路径差异、验证证据及人工步骤见 [M2 移植记录](m2-procamera2d-multiplayer.md)。下方 M1 和其他切片保留为历史记录；M3–M6 不变。

## 2026-09-09～10：M1 怪物 Debug 列表

当前状态：**实现完成／等待人工验收**。仅实施 M1，未推进镜头、击退、升级、选卡玩法或波次。以下自动验证通过不代表人工验收已完成。

### 本轮基线与接入

- 分支 `master`；开始时仅有 Gameplay 下 `GameDirector.cs.meta`、`Player.meta`、`Pooling.meta`、`QTI.meta` 四个 `AD` 状态，索引新增、工作区不存在。保留这些状态，不提交、不清理。
- 当前实际入口是 `Assets/_Project/Scenes/Boot.unity` → additive `Assets/_Project/Scenes/Gameplay.unity`。
- Gameplay 的刷怪器使用 `NetworkEnemyBase.prefab`；NetworkPlayer 初始武器是 ID 6（Circling）。下方原有 Skeleton / Projectile 说明保留为历史切片，不能替代当前资源配置。
- 新组件挂在现有 CombatUI 根上，位于 HP 与 CardPickMenu 子树之外；不修改 Scene，不新增 Demo，不运行重建生产 Prefab 的工具。

### 文件与调用链

| 相对本轮基线的文件 | 作用 |
| --- | --- |
| `Assets/_Project/NetworkCombat/Mirror/NetworkEnemyDebugPanel.cs` 及 `.meta` | 本地 IMGUI 面板、只读行快照、死亡展示缓存、生命周期订阅。 |
| `Assets/_Project/UI/CombatUI/CombatUI.prefab` | 根上挂载一个面板，引用现有选卡菜单以避免遮挡。 |
| `Assets/_Project/Tests/PlayMode/Gameplay/NetworkEnemyDebugPanelTests.cs` 及 `.meta` | Host 下验证权威/预测、状态层数、缺失数据、成员筛选、死亡缓存和清理。 |
| `Assets/_Project/Tests/PlayMode/Gameplay/GameplayHealthHUDLoadingTests.cs` | 实际加载正式 Gameplay，检查单实例、正确场景归属及卸载销毁。 |
| `Assets/_Project/Tests/PlayMode/Gameplay/ModifierSelectionProcessProbe.cs` | 在已有 opt-in 双进程测试内增加正式敌人列表、菜单隔离、死亡和卸载断言；调整受控攻击等待及夹具保护以适应当前 Circling，不改变生产玩法。 |
| 本文与 `Assets/_Project/UI/CombatUI/README.md` | M1 进度、操作与证据说明。 |

```text
BootGameplayNetworkManager → additive Gameplay
  → GameplayUILoader → CombatUI / GameplayUIRoot → NetworkEnemyDebugPanel

NetworkClient.spawned → NetworkEnemySimulationAgent（仅真实已生成敌人）
  ├─ NetworkCombatWorld.Replica.TryGetEntity → canonical HP / MaxHP / Alive / version
  ├─ CombatantBehaviour → 本端 HP（可能含预测）
  ├─ StatusController.GetCanonicalStackCount → canonical 状态层数
  └─ Assignment / Authority → 模拟端、本端角色、目标与 Epoch
  → 每 0.2 秒生成只读展示快照 → OnGUI 绘制

Replica.EntityChanged（权威死亡）→ 2 秒纯展示缓存 → 移除
失联 / World 替换 / 禁用 / Gameplay 卸载 → 解除订阅、清空行与缓存
```

没有新增网络消息、Command、RPC、SyncVar 或可写战斗状态。面板不读取服务器 Ledger 来代替 Host 的客户端副本，不调用初始化、发奖、扣血、暂停或选择取消入口。`Rows` 为只读视图，行中的 canonical struct 是值拷贝；死亡缓存不持有 Unity 对象。

### 展示规则

- Editor / Development Build 默认展开；`F3` 或标题按钮切换；`F4` 仍属于原网络 HUD。失焦时不处理 F3。
- 右上角滚动列表按 netId 排序；明确区分 canonical HP 和可能含预测的本端 HP。
- canonical 快照、运行时或 Assignment 缺失时显示 `unavailable` 与原因；不能用本端 HP 或默认 Frozen 值猜测权威状态。
- 状态列表显示 canonical 副本层数，不把本端预测层数并入；不展开完整 AI 状态机。
- 已销毁敌人的死亡行标记 `Recent death - last observed data`，保留两秒；普通 despawn 不伪装成击杀。重复死亡版本不延长缓存。另保留最多两秒的已观察行快照，应对 Host 先销毁共享对象、后处理本地 canonical RPC 的顺序；只有收到权威死亡才会再次显示该行，其余字段明确是最后观察值。
- 选卡时面板自动只留标题，菜单关闭后按用户原展开意图恢复。后台只读快照仍更新；开关不改变选卡或输入锁。
- 无本地角色也可显示连接／等待状态；Dedicated Server 的既有 UILoader 跳过 UI，组件自身也有运行环境检查；普通非 Development Player 禁用面板。

### 检查与日志

定向测试、构建和启动器日志位于 `Logs/M1EnemyDebug/`；双进程原始日志位于启动器报告的 `Logs/ModifierSelectionProcess/<时间戳>/host.log`、`client.log`。测试使用 Unity 6000.3.17f1 和既有 `MonsterSupergroup_clone_0`；其 Assets、ProjectSettings 是指向本项目的 junction，实际编译的是本轮代码与正式资源。

已执行最终定向 PlayMode 测试 **26/26 通过，退出码 0**：新面板 6 项、真实 Gameplay 加载 1 项、PlayerHealthHUD 9 项、ModifierSelection 8 项、UpgradeSelectionLock 2 项。以 `targeted-4.xml` 和 `targeted-4.log` 为准；新增项覆盖先销毁、后收到权威死亡的 Host 顺序。此前 `targeted-3` 的 25 项也全部通过。

前两轮失败留档：`targeted-1` 为新增测试夹具在 NetworkIdentity 缓存组件之后才加模拟 World；`targeted-2` 为测试状态快照未设置合法 TotalTicks。均仅修正新增测试数据/构造，不修改正式网络或 GAS 行为。

双进程前两次失败也保留：`process-1.txt` 对应 `20260909-234004`，既有测试只等待普通冷却，当前 Circling 被服务器以 `InvalidAttackRate` 拒绝；仅修正测试夹具为 `GetAttackSequenceDuration() + GetCooldown()`，沿用 `PlayerWeaponCooldownSnapshot.TryAdmit` 的规则。`process-2.txt` 对应 `20260909-234522`，受控击杀已无准入拒绝，但暴露了 Host 销毁先于本地 RPC 的死亡行遗漏，已在面板中修复并补上述测试。两次启动器均以超时退出，不能视为通过。

`process-3.txt`（`20260909-235052`）已验证正式 Host 的死亡行，但在后续命中检查超时。`process-4.txt`（`20260909-235502`）增加准入诊断，确认拒绝码为 `TargetCanonicalDead`：既有菜单测试末尾撤去夹具保护，测试玩家在等待持续武器间隔时被击倒。仅将既有测试保护保留到后续检查，不修改玩家伤害或升级逻辑。夹具同时读取已有服务器冷却快照，避免更换增益后按缩短的新持续时间过早开始下一次攻击；两次失败均留档。

`process-5.txt`（`20260909-235825`）进一步暴露了测试在取消候选后过早禁用武器：排队中的 `TargetReceiveState` 随后通过 `SetWeaponExecutionEnabled(true)` 恢复自动攻击，使受控命中收到 `InvalidAttackRate`。仅让夹具等待这条既有状态消息交付后再进入受控命中，并断言武器保持禁用；未修改 Owner 基线恢复或攻击准入行为。

双进程最终结果：**Host PASS、Client PASS，启动器退出码 0**。最终包见 `Builds/M1EnemyDebug/M1EnemyDebug.exe`，构建日志 `Logs/M1EnemyDebug/build-6.log` / `build-6.exit.txt`（退出码 0）。启动器日志为 `Logs/M1EnemyDebug/process-6.txt`，原始日志为 `Logs/ModifierSelectionProcess/20260910-000221/host.log` 与 `client.log`。真实 Gameplay 单面板、实际 EnemyBase、选卡隔离、权威死亡、受控网络命中、Client 重连及双方卸载断言全部走完；两端最后均有 `result=PASS`。

关键证据定位：Host 日志 `enemy-debug-canonical-death enemy=6`、`enemy-debug-unloaded`；Client 日志重连前后 `enemy-debug-row`、`reconnect-verified`、`enemy-debug-unloaded`。最终定向测试与最终包使用相同面板/Prefab 代码；其后的修正仅涉及 opt-in 进程夹具和文档。

测试范围说明：复用既有 opt-in ModifierSelection 验证器，实际运行 Boot、Gameplay、EnemyBase、Mirror、GAS 和菜单。既有夹具在内存中把常驻敌人 HP 提升到 100000、按测试阶段保护玩家，并通过受控 Native GAS 命中验证另一个默认 100 HP 的 EnemyBase 击杀；这些不是生产 Prefab 改动，也不能替代正常 Circling 碰撞与输入的人工验收。构建启用 Development、KCP 和 IncludeTestAssemblies；只有传入测试角色参数才自动启动该夹具。

测试框架删除了基线中原本干净的四个 `Assets/Resources/PerformanceTestRun*.json[.meta]` 文件，已仅恢复这些测试副作用；没有恢复或覆盖用户的四个 AD 路径。

最终 `git diff --check` 退出码 0；索引与本轮起始快照一致，用户四个 AD 状态保留。检查记录为 `Logs/M1EnemyDebug/final-worktree-check.txt`；关键源文件及构建程序集 SHA256 记录为 `final-artifact-hashes.json`。没有提交或暂存本轮改动。

复现命令（PowerShell；所有进程均以隐藏窗口运行）：

```powershell
$unity = 'D:\RealSoftware\6000.3.17f1\Editor\Unity.exe'
$project = 'F:\UnityStore\MonsterSupergroup_clone_0'
$logs = 'F:\UnityStore\MonsterSupergroup\Logs\M1EnemyDebug'
$filter = 'MonsterSupergroup.Gameplay.Tests.NetworkEnemyDebugPanelTests;MonsterSupergroup.Gameplay.Tests.GameplayHealthHUDLoadingTests;MonsterSupergroup.Gameplay.Tests.PlayerHealthHUDTests;MonsterSupergroup.Gameplay.Tests.ModifierSelectionTests;MonsterSupergroup.Gameplay.Tests.UpgradeSelectionLockTests'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList @('-batchmode','-nographics','-projectPath',$project,'-runTests','-testPlatform','PlayMode','-testFilter',$filter,'-testResults',"$logs/targeted-4.xml",'-logFile',"$logs/targeted-4.log")
$env:MODIFIER_SELECTION_VALIDATION_OUTPUT = 'F:\UnityStore\MonsterSupergroup\Builds\M1EnemyDebug\M1EnemyDebug.exe'
Start-Process $unity -WindowStyle Hidden -Wait -ArgumentList @('-batchmode','-nographics','-projectPath',$project,'-executeMethod','MonsterSupergroup.Gameplay.Tests.ModifierSelectionValidationBuild.Build','-quit','-logFile',"$logs/build-6.log")
& 'F:\UnityStore\MonsterSupergroup\Tools\Run-ModifierSelectionProcessValidation.ps1' -Executable $env:MODIFIER_SELECTION_VALIDATION_OUTPUT *>&1 | Tee-Object -FilePath "$logs/process-6.txt"
git diff --check
git status --short
```

### Unity 人工验收

1. Unity 打开 `Assets/_Project/Scenes/Boot.unity` 后 Play，在已有 KCP 面板选择 Host；直接运行 `Builds/M1EnemyDebug/M1EnemyDebug.exe`（不传 `--modifier-selection-role`，不通过自动测试脚本启动），以 `127.0.0.1` 和相同端口连接 Client。也可直接运行该包两次分别 Host / Client。各端应沿正式生命周期加载 Gameplay，场景下各有一份 `GameplayUIRoot`，根上各一份 `NetworkEnemyDebugPanel`。Steam 双账号需另行验收。
2. KCP 的 F4 面板可以收起；右上角应显示 Enemy Debug。核对实际 EnemyBase 的 netId，双方应看到相同敌人的 canonical 身份、HP 与版本；本端角色通常一端 ClientOwner、另一端 Replica。
3. 使用当前 Circling 战斗。canonical HP 经网络同步后收敛；本端 HP 可短暂领先。权威死亡后显示 Dead，网络对象销毁后的快照最多保留约两秒（加 0.2 秒刷新误差）。没有波次，敌人清完不会自动补怪。
4. 升级选卡打开时列表自动隐藏为标题；按钮和 1/2/3 仍可选。按 F3 不应关闭选卡、改变免疫、恢复移动或发放额外奖励。
5. 保持目标敌人存活时断开负责模拟它的 Client；Host 的列表应反映现有 ServerFallback / 新目标 / Epoch。原 Client 重连后只显示当前有效数据，不能看到旧死亡缓存或多份 UI。
6. 在 Inspector 禁用/启用面板，检查重绑；再停止 Host、重新开局，检查 Gameplay 卸载及重新创建只有一份面板。两个窗口分别测试 F3 焦点和列表滚动。

### 验收证据与边界

| 已批准 M1 验收项 | 本轮证据 | 尚未验证 |
| --- | --- | --- |
| 1. 真实 Boot、独立 Host / Client、单面板、实际 EnemyBase、模拟身份可对应 | 正式 Gameplay 加载测试检查根归属、单实例；双进程 `enemy-debug-row` 记录真实 netId、ClientOwner / Replica、Simulator / Target / Epoch。 | 两个可见窗口的人工阅读及布局检查。 |
| 2. Circling 攻击下 canonical / 本端预测标签明确，双方权威结果收敛 | 面板测试明确构造 canonical 80、本端 55 和 canonical Burn x2 / 本端有效 x1；双进程通过现有 GAS / Mirror 根准入和权威死亡路径。 | 正常 Circling 碰撞攻击同一敌人后，两个可见列表逐次对照 HP / version；受控命中不等价于此项完整通过。 |
| 3. 死亡行消失、无旧对象引用、隐藏恢复当前数据 | 两种销毁 / RPC 顺序、两秒过期、重复死亡、普通 despawn、隐藏期间数据更新均有 PlayMode 断言。死亡缓存只含值类型和字符串。 | 可见窗口中死亡行的可读性、滚动和 F3 手动操作。 |
| 4. 断线接管、重连有效对象、停止后卸载 | PlayMode 覆盖失联、World 替换和反复解绑；双进程重连后核对有效对象及单实例，Client 日志记录 enemy 5 为 ServerFallback / Target 2 / Epoch 2；双方卸载后面板为零。 | Host 可见列表的接管瞬间，完整手动重开局。 |
| 5. 菜单、按钮、1/2/3、Debug 开关不改奖励或锁 | 既有 ModifierSelection / UpgradeSelectionLock 回归通过；双进程 `enemy-debug-selection-isolated` 检查隐藏、候选不变、锁不变，随后走真实按钮与服务器确认。 | 真实键盘 1/2/3 与 F3 的焦点、点击遮挡和实际输入体验。 |
| 6. 必要回归与正式场景加载 | `targeted-4.xml`：26/26；包含初始快照、权威 / 预测、GAS 层数、成员筛选、死亡、解绑重绑和实际 Gameplay UI 加载 / 卸载。 | 未重跑仓库全部测试；未做大量敌人的性能测量。 |

Editor / Development 门控、Dedicated Server 的 UILoader 跳过及组件自身保护已做静态核对；独立普通 Release、`UNITY_SERVER` 构建与 Steam 双账号均为**未验证**。

### 遗留风险、后续与回退

- 双进程日志中出现现有 Circling 动画/音频链的 FMOD `EventNotFoundException`，事件 GUID 为 `{840d4d3b-6223-4aab-a508-f0bcd8e4de60}`；未修改音频资源，不能把功能断言通过表述为日志零错误。正常碰撞 / 动画表现留待人工验收。
- 成员采样间隔为 0.2 秒。Host 上从未被采样、便已销毁的瞬时敌人，或延迟超过短暂快照保留窗口的死亡消息，可能没有可补出的死亡行；不为这种情况扩充协议或猜测敌人身份。
- 状态展示是 canonical 副本层数；死亡行的其余字段是最后观察值。未展示完整 AI 状态机、位置轨迹或所有 GAS 内部字段。
- 人工验收后再决定 M1 是否最终完成。镜头、普通击退、武器候选、波次与 XP 拾取仍属于后续独立里程碑，本轮停止在 M1。
- 临时撤下功能可禁用 CombatUI 根上的 `NetworkEnemyDebugPanel`。完整回退只移除本轮挂载、新组件及对应测试/文档改动；不需协议或玩法数据迁移，不触碰本轮基线的四个 AD 路径。

## 原 Boot→Gameplay 战斗切片记录

## Goal

Starting from `Boot`, Host and Remote Client load `Gameplay` additively,
create exactly one `NetworkPlayer` per ready connection, spawn exactly one
`NetworkEnemySkeleton` per player, activate the owner-local Dante
`PlayerBuildRuntime`, and complete both canonical combat convergence loops.

1. Player -> Dante local projectile -> Enemy hurtbox -> `CombatResult` ->
   Server `CombatLedger` -> canonical Enemy HP.
2. Skeleton SimulationOwner melee -> local Player hitbox ->
   `PlayerCombatantBinding` -> `PlayerHealthReport` -> canonical Server Player
   HP.

## Required topology and authority

- `Boot` owns the Mirror manager and persistent combat/simulation worlds.
- `Gameplay` is additive content and owns player starts plus the product Enemy
  spawner; it does not own another manager or world.
- Mirror `NetworkIdentity` authority remains server-owned for Enemies.
- Gameplay `SimulationOwner` selects the one Client that runs each normal
  Enemy's movement and melee decisions.
- The attacking player's Client resolves Dante projectile hits immediately;
  the Server accepts `CombatResult` through the existing gateway and owns
  canonical Enemy HP/death.
- Each Player owner applies local incoming melee damage and reports final HP
  through the existing `PlayerHealthReport` path.
- Host and Remote Client each own an independent `PlayerBuildRuntime`.

## Non-goals

Do not expand this slice into Status ownership, Knockback/Pull, Boss simulation,
A*, network-spawned projectiles, `PlayerHand`, a parallel attack system, or
Legacy GAS damage/crit execution.

## Acceptance criteria

- Host and a late-joining Remote Client both enter additive `Gameplay`.
- One Player exists per connection and one Skeleton exists per Player.
- Each Skeleton targets and is simulated by its assigned Player owner.
- Both owner-local Dante builds damage their assigned Enemy and Server
  canonical Enemy HP converges.
- Skeleton melee damages each local Player and Server canonical Player HP
  converges through owner-final health reports.
- When the Remote Client disconnects, its Enemy remains, retains a cached
  snapshot, and changes to `ServerFallback` targeting the remaining Player.
- GAS EditMode, Gameplay PlayMode, NetworkCombat EditMode, and NetworkCombat
  Sandbox regressions pass.
