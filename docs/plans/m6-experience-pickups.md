# M6：共享 XP 掉落与个人成长

正式 Boot → Gameplay 使用服务器确认击杀产生网络经验球。首次通过完整服务器校验的领取者获得全部 XP；击杀者不再直接获得经验。M1–M5、Enemy 单一 SimulationOwner、GAS、PlayerBuildRuntime、Modifier、音频与 EnemyBase 尺寸保持不变。

## 参考配置映射

参考目录：`F:/DecomplieLatest/HellMaiden/ExportedProject/Assets`，本次实际读取，未通过类名推测。

| 参考 | 正式接入 | 差异与边界 |
|---|---|---|
| Systems.unity 的 Leveler.xpCurve，7 个未加权关键帧 | `GameplayExperienceRules.asset` | 保留时间、值、切线，逐级取整数；100 级起 1000 XP，无 100 级封顶。不移植连续五级截断或全局事件。 |
| LootManager.XPDropControl，初值 2、权重 0.5 | `ExperienceDropSchedule`，每个服务器 RunId 一份 | 前四次正值击杀掉落，之后隔次掉落；零值击杀仍推进累加器，但不生成球。按 Enemy 与死亡版本去重，范围攻击不同目标不互相吞掉奖励。 |
| XPGem / WorldItem 的拉取范围、倍率、后退与飞行动画 | `NetworkExperienceCollector / World / Gem` | 服务器确认归属和入账后才播表现；后退 0.1 秒，线性；再飞 0.3 秒，原二次曲线和正弦弧线。动画不授予 XP。 |
| XP_0.prefab、orb_0 Animator、Pulsating、轨道球/火焰/光晕/阴影 Sprite 与 XP atlas | `Content/HellMaiden/NativeGAS/Experience`、`NetworkExperienceGem.prefab` | 仅导入 XP_0 所需闭包，剥离旧 XPGem 脚本，不接入旧 LootManager、Minimap、磁铁、对象池或镜头回收。保留 Sprite 和粒子结构；网络 Prefab 的显示根节点归零，移除导出对象的 (36.35, -24.06) 旧场景位置。 |
| 原自定义 URP Sprite Shader | 已安装 AllIn1SpriteShader 与独立 XP_Mat | 原导出 shader 不可直接复用。使用现有渲染器兼容材质，粒子显式绑定同一 XP atlas；不声称完整重建旧 shader 的所有效果。 |
| 本项目现有 UI_XPBar_Fill / Dark | `PlayerExperienceHUD` | 原纹理采用空的 Multiple Sprite 导入表，HUD 直接引用纹理并创建、释放自身显示 Sprite，不改共享导入设置。 |

关键门槛：等级 1/2/3/4/7/12/18/50/99 分别需要 19/39/60/81/171/305/374/596/999 XP。当前 Enemy 基础值为 2，拾取者倍率 2，因此每球奖励 4；自动吸取半径为服务器属性 pullArea，当前 2。配置在服务器 World 初始化时捕获，重连不重置。非法配置阻止开局与授予。

## 调用链与资源接线

1. `NetworkExperienceWorld` 挂在现有 `NetworkCombatWorld.prefab`。World 在 Enemy 生成前订阅 `ConfirmedKillProduced`，在后续死亡表现和销毁回调之前冻结 XP、位置与死亡身份。
2. 客户端模拟者使用当前 epoch 的最新已接纳快照；服务器模拟者使用服务器位置；没有当前快照则使用 Agent 记录的生成位置。创建注册在 Boot.spawnPrefabs 的 `NetworkExperienceGem`，初始同步含 RunId、DropId 和基础值。
3. `NetworkPlayer.prefab` 上的 `NetworkExperienceCollector` 仅在本地 Owner 存活、有效且未选择时搜索可拾取球。以每 0.1 秒最多一条请求提交 RunId/DropId；不提交数量、半径、目标玩家或最终位置。
4. World 校验连接、当前角色与成员、本局、权威生命、选择锁定、服务器距离和未领取状态。先预留球，再通过 `TryGrantExperience` 入账，失败释放预留；成功后通过 `ServerPresentCollection` 在 Host 本地先分离显示对象，可靠 RPC 通知远端，再立即销毁网络球。Host 本地与 RPC 共用 `presented` 去重；动画不决定入账。
5. `NetworkModifierSelection` 按当前等级逐次扣门槛，保留余量，将所有等级加入原奖励队列；原 OnConfirmedKill 只保留自身死亡取消选择。F5 使用当前门槛，恰好升级一次，保留余量，不乘拾取倍率。
6. 个人 XP/等级/队列/阶段仍使用原 checkpoint。未领取球及累加器只属于服务器局。断线后剩余球由 Mirror 当前生成对象基线恢复，已领取球不回放。
7. `LocalPlayerUIBinder → CombatHUDController → PlayerExperienceHUD` 在底部中央显示本人等级、XP 余量/当前门槛和填充条，保留顶部波次与中央选择空间。HUD 和玩家任意先后创建、失权、重连均沿用现有 Owner 入口。
8. Stop/World 禁用清理球与订阅；飞行显示对象在完成、目标消失、断线或新局时销毁。没有第二套经验存档或镜头同步协议。

## 验证记录

测试从正式 Boot 启动，使用正式 NetworkEnemyBase、Circling 和 Physics2D。只有验证夹具会暂停武器自动触发、锁定角色位置/敌人追逐或通过确认击杀边界造球；至少一条击杀完整通过真实武器、攻击准入、GAS、服务器确认死亡和网络领取。

| 检查 | 结果与证据 |
|---|---|
| 网络 EditMode 测试 | 358/358 通过：`Logs/M6/editmode-1.xml`，包含真实曲线、100 级后、F5 余量、全局累加器、重复死亡和非法配置。 |
| M2–M5、Ultimate 与 M6 PlayMode | 38/38 通过：`Logs/M6/regression-1.xml`。覆盖出生位置来源、请求拒绝不消耗、倍率、禁用/Stop、逐级队列及真实武器击杀。 |
| Host + Client，80 ms 延迟 / 10% 不可靠快照丢包 | 首轮通过：`Logs/M6/Host-20260910-190701-197-p7988`。双方争抢只授予总计 4 XP；重复请求不授予；领取前、入账后飞行中、Equipment 目标阶段断线重连通过；两进程退出 0，玩法错误 0。 |
| 最终 HUD/显示根节点与加载测试 | 8/8 通过：`Logs/M6/playmode-final.xml`；新增可见球在实际拾取范围内的检查，确认两个 XP 纹理接线及加载/卸载。 |
| 最终同版本 Host + Client | `Logs/M6/Host-20260910-191712-833-p7988`；80 ms / 10% 丢包，全部断言通过，2 个进程退出 0、玩法错误 0。 |
| 最终同版本带窗口 server-only + 双 Client | `Logs/M6/Dedicated-20260910-191712-834-p7989`；80 ms / 10% 丢包，全部断言通过，3 个进程退出 0、玩法错误 0。 |
| 构建与实际画面 | `Logs/M6/build-final.log` 成功。已查看最终两端的经验球、XP 条及选卡 HUD 截图；原始截图保存在上述目录（unclaimed、before-race、race-award、level-7-target）。 |

首轮环境中的 FMOD bank 是 Git LFS 指针，导致 ERR_FORMAT；已使用本地已有 LFS 对象还原真实 bank，不改音频资产版本。之后进程无音频错误。

画面检查修正了两个自动数值断言无法发现的问题：空 Sprite 导入表导致经验条显示白条，以及导出 XP_0 根节点旧坐标导致可见球偏离实际掉落位置。最终构建已验证贴图填充与经验数值变化，经验球出现在对应 Enemy 死亡位置。

旧 Circling 碰撞夹具使用预估位置，未考虑球自身碰撞体偏移及当前缩小后的 Enemy 碰撞体。M6 和原 M3 夹具改为对齐实际 Collider.bounds；未调整正式 Enemy 尺寸、武器伤害或击退倍率。修正后 M3 同 root 五次真实命中、位置位移与 canonical HP 对齐通过（`Logs/M6/playmode-5.log`）。

## Host 领取动画修复

人工验收发现 Host 没有后退和飞行动画，独立客户端正常。断点是 `TryCollect → gem.RpcPresentCollection → NetworkServer.Destroy`：已安装 Mirror 的 `LocalConnectionToClient.Send` 将 Host RPC 排队，而 `NetworkServer.UnSpawnInternal` 立即移除共享对象的 `NetworkClient.spawned` 条目；随后 `NetworkClient.OnRPCMessage` 找不到 gem，静默跳过表现。远端在可靠通道中先处理 RPC，再处理销毁，因此能正常分离显示对象。

修复仅在原表现入口增加 Host 本地同步执行，先分离已有 visual，再发送原 RPC 和销毁网络球；不延迟网络销毁，不改 XP 归属、倍率、领取校验、动画参数、Prefab 或场景。共享 `presented` 记录阻止本地调用与 RPC 重复创建动画，server-only 不创建表现。

- 修复前新增正式 Boot 测试失败：Host 领取后期望 1 个飞行对象，实际 0 个（`Logs/M6HostFlight/before.xml`）。
- 修复后 9/9 M6 PlayMode 测试通过（`Logs/M6HostFlight/after.xml`），包含后退/返回轨迹、网络球立即销毁后动画仍在、重复本地/RPC 表现仅一次、动画不发奖、飞行中断线清理与真实 Circling 击杀拾取。
- 原测试只检查 0.5 秒后动画清空，并未断言它曾出现；此前通过记录不能证明 Host 播放正常。多进程夹具现增加每端实际轨迹、可见渲染、获胜者身份、单个动画和 Host 观看远端拾取的断言，并捕获后退与飞行两帧。
- 同版本构建成功：`Logs/M6HostFlight/build.log`。正式 Boot 的 Host + Client（`Logs/M6/Host-20260910-201029-616-p7988`）与 server-only + 双 Client（`Logs/M6/Dedicated-20260910-201029-616-p7989`）在 80 ms 延迟、10% 不可靠快照丢包下通过；5 个进程退出码均为 0、玩法错误均为 0。
- Host 与远端对同一球都记录到后退 1.000 单位，再从出生点朝领取者前进约 0.94 单位的逐帧采样（剩余帧在 0.4 秒结束时清理），接收目标身份一致。Host 自己获胜、Host 观看远端领取均覆盖；无重复动画、重复 XP 或重连/Stop 残留。已查看 Host 后退、Host 飞向获胜者及客户端飞行截图；截图名为 `host-flight-back.png`、`host-flight-to-winner.png`、`client-flight-to-winner.png`。

## 人工验收

构建：`Builds/M6Experience/M6Experience.exe`。带窗口运行时使用 `-force-d3d11`；同一构建可分别启动 Host/Client 或 server-only/两个 Client。自动复验入口：`Tools/Run-ExperienceProcessValidation.ps1 -CaptureFrames -VisibleWindows`，增加 `-Dedicated` 切换服务器组合，增加 `-Simulation` 使用延迟和丢包。

1. 从 Boot 连接两人，Host/服务器点击“开始本局”。观察同一波次继续推进，只有本人升级选择会限制本人的操作。
2. 在距离玩家超过 2 单位的位置击杀 Enemy，观察 XP 球；击杀当时两人 XP 都不应因此增加。前四次击杀掉落，此后隔次掉落。
3. 两人靠近同一球争抢，只有一人的 XP 增加 4；另一人保持不变。观察实际后退和飞向获胜者的方向，不能拉动玩家或怪物根节点。
4. 保持选择菜单打开，靠近另一颗球；本人不吸取，另一名玩家仍可领取，波次仍继续。底部等级/XP 与顶部波次、中央选择按钮均可读。
5. 连续拾取越过门槛，按获得等级逐次选择；也可用 F5 每次加一级，已有 XP 余量应保持。第 4/12/18 级武器、7 起奇数级 Perk 和其他 Equipment 继续沿用 M4。
6. 分别在领取前、确认后飞行中、选择第一阶段和目标阶段断线重连；剩余球仍在，已领取球不再出现，XP、等级、Build 与原候选恢复。失权/断线不保留旧 HUD 或拉取动画。
7. 有剩余球和待选奖励时 Stop，再启动并手动开始新局；回到等级 1、0/19 XP，旧球、旧队列和旧局数据均不残留。

自动断言与截图检查不能替代操作者对吸取方向、持续战斗观感和可读性的最终确认；这些保留为人工 review。

## 回退

M6 使用独立提交。完整回退该提交，必须一起撤销网络拾取授予、动态曲线、HUD/资源/Boot 接线与测试扩展，并恢复原击杀者直发 XP、固定门槛和相应 F5 行为。不要只回退 OnConfirmedKill 或只删除经验球，否则会双发或无奖励。停止旧进程后统一重建，再从新局验证；不混用版本、不局内热回退。保留 M1–M5、音频和 EnemyBase 尺寸。
