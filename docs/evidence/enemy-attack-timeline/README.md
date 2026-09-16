# 敌人范围攻击共同时钟：验证记录

日期：2026-09-16。实现、自动化、隔离机制、最终候选的单人／双端完整技术运行及界面重开已完成。保留全部中间失败和低帧率运行记录，后续画面收尾使用同一候选 05，未覆盖历史证据。

## 构建与来源

- 原 Phase 6 人工包保留：`Builds/Limbo-P6-20260916-02.zip`，SHA-256 `9A19885ED4E2FF609119833FBB9977993A0258ED96685129260FFA0F02830670`。
- 当前候选：`Builds/EnemyAttackTimeline20260916-05/MonsterSupergroupLimbo.exe`。完整文件哈希见 `Logs/EnemyAttackTimeline/build-r5-manifest.json`，构建结果见 `build-06.json`。
- 压缩包：`Builds/EnemyAttackTimeline20260916-05.zip`，SHA-256 `5B7699730236588350EA6F5451B5ABEA6E81E83A396CD0BF357B4B2434F50ED3`。
- 源码包含未提交修改，不能仅以 HEAD 表示本次构建。最终源文件差异、哈希与测试报告另存于同一日志目录。
- 不修改数值、源动画、命中几何或波次资产，不以这些技术运行判断玩法压力。

## 修复前复现与修复

| 问题 | 复现 | 修复范围 |
|---|---|---|
| 骷髅只接收 Warning 后不能自主进入 Active | `repro.xml` | 单击与三连击共用时间求值；所有客户端角色唯一执行入口 |
| Dash 检查点恢复提前推进曲线游标 | `attack-regression-02.xml` | 恢复运动状态与正常模拟 Tick 分开 |
| 动作开始前的击退命令误取消新动作 | `interrupt-zero-repro.xml` | 零 ActionId 表示接纳时没有动作，不能作为通配符 |
| 迟到 Recovery 粒子恢复可能继续发射 | `lostsoul-tail.xml` 中顺序／迟到采样对照 | 按原 Active 发射，再停止发射并模拟尾部 |
| LostSoul 取消 Active 后身体受击区未恢复 | `lostsoul-cancel-repro2.xml` | 明确取消与正常释放分开；只恢复存活敌人的身体状态 |

保留误选碰撞组件的第一版复现 `lostsoul-cancel-repro.xml`；该失败是测试初始化问题，真正运行缺陷由第二版复现证明。

## 有画面隔离运行

所有 `*-fixture` 明确包含定位、补血、暂停／慢动作或交接辅助，不能作为人工压力证据。`warning-only` 主动屏蔽接收的 Active／Recovery 消息，保留启动、逐击合法姿态和取消。

| 对象 | 原始目录前缀 | 说明 |
|---|---|---|
| Skeleton v0/v2、Elite v0/v1 | `Logs/LimboReference/attack-stage2-r1-*` | 候选 01；Host／Client、四象限、交接，实际扣血 50／200 |
| Dash v0/v1 | `Logs/LimboReference/attack-dash-r2-*` | 候选 02；两种目标端，实际扣血 50／80；模拟运动与本地命中分离 |
| LostSoul v0/v1 | `Logs/LimboReference/attack-r3b-lostsoul-main-*` | 候选 03；四组合，实际扣血 50；取消恢复缺陷修复前的专项记录，不能证明该缺陷已修好 |
| Ghoul 首轮 | `Logs/LimboReference/attack-r3-ghoul-main-*` | 已覆盖交接，但原夹具部分象限不足三套，保留为不完整证据 |
| Ghoul 补验 | `Logs/LimboReference/attack-r5-ghoul-main-*` | 候选 05；前 80 秒固定测试目标相对方位，之后恢复原交接、暂停、慢动作和自然武器击杀探针 |

`attack-timeline.jsonl` 是本地实际阶段／窗口／中心记录；旧专项观察器在屏蔽消息的情况下可能显示最后收到的 Warning，不能据此推断本地仍处于 Warning。有效扣血从健康变化记录核对，伤害尝试另列。

## 完整流程

- 候选 02 的单人运行及界面重开已记录，仍属于中间构建。
- 候选 03 的单人／双端完整运行因发现取消恢复缺陷主动中断，目录内有 `validation-interrupted.json`，不计为完成。
- 候选 05 的 `attack-full-solo-r5` 已于 721.1871798429638 秒请求并结算；`attack-full-pair-r5` 于 721.0149232414551 秒请求、729.4746281639673 秒结算。配置点仍为 720.9，实际请求发生在首次跨过的帧。双端显式注入的选卡等待正常提交；等待期间又生成 8 只。
- 第一轮出生快照：单人 1583；双端各 2548，netId／属性集合完全一致。错误属性为 0，结束伤害区、预警和陷阱清零。该轮结算截图已补存，随后通过界面进入第二轮。

## 最终测试与统计限制

- PlayMode：529/529。
- EditMode 首轮：784/785。`LustSinnerVariantTests` 还原真实资产时遇到 Windows 1224 文件映射占用，留下临时 HP 31；已恢复原资产，测试使用 `AssetDatabase.ReleaseCachedFileHandles()` 后再还原原字节，完整复验 785/785。保留两轮 XML，不将首轮失败删去或改成通过。
- Ghoul 中断专项旧 Update 观察器有一条“动作清零但 Collider 尚开启”的重定位中间态：enemy 15 在 combat 33.11572437547835 变更 epoch 3→4，唯一执行器同帧 LateUpdate 关闭。碰撞入口的 epoch 检查拒绝旧窗口；新窗口日志无越界，旧 epoch 没有继续扣血。保留原观察记录及 `ghoul-r5-interrupt-windows.json`，不只凭 Collider 的瞬时值判定有效伤害。
- 双端总生成 2548；存活账本移除 2429、自爆 47、淘汰 13、结束存活 59，守恒差为 0。健康观察器捕获确认击杀 2427，少 2 条回调：enemy 321（Brotchi）和 680（Rusher）均在同一关卡时间出生并从存活账本移除，随后有 HP→0 记录。观察器回调缺口单列，不能将 2429 全部伪装为已观察的确认击杀；这两只不能用于首伤到死亡样本。
- 第一轮完整运行的每秒区间平均帧时间再取均值：单人约 349.74 ms、双端 Host 106.17 ms／Client 55.82 ms。运行时多个实例并行且开发面板开启，安全提示阻挡了关闭面板操作；不能据此单独归因于本次攻击改造。原始低帧率证据保留，关闭面板后的第二轮另列如下。

## 最终界面重开与第二轮完整运行

用户取消安全提示后，经实际界面操作完成重开，未修改运行代码、资源或构建。三端均为 Round 2，开始时生成／存活／等待计数为 0，玩家 500 HP、4.55 移速、XP 修正 2、武器 ID 1。

| 环境 | 新 RunId | 请求时间 | 结算时间 | 出生观察数 |
|---|---|---:|---:|---:|
| 单人 Host | `c0fb77b685b64f85bfb2bc928d6c1457` | 720.9165415614843 | 720.9165415614843 | 3243 |
| 双端 Host／Client | `7558dddbfa49424c9f9b78eb0358c3a4` | 720.9023795835674 | 729.1134386826307 | 各 4426 |

- 双端等待 8.2110591 个战斗秒，期间已开始的生成任务继续生成 1 只。Client 的选卡界面在 TransitionPending 期间保留，随后实际提交并结算；Host 期间自然获得选卡也参与等待。
- 两端出生 netId／HP／伤害／移速／XP 集合完全一致，缺失、额外和差异均为 0；单人及双端属性不匹配为 0。每局请求和完成均只有一次。
- 单人账本：3243 成功出生、3053 死亡移除、22 自爆、81 淘汰、结束存活 87，守恒差 0。双端服务端账本：4426 成功出生、4366 死亡移除、11 自爆、0 淘汰、结束存活 49，守恒差 0。死亡移除计数与其他观察器的确认击杀口径分别保留。
- 双端完成快照显示存活 53，随后同一战斗时间的 4 条死亡移除使最终账本为 49；不拿较早快照的数值替代最终账本，也不把清场后的对象数当作结束前存活数。
- 结束检查：伤害区 0、预警 0、陷阱状态／占用／碰撞／预警根均为 0，timeScale=1。三个游戏进程均通过正常关闭退出，日志包含 `process-closed`；无异常或属性不匹配日志。
- 文件：`ui-closeout-r5.json`、`full-solo-r5-after-ui-audit.json`、`full-pair-r5-after-ui-audit.json`。同目录原 `full-*-r5-audit.json` 保留为第一轮分析，不覆盖。

画面：[单人重开](full-solo-r5-restarted.png)、[双端主机重开](full-pair-r5-host-restarted.png)、[客户端重开](full-pair-r5-client-restarted.png)、[客户端转场等待选卡](full-pair-r5-client-round2-transition-wait.png)、[单人结算](full-solo-r5-round2-completed.png)、[双端主机结算](full-pair-r5-host-round2-completed.png)、[客户端结算](full-pair-r5-client-round2-completed.png)。更多中段画面保存在 `Logs/EnemyAttackTimeline`。

### 帧时间复核边界

实际 D3D11、1280×720、Windowed、targetFrameRate=60、vSync=0。关闭 F2/F3 面板后，统计第二轮 35 秒起 Running／TransitionPending 的每秒记录，平均值按实际帧数加权；三个游戏实例在同一电脑运行。

| 环境 | 加权平均帧时间 | 每秒平均值的 P95 | 最差一秒的平均值 |
|---|---:|---:|---:|
| 单人 Host | 18.456 ms | 28.580 ms | 35.624 ms |
| 双端 Host | 18.542 ms | 32.152 ms | 42.141 ms |
| Client | 16.770 ms | 17.057 ms | 18.759 ms |

这里的 P95 是每秒平均值的分布，不是逐帧 P95。单秒日志写入耗时最大分别为 0.728／2.941／0.438 ms，刷新最大为 0.357／3.232／0.325 ms。后半段 Host 有掉帧，不声称全程锁定 60 FPS，也不将此视为独立性能基准；后续人工压力测试应使用无辅助入口。所有自动移动、补血、首个合法选项选择及双端终点奖励注入仍按原技术配置记录。

## 适配差异

普通范围攻击与 Ghoul 一样按绝对战斗时间推进，不累计来源逐段等待的帧误差。LostSoul 在每端首次进入有效 Active 时固定本地爆炸中心；两端可不同，同端特效与九边形必须一致，交接不重新定位。有效中断统一等待服务端接纳，存在确认延迟。玩家伤害继续走 OwnerFinal，不新增范围伤害 RPC。
