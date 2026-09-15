# Limbo 阶段一：Imp 接入与 0–105 秒预览

本轮范围是 Brotchi + Imp 参考预览，不是完整 Limbo。来源为 `docs/hellmaiden-enemy-recovery.md` 和 `docs/evidence/hellmaiden-attacks`；原始恢复证据没有改写。

## 配置与实现

- `Assets/_Project/Content/NetworkCombat/Limbo/Imp/ImpSource.json` 是从恢复快照提取的原始配置；`ImpAdapted.json` 是独立适配配置。快照 SHA256 为 `4dcefbcae22fb6e8cd067618cd0ba782eb5a23d07b20a31a3ecf3323cd6d5c38`，Animator 对象 `o00083`，子弹对象 `o00309`。
- `Tools/Export-LimboImp.py` 更新原始提取值，只在适配文件不存在时创建它。`LimboImpAssets.Create` 定向更新独立 ReferenceImp／ReferenceImpBullet 与相关 Timeline 引用，不覆盖普通 Imp、原始数据库或已有适配 JSON。
- 使用现有 SourceEnemyDB／AdaptedEnemyDB 和出生覆盖链：v0 = 50 HP / 50 Damage / 2 Speed；v1 = 150 / 70 / 2，普通出生速度倍率 0.9–1.1。重定位重置后速度倍率恢复 1。
- 已接入恢复的 12 个攻击 Transition，包括 `attackLeftUp` 实际引用 `Enemy_Imp_AttackMoment_LeftDown` 的原始绑定；没有按名字替换它。播放速度 1、Fade 0.25、NormalizedStart NaN 与事件时间保留。W/A/R = 0.85/0.29/0.43，冷却 2，触发距离 10。
- 子弹速度 5、duration 3、固定超时关闭、pierce 1；恢复碰撞圆半径 0.37 与相关层级变换。充能展示关闭碰撞和伤害处理。

## 网络与时间

在现有 `NetworkEnemySimulationWorld` 中保管参考飞行记录，不新增敌人管理、生命或伤害系统。发射协议增加结束模式、服务器战斗时间戳、发射时目标玩家；保留既有 actionId、epoch、发射去重和参数校验。

参考飞行位置由 `Origin + Direction × 5 × (CombatTime − FiredAt)` 求得。只有年龄严格大于 3 秒且位置在参考视野外，服务端才通过现有终止广播回收。命中请求也由服务端只授予一次，再调用命中玩家现有 PlayerMovement → Combat/GAS 入口。远端自己的镜头不能结束全局飞行。记录独立于射手，射手销毁不会取消已发射子弹。

单人使用 Host 实际摄像机边界。多人使用发射时目标的同尺度跟随视野；目标失效后保留最后有效视野。这是多人适配约定。尚未对倒地／断线后的最后视野做专项运行验收。

参考攻击期限、子弹位移和年龄使用同一个服务器战斗时钟，暂停和慢动作改变战斗时钟速率；镜像端同步速率。位置快照、网络传输、插值、交接超时仍使用 Mirror 时钟。本轮验证了暂停；围栏慢动作与其他持续状态留待后续阶段。

## 已完成的隔离画面验证

以下均为 2026-09-14 真正启动的 D3D11 游戏窗口，经过画面观察并保存截图。`imp-v0`／`imp-v1` 为单只敌人的显式机制配置，包含关闭武器、定位、重定位重置、必要补血、暂停和销毁射手等测试操作；不能算正常关卡试玩。

| 记录（`Logs/LimboReference/`） | 已观察结果 |
|---|---|
| `imp-v0-first`，单人 Host | 23 次唯一发射、23 次终止，23 个完整攻击循环；两次各 50 点实际扣血。四象限定位、4 次现有重定位重置、子弹池复用；暂停 3.009 秒，战斗时间不变；射手销毁约 1.37 秒后剩余飞行才自然结束 |
| `imp-v0-pair` | Host/Client 同样的 23 次发射和终止，发射内容逐项一致；Host 至少 21 个完整周期；在预警与飞行中请求并执行模拟权切换，未重复发射；实际 Host 扣血 50 |
| `imp-v1-pair` | 两端同样的 23 次发射和终止、相同发射内容；实际 Host 扣血 70；同样覆盖两种交接时机与暂停、射手销毁后的飞行 |
| `imp-v0-remote` | 强制以 Client 为早期目标；18 次唯一发射／终止，Client 18 个完整攻击循环，实际扣血两次各 50；两端正常结束机制测试 |
| `imp-v1-remote` | 同上，18 次唯一发射／终止，Client 17 个完整攻击循环，实际扣血两次各 70；两端正常结束机制测试 |

Host 观察的 W 为约 0.8503–0.8639 秒、A 为 0.2841–0.3040、R 为 0.4169–0.4349；阶段边界存在约一帧的采样偏差。配置与快照中的期限仍为 0.85/0.29/0.43。多个完整循环和四象限已覆盖，不能把冷却 2 秒当作完整攻击间隔。

已观察子弹超过 3 秒仍存活：v0 记录到约 5.695 秒，v1 约 5.744 秒。新记录包含服务器视野与本地视野，能对照年龄、位置、视野和终止时间。玩家受伤记录有本地变化及同值的同步确认回调；同值回调不计为第二次扣血。所有有效扣血均可对应服务端唯一命中授予。

这里的“复用”证据是子弹对象池和敌人的现有重定位重置。当前联网敌人生成／移除使用 Instantiate/NetworkServer.Destroy，没有新增敌人对象池，也不声称验证了不存在的网络敌人池。

## 运行入口与门槛

```powershell
# 构建：Unity 执行 MonsterSupergroup.NetworkCombat.Editor.LimboImpAssets.CreateAndBuild
./Tools/Run-LimboReference.ps1 -Role host -Profile imp -AutoWalk -RunName imp-host
# 双端：Host 等待准备房原始成员，Client 用同一端口连接
./Tools/Run-LimboReference.ps1 -Role host -Profile imp -WaitFor 2 -Port 7996 -AutoWalk -RunName imp-pair
./Tools/Run-LimboReference.ps1 -Role client -Profile imp -Port 7996 -AutoWalk -RunName imp-pair
# 显式机制测试：imp-v0 / imp-v1，可加 -FixtureTarget client
```

连续预览使用 `Resources/LimboReference/ImpOpening.asset`，终点 105 秒；原片段仍为 Brotchi 1–136/250、Imp 60–105/15。105 秒精英不进入预览。`imp-validation` 只供显式待验证配置使用。

统一门槛区分 EvidenceMissing、ImplementationPending、ValidationPending、Ready；验证配置只能越过 ValidationPending，不能越过数据或实现缺口。已完成上述机制验证后仅解除 Imp 的门槛；Slime／Rusher／Brotchi v1 对应的 10 个片段明确保留 ValidationPending。Full 继续被精英／Skeleton 等实现缺口及围栏生命周期阻挡。最终构建的 `full` 画面实际显示 `Elite_Skeleton [ImplementationPending]`，说明攻击数据已恢复但行为尚未接入和验证，生成数为 0；没有沿用过时的“原始数据缺失”提示。

## 连续预览与最终回归

| 验收类别 | 实际结果及限制 |
|---|---|
| 规则与数值 | 最终 EditMode 回归 32 项通过、0 失败，覆盖恢复配置、参考边界、原始预算、视野与年龄终止条件、协议序列化／字节估算及既有 Imp／交接／参考调度回归。最终 Windows 开发构建成功 |
| 单人 Host 运行 | `imp-preview-host` 正常到达 105 秒：184 次成功生成（169 Brotchi + 15 Imp）、0 位置失败、116 次死亡，结束时存活 68、峰值 82；184 份出生属性匹配。玩家结束生命 450，连续预览中的这次 50 点扣血来自接触敌人，不能当作 Imp 的独立命中证据 |
| Host/Client 运行 | `imp-preview-pair` 第一局两端到达 105 秒，共同记录相同的 179 个敌人 ID（166 Brotchi + 13 Imp），两端出生属性一致；Client 未重复驱动刷怪。0 位置失败、12 次死亡，结束存活及峰值均为 167。Host 生命 400，Client 500 |
| 重复开局 | 双端通过结算界面实际重新开始，新 RunId、角色、生命和波次归零，重新生成；观察至约 80 秒后停止。第二局仅用于重复开局验证，未完成第二次 105 秒运行 |
| 完整配置门槛 | 最终构建另起 `imp-full-gate-final`，有画面确认 Full 在战斗开始前被未实现的精英阻止；0 次生成 |

连续预览使用 `-AutoWalk` 通过现有 `PlayerMovement.SetDirection` 提供移动输入，升级通过正常界面手动选择；未启用机制配置中的补血、定位、强制生成、暂停脚本或销毁射手。单人升级选择依次为重振、迅捷处决、灼热余迹、放大、重振升级，保留于日志与截图。

双端第一局有较长时间停留在选卡界面，玩家攻击和升级保护影响了死亡数与密度；这局用于联机一致性验收，不用于压力等价结论。Imp 原预算仍为 15，双端只在全局预览截止前生成了 13 只；来源模式使用逐次相对等待，帧边界会累积延迟，不能把名义预算当作截止时必须成功生成的数量。没有将剩余数量压缩补发，也没有改写片段。

单人连续预览记录了 26 次唯一 Imp 发射和 23 次逐颗自然终止；剩余 3 颗由现有关卡结束清理入口统一回收。该入口没有逐颗终止事件日志，因此不能宣称获得了 26 份逐颗终止记录。双端原始日志包含两局，分析时必须按 RunId／round 分组，不能将第一局的完成状态套用到第二局。

原始 JSONL／CSV 的无损压缩副本、哈希及逐局统计见 [证据清单](evidence/limbo-imp/README.md) 和 [validation.json](evidence/limbo-imp/validation.json)。[最终测试结果](evidence/limbo-imp/editmode-final.xml) 与 [试玩截图索引](evidence/limbo-imp/README.md#画面) 随报告保存。未开展首伤至击杀时间的专项测量，也未完成同条件原游戏压力对照。

## 适配差异与未完成项

| 项目 | 原始值／行为 | 本项目适配 |
|---|---|---|
| 原始攻击数值与绑定 | 本次恢复快照 | 保留；外观使用本项目现有 Imp 资源 |
| 旧 Stats 引用 | 原始复用路径可能持有旧伤害引用 | 出生、重置重新绑定当前变体；每次发射读取当前 Stats，池化子弹重新配置伤害。按用户选择修正来源缺陷 |
| 空方法动画回调 | 事件时间存在，但恢复的音频目标调用方法名为空 | 保留事件时间，省略无实际调用的方法／音频目标引用，不迁入来源音频 |
| 子弹终止 | 原始单机本地判断 | 服务端统一判断并广播；客户端镜头不拥有终止权 |
| 玩家／成长 | 来源 XP 修正 1，参考镜头可视高 24 | 本轮仍为当前 XP 修正 2、可视高约 16.782；生命 500、移速 4.55、武器 ID 1、Nordic 地图。校准留阶段五 |
| 关卡边界 | 主流程至 720.9 秒 | 本轮只交付 0–105 秒；不含精英、可选任务、Minos 战 |

后续进度更新：Skeleton、Elite_Skeleton 及接触组合已有独立接入和机制记录，正常单人完成 209.05 秒与结算界面重开；双端连续及双端重开仍待完成，见 [阶段二记录](limbo-stage2-integration.md)。B／围栏正进行后台接入与检查，画面验证尚未通过。L/B/屏外完整专项验证、特殊攻击、完整流程、成员断线重连、倒地／复活和来源正常流程对照均未因 Imp 或上述接入而自动完成。

本轮结论限于恢复规则接入及 Brotchi + Imp 的当前项目运行验证；未与原始游戏完成同条件压力对照，不能声称原游戏战斗效果已完全复刻。
