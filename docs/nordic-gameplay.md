# Nordic 正式 Gameplay 接入

`Assets/_Project/Scenes/Gameplay.unity` 已接入 Nordic Midgard 静态地图。仍从 Boot → 原准备房间进入，场景路径和 GUID 不变；单独打开 Gameplay 不能替代正式网络初始化。

## 资源与边界

- 正式地图：`Assets/_Project/Content/Nordic/NordicGameplayMap.prefab`，继承 `NordicStaticMap.prefab` 的变体。
- 布局来源：`MidgardLayout.json` 和原静态地图。迁移不重新计算布局、不复制 Nordic DLL、不在运行时生成 Chunk。
- Ground：119.3386 × 67.1280，中心 (0, 0.99)。保留原图美术和实体形状，边界在 `Edges`，普通实体在 `Obstacles`。
- 导航：`GameplayNavigation.bytes`，XY 网格，节点间距 0.25，八向连接且禁止切角。障碍检测直径使用当前四种地面敌人身体半径的最大值和 0.01 安全余量。运行时加载编辑器扫描结果。
- `GameplayMapContext` 统一提供 Ground、就绪状态、实体查询、导航连通性、出生点、边界和瞬移放置。出生搜索使用 0.25 网格，按距离寻找最近合法候选；不移除美术。
- 预览场景 `NordicStaticSample` 和基础静态地图继续独立保留。

## 玩家、移动与动画

`NetworkPlayer.prefab` 保留原根节点、NetworkIdentity、原有 12 个网络组件的顺序、战斗组件、攻击挂点和属性资源。原脚底圆半径 0.1、受击圆半径 0.13。

视觉层级为 `Nordic Visual / Facing / Viking (Character Variant)`。保留 Nordic Scaler 和所有部件的原比例；Facing 负责左右镜像，视觉整体按原 Idle 的最低可见顶点对齐脚底。死亡动画的缩放作用于内部原始层级。

`NordicPlayerAnimator` 使用 Animancer 播放原 Idle、Walk、Dash、Die。Dash 时长读取正式冲刺时长；死亡保持末帧，已有生命恢复时反向播放 Die。没有额外攻击、伤害、复活或动作锁定逻辑。新导入的两段 Nordic 动画移除了源游戏事件；武器动画事件保留。

新增 `NetworkPlayerPresentation` 仅同步动作、朝向、开始时间和时长。状态变化时发送，每秒最多 20 次；远端使用既有生命数据判断存活，不启用本地输入或移动组件。身体各部件使用自己的原颜色参与受击反馈。

正式移动继续用 FixedUpdate、动态刚体及插值。普通移动保留实体碰撞；冲刺保留曲线、速度及消耗，可穿过普通障碍，终点被占用时沿路径向后寻找合法落点。四边实体和按脚底大小计算的边界约束共同限制移动。`GameplayMapContext.Place` 用于出生或明确的位置跳转，Timeline 的已有位置跳转也接入它。

## 镜头、排序与战斗兼容

镜头仍由 `GameplayCameraRig` 和 `LocalPlayerInputBinding` 绑定本机玩家。固定透视 FOV 80、Z=-10、零旋转，ProCamera2D 在 LateUpdate 跟随；关闭自动缩放。震屏事件和设置开关保留，最终位置限制在 Ground 内，极端比例使用留边视口。

瞄准及屏幕范围通过射线与 Z=0 平面求交。正式镜头选用已有 `NordicRenderer2D`，保持 Custom Axis (0,1,0)。默认渲染器索引和全局 Sorting Layers 不变。

玩家和敌人的视觉组合以脚底为 SortingGroup 原点，在 Props 层参与 Y 排序。经验掉落使用相同适配；武器、投射物、命中特效和终极技能在 Foreground，地面攻击预警在 EnemyAttack。材质、内部顺序和命中参数不因排序适配改变。

地面敌人使用项目现有 A* 组件，保留模拟归属、目标选择、移动速度、攻击状态机与原刚体质量。路径按实际脚底推进，避免物理受阻后继续沿过期方向穿过树角。地图内承担模拟的一端不因离开镜头关闭必要物理。刷怪仍使用原时间表、半径和尝试次数，额外检查地形占位与导航可达性；失败记录 `NoLegalPosition`。

## 工具

所有入口均已加入项目共享工具清单，在项目根目录执行：

```powershell
# 重新接入已有静态布局并烘焙导航；不会重新生成 Midgard 美术布局
./Tools/Invoke-ProjectTool.ps1 -ToolId migrate.nordic-gameplay -Apply

# 校验资源、动画绑定、网络组件顺序、唯一 Ground、地图尺寸和导航引用
./Tools/Invoke-ProjectTool.ps1 -ToolId validate.nordic-gameplay

# 生成包含验收组件的独立程序；默认正式 Build Settings 不变
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile nordic-gameplay

# 正式入口、运动、边界、冲刺、受击和截图
./Tools/Invoke-ProjectTool.ps1 -ToolId test.nordic-gameplay

# 追加多人、专服、原六波次、敌人交接、升级和换局流程
./Tools/Invoke-ProjectTool.ps1 -ToolId test.nordic-gameplay -Parameters @{ FullSuite=$true }

# 按原时间表运行到第六波，测量完整地图与 30 个存活敌人的实际渲染开销
./Tools/Invoke-ProjectTool.ps1 -ToolId test.nordic-gameplay -Parameters @{ FullPerformance=$true }
```

如机器没有配置 Unity 路径，为编辑器工具附加 `-Unity 'D:/RealSoftware/6000.3.17f1/Editor/Unity.exe'`。实际验收构建输出位于 `Builds/NordicGameplay/MonsterSupergroup.exe`；常规发布继续使用项目原构建入口。

静态结果和依赖 SHA-256 清单输出到 `Logs/NordicGameplay/static-validation.json`、`dependencies.json`。图形验收会暂停后续刷怪以单独测试边界，因此几何检查截图中的波次状态不能当作正式波次运行结果；正常波次截图和性能记录单独标记。

## 备份与保留项

迁移基线在 `Logs/NordicGameplay/Before-20260913-144449/baseline.zip`，覆盖本次修改的既有文件，并保存开始本次工作时这些文件的已有修改；它不是整个 Unity 工程的完整备份。原 Gameplay、NetworkPlayer 另存于 `Logs/NordicGameplay/MigrationBaseline/`。`baseline-diff.json` 记录本次改动相对于工作开始状态的文件列表，并区分同时发生的 Steam 工作。

恢复时先关闭 Unity，从基线备份提取该列表中的原文件；新地图变体、导航、接入组件和工具是新增内容，可以在确认无引用后移除。不要整包覆盖后续新增修改。项目的 UI、武器数据、敌人数值、波次配置、默认构建场景和默认渲染器均按基线保留。

验收结果与截图索引见 `docs/nordic-gameplay-acceptance.md`（另存于 `Logs/NordicGameplay/验收报告.md`）。该报告区分自动通过、截图核验和未覆盖场景，并记录完整密度的实际帧耗时与限制。
