# M4 武器、Equipment 与 Perk 选择

## 范围和正式入口

正式 Boot → Gameplay；保留 Mirror Owner、GAS、PlayerBuildRuntime、单槽 Equipment Modifier 和既有 Build 对齐。
初始武器仍为 Circling，XP 来源和每级阈值保持原配置。没有新增波次、拾取、旧单机初始化、重抽或禁用系统。

`服务器 XP → NetworkModifierSelection 的逐级队列 → UpgradeOfferProvider → Owner CardPickMenu`
`→ 原 Select Command / 新 Back Command → 所有者、当前事件和候选校验`
`→ PlayerBuildRuntime.EquipWeapon / AddEquipment / UpgradeEquipment / AddPerk → Owner Build 基线`

服务器决定候选、阶段和最终应用。网络只传内容身份，不传资源对象、曲线或 Modifier 实例。
Host 跳过客户端 Build 重放；独立 Owner 按 revision 对齐原 Build，并继续接收现有冷却和召唤成熟时间基线。

## 参考配置映射

实际读取的参考根目录：`F:/DecomplieLatest/HellMaiden/ExportedProject/Assets`。

| 原实现 / 数据 | 正式实现 | 复用与差异 |
| --- | --- | --- |
| `Scripts/Assembly-CSharp/AstralShift/HellMaiden/Player/Attacks/Leveler.cs`，`ProcessLevelUp` | `UpgradeSelectionRules`、服务器逐级奖励队列 | 7 级起奇数级 Perk；连续升级保留每个获得等级。不导入全局菜单和暂停 |
| `MonoBehaviour/Card Pool Weights.asset`，`CardPool` | `GameplayUpgradeSelectionRules.asset`、`UpgradeOfferProvider` | 武器等级为 4／12／18，按现有武器 poolWeight 不放回抽取，排除已拥有武器 |
| `MonoBehaviour/Perk Drop Weights.asset`，`PerkPool.UpdateWeights` | `GameplayPerkDropWeights.asset`，现有 `PerkDropWeightsData` 数据类型 | 使用 0／20／50 阈值、Tier 与稀有度线性插值；不导入全局玩家、存档或旧池 |
| `RuntimePerk`、`PerkData.GetMaxLevel` | 从 `PlayerBuildSnapshot.Perks` 的逐次条目推导下一合法成长 | 普通每稀有度三次，水晶单档一次；继续调用现有 AddPerk 累加 |
| `ProgressionManager` | 本轮不移植 | 原文件负责关卡进度和 XP 倍率，不负责上述奖励分类 |

正式数据位于 `Assets/_Project/Content/NetworkCombat/`：

- `GameplayUpgradeSelectionRules.asset`：武器等级、Perk 起始等级与间隔、权重资产引用。由正式 `NetworkPlayer.prefab` 的 `NetworkModifierSelection` 引用。
- `GameplayPerkDropWeights.asset`：只复制已核实的权重数值，使用新的资源 GUID；不修改原关卡资产。
- Boot 的 RuntimeDB 新绑定现有 `NativeGasPerkDB`。现有 WeaponDB、EquipmentDB 不变，不调用 `RuntimeDB.Init()`。

| 等级阈值 | Tier 权重 T0／T1／T2／T3 | 各 Tier 的稀有度权重 |
| --- | --- | --- |
| 0 | 80／19／0.99／0.01 | T0: B85,S14,G0.99,C0.01；T1: S98,G1.99,C0.01；T2: G99.99,C0.01；T3: C100 |
| 20 | 80／19／0.99／0.01 | T0、T1、T3 同上；T2: G98,C2 |
| 50 | 47／35／15／3 | T0: B85,S14.4,G0.5,C0.1；T1: S93.5,G6,C0.5；T2: G95,C5；T3: C100 |

B／S／G／C 分别为青铜／白银／黄金／水晶。优先在可提供三项的正权重 Tier 中抽取；否则在候选最多的合法 Tier 中抽取。Tier 内按稀有度权重除以该稀有度候选数，再乘 Perk 自身 poolWeight。

## 行为与恢复

- 每一级只排一次奖励，显式武器等级优先；`ServerQueueUpgrades(count)` 仍是额外 Equipment 入口，不改变等级。
- 四个武器槽；六种武器 ID 为 1、2、3、6、8、402。选入第一个空槽，不重复拥有、不替换。满槽或无未拥有候选时，将当前队列项固定转换为 Equipment。
- Equipment 第一阶段仅选卡，第二阶段确认合法武器目标；即使只有一个目标也确认。最多四项，第四项仅目标阶段开放。满槽仍可升级同卡，升级替换旧绝对等级；依赖卡、跨槽卡和不兼容卡继续排除。
- Back 返回原卡片，保持顺序和内容，不重新抽取。每次阶段切换更新事件身份；最终应用成功才消费队列、增加 Build revision。两阶段之间继续保持当前玩家的选择限制。
- Perk 仅为当前 DB 的七个原生武器属性定义。ID 3 攻速的前三次各 +5%，第四次 +7%，累计 +22%。ID 28 额外投射物只有一次水晶成长。新武器继承现有全局 Perk。
- 有一／两项就显示实际数量；零候选保持该轮待处理、解锁玩家并诊断重试。已发出的候选失效时保留该内容等待处理，不自动重抽。
- RunSession checkpoint 保存逐级队列、阶段、原始卡片、已选 Equipment、目标候选和事件序号。重连重建运行时句柄并重新签发事件身份，候选内容不变。Perk 进度直接来自 Build 快照逐次条目，没有第二份成长存档。
- 失权、死亡、组件禁用、退出及场景卸载沿用原选择清理入口；没有全局暂停。

## 测试入口

Editor 中使用现有 `ModifierSelectionValidationBuild.Build`，通过环境变量
`MODIFIER_SELECTION_VALIDATION_OUTPUT=F:/UnityStore/MonsterSupergroup/Builds/M4Selection/M4Selection.exe`
生成包含测试程序集的正式 Boot＋Gameplay 开发构建。

`Tools/Run-UpgradeSelectionProcessValidation.ps1` 支持 Host＋Client 和 `-Dedicated` 的 server-only＋两个 Client。
可见画面使用 `-CaptureFrames -VisibleWindows -ForceD3D11`；临时启动选项不修改正式图形 API 配置。
日志、帧图、Owner／Server Build 快照及每个进程的退出码保存在 `Logs/M4/`。

测试夹具通过服务器 XP 入口一次推进到 18 级，只固定抽样随机源以保证六种武器覆盖；不改正式升级规则或武器数值。
选卡阶段和目标阶段分别断线重连，另一玩家同时完成自己的队列。Ovid 成熟时间按本轮用户追加要求从 60 秒改为正式 3 秒配置；出生动画、攻击冷却、绝对成熟时间基线及断线恢复语义保持原实现。
原 Equipment 专用多人夹具仍保留，其测试运行时使用 Equipment-only 节奏，适配二阶段确认；正式配置不受影响。

## 人工验收步骤

1. 使用同一构建从 Boot 启动两端。确认只弹出本人的菜单，另一端可继续行动；不从单独 Demo 场景进入。
2. 对照规则检查 4／12／18 级武器、7／9／11… Perk。两人选不同结果，确认四槽上限和全局 Perk 继承。
3. 第一阶段分别点击按钮和按 1／2／3。Equipment 选卡后确认 Build 尚未改变；在目标阶段分别点击及按 1／2／3／4，检查新增或具体升级等级和目标武器名称。
4. 在目标阶段返回，核对原卡片内容、顺序不变。重复点击、旧阶段请求不能重复发奖。
5. 在两个阶段分别断线并重连，核对同一候选、正确阶段、原 Build 和 Perk 累加。重连后旧请求不可使用。
6. 使用装备后的六种武器攻击，检查另一端近战、火球、龙息、Circling、冲刺火径和 Ovid 表现。Ovid 保留原成熟等待；震屏、普通击退和 Ultimate 继续按 M2／M3 流程验收。
7. 退出并重载 Gameplay，检查旧菜单、锁定、Owner 绑定及旧目标均已清理。

## 回退

M4 单独提交。仅回退该提交中的选择扩展、规则资产、Boot／Prefab 接线、菜单及测试变更。
保留此前 M1／M2／M3 和用户初始武器 Inspector 提交。新局验证原 Equipment 流程；不混用不同合同版本、不热回退正在运行的对局。

## 实际验证记录

2026-09-10，Unity 6000.3.17f1。原工作树基线为 `2dcf7af`。本轮同时观察到另一批 FMOD／音频修改，M4 提交不包含这些文件或 NetworkPlayer 上的音频字段。

| 验证 | 实际结果与证据 |
| --- | --- |
| 选择、恢复、UI 与隔离 Debug 回归 | `Logs/M4/selection-final-playmode.xml`：60／60 通过 |
| 网络 EditMode 回归（包括合同） | `Logs/M4/network-editmode.xml`：336／336 通过 |
| Gameplay 完整回归，音频修改前 | `Logs/M4/gameplay-playmode.xml`：353／361 通过；2 个原联机测试因缺失 FMOD 事件失败，另 6 个 Debug 测试报已有 NetworkCombatWorld；Debug 6 项隔离重跑通过 |
| 当前工作树 Gameplay 集成回归 | `Logs/M4/gameplay-integration-playmode.xml`：379／387 通过；2 个新音频缺失 Bank 测试报未预期的 FMOD Error，6 个 Debug 测试仍报 NetworkCombatWorld 残留。M4、镜头、普通击退及 Ultimate 相关用例通过；不能据此声明整个项目测试全绿 |
| 正式 Boot，Host＋Client | `Logs/M4/Host-20260910-142212-385-p7977/`：两个角色均 PASS，退出码均 0；两个阶段恢复、17 次逐级奖励、额外 Equipment 第四目标、六种武器攻击和观察端表现、场景退出清理通过 |
| 正式 Boot，server-only＋两个 Client | `Logs/M4/Dedicated-20260910-143523-087-p7980/`：三个角色均 PASS，退出码均 0；覆盖与 Host 相同。包含当前音频修改，不表示这些修改属于 M4 |

上述两个进程通过记录使用调整前的 60 秒成熟配置。用户追加要求后的最终 3 秒配置已完成复测：

| 最终复测 | 结果 |
| --- | --- |
| 选择／恢复／Debug／召唤时序／镜头与击退相关 PlayMode | `Logs/M4/three-second-playmode.xml`：99／99 通过 |
| Ovid 资产迁移 EditMode | `Logs/M4/three-second-migration-editmode-2.xml`：31／31 通过。首轮 30／31，唯一失败是旧用例仍断言初始武器为火球 ID 2；已按正式 Circling ID 6 修正测试，未改玩家初始武器 |
| 最终构建 | `Logs/M4/build-three-second.log`：Build Successful；产物 `Builds/M4Selection/M4Selection.exe` |
| 最终 Host＋Client | `Logs/M4/Host-20260910-145028-931-p7981/`：两角色 PASS，退出码均 0 |
| 最终 server-only＋双 Client | `Logs/M4/Dedicated-20260910-145028-931-p7982/`：三角色 PASS，退出码均 0 |

最终两个模式使用同一版本构建，覆盖上述奖励、恢复、六武器攻击与观察端表现以及退出清理。此次 3 秒调整同时更新正式 Ovid Prefab、组件默认值和迁移配置，重跑迁移不会恢复为 60 秒；未调整出生动画时长和后续攻击冷却。

过程中的失败记录没有删除：最早夹具停止导航后没有清除 Rigidbody 重力，并且持续保留合成移动输入，导致目标离开召唤检测范围；修正测试设置后又发现普通攻击会在等待成熟期间将目标击退出范围，遂先验证召唤攻击再开放其他武器。双 Client 的早期单次冲刺验证也曾超时；最终夹具等待两端都收到表现，并允许新的实际冲刺，既不重放旧请求，也不放宽表现时效。日志中初次观察的火径计数为 0、最终通过；早期单次火径未到达的具体原因没有独立证明，不将其归因于 M4 奖励或网络协议。

可见 D3D11 截图已经检查：Perk 成长文字、四个武器目标和返回按钮清晰可见。隐藏窗口的黑屏帧不作为视觉证据。进程结束必须同时满足角色 PASS 和退出码 0。

**仍需人工确认：**真实键盘 1／2／3／4、实际鼠标操作与观感、六武器联合作战的持续画面以及完整 M2／M3／Ultimate 人工回归。自动进程检查调用真实按钮事件和与数字键共享的 Select 入口，不能等同于已操作物理键盘。
