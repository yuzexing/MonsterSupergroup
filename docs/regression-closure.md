# 剩余 13 项回归失败修复记录

原始记录保留在 [Phase 5 test-results.json](evidence/limbo-full/test-results.json)。本页对应后续修复，不能用新结果覆盖历史失败。玩家 Prefab 默认武器 6、Limbo 武器 1、地图、敌人数值和联网资格均保持原配置。

## 实际修复

生产修改限于两处编辑器工具：`DanteNativeGasMigration.EnsurePlayerBuildRuntime` 与 `PlayerRuntimeCombatPrefabMigrator.ConfigureNetworkPlayer` 只在首次新增 `PlayerBuildRuntime` 时设置原有默认武器 2。已有组件保留选择，防止重跑迁移覆盖 6 或显式的 1。没有修改战斗运行代码或生产网络协议。

| 原失败组 | 处理及复验重点 |
|---|---|
| Dante／Circling／Dash 武器检查，3 EditMode | 不再要求历史默认 2；验证选择能在数据库唯一解析、运行组件唯一。新增两工具 × 已有 6／已有 1／首次新增共 6 项测试，每项重复迁移两次，使用临时 Prefab 并检查实际玩家文件字节不变。当前默认 6 继续由已有 Ovid 测试集中检查。 |
| Ultimate，1 EditMode | 旧 `title` 字段已不存在；改查 `LocalizedTitle` 的 `MonsterContent` 表、`ultimate.0.name` 条目及英文 `Dante's Inferno`。原身份、属性、资源与动画断言保留。原先“Ultimate 资产引用为空”的描述不准确。 |
| 专用服／Boss，2 PlayMode | 经 Mirror 登记实际玩家 Prefab、有效连接、Avatar 与 GAS 生命，再验证资格。裸 Endpoint 保留为反例：不合格、无刷怪。Boss 使用有真实 Combatant 的敌人 Prefab。测试连接的传输为空实现，仅作为结构夹具，不宣称真实双端。 |
| ObserverRelay／Fallback，1 PlayMode | 建立合格远端、走当前交接入口并确认首快照。使用当前 epoch、轮次和有效检查点；第一份后续移动采样来自实际下一帧。与可靠交接检查点同时间的 UDP 采样不能覆盖检查点，不能把此优先级误判成中继失败。保留顺序、重复、超时与死亡检查。 |
| EnemyBase 导航，1 PlayMode | 明确主动 `attackScript` 为空，接触使用 `EnemyContactDamage`；保留 Replica 不导航及实际接触扣血检查，不补挂无用主动攻击脚本。 |
| 玩家 Dash，1 PlayMode | 通过实际地图找合法、有运动空间的起点，同步物理并等位置稳定；增加地图外请求不得消耗资源的反例。原时序、版本、非有限值及协议容差检查保留。 |
| Ultimate 路由，2 PlayMode | 地图内合法布置，保留实际范围、执行次数和位移断言。未关闭地图约束或扩大校验容差。 |
| 普通 Orb 击退交接，1 PlayMode | 使用当前检查点与正式交接状态，检查剩余轨迹、硬直期限、事件去重和旧 epoch 拒绝；冻结与禁用仍必须清理。按现行交接约定继承击退，不恢复“交接速度归零”的旧约定。 |
| Summon 首次装备，1 PlayMode | 实际复现到基线启用与测试 Gate 每帧禁用竞争，经 `OnDisable → CancelPet → PresentationTerminated` 销毁茧。基线完成后一次性禁用自动执行，关闭 Gate 后由单一手动 Tick 驱动。保留拒绝攻击后原茧仍存在的严格 `Single()`，增加重复基线、显式禁用和重新绑定检查及终止原因记录。 |

Summon 当前资产的初始成熟等待为 **3 秒**。旧测试名中的 SixtySecond 已改为 AuthoredDeadline；断言仍读取实际配置，没有把运行值改为 60 或缩短等待以通过测试。适配器重新绑定只恢复绑定，不自行重新启用已关闭的武器；夹具继续用单一执行者。

## 自动化结果

当前最终无过滤广域：**EditMode 763/763；PlayMode 523/523；失败、跳过均 0**。其中 EditMode 包含 1 项 Unity Addressables 文档示例；项目过滤版为 762/762。新增测试数为迁移幂等 6 项，其余反例在原测试中补充。当前树还包含此前 Phase 5 已增加的检查，不能用总数差推断本轮新增数量。

定向：EditMode 53/53；PlayMode 16/16。PlayMode 原 9 项失败均在最终广域复验覆盖。测试结果及 SHA-256 见 [本轮清单](evidence/regression-closure/test-results.json)，原始 XML／日志在 `Logs/RegressionClosure20260916`。

修复前迁移复现：4 项确实覆盖已有选择，另 2 项首次新增夹具错误删除了仍被依赖的组件；后者改为从最小新对象建夹具。中间编译与定向失败保留：缺测试程序集依赖、可靠检查点时间前提、以及重新绑定误期待自动启用。这些不是额外已确认的游戏运行缺陷。

## 图形与独立进程验证

最终构建已完成 **Host／Client 两个图形进程**（`host-client-5`）及 **独立无图形专用服＋真实图形 Client**（`dedicated-1`）。两组各端均 PASS，日志无异常；不是用测试连接替代真实 Client。Native ScreenCapture 截图与逐帧日志一同归档。

| 验证 | Host／Client | 专用服／Client |
|---|---|---|
| 初始生成资格 | 2 个有效参与者、服务端成功生成 2 | Client 加入前无合格参与者、0 敌人；加入后生成 1 |
| 地图内 Dash | 两端 Owner 各消耗 1 充能；服务端对 Client accepted=1、rejected=0 | Client 消耗 1，服务端 accepted=1、rejected=0 |
| 首次装备 Summon | 两端实际 Cocoon，成熟期限按资产 3 秒；观察期间原 PetId 保留，显式禁用才终止 | Client 同样保留原 Cocoon 至显式禁用 |
| Boss 归属 | 敌人 #6 为 ServerAuthoritative、owner=0；服务端位移 0.2183，Client 不导航 | #4 同归属；位移 0.2194，Client 不导航 |
| Ultimate 启动击退 | Client 执行一次；#7 轨迹约 `(14.13,.99)→(18.13,.98)`；交给 Host 后 epoch=2，保留同一起终点 | Client 执行一次；#5 轨迹约 `(13.35,.99)→(17.35,.98)` |
| 真实快照超时及接管 | #8：约 1 秒后 ServerFallback epoch=2 并移动；恢复报告后 Client owner=4、epoch=3，首快照确认 | #6：ServerFallback epoch=2；恢复后 Client owner=2、epoch=3，首快照确认 |
| 结束 | 两端场景卸载、敌人与 Summon 清零，PASS | 两端同样 PASS |

Boss 测试使用生产 EnemyBase 身体与网络生命周期，显式选择 BossServer 模拟模式，只验证归属和导航，不代表新增或验证 Minos 战。普通 Orb 的实际多次物理命中及检查点继承在 PlayMode 测试覆盖；此次独立进程的在途交接画面使用 Ultimate 触发，不冒称为 Orb 画面。

采用同一 `Builds/RegressionClosure20260916/RegressionClosure.exe` 开发验证构建，显式 `--regression-role` 才安装测试程序集中的 `RegressionClosureProcessProbe`。文件仅协调步骤；玩家、出生、动作、状态和命中仍走实际 KCP／Mirror／GAS。正常启动不启用该探针。

辅助明确包含：运行时装备 Ovid 402、现有玩家无敌引用计数、合法地图定位、每参与者生成配置、探针敌人最低 HP 10000、对测量目标暂停导航、显式结束清理。此处不是 Limbo 正常连续局或玩法压力测试，也不是完整重连／倒地复活矩阵。

首轮 `host-client-1`：有早期截图但部分被准备菜单遮挡，不作为最终画面证据；探针错误等待服务端专属计数在 Client 改变，且使用会被正常基线更新覆盖的临时保护设置。第二轮 `host-client-2` 已完成两端 Dash 确认，但步骤文件尚在写入时被对端读取，出现 sharing violation。第三轮启动脚本错误拆开命令参数，尚未进入机制测试。第四轮探针从根节点误取身体 CircleCollider，实际绑定在子节点。修复限于探针及脚本：分别检查 Owner 确认与服务端计数、使用成对的现有保护引用、关闭文件后原子发布标记、完整传递参数、读取控制器实际绑定的碰撞体。显式旧式隔离入口不经过准备房，因此隐藏其菜单及调试面板，保留 Gameplay 镜头；正常菜单不修改。失败轮次保留，不计完成。

复跑命令（目录必须使用新名称）：

```powershell
./Tools/Run-RegressionClosure.ps1 -Mode pair -RunName pair-repeat -Port 8071
./Tools/Run-RegressionClosure.ps1 -Mode dedicated -RunName dedicated-repeat -Port 8072
```

两组依次执行，脚本只启动进程；每端自行写入 `*-result` 后退出。状态步骤文件仅协调隔离测试，不能驱动或确认战斗协议。测试配置保留原默认启动，不会自动重跑该流程。

交付索引：[最终验收及构建哈希](evidence/regression-closure/verification.json)、[全部自动化与进程记录](evidence/regression-closure/test-results.json)。原始归档为 `Logs/RegressionClosureEvidence-20260916.zip`，共 133 个文件、41,409,995 字节，CRC 全部通过；SHA-256 在验收清单中。Phase 5 原压缩包与原构建未覆盖。

抽查画面：[Client 首次茧](evidence/regression-closure/pair-client-cocoon.png)、[Client Ultimate](evidence/regression-closure/pair-client-ultimate.png)、[Host 接手击退](evidence/regression-closure/pair-host-handoff.png)、[专用服 Boss 归属的客户端画面](evidence/regression-closure/dedicated-client-boss.png)、[专用服恢复交回](evidence/regression-closure/dedicated-client-takeover.png)。截图证明显示及现场状态，位移、事件次数与 epoch 以同帧附近日志核对；单张截图不能证明完整轨迹。

## 交付边界

本次不修改原始恢复证据，不覆盖 `Builds/LimboFull20260915` 或 Phase 5 原日志。普通玩家、敌人配置保持不变。Spine、人工压力、XP／镜头校准、Minos 和完整成员生命周期矩阵均未扩展。
