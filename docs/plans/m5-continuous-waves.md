# M5：正式 Gameplay 持续多人波次

## 实现与参考映射

正式场景仍通过 Boot 的 Mirror 网络管理器加载。Gameplay 的 `NetworkGameplayEnemySpawner` 在 Waves 模式下等待服务器操作员开始本局；成员注册不再生成敌人。`BeginRun / TryBeginRun` 校验 Gameplay、World、当前连接的角色及 canonical 存活状态，然后冻结规则并锁定原成员名单。重复开始不重置状态。

当前实现参考 HellMaiden 的时间段调度结构，**不是原版生成系统的完整移植**。本次明确采用用户确认的固定节奏和世界距离：

| HellMaiden 实现 | 本次对应 | 未移植内容 |
| --- | --- | --- |
| ProgressionManager → ProgressionTimeline | 服务器 RunId 范围内的有效时间与调度 | 全局玩家、任务、关卡初始化、XP 倍率、ProgressionStack 单例 |
| EnemySpawnerClip → EnemyContinuousSpawner | 正式规则资产中的波长、数量、间隔 | Timeline 片段资产、spawnCurve 时间分布、原关卡数值 |
| EnemySpawner / SpawnHelpers | Ground 范围内轮流围绕存活成员取点 | 单机相机外生成、旧寻路/地图依赖、rubberband |
| EnemyFactory / PoolManager | 现有 NetworkEnemyBase → NetworkServer.Spawn → SimulationWorld | 旧单机 Enemy 创建及全局玩家目标 |

参考根目录：`F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/Scripts/Assembly-CSharp/AstralShift/HellMaiden/`。本轮读取了实际代码，没有将类名或默认字段值作为原关卡配置。

## 已确认规则与接线

- `Assets/_Project/Content/NetworkCombat/GameplayWaveRules.asset`：30 秒一轮，每轮全局 6 只，在 0/2/4/6/8/10 秒各生成一次；存活上限 30。人数和波数不放大数量。
- 以上一波存活敌人继续战斗，不等清空。上限、找不到位置或错过的计时机会均消费为跳过，不在空位出现后补发。
- 没有在线且 canonical Alive 的成员时冻结波次时间；恢复后从剩余时间继续。全员死亡等待 Stop / 新局，不新增复活和结算。选卡不暂停波次。
- 固定成员身份排序并轮转选择目标；取点半径 5，与所有在线存活角色保持至少 2 单位距离，每次最多 16 个角度。使用 Ground 的 SpriteRenderer 世界 bounds，并为 Enemy 的实际圆形身体碰撞体留边距。不保证屏幕外生成。
- 规则和 Ground 范围在开局捕获；运行中调整资产不改变当前局。非法规则、缺少 Ground、错误或未向 Mirror 注册的 Enemy Prefab 阻止开局。
- `ServerWaveSchedule` 是可注入时间的纯调度器；仅服务器运行。存活数直接读取本场景已生成网络 Enemy 的服务器 ledger，不能由本地预测 HP、销毁回调或客户端计数决定。
- `NetworkWaveProgress` 追加在 Boot 已有的 `NetworkCombatWorld.prefab` 上，用 Mirror SyncVar 完整快照同步 RunId、阶段、波数、有效时间、本波生成/跳过、累计计数和全局存活数。正常更新间隔 0.2 秒，重连走 Mirror 初始快照，没有客户端生成计时器或历史事件回放。
- `WaveProgressHUD / NetworkWaveHUD` 挂到正式 CombatUI 根，顶部中央显示进度；其图片和文字不接收点击，不受 F3 Debug 展开状态影响。断线和卸载清除显示，重新加载绑定新 World。
- 停止、禁用和卸载释放订阅及成员缓冲，调度终止；仅通过 Stop → Gameplay 卸载 → 新网络会话 → 开始本局重开。

### 接口变化

- `BootGameplayNetworkManager.BeginRun()` 保留，失败时抛出明确原因；新增 `CanBeginRun(out error)`、`TryBeginRun(out error)` 供操作界面使用。三者均不能由普通 Client 开局。
- KCP 角色增加 Server，命令行支持 `--kcp-role=server`。面板增加 `Server only` 和服务器侧 `Start run`，继续沿用已有地址、端口、延迟模拟与 Stop 清理。
- 新增数据类型 `WaveProgressSnapshot`、规则资产 `GameplayWaveRules`；没有修改伤害、攻击、击退、Build 或 XP 合同。
- `NetworkGameplayEnemySpawner.Configure(prefab, distance)` 显式使用原 PerMember 模式，供旧受控战斗夹具和回退使用。正式 Gameplay 序列化为 Waves；配置错误不自动切换到 PerMember。
- 修正 Boot 的远端 Ready 顺序：第一次 Ready 只请求加载 Gameplay，并回复 NotReady；Gameplay 加载完成后的 Ready 才开始 Mirror 对象同步和角色创建。原顺序会先发完整对象批次，再加载场景及再次同步，在大量存活 Enemy 的延迟重连测试中暴露了 `netId=0` 延迟对象初始化异常。没有修改 Mirror 库或放宽实体 ID 校验。

## 自动验证

记录在 `Logs/M5/`，未运行仓库全部测试。

| 检查 | 结果 |
| --- | --- |
| 网络 EditMode，包括新增调度、配置快照、Mirror 序列化、Server-only 参数 | `editmode-1.xml`：344/344 通过 |
| 第一轮正式场景 PlayMode：M5 5 项，场景清理 4 项，M2 5 项，M3 1 项，Ultimate 3 项，M4/F5 8 项 | `playmode-1.xml`：26/26 通过 |
| 第一轮 Host + Client | `Host-20260910-164153-090-p7986`：双方 PASS，退出码均 0 |
| 第一轮 server-only + 两个 Client，80 ms 延迟、10% 不可靠快照丢包 | `Dedicated-20260910-164153-074-p7987`：三端 PASS，退出码均 0 |
| 加载顺序修复后的定向 PlayMode，新增一项 Boot 首次 Ready 回归 | `playmode-ready.xml`：29/29 通过，Unity 退出码 0 |
| 最终可见 Host + Client | `Host-20260910-170147-303-p7990`：双方 PASS，loggedErrors=0，退出码均 0 |
| 最终带窗口 server-only + 双 Client，80 ms 延迟、10% 不可靠快照丢包 | `Dedicated-20260910-170147-303-p7991`：三端 PASS，loggedErrors=0，退出码均 0 |

第一轮构建为 `build-1.log`，构建成功。第一轮隐藏窗口截图为黑帧，**不计为画面验证通过**。

最终测试使用同一份 `build-visible.log` 对应的构建，Unity 构建退出码 0。测试完成后所有验证进程均已退出。最终日志未出现 `netId=0`、运行期 Exception / Error 或重复 AudioListener 警告。EditMode 与 PlayMode 断言、进程退出码分别记录，未把退出成功当成玩法验证。

已检查保存的 1920×1080 实际画面：

- [Client 第三波](</F:/UnityStore/MonsterSupergroup/Logs/M5/Host-20260910-170147-303-p7990/client-three-waves.png>)：顶部显示 Wave 3、Alive 18/30、Spawned 6/6；Debug 与网络面板未遮挡顶部进度。
- [延迟 Client 选卡期间](</F:/UnityStore/MonsterSupergroup/Logs/M5/Dedicated-20260910-170147-303-p7991/client-upgrade-during-waves.png>)：顶部进度完整可读，中央三张 Equipment 卡未被覆盖；Enemy Debug 沿用选择期间收起行为。
- [server-only 等待开局](</F:/UnityStore/MonsterSupergroup/Logs/M5/Dedicated-20260910-170147-303-p7991/server-waiting-start.png>)、[运行状态](</F:/UnityStore/MonsterSupergroup/Logs/M5/Dedicated-20260910-170147-303-p7991/server-three-waves.png>)：带窗口操作面板可见，无本地游戏角色。状态按 0.2 秒刷新，截图可能比服务器最新生成事件晚一次刷新。

截图中的网络输入框保留 Awake 时的初始值；夹具之后通过服务配置使用 7990 / 7991 端口，延迟设置与实际启动参数见对应 `run.json` 和日志。该既有输入框缓存显示不用于判断测试实际网络配置。

第二轮可见测试 `Host-20260910-165002-989-p7988` / `Dedicated-20260910-165002-970-p7989` 未通过：新增 Equipment 断言错误地从远端读取服务器专用 BuildRevision，现已改为等待 OwnerBuildRevision，并在服务器另验两人的 BuildRevision。同时发现上述 Ready 顺序异常，已修复并补充回归。最终进程夹具会将运行中出现的 Unity Error / Exception / Assert 计入失败，不能只因波次数量断言通过而忽略异常。

多进程夹具通过正式 KCP 服务启动、正式服务器开局入口和真实默认规则运行三波（18 只）。测试期间仅禁用 Owner 自动攻击并保护角色，避免计数测试被战斗干扰；不会修改生产武器和 Enemy 属性。上限阶段额外通过真实网络 Prefab 填满剩余容量以缩短等待，再注入受信任的 canonical 死亡来验证下一次机会释放名额。这部分不代表普通武器击杀/XP 流程验收。

同一夹具还覆盖单端断线接管、原身份重连、server-only 全员离线暂停/恢复、全员 canonical 死亡暂停、Stop 后原进程重新开局及新 RunId。最终夹具补充 F5 → Equipment 两阶段选择和选卡期间持续计时。

`-CaptureFrames -VisibleWindows` 同时显示 server-only 的操作窗口并保存其等待/运行画面；不带这两个参数时可使用无图形自动测试。两种方式调用同一 KCP 服务及服务器开局入口。

自动化入口：

```powershell
& 'Tools/Run-WaveProcessValidation.ps1' -CaptureFrames -VisibleWindows -Port 7986
& 'Tools/Run-WaveProcessValidation.ps1' -Dedicated -Simulation -CaptureFrames -VisibleWindows -Port 7987
```

构建入口：`MonsterSupergroup.Gameplay.Tests.GameplayWaveValidationBuild.Build`，输出 `Builds/M5Waves/M5Waves.exe`，Development + IncludeTestAssemblies + KCP 构建定义。只有显式传入 `--m5-role` 才加载测试夹具。可见启动统一使用 D3D11 临时参数，不修改项目默认图形 API。

## 人工步骤

1. 运行 `Builds/M5Waves/M5Waves.exe -force-d3d11`，或 Unity 打开正式 Boot 后 Play。启动两个进程，选择相同端口，一个 Host，一个 Client。未点 Start run 时应无自动生成敌人，顶部显示等待。
2. 等两名角色进入后，在 Host 的 KCP 面板点 **Start run**。Client 不显示该按钮。首只当轮立即出现，后续每 2 秒一只，共 6 只；第 30 秒进入第二波。F4 收起网络面板，F3 可收起 Enemy Debug，顶部波次仍保留。
3. 两名玩家移动、分散到 Ground 边缘，观察敌人轮流从角色周围进入战斗。出生时身体应在 Ground 内，非贴身生成；之后怪物追逐、碰撞和击退仍沿用原规则。
4. 先不杀敌观察上限 30，查看跳过计数；击杀后只有后续新的生成机会补充。对照两端同一 RunId 下的波数、数量和倒计时；允许正常传输/0.2 秒刷新延迟。
5. 一端按 F5 升级，走按钮或数字键选择 Equipment 和目标。菜单打开期间波次继续，顶部进度可读且不遮挡选项，另一端继续活动。
6. 断开负责部分 Enemy 模拟的 Client，检查既有敌人接管；相同进程重新连接后成员身份、Build 和当前波次恢复，没有额外一只成员怪、重复计时或旧菜单。
7. server-only 组合：另启一个带窗口进程，参数 `--dedicated-server -force-d3d11`，在 KCP 面板点 **Server only**；两个 Client 加入后由服务器窗口点 Start run。断开两名 Client 后波次暂停，重连恢复剩余时间。服务器窗口只需操作面板，不应创建本地游戏角色或 Gameplay HUD。
8. 运行中与暂停时分别在服务器点 Stop，确认两端 Gameplay 清理；同一服务器重新启动，Client 加入后再点 Start run。新局从 Wave 1 开始，无旧怪物、计时器或目标。

需要人工确认：真实操作员按钮/键盘焦点、不同分辨率下的可读性、正常 Circling 战斗下的出现位置、追逐/击退与震屏观感。Steam 双账号、普通 Release 和独立 UNITY_SERVER 构建未验证。自动截图不能代替这些项目。

## 交付与回退

M5 独立提交，保留基线音频提交 `61aa08d`、M1–M4、GAS、PlayerBuildRuntime、Modifier 和 Enemy SimulationOwner 规则。测试/构建产生的 PerformanceTest 资源、渲染管线运行时缓存及预加载资源列表改动不纳入提交。

完整回退撤销本次提交并重建同版本客户端/服务器；从新局验证 PerMember 模式恢复原先每成员一只敌人及稳定身份去重。不在运行中的局内切换协议或热回退。旧夹具已显式选用 PerMember；M5 夹具始终使用正式场景 Waves 默认配置。
