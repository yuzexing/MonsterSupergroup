# EditorTool 完整清单

本文件由 `Tools/Update-EditorToolDocs.py` 从共享清单生成。不要直接编辑。

## 当前工具

| ID | 名称 / 用途 | 分类 / 使用者 | 参数 | 运行条件 | 影响 / 产物 |
| --- | --- | --- | --- | --- | --- |
| sample.gas-rebuild | 重建 GAS Vertical Slice：创建/重建 GAS 样例场景和配置 | 维护与样例 / 人工 / AI | Apply | edit | 创建/重建 GAS 样例场景和配置；Logs/ProjectTools |
| validate.gas | 校验 GAS：检查 Modifier 配置与生成注册表 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| generate.gas-registry | 生成 GAS 注册表：扫描 Modifier 类型，更新 GeneratedModifierRegistry.g.cs | 制作 / 人工 / AI | Apply | edit | 扫描 Modifier 类型，更新 GeneratedModifierRegistry.g.cs；Logs/ProjectTools |
| import.beam | 导入 beam：导入或重建 beam 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入或重建 beam 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| validate.beam | 校验 光束 资源：校验当前已导入的 beam 配置与表现引用 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| preview.beam | 光束 表现预览：采集现有 beam 表现 PNG；仍需视觉核验 | 表现预览 / 人工 / AI | — | 图形设备 | 只读；不修改玩法资产；Logs/Phase02 |
| import.circling | 导入 circling：导入或重建 circling 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入或重建 circling 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| validate.circling | 校验 环绕 资源：校验当前已导入的 circling 配置与表现引用 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| diagnostic.circling-dependencies | 环绕 表现依赖诊断：输出 circling 的表现资源依赖 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| preview.circling | 环绕 表现预览：采集现有 circling 表现 PNG；仍需视觉核验 | 表现预览 / 人工 / AI | — | 图形设备 | 只读；不修改玩法资产；Logs/Phase02 |
| import.dash | 导入 dash：导入或重建 dash 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入或重建 dash 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| maintenance.restore-dash | 恢复来源冲刺默认配置：覆盖玩家冲刺参数为参考工程默认配置 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 覆盖玩家冲刺参数为参考工程默认配置；Logs/ProjectTools |
| validate.dash | 校验 冲刺 资源：校验当前已导入的 dash 配置与表现引用 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| diagnostic.dash-dependencies | 冲刺 表现依赖诊断：输出 dash 的表现资源依赖 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| preview.dash | 冲刺 表现预览：采集现有 dash 表现 PNG；仍需视觉核验 | 表现预览 / 人工 / AI | — | 图形设备 | 只读；不修改玩法资产；Logs/Phase02 |
| import.melee | 导入 melee：导入或重建 melee 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入或重建 melee 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| validate.melee | 校验 近战 资源：校验当前已导入的 melee 配置与表现引用 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| preview.melee | 近战 表现预览：采集现有 melee 表现 PNG；仍需视觉核验 | 表现预览 / 人工 / AI | — | 图形设备 | 只读；不修改玩法资产；Logs/Phase02 |
| rebuild.dante | 重建 dante：导入或重建 dante 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply | edit | 导入或重建 dante 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| maintenance.reserialize-equipment | 重存规范装备资产：一次性重存已完成；仅用于明确的资源维护，重存八种装备及元数据 | 维护与样例 / 人工 / AI | Apply | edit | 一次性重存已完成；仅用于明确的资源维护，重存八种装备及元数据；Logs/ProjectTools |
| import.projectile | 导入 projectile：导入或重建 projectile 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入或重建 projectile 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| validate.projectile | 校验 投射物 资源：校验当前已导入的 projectile 配置与表现引用 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| preview.projectile | 投射物 表现预览：采集现有 projectile 表现 PNG；仍需视觉核验 | 表现预览 / 人工 / AI | — | 图形设备 | 只读；不修改玩法资产；Logs/Wisp/Previews |
| import.ultimate | 导入 ultimate：导入或重建 ultimate 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入或重建 ultimate 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| validate.ultimate | 校验 大招 资源：校验当前已导入的 ultimate 配置与表现引用 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| preview.ultimate | 大招 表现预览：采集现有 ultimate 表现 PNG；仍需视觉核验 | 表现预览 / 人工 / AI | — | 图形设备, 等待 Play Mode 完成 | 只读；不修改玩法资产；Logs/Phase02 |
| import.summon | 导入 summon：导入或重建 summon 的玩法和表现资产；可能覆盖当前绑定和默认参数 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入或重建 summon 的玩法和表现资产；可能覆盖当前绑定和默认参数；Logs/ProjectTools |
| validate.summon | 校验 召唤 资源：校验当前已导入的 summon 配置与表现引用 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| preview.summon-shadow | 召唤物阴影证据：采集召唤物阴影，不修改原始材质 | 表现预览 / 人工 / AI | — | 图形设备 | 只读；不修改玩法资产；Logs/Phase02 |
| preview.summon | 召唤 表现预览：采集现有 summon 表现 PNG；仍需视觉核验 | 表现预览 / 人工 / AI | — | 图形设备 | 只读；不修改玩法资产；Logs/Phase02 |
| rebuild.perks | 重建 perks：重设七种 Perk 的权重、依赖、稀有度参数及数据库 | 维护与样例 / 人工 / AI | Apply | edit | 重设七种 Perk 的权重、依赖、稀有度参数及数据库；Logs/ProjectTools |
| migrate.enemy-variants | 迁移普通怪物 Variant：迁移怪物 Prefab 继承关系、组件和场景注册 | 维护与样例 / 人工 / AI | Apply | edit | 迁移怪物 Prefab 继承关系、组件和场景注册；Logs/ProjectTools |
| migrate.imp | 迁移 Imp Variant：导入 Imp 资源并更新网络怪物 Prefab 与场景注册 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入 Imp 资源并更新网络怪物 Prefab 与场景注册；Logs/ProjectTools |
| migrate.lust-sinner | 迁移 LustSinner Variant：导入 LustSinner 资源并更新网络怪物 Prefab 与场景注册 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入 LustSinner 资源并更新网络怪物 Prefab 与场景注册；Logs/ProjectTools |
| create.localization-entries | 创建内容本地化条目：绑定指定内容资产的名称与说明条目，不覆盖已有译文 | 制作 / 人工 / AI | AssetPath、Apply | edit | 绑定指定内容资产的名称与说明条目，不覆盖已有译文；Logs/ProjectTools |
| validate.localization | 校验本地化：校验全部语言、内容参数、引用与字体 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| export.localization-csv | 导出本地化 CSV：从插件表格导出 CSV，写入 docs/localization | 制作 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| setup.experience-legacy | M6 经验历史初始化：导入经验美术并覆盖经验曲线、XP 配置、HUD 和 Boot 场景 | 维护与样例 / 人工 / AI | Apply、Source | edit, 外部参考工程 | 导入经验美术并覆盖经验曲线、XP 配置、HUD 和 Boot 场景；Logs/ProjectTools |
| diagnostic.backend-steam | Editor 后端：Steam：设置本机 Editor 首选后端，仅影响下一次 Play Mode | 联机诊断 / 人工 / AI | — | edit, 已打开的 Editor | 只读；不修改玩法资产；Logs/ProjectTools |
| diagnostic.backend-kcp | Editor 后端：KCP：设置本机 Editor 首选后端；日常验收优先游戏内本地联机入口 | 联机诊断 / 人工 / AI | — | edit, 已打开的 Editor | 只读；不修改玩法资产；Logs/ProjectTools |
| diagnostic.steam | Steam 状态诊断：输出当前 Steam 与邀请状态，不发送邀请 | 联机诊断 / 人工 / AI | — | play, 已打开的 Editor | 只读；不修改玩法资产；Logs/ProjectTools |
| diagnostic.overlay | Steam 叠加层诊断：只打开叠加层；不能据此判定大厅邀请成功 | 联机诊断 / 人工 / AI | — | play, 已打开的 Editor | 只读；不修改玩法资产；Logs/ProjectTools |
| setup.boot | 初始化 Boot / Gameplay：历史初始化：创建缺失场景，可能迁移怪物及修复组件 | 维护与样例 / 人工 / AI | Apply | edit | 历史初始化：创建缺失场景，可能迁移怪物及修复组件；Logs/ProjectTools |
| setup.sandbox | 初始化网络 Sandbox：创建测试场景、玩家与怪物配置；不属于正式构建 | 维护与样例 / 人工 / AI | Apply | edit | 创建测试场景、玩家与怪物配置；不属于正式构建；Logs/ProjectTools |
| create.wave-timeline | 创建默认波次时间轴：创建缺失的波次时间轴，并在缺失时绑定波次规则 | 维护与样例 / 人工 / AI | Apply | edit | 创建缺失的波次时间轴，并在缺失时绑定波次规则；Logs/ProjectTools |
| open.waves | 波次时间轴：打开已提交的时间轴；缺失时明确失败 | 制作 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| validate.waves | 校验波次并导出预览：验证规则和注册怪物，输出波次 CSV 到 Logs/TimelineWaves | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| repair.player-prefab | 修复玩家运行时 Prefab：修改 NetworkPlayer.prefab 的战斗、输入、Build 和网络组件绑定 | 维护与样例 / 人工 / AI | Apply | edit | 修改 NetworkPlayer.prefab 的战斗、输入、Build 和网络组件绑定；Logs/ProjectTools |
| create.preparation-catalog | 创建缺失的准备房间配置：仅在缺失时创建 Assets/Resources/PreparationMenuCatalog.asset | 维护与样例 / 人工 / AI | Apply | edit | 仅在缺失时创建 Assets/Resources/PreparationMenuCatalog.asset；Logs/ProjectTools |
| diagnostic.enemy-handoff | 仇恨交接控制：在运行中的 Host 显式启用仇恨目标切换控制 | 联机诊断 / 人工 / AI | — | play-host, 已打开的 Editor | 只读；不修改玩法资产；Logs/ProjectTools |
| sample.nordic-validate-imports | 校验 Nordic 资源：检查导入的 Nordic 美术、GUID 依赖与着色器 | 维护与样例 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| sample.nordic-create | 创建 Nordic 样例场景：按已恢复的 Midgard 规则重建四屏乘四屏静态场景、固定碰撞边界和样板专用 Y 轴渲染器。 | 维护与样例 / 人工 / AI | Apply | edit | 重建 NordicStaticSample 场景布局；Logs/ProjectTools |
| sample.nordic-sorting | 更新 Nordic 排序：保留原规则布局，核验无旧排序编号并重新绑定样板专用 Y 轴渲染器；旧手工场景需先重建。 | 维护与样例 / 人工 / AI | Apply | edit | 保留布局，修改 Nordic 样例渲染排序；Logs/ProjectTools |
| sample.nordic-validate | 校验 Nordic 场景：校验四屏地图尺寸、固定边界、Nordic 视觉依赖和样板相机。 | 维护与样例 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| open.preparation | 准备房间配置：选中现有配置；不会创建默认资源 | 制作 / 人工 / AI | — | edit, 已打开的 Editor | 只读；不修改玩法资产；Logs/ProjectTools |
| open.localization | 本地化表格：选中现有配置；不会创建默认资源 | 制作 / 人工 / AI | — | edit, 已打开的 Editor | 只读；不修改玩法资产；Logs/ProjectTools |
| validate.all | 全部正式资源：依次校验 GAS、本地化、波次与怪物 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| validate.tools | 校验工具清单：检查重复 ID、工具绑定、文档元数据和构建场景 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| validate.enemies | 校验怪物 Variant：校验现有怪物继承与注册 | 校验 / 人工 / AI | — | edit | 只读；不修改玩法资产；Logs/ProjectTools |
| build.player | 构建验收包：按配置校验并构建已提交资源；不执行导入或修复 | 构建与验收 / 人工 / AI | Profile、Output、ScriptsOnly | edit | 只读；不修改玩法资产；配置中定义的 Builds 子目录 |
| preview.attack | 武器表现预览：选择 projectile、beam、circling、dash、melee 或 summon；大招使用 preview.ultimate | 表现预览 / 人工 / AI | Profile | 图形设备 | 只读；不修改玩法资产；Logs/Phase02 |
| test.beam | 光束攻击验收：光束武器的多进程攻击与命中。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.boot-process | 启动与战斗加载验收：Boot 服务、Gameplay 加载及玩家绑定。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Port、ProcessTimeoutSeconds | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.circling | 环绕攻击验收：环绕武器的多进程表现与命中。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.combat-menu | 战斗 ESC 菜单验收：菜单打开、输入阻隔、选卡恢复及退出；复用准备页测试。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Scenario、Width、Height、Port、Visible | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.dash | 冲刺攻击验收：冲刺武器、多进程执行及同步。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.enemy-handoff | 怪物仇恨交接验收：三进程权限交接、动作继承、异常恢复和高频压力；Profile 为 normal / impaired。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Profile、Duration、SkeletonPrefab、Port | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.enemy-simulation | 怪物模拟验收：普通怪模拟、权威写入及多进程同步。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Port、ProcessTimeoutSeconds | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.experience | 经验与升级验收：经验拾取、等级进度及多进程同步。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Simulation、CaptureFrames、VisibleWindows、Port | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.camera | 战斗相机验收：本地拥有角色绑定与相机跟随。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、CaptureFrames、VisibleWindows、ForceD3D11 | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.health-hud | 生命 HUD验收：生命显示、角色绑定及网络更新。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.imp | 小恶魔投射物验收：Imp 投射物、表现及命中。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Impaired、Graphics、Port | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.localization | 本地化与选项验收：Unity PlayMode 中的翻译、语言选项及卡片刷新，并校验正式表格。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Unity、BuildPlayers、Network | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.local-room | 本机 KCP 房间验收：真实首页建房与加入 UI；Profile 支持 party / solo / errors / admission / release。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Scenario、Width、Height、Port、Visible、Headless | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.melee | 近战攻击验收：近战武器的多进程攻击与命中。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.modifier-selection | 联网选卡验收：奖励队列、选项请求和装备目标选择。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Keyboard | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.nordic | Nordic 场景验收：独立样例的场景加载、渲染及静态资源。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、OutputDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.options | 游戏选项验收：音量、画面、语言设置与输入交互。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Artifacts | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.knockback | 普通怪击退验收：击退轨迹、剩余时间及网络继承。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、EnemyPrefab、Dedicated、ImpairedNetwork、CaptureFrames、ValidateHitFlash、ValidateDamageNumbers、VisibleWindows、ForceD3D11、ForceGfxDirect、IsolateTemporaryCache、PsoCacheSeed、Port | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.player-debug | 玩家状态面板验收：Host 全员与客户端自己状态的展示及只读采集。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Width、Height、Port、Headless | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.preparation-menu | 准备房间与开局验收：四席位、邀请适配、本机房间、ESC 和换局的组合流程；保留原有独立场景断言。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Profile、Width、Height、Port、Headless、MixedLanguages、Language、Visible | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.run-end | 全员倒地与换局验收：战败、回大厅、连续重开和队伍身份保留；复用准备页测试。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Scenario、Width、Height、Port、Visible、profile | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.runtime-boundary | 战斗运行边界验收：拥有端与服务端战斗执行边界。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.summon | 召唤攻击验收：召唤武器、召唤物及多进程同步。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.timeline-waves | 时间轴刷怪验收：波次时间轴调度与怪物生成。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Impaired、Port | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.ultimate | 大招验收：充能、执行、生效与表现同步。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.upgrade-selection | 升级选卡验收：经验跨级、奖励选择与 Build 更新。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、CaptureFrames、VisibleWindows、ForceD3D11、IsolateTemporaryCache、PsoCacheSeed、Port | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.waves | 波次验收：波次调度、计时及网络生成。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Simulation、SelectionBeforeRun、CaptureFrames、VisibleWindows、Port | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.wisp | 幽灵投射物验收：Wisp 投射物表现及构建场景探针。检查真实进程退出码与完成结果。 | 自动验收 / 人工 / AI | Executable、Dedicated、Port、LogDirectory | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.editor-tools | 编辑器工具验收：运行入口约束、维护权限、缺失资源、构建场景和只读本地化测试 | 自动验收 / 人工 / AI | Unity | edit | 只读测试，不生成正式资源；Logs/ProjectTools/Tests |
| validate.waves-and-enemies | 校验波次与怪物：依次校验波次规则和怪物；任一失败则整体失败 | 校验 / 人工 / AI | — | edit | 只读；输出诊断报告；Logs/TimelineWaves、Logs/ProjectTools |
| migrate.nordic-gameplay | 接入 Nordic 正式地图：正式 Gameplay 的地图、导航、Axeldor 与镜头适配。 | 维护与样例 / 人工 / AI | Apply | edit | 迁移 Gameplay 场景及 NetworkPlayer；保留玩法配置；Logs/ProjectTools |
| validate.nordic-gameplay | 校验 Nordic 正式地图：正式 Gameplay 的地图、导航、Axeldor 与镜头适配。 | 维护与样例 / 人工 / AI | — | edit | 只读校验正式场景；Logs/ProjectTools |
| test.nordic-gameplay | Nordic 正式玩法验收：正式地图、人物、镜头、冲刺与边界验收；FullSuite 追加多人、专服、六波次、敌人交接、升级和换局回归。 | 自动验收 / 人工 / AI | Executable、OutputDirectory、FullSuite、FullPerformance | — | 只读；不修改玩法资产；原场景日志目录及 Logs/ProjectTools |
| test.player-exit | Windows 退出验证：记录人工或 UI Automation 操作后的真实进程退出及 Windows 崩溃事件 | 构建与验收 / 人工 / AI | Executable、Mode、Scenario、Graphics、Iterations、InteractionTimeoutSeconds、ArtifactDirectory、AllowValidationBuild | external, 图形设备 | 启动指定包；操作员执行真实 UI，超时清理判失败；不修改玩法资产；Logs/PlayerExit/<时间>/result.json |

## 构建配置

正式流程包含 Boot、MainMenu、Gameplay；专项场景保持独立。所有配置都使用统一构建服务。

| Profile | Development | 测试程序集 | 场景 | 宏 | 输出 |
| --- | --- | --- | --- | --- | --- |
| menu-development | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_MENU_VALIDATION | Builds/MenuDevelopment/MonsterSupergroup.exe |
| menu-release | — | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_MENU_VALIDATION | Builds/MenuRelease/MonsterSupergroup.exe |
| kcp-development | True | — | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD | Builds/KcpDevelopment/MonsterSupergroupKcp.exe |
| boot-process | True | — | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | — | Builds/BootGameplayValidation/MonsterSupergroupBootGameplayValidation.exe |
| sandbox | True | — | Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity | — | Builds/EnemySimulationValidation/MonsterSupergroupEnemySimulationValidation.exe |
| nordic | True | — | Assets/_Project/Scenes/NordicStaticSample.unity | — | Builds/NordicStaticSample/NordicStaticSample.exe |
| enemy-variants | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/EnemyVariants/EnemyVariants.exe |
| imp | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Imp/Imp.exe |
| lust-sinner | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/LustSinner/LustSinner.exe |
| enemy-hit-flash | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/EnemyHitFlash/EnemyHitFlash.exe |
| camera | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/GameplayCameraValidation/GameplayCameraValidation.exe |
| experience | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/M6Experience/M6Experience.exe |
| waves | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/M5Waves/M5Waves.exe |
| health-hud | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/HealthHUDValidation/HealthHUDValidation.exe |
| modifier-selection | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/ModifierSelectionValidation/ModifierSelectionValidation.exe |
| knockback | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/M3Knockback/M3Knockback.exe |
| timeline-waves | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/TimelineWaves/TimelineWaves.exe |
| wisp | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/WispValidation/MonsterSupergroup.exe |
| options | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION、MONSTER_OPTIONS_VALIDATION | Builds/OptionsValidation/MonsterSupergroup.exe |
| enemy-handoff-development | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION、MONSTER_ENEMY_HANDOFF_VALIDATION | Builds/EnemyHandoff/EnemyHandoff.exe |
| player-debug-development | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/PlayerDebugDevelopment/PlayerDebug.exe |
| rewired-development | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/RewiredAbilities/Development/RewiredAbilities.exe |
| enemy-handoff-release | — | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION、MONSTER_ENEMY_HANDOFF_VALIDATION | Builds/EnemyHandoffRelease/EnemyHandoff.exe |
| player-debug-release | — | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/PlayerDebugRelease/PlayerDebug.exe |
| rewired-release | — | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/RewiredAbilities/Release/RewiredAbilities.exe |
| beam | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Phase02/BeamValidation.exe |
| circling | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Phase02/CirclingValidation.exe |
| dash | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Phase02/DashValidation.exe |
| melee | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Phase02/MeleeValidation.exe |
| summon | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Phase02/SummonValidation.exe |
| ultimate | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Phase02/UltimateValidation.exe |
| runtime-boundary | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/Phase01/RuntimeBoundaryValidation.exe |
| nordic-gameplay | True | True | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | MONSTER_KCP_DEVELOPMENT_BUILD、MONSTER_MENU_VALIDATION | Builds/NordicGameplay/MonsterSupergroup.exe |
| player-development | True | — | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | — | Builds/PlayerDevelopment/MonsterSupergroup.exe |
| player-release | — | — | Assets/_Project/Scenes/Boot.unity、Assets/_Project/Scenes/MainMenu.unity、Assets/_Project/Scenes/Gameplay.unity | — | Builds/PlayerRelease/MonsterSupergroup.exe |

## 59 个旧菜单的去向

旧菜单已移除；兼容期为本轮工具整理后的一个交付版本。

| 旧菜单 | 旧类 / 方法 | 新 ID / 配置 | 处置 | 来源 |
| --- | --- | --- | --- | --- |
| Tools/MonsterSupergroup/Gameplay/Rebuild GAS Vertical Slice | MonsterSupergroup.Gameplay.Editor.GasVerticalSliceSceneBuilder.RebuildFromMenu | sample.gas-rebuild | 归维护；显式 Apply | Assets/_Project/Gameplay/Editor/GasVerticalSliceSceneBuilder.cs |
| Tools/MonsterSupergroup/GAS/Validate All | MonsterSupergroup.GAS.Editor.GasAssetValidator.ValidateAllMenu | validate.gas | 保留并统一入口 | Assets/_Project/GAS/Editor/GasAssetValidator.cs |
| Tools/MonsterSupergroup/GAS/Rebuild Registry | MonsterSupergroup.GAS.Editor.ModifierRegistryGenerator.Rebuild | generate.gas-registry | 归维护；显式 Apply | Assets/_Project/GAS/Editor/ModifierRegistryGenerator.cs |
| Tools/HellMaiden Migration/Import Dante Beam Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteBeamNativeGasMigration.Import | import.beam | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteBeamNativeGasMigration.cs |
| Tools/HellMaiden Migration/Validate Dante Beam Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteBeamNativeGasMigration.ValidateImportedAssets | validate.beam | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteBeamNativeGasMigration.cs |
| Tools/HellMaiden Migration/Capture Dante Beam Presentation Preview | MonsterSupergroup.HellMaidenMigration.Editor.DanteBeamPresentationPreview.Capture | preview.beam | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteBeamPresentationPreview.cs |
| Tools/HellMaiden Migration/Import Dante Circling Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteCirclingNativeGasMigration.Import | import.circling | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingNativeGasMigration.cs |
| Tools/HellMaiden Migration/Validate Dante Circling Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteCirclingNativeGasMigration.ValidateImportedAssets | validate.circling | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingNativeGasMigration.cs |
| Tools/HellMaiden Migration/Diagnose Dante Circling Presentation Dependencies | MonsterSupergroup.HellMaidenMigration.Editor.DanteCirclingNativeGasMigration.DiagnosePresentationDependencies | diagnostic.circling-dependencies | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingNativeGasMigration.cs |
| Tools/HellMaiden Migration/Capture Dante Circling Presentation Preview | MonsterSupergroup.HellMaidenMigration.Editor.DanteCirclingPresentationPreview.Capture | preview.circling | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingPresentationPreview.cs |
| Tools/HellMaiden Migration/Import Dante Dash Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteDashNativeGasMigration.Import | import.dash | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs |
| Tools/HellMaiden Migration/Restore Source Player Dash Movement Configuration | MonsterSupergroup.HellMaidenMigration.Editor.DanteDashNativeGasMigration.RestoreDashMovementConfiguration | maintenance.restore-dash | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs |
| Tools/HellMaiden Migration/Validate Dante Dash Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteDashNativeGasMigration.ValidateImportedAssets | validate.dash | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs |
| Tools/HellMaiden Migration/Diagnose Dante Dash Presentation Dependencies | MonsterSupergroup.HellMaidenMigration.Editor.DanteDashNativeGasMigration.DiagnosePresentationDependencies | diagnostic.dash-dependencies | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs |
| Tools/HellMaiden Migration/Capture Dante Dash Presentation Preview | MonsterSupergroup.HellMaidenMigration.Editor.DanteDashPresentationPreview.Capture | preview.dash | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteDashPresentationPreview.cs |
| Tools/HellMaiden Migration/Import Dante Melee Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteMeleeNativeGasMigration.Import | import.melee | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteMeleeNativeGasMigration.cs |
| Tools/HellMaiden Migration/Validate Dante Melee Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteMeleeNativeGasMigration.ValidateImportedAssets | validate.melee | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteMeleeNativeGasMigration.cs |
| Tools/HellMaiden Migration/Capture Dante Melee Presentation Preview | MonsterSupergroup.HellMaidenMigration.Editor.DanteMeleePresentationPreview.Capture | preview.melee | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteMeleePresentationPreview.cs |
| Tools/HellMaiden Migration/Rebuild Dante Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteNativeGasMigration.Rebuild | rebuild.dante | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteNativeGasMigration.cs |
| Tools/HellMaiden Migration/Reserialize Canonical Equipment Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteNativeGasMigration.ReserializeCanonicalEquipmentAssets | maintenance.reserialize-equipment | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteNativeGasMigration.cs |
| Tools/HellMaiden Migration/Import Dante Projectile Presentation | MonsterSupergroup.HellMaidenMigration.Editor.DanteProjectilePresentationMigration.Import | import.projectile | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationMigration.cs |
| Tools/HellMaiden Migration/Validate Dante Projectile Presentation | MonsterSupergroup.HellMaidenMigration.Editor.DanteProjectilePresentationMigration.ValidateImportedAssets | validate.projectile | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationMigration.cs |
| Tools/HellMaiden Migration/Capture Dante Projectile Presentation Preview | MonsterSupergroup.HellMaidenMigration.Editor.DanteProjectilePresentationPreview.Capture | preview.projectile | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationPreview.cs |
| Tools/HellMaiden Migration/Import Dante Ultimate Attack Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteUltimateAssetMigration.Import | import.ultimate | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/DanteUltimateAssetMigration.cs |
| Tools/HellMaiden Migration/Validate Dante Ultimate Attack Assets | MonsterSupergroup.HellMaidenMigration.Editor.DanteUltimateAssetMigration.ValidateImportedAssets | validate.ultimate | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteUltimateAssetMigration.cs |
| Tools/HellMaiden Migration/Capture Dante Ultimate Presentation Preview | MonsterSupergroup.HellMaidenMigration.Editor.DanteUltimatePresentationPreview.Capture | preview.ultimate | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/DanteUltimatePresentationPreview.cs |
| Tools/HellMaiden Migration/Import Ovid Summon Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.OvidSummonNativeGasMigration.Import | import.summon | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/OvidSummonNativeGasMigration.cs |
| Tools/HellMaiden Migration/Validate Ovid Summon Native GAS Assets | MonsterSupergroup.HellMaidenMigration.Editor.OvidSummonNativeGasMigration.ValidateImportedAssets | validate.summon | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/OvidSummonNativeGasMigration.cs |
| Tools/HellMaiden Migration/Capture Ovid Summon Shadow Evidence | MonsterSupergroup.HellMaidenMigration.Editor.OvidSummonPresentationPreview.CaptureShadowEvidence | preview.summon-shadow | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/OvidSummonPresentationPreview.cs |
| Tools/HellMaiden Migration/Capture Ovid Summon Presentation Preview | MonsterSupergroup.HellMaidenMigration.Editor.OvidSummonPresentationPreview.Capture | preview.summon | 保留并统一入口 | Assets/_Project/Migration/HellMaiden/Editor/OvidSummonPresentationPreview.cs |
| Tools/HellMaiden Migration/Rebuild Pure Weapon Perks | MonsterSupergroup.HellMaidenMigration.Editor.PureWeaponPerkMigration.Rebuild | rebuild.perks | 归维护；显式 Apply | Assets/_Project/Migration/HellMaiden/Editor/PureWeaponPerkMigration.cs |
| Monster Supergroup/Network Combat/Build Boot Gameplay Process Validation Player | MonsterSupergroup.NetworkCombat.Editor.BootGameplayProcessValidationBuildUtility.BuildWindowsPlayer | build.player -Profile boot-process | 构建流程合并；旧方法转发一版 | Assets/_Project/NetworkCombat/Editor/BootGameplayProcessValidationBuildUtility.cs |
| Monster Supergroup/Network Combat/Migrate Enemy Prefab Variants | MonsterSupergroup.NetworkCombat.Editor.EnemyPrefabVariantMigration.Migrate | migrate.enemy-variants | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.cs |
| Monster Supergroup/Network Combat/Migrate Imp Variant | MonsterSupergroup.NetworkCombat.Editor.EnemyPrefabVariantMigration.MigrateImp | migrate.imp | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs |
| Monster Supergroup/Network Combat/Migrate LustSinner Variant | MonsterSupergroup.NetworkCombat.Editor.EnemyPrefabVariantMigration.MigrateLustSinner | migrate.lust-sinner | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.LustSinner.cs |
| Monster Supergroup/Network Combat/Migrate Enemy Simulation Prefabs | MonsterSupergroup.NetworkCombat.Editor.EnemySimulationPrefabMigrator.Migrate | migrate.enemy-variants | 删除重复菜单；旧方法兼容转发 | Assets/_Project/NetworkCombat/Editor/EnemySimulationPrefabMigrator.cs |
| MonsterSupergroup/Localization/Create entries for selected content | MonsterSupergroup.NetworkCombat.Editor.GameLocalizationAssets.CreateSelectedEntries | create.localization-entries | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs |
| MonsterSupergroup/Localization/Validate tables and content | MonsterSupergroup.NetworkCombat.Editor.GameLocalizationAssets.Validate | validate.localization | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs |
| MonsterSupergroup/Localization/Export translation CSV | MonsterSupergroup.NetworkCombat.Editor.GameLocalizationAssets.ExportCsv | export.localization-csv | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs |
| Tools/Network Combat/Apply M6 Experience Setup | MonsterSupergroup.NetworkCombat.Editor.GameplayExperienceSetup.Apply | setup.experience-legacy | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/GameplayExperienceSetup.cs |
| Monster Supergroup/Network Combat/Build KCP Development Player | MonsterSupergroup.NetworkCombat.Editor.KcpDevelopmentBuildUtility.BuildWindowsPlayer | build.player -Profile kcp-development | 构建流程合并；旧方法转发一版 | Assets/_Project/NetworkCombat/Editor/KcpDevelopmentBuildUtility.cs |
| Monster Supergroup/Network Combat/Editor Backend/Steam | MonsterSupergroup.NetworkCombat.Editor.NetworkBackendEditorSettings.SelectSteam | diagnostic.backend-steam | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/NetworkBackendEditorSettings.cs |
| Monster Supergroup/Network Combat/Editor Backend/KCP Local | MonsterSupergroup.NetworkCombat.Editor.NetworkBackendEditorSettings.SelectKcp | diagnostic.backend-kcp | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/NetworkBackendEditorSettings.cs |
| Monster Supergroup/Network Combat/Steam/Log Invite Diagnostics | MonsterSupergroup.NetworkCombat.Editor.NetworkBackendEditorSettings.LogInviteDiagnostics | diagnostic.steam | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/NetworkBackendEditorSettings.cs |
| Monster Supergroup/Network Combat/Steam/Open Lobby Invite Dialog | MonsterSupergroup.NetworkCombat.Editor.NetworkBackendEditorSettings.OpenInviteDialog | diagnostic.overlay | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/NetworkBackendEditorSettings.cs |
| Monster Supergroup/Network Combat/Configure Boot Gameplay Loop | MonsterSupergroup.NetworkCombat.Editor.NetworkCombatSetupUtility.ConfigureBootGameplayLoop | setup.boot | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/NetworkCombatSetupUtility.cs |
| Monster Supergroup/Network Combat/Build Validation Sandbox | MonsterSupergroup.NetworkCombat.Editor.NetworkCombatSetupUtility.BuildSandbox | setup.sandbox | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/NetworkCombatSetupUtility.cs |
| Monster Supergroup/Network Combat/Build Enemy Simulation Process Validation Player | MonsterSupergroup.NetworkCombat.Editor.NetworkEnemyProcessValidationBuildUtility.BuildWindowsPlayer | build.player -Profile sandbox | 构建流程合并；旧方法转发一版 | Assets/_Project/NetworkCombat/Editor/NetworkEnemyProcessValidationBuildUtility.cs |
| Monster Supergroup/Network Combat/Waves/Create Default Timeline | MonsterSupergroup.NetworkCombat.Editor.NetworkWaveTimelineEditorUtility.EnsureDefault | create.wave-timeline | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs |
| Monster Supergroup/Network Combat/Waves/Open Timeline | MonsterSupergroup.NetworkCombat.Editor.NetworkWaveTimelineEditorUtility.Open | open.waves | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs |
| Monster Supergroup/Network Combat/Waves/Validate and Export Preview | MonsterSupergroup.NetworkCombat.Editor.NetworkWaveTimelineEditorUtility.ValidateConfigured | validate.waves | 保留并统一入口 | Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs |
| Tools/Monster Supergroup/Repair Network Player Runtime Combat Prefab | MonsterSupergroup.NetworkCombat.Editor.PlayerRuntimeCombatPrefabMigrator.Run | repair.player-prefab | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/PlayerRuntimeCombatPrefabMigrator.cs |
| MonsterSupergroup/Menu/Create preparation catalog | MonsterSupergroup.NetworkCombat.Editor.PreparationMenuAssets.CreateCatalog | create.preparation-catalog | 归维护；显式 Apply | Assets/_Project/NetworkCombat/Editor/PreparationMenuAssets.cs |
| Tools/Network Combat/Show Handoff Test Controls | MonsterSupergroup.NetworkCombat.EnemyHandoffTestControls.ShowInEditor | diagnostic.enemy-handoff | 保留并统一入口 | Assets/_Project/NetworkCombat/Mirror/EnemyHandoffTestControls.cs |
| Tools/Nordic/1. Validate Imported Resources | MonsterSupergroup.NordicSample.Editor.NordicStaticSampleBuilder.ValidateImports | sample.nordic-validate-imports | 保留并统一入口 | Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs |
| Tools/Nordic/2. Build Static Sample | MonsterSupergroup.NordicSample.Editor.NordicStaticSampleBuilder.BuildSample | sample.nordic-create | 归维护；显式 Apply | Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs |
| Tools/Nordic/5. Refresh Sample Sorting (Keep Layout) | MonsterSupergroup.NordicSample.Editor.NordicStaticSampleBuilder.RefreshSampleSorting | sample.nordic-sorting | 归维护；显式 Apply | Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs |
| Tools/Nordic/3. Validate Static Sample | MonsterSupergroup.NordicSample.Editor.NordicStaticSampleBuilder.ValidateScene | sample.nordic-validate | 保留并统一入口 | Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs |
| Tools/Nordic/4. Build Standalone Preview | MonsterSupergroup.NordicSample.Editor.NordicStaticSampleBuilder.BuildPlayer | build.player -Profile nordic | 构建流程合并；旧方法转发一版 | Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs |

## Editor 集成与自动钩子

这些文件没有独立按钮也不等于无用。属性绘制器、Inspector、构建前检查和 Steam AppID 后处理继续自动生效。

| 文件 | 主要类型 | 自动集成 | 处置 |
| --- | --- | --- | --- |
| Assets/_Project/Gameplay/Editor/GasVerticalSliceSceneBuilder.cs | GasVerticalSliceSceneBuilder | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/GAS/Editor/GasAssetValidator.cs | GasValidationIssue、GasAssetValidator | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/GAS/Editor/GasBuildPreprocessor.cs | GasBuildPreprocessor | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/GAS/Editor/ModifierDataDrawers.cs | ModifierDataDrawer、EquipmentDataModifierDrawer、PerkDataModifierDrawer | CustomPropertyDrawer、CustomPropertyDrawer | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/GAS/Editor/ModifierRegistryGenerator.cs | ModifierRegistryGenerator、GeneratedModifierRegistry | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/GAS/Editor/ModifierSelectionService.cs | ModifierSelectionService | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/GAS/Editor/ModifierTypeCatalog.cs | ModifierDescriptor、ModifierTypeCatalog | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteAttackPresentationPreview.cs | DanteAttackPresentationPreview | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteBeamNativeGasMigration.cs | DanteBeamNativeGasMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteBeamPresentationPreview.cs | DanteBeamPresentationPreview | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingNativeGasMigration.cs | DanteCirclingNativeGasMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingPresentationPreview.cs | DanteCirclingPresentationPreview | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs | DanteDashNativeGasMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteDashPresentationPreview.cs | DanteDashPresentationPreview | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteMeleeNativeGasMigration.cs | DanteMeleeNativeGasMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteMeleePresentationPreview.cs | DanteMeleePresentationPreview | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteNativeGasMigration.cs | DanteNativeGasMigration、EquipmentMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationMigration.cs | DanteProjectilePresentationMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationPreview.cs | DanteProjectilePresentationPreview | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteUltimateAssetMigration.cs | DanteUltimateAssetMigration、GUID | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteUltimatePresentationPreview.cs | DanteUltimatePresentationPreview | InitializeOnLoad | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/LegacyEquipmentModifierConverter.cs | LegacyEquipmentModifierConverter | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/LegacyPerkModifierConverter.cs | LegacyPerkModifierConverter | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/OvidSummonNativeGasMigration.cs | OvidSummonNativeGasMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/OvidSummonPresentationPreview.cs | OvidSummonPresentationPreview | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/Migration/HellMaiden/Editor/PureWeaponPerkMigration.cs | PureWeaponPerkMigration、PerkMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/BootGameplayProcessValidationBuildUtility.cs | BootGameplayProcessValidationBuildUtility | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.cs | EnemyPrefabVariantMigration、ReferenceRecord、ReferenceReport、Baseline、Baselines | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs | EnemyPrefabVariantMigration | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.LustSinner.cs | EnemyPrefabVariantMigration、LustReport | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Validation.cs | EnemyPrefabVariantMigration、ContentComparison | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/EnemySimulationPrefabMigrator.cs | EnemySimulationPrefabMigrator | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs | GameLocalizationAssets、Entry、LevelOverride、Binding、Manifest | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/GameplayExperienceSetup.cs | GameplayExperienceSetup | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/KcpDevelopmentBuildUtility.cs | KcpDevelopmentBuildUtility | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/MenuLocalizationAssets.cs | MenuLocalizationAssets | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/NetworkBackendEditorSettings.cs | NetworkBackendEditorSettings | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/NetworkCombatSetupUtility.cs | NetworkCombatSetupUtility | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/NetworkEnemyProcessValidationBuildUtility.cs | NetworkEnemyProcessValidationBuildUtility | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs | NetworkWaveTimelineEditorUtility、GameplayWaveRulesInspector | CustomEditor | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/PlayerBuildRuntimeEditor.cs | PlayerBuildRuntimeEditor | CustomEditor | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/PlayerRuntimeCombatPrefabMigrator.cs | PlayerRuntimeCombatPrefabMigrator | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/PreparationMenuAssets.cs | PreparationMenuAssets | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NetworkCombat/Editor/SteamAppIdBuildPostprocessor.cs | SteamAppIdBuildPostprocessor | PostProcessBuild | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | NordicStaticSampleBuilder、Manifest、Prop、Lamp、SpriteInfo、ImportResult | — | 保留；菜单与批处理按映射统一，Inspector/自动钩子保持 |

## 额外批处理入口 / 无参公共方法

此表是旧代码入口清单，不表示每个辅助方法都应直接由 AI 调用；日常调用以稳定 ID 为准。需要写入的维护方法使用统一入口的 `-Apply`。

| 来源 | 方法 | 处置 |
| --- | --- | --- |
| Assets/_Project/EditorTools/Editor/ProjectToolCatalog.cs | Load | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/EditorTools/Editor/ProjectToolCatalog.cs | Validate | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/EditorTools/Editor/ProjectToolRunner.cs | Batch | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/EditorTools/Editor/ProjectToolRunner.cs | HellMaiden | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Gameplay/Editor/GasVerticalSliceSceneBuilder.cs | RebuildAndValidate | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Gameplay/Editor/GasVerticalSliceSceneBuilder.cs | ValidateGeneratedContent | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/GAS/Editor/GasAssetValidator.cs | ValidateAllMenu | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/GAS/Editor/ModifierRegistryGenerator.cs | Rebuild | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/GAS/Editor/ModifierRegistryGenerator.cs | IsCurrent | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/GAS/Editor/ModifierRegistryGenerator.cs | GenerateSource | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/GAS/Editor/ModifierRegistryGenerator.cs | Create | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/GAS/Editor/ModifierTypeCatalog.cs | Refresh | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteBeamNativeGasMigration.cs | Import | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteBeamNativeGasMigration.cs | ValidateImportedAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteBeamPresentationPreview.cs | Capture | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingNativeGasMigration.cs | Import | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingNativeGasMigration.cs | ValidateImportedAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingNativeGasMigration.cs | DiagnosePresentationDependencies | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteCirclingPresentationPreview.cs | Capture | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs | Import | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs | RestoreDashMovementConfiguration | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs | ValidateImportedAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteDashNativeGasMigration.cs | DiagnosePresentationDependencies | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteDashPresentationPreview.cs | Capture | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteMeleeNativeGasMigration.cs | Import | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteMeleeNativeGasMigration.cs | ValidateImportedAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteMeleePresentationPreview.cs | Capture | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteNativeGasMigration.cs | Rebuild | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteNativeGasMigration.cs | ReserializeCanonicalEquipmentAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationMigration.cs | Import | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationMigration.cs | ValidateImportedAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationPreview.cs | ImportAndCapture | 保留显式维护/历史验证组合；可能修改资源或需要历史基线，不作为只读预览和普通构建调用 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteProjectilePresentationPreview.cs | Capture | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteUltimateAssetMigration.cs | Import | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteUltimateAssetMigration.cs | ValidateImportedAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/DanteUltimatePresentationPreview.cs | Capture | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/OvidSummonNativeGasMigration.cs | Import | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/OvidSummonNativeGasMigration.cs | ValidateImportedAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/OvidSummonPresentationPreview.cs | CaptureShadowEvidence | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/OvidSummonPresentationPreview.cs | Capture | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/Migration/HellMaiden/Editor/PureWeaponPerkMigration.cs | Rebuild | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/BootGameplayProcessValidationBuildUtility.cs | BuildWindowsPlayer | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/NetworkCombat/Editor/BootGameplayProcessValidationBuildUtility.cs | BuildWindowsPlayerBatch | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.cs | Migrate | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.cs | Validate | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs | MigrateImp | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs | ValidateImp | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs | BuildImpValidation | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs | VerifyImpMigrationRepeat | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.LustSinner.cs | MigrateLustSinner | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.LustSinner.cs | ValidateLustSinner | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.LustSinner.cs | BuildLustSinnerValidation | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Validation.cs | AuditAndBuild | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Validation.cs | CompareArchivedSkeleton | 保留显式维护/历史验证组合；可能修改资源或需要历史基线，不作为只读预览和普通构建调用 |
| Assets/_Project/NetworkCombat/Editor/EnemySimulationPrefabMigrator.cs | Migrate | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/EnemySimulationPrefabMigrator.cs | MigrateBatch | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs | Import | 删除：I2 一次性迁移已完成；无安全的日常等价操作，不再调用 |
| Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs | CreateSelectedEntries | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs | Validate | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/GameLocalizationAssets.cs | ExportCsv | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/GameplayExperienceSetup.cs | Apply | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/GameplayExperienceSetup.cs | EnsurePrefabIds | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/GameplayExperienceSetup.cs | FinalizePresentationAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/KcpDevelopmentBuildUtility.cs | BuildWindowsPlayer | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/NetworkCombat/Editor/KcpDevelopmentBuildUtility.cs | BuildWindowsPlayerBatch | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/NetworkCombat/Editor/NetworkCombatSetupUtility.cs | ConfigureBootGameplayLoop | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/NetworkCombatSetupUtility.cs | BuildBootGameplayAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/NetworkCombatSetupUtility.cs | BuildSandbox | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/NetworkCombatSetupUtility.cs | BuildSandboxAssets | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/NetworkEnemyProcessValidationBuildUtility.cs | BuildWindowsPlayer | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/NetworkCombat/Editor/NetworkEnemyProcessValidationBuildUtility.cs | BuildWindowsPlayerBatch | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs | EnsureDefault | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs | Open | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs | ValidateConfigured | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/NetworkWaveTimelineEditorUtility.cs | VerifyRepeat | 保留显式维护/历史验证组合；可能修改资源或需要历史基线，不作为只读预览和普通构建调用 |
| Assets/_Project/NetworkCombat/Editor/PlayerRuntimeCombatPrefabMigrator.cs | Run | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/PlayerRuntimeCombatPrefabMigrator.cs | RunBatch | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NetworkCombat/Editor/PreparationMenuAssets.cs | CreateCatalog | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | ValidateImports | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | SmokeCheck | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | BuildSample | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | RefreshSampleSorting | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | RefreshSortingAndBuildPlayer | 保留显式维护/历史验证组合；可能修改资源或需要历史基线，不作为只读预览和普通构建调用 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | ValidateScene | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | BuildPlayer | 保留领域入口；旧构建转发统一服务；以工具清单标注为日常调用范围 |
| Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs | BuildAndValidatePlayer | 保留显式维护/历史验证组合；可能修改资源或需要历史基线，不作为只读预览和普通构建调用 |
| Assets/_Project/Tests/PlayMode/Gameplay/EnemyHandoffValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/EnemyHitFlashValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/EnemyHitFlashValidationBuild.cs | BuildScriptsOnly | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/GameOptionsValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/GameOptionsValidationBuild.cs | BuildScriptsOnly | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/GameplayCameraValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/GameplayExperienceValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/GameplayWaveValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/HealthHUDValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/ModifierSelectionValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/OrdinaryKnockbackValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/PlayerDebugValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/PreparationMenuValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/RewiredAbilityValidationBuild.cs | Development | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/RewiredAbilityValidationBuild.cs | Release | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/TimelineWaveValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |
| Assets/_Project/Tests/PlayMode/Gameplay/WispValidationBuild.cs | Build | 保留原签名并薄转发统一构建服务；兼容一版；输出替代 Profile 提示 |

## 外部脚本

28 个旧 PowerShell 入口保留参数并转发至统一执行器；实际场景断言位于 `Tools/Scenarios`。进程公共能力在 `Tools/ProjectTools.psm1`。

| 旧脚本 | 处置 |
| --- | --- |
| Tools/Export-LocalizationMigration.py | 删除：I2 一次性提取已完成，保留历史记录 |
| Tools/Import-NordicStaticSample.py | 保留：Nordic 显式来源导入；需要 --source，属于维护 |
| Tools/Run-BeamProcessValidation.ps1 | 兼容转发 → test.beam |
| Tools/Run-BootGameplayProcessValidation.ps1 | 兼容转发 → test.boot-process |
| Tools/Run-CirclingProcessValidation.ps1 | 兼容转发 → test.circling |
| Tools/Run-CombatMenuValidation.ps1 | 兼容转发 → test.combat-menu |
| Tools/Run-DashProcessValidation.ps1 | 兼容转发 → test.dash |
| Tools/Run-EnemyHandoffValidation.ps1 | 兼容转发 → test.enemy-handoff |
| Tools/Run-EnemySimulationProcessValidation.ps1 | 兼容转发 → test.enemy-simulation |
| Tools/Run-ExperienceProcessValidation.ps1 | 兼容转发 → test.experience |
| Tools/Run-GameplayCameraProcessValidation.ps1 | 兼容转发 → test.camera |
| Tools/Run-HealthHUDProcessValidation.ps1 | 兼容转发 → test.health-hud |
| Tools/Run-ImpProjectileValidation.ps1 | 兼容转发 → test.imp |
| Tools/Run-LocalizationValidation.ps1 | 兼容转发 → test.localization |
| Tools/Run-LocalRoomValidation.ps1 | 兼容转发 → test.local-room |
| Tools/Run-MeleeProcessValidation.ps1 | 兼容转发 → test.melee |
| Tools/Run-ModifierSelectionProcessValidation.ps1 | 兼容转发 → test.modifier-selection |
| Tools/Run-NordicStaticSampleValidation.ps1 | 兼容转发 → test.nordic |
| Tools/Run-OptionsValidation.ps1 | 兼容转发 → test.options |
| Tools/Run-OrdinaryKnockbackProcessValidation.ps1 | 兼容转发 → test.knockback |
| Tools/Run-PlayerDebugProcessValidation.ps1 | 兼容转发 → test.player-debug |
| Tools/Run-PreparationMenuValidation.ps1 | 兼容转发 → test.preparation-menu |
| Tools/Run-RunEndValidation.ps1 | 兼容转发 → test.run-end |
| Tools/Run-RuntimeBoundaryProcessValidation.ps1 | 兼容转发 → test.runtime-boundary |
| Tools/Run-SummonProcessValidation.ps1 | 兼容转发 → test.summon |
| Tools/Run-TimelineWaveValidation.ps1 | 兼容转发 → test.timeline-waves |
| Tools/Run-UltimateProcessValidation.ps1 | 兼容转发 → test.ultimate |
| Tools/Run-UpgradeSelectionProcessValidation.ps1 | 兼容转发 → test.upgrade-selection |
| Tools/Run-WaveProcessValidation.ps1 | 兼容转发 → test.waves |
| Tools/Run-WispProcessValidation.ps1 | 兼容转发 → test.wisp |
