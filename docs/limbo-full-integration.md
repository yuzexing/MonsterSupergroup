# Phase 5：完整 Limbo 与转场等待

实施与技术验证记录。最终交付状态以本页末尾验收记录为准；人工玩法压力、Spine、数值校准和 Minos 不在本阶段。

## 实现

直接引用 `Assets/_Project/Content/NetworkCombat/Limbo/Limbo.playable` 的全部 31 个片段，保留原始时间精度：14 C（名义预算 1139）、10 L（累计数取决于补怪）、7 B（名义批量 60）。保留两次围栏、残留和屏外规则。来源清单及哈希见 [source-check.json](evidence/limbo-full/source-check.json)。`limbo-source.json` 的历史 missingEvidence 字段不是当前恢复状态；已恢复攻击证据及已审核适配资产沿用上一阶段。

`GameplayWaveRules.referenceEndPolicy` 明确选择立即预览结束或等待参与者；旧预览保留立即结束。Full 在首次跨过 720.9 秒时进入追加的 `WavePhase.TransitionPending`，不取消选卡、不停止战斗、不清场。快照同步实际请求时间、等待人数和原因。服务端逐帧检查，单人选卡暂停时仍可作出完成决定。

参与者取自 RunSession、Mirror 连接和 GAS 生命事实；存活玩家的有效选卡、装备目标选择及连续提交会阻塞。断开及倒地成员不阻塞；恢复中的未知角色/生命基线阻塞。无合法候选且已解锁的奖励队列不阻塞。全员在线倒地仍优先失败，没有在线玩家不会成功。旧 RunId/Round 不得提交完成。

等待时既有生成任务和攻击继续，关卡时间可以超过请求点；没有自动成功超时。只有可以结算时才调用既有 `CompleteReferenceStage`，执行停止、选卡清理与结算。结算文案为“Limbo 参考流程完成；Minos 战未实现”。

主要实现位置：`NetworkCombat/Server/ServerWaveSchedule.Reference.cs`、`ServerWaveSchedule.Barriers.cs`、`RunSession.Reference.cs`、`Mirror/NetworkGameplayEnemySpawner.Reference.cs`、`BootGameplayNetworkManager.RunEnd.cs`、`NetworkWaveHUD.cs`。没有新增生命、伤害、敌人管理或网络管理系统。

## 配置与技术辅助

`Full.asset` 是无辅助完整配置；`FullValidation.asset` 使用同一 Timeline，技术观察器允许低生命时通过现有入口补血、选择第一个合法选项，`-AutoWalk` 仅提供普通方向输入。玩家初始仍为 500 HP、4.55 移速、武器 ID 1、XP 修正 2、当前镜头及 Nordic。出生 XP 分母为 841.5766649882。

`full-fixture` 用于短时机制检查，关闭武器执行并按明确 case 注入奖励、断线/重连或倒地；不能当作正常完整运行。`cancel` 刻意保持选卡直到通过现有界面退出。`barrier-first`、`burst-first`、`slime-b` 将第二围栏和 Slime v1 B 平移至短时范围，原始强度、时长、几何和概率规则保留。完整连续运行不强制 B 成功。

定向资产工具 `LimboFullAssets.Create` 保留已存在适配值和审核状态；历史敌人审核工具只测试独立流程门槛，不能自动放行或重新降级 Full。启动脚本默认仍是 opening，所有旧入口保留。

最终构建目录为 `Builds/LimboFull20260915`。在项目根目录运行以下命令进入无辅助单人参考流程；不附加 `AutoWalk`，不会自动选卡或补血：

```powershell
./Tools/Run-LimboReference.ps1 -Role host -Profile full -BuildDirectory Builds/LimboFull20260915 -RunName manual-full
```

需要记录技术运行时使用 `full-validation -AutoWalk -ArtObserve`；双端 Host 增加 `-WaitFor 2`，Client 使用相同 Port、RunName 和构建。已有同名运行目录不会被覆盖。`full-fixture -FullCase chain` 等短时入口始终是机制测试，不能作为无辅助完整试玩。

## 验收记录

最终发布构建已完成单人 Host、Host/Client 有画面完整运行和结算界面重开，Full 状态为 Ready。[最终清单](evidence/limbo-full/verification.json)、[构建哈希](evidence/limbo-full/final-build.json)、[31 片段核对表](evidence/limbo-full/31-clips.csv)分别保存结论、版本与逐片段计数。

| 最终构建第一完整轮 | 单人 Host | Host/Client |
|---|---:|---:|
| 实际转场请求秒数 | 720.910100 | 720.918630 |
| 实际结算秒数 | 720.910100 | 729.434841 |
| 成功生成 | 3366 | 3610 |
| 确认击杀 / 自爆 / 无经验淘汰 | 3045 / 15 / 217 | 3511 / 37 / 6 |
| 结束真实存活 / 来源计数 | 89 / 87 | 56 / 56 |
| 生成事件中的真实存活峰值 | 225 | 144 |
| 出生属性不匹配 / 运行异常 | 0 / 0 | 两端均 0 / 0 |

以上均满足“生成＝确认击杀＋自爆＋淘汰＋结束存活”。不是压力测试，不根据两局数量差异推断多人难度。单人第 25 号片段（635.666…秒 Slime B）被围栏占用跳过 5 只，未补排；第 24 号 Dash 曲线生成 59/60 时已满足结算条件，剩余相对等待任务随实际结算结束。双端这两个片段分别生成 5 和 60。不能以名义预算当作保证生成数。

双端从 720.918630 秒等待客户端连续装备选择；728.105161 秒 Host 新选卡加入等待，729.434841 秒全部结束后结算。等待阶段现有战斗持续，未提前取消选择。两端出生 ID、属性及构建哈希一致，结束旧伤害区、预警、陷阱占用与碰撞均为零，时间比例恢复为 1。

两局均通过结算界面重开：单人新 RunId `ff507cf4f3b54d7391fcb9e9710860d5`，双端 `8c3ccf4d1dec4bbb890ace781fbf4271`，Round 2。新轮次初始生命 500，生成/存活/请求时间/等待人数均归零；Client 可继续运行新轮次。重开后的短局单独记账，不并入上表。

最终原始记录：`Logs/LimboReference/full-final-solo`、`full-final-pair`；摘要为 [单人](evidence/limbo-full/full-final-solo.json)、[双端](evidence/limbo-full/full-final-pair.json)。画面包括 `ui-settlement.png`、`ui-restart.png` 和各端带 RunId 的请求、结束及后半段里程碑 PNG。双端 Client 的 `full-d471cbc04dc34319af5dc6cb16beff78-request.png` 显示等待阶段选卡仍有效。

候选完整运行 `full-candidate-solo`、`full-candidate-pair` 保留为历史证据。候选单人请求/完成 720.921201 秒；候选双端请求 720.939010、完成 729.467681 秒。它们不替代上述最终发布构建复验。

隔离记录包括 `full-probe-chain`（连续装备选择及双端界面重开）、`full-probe-solo-busy`（选卡暂停）、`full-probe-unavailable`（保留奖励不阻塞）、`full-probe-reconnect`（原成员重连）、`full-disconnect-r2`（离线不阻塞）、`full-downed-fixed`（全员倒地失败优先）、`full-burst-first`、`full-barrier-first`、`full-slime-b`。前一次 `full-disconnect` 在建立连接前失败，未计验收；`full-downed` 在加载期间错误注入伤害，保留失败并修正测试条件。第一次退出检查发现取消日志重复，已在最终构建复验修正。重连审计保留原始比较：恢复期间空 RunId 导致三个出生记录落入空分组；按已观测的最近非空 RunId 比较后，ID 和属性均一致，修正的是审计归组，不是运行事实。

记录工具 `Tools/Audit-LimboFullRuns.py` 只读取日志；预算、尝试、成功、真实存活、来源计数、确认击杀、自毁及无经验淘汰分开记录。第一完整轮和界面重开的后续轮分开统计。动作证据由通用 `stage2-observation.jsonl` 记录所有身份的配置、动作检查点、阶段、伤害与死亡；不依赖特定 Ghoul 隔离观察器。

无辅助入口的独立冒烟记录为 `full-normal-smoke`：人工通过选项及装备目标界面提交，继续战斗后从菜单退出；没有自动移动、自动选卡或补血。该短局仅证明可操作性，不算无辅助完整通关或压力测试。最终构建的 `full-final-cancel` 在选卡等待期间通过菜单退出，记录一次请求、一次取消、零次完成，退出后旧伤害区/预警为零。历史 `full-cancel-held` 的三次重复取消记录保留；修复仅使停止、禁用和销毁的重复通知幂等。

### 来源与适配边界

| 项目 | 本次处理 |
|---|---|
| 波次、变体、攻击、几何与 XP 出生公式 | 沿用已恢复数据，未增加每分钟强化或人数系数；原始证据哈希不变 |
| 720.9 秒转场 | 原场景请求独立 Minos；适配为等待有效参与者空闲后进入现有结算，并明确说明 Minos 未实现 |
| 等待集合 | 以服务端成员连接、GAS 生命与现有选择事务判断；无候选的保留奖励不等于有效选择事务 |
| 等待期间 | 保留原暂停和战斗规则，没有额外保护、自动确认或超时通关；技术辅助由显式 validation/fixture 入口单独记录 |
| 显示及玩家环境 | 沿用此前原图资源及 Shader 适配限制，保留 XP 2、当前镜头和 Nordic；不是同条件原游戏压力对照 |

短时机制的断线/重连和倒地只验收与转场直接有关的路径，不代表完整成员生命周期矩阵通过。技术运行中的帧时间、粒子及内存采样保存在审计结果；同机多个可见进程同时运行，不用这些采样给出发布性能保证。

## 回归问题与范围

[test-results.json](evidence/limbo-full/test-results.json) 保留 Phase 5 当时的失败消息与调用栈。后续 13 项修复与最新复验见 [回归收尾记录](regression-closure.md)；没有覆盖原始结果。

本阶段当时定向 EditMode 为 **100/100**（`edit-ready.xml`），PlayMode 为 **39/39**（`play-final-flow.xml`）。广域原始结果为 EditMode **749/753**、PlayMode **513/523**；后者的镜头顺序失败随后修正。2026-09-16 已处理剩余 4 项 EditMode 和 9 项 PlayMode，并在当前树完成无过滤广域 **EditMode 763/763、PlayMode 523/523**。新增迁移幂等检查 6 项；当前树另含此前增加的检查。独立图形 Host/Client 及专用服加真实 Client 均通过，范围与测试辅助见新记录。

| 问题 | 处理与影响 |
|---|---|
| 两项 Mirror 异步场景卸载/重连失败 | 菜单 Awake 在卸载中的孤立场景创建 UI。延迟至有效场景完成加载后创建；生命周期套件复验通过 |
| 选卡输入锁测试 | 测试未建立 PlayerStats 基线，正常输入也被未初始化门槛拦截。补齐实际初始化；原断言不变 |
| 三项波次/经验 HUD 文本失败 | 测试默认英文，但语言状态为中文。夹具显式设置并恢复语言；原断言不变 |
| 死亡/复活展示随测试顺序失败 | 初始 owner 500 HP 报告在测试人工展示死亡后抵达。等初始报告完成再测试，14 项顺序回归通过 |
| 4 项 NetworkCombatSandbox | 已补有效连接／Avatar／生命登记，按当前 epoch 与实际下一帧检查中继；EnemyBase 明确使用接触组件，无主动攻击。保留不合格 Endpoint 的零生成反例。广域通过，真实专用服及接管另有图形记录 |
| 玩家 Dash 命令容差 | 已使用合法地图起点及稳定物理位置，保留容差并增加地图外请求反例。自动化和真实 Client 服务端确认均通过 |
| 普通 Orb 击退交接 | 已按现行约定验证剩余轨迹、硬直与去重的继承；冻结和禁用仍清理。未恢复旧“交接归零”行为，广域通过 |
| Ultimate 击退路由 | 已修正地图外夹具，三项广域通过；真实 Client 的一次启动击退及在途交接另有记录 |
| Summon 首次装备窗口 | 已复现测试 Gate 与基线启停竞争并移除双重执行者；保留严格茧存在断言，补重复基线／禁用／重绑。广域通过，首次茧有双端画面 |
| 4 项 EditMode 武器迁移 | 旧默认 2 检查改为验证配置保留与有效引用；Ultimate 缺的是旧 `title` 字段，并非资产丢失，已检查本地化表和条目。两处迁移工具修复已有选择被覆盖的风险，广域通过 |

上述历史失败已收尾；完整成员生命周期矩阵及同条件原游戏正常流程压力对照仍不属于本阶段，不能从广域测试全过推算这些验收已完成。

本次完成范围：全部 31 个敌军片段已整合，完整参考流程到 720.9 秒请求转场，在已连接、存活玩家结束忙碌后进入现有结算；本轮技术验证完成。Minos、可选任务及支援系统完整迁移、人工压力、Spine、XP/镜头校准、完整断线重连/复活矩阵仍不包含在内。
