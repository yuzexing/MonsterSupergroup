# 敌人生成定义与 Timeline 配置

## 已迁移的正式入口

Timeline 的 `NetworkEnemySpawnClip.Enemy` 直接引用 `EnemyDefinition`。定义统一组合 `Prefab`、`EnemyStatsDefinition` 和 `EnemyAppearanceDefinition`。外观资产保存 LUT 与 Original → Baked 图集映射。原色也使用明确的 Appearance 资产，只是 LUT 和映射为空。

资产目录：`Assets/_Project/Content/NetworkCombat/EnemyDefinitions/`。

- `Definitions/`：21 份已迁移的具体敌人组合，例如 `Brotchi_Level02.asset`。
- `Stats/`：基础 HP、Damage、Speed、XP、击退等；不保存当前血量。
- `Appearances/`：配色方案和现有预烘焙图集引用；本功能不生成新 LUT 或重新烘焙图片。
- `EnemyCatalog.asset`：可选择的定义目录，由 Boot 的 NetworkManager 统一引用。

已迁移 46 条 Timeline、97 个片段，包括原始 Limbo、既有验证夹具、普通波次和用户的 `CustomStatges/TestTimeline.playable`。`Full.asset` 原先选择的自定义 Timeline 未被改回 Limbo。数据库和原始导入文件仍保留作历史来源，但已迁移片段不再从旧 EnemyDB 查数值或外观。

## 日常操作

1. 双击需要编辑的 `.playable`，选中时间轴右侧的具体刷怪片段。
2. 在 Inspector 的 **Network Enemy Spawn Clip → Enemy** 下拉框中搜索、选择定义。不再手填 EnemyName、Variant 下标或 Prefab。
3. 下方 Prefab、Stats、Appearance、Definition ID、HP 等是只读预览。点击 **Open Definition** 打开定义资产。
4. 在定义资产中配置 Prefab、Stats 和 Appearance。数值在 Stats 资产中编辑；配色及烘焙映射在 Appearance 资产中编辑。
5. 刷怪 Start、Duration、模式、Count、曲线、Cooldown、接触半径等仍在 Timeline 片段中配置。Source Location 是可选来源备注。
6. 非 Play Mode 修改后重新开始一局；已经捕获的运行数值不会因修改资产自动更新。

创建入口：`Tools → MonsterSupergroup → Enemies → Create Enemy Definition`。该工具创建 Definition、默认 Stats、原色 Appearance，并加入目录。随后配置有效网络 Prefab。

复制现有类型：Definition Inspector 的 **Duplicate as New Enemy Definition**。新资产自动获得新 ID，但 Stats/Appearance 默认仍共享；需要独立调参时，先复制这些资产再重新绑定，避免影响其他使用者。

## 稳定 ID 与目录

DefinitionId 由编辑器生成并持久保存，正常改名、移动、重导入、修改数值不会改变它。Inspector 中只读显示并可复制。普通 Ctrl+D 会复制旧 ID，因此会被重复校验拦截；推荐使用专用复制按钮。只有确认是新副本时，才使用 **Assign New ID to This Copy**。

DefinitionId、Prefab 的 Mirror assetId、场景实例 netId 是不同身份。多个定义可以使用同一个 Prefab；普通波次的 DefinitionIndex 与 PrefabIndex 分开，避免把不同数值/配色合并。

`Refresh Catalog` 用于重新发现定义；`Validate All` 检查 ID、资源引用、网络组件、动画图集映射和 Timeline 的定义引用。构建前刷新内容指纹并验证。正式场景无需为每个敌人重复指定外观数据库。

## 运行链路

Boot Catalog → Registry → Timeline 编译捕获定义 → 服务端计算实际出生数值 → ConfigureBirth → Mirror Spawn。出生消息带 DefinitionId 和实际参数。客户端按 ID 解析本地外观，不重新随机速度或自行重算另一份 HP。

独立 Client 的初次 SyncVar 反序列化可能先触发 handoff hook。产品敌人初始化现在必须等到 OnStartServer/OnStartClient 已进入并解析出生定义，避免提前初始化而漏掉外观。Host、远端 Client 和重连路径共用此约束。

远端准备进入场景/恢复时先比较目录指纹；不同内容拒绝进入。这是内容一致性校验，不是反作弊。修改网络代码后必须重新构建两端，不要使用旧 Player 验证新增 GUID 协议。

外观缓存按 Appearance 资产及原始 Sprite 隔离，引用计数避免一个敌人死亡破坏其他存活敌人的图集。不同外观即使共享 LUT 也不会按 LUT 错误合并。销毁的是临时 Sprite，不是原始或烘焙纹理资产。

## 迁移与兼容边界

正式迁移已完成，日常调参无需再次运行 Apply。迁移工具保留 Preview → Review → Apply 流程，并生成源资产备份；失败保存会停止并记录错误。不要把历史导入工具当作刷新按钮：迁移后旧 Limbo 生成器不得覆盖正式新定义。

schema 0 仅为明确的旧测试/迁移入口；schema 1 的 Enemy 为空会报错，不能自动退回旧名称和 Prefab 字段。已迁移的 97 个片段都是 schema 1。旧字段只保留兼容和来源信息。

同一种 Slime 数值搭配 Rusher Prefab 的合法组合保留为独立 Definition，并共享对应 Stats。不是每个 enemyName 只能绑定一个 Prefab。

## 本轮发现并修复的相邻问题

换局后，回收到 Boot 场景的动态经验球可能被 Mirror 的编辑器场景后处理误认成未配置 sceneId 的场景物体。闲置掉落物及飞行特效现在放在本系统拥有的 DontDestroyOnLoad 对象池根下；租用时返回有效场景，结束连接时显式清理。不关闭 Mirror 校验、不手工伪造 sceneId。

Timeline 下拉框的内部 UI item id 会被 Unity 改写；选择项现在直接保存定义引用，不再以内部 UI id 查字典。该问题是在实际 GUI 搜索选择时发现，并增加回归测试。

## 实际验收结果（2026-09-20）

| 检查 | 结果与证据 |
|---|---|
| Unity 编译及 Windows 验收包 | 通过；`Logs/EnemyDefinitions/continuation-20260920/final-verification-build.result.json`，Unity 退出码 0 |
| EditMode 相关回归 | 165/165 通过，含 14 项 EnemyDefinition 专项；`continuation-20260920/edit-final.xml` |
| PlayMode 扩展回归 | 17/17 通过；包含定义生成、伤害、重置、死亡、重开、连续三次重开、回血/经验对象池、真实 Circling 击杀与自动拾取；`continuation-20260920/play-final-2.xml` |
| 原始数据一致性 | 97 片段重新导入后 Prefab、基础数值、LUT/映射和时间/模式参数一致；`migration-verification.txt` |
| 重复迁移 | 已执行第二次 Preview/Apply，定义 ID、路径、数值、外观不变；`migration-idempotence.txt` |
| 实际 Timeline GUI | 打开真实下拉、搜索选择、GUI 复制为新定义生成新 ID、再次下拉选中新资产、保存/重导入保留引用；`Gui/acceptance.txt`、`Gui/verified-existing.json`、`Gui/verified-new-definition.json` |
| 独立 Host + Client | 最终验收包双进程通过，均退出码 0；生成两种 Brotchi、同步伤害和配色、重置、死亡后保留另一只的外观、Client 断线重连、整局重开；`Process-final-20260920/result.json` |

GUI 临时资产已经清理，不会留在正式 Catalog 中。GUI 创建/选择与运行生命周期是两个独立验收场景；运行场景使用已迁移的 Brotchi Level01/Level02，而不是把 GUI 临时副本作为正式内容留下。

定向生命周期测试会禁用自动攻击并注入已准入的受控伤害，以隔离出生/外观/重连行为；真实武器物理命中、击杀掉落与自动拾取另由 PlayMode 回归覆盖。双进程使用同机 KCP 和 D3D11，不等于 Steam 跨机器、高延迟丢包、专服或长时间压力验收。

旧经验测试中的两个假设也已隔离：真实击杀测试明确使用 Opening 夹具，不再依赖用户选择的高血量自定义关卡；回血回执等待可靠网络完成，而不是假定两帧内完成。未改变这些测试的伤害、掉落、回血断言。

## 重跑入口

验收包：`Builds/EnemyDefinitions20260920/MonsterSupergroup.exe`。

双进程：`Tools/Run-EnemyDefinitionValidation.ps1`；每次必须使用新的输出目录，避免复用旧同步标记。脚本同时检查探针结果和真实进程退出码，超时强制清理不计作通过。

编辑器构建入口：`MonsterSupergroup.NetworkCombat.Editor.EnemyDefinitionVerification.BuildValidationPlayer`。该入口使用已有 enemy-variants 开发验收配置，不修改正式发布设置。

迁移前备份在 `Logs/EnemyDefinitions/asset-backup-*`，源代码工作记录在 `implementation-20260920/` 和 `continuation-20260920/`。早期失败日志保留用于追溯；最终结果以本文列出的最终 XML/JSON 为准。没有创建 Git commit 或 push。
