# Limbo：空间机制收尾与 Dash 接入

**当前进度更新：** 后续LostSoul行为与原始美术已完成本轮技术验证，最新预览截止449.9166666666667秒。当前入口、构建和双端连续证据见 [LostSoul接入报告](limbo-lostsoul-integration.md)；本页保留Dash阶段的历史记录。

**美术状态更新（2026-09-15）：** 七套参考身体及八组提示已接入来源资源；本页下方的红点、普通 Skeleton 替代精英、橙色线条描述属于当时构建。最新美术验证、保留差异与待验项见 [序列帧美术恢复](limbo-art-restoration.md)。本页已有的数值／行为证据继续保留，不能自动视为新美术构建的运行证据。

本轮技术验证使用真实 D3D11 游戏窗口和同构建的 Mirror Host／Client。人工玩法压力留待整体接入完成后，不用本轮补血、定位和自动选卡的机制记录判断难度。B／围栏、全员屏外与服务端重定位修复的完整记录见 [空间机制收尾](limbo-spatial-closure.md)。

## 来源、适配及运行入口

恢复快照 `docs/evidence/hellmaiden-attacks/runtime-assets.json` SHA-256 仍为 `4dcefbcae22fb6e8cd067618cd0ba782eb5a23d07b20a31a3ecf3323cd6d5c38`。Dash 控制器／攻击／动画对象分别是 `o00004 / o00061 / o00065`。`Tools/Export-LimboDash.py` 只提取数值与绑定；输出 `Assets/_Project/Content/NetworkCombat/Limbo/Dash/DashSource.json`，运行适配文件为同目录 `DashAdapted.json`，重复提取不覆盖后者。

| 项目 | 来源值／本次适配 |
|---|---|
| v0 / v1 属性 | 70／50／6 与 180／80／6；基础 XP 7，击退倍率 1。HP／伤害／基础追击速度按 1:1，出生追击速度服务端抽样 U(.9,1.1) |
| 阶段 | Warning .5 + .28 = .78；Active .429999948；Recovery .08；结束恢复后冷却 .5。触发距离 5 |
| 冲刺 | 名义距离 6，曲线 `2u-u²`；锁向，不追踪，不返回；距离与时长不乘出生移速随机值 |
| 命中 | 随身体移动的 CircleCollider2D，半径 .55，局部中心 y=.46；仅 Active 开启 Thorns。身体碰撞圆 .46，偏移 y=.3 |
| 物理 | mass=20、linearDamping=10、angularDamping=0、gravityScale=0、FreezeRotation。读取导出 Prefab 中完整的原生 Rigidbody2D 块，并在来源文件保存该 Prefab 哈希；运行快照的原生组件引用本身没有展开这些字段 |
| 碰撞层 | Active 临时排除 EnemyCollision（64），正常结束／取消／死亡／重定位／交接恢复原状态 |
| 动画 | 保留 12 个方向绑定、Transition 播放参数及事件时间，包括来源 `rigth` 拼写；使用当前资源的独立动画副本按来源长度适配。未迁入原始 Dash 精灵和音频 |
| 外观／预警 | 当前参考 Brotchi 红点外观；三段橙色线条组成方向箭头。预警固定在攻击起点，没有伤害组件；数值动画与身体命中窗口独立。不能称为原始美术还原 |
| Stats 引用 | 每次初始化、重置和攻击资源复用重绑当前 Stats，修正来源旧引用风险；沿用已批准的适配决定 |

实际物理位移还受固定物理步长、阻尼和碰撞影响，不能把名义曲线位置当成刚体实际位置，也不能将每次实际位移强制补足 6。日志同时保留曲线位置、刚体位置、速度、碰撞层和阶段期限。

`LimboDashAssets.Create` 只创建缺少的独立参考资源；已有适配值与资源不覆盖。`UpdatePhysics` 为明确的定向物理参数更新。生成操作不审批验证状态；`MarkSpatialValidated / MarkValidated` 是查阅记录后的显式验收操作。敌人行为与 B／围栏门槛分别管理，Full 保留 LostSoul／Ghoul 的实现门槛。

```powershell
# 最终构建目录在本报告连续验证结果中记录。
./Tools/Run-LimboReference.ps1 -Profile dash -BuildDirectory Builds/LimboDashRelease20260915 -Windowed -RunName dash-manual
# 有测试辅助的技术预览：普通移动输入、低血量补血、选择实际提供的第一张卡
./Tools/Run-LimboReference.ps1 -Profile dash-validation -BuildDirectory Builds/LimboDashRelease20260915 -Windowed -AutoWalk -RunName dash-technical
# 隔离配置：v0/v1、Host/Client 目标，main/boundary/reuse 三种矩阵
./Tools/Run-LimboReference.ps1 -Profile dash-fixture -BuildDirectory Builds/LimboDashRelease20260915 -DashVariant 1 -DashCase boundary -FixtureTarget client -WaitFor 2 -Windowed -RunName dash-boundary-manual
```

`dash` 没有自动补血／选卡／定位；不传 `-AutoWalk` 时也没有自动移动。正常产品启动和普通波次没有切换到参考资产。

## 网络执行与交接

沿用 actionId、epoch、模拟权、检查点、PlayerDamageInteraction 和现有 GAS／Combat 入口。动作新增 Dash 标记、起点、终点、上次曲线位置及预警起点。服务器检查有限数值、阶段次序、配置时长、距离和曲线位置；拒绝客户端伪造的 Dash 配置。镜像端只重建预警及本地玩家命中区，位移由当前模拟方执行。

先关闭旧执行路径并丢弃旧待处理碰撞，再恢复剩余阶段；不回到起点，不重新开启已经过期的 Active。正常结束结算窗口内已有碰撞，取消清理丢弃它们。Active 恢复解除通用近战冻结刚体的冲突，结束后恢复原约束与碰撞排除。

Mirror 当前自动序列化会让所有动作状态多传 1 个布尔值和 4 个 Vector2，即 33 字节，并非只有 Dash 才增加。按当前 20 Hz，一个敌人每条状态流约增加 660 B/s；30 只约 19.8 kB/s，尚未包括消息头与多接收端转发。没有增加另一个攻击计时器、移动模拟器或伤害 RPC。

## 已完成的机制矩阵

主矩阵构建 `Builds/LimboDashPhysics20260915`，记录 `dash-physics-20260915-{0-host,0-client,1-host,1-client}`。均为 150 秒双窗口隔离流程，两端最终 damageAreas=0、warnings=0。

| 检查 | 结果 |
|---|---|
| 两个变体 × 两个目标端 × 四方向 | 每组两端均记录 LD 8、RD 7、RU 7、LU 7 次进入 Recovery 的完整动作；不是按名义定位方向推算 |
| 实际扣血 | 所有 v0 损血记录为 50，v1 为 80；所有伤害尝试均在 Active，当前 Stats 绑定错误为 0 |
| W／A／R | 全部有效动作的期限差分别 .78／约 .43／.08；同动作的起点、终点、预警起点没有被横移目标或交接改写 |
| 模拟权切换 | 四组各包含 Warning、Active、Recovery 的成功交接，保留动作与剩余期限；旧路径关闭，无额外位移执行 |
| 暂停／慢动作 | Active 使用既有 PauseManager 暂停约 2 秒墙钟，战斗时间冻结；99–105 秒用既有慢动作租约 .25 验证位移与攻击期限同步。此处是显式慢动作测试，不冒充自然围栏触发 |
| 屏外重置入口 | 110 秒显式调用现有 ReferenceReposition，同一敌人的重置版本 0→1，继续绑定当前 Stats；该步骤不是敌人池复用 |
| 受击、取消和死亡 | 120 秒后开放正常武器，四组均由有效武器伤害自然击杀；没有用强制销毁替代。未将这些死亡统一宣称为 Active 阶段自然死亡样本 |

主矩阵中 62 秒后两个玩家同处测试区域，同一次冲刺可以分别伤到两名玩家；这与同一玩家重复扣血不同。首伤至死亡两端观察点含确认延迟，部分记录相差较大，保留原值，不作为压力或 DPS 对照。

最终边界与复用构建 `Builds/LimboDashFixtures20260915`：

| 记录 | 已确认结果 |
|---|---|
| `dash-boundary-final-20260915-0-host / 0-client` | 各 9 次圈外测试最小间距为正，0 次扣血；各 9 次圈内重叠，9 次实际扣血，每次 50 |
| `dash-boundary-final-20260915-1-host / 1-client` | 同上，每次 80；Client 一次重复碰撞尝试受既有无敌限制拦截，实际仍为 9 次扣血 |
| `dash-reuse-20260915-0-host / 0-client` | 两端在同一场景看到 v0 netId=4 自然死亡，40 秒后 v1 netId=6 出生；同一个预警实例跨这两个 ID 复用。50→80 的 Stats 绑定正确，两个敌人均自然受击死亡，80 秒清理为零 |

边界先等待目标租约确认且交接前已锁向的旧动作结束，再依据实际锁向、实际玩家碰撞圆与 Dash 圆布置内／外位置。暖场动作单独保留，不算边界样本。不同敌人是新网络对象，复用的是攻击预警资源。

`Archive-dash-main-20260915` 和 `Archive-dash-fixtures-20260915` 保存记录 ZIP、逐文件 SHA-256 及各自实际构建程序集哈希；源录制不覆盖。运行目录均在 `Logs/LimboReference`。

## 发现并修复的问题与保留的失败记录

1. 首次复制当前 Brotchi 时继承了 mass=1、阻尼=0、gravityScale=1，导致偏移。已从来源完整原生块恢复物理配置；`dash-first-20260915-0-host` 只保留为诊断，后续四组均用正确物理构建重跑。
2. 初始隔离定位靠近地图树木，产生物理阻挡。主矩阵改用地图检测得到的开阔测试位置，不更改地图或扩大命中圆。该调整只用于隔离测试。
3. 最初远端边界脚本假定水平攻击，并把继承旧目标锁向的暖场攻击算成圈外样本。两次失败矩阵 `dash-boundary-20260915-*`、`dash-boundary-aligned-20260915-*` 保留；最终脚本等待旧动作结束并按真实方向测试。这是测试定位错误，不通过放宽伤害判定解决。
4. 资产工具曾把证据说明写进 Ready 状态的阻塞理由字段，导致启动仍被挡住。说明归入报告，阻塞字段只表达实际阻塞；创建和审批操作已分开。

## 389.6 秒连续技术预览

直接使用主 Timeline 的 `[0,389.6)`：11 个敌军片段，C 原始名义预算合计 917，两个 L 没有固定累计数量。部分 C 被预览终点截断，不要求实际出生 917。保留 285 秒极短片段的跨帧生成、240 秒 Skeleton v2、330 秒精英 v1、333 秒围栏和 360.31 秒 Brotchi v1；389.6 秒不启动 LostSoul。XP 分母仍为 841.5766649882。

发布构建 `Builds/LimboDashRelease20260915/MonsterSupergroupLimbo.exe` 已完成一次单人 Host 与一次 Host／Client 的真实画面连续运行，并分别从结算界面重开。三进程所用 NetworkCombat DLL SHA-256 相同：`7101b30c922fb0ff7b306e4baeacfa318e0e5967baac68e905510f6258aeb813`。构建结果及 241 个二进制／清单文件的哈希见 `dash-release-build-20260915/result.json` 和 `binary-manifest.json`。

| 首局记录 | 单人 Host | Host／Client |
|---|---:|---:|
| 运行目录 | `dash-preview-solo-20260915` | `dash-preview-pair-20260915` |
| 终点 | 389.6 | 两端 389.6 |
| 成功生成／确认死亡／无经验淘汰／结束快照存活 | 1286／1179／39／68 | 1374／1276／37／61 |
| 生成事件处记录的存活峰值 | 169 | 202 |
| Rusher v2 的 L 累计生成 | 348 | 447 |
| Dash v0 的 L 累计生成 | 121 | 109 |
| Skeleton v2 的 C 实际生成 | 192／原预算200 | 193／原预算200 |
| 两段 Slime v0 的 C 实际生成 | 60＋90 | 60＋90 |
| Brotchi v1 的 C 实际生成 | 58／原预算150 | 58／原预算150 |
| 出生属性不匹配 | 0／1286 | 两端各0／1374；同一1374个ID的HP、伤害、速度、XP一致 |
| 选卡次数／最长墙钟停留 | 22／.229秒 | Host18／.258秒；Client20／.402秒 |
| 实际测试补血次数 | 0 | Host0／Client5 |

这两局开启了技术保护、自动普通移动和确定性选卡，即使未触发补血的单人局也不重新归类为正常压力测试。没有额外跳时、强制生成或修改敌人属性。不同 L 累计数量来自实际击杀／补怪，不表示多人难度系数；结束快照保留停止前存活数，不是场景清理后的残留对象数。

285秒极短C片段：单人60只在285.0180–287.0340秒出生，双端60只在285.0226–287.0341秒出生；最短间隔均约.03333秒，保留逐次等待，没有改成同帧60只。

首次围栏在单人约333.96秒、双端约334.99秒进入入场。双端 Framing／Building／Shrinking／Stopping 的战斗时间与状态一致，实际碰撞和橙色显示均有画面记录。单人围栏期间仍有 Dash Active 样本；本次双端连续局的 Dash 在围栏前已退出，双端慢动作一致性依赖主隔离矩阵的租约测试，不能声称这局捕获了不存在的重叠攻击。B 不在本连续区间内，继续采用独立空间矩阵记录。

首局结束后三端 `damageAreas=0 / warnings=0`，空间记录均为 `slots=0 / states=0 / collisionAreas=0 / livingWarningRoots=0`，timeScale=1。Host 界面点击“重新开始”后，单人新 RunId 为 `a271d6b2746147bba77ec9eb8dc61aa3`，双端共同为 `91cd1135209d4db885075d90551183b5`。双端0.4秒画面显示生成0、存活0，玩家500HP／等级1／武器1；随后两端观察到相同的65个新敌人ID及属性。单人新轮次观察至约87秒，双端至约41秒后停止测试进程，第二局均未运行至终点，不算第二次完整验收。

单人首局发现一个保留的显示问题：现有 Ovid 召唤物 `caccoon` 材质被按 Float／Range 读取 `_GlowColor`，Unity 开发控制台出现错误。Dash 参考资源没有引用此材质；确切触发调用尚未定位。本轮不把它改写为 Dash 故障，也不以这局宣称所有现有武器画面无错误。问题和带提示的原图均保留，后续应在现有 Ovid 显示路径做定向修复；没有静默改选卡或关闭错误日志绕过。另有 A* 在重定位／回收时取消待处理路径的警告，未观察到托管异常、断线或出生属性错误。

`Archive-dash-preview-20260915` 保存两局及重开记录、三端结算／重开截图、出生／伤害／动作／陷阱记录、构建哈希和摘要。连续技术流程已通过；上面的现有召唤物材质显示问题单独保留。

## 自动化及未完成项

发布前 EditMode 125／125 通过，包含参考配置、Imp、近战、空间规则、Dash 和交接／协议／调度回归。生产行为的 PlayMode 16／16 已通过，覆盖真实协程、近战展示、Imp 与模拟权恢复。具体 XML 在 `dash-release-regression-20260915/edit.xml`、`dash-physics-regression-20260915/play.xml`；自动化不能替代上述画面记录。

人工压力、原始精灵外观、LostSoul、Ghoul、完整 720.9 秒转场等待、完整断线重连／倒地复活矩阵、同条件原游戏正常流程压力对照仍未完成。当前玩家基线 HP500、速度4.55、武器1、XP修正2、现有镜头及 Nordic 地图没有改成新难度设计。
