# 吞噬、音乐与魅惑原型

## 阶段与当前状态

按“能力切换 → 音乐 → 魅惑”交付。阶段一（统一能力选择、输入分发、吞噬适配及普通敌人试玩入口）已获用户验收通过。阶段二音乐已经交付，用户要求继续阶段三并在完成后人工验收。三种能力现均已接入：吞噬、音乐和魅惑。

每阶段完成测试并交付试玩后暂停，收到用户反馈再实施下一阶段。阶段一自动测试及验收记录见下方；阶段二独立记录测试结果，不沿用阶段一通过数量。

## 启动与操作

普通 `Assets/_Project/Scenes/Boot.unity` → Gameplay 默认启用原型，每名玩家初始选择吞噬，选择互不影响。原关卡 Timeline 保持不变；若当前波次只有精英等不合格敌人，不能用它判断吞噬是否工作。

普通敌人试玩：打开并保存 Boot 场景，选择 `Tools > MonsterSupergroup > Prototypes > Play Normal Enemies (Offline)`。它启动离线房间并使用武器 ID 6，临时生成约 3 分钟的普通 Brotchi 波次，保留伤害、死亡、武器、掉落、经验与选卡。退出 Play Mode 后清理临时资源，不覆盖正式波次或 EnemyDefinition。原 `Prototypes > Gluttony > Play Normal Enemies (Offline)` 入口继续可用。

| 输入 | 当前行为 |
|---|---|
| 1 | 选择吞噬；近身普通被动开始工作，R 可标记 |
| 2 | 选择音乐；R 开始演奏 |
| 3 | 选择魅惑；R 甩怪、T 接怪、F 假人 |
| R | 吞噬时标记；音乐时开始演奏；魅惑时甩怪 |
| 空格 | 正在演奏时提交一次节拍判定，切到吞噬或魅惑后也可继续打拍 |
| T / F | 魅惑接怪 / 放假人；其他能力不发起这两个技能 |
| 1 / 2 / 3，装备目标阶段含 4 | 出现升级选卡时优先选卡；关闭当帧仍不切换能力 |
| Q / Shift / E | 保留原终极技能、冲刺、交互操作 |

新增键经 Rewired → InputHandler → 当前 Controller → 本人 PlayerMovement 分发，只处理按下瞬间，并检查窗口焦点。待确认的切换期间不发起新技能。升级选卡、加载、菜单及原型设置面板会阻挡原型操作。

## 切换、冷却与会话开关

- 只有当前能力可以发起新技能。切出吞噬后停止普通近身被动和新的 R 标记；本人已有标记仍可接近收取，按原截止时间过期，不刷新有效期。切出音乐后已开始的演奏继续接收空格，加速保持到期；切回不会重复开启演奏。切出魅惑后已放置的假人继续保持至到期。
- 切换不禁用或重建技能组件，不重置冷却。切回吞噬沿用原被动、主动截止时间；等待期间冷却正常流逝。标记免费收取也不消耗或刷新普通被动冷却。
- 客户端与服务器共同验证新施放资格，施放请求带当前选择版本；服务器拒绝过期版本。已有标记收取使用原施法 ID，切换不会令其失效。
- 右下角 `Prototype abilities / settings` 显示当前能力、提示和冷却；切出后保留未结束演奏及加速倒计时。Host 的 `Enable prototype abilities for this session` 控制本局全体玩家的原型；关闭立即取消吞噬标记、音乐演奏、加速及假人。中断演奏进入音乐冷却，切换能力不改变已有冷却截止时间。
- 面板的 `Gluttony OFF / Passive / Both` 只控制吞噬模块，区别于全局原型开关。`Apply parameters` 会清除标记；只有显式 `Reset cooldowns + clear marks (test)` 会同时重置吞噬冷却。面板调参不写回资产。
- 面板以 `Gluttony / Music / Allure` 标签切换参数，音乐的 `Apply music` 终止当前音乐效果并应用本局参数；`Reset music cooldown + effects (test)` 另行清除音乐冷却。所有者均可调整本机输入校准，Host 权限仅用于本局战斗参数。
- 死亡、断线、换局及禁用清理临时效果。首版不保存完整技能重连状态，不能据此承诺断线重连后仍恢复原冷却。

吞噬配置资产为 `Assets/_Project/Content/NetworkCombat/GluttonyPrototype.asset`：已启用被动和主动，**实际被动冷却 5 秒、主动冷却 1 秒，沿用用户现有调参值**。代码 `GluttonyParameters.Defaults` 的回退冷却为 10 秒 / 20 秒。范围、数量和反馈参数见 [吞噬原型说明](gluttony-prototype.md)。

## 扩展接口

稳定能力 ID 为 `Gluttony=1`、`Music=2`、`Allure=3`。`NetworkPlayerPrototypeAbilities` 保存当前选择及版本，并注册同一玩家上的 `IPrototypeAbilityModule` 组件；每个模块负责自己的配置、状态、效果和冷却。

模块通过 `TryHandleAction` 接收当前能力的新动作，通过 `TryHandleOngoingAction` 接收已开始效果的后续输入，通过 `ServerCancelEffects` 响应生命周期及全局关闭。音乐在切出后继续接收节拍输入；魅惑假人独立保留至原定到期时间。

`PlayerMovement.BindPrototypeInput` / `UnbindPrototypeInput` 使用委托匹配绑定与释放，旧对象解绑不会移除后绑定的输入。`SelectPrototypeAbility` / `PrototypeAction` 为统一入口；旧 `GluttonyAction` 兼容调用转入统一入口，统一入口拒绝后不会再次施放吞噬。

Rewired 动作 ID：63 / 64 / 65 对应 1 / 2 / 3；66 / 67 / 68 对应 T / F / 空格；69 对应第四装备选项 4；R 保持 Button3 / 5。本人 `ModifierSelectionController.BlocksPrototypeInputThisFrame` 供各入口共同检查选卡消费帧，避免菜单关闭后同一按键继续触发原型。

## 阶段一验收

| 验证项 | 当前记录 |
|---|---|
| Boot 新旧动作与物理键静态检查 | 输入实现时独立检查 11 组映射通过；此检查不等于真实按键试玩 |
| Unity 编译与 Mirror 后处理 | 通过；最终 EditMode、图形 PlayMode 均退出码 0，无 C# 或 Weaver 编译错误 |
| 原吞噬 EditMode / PlayMode 回归 | 本轮重新通过 24 条规则测试与 3 条真实场景测试，包含正常战斗、掉落和经验 |
| 新增输入、切换与选卡回归 | EditMode 合计 45/45；图形 PlayMode 合计 21/21，均无跳过，含 8 条能力切换与会话生命周期用例、10 条选卡用例 |
| 用户试玩验收 | 2026-09-21 用户反馈“验收通过，继续任务”；记录为阶段一整体验收通过，不拆解成未单独记录的逐项按键证明 |
| Host + Client 双进程和各自独立选择 | 本阶段未执行独立双进程；自动测试覆盖单进程 Host、本人输入分发和服务端资格检查，不据此声称远端 Client 已验收 |

证据目录：`Logs/PrototypeAbilities/stage1-20260921-195344/`。最终结果为 `editmode-final.xml`、`editmode-final-exit.txt`、`playmode-final.xml`、`playmode-final-exit.txt` 及同名前缀日志。Unity 6000.3.21f1，图形流程使用 D3D11。

EditMode 过滤器：`MonsterSupergroup.NetworkCombat.Tests.GluttonyPrototypeTests;MonsterSupergroup.NetworkCombat.Tests.PrototypeAbilityInputTests`。

PlayMode 过滤器：`MonsterSupergroup.Gameplay.Tests.GluttonyPrototypePlayModeTests;MonsterSupergroup.Gameplay.Tests.PrototypeAbilityPlayModeTests;MonsterSupergroup.Gameplay.Tests.ModifierSelectionTests`。可用菜单 `Tools > MonsterSupergroup > Prototypes > Run Stage One PlayMode Tests` 重跑。

早期失败日志保留用于追溯：首次 Boot 加载与启动本地化字体导入重叠时，Rewired 数据及 WeaponDB 引用加载失败；测试现在等待本地化和两帧渲染准备后加载 Boot。另一项测试因标记续收经验触发选卡，被正常切换门禁拦截；现先完成选卡再验证冷却。没有忽略异常日志或放宽玩法断言。过期施放版本通过服务端资格入口验证，不冒充网络报文重放测试。

阶段一原验收范围：1→2→3→1 且长按不重复切换；选卡数字不触发切换；R 标记后切出仍可免费收取，普通未标记敌人不再被动吞噬；切回冷却不刷新；关闭全局原型清除已有标记；Host 与 Client 只改变本人选择。阶段一的音乐、魅惑均为占位状态；阶段二开始音乐由真实模块处理。

## 阶段二：音乐试玩

按 `2` 选择音乐，再按 `R`。听完两拍预备后，在后续十个短音到达时各按一次空格。角色附近的收缩环与固定环重合时为拍点；十个指示符从灰色 `o` 变为绿色 `+`（成功）或红色 `x`（失误）。成功立即反馈，服务器再揭晓本拍效果，不预告随机结果。判定环、十拍进度与节拍提示音只属于使用者，队友只能看见击退波、电击和加速效果。

演奏期间可以继续移动、自动攻击并受到伤害。每拍只有一次机会，乱按落在成功窗外会消耗该拍；漏拍按失误处理，后续继续。切换能力不会中断当前演奏，再次按 R 不能叠加第二段演奏。

| 参数 | 默认值 |
|---|---|
| 节奏与结构 | 120 BPM，2 拍预备，固定 10 拍 |
| 成功窗口 | 拍点前后 120 毫秒 |
| 冷却 | 演奏结算或中断后 20 秒 |
| 成功效果 | 击退、电击、加速各 1/3 概率，当拍成功后揭晓 |
| 击退 | 半径 4，距离 2，持续 0.25 秒，沿远离玩家方向 |
| 电击 | 半径 6，随机最多 3 个不同有效敌人，各 20 点伤害 |
| 加速 | 移速 +25%，持续 2 秒；重复触发刷新时间，不叠层 |
| 十拍全中 | 半径 6 内有效敌人额外各受 60 点电击伤害 |
| 节拍音量 | 0.3，再乘本机游戏总音量和音效音量 |
| 本机输入校准 | 默认 0，可调 -200 至 +200 毫秒；正值补偿偏晚输入 |

集中配置资产为 `Assets/_Project/Content/NetworkCombat/MusicPrototype.asset`，代码回退为 `MusicParameters.Defaults`。Host 可在 Music 面板临时调整节奏、预备拍数、判定窗、冷却和各效果数值；十个判定点固定。输入校准属于本机，演奏中禁用调整，以免改变进行中的时间基准。

无有效敌人时保持原随机效果，不改抽、不扣除本拍成功记录。音乐按正常伤害、无敌与击退规则筛选目标，不套用吞噬专属资格。加速通过独立临时修饰器应用，既有装备和成长属性不被覆盖。死亡、断线、换局或关闭原型会清理临时状态。

本机以 `AudioSettings.dspTime` 为节拍时钟，用 `AudioSource.PlayScheduled` 预排短音，避免依赖逐帧播放的时序。参考 [Unity PlayScheduled 文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AudioSource.PlayScheduled.html)。服务器验证施法 ID、拍点索引、原始判定时间范围并去重；合拍与否依据提交的本机节拍时间，不按网络消息到达时刻重新判定。效果、伤害、击杀及掉落仍由服务器结算。

演奏期间经验正常入账，升级继续排队，选卡只延后弹出；演奏结算或中断后自动恢复原选卡流程。此延后状态与施法 ID 对应，释放旧演奏不会取消更新演奏的延后状态。

## 阶段二测试记录（2026-09-21）

| 验证类别 | 实际结果 |
|---|---|
| Unity 编译 / Mirror 后处理 / Windows 构建 | 通过；Unity 6000.3.21f1，D3D11；独立构建退出码 0 |
| EditMode | 115/115，无跳过；含音乐时序、请求去重、权威伤害、击退契约及吞噬/输入回归 |
| PlayMode | 33 个用例均有最终通过记录，无跳过；见下面两个原始 XML 的合并说明 |
| 普通网络独立 Host + Client | 双方各 10/10；客户端第 4 拍后切到魅惑继续；各拒绝 30 次重复本地输入、发送 10 次重复 Mirror 请求 |
| 延迟网络独立 Host + Client | 同一构建，额外 100 ms 延迟、0.02 抖动；同样双方 10/10 与重放去重通过 |
| 战斗与掉落 | 两轮各权威扣血总计 80、击杀 2、掉落决策 2、生成掉落 2；目标无重复结算 |
| 升级与表现 | 两名玩家各延后两级奖励，完成后自动恢复并全部选完；观察者节拍 AudioSource 数为 0，使用者为 12（2 预备 + 10 拍） |
| 实际键盘与音画试玩 | 自动联机以真实 DSP 时钟调用生产输入接口，不冒充真人打拍；键盘及主观节奏手感留给本阶段试玩验收 |

证据目录：`Logs/PrototypeAbilities/stage2-20260921-202848/`。

- `editmode.xml`：115/115，`editmode-exit.txt` 为 0。
- `playmode-final.xml`：32/33；其中全连用例在首批怪物加载时错过窗口。等待战场初始化完成后，`music-final.xml` 的六项音乐用例全部通过，`music-final-exit.txt` 为 0。`playmode-combined-summary.json` 按完整用例名取最后一次结果，合计 33/33，保留两个原始 XML，不伪造单轮通过结果。
- `build-main.log` / `build-main-exit.txt`：主工程独立构建成功。更早的构建失败记录保留：测试入口 meta GUID 长度错误已修复；验证副本缺少工具 catalog，最终改在主工程构建。
- `music-network-20260921-204951-normal/` 与 `music-network-20260921-205025-impaired/`：双方日志、构建 SHA256、每名玩家结果 JSON、服务器伤害/掉落汇总及 PASS 标记。

首轮场景回归暴露并修复了玩家死亡释放延后的缺口：OwnerFinal 生命值上报不产生敌人式击杀事件，现在直接观察权威存活状态；经验和排队奖励保留。吞噬冷却测试也等待真实掉落入账后再选卡。演奏中禁用升级组件会中断音乐并释放临时效果，重新启用后恢复已获得的奖励。没有扩大 ±120 ms 窗口或忽略异常日志。

本机音频起点与 `NetworkTime.predictedTime` 对齐，避免用带插值延迟的画面时间作输入基准。每条拍点消息有 1.5 秒传输宽限和 0.35 秒未来时间上限；超过范围会拒绝。判定窗仍是本机音频时间的 ±120 ms。后台漏拍会自动结算，失去连接的演奏也会超时或随玩家清理。

重跑入口：`Tools > MonsterSupergroup > Prototypes > Run Stage Two PlayMode Tests`。独立构建入口为 `MonsterSupergroup.Gameplay.Tests.MusicPrototypeValidationBuild.BuildBatch`；构建产物在 `Builds/MusicPrototype/MusicPrototype.exe`，两进程测试脚本为 `Tools/Scenarios/Run-MusicPrototypeValidation.ps1 -Profile normal` 或 `-Profile impaired`。测试脚本只在显式 `--music-role` 参数下安装探针，不影响普通试玩。

阶段二交付后，用户要求“继续往后做，做完我人工验收”；阶段三按该指示实施。

## 阶段三：魅惑试玩

按 `3` 选择魅惑，角色仍可移动、自动攻击、受伤及升级。

| 输入 | 首版行为 | 默认冷却 |
|---|---|---|
| R 甩怪 | 从本人当前画面内、仇恨属于自己的普通敌人中，按距离选最多 10 只，转给另一名存活队友 | 20 秒 |
| T 接怪 | 从队友当前画面内、仇恨属于队友的普通敌人中，按距队友远近选最多 10 只，转给自己 | 20 秒 |
| F 假人 | 脚下放置固定 5 秒的假人，施放时选取本人画面内、仇恨属于自己的最多 10 只普通敌人 | 20 秒 |

三个冷却互相独立，切换能力继续计时。无合格目标、无存活队友或来源视野报告不可用时，提示原因且不扣冷却。R/T 自动选择双人局中的另一名存活玩家；开局名单超过两人时拒绝施放并提示首版仅支持双人，不因其中一人死亡或断线而自动选择剩下的人。单人可以使用 F。

假人没有血量、碰撞、伤害，不吸引施放后新出现的怪，每人最多一个。它只覆盖追踪位置，原玩家仇恨归属保留。切出魅惑不会取消假人；到期、施法者死亡或断线、关闭原型及换局时移除覆盖。玩家仍可能被碰撞或既有攻击伤害。新的 R/T 可以覆盖 F；旧假人到期只释放匹配的施法 ID，不把已经转移的怪拉回来。

R/T 改变的仇恨不自动到期。敌人立刻追向新玩家，但从当前位置继续移动；原模拟者继续计算，直到敌人身体范围进入新仇恨玩家实际游戏摄像机的画面，才进行模拟交接。两人画面重叠时可以立即交接；离开画面不会自动交回。仇恨玩家失效时恢复正常重选。原模拟者断线时服务器立即兜底，不等待屏幕条件。

每个所有者每 0.1 秒上报 `GameplayCameraGeometry.ViewBounds`，服务器验证发送者、局号、序列和有限几何值。超过 0.5 秒的报告不授权普通屏幕交接，不用角色中心估算远端画面。目标版本与模拟 Epoch 分开：单纯 R/T/F 目标变化不重启前摇、冲刺、攻击或击退；实际模拟交接仍复用原动作与位置检查点。移动与攻击共用最终目标解析。

等待交接或假人生效期间暂免普通离屏重定位。F 可暂停已有待交接；到期后按当前有效仇恨继续检查接收者画面。F 不改变阵营、HP、掉落及经验，也不套用吞噬对自爆等普通敌人的额外排除条件；精英和 Boss 仍不属于魅惑首版目标。

配置资产：`Assets/_Project/Content/NetworkCombat/AllurePrototype.asset`。Host 在右下角原型设置的 `Allure` 标签中调整三个冷却、最大数量及假人时长。`Apply allure` 清当前假人并应用本局参数，保留冷却；显式测试重置按钮才清冷却。HUD 显示三个冷却、上次影响数、假人倒计时，切出后仍显示未到期假人。R/T/F 成功都有本人及队友提示。F3 敌人面板分别显示 Aggro、Tracking、Simulator、Target version、Epoch、Allure transfer pending 和首快照交接诊断。

### 人工试玩入口

Editor 单人仍使用 `Tools > MonsterSupergroup > Prototypes > Play Normal Enemies (Offline)`，可测三能力切换、音乐及 F。

同机双窗口使用本轮独立构建（正常攻击、伤害、掉落、经验及选卡均开启）：

```powershell
powershell -ExecutionPolicy Bypass -File Tools/Scenarios/Start-PrototypePlaytest.ps1
```

脚本打开 Host 和 Client，自动进入双人普通敌人波次，输出两个 PID 及日志目录，不自动施放技能或操作键鼠。点击哪一个窗口，键盘就控制哪名玩家。关闭各游戏窗口结束试玩。两台电脑需使用相同版本构建，Host 运行 `-Role host`，另一台运行 `-Role client -Address <Host 的 IP>`；默认 KCP 端口 8000。单人独立窗口可用 `-Role host -WaitFor 1`。

该启动器只在显式 `--prototype-playtest=host/client` 参数下生效。Editor 与独立窗口复用 `PrototypePlaytestBattle`，运行时创建约三分钟的普通 Brotchi 波次；不保存正式 Timeline、EnemyDefinition 或敌人数值。

建议人工依次检查：两人靠近 R/T；相隔一屏 R 后怪物连续走向队友；F 后侧移；F 后 R/T 覆盖；切换到 1/2 后假人仍到期；关闭旧模拟者窗口后其余玩家仍看到怪物继续移动。主观手感与真实键盘验收由用户完成，自动网络接口测试不代替这项结论。

## 阶段三测试记录（2026-09-21）

证据目录：`Logs/PrototypeAbilities/stage3-20260921-223749/`。Unity 6000.3.21f1，图形测试及玩家构建使用 D3D11。

| 验证类别 | 实际记录 |
|---|---|
| EditMode | `editmode-final.xml`：177/177，无跳过；涵盖魅惑冷却、目标版本、视野期限，以及原敌人交接、吞噬、音乐、输入和击退契约 |
| PlayMode | 57 个不同用例均有最终通过记录；原始 `playmode-final.xml` 56/56，人数条件调整后 `allure-final.xml` 10/10，新增 `roster.xml` 1/1 |
| 魅惑场景 | 单人 R/T 拒绝不扣冷却、F 最近目标及数量限制、切换保留、到期、关闭、死亡、空目标、重复请求、旧选择版本拒绝，以及目标变化保留已有攻击 |
| 三人限制 | 生产服务器入口使用临时三人 RunSession 名单，验证正常、第三人倒地及断线三种情况都拒绝 R/T；不是三客户端联机试玩 |
| 旧能力回归 | 吞噬 3 项、音乐 6 项、音乐战斗效果 2 项、选卡 10 项、升级延后 4 项、统一选择 8 项；另有原交接 6 项及敌人面板 7 项 |
| Unity 编译 / Mirror 后处理 / Windows 构建 | 最终 `build-release.log` 构建成功，退出码 0；试玩产物为 `Builds/AllurePrototype/AllurePrototype.exe` |
| 普通网络独立 Host + Client | `allure-network-20260921-232300-normal/`：双方 PASS；覆盖画面重叠、连续 R/T 与 R/R、F 后切换与转移、跨屏、过期视野、假人暂停交接、旧模拟者断线 |
| 延迟网络独立 Host + Client | `allure-network-20260921-232351-impaired/`：额外 100 ms 延迟、0.02 抖动，双方 PASS；同样覆盖上述场景 |
| 人工启动器冒烟 | `manual-launch-smoke/`：两个隐藏的真实玩家进程进入准备房、自动准备并开始战斗，生成普通敌人；未自动施放技能或控制真实键鼠 |
| 人工验收 | 待用户试玩反馈；未把自动 Command 调用、攻击状态夹具或场景启动冒烟记作真实按键与手感验收 |

`playmode-combined-summary.json` 按完整用例名取最后一次结果，共 57/57；保留各次原始 XML，没有合成虚假的单轮 XML。上述通过测试的 Unity 退出码为 0。早期导入与编译失败日志保留：人工启动器局部变量初始化问题已修复，新 Allure 测试等待本地化及字体导入完成后才加载 Boot，避免 Rewired/WeaponDB 首次加载竞态。

攻击连续性的专门场景使用真实 NetworkEnemySkeleton，并预置正在进行的 Warning 动作，检查共享目标、ActionId、前摇截止时间、下一攻击时间、模拟 Epoch 与角色切换事件均保持正确；这是目标解析层测试，不表示魅惑可以选中精英。真实冲刺、前摇过程中交接的主观表现仍属于人工验收范围。

独立联机测试入口为 `MonsterSupergroup.Gameplay.Tests.AllurePrototypeValidationBuild.BuildBatch` 和 `Tools/Scenarios/Run-AllurePrototypeValidation.ps1 -Profile normal` / `-Profile impaired`。仅显式 `--allure-role` 参数安装自动探针，与 `--prototype-playtest` 人工入口分开。测试用临时普通敌人夹具并关闭自动武器，以隔离目标移动；为连续覆盖场景把三冷却临时设为 0.35 秒，假人仍为 5 秒。默认 20 秒冷却由配置及场景回归验证。

两轮最终联机的 `build-manifest.json` 包含 EXE、NetworkCombat 及 Gameplay 程序集共 7 个 SHA256，普通网络、延迟网络与当前试玩构建全部一致。每轮保留双方日志、PASS 标记、`server-verified` 和四份逐快照 CSV。跨屏场景中，旧模拟者累计移动分别为 13.722 / 13.783，新模拟者继续移动 1.666 / 1.547，首个有效快照序号均为 1 且敌人已进入接收者画面。假人生效时进入接收者画面仍保持旧 Epoch，过期后才交接；断线场景则在接收者画面外立即获得兜底模拟的有效快照并继续移动。各场景同时检查相邻快照位移，最终两轮记录的最大单步位移分别为 0.299 / 0.239。

早期联机失败也保留：`225555-impaired` 的断线断言原先比较整段起终点，敌人折返时位移相抵，现分别检查新旧模拟者各自的运动段；`230658-normal` 的假人场景原先让怪走完整个屏幕间距，抵达前默认 5 秒已自然结束，现先走近实际画面边缘再通过目标核心夹具放假人，验证有效期内进入画面仍不交接。该夹具验证交接条件，F 的脚下位置及本人选敌由生产施放场景另行覆盖。没有延长假人默认时长、加速怪物或放宽相邻快照位移检查。

最终普通网络 Host 日志另有既有硬件信息采集器查询 F 盘型号失败的文本；发生在 `DBL.LogHardwareInfo`，未影响战斗或验证结果。首版仍使用占位音画，真实键盘、音画手感和实际冲刺表现等待人工验收；正式谱面、美术、技能成长、四人接收者选择及完整技能重连存档不在本轮范围。
