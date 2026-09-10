# Stop 重开后的 Enemy 死亡缓存与同步回归

2026-09-10。对应人工现场：Host A，Client B 在 F5 选择中，开始刷怪后 B 模拟的怪物在 A 端显示 0 HP 并停止同步。

## 已确认根因

缺少的前置条件是：**同一进程、同一个 Boot 场景中，上一局已经击杀过该编号的 Enemy，再通过网络面板 Stop 重开。** 退出 PlayMode 或新启进程会销毁旧 World，因此只测试全新进程内的一局会漏掉问题。

人工现场有可对应的证据：

- 上一局 `6f0d5010c6ba4f90a5e00603ef3e3913` 的 XP 日志记录 `enemy=5 death=9`。
- 下一局 `69199e98119244bf8f0f394e31b876ea` 重新生成 #5，但 A 的 Debug 列表仍显示 `0/100 Dead v9`；服务器波次把当前 12 只 Enemy 全部计为存活。
- 摘要保存在 `Logs/SelectionEnemySpawn/manual-repro-events.txt`。

调用链：

1. `NetworkCombatWorld.Broadcast` 先发送 canonical RPC，再通知 `ServerCanonicalBatchProduced`。
2. `NetworkEnemyServerDriver.HandleCanonicalBatch` 根据服务器确认死亡立即 `NetworkServer.Destroy`。
3. Host 共享对象的 `NetworkCombatantAdapter.OnStopClient` 此时执行 `Replica.ForgetEntity`；随后 Host 消费排队的 World RPC，又把死亡记录放入 Replica。
4. World 是 **Boot 场景中的网络对象**。Mirror 的 Stop 只重置并停用场景对象，下一局继续复用；原代码仅在 `NetworkCombatWorld.OnDestroy` 清空 Replica。
5. Mirror 服务器重启后重新分配 netId。新 #5 的服务器状态为 `100 HP / v1`，`CanonicalWorldReplica.Apply` 却因本地已有旧局的 `Dead v9/v10` 而拒收较低版本。
6. 新 Enemy 的 `NetworkCombatantAdapter.OnStartClient` 读取旧记录，把 Host 侧本地 HP 也设为 0。`NetworkEnemySimulationWorld.SubmitClientSnapshots` 根据 `enemy.IsCanonicalAlive` 提前返回，因此 B 继续模拟，但 A 不再接纳它的移动快照。

Debug 的 Canonical HP 来自**本地保存的服务器事实副本**，并非每次直接查询 Host 的服务器 Ledger。此次两者的矛盾来自旧局缓存，不是 F5 授予了怪物伤害，也不是 KCP 延迟开关要求两端一致。其他 local/canonical HP 差异不能据此一概归因。

## 修复范围

`Assets/_Project/NetworkCombat/Mirror/NetworkCombatWorld.cs` 增加两个客户端生命周期回调：

- `OnStopClient` 清空 Replica，包括已销毁实体留下的死亡记录、状态记录和订阅。
- `OnStartClient` 再清空一次，确保复用的 Boot World 在本次实体注册与基线到达前没有旧会话事实。

运行期版本校验、服务器伤害准入、单一 SimulationOwner、快照权限、F5 选择锁定和免疫继续使用现有实现。无需变更 Scene、Prefab、协议或角色 Build。

## 先复现，再修改

测试提交 **`66ff5cf`** 只有测试与启动脚本，正式代码仍为修复前版本。可以检出该提交重新构建以验证失败。

新增测试：

- `GameplayWavePlayModeTests.CanonicalDeath_StopRestartWithSelection_DoesNotPoisonReusedEnemyId`：正式 Boot，确认死亡、Stop、复用 World、F5、重新出生，比较 Ledger、Replica 和本地 HP。
- `GameplayWaveProcessProbe` 的 `--m5-selection-before-run` 分支：第一局使用真实 Circling、Physics2D、网络结果提交产生确认击杀与销毁；Stop 后第二局 B 保持正式 F5 选择，验证复用同一 Enemy netId、HP、持续移动及最终收敛。
- `Tools/Run-WaveProcessValidation.ps1 -SelectionBeforeRun` 启动上述流程。标记文件仅协调测试，不参与正式网络状态。

多进程测试为隔离断言，仅在运行时关闭自动武器执行并保护玩家；第一局显式触发真实 Circling 攻击，固定球角速度并对齐碰撞位置。未向 Replica 注入假死亡，未修改正式武器、Enemy 或地图资产。第二局观察 8 秒，再给 Observer 2 秒收敛时间，允许误差不超过 0.02 世界单位。

修复前后没有修改该回归测试。三份测试/启动文件的 SHA-256 已核对一致，记录位于 `Logs/SelectionEnemySpawn/regression-source-hashes.json`。

| 验证 | 结果与证据 |
| --- | --- |
| 修复前 PlayMode | 1/1 失败，`Expected: 100, But was: 0`；`Logs/SelectionEnemySpawn/before-playmode.xml` |
| 修复前完整 Host 回归，第 1 次 | 服务器 `HP100 v1`、Host `HP0 v9`，Host 不移动，位置误差 **4.8132**；`Logs/M5/Host-20260910-213345-402-p7996` |
| 修复前完整 Host 回归，第 2 次 | 相同失败，旧版本为 v10，误差 **4.8132**；`Logs/M5/Host-20260910-213345-402-p7997` |
| 修复后 Host 回归，第 1 次 | 两进程 PASS / exit 0；HP100，持续接纳快照，最终误差 **0.0000**；`Logs/M5/Host-20260910-213556-336-p7996` |
| 修复后 Host 回归，第 2 次 | 两进程 PASS / exit 0，最终误差 **0.0000**；`Logs/M5/Host-20260910-213814-793-p7996` |
| 修复后 Host 延迟/丢包 | `LatencySimulation` 80 ms、10% 不可靠包丢失，两进程 PASS / exit 0，最终误差 **0.0000**；`Logs/M5/Host-20260910-214158-909-p7996` |
| 修复后 PlayMode | 波次、Enemy Debug、XP 三组共 **23/23** 通过，包含新增回归；`Logs/SelectionEnemySpawn/after-playmode.xml` |
| 修复后 M6 Host＋Client | PASS / 两进程 exit 0；覆盖真实击杀、拾取、断线恢复、选卡恢复、Stop；`Logs/M6/Host-20260910-213704-449-p7990` |
| 修复后 M6 server-only＋双 Client | PASS / 三进程 exit 0；`Logs/M6/Dedicated-20260910-214005-720-p7991` |

之前三个仅检查 HP 的诊断运行同样失败；上表以补全位置断言后的两次完整回归作为主证据。

## 额外检查与未通过项

- 新的 server-only「两客户端同时重连到重开的新局」分支暴露 `NetworkCombatantAdapter.OnStartClient → ConfigureEntityId(0)` 初始化异常。修复后的首次运行虽完成 HP/位置断言，但 client2 记录异常；再次运行也失败。该组合**未记为通过**。
- 已使用正式代码未修复的对照构建重跑同一分支，在 `Logs/M5/Dedicated-20260910-214050-990-p7998` 中复现相同 `netId=0` 异常。这是额外发现的既有启动问题，尚未定位修复；未通过放宽断言或忽略错误掩盖它。
- D3D11 截图运行 `Logs/M5/Host-20260910-213642-483-p7997` 的两端玩法日志均 PASS，但客户端退出码为 `-1073741819`，隐藏窗口截图为黑色。因此不计为完整进程通过，也不计为实际画面验收。
- 本轮修复后已完成独立进程和 PlayMode 验证；原主项目＋clone_0 的双编辑器人工画面仍需按下面步骤复验。

## 重跑与人工复验

已保留两个同测试源码的本地构建：

```powershell
# 修复前：应稳定在 Host 的旧死亡状态/位置断言失败。
./Tools/Run-WaveProcessValidation.ps1 -Executable Builds/SelectionRestartBeforeFix/M6Experience.exe -SelectionBeforeRun -Port 7996

# 修复后：应通过相同断言。
./Tools/Run-WaveProcessValidation.ps1 -Executable Builds/SelectionRestartFixed/M6Experience.exe -SelectionBeforeRun -Port 7996
```

常用 `Builds/M6Experience/M6Experience.exe` 也已恢复为修复后的构建。完整构建与运行日志均保留在本地 Logs；未把构建或原始日志加入 Git。

人工使用主项目与 clone_0 的同版本代码：

1. 两端从正式 Boot 进入，Host 开始本局，确实击杀一只分配给 B 的怪物，记下其编号。
2. 使用网络面板 Stop；**保持 PlayMode 和 Boot 场景，不退出重进 PlayMode**。
3. A 再次 Host，B 再次加入。B 按 F5 保持选择，A 开始本局。
4. 检查复用编号的 Enemy：A 的 Canonical/Local HP 应为新局值；B 模拟、A 观察的移动一致，状态不带入旧局。
5. B 完成两个选择阶段，继续正常战斗；再 Stop 重开确认无旧死亡记录。

回退正式修复只需撤销 `NetworkCombatWorld` 的两处客户端会话清理；测试提交保留可重现原缺陷。没有数据或网络协议迁移。
