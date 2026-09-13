# 项目编辑器工具使用指南

项目菜单统一为 **MonsterSupergroup**。日常入口集中在“制作、校验、构建与验收、联机诊断”；历史导入、覆盖默认配置、初始化场景和样例工具进入“维护与样例”。

工具中心默认停靠在场景视图旁，支持名称、用途和稳定 ID 搜索。选择一项后可查看运行条件、参数、资产影响、输出位置和最近结果。维护项目默认隐藏；执行前会列出影响范围并确认。工具中心与命令行读取同一份 [catalog.json](catalog.json)。

完整去向见 [工具清单](inventory.md)，包括全部 59 个旧菜单、额外批处理方法、Editor Inspector 和自动钩子；[原始映射](legacy-inventory.json)保留整理依据。调用频率没有采集数据，不能把“没有代码调用”当成“没有用途”。

本次整理的实际编译、构建、Editor 操作、联机回归结果和已知退出崩溃见 [验收记录](acceptance-2026-09-13.md)。

## 按任务使用

| 要做的事 | 人工入口 | AI / 自动化 |
| --- | --- | --- |
| 修改波次 | 制作 → 波次时间轴 | 修改明确资产路径后运行 `validate.waves` |
| 修改准备房间角色和初始武器范围 | 制作 → 准备房间配置 | 编辑 `Assets/Resources/PreparationMenuCatalog.asset`，然后 `validate.all` |
| 修改翻译 | 制作 → 本地化表格，使用 Unity Localization 的表格编辑器 | `validate.localization`、`export.localization-csv` |
| 为新内容绑定翻译 | 工具中心 → 创建内容本地化条目；选中目标资产 | `create.localization-entries -AssetPath ... -Apply` |
| 新增 GAS Modifier 类型 | 工具中心 → 生成 GAS 注册表 | `generate.gas-registry -Apply`，然后 `validate.gas` |
| 本机多人验收 | Development 包中的“创建本地主机 / 加入本地主机” | `build.player`，然后 `test.local-room` |
| 高频仇恨交接 | Play Mode Host → 联机诊断 → 仇恨交接控制 | `test.enemy-handoff -Profile normal` 或 `impaired` |
| 查看武器表现 | 工具中心 → 表现预览 | `preview.attack -Profile beam` 等，要求图形设备 |
| 查看大招表现 | 在空场景进入 Play Mode 后运行 `preview.ultimate` | 单独调用 `preview.ultimate`；执行器会等待 Play Mode 采集完成 |
| 重新导入参考资源 | 维护与样例中选择明确的内容导入 | 提供 `-Source` 与 `-Apply` |
| 修复 Prefab 绑定 | 先校验，再使用相应维护工具并确认 | `repair.player-prefab -Apply` 或对应怪物迁移 ID |
| 制作 Nordic / GAS 样例 | 维护与样例 | 独立样例工具与 `build.player -Profile nordic` |

“AI 可调用”意味着参数和结果明确，不代表可以省略视觉核验。截图产出后仍需检查画面。Steam 状态、叠加层和运行中 Host 交接控制依赖当前 Editor 会话，批处理不会新建一场游戏冒充诊断成功。

## 命令行

在项目根目录运行。以下例子使用 PowerShell；命令 ID 不随中文菜单改名。

```powershell
./Tools/Invoke-ProjectTool.ps1 -List
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Help
./Tools/Invoke-ProjectTool.ps1 -ToolId validate.localization -Unity '<Unity目录>/Editor/Unity.exe'
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile menu-development
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile menu-release
./Tools/Invoke-ProjectTool.ps1 -ToolId test.local-room -Profile party -Port 7777
./Tools/Invoke-ProjectTool.ps1 -ToolId test.preparation-menu -Profile local-party -Port 7778 -Headless -MixedLanguages
./Tools/Invoke-ProjectTool.ps1 -ToolId test.enemy-handoff -Profile impaired -Duration 120
./Tools/Invoke-ProjectTool.ps1 -ToolId preview.attack -Profile projectile
./Tools/Invoke-ProjectTool.ps1 -ToolId generate.gas-registry -Apply
./Tools/Invoke-ProjectTool.ps1 -ToolId create.localization-entries -AssetPath 'Assets/MonoBehaviour/目标内容.asset' -Apply
./Tools/Invoke-ProjectTool.ps1 -ToolId import.beam -Source '<参考工程根目录>' -Apply
```

Unity 路径可通过 `-Unity` 或当前进程的 `UNITY_EDITOR_PATH` 环境变量指定；也会查找 Unity Hub 的标准安装位置。执行前核对 `ProjectSettings/ProjectVersion.txt`。不要在同一项目已打开的 Editor 旁启动第二个批处理 Editor；此时使用工具中心。

外部 HellMaiden 工程通过 `-Source` 或 `HELLMAIDEN_SOURCE_PROJECT` 指定，根目录应包含 `Assets`。不再默认猜测 F/D 盘位置。Nordic 资源导入脚本要求明确的 `--source`，它属于维护，不会由构建调用。

场景测试接受原脚本参数；`-Profile` 在旧脚本使用 `Scenario` 时自动映射。复杂参数可用 `-Parameters @{...}` 或 UTF-8 JSON `-ParametersFile`。布尔参数在 JSON 中使用 `true/false`。不认识的参数会报错，不会静默忽略。`-Help` 显示单项参数；构建帮助另列全部 Profile。

## 执行与结果

- **只读校验 / 预览 / 构建：**不执行导入、迁移或创建默认资源。正式 Content、Localization、Scenes、UI、Resources、MonoBehaviour 文件会进行前后哈希对比；发现变化则报告失败，不自动覆盖或回滚用户修改。
- **维护：**统一入口要求 `-Apply`；人工界面确认后执行。影响包括 Prefab 组件、数据库默认值、经验曲线、场景注册或生成代码，具体看该工具的说明。不要把恢复默认配置当成一般校验。
- **构建：**场景、宏、Development、测试程序集由 Profile 指定。常规验收使用 Boot → MainMenu → Gameplay。Sandbox / Nordic 使用独立场景。Wisp 测试的构建场景探针仍保留，资源导入已拆出。
- **资源缺失：**明确失败；需要先单独运行对应维护操作，不会通过构建重新生成并覆盖已有内容。
- **预览：**要求图形设备时不加 `-nographics`。大招预览会跨 Play Mode 等待，未完成状态不算成功。
- **报告：**每次调用写入 `Logs/ProjectTools/<时间>-<ID>/`，包含日志、结果 JSON、耗时、错误和产物位置；各测试保留原来的详细日志目录。`Logs/ProjectTools/<ID>.json` 是该工具最近结果。
- **失败：**资源检查、编译、原生进程非零退出、缺少完成结果、超时都判失败。已有 Unity 原生退出崩溃继续如实记录；`result=PASS` 只证明场景断言通过，不代表进程正常退出。
- **超时：**统一 Unity 调用默认 1800 秒，可用 `-TimeoutSeconds` 修改。场景脚本保留各自等待条件和超时；清理只处理本次记录的进程。

## 兼容与清理边界

旧顶部菜单立即移除。旧构建类和 28 个旧 PowerShell 入口保留一版薄转发，提示对应新入口；它们不再持有另一套构建实现。旧测试场景的断言仍在 `Tools/Scenarios`，组合测试仍复用准备页测试。

`EnemySimulationPrefabMigrator` 的旧方法转发实际 Variant 迁移。规范装备重存不再是日常菜单，归入显式维护。已完成的 `GameLocalizationAssets.Import` 和 `Export-LocalizationMigration.py` 删除，不能再用旧 I2 提取数据覆盖当前插件表格；历史迁移记录、表格与 CSV 保留。

`LegacyEquipmentModifierConverter`、`LegacyPerkModifierConverter` 保留，其迁移与测试仍有调用。GAS / 本地化构建前检查、Steam AppID 构建后处理、GAS 属性绘制器、Build Inspector、波次 Inspector 继续工作。没有删除玩法、美术、场景或第三方插件菜单。

## 扩展工具

1. 在所属领域实现操作方法；公共执行层不引用测试实现类型。普通操作不主动退出 Unity。
2. 在 `catalog.json` 中登记稳定英文 ID、中文用途、方法绑定或场景脚本、参数、影响和运行条件。一个 ID 对应一个明确动作；维护与只读操作分开。
3. 构建只增加 `builds` 配置。特殊构建场景处理通过 `ProjectBuildService.ActiveProfile` 判断，不复制 `BuildPipeline.BuildPlayer`。
4. 新进程测试使用 `Tools/ProjectTools.psm1` 的启动、等待和清理函数，保留自己的行为断言。
5. 运行 `Tools/Update-EditorToolDocs.py` 更新完整清单，再用 `--check` 检查文档与元数据一致；Unity 中运行 `validate.tools` 检查绑定和配置。

不得新增第二个项目根菜单，也不要用 `ExecuteMenuItem` 调用项目内部业务。正式表格和资源以已提交内容为准，自动构建只校验与产出。
