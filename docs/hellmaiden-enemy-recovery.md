# HellMaiden 攻击数据恢复结果

本次实现的是证据采集工具与恢复数据，未修改 MonsterSupergroup 的生产攻击代码、场景、Prefab、Limbo 配置或敌人启用门槛，也未修复 HellMaiden 导出工程。

## 已恢复的数据

原游戏包 `Hell Maiden-v0.2.31` 的 Unity 运行时成功反序列化了此前 AssetRipper 导出失败的组件。两次独立进程采集均沿实际加载的 `Progression_Limbo` Timeline 引用读取资源：**31 个敌军片段、8 个数据库身份、16 个变体、9 个不同敌人 Prefab、1038 个引用对象**。只剔除进程实例 ID 后，两份完整字段图一致，未发现悬空引用、字段读取错误或递归深度截断。

- [快照一致性及 SHA256](evidence/hellmaiden-attacks/capture-comparison.json)
- [完整运行资源字段图](evidence/hellmaiden-attacks/runtime-assets.json)
- [按敌人整理的属性、绑定、Transition、事件及攻击配置](evidence/hellmaiden-attacks/recovered-enemies.json)
- [子弹和爆炸组件](evidence/hellmaiden-attacks/projectile-and-explosion.json)
- [碰撞几何与层级 Transform](evidence/hellmaiden-attacks/geometry.json)
- [分次运行、分敌人的攻击记录摘要](evidence/hellmaiden-attacks/observations.json)
- [最终核验：覆盖、出生属性、阶段时间与实际扣血](evidence/hellmaiden-attacks/verification.json)
- [原始快照、日志、采集源码与画面证据包](evidence/hellmaiden-attacks/raw-evidence.zip)

完整原始快照分别在 `Logs/EnemyRecovery/capture-a/assets.json` 和 `capture-b/assets.json`。字段图里的 `o00083` 等是采集图中的稳定引用编号，不是 Unity GUID 或原包 PathID；须与该图及原包哈希一起使用。动画、美术和音频只记录引用与必要参数，没有导入生产资源。

数据库 `currentStats` 在未初始化资源上为零是正常状态；恢复表使用 `baseStats`，出生后的最终值另见事件日志的 `birth`。未把 Prefab 默认属性或未初始化的零值当成运行属性。

## 攻击时序与关键结果

以下时间单位为秒，显示值取舍小数；机器数据保留原始浮点精度。W/A/R 分别是原始代码返回的预警、攻击、恢复阶段时间。普通重复周期还要加控制器冷却与实际帧边界等待；动画存在并不表示接触敌人会运行主动攻击状态。

| 敌人 | W | A | R | 控制器冷却 | 恢复结果 |
|---|---:|---:|---:|---:|---|
| Imp v0/v1 | 0.85 | 0.29 | 0.43 | 2 | 同一组攻击资源；基础周期 3.57，四方向完整绑定已恢复 |
| Skeleton v0/v2 | 0.57 | 0.08 | 0.29 | 0.5 | 基础周期 1.44；独立多边形命中区 |
| Elite_Skeleton v0/v1 | 0.57 | 0.08 | 0.43 | 0.5 | 基础周期 1.58；精英攻击资源单独保存 |
| Brotchi_Dash v0/v1 | 0.78 | 0.43 | 0.08 | 0.5 | W=额外 0.5+动画 0.28；距离 6、曲线和锁向配置均恢复 |
| Ghoul v0 | 见下表 | 见下表 | 0.121428572 | 0.5 | 三个索引完整恢复，只在第三击后完成组合恢复 |
| LostSoul v0/v1 | 1.0333333 | 0.146666661 | 0.75333333 | 1 | 加速追击→爆炸→恢复结束自毁；不按冷却再次攻击 |
| Brotchi、Slime、Rusher | 不适用 | 接触 | 不适用 | 不用于接触频率 | 接触绑定、原始几何及复用情况单独核查 |

Ghoul 真实 `attackSets`：

| 索引 | LeftUp 预警引用 | W | LeftUp 攻击引用 | A |
|---|---|---:|---|---:|
| 0 | Enemy_Ghoul_Warning_Left | 0.292857170 | Enemy_Ghoul_Attack_Left | 0.235714287 |
| 1 | Enemy_Ghoul_Warning_Left 1 | 0.378571451 | Enemy_Ghoul_Attack_Left 1 | 0.200000000 |
| 2 | **Enemy_Ghoul_Attack_Right 2** | 0.207142860 | Enemy_Ghoul_Attack_Left 2 | 0.207142860 |

第三击的 LeftUp 预警确实引用攻击动画，不能按名称推测并改为一个“看起来正确”的 Warning 动画。三击基础周期为各击 W+A 之和，再加一次 R 和 0.5 冷却，约 **2.1428572** 秒；以状态日志核对实际帧边界。第一版批量观察曾把中间 `RecoveryExit` 当作完整组合结束，该版 Ghoul 只有两次完整组合，不算通过；最终采集器仅在索引归零后计完整组合。

## Imp 首个实施任务

Imp v0/v1 已获得两份一致的资源字段快照。最终 `observe-final` 中两个变体各有两个出生／复用段，每段四次完整 W→A→R 循环；实际扣血分别为 50、70。夹具在循环间改变四象限相对位置；16 次投射物记录的固定步位移速度均约为 5。日志含固定步时间、池化后的伤害绑定快照和失活碰撞区的关闭状态。

最终 16 次 Imp 循环的实测 W 为 **0.850390–0.853607**、A 为 **0.300000–0.302066**、R 为 **0.433380–0.435970** 秒。配置值与实测值的差额均在对应阶段记录到的一个帧边界内。逐次时间及容差见 `verification.json/timingChecks`；不把这个帧率下的实测范围改写成新的配置时长。

主要配置来源：字段图 `o00083`（EnemyAnimator）、`o00309`（BulletProjectile），由 Imp 实际 Timeline 引用可追溯。

| 参数 | 原始运行对象 | 当前 MonsterSupergroup | 后续适配 |
|---|---|---|---|
| v0/v1 HP、伤害、基础速度 | 50/50/2；150/70/2 | NetworkEnemyImp 默认 12/5/2；独立 Limbo DB 已有来源值 | 保持从参考 DB 覆盖，出生时一次初始化 |
| 四方向绑定 | 同侧 Up 与 Down 共用 Down 动画 | 迁移工具也按该组合绑定 | 名称组合在此处得到原始证据支持；仍需对照事件与 Transition |
| W/A/R、距离、冷却 | 0.85/0.29/0.43；10；2 | 当前攻击额外时间均为 0，距离 10、冷却 2 | 精确核对实际绑定动画和网络时钟，不只比较冷却字段 |
| 子弹速度 | **5** | **6** | 写入独立参考适配值 |
| duration / timeout | **3 / false** | **5 / true** | 实现来源的屏外结束条件，不把 3 当固定寿命 |
| 自然结束 | `elapsed > 3 && outsideCamera` | 当前启用固定 timeout | 用现有摄像机几何及投射物回收入口校准 |
| 穿透次数 | 1 | 1 | 保持；核查同一命中不会由网络展示重复扣血 |
| attachedWhileCharging | false | false | 保持原始充能／发射状态路径 |
| projectileMovement | **原始对象确为空** | 当前无独立运动组件 | 使用原代码的直线后备分支，不能解释为缺失待补 |
| rotationTransform | **原始对象确为空** | 迁移代码绑定 BulletVisual | 记录为适配差异；原始直线分支不依赖它 |
| 碰撞圆 | 局部半径 0.37，完整层级见字段图 | 现有攻击资源有独立配置 | 按完整 Transform 与触发层检查世界命中范围 |

自然结束规则由原始程序集 `BulletProjectile.FiredFixedUpdateTick` 复核：先判断“在镜头外或启用 timeout”，再判断 `elapsed > duration`。`projectileMovement=null` 时每次固定步沿锁定方向移动 `speed × fixedDeltaTime`。命中达到一次穿透后会进入 Hit/End 并回池，可能早于自然结束。

最终这一组 16 发子弹均因命中结束，尚未在该组运行中触发自然屏外结束；上述自然结束条件属于字段与原始 IL 证据，后续接入时仍须单独验证该分支。

当前适配证据位置：`Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs:94`、`:119`，`Assets/_Project/Content/HellMaiden/Enemies/Imp/GameObject/EnemyBulletAttackImp.prefab:16885`。原始程序集的可读 IL 副本在 `Logs/EnemyRecovery/build/original-attack-il.txt`；导出 C# 只用于导航，不替代该原始程序集证据。

## 复用差异：属性与实际命中不能合并

在同一池中先生成低变体，再生成高变体，观察到：

| 场景 | 控制器当前伤害 | 复用时实际扣血 |
|---|---:|---:|
| Brotchi v0→v1 | 60 | 50 |
| Brotchi_Dash v0→v1 | 80 | 50 |
| Rusher 外观 Slime v2→v1 | 50 | 首次复用 30，后一次 50 |
| Slime 外观 v0→v1 | 50 | 首次复用 40，后一次 50 |

这是受对象历史影响的运行结果，不是将 v1 数据库伤害重新定为 50/30/40。原始 `ThornsEnemyAttack.Start`、`EnemyAttackDash.Start` 绑定伤害 Stats，而工厂在重生时替换控制器 Stats；Thorns 的 `CancelAttack` 又可能重绑，从而产生不同复用结果。最终 `post-start-bindings` 记录引用是否为同一 Stats，供复核。

建议 MonsterSupergroup 在现有生命周期中每次重生绑定当前 Stats，并把避免旧引用明确记为适配差异。尚未实施这一建议，也不声称已完全复制这种来源缺陷。无论选择保留还是修正，都不能引入第二份运行生命或伤害管理。

## LostSoul 爆炸链

爆炸 VFX、碰撞对象、粒子和攻击脚本引用已恢复。实际流程为：预警期间速度切为 6.2×当前移速倍率；进入攻击后 Trigger 打开爆炸碰撞；进入恢复时 Stop 关闭；恢复结束调用 `Kill(true,false)` 并归还爆炸对象。

因此命中窗口由攻击阶段约 0.146667 秒决定，不能用整个粒子播放长度替代。原始 `Trigger` 确实没有启动那段粒子寿命检查协程；本条攻击链依靠 Recovery/DestroyEnemy 完成关闭和回收，不需要凭空补一个粒子回调。日志分别记录 Trigger、Stop、碰撞启停和敌人失活。无 XP 由原始 `Kill(true,false)` 调用证实；本次未对 XP 掉落实例做独立计数。

## 采集环境与可复现方式

源包：`F:/BaiduNetdiskDownload/d-地狱公主/Hell Maiden-v0.2.31`，Unity **6000.3.16f1**，Mono。

| 原始文件 | SHA256 |
|---|---|
| Hell Maiden_Data/data.unity3d | a4497eef9dc84990e50ca528a504dc51a99d9c47369ab6798bfa06e2b4f84395 |
| Hell Maiden_Data/Managed/Assembly-CSharp.dll | 97014f8388f08f45f281abdf3f21616e78980122e62bdd68d5d06eb7fad2e928 |
| Hell Maiden_Data/Managed/Kybernetik.Animancer.dll | cf4cf88aba9c7c9000d11024449eff67e8c4c0d937acb022f83799633ab74a40 |

运行副本位于 `Logs/EnemyRecovery/runtime`。工具只在副本注入 `GameDirector.Awake` 入口、工厂返回记录和攻击阶段观察。所有托管 PlayerPrefs 调用转为内存字典，Saved Games 与 persistentDataPath 转入各次输出的 profile 目录；副本使用已有 LocalProfileData，停用 Steam 统计入口。

静态采集保持新档；攻击观察使用无局外升级、武器 ID 1 的初始属性，再停用武器、暂停主波次、逐敌测试，完整循环后补血，轮次之间重置测试位置。观察夹具只将 `CUT_HUB_STR_INTRO2` 标记为已看过，并在场景完成加载后清理一次转场无敌；后续受击无敌仍走原代码。这些均记录在 `fixture-*` 事件中，不属于完整新档流程或原游戏整体压力对照。

操作入口：

```powershell
Tools/EnemyRecovery/Build.ps1
Tools/EnemyRecovery/Run.ps1 -Name capture-new-a -Mode capture
Tools/EnemyRecovery/Run.ps1 -Name capture-new-b -Mode capture
Tools/EnemyRecovery/Run.ps1 -Name observe-new -Mode observe -Enemy all
python Tools/EnemyRecovery/Analyze.py --capture-a Logs/EnemyRecovery/capture-new-a/assets.json --capture-b Logs/EnemyRecovery/capture-new-b/assets.json
python Tools/EnemyRecovery/ProtectInputs.py verify
python Tools/EnemyRecovery/Verify.py --run observe-new
```

首次复现需先将合法持有的原始包复制至上述 runtime 目录，再运行 `ProtectInputs.py baseline`。不能把副本目标改为原包。Build 拒绝向指定目录外打补丁，也拒绝在采集进程运行时重建。Run 拒绝覆盖同名输出；新运行记录工具源码、采集 DLL 和注入后程序集哈希。Analyze 默认比较本次 capture-a/b，复现实验可按其参数入口指定新运行。

`input-fingerprints.json` 对原包 290 个文件与现有存档做哈希，对导出工程 90697 个文件做完整清单，并对本任务涉及的代码／序列化资产类别做哈希。`input-verification.json` 记录前后比较。托管偏好已隔离；未保存 Unity 原生窗口偏好的注册表前后快照，不将该项描述为已完成审计。

## 已执行验收

最终采集副本以真实图形窗口完成测试，结束后已关闭。以下属于原游戏注入副本的隔离机制验收，不代表 MonsterSupergroup 接入验收。

| 项目 | 最终结果 |
|---|---|
| 静态资源 | 两次独立进程的完整字段图一致；31 片段均追溯到实际资源，0 个引用／字段读取错误 |
| Imp | v0/v1 各 8 次完整循环，复用同一对象；各 8 次实际命中为 50／70 |
| Skeleton、精英、Dash | 各实际变体均 8 次完整循环；Dash v1 的旧伤害引用列为来源差异 |
| Ghoul | 8 组三连击，合计 24 个攻击阶段；三个索引分别记录，不把中间恢复当组合结束 |
| LostSoul | v0/v1 各 4 次预警、爆炸、关闭与自然自毁；每次命中 50 |
| 接触敌人 | Brotchi、Slime、Rusher 所有实际变体／外观组合均采集接触与复用；保留旧引用差异 |
| 出生与时序 | 38 次夹具出生的 HP、Damage、移速抽样与来源 DB 相符；各已记录攻击阶段时间符合原始有效时长及帧边界 |
| 文件保护 | 最终进程关闭后再次核对原包、导出工程与现有存档，差异为空 |

最终 Player.log 仍有标题界面切换中的 DOTween 空目标警告／被捕获回调异常，调用栈指向 TitleScreenController；采集器未报字段或观察异常，敌人循环完成。不将“采集检查通过”描述为整个游戏运行零警告。

证据包含两次原始快照、最终逐帧 JSONL、Player.log、运行清单、当次采集源码、原始程序集 IL，以及代表性截图。[Imp v1 实际命中画面](evidence/hellmaiden-attacks/imp-v1-observation.jpg)中玩家由 500 降为 430。所有输出文件的 SHA256 见 [证据清单](evidence/hellmaiden-attacks/evidence-manifest.json)。

## 验收边界与下一步

资源恢复的启用阻塞已从“字段无法取得”变为“数据已恢复，等待适配与网络验证”。生产 `missingEvidence` 和禁用标记保持原状，不通过删标记来宣布完成。

本次没有执行 MonsterSupergroup 新攻击的 Host/Client 接入或验证，也没有完成未经注入原游戏的完整压力对照。原包采集过程有图形窗口，截图和逐帧日志保存在对应 `observe-*` 目录；截图是观察样本，不是完整视频。

**首个后续实施任务：** 按本报告的 Imp 原始绑定、0.85/0.29/0.43 时序、速度 5 和屏外回收规则，在现有网络投射物／GAS 入口建立独立参考适配；保留原始值与适配差异，完成有画面的单人 Host 和 Host/Client 命中、回收、复用验证后，才解除 Imp 对应门槛。围栏网络生命周期、B 预警展示和 L/B/屏外处理验证仍是独立待办。
