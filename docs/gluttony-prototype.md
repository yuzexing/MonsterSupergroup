# 暴食原型：标记 → 接近 → 收取

## 阶段一：统一能力切换（2026-09-21）

吞噬已接入统一原型选择器。普通 Boot → Gameplay 默认启用原型，每名玩家初始选择吞噬；1 / 2 / 3 分别切换吞噬、音乐、魅惑。阶段一已由用户验收通过。阶段二接入音乐（2 选择、R 开始、空格打拍）；阶段三接入魅惑（3 选择、R 甩怪、T 接怪、F 假人）。完整操作、扩展接口和本阶段验收记录见 [三种能力原型](prototype-abilities.md)。

切出吞噬后停止普通近身被动和新的 R 标记，但已有标记仍可接近收取，直至原定到期时间；切换不刷新、不重置冷却。升级选卡优先使用数字键，选卡关闭当帧不会再触发能力切换。阶段一 Unity / Mirror 与回归已通过，用户已完成试玩验收；阶段二音乐及最新回归记录见统一原型文档，下方旧吞噬验证仅作为历史记录。

## 试玩入口

打开 `Assets/_Project/Scenes/Boot.unity`，保存自己尚未保存的场景修改，然后选择：

`Tools > MonsterSupergroup > Prototypes > Play Normal Enemies (Offline)`

旧的 `Prototypes > Gluttony > Play Normal Enemies (Offline)` 菜单仍可用。该入口启动真实 Boot → 离线准备房间 → Gameplay，使用当前武器 ID 6，启用原型及吞噬的被动、主动。只在本次 Play Mode 使用临时普通 Brotchi 波次（约 3 分钟、48/64/80 只分三段生成，最大存活 120）；不会保存或替换用户的 Full.asset、TestTimeline 或正式怪物数值。玩家仍可能受伤、死亡、升级，需要正常移动和选卡。退出 Play Mode 清理临时配置。

普通启动默认开启原型。在游戏右下角打开 `Prototype abilities / settings`，Host 可用 `Enable prototype abilities for this session` 控制本局所有原型，或用 `Gluttony OFF / Passive / Both` 单独控制吞噬模块。关闭全局原型会清除当前标记，不会重置冷却；再次开启不能恢复旧标记。参数修改对本次会话中的玩家统一生效。F2/F3 可隐藏原有玩家、敌人调试面板，减少遮挡。

当前 Full.asset 引用的 TestTimeline 生成精英骷髅。精英不在本轮吞噬目标范围内，不能仅使用这条时间轴判断原型是否工作。

## 操作与规则

- 被动：选择吞噬时，接近一只有效普通敌人后自动吞噬；成功后进入冷却。当前配置资产沿用用户调参值 5 秒；没有目标、死亡目标、无敌或距离校验失败不消耗冷却。
- 主动：选择吞噬时，鼠标确定方向，按 R 瞬时检测前方矩形一次，最多标记 5 只，标记持续 6 秒；当前配置资产沿用用户调参值 1 秒冷却，空挥也消耗主动冷却。长按 R 不重复施放。
- 收取：接近本人标记的敌人自动免费吞噬，可连续收取多只，不消耗、不刷新被动冷却。标记过期的免费请求不会偷偷改为普通吞噬。
- 标记不定身、不减速、不拖拽，敌人继续正常移动和攻击。普通武器、掉落、经验、升级选择仍运行。
- 普通吞噬每次只取最近一只；标记收取优先。多个受击 Collider 按敌人身份去重。
- 暂不包含精英、Boss、无敌目标、自爆或死亡攻击敌人。其他玩家可正常击杀被标记者，但只有标记来源玩家获得免费收取资格。
- 吞噬复用服务器确认击杀及现有掉落，不另外加经验、回血、成长、暴击或武器命中 Modifier。

## 基础反馈

短暂矩形、跟随敌人的菱形标记、临近过期闪烁、吞噬范围圈、收取光点和占位提示音。光点是独立表现对象，不移动真实敌人的网络 Transform。HUD 显示冷却、本批收取数量与剩余标记。

## 参数

资产：`Assets/_Project/Content/NetworkCombat/GluttonyPrototype.asset`。

当前资产值：Enabled=true、PassiveEnabled=true、ActiveEnabled=true；被动 5 秒、主动 1 秒。这两个冷却值是用户现有调参值，阶段一予以保留；`GluttonyParameters.Defaults` 的代码回退值仍为被动 10 秒、主动 20 秒，不能把它们误写成当前实际试玩配置。

其余当前参数：吞噬半径 1.15；矩形长 5.75、宽 2.3；最多 5 只；标记 6 秒；扫描间隔 0.05 秒；矩形显示 0.2 秒；反馈音量 0.12。它们是试验起点，不是完成平衡或半径校准的结论。

运行时面板可以调整冷却、半径、矩形尺寸、最大数量、标记时长、声音音量。Apply 会清除当前标记；Reset cooldowns + clear marks 同时重置冷却和清除标记。面板修改不保存回配置资产，也不会给每个客户端单独设置一套规则。

## 代码入口

所有以下路径位于 `Assets/_Project/`：

| 入口 | 职责 |
|---|---|
| `NetworkCombat/Mirror/NetworkPlayerPrototypeAbilities.cs` | 每名玩家的能力选择、切换版本、新施放门禁与统一输入分发 |
| `NetworkCombat/Mirror/IPrototypeAbilityModule.cs` | 模块注册、当前能力动作、已开始效果的后续输入与清理接口 |
| `NetworkCombat/Server/GluttonyPrototypeRuntime.cs` | 纯规则、独立冷却、标记集合与带版本的原子快照 |
| `NetworkCombat/Mirror/NetworkPlayerGluttony*.cs` | Owner 输入/查询、服务器校验、请求去重、复制、生命周期 |
| `NetworkCombat/Mirror/GluttonyGeometry.cs` | 目标资格、近身与矩形几何、最新敌人位置快照 |
| `NetworkCombat/Mirror/GluttonyPrototypeView*.cs` | 占位反馈、只读 HUD、Host 调参 |
| `NetworkCombat/Mirror/NetworkCombatWorld.Gluttony.cs` | 会话参数和受控吞噬入口 |
| `NetworkCombat/Server/CombatLedger.cs` / `ServerCombatGateway.cs` | 吞噬进入原有 canonical HP、ConfirmedKill、状态移除和掉落通知 |
| `NetworkCombat/Editor/GluttonyPrototypePlaytest.cs` | 不写原关卡的离线普通敌人试玩入口 |

Boot 已配置 Rewired Button3（Action 5）→ R；R 经统一选择器分发给当前能力。NetworkPlayer Prefab 已接入选择器、吞噬技能、配置和表现组件。鼠标主动方向单独计算，不覆盖普通武器自动索敌。标记只是技能自己的目标批次，未新增第二套 GAS 状态系统或敌人控制状态机。

修复了原本分开复制标记集合和施法确认导致的确认窗口：计数与目标 ID 一起复制，Owner 回执不直接覆盖 SyncVar；旧复制消息不能恢复已收取的标记。待确认请求按目标隔离，免费收取不串行等待上一只。

## 2026-09-21 吞噬原型历史验证（统一切换接入前）

本节为原吞噬实现的历史证据，不代表阶段一新增能力切换、输入优先级或联机版本校验已经通过验收。阶段一结果以 [三种能力原型](prototype-abilities.md) 的最新记录为准。

证据目录：`Logs/GluttonyPrototype/20260921-125246/continuation-140405/`。

| 验证 | 结果 / 证据 |
|---|---|
| Unity 编译及 Mirror 后处理 | 原生 Unity 测试启动完成；没有 C# 编译错误或 Weaver error，详见 `unity-editmode.log`、`unity-playmode-visible.log` |
| Unity EditMode | 24/24 通过、无跳过、进程退出 0；`unity-editmode.xml` |
| 最终可见编辑器 PlayMode | 3/3 通过、无跳过、进程退出 0；`unity-playmode-visible.xml` |
| 受控真实场景 | 距离失败不扣 CD；普通吞噬后免费收取不改变原截止时间；两只矩形标记敌人连续收取且不消耗被动 |
| 正常战斗短流程 | 1 次施放、1 次被动吞噬、1 次标记收取、15 条普通武器伤害、4 次经验收集；`visible-normal-battle.txt`、`visible-normal-battle.png` |
| 操作系统 R 输入 | 验证克隆中按住 R 1.2 秒，Action 5 仅出现一次，服务器接受一次空挥；`input-evidence.txt`、`manual-input.log`、`manual-input-accepted.png` |
| 独立 CLR 规则断言 | 24/24；`managed-assertions.txt`。它不是 Unity 测试，不与上述 Unity 用例重复相加 |

正常战斗用例采用临时普通敌人波次，保留真实武器、伤害、移动、掉落、升级；移动和技能 API 由脚本驱动，玩家可正常死亡，所以它是短流程，不是完整 3 分钟的人工体验验收。R 输入验证是另外一轮操作系统按键注入，覆盖按键到服务器施放链路；该次为空挥，不冒充命中多个敌人的输入验收。

重跑入口位于同一 Gluttony 菜单下的 Run EditMode Tests / Run PlayMode Tests；进入测试前保存场景并退出 Play Mode。

## 验证边界和环境记录

当前完成了本机离线 Host 及可见编辑器验证。未完成独立 Host+Client 双进程、Steam 跨机器、高延迟丢包或长时间压力验收；本轮也不实现完整的技能重连状态保存。断线、死亡、禁用和换局会清理临时标记；不据此承诺重连保持剩余冷却。

真实舌头、定身拖拽、进化成长、回血、正式技能选取 UI、手柄绑定和专属吞噬死亡动画均不在本轮范围。是否有趣仍需对照 OFF / Passive / Both 试玩，观察是否真的主动改路线，而不只是原地等敌人进入吞噬圈。

验证使用 Unity 6000.3.21f1 的独立 ParrelSync 克隆；图形流程使用 D3D11。最初子进程 UPM 启动失败，定位到远程进程缺少 ALLUSERSPROFILE/PROGRAMDATA；仅给验证子进程补齐 Windows 返回的 C:/ProgramData 后成功，未修改系统环境变量或安全软件。

主项目编辑器此前一直卡在 Reloading Domain；本次未强行关闭主编辑器。验证编辑器已正常退出。原始备份位于 `Logs/GluttonyPrototype/20260921-125246/originals/`，续接修改前备份位于本次证据目录的 `before/`。遗留 .gluttony-tmp 文件已移出 Assets，保存在 `retired-temp/`。未创建 Git commit 或 push。
