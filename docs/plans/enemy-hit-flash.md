# Enemy 受击闪白修复

## 行为

`InitNetworkMovementOnly` 初始化 `EnemyAnimator`，仍不创建攻击 FSM。
联网敌人通过拥有者 `ClientCombatCollector.DamageResolved` 同步播放本地预测闪白；普通攻击、技能与持续伤害共用此入口。非联网敌人保留原来的伤害反馈入口。

服务器对实际扣血大于零的敌人生成 `EnemyHitPresentation`，包括服务器接管后的持续伤害。事件随可靠 `CanonicalWorldBatch.EnemyHitPresentations` 发送，包含伤害事件 ID、目标 ID 和结算后版本；快照及普通状态同步不包含历史事件。

`NetworkCombatWorld` 只在成功启动闪白后记录事件 ID；服务器回执更新确认版本但不重播已预测的命中。较旧版本、重复事件及未知/已销毁目标被丢弃。已生成但未初始化的目标只暂存最近事件，最多保留 0.5 秒。销毁、断线和新局清理记录，模拟权交接保留记录。

沿用原有 0.04 秒间隔的白/黑/白/黑表现并恢复材质参数。伤害数字、伤害计算、击退、掉落与确认死亡立即销毁的时序不变；致死闪白可以被销毁打断。所有客户端与服务器必须使用同一版本的网络批次结构。

## 自动验证

- EditMode：`EnemyHitPresentationContractTests;NetworkCombatGatewayTests;EnemySimulationTests;ServerStatusDamageAdmissionTests`，63/63 通过，结果 `Logs/EnemyHitFlash/edit-2.xml`。
- PlayMode：`EnemyHitFlashPlayModeTests;EnemyNetworkKnockbackTests;OrdinaryHitKnockbackPlayModeTests;UltimateStartupKnockbackPlayModeTests`，15/15 通过，结果 `Logs/EnemyHitFlash/play-3.xml`。
- 初始化等待补充回归：`Logs/EnemyHitFlash/play-pending-echo.xml`，1/1 通过，确认回执不会延长期限，新快照会淘汰待播放的旧预测事件。
- 最终初始化/材质/去重回归：`Logs/EnemyHitFlash/play-final-clock.xml`，1/1 通过；初始化等待改用本机实时钟，避免网络时间校正延长 0.5 秒期限。
- 击杀/掉落回归：`RealCirclingPhysicsKillProducesGem_OnlyAutomaticPickupGrantsXP`，1/1 通过，结果 `Logs/EnemyHitFlash/play-kill-drop.xml`；真实武器命中经服务器确认后销毁敌人、生成经验宝石，自动拾取只授予一次 4 XP。
- PlayMode 使用正式 Boot/Gameplay 与真实 EnemyBase Renderer，检查材质白黑交替、恢复、预测/确认去重、远端批次、多次命中、零预测、未成功播放、初始化等待和过期、快照、销毁、完整 FSM 与非联网伤害路径。

独立进程复用 M3 正式 Boot/Gameplay、Circling、Physics2D、攻击准入、GAS 与模拟权交接验证。启用 `-ValidateHitFlash` 后额外检查每个客户端的真实材质参数、事件集合与确认血量，覆盖双方轮流/同时攻击、三种 DOT、来源断线后服务器 DOT 接管及重连不回放旧闪白。

最终构建：`Logs/EnemyHitFlash/build-dot.log` 成功。独立 Host/Client 运行：`Logs/M3Knockback/Host-20260912-122034-725-p7988`，两个进程均 `PASS`、退出码 0。未执行 Dedicated 或网络延迟/丢包配置。

| 阶段 | 服务器确认伤害 | 闪白结果 |
| --- | --- | --- |
| Client 攻击 / Host 攻击 | 各 5 次 | 攻击者即时、观察者确认后播放，各事件一次 |
| 双方同时攻击 | 10 次 | 两端均呈现全部 10 次；击退按原规则合并为 5 次 |
| 断线交接 / 服务器权威模拟 | 各 5 次 | 闪白、血量与位移收敛断言通过 |
| 燃烧 / 中毒 / 流血 | 每阶段 5 次直接伤害 + 3 次 DOT | 两端各 8 次闪白，无确认重播 |
| 来源断线后服务器 DOT | 1 次直接伤害 + 4 次 DOT | 留在线上的 Host 呈现 5 次，重连客户端回放数为 0 |

上述运行最终敌人血量为 9397，与全部确认伤害一致；闪白不会造成额外扣血或击退。死亡销毁、网络经验宝石和拾取由独立的真实武器 PlayMode 回归验证。

### 验收中观察到的独立异常

这两个问题已在后续修复中完成复现、修复与严格回归，详见 [模拟快照与 DOT 确认重放修复](snapshot-dot-reconciliation.md)。以下保留原始验收记录；后续最终三种独立进程场景均为零异常、无额外 DOT 或重复闪白。

最终 Host 日志仍有 6 次 `ArgumentException: Invalid server simulation snapshot`，栈为 `ServerEnemySimulationRegistry.RecordServerSnapshot` → `NetworkEnemySimulationWorld.Update`。闪白、血量和位移断言均通过，但整轮运行不属于零异常验收。本次未改这两个方法，未扩展修改模拟快照逻辑。

较早运行 `Host-20260912-121650-406-p7988` 的来源客户端曾预测出第 4 个燃烧 tick，被服务器按 3 tick 预算拒绝。该本地有效扣血按约定即时闪白，观察者只有 3 个确认 tick。最终运行的三种 DOT 均没有出现额外预测。夹具分别核对本地正伤害预测与服务器确认集合；同一事件 ID 的二次播放始终判失败，服务器拒绝的事件不得在观察端呈现。本次未改持续伤害计算或状态进度恢复。

```powershell
& 'D:/RealSoftware/6000.3.17f1/Editor/Unity.exe' -batchmode -nographics -projectPath . -executeMethod MonsterSupergroup.Gameplay.Tests.EnemyHitFlashValidationBuild.Build -quit -logFile Logs/EnemyHitFlash/build.log
./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Executable Builds/EnemyHitFlash/EnemyHitFlash.exe -ValidateHitFlash -CaptureFrames -ForceD3D11
```

## 验证环境

工作区同时存在菜单本地化开发改动。初次 PlayMode 编译遇到缺失/重复程序集引用；仅去掉了三条与既有 GUID 引用重复的名称引用，未修改菜单逻辑。
完整构建最初另受菜单本地化资源生成检查影响。曾准备隔离验证副本；主工作区的相关资源随后已由同时进行的菜单工作生成，因此最终构建继续使用主工作区。没有为本次闪白修复生成或回写菜单资源。

独立进程夹具同步适配当前准备房入口、实际武器碰撞体偏移及存活玩家接收模拟权的行为，没有改变产品攻击和交接逻辑。隐藏窗口截图为黑帧，不作为画面通过证据；画面断言读取真实 Renderer 的 MaterialPropertyBlock。

清理 `Logs/EnemyHitFlash/validation-project` 临时副本的操作被自动审批拒绝，理由 `blocked by policy`；副本保留，没有绕过限制重试删除。
