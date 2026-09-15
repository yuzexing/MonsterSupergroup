# Limbo 参考基线实施记录

当前交付已扩展为 **Limbo 0–389.6 秒技术预览**：B／围栏和全员屏外机制已完成本轮技术矩阵，Dash v0／v1 已接入并通过双端机制验证；单人 Host、Host／Client 连续流程及结算界面重开已有记录。详见 [Dash 接入与连续技术验证](limbo-dash-integration.md) 和 [空间机制收尾](limbo-spatial-closure.md)。当前共接入6类来源身份，LostSoul／Ghoul 及完整720.9秒仍未放行。人工压力留待整体接入完成，尚未完成同条件原游戏压力对照。

下文保留此前0–60秒阶段的实施与验收历史（其中Imp数据缺失门槛是当时状态）。当前可玩入口是 `-Profile dash -BuildDirectory Builds/LimboDashRelease20260915`；`dash-validation` 含明确测试辅助，不能当作人工压力结果。旧 opening／imp／stage2 入口保留，默认仍为60秒开场。单人技术局发现的现有Ovid材质显示提示已列入Dash报告，未隐去。

## 启动、修改与证据

在项目根目录运行：

```powershell
# 手动游玩的单人 Host；复用 Boot、准备房、KCP、Gameplay、现有结算流程
./Tools/Run-LimboReference.ps1 -Role host -RunName manual-opening

# 有画面的自动移动输入；没有生命、伤害、位置传送等作弊覆盖
./Tools/Run-LimboReference.ps1 -Role host -AutoWalk -RunName walk-opening

# 两个终端依次执行，Host 等待双方通过现有准备房准备完成
./Tools/Run-LimboReference.ps1 -Role host -AutoWalk -WaitFor 2 -Port 7994 -RunName pair-opening
./Tools/Run-LimboReference.ps1 -Role client -Port 7994 -RunName pair-opening
```

需要先构建时，通过 Unity 执行 `MonsterSupergroup.NetworkCombat.Editor.LimboReferenceAssets.CreateAndBuildBatch`。
可执行文件为 `Builds/LimboReference/MonsterSupergroupLimbo.exe`。不带 `--limbo-role` 启动时沿用普通产品流程和普通波次。
脚本不会结束其他游戏进程，也不会覆盖同名运行日志；使用新的 `RunName`。
参考启动脚本默认使用 D3D11。最后一次 D3D12 双窗口显示检查出现了原生 GPU device error，日志保留在 `full-gate-delivery/host/player.log`；根因尚未定位。`-GraphicsApi d3d12` 可显式选择原后端，项目全局图形设置没有修改。
升级必须通过现有界面选择，自动移动不会自动选卡。

日志保存在 `Logs/LimboReference/<RunName>/<role>/`：

- `spawns-*.csv`：仅服务端输出；生成尝试、成功／失败、死亡、无经验淘汰、重定位、预览结束。
- `host-audit.jsonl` / `client-audit.jsonl`：各端出生属性、每秒波次快照、玩家位置、逐次生命变化。
- `player.log`：Unity 与现有 Mirror 日志。`process.pid` 标识本次脚本启动的进程。
- `Tools/Analyze-LimboRun.py <运行目录>` 汇总为 `summary.json`；未到终点的记录不会标为完成。

## 数据与代码位置

均为项目内相对路径；以下根目录为 `Assets/_Project/`。

| 内容 | 位置与说明 |
|---|---|
| 来源记录 | `Content/NetworkCombat/Limbo/limbo-source.json`；来源路径、行号、资产 fileID、SHA256、31 个片段、曲线切线、全部被引用变体。`Tools/Export-LimboReference.py` 只读取来源，输出数值，不导入其代码或资源 |
| 原始／适配敌人数据 | `Content/NetworkCombat/Limbo/SourceEnemyDB.asset` 与 `AdaptedEnemyDB.asset`，均为既有 EnemyDatabase 类型；运行只读适配数据。当前 HP、伤害、速度、基础 XP 为 1:1 |
| 时间轴 | `Content/NetworkCombat/Limbo/Limbo.playable`，31 个独立来源片段；不把 250 预算压缩到预览时间 |
| 开场规则 | `Content/NetworkCombat/Limbo/Resources/LimboReference/Opening.asset`；终点 60、来源时长 841.5766649882、上限 1000 |
| 完整规则草案 | 同目录 `Full.asset`，终点 720.9；LostSoul／Ghoul 行为适配和完整流程未完成，启动门槛继续拒绝 |
| 扩展现有调度 | `NetworkCombat/Timeline/NetworkEnemySpawnClip.cs`、`NetworkWaveTimelineCompiler.Reference.cs`、`NetworkCombat/Server/ServerWaveSchedule.Reference.cs` |
| 联网出生与生命周期 | `NetworkCombat/Mirror/NetworkGameplayEnemySpawner.Reference.cs`、`.Reposition.cs`、`NetworkEnemySimulationAgent.Birth.cs`、`NetworkEnemySimulationWorld.Reference.cs` |
| 启动和审计 | `NetworkCombat/Mirror/LimboReferenceLaunch.cs`；仅显式命令行启用 |

修改 `AdaptedEnemyDB.asset` 调整参考运行属性；修改 `Opening.asset` 中的参考配置调整种子、上限和空间参数。来源数据库保持原始记录。资产生成器不会覆盖已有适配资产的调参。
不要通过删除 `missingEvidence` 字符串来绕过验证；必须先补齐对应攻击证据、行为、Prefab 注册和真实运行验证。

## 已实现的规则

- C：100 段右端采样、累计密度归一化、反求整数生成时间戳、相邻时间戳的相对等待。保留 `WaitWhile` 和间隔等待各自的帧边界；不追赶卡帧期间的漏事件。达到来源计数恰好等于上限时终止，剩余预算放弃；位置失败消耗机会。
- L：在批次边界按本生成器存活数补缺口，逐只等待；批次中不重查全局上限，允许已开始批次跨片段结束。累计生成没有固定总预算。
- B：共享陷阱占用、第一次延迟后捕获玩家中心、再次延迟后同帧整批生成；椭圆半径 6、比例 1.41；不检查或增加来源全局存活计数。围栏已接入同一占用入口，互斥、概率等待、暂停及取消记录见空间收尾报告。
- 同一服务端刷怪入口独占现有 `NetworkWaveProgress`。客户端只接受出生属性和状态；不编译执行自己的刷怪进度，不各自抽样移速。
- 出生值在首次服务端 Combatant 注册前写入；`Reset → 属性配置回调 → Combatant 绑定` 保留既有生命与伤害系统。首个最大生命注册增加了不变量检查。
- 开场 Brotchi v0：HP 20、接触伤害 50、速度 `3 × U(0.9,1.1)`、局部接触圆半径 0.55。伤害仍走既有接触伤害、玩家无敌和网络健康报告。
- 出生 XP：`基础 XP × (1 + 1.5 × Cxp(出生时间 / 841.5766649882))`；曲线保留关键帧切线。没有时间／波次 HP 或伤害倍率。
- 普通出生在当前镜头边界外 2，加当前外观边界；100 次主尝试和 100 次备用尝试。方向分布遵循来源主分量落边算法，合法性使用当前地图的障碍和可达检查，不复制来源反向导航判断。
- 重定位代码：30 秒追踪宽限、屏外 5 秒或距镜头边界 20；移动方向 90° 锥形、屏外 1.5；普通敌人恢复条件、清除出生速度倍率，精英保留条件。位移使用现有模拟权 epoch 和 checkpoint，旧 epoch 位移被拒绝。到期敌人仅在屏外处理时无经验销毁。
- 60 秒有限结束进入既有结算流程；结算说明读取服务器结束原因。单人 Host 选卡通过既有 PauseManager 暂停游戏和参考时钟，关闭后恢复。多人不增加全局选卡暂停或人数倍率。

## 差异与未完成项

| 项目 | 当前状态与限制 |
|---|---|
| 外观 | 使用既有 `NetworkEnemyBase` 的红点外观，占位；未导入参考美术。显示大小与来源不同，不能据此判定可读性和躲避手感一致 |
| 主动攻击 | 来源攻击数据已恢复。Imp、Skeleton、Elite、Dash 已接入并有双端机制记录；Ghoul、LostSoul 待适配。连续记录与压力结论分开 |
| 围栏 | 现有 BarrierTrap 的服务端生命周期、双端展示／碰撞、暂停、取消、释放和重开已完成本轮技术矩阵，生成机制 Ready |
| B 展示 | 固定中心预警、一次生成、独立占用释放及围栏互斥已通过本轮双端技术矩阵；Ready 不自动解除尚未实现的敌人行为门槛 |
| 重定位／淘汰 | 已补服务端继续模拟时的位置应用；按全体活跃参与者视野判定。宽限、重新入屏、两个入口、找点失败、普通重置及精英例外已有独立画面记录 |
| XP／玩家成长 | 玩家当前基础 XP 修正仍为 2，来源为 1；当前掉落控制和升级选择保持产品现状。出生 XP 倍率已还原，不等于完整成长速度已校准 |
| 武器／玩家 | 参考启动通过准备房选择已有 ID 1。实际基础生命 500、移速 4.55；现有自动攻击基础伤害 19，不能将 19 当作 DPS。升级选择会改变后半段输出 |
| 镜头／地图 | 保留当前镜头与 Nordic 地图；可视高度约 16.782，来源 24；当前地图约 119.339×67.128，来源名义生成布局 150×150。几何、路径与玩家移动路线会改变实际密度 |
| 暂停 | 已对齐单人选卡的 Time.timeScale 暂停；网络状态／持续伤害等按网络时钟计时的完整暂停效果仍需专测 |
| 结束 | 60 秒为预览截断。720.9 秒配置及完成事件已保留，但完整场景、忙碌等待转场、Minos 战均未验收 |
| 多人 | 当前只做正确性验证，没有 2／3／4 人平衡设计。准备房现有规则只允许本局原始成员重连，新身份中途加入会被原系统拒绝 |
| 原游戏效果 | 没有启动原游戏；没有原游戏实际存活曲线、击杀用时和录像，不能宣布效果复刻完成 |

## 验证记录

最终运行结果见本文件后续记录及 `Logs/LimboReference`。早期调试运行不可替代最终版本验收。

- 规则测试：`LimboReferenceTests`、`TimelineWaveTests`、`RunSessionTests`，21 项通过，见 `editmode-release.xml`。
- Unity PlayMode 协程对照：`LimboCoroutineTimingTests` 通过；C/L 零秒间隔在真实 Unity 中的连续出生帧间距与参考等待流程一致，见 `coroutine.xml`。这不是原游戏试玩。
- 来源完整性：重新计算实际引用 Timeline、EnemyDB、Limbo 场景的 SHA256，与本次提取清单全部一致；没有修改这三份来源资产。
- 早期有画面单人 `host-walk`：到 60 秒，98 次成功生成、67 次死亡、结束存活 31、生命 450/500；98 份出生属性一致。该记录在补齐额外 WaitWhile 和全局选卡暂停之前，仅作为发现问题的证据。
- 早期有画面双端 `pair-first`：两端共同观察 102 个敌人 ID，属性无差异，服务端单份生成日志、两端到达 60 秒；同样不是最终时序版本的验收。

### 最终时序版本的有画面验证（2026-09-14）

| 记录 | 已观察结果 |
|---|---|
| `host-final` 第一局 | 原开场片段 `1–136`、预算 250 不变，预览到 60 秒；93 次尝试／成功、0 位置失败、14 次死亡、79 只存活，玩家 500/500；93 份出生属性匹配。36.202 秒进入升级时，游戏与波次时钟保持暂停，选择“重振”并装备当前武器后恢复 |
| `host-final` 第二局 | 从结算页点“重新开始”：原敌人清理，新的 RunId 和 round=2，波次／存活归零重新生成；观察至 29.4 秒，43 次成功生成、7 次死亡、36 只存活，再次进入升级暂停。此局仅用于重复开局检查，未跑至终点 |
| `pair-final` | Host 自动移动、Client 站定，两端实际窗口都已观察。服务端到 60 秒生成 92 只、死亡 6、存活 86；两端共同记录同样的 92 个敌人 ID，HP、伤害、移速、XP 无差异，Client 没有第二份刷怪日志，两端同步进入正确预览结算页 |
| `pair-final` 远端受伤 | Client 从 500 降至 0，共 10 次、每次 50 伤害，最短实测墙钟间隔 0.6183164 秒，未观察到同次接触重复扣血；约主时间轴 13.061 秒倒地，Host 存活并继续推进至 60 秒 |
| `full-gate-final` | 使用 Full 配置启动，进入战斗前明确记录 `[LimboGate]`：Imp 缺少原始动画绑定／时序及子弹运动参数。没有生成敌人；这项检查只验证禁用门槛，不代表完整关卡验收 |
| `gate-d3d11` | 最终交付构建以 D3D11 启动，画面清晰显示完整配置未启用及具体缺失证据；0 次生成 |
| `pair-d3d11` | 最终启动脚本默认后端的双窗口复验：两端到达 60 秒，共同观察 98 个敌人 ID，出生属性无差异；服务端 98 次成功、6 次死亡、92 只存活，Host 500/500，Client 10 次各 50 伤害后倒地。两端结算画面已保存；本次没有 GPU 崩溃或托管异常 |

`host-final/summary.json` 包含两局分组，第二局未完成，不把第一局的完成状态套到第二局。截图位于对应角色目录或 `pair-final` 目录。
这些实际出生数受逐次等待、帧率与暂停影响，**不是要求固定等于 104 的验收目标，也不是全关卡敌人总数**。
上述开场中未自然触发重定位或到期淘汰，不能据此宣布这两个机制通过有画面验证。

上述开场记录之后，L、B、重定位、精英例外和部分暂停／复用／交接矩阵已继续完成，见阶段二及空间收尾报告。完整持续状态、原始成员重连／复活矩阵、完整关卡转场和同条件原游戏压力对照仍待后续。

## 下一阶段

攻击字段现已从原始运行副本恢复，两次独立资源快照一致；详见 [敌人攻击数据恢复结果](hellmaiden-enemy-recovery.md)。Imp 已完成现有攻击／GAS／Mirror 接入及双端画面验证，对应门槛已解除；旧 Stats 引用的修正已记录为适配差异。完整混合关卡继续禁用，其他敌人的实现／运行验证门槛保留。

Skeleton、Elite_Skeleton 独立攻击／几何与接触六组合记录见 [阶段二记录](limbo-stage2-integration.md)。此前有长选卡／操作缺口的局保留为历史，不作为压力基线。本轮已补齐空间技术矩阵、Dash双端验证及389.6秒技术连续流程与重开；下一步按主时间轴接入 LostSoul v0／v1，完成独立机制后再扩至449.916666…秒，不提前启用Ghoul或Full。人工玩法压力仍交由用户在整体接入后测试；当前技术辅助及现有Ovid显示问题均有记录。
