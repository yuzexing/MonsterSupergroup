# EditorTool 整理验收记录 · 2026-09-13

环境：Windows，Unity 6000.3.17f1。范围为编辑器工具、构建和调用链；没有修改战斗规则。此次统一了 59 个旧菜单的去向，保留一个 `MonsterSupergroup` 根菜单、18 个快捷操作、91 个注册工具与 32 个构建配置。原来的 21 处 `BuildPipeline.BuildPlayer` 收敛到一处。

工具整理和下列功能回归已完成。**图形版 Player 的原生退出崩溃仍存在，该项验收为失败**；不能把此次交付称为全部图形进程验收通过。

## 交付入口

- 人工：Unity → MonsterSupergroup → 工具中心。菜单与工具详情均为中文；稳定英文 ID 用于自动化。
- AI：`Tools/Invoke-ProjectTool.ps1`。`-List` 列工具；`-ToolId <ID> -Help` 查看说明、参数和脚本参数可选值。
- [使用指南](README.md)、[完整工具与兼容映射](inventory.md)、[共享元数据](catalog.json)。
- Development 包：`Builds/MenuDevelopment/MonsterSupergroup.exe`。
- 非 Development 包：`Builds/MenuRelease/MonsterSupergroup.exe`。
- 仇恨交接专项包：`Builds/EnemyHandoff/EnemyHandoff.exe`。

验收包沿用项目现有输出目录；日志和构建产物在本机 `Logs`、`Builds` 下，不作为源码提交。复制验收包时须复制整个目录，不能只复制 EXE。

## 自动与实际执行结果

以下路径均相对项目根目录。每个统一入口另有 `Logs/ProjectTools/<时间>-<ID>/result.json`，记录成功状态、耗时、错误、产物及真实进程退出码。

| 项目 | 实际结果 | 证据 |
| --- | --- | --- |
| 元数据、方法绑定、场景存在性 | 通过，使用参数字典指定 Unity 的完整调用也通过 | `Logs/ProjectTools/20260913-124243-659-validate.tools/` |
| 清单与代码约束 | 59 个旧菜单全部映射、18 个快捷入口、1 个构建实现；无项目内部菜单文字调用 | `Tools/Update-EditorToolDocs.py --check` |
| 工具 EditMode 测试 | 7/7；覆盖缺失场景不创建、无 Apply 拒绝写入、配置场景、只读边界与报告 | `Logs/ProjectTools/Tests-20260913-124154/editmode.xml` |
| PowerShell 执行契约 | 通过；真实退出码 7 被拒绝、参数类型转换、Profile 映射、未知参数拒绝 | `Logs/ProjectTools/20260913-124153-813-test.editor-tools/` |
| Development 完整构建 | 通过，Boot＋MainMenu＋Gameplay | `Logs/ProjectTools/20260913-122308-854-build.player/` |
| 非 Development 完整构建 | 通过，Boot＋MainMenu＋Gameplay | `Logs/ProjectTools/20260913-122530-619-build.player/` |
| 仇恨交接专项构建 | 通过 | `Logs/ProjectTools/20260913-122710-050-build.player/` |
| 新入口 KCP 三进程 | 通过；首页 UI 建房、加入、席位、开局、重连和换局 | `Logs/PreparationMenu/20260913-122530-local-party-p7780/` |
| 旧脚本兼容转发 | 通过；旧 PreparationMenu 脚本提示新入口，再执行原 ESC 场景断言 | `Logs/PreparationMenu/20260913-122655-combat-menu-p7781/` |
| 战败、回大厅、重开 | 三进程通过 | `Logs/PreparationMenu/20260913-122655-run-end-p7782/` |
| 本地化、选项、菜单与卡片回归 | 22/22 PlayMode 测试通过，后续表格校验通过 | `Logs/LocalizationValidation/20260913-123134/results.xml` |
| 大招表现预览 | 真实图形设备、PlayMode 采集完成后才返回成功；已查看第二波截图，火焰环与中央符文可见，无粉色错误材质 | `Logs/ProjectTools/20260913-122946-836-preview.ultimate/`、`Logs/Phase02/Ultimate-Preview-SecondWave.png` |
| 非 Development 本地入口隐藏 | 无图形模式断言和正常退出通过 | `Logs/PreparationMenu/20260913-124212-local-release-p7786/` |
| 非 Development 1920×1080 图形窗口 | 首页截图布局和入口隐藏正确；功能断言 PASS，退出码 `-1073741819`，因此整次验收失败 | `Logs/PreparationMenu/20260913-124153-local-release-p7785/`、`Logs/ProjectTools/20260913-124153-806-test.preparation-menu/` |

三进程联机回归使用真实独立游戏进程与 KCP，默认 1280×720、无图形模式。运行中的场景断言和实际网络行为有执行，不以只检查 Assignment 或构建字段代替。此次不宣称在所有分辨率逐页完成人工操作，也没有向真实 Steam 好友发送邀请。

## Editor 真实鼠标与键盘核验

在实际打开的 Editor 中完成：

1. 顶栏只见一个 MonsterSupergroup 项目根菜单；第三方菜单仍保留。
2. 工具中心默认停靠在 Scene / Game 视图旁，详情、参数、影响、产物和最近结果可见。
3. 使用真实键盘输入 `validate.localization`，列表正确筛选；点击执行后显示成功，报告为 `Logs/ProjectTools/20260913-124023-371-validate.localization/result.json`，和批处理校验使用同一个执行入口。
4. 搜索 `generate.gas-registry`，点击“查看影响并执行”，确认框列出将更新 `GeneratedModifierRegistry.g.cs`；点击取消，没有运行生成。
5. 搜索 `diagnostic.enemy-handoff`，未进入 Play Mode Host 时显示条件提示并禁用执行。

本次没有实际执行会覆盖资源的历史导入、Perk 重建或场景初始化；这些入口保留实现并经过绑定校验，其资产影响和 `-Apply` 要求记录在完整清单中。未把这些维护操作标为已验收执行。

## 高频交接回归

两种网络条件都跑了 Host＋两个客户端，且真实产生和接收移动快照；每组压力阶段持续 120 秒。模拟异常网络沿用项目已有的延迟、抖动、非可靠丢包及乱序注入配置。

| 网络 | 怪物数 × 每秒请求 | 请求数 | 完成交接数 | 合并数 | 最大快照间隔 | 停止后的收敛时间 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 正常 | 20 × 20 | 48000 | 47260 | 780 | 0.133 秒 | 0.100 秒 |
| 正常 | 100 × 5 | 60000 | 59800 | 300 | 0.986 秒 | 0.033 秒 |
| 模拟异常 | 20 × 20 | 48000 | 10805 | 26210 | 0.635 秒 | 0.534 秒 |
| 模拟异常 | 100 × 5 | 60000 | 49295 | 10905 | 0.486 秒 | 0.517 秒 |

计数沿用现有诊断口径，包含压力结束时的收敛处理，不把请求、合并和完成数解释为相加守恒关系。动作阶段切换、Boss 动作连续性、击退、超时接管、旧消息拒收、断线重连、Downed 输入锁和全员 Frozen 阶段均通过；所有进程正常退出。

完整记录：`Logs/EnemyHandoff/20260913-122902-normal/` 与 `Logs/EnemyHandoff/20260913-123157-impaired/`。

## 资源与失败处理

- 正式 Content、Localization、Scenes、UI、Resources、MonoBehaviour 共 1401 个文件，与开始整理前的 SHA-256 清单对比无变化。记录：`Logs/EditorTools/asset-check.json`。
- 新工具的只读执行会比较正式资源前后哈希；发现变化即失败，不自动回滚用户内容。
- 已完成的 I2 迁移专用实现与提取脚本删除；表格、CSV、内容 GUID、战斗配置和两个 Legacy Modifier 转换器保留。
- 图形 Player 的退出崩溃为既有问题，详见 [原生退出诊断](../plans/native-player-exit-diagnosis.md)。此次新执行器确实将 `0xC0000005` 判为失败，并记录 `CleanupRequested=false`，说明不是测试清理主动结束了进程。无图形模式通过不能消除这一失败。

## 可重复人工步骤

1. 打开工具中心，分别按“准备房间”“本地化”“仇恨”或稳定 ID 搜索；核对中文用途与运行条件。
2. 从制作入口打开现有时间轴、准备配置和本地化表格，再运行对应校验。缺失资源应报错，不能自动创建默认数据。
3. 选择一项维护工具，检查影响说明并取消；需要真正修改时，再检查版本控制差异后明确执行。
4. 构建 Development 包，启动三个进程，通过游戏内本地建房 / 加入入口验证开局、ESC、选项和战败换局。
5. 使用 `test.enemy-handoff -Profile normal` 或 `impaired` 重跑交接；先构建 `build.player -Profile enemy-handoff`。
6. 使用 `preview.ultimate` 或 `preview.attack -Profile beam` 产出画面后检查实际截图。Unity 批处理前先关闭同项目 Editor。
7. 检查统一结果文件与进程退出码；即使日志出现 PASS，非零退出、超时或缺失完成结果都必须保留为失败。
