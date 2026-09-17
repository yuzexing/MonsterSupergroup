# Ghoul 三连击与原始美术接入

2026-09-16 修正：此前接入遗漏了来源 `EnemyWarningStep(0.4)`，不能将“三连击停止普通导航”解释为整个攻击没有位移。每击 Warning 有独立前进，Active/Recovery 停止；修复及当前验收边界见 [出生动画与 Ghoul 预警前进修复](limbo-ghoul-motion-fix.md)。下文为当时交付记录，不包含这次修复的人工画面验收。

后续状态：完整 31 片段与 720.9 秒转场等待已在 [Phase 5](limbo-full-integration.md) 完成技术验证，Full 已放行；下文的门槛与 16 项失败描述保留为 Ghoul 交付时的历史状态，后续分类与复验以 Phase 5 报告为准。

2026-09-15。**Ghoul 三连击与原始美术已接入并完成本轮技术验证；599.966666… 秒技术预览可运行。** 同一最终构建完成单人 Host、Host/Client 有画面连续运行及结算界面重开。完整 Limbo 仍由独立流程门槛阻止启用；本报告不形成玩法压力结论。广域回归的 16 项失败仍需后续定位，不能把本轮交付视为全项目测试通过。

## 实现与来源

- 来源是 `docs/evidence/hellmaiden-attacks/runtime-assets.json`，SHA256 `4dcefbcae22fb6e8cd067618cd0ba782eb5a23d07b20a31a3ecf3323cd6d5c38`。controller `o00022`、attack `o00134`、MultipleAttackAnimator `o00138`；原始证据与来源资源未修改。
- 独立资源位于 `Assets/_Project/Content/NetworkCombat/Limbo/Ghoul/`：`GhoulSource.json`、`GhoulAdapted.json`、`ReferenceGhoul.prefab`、`ReferenceGhoulAttack.prefab`。重复提取不覆盖适配值；旧 Vector3 包装仅转换表示，保留原值。
- Ghoul v0：50 HP、50 Normal 伤害、基础移速 6、出生倍率 0.9–1.1、基础 XP 9、击退倍率 1；触发距离 2.5、冷却 0.5 秒，无持续接触伤害。

| 索引 | Warning | Active | 后续 |
|---|---:|---:|---|
| 0 | 0.292857170 | 0.235714287 | 下一击预警 |
| 1 | 0.378571451 | 0.200000000 | 下一击预警 |
| 2 | 0.207142860 | 0.207142860 | Recovery 0.121428572，再冷却 |

第三击左侧 Warning 保留 `Enemy_Ghoul_Attack_Right 2`。逻辑时间取来源左侧绑定，右侧动画长度差异保留。最终 Recovery 的展示索引为 2，逻辑计数已归零，两者独立保存。

转录完整六边形、Collider 偏移及父子变换。每击先旋转攻击资源，再应用父级局部偏移 `(0,-0.5)`、`(0,+0.5)`、`(0,-0.5)`。身体圆半径 0.46、偏移 `(0,0.3)`；HurtBox 为 `(1.2,1.5)`、偏移 `(0,0.72)`。

E9 恢复 19 个身体动画及依赖，沿用已恢复的 Ghoul 预警。当前共有 8 类来源身份、9 套身体、17 种外观/变体组合（16 个不同数据库变体）。裁切、轴心、方向、Transition 和嵌套 attackSets 按恢复绑定；不迁移音频，已有 Shader 适配差异保留。

## 联网适配与明确差异

一套连击共用组合 ID 和绝对战斗起点，每击由当前模拟端在 Warning 开始锁向。Replica 使用已接受的姿态建立本地玩家命中区，不重新瞄准或驱动移动。检查点保存索引、锁向/执行位、阶段期限与组合起点，交接保留组合；过期窗口和缺失合法姿态的窗口不补打。

统一绝对时间消除了来源逐段等待的累计帧延迟，这是明确适配差异。暂停和慢动作使用已有战斗时钟。正常窗口关闭先结算有效旧碰撞；取消、交接和回收丢弃失效请求，旧预警退场不能释放新借用实例。

Ghoul 的普通命中击退等待既有 GAS 服务端接纳后执行和广播取消，未沿用旧的本端提前击退。原因是未经确认的 Cancelled 检查点一旦传播，拒绝后回滚可能复活已取消的攻击。这会增加接纳延迟；普通伤害预测保留。中断关联组合、命中事件和命令 ID，两端均可提交，重复取消只广播一次。没有新增生命或伤害 RPC 系统。

动作字段增加 43 个未压缩字节，普通命中元数据增加 8 字节组合 ID。Mirror 实测完整检查点为 252 字节；现有按实际 writer 大小的分包与 MTU 处理继续使用。

收尾时移除了 Unity 自动写入普通 NetworkEnemyBase 的四个默认字段及公共字体的 kerning 缓存，差异保存在 `unrelated-editor-serialization.patch`；没有将普通资源的无关重序列化作为本轮改动交付。最终构建包含当时 Unity 生成的这些等价默认值/字体缓存，战斗代码与独立参考配置与验收构建一致。

## 已归档的机制记录

| 检查 | 记录与结果 |
|---|---|
| Host / Client 目标，四象限 | `ghoul-candidate-host` / `ghoul-candidate-client`：双方每象限 8–10 套完整组合，7 个阶段交接；含暂停、0.25 慢动作、自然武器击杀与攻击池复用 |
| 六边形内外 | `ghoul-candidate-boundary-host`、`ghoul-candidate-boundary-client`；最后 `ghoul-final-boundary` 再验 Client：31 次内部定位、30 次实际 50 扣血，32 次外部定位无命中 |
| 纯扣血不中断 | `pure-damage-continuation.json`：Host 7 个、Client 8 个非致死扣血后同组合继续的样本；隔离时临时关闭武器击退/硬直，未改敌人 Stats |
| 双端命中与中断 | `ghoul-final-interrupt`：双方同样 28 条唯一组合中断，来源玩家 3 为 23 条、玩家 2 为 5 条；无重复组合取消，40 秒后两名玩家均置于正常武器射程内 |
| 实际窗口 | 最新窗口日志区分已应用阶段和按时间计算的阶段；`final-interrupt-audit.json` 两端非 Active 伤害区样本为 0，Stats 绑定错误 0 |
| Imp v1 B | `ghoul-candidate-imp-burst`：只生成 10，来源全局计数为 0；双端出生一致，占用/预警/碰撞清理为空 |
| LostSoul v1 B | `ghoul-candidate-soul-burst`：10 次出生、10 次自爆，独立无经验终止，无玩家击杀记账；双端一致 |
| 后期 Rusher | `ghoul-candidate-rusher`：80 只 Slime v1，150/50/4；双端出生与属性一致 |
| Ghoul L / C | `ghoul-final-limited` / `ghoul-final-curve`：不杀敌时分别生成 30 / 50，双端出生一致，结束清理为空 |

上表运行目录均位于 `Logs/LimboReference/`，分析与测试结果位于 `Logs/LimboGhoul/`。隔离配置的定位、武器抑制、补血、强制交接、临时武器参数和选卡均明确记录；不将重新生成的敌人称为敌人池复用。

保留两类失败记录：`ghoul-candidate-interrupt-client` 在连接启动时失败，另开 `...client2` 后完成；`ghoul-candidate-limited` 的一条出生观察发生在强制重置之后，Host 记录 5.4773、Client 记录重置后的 6。移除 L/C 配置中的定位辅助后，`ghoul-final-limited` / `...curve` 两端完全一致。未改来源属性解决差异。

## 自动化结果与限制

- `edit-final.xml`：559/559；另有组合协议、取消后旧窗口拒绝、第三击绑定、精准终点与 Full 门槛检查。
- `play-enemy-regression.xml`：59/59，覆盖 Ghoul、此前敌人、近战展示、围栏和协程。
- 最后调整正常窗口碰撞结算的归属顺序后，`play-window-settlement.xml`：18/18。
- 广域 `play-final-candidate.xml`：503/519，16 项失败，不能宣称全项目回归通过。失败涉及旧界面文字断言、场景卸载、角色/输入及非 Ghoul 击退/沙盒场景；`play-impulse-isolated.xml` 独立重跑仍有 4 个非 Ghoul 击退测试失败。未取得修改前同条件结果，不把它们全部认定为既有问题，也未扩大为全项目修复。
- `edit-protocol.xml` 的首次新测试因未初始化其 inactive 测试对象引用而失败；修正测试初始化后 `edit-protocol2.xml` 为 14/14。历史失败 XML 保留。

## 连续预览与启动

预览 `[0,599.9666666666667)` 共 22 个片段：12 C、8 L、2 B。C 名义预算 1077、B 名义批量 20，L 没有固定累计总数。截止点不启动第二次围栏及同刻精英。XP 分母 841.5766649882，玩家基线仍为 500 HP、4.55 移速、武器 1、XP 修正 2、当前镜头/Nordic。

最终构建：`Builds/LimboGhoul20260915/MonsterSupergroupLimbo.exe`，构建日志 `Logs/LimboGhoul/build-final.log`。`ghoul` 为无测试辅助入口；`ghoul-validation` 含明确保护与第一项选卡，`-AutoWalk` 只提供普通移动输入；`ghoul-fixture` 用于显式隔离配置。旧入口保留。

```powershell
./Tools/Run-LimboReference.ps1 -Role host -Profile ghoul -BuildDirectory Builds/LimboGhoul20260915 -RunName manual-ghoul
```

连续运行目录为 `ghoul-final-solo` 和 `ghoul-final-pair`，以下只统计各自第一局到达终点的记录，不包含重开后的部分运行。

| 项目 | 单人 Host | Host/Client（服务端统计） |
|---|---:|---:|
| 结束时间 | 599.9666666666667 | 599.9666666666667，两端一致 |
| 成功生成 / 确认击杀 | 2447 / 2342 | 2304 / 2208 |
| 无经验淘汰 / LostSoul 自爆 | 9 / 1 | 11 / 1 |
| 结束时存活 | 95 | 84 |
| C 实际生成 / L 累计生成 / B 实际生成 | 1073 / 1354 / 20 | 1073 / 1211 / 20 |
| Ghoul 累计生成 | 222（L 172，C 50） | 248（L 198，C 50） |
| 后期 Rusher / Slime v1 累计生成 | 303 | 230 |
| 重定位 | 40 | 27 |
| 出生属性不匹配 | 0 | 0，出生 ID 也一致 |
| 结束伤害区 / 预警 / 陷阱 | 0 / 0 / 0 | 两端 0 / 0 / 0 |
| 帧时间中位数 / P95（毫秒） | 16.667 / 17.047 | Host 16.667 / 17.053；Client 16.667 / 17.009 |
| 最长一次自动选卡停留（秒） | 0.2183 | Host 0.2174；Client 0.2567 |

两局均覆盖片段索引 0–21，未启动截止点同刻的第二围栏与精英。最后 Dash C 原预算仍为 60，但相对等待与预览截止使两局实际均生成 56；C 总计 1073，不能为凑齐名义 1077 在终点补发。两个 B 各生成 10；它们与来源全局计数分开。生成、击杀、淘汰、自爆和结束存活的账目闭合。

双端连续局中，Ghoul 经常在组合完成前被击杀或中断，未记录到完整三连击样本；四象限完整组合验收使用前述隔离记录。连续局记录双方同样 21 次唯一组合中断，其中玩家 3 发起 13 次、玩家 2 发起 8 次；无重复组合取消、Stats 绑定错误或非 Active 命中区样本。不能把技术保护或高击杀速率解释为正常玩法压力。

单人第一局 RunId 为 `32566fe2b2d04cceb4e8441bac516ddd`，双端为 `5189dd7e265041cd8ac5677568e6c32f`。实际点击结算界面“重新开始”后，单人第二局为 `b701b7848a294a3b80a501d8b7cf3a95`；双端第二局共同为 `621a9df8fa5e4aa1ad6237e3286f72eb`。新轮次从零计时、初始 500 HP 和空生成计数开始。第二局仅检查重开与初始化，随后停止测试进程，未宣称再次完整到达终点。Client 在重开卸载时记录的 Stopped 不覆盖此前 Completed；分析文件单列 `firstCompletionSnapshot` 与 `wavePhaseTransitions`。

- [单人结算画面](../Logs/LimboReference/ghoul-final-solo/host/ui-end-solo.jpg)、[单人界面重开](../Logs/LimboReference/ghoul-final-solo/host/ui-restarted-solo.jpg)。
- [Host 结算](../Logs/LimboReference/ghoul-final-pair/host/ui-end-host.jpg)、[Host 重开](../Logs/LimboReference/ghoul-final-pair/host/ui-restarted-host.jpg)、[Client 结算](../Logs/LimboReference/ghoul-final-pair/client/ui-end-client.jpg)、[Client 重开](../Logs/LimboReference/ghoul-final-pair/client/ui-restarted-client.jpg)。
- [汇总原始统计](../Logs/LimboGhoul/final-summary.json)、[单人审计](../Logs/LimboGhoul/final-solo-audit.json)、[双端审计](../Logs/LimboGhoul/final-pair-audit.json)。汇总中的其他辅助/性能计数可能包含重开的部分记录，首局出生账目以对应 RunId 和首份 spawns CSV 为准。
- [归档索引与校验值](evidence/limbo-ghoul/verification.json)；完整原始运行、历史失败、字段快照、测试 XML 和画面打包为 `Logs/LimboGhoulEvidence-20260915.zip`。构建单独保留，不包含在证据包中。

人工压力、Spine、599.966666… 秒后的完整流程、完整断线重连/倒地复活矩阵，以及同条件原游戏压力对照均未完成。
