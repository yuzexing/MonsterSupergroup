# 缓存生命周期修复与回归

修复前基线：`28f3617`（保留此前新局 0 HP 修复）。复现测试位于 `GameplayWavePlayModeTests.cs`，分类为
`CacheLifecycleReproduction`。全部从正式 Boot 加载 Gameplay，使用现有网络 World、
Enemy Prefab、波次生成和 Mirror Stop/Start；不修改 Scene、Prefab 或正式玩法配置。

## 修复前的实际结果

| 检查 | 观测 | 测试性质 |
| --- | --- | --- |
| 服务器状态移除版本 | 64 个合法状态移除后活动状态为 0；Stop 后保留 64 条，新 RunId 启动后仍为 64 | 防回归断言失败：新局预期 0，实际 64 |
| Host 已销毁敌人的血量副本 | 第 6 次死亡保留 6 条，第 12 次保留 12 条；停止刷怪并等待 3 秒后仍为 12；Stop 后归零 | 现状复现测试通过，表示成功观察到了积累，不表示问题已修复 |
| 攻击表现等待记录 | 正式服务器准入和可靠 RPC 产生等待记录；Stop 前后均为 1 | 清理断言失败：Stop 后预期 0，实际 1 |
| 新局攻击序号对照 | 复用相同 Enemy netId 与 assignment epoch 后，出生时接收序号为 0；新消息在服务器和 Host 上均为 1 | 正常接收检查通过，未复现旧序号压住新序号 |

因此，第 3 项 review 结论应收窄为**会话等待缓存确实未清理**。
本轮 Host 初始化顺序没有产生推测中的序号阻塞；不能将该推测写成已确认的玩法故障。
Skeleton 实际攻击和不同初始化时序的画面影响未由这条复现证明。

## 测试如何设置条件

- `RemovedStatusVersions_StopRestart_DoesNotRetainPreviousRunRecords`：在实际 Enemy
  上通过 `ServerStatusRegistry.Apply` 设置 64 个合法状态，使用 Owner 的事件编号；随后由
  现有权威死亡夹具触发真实 despawn 和清理。状态设置用来隔离生命周期问题，不冒充武器准入测试。
- `HostDeaths_DespawnedRecordsAreReleasedAfterNotifications`（原名 `HostDeaths_CharacterizesRetentionGrowthUntilStop`）：仅在测试运行时加快波次，连续处理
  12 个正式生成的 Enemy。死亡使用已有 `SetCanonicalHealth` 夹具，经
  `NetworkCombatWorld → NetworkEnemyServerDriver → NetworkServer.Destroy`，Host 自行消费
  实际排队的 RPC。未手动往 Replica 写死亡数据。等待时间超过 Debug 死亡行的 2 秒展示时间。
- `PendingAttackEdge_StopRestart_ClearsWaitingRecordsAndAcceptsFreshSequence`：从当前
  SimulationOwner 的服务器入口提交合法阶段消息，确认 Registry 接纳后，在同帧触发死亡。
  Host 随后收到真实可靠 RPC，形成目标已不存在的等待记录；没有反射写入缓存。
  正式 EnemyBase 使用 movement-only，不会自行产生这条阶段消息，因此这属于协议边界条件注入。

第二项已由观测型复现改为防回归断言：两轮各 6 次死亡和空闲等待后的已销毁 Enemy 记录均须为 0。
第一和第三项保留原清理断言，新局正常接收首条攻击表现的检查也保留。

## 生产修复

- `NetworkCombatWorld`：服务器 Stop 清空状态实例和移除历史；Start 创建新 Gateway，隔离
  战斗缓存与统计。World 自身事件和连接 epoch 分配方式保留，其他组件仍按原生命周期订阅。
- `ServerStatusRegistry / ServerCombatGateway`：移除历史记录目标身份、版本和原状态到期时间。
  Enemy 注销即释放其历史；服务器原有 Advance 回收已到期历史。同局玩家断线仍保留有效历史，
  checkpoint 恢复继续递增版本并只处理剩余 tick。
- `NetworkCombatWorld.ApplyCanonical`：先应用整批状态、通知订阅者，再释放已不在客户端 spawned
  集合中的死亡 Enemy。保留玩家、存活的早到基线和仍生成的对象；Debug 自己保存 2 秒死亡行。
- `NetworkEnemySimulationWorld`：客户端 Stop 和 Start 清理攻击表现、击退等待记录及遍历缓冲；
  不清理 Host 共用的玩家、Enemy、模拟者注册表。重试时丢弃落后于当前 assignment epoch 的攻击记录。

未增加协议字段、死亡历史集合或清理计时器；未修改 SimulationOwner、伤害准入、GAS、Build、
奖励规则、Scene、Prefab 或正式资源。

补充测试覆盖 Enemy 注销与到期回收、玩家有效状态恢复、两个客户端等待队列、Start 防御清理、
旧 epoch、合法消息早于注册以及保留 Host 注册表。Debug 测试走 World 的实际应用入口，检查
死亡缓存已释放但死亡行仍展示并按时消失。

### 联机回归发现的阻塞及最小补修

三项清理修复后的专用服务器波次夹具 `Logs/M5/Dedicated-20260910-225230-794-p7998` 在第二次开局
触发 `NetworkCombatantAdapter.OnStartClient → ConfigureEntityId(0)`：server/client2 为 PASS、退出 0，
client 为 FAIL、退出 1。这是实际初始化异常，不能按原生进程退出问题忽略。

使用修复前 `28f3617` 对应备份构建、同一夹具和延迟设置作对照：
`Logs/M5/Dedicated-20260910-225453-638-p7999` 同样复现（client2 FAIL、退出 1；其余两端退出 0）。
该构建的网络程序集 SHA-256 为
`07E9A7998FDCE40C60D60612F509A679A07DFABFCB396571215901B51F7707B6`。

原因是当前 Mirror `SetClientReady` 先将连接标记 ready，但玩家尚未创建时不会发送初始 Spawn 批次。
此时其他玩家生成可提前发来 Spawn，客户端先保存其 payload；之后
`OnObjectSpawnStarted` 清空 `pendingSpawns`，却未移除 spawned 中尚未初始化的对象。
`OnObjectSpawnFinished` 因而对 netId=0 的对象执行客户端初始化。

新增 `ClientSpawnBatchLifecycleTests` 直接经过现有 Mirror 消息处理入口，未写入私有缓存。
生产补修前 `Logs/CacheLifecycleReview/spawn-before.xml` 为 **0/3 通过**：一次和重复批次开始均令
预期 netId 42 变为 0；Shutdown 后预期 0 条等待数据，实际 1 条。退出码 2。

为使既有联机回归通过，补修 `Assets/Mirror/Core/NetworkClient.cs`：保留批次开始前合法的等待数据，
仍在批次完成时消费并清空，连接 Shutdown 时释放未完成的数据。仅移动清理边界，不改消息字段、
权限或角色创建规则，也不在 Combatant 中接受／忽略 netId=0。后续升级 Mirror 时应保留这组回归。

## 重跑

关闭共享此项目 Assets 的 Unity 编辑器，在项目根目录运行：

```powershell
$p = Start-Process -FilePath 'D:\RealSoftware\6000.3.17f1\Editor\Unity.exe' -WindowStyle Hidden -PassThru -Wait -ArgumentList @(
    '-batchmode', '-nographics',
    '-projectPath', 'F:\UnityStore\MonsterSupergroup',
    '-runTests', '-testPlatform', 'PlayMode',
    '-testCategory', 'CacheLifecycleReproduction',
    '-testResults', 'F:\UnityStore\MonsterSupergroup\Logs\CacheLifecycleReview\reproduction.xml',
    '-logFile', 'F:\UnityStore\MonsterSupergroup\Logs\CacheLifecycleReview\reproduction.log'
)
$p.ExitCode
```

完整波次测试使用 `-testFilter MonsterSupergroup.Gameplay.Tests.GameplayWavePlayModeTests`
替代 `-testCategory CacheLifecycleReproduction`。
修复后复现组共 5 项，预期全部通过，Unity 退出码为 `0`。

首次编译遇到缺少命名空间和项目 NUnit 不支持 `Assert.Multiple`，均仅在测试中修正。
可用证据从成功编译后的运行计数，保留以下本机产物：

- `Logs/CacheLifecycleReview/current-run4.xml` 和同名 `.log`：3 项，1 通过、2 个预期缺陷失败。
- `Logs/CacheLifecycleReview/current-run5-wave-suite.xml` 和同名 `.log`：11 项中 9 通过、2 个相同缺陷失败。
  原有 8 项波次／重开测试全部通过，包括已修复的新局 0 HP 回归；另 1 项通过的是缓存积累的现状复现。
  三项新测试与 run4 的计数和行为完全一致，Unity 两次均正常以测试失败码 `2` 退出。

修复前测试文件 SHA-256：`41D9A5ABBDE12DDA57A7021E0F2A2623320026AD23A17327F6566688B919C5FC`。

## 修复后结果

| 检查 | 修复后观测 |
| --- | --- |
| 64 条状态历史 | Enemy 注销后 0；Stop 后 0；新局 0，活动状态 0 |
| Host 12 次死亡 | 第 6 次、12 次、空闲 3 秒后，已销毁 Enemy 保留记录均为 0 |
| 攻击等待 | Stop 前 1，Stop 后 0；新局出生序号 0，首条服务器／Host 序号均为 1 |
| Debug | 迟到死亡通知仍产生死亡行，2 秒展示期结束后消失；Replica 不保留该 Enemy |
| 玩家恢复 | 原状态有效期内保留移除版本；恢复版本递增，tick 不回放；原到期时间后历史为 0 |

- `Logs/CacheLifecycleReview/fixed-editmode.xml`：网络战斗 EditMode **361/361 通过**，退出码 0。
- `Logs/CacheLifecycleReview/fixed-playmode-final.xml`：核心 PlayMode **63/63 通过**，无跳过，退出码 0。
  包括波次 13、Debug 7、XP 9、普通／网络击退 9，以及 Ultimate 各层与恢复 25 项。
- `Logs/CacheLifecycleReview/fixed-restoration-playmode.xml`：补充 PlayMode **32/32 通过**，无跳过，退出码 0。
  包括加强注销／Stop／新 Gateway 断言后的完整波次 13、正式 Boot 战斗 1、状态绑定 5、Build 恢复 5、
  网络选择 8 项。两组共有 82 项不同的 PlayMode 用例，波次 13 项重复验证。
- Mirror 补修后 `Logs/CacheLifecycleReview/all-fixed-playmode.xml`：合并上述 82 项及 Spawn 边界 3 项，
  **85/85 全部通过**，无失败、无跳过，退出码 0。Spawn 回归使用相同测试源码由 0/3 转为 3/3。
- 首轮 `fixed-playmode.xml` 为 62/63：新增保留检查在服务器 Spawn 同帧施加死亡，Host 客户端尚未
  注册该对象。测试补齐等待 `NetworkClient.spawned` 的前置条件，并断言对象仍在集合中；
  原“必须保留”的断言未删除或放宽。随后上述 63 项完整重跑通过。失败退出码 2 属于测试失败。

### 同版本多进程

通过既有 `GameplayExperienceValidationBuild.Build` 构建正式 Boot 和 Gameplay，
`Logs/CacheLifecycleReview/final-build.log` 构建成功、退出码 0。以下最终联机复测均使用
`Builds/M6Experience/M6Experience.exe`，其网络程序集 SHA-256 为
`00A98D6C8ED4ECFAB033C6E845ACB784465466738F7CC8EEF941A8EB702518A7`。
包含 Spawn 补修的 `Mirror.dll` SHA-256 为
`4B7CC4908015EC1B0122B18C85788F8C66B7E96FCBADEF058D833BB4FA11E755`。

| 夹具 | 设置 | 结果与本机日志目录 |
| --- | --- | --- |
| XP Host＋Client | 无延迟注入 | 两端 PASS，退出均为 0；`Logs/M6/Host-20260910-230157-312-p7990` |
| XP server-only＋双 Client | 80ms 延迟、10% 不可靠消息丢包 | 三端 PASS，退出均为 0；`Logs/M6/Dedicated-20260910-230207-500-p7991` |
| 选卡后开局／Stop 重开 Host＋Client | 80ms 延迟、10% 不可靠消息丢包 | 两端 PASS，退出均为 0；`Logs/M5/Host-20260910-230258-957-p7996` |
| 连续波次 server-only＋双 Client | 80ms 延迟、10% 不可靠消息丢包 | 三端 PASS，退出均为 0；`Logs/M5/Dedicated-20260910-230147-180-p7998` |

XP 两组均经过真实 Circling 确认击杀、两人争抢仅发一次奖励、重复请求、不同领取阶段断线、
升级队列和目标选择恢复、Stop 清理。Host 夹具另检查 Host 观察远端获胜者的后退／飞行动画。
新局 0 HP 回归中，旧 Enemy 死亡通知处理后已无残留；相同 netId 的新 Enemy 两端 HP 均为 100，
Observer 两秒内的位置误差为 `0.0000`（阈值 `0.02`），模拟者快照持续接纳。
专用服务器波次夹具验证三波 18 只、上限与释放、单人断线接管／重连、全员离线暂停／恢复、
选卡不暂停波次、全员死亡暂停，以及两轮 Stop／新局；第二次开局未再出现 netId=0 异常。
四组最终联机测试共 10 个进程，均为玩法 PASS、`loggedErrors=0`、退出码 0，无原生退出崩溃。

联机重跑命令（项目根目录，先构建同版本程序）：

```powershell
./Tools/Run-ExperienceProcessValidation.ps1 -Port 7990
./Tools/Run-ExperienceProcessValidation.ps1 -Dedicated -Simulation -Port 7991
./Tools/Run-WaveProcessValidation.ps1 -Executable Builds/M6Experience/M6Experience.exe -SelectionBeforeRun -Simulation -Port 7996
./Tools/Run-WaveProcessValidation.ps1 -Executable Builds/M6Experience/M6Experience.exe -Dedicated -Simulation -Port 7998
```

### 验证边界

本轮多进程使用无图形测试模式。吸取方向有动画位移断言，Debug 展示与到期有 PlayMode 断言，
但未重新进行主项目＋clone 编辑器的肉眼验收，也未评价画面观感。
第 3 项修复针对已经复现的等待记录残留；新局首条序号继续正常接收，不能据此声称之前存在
序号阻塞。正式 EnemyBase 自然战斗不会生成该攻击阶段消息；Skeleton 的跨会话攻击画面没有单独验证。

## 回退

仅撤销本次五个生产文件的清理逻辑、对应测试调整与本报告，不撤销 `28f3617` 的跨局 Replica 清理，
也不影响其他里程碑和用户改动。保留修复前日志作对照；从新局验证，不在运行中的会话热回退。
