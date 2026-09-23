# 基于原生 Build Profiles 统一构建配置与入口

## 1. 核查结论与已确认范围

**实验性上下文管理已在配置中开启**：[config.toml 第 159–160 行](/C:/Users/ADMIN/.codex/config.toml:159) 为：

```toml
[features.context_management]
experimental_mode = true
```

这是官方支持的配置项；本次确认了磁盘配置，无法直接读取当前任务内存中的生效状态。[官方配置说明](https://learn.chatgpt.com/docs/config-file/config-reference)

项目实际使用 **Unity 6000.3.21f1**。本轮仅完成只读审计，没有修改文件、切换 Profile 或启动构建。

当前差距：

| 部分 | 当前实现 | 改造方向 |
|---|---|---|
| 配置来源 | JSON 中的 7 个配方、34 个别名，加窗口变量与命令行覆盖 | 原生 Profile＋业务子资产 |
| 解析与构建 | 手工指定 Windows、场景、Development，通过 `extraScriptingDefines` 注入宏 | 解析完整有效配置，调用 Profile 构建重载 |
| 原生按钮 | 没有共享服务适配器，Guard 会拒绝缺少上下文的构建 | 原生按钮先进入共享服务 |
| 构建保护 | 已有版本、唯一目录、结果指针、资源检查、取证清理 | 增量保留，完善事务顺序和失败隔离 |
| TMP | 包内回调会清理动态字体数据，项目未发现配套恢复事务 | 补齐恢复，验证非空缓存场景 |

已确认的决策：

- 实施时删除 `Window-dev`、`Window-test` 及各自 `.meta`，新建规范模板。
- 旧配方、旧业务参数**暂不兼容**；旧脚本文件保留，旧构建调用明确报迁移错误，交由你之后审查去留。
- 使用“应用配置／激活 → 等待编译完成 → 构建”；查看模板不自动切换 Profile。
- `Build and Run` 只允许 Direct 分发；Steam 分发在开始构建前解释并拒绝该执行请求。
- 保留其他未提交修改；不扩展战斗、网络、诊断采集功能，不上传 Steam。

## 2. 配置模型与初始模板

新增 Editor 专用 `MonsterBuildSettings : ScriptableObject`，通过 `CreateComponent<T>()` 嵌入原生 Profile。复用现有枚举，字段限定为：

- `SchemaVersion`
- `BuildKind`
- `Network`
- `Distribution`
- `Diagnostics`
- `PurposeId`：沿用现有 7 种产品／专项用途的稳定 ID。

测试程序集、开发工具、专项宏及构建校验由用途规则派生，不开放独立布尔开关。场景以原生 Profile 为准，同时校验其是否符合用途要求。

原生组件 API 已在本机安装版本的程序集与 XML 文档中确认存在。[Unity BuildProfile API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Build.Profile.BuildProfile.html)

**配置归属：**

| 内容 | 唯一来源 |
|---|---|
| 平台、架构、场景、Development、调试、分析、压缩 | 原生 Profile |
| Player／Graphics／Quality | Profile 覆盖及实际继承的项目设置 |
| 构建类型、用途、网络、分发、诊断 | `MonsterBuildSettings` |
| 项目构建宏与能力 | 共享用途规则 |
| 输出位置、清理缓存、构建后运行 | 本次执行请求 |
| BuildId、时间、结果 | 本次执行生成 |

版本继续以**全局 Player Settings 的 `bundleVersion`** 为唯一来源，当前磁盘值为 `0.0.1`。保留独立版本更新操作；Profile 的版本覆盖必须与全局一致，否则拒绝构建，不能误把活动 Profile 的覆盖值当作全局版本。

初始创建 11 个模板：

| 模板 | 业务配置 | Development |
|---|---|---|
| Windows-Dev-Kcp | 产品 Dev／Kcp／Direct／Normal | 开 |
| Windows-Test-Steam | 产品 Test／Steam／Steam／Normal | 关 |
| Windows-Test-Evidence | 产品 Test／Steam／Steam／Evidence | 关 |
| Windows-Test-Profiler | 产品 Test／Steam／Steam／Normal | 开 |
| Windows-Shipping | 产品 Shipping／Steam／Steam／Normal | 关 |
| 六个专项 Dev 模板 | 对应现有六种专项用途／Kcp／Direct／Normal | 开 |

默认 Windows x64 Player；Dev、Profiler 使用 LZ4，其余产品模板使用 LZ4HC。Profiler 模板开启自动连接 Profiler；默认关闭深度分析、等待调试器、生成解决方案。Player／Graphics／Quality 默认继承项目。

各模板显式保存原配方场景，避免继承当前额外含 `Network` 场景的全局列表。专项 Test、Steam Direct 等合法变体通过复制并编辑 Profile 获得，不生成全排列。

## 3. 共享解析、宏与入口设计

扩展现有 Resolver，输入为 `BuildProfile + BuildExecutionRequest`，输出只读 `ResolvedProjectBuild`：

- Profile GUID、路径、业务配置、原生选项、场景顺序。
- Player／Graphics／Quality 的配置来源、关键有效值及相关资源依赖摘要。
- 实际符号、预期受管理符号、派生能力、版本来源、校验清单。
- 分开保存内容配置摘要、工程输入摘要和执行信息；输出路径、运行请求、BuildId 不参与内容配置比较。

预览不修改资源、宏或活动 Profile。执行前重新解析，预览后配置或输入改变时拒绝过期计划并要求刷新。

公开 API 未暴露的原生字段集中放入 **6000.3.21f1 适配层**，使用 `SerializedObject` 读取；字段缺失或版本结构不匹配时明确报错，不反射调用 Unity 内部方法。配置摘要覆盖继承设置及依赖，不能只哈希 Profile 文件。

**宏准备：**

- “应用配置”先显示差异，再替换本工具管理的符号集合，保留无关符号。
- 分别检查 Profile 与有效 Player Settings 的符号；全局残留构建宏、缺失宏、多余宏和冲突宏均拒绝。
- 测试程序集由用途规则派生 `IncludeTestAssemblies`，不靠随意添加 `UNITY_INCLUDE_TESTS` 解锁。
- 应用或激活后等待编译、导入、域重载完成；构建只校验，不修宏、不续跑跨重载构建。
- 修正取证宏引起 Editor 默认采集的变化，并处理交接专项日志等受影响分支，保持原有 Editor 行为。

Profile 符号会同时影响活动 Profile 下的 Player 和 Editor，并叠加 Player Settings 符号，因此不能视为旧 `extraScriptingDefines` 的直接替换。[Unity 符号说明](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Build.Profile.BuildProfile-scriptingDefines.html)

**三个入口：**

- 原生按钮：唯一 `RegisterBuildPlayerHandler` 适配器提取执行请求，再进入服务；原生传入配置与计划不符时报错。
- 自定义窗口：只保存所选 Profile 引用和执行选项；业务编辑直接作用于子资产，显示准备状态及最终摘要。
- 命令行：新增明确的 `-BuildProfile <资产路径>`，启动 Unity 时传入 `-activeBuildProfile`，通过统一静态入口执行；方法内不临时切换。

服务要求目标 Profile 已激活且编译就绪，调用 `BuildPipeline.BuildPlayer(BuildPlayerWithProfileOptions)`，只附加执行选项与派生测试程序集选项。Guard 校验合法上下文及实际构建参数；直接绕过服务仍拒绝，回调不递归发起构建。

## 4. 实施顺序与失败处理

**阶段一：模型、规则和模板**

在现有 [EditorTools](/F:/UnityStore/MonsterSupergroup/Assets/_Project/EditorTools/Editor) 中新增业务子资产、原生适配层、执行请求和准备工具，改造 Resolver。

从现有配方一次性迁移用途规则与必要模板。保留 Wisp 场景副本注入、Nordic 校验、Sandbox／Nordic 不含测试程序集等区别。删除两个旧 Profile；旧 JSON 配方停止参与执行，其历史内容与旧脚本保留供审查。

**阶段二：统一入口与构建服务**

改造窗口、Service、Identity／Guard、ToolRunner 和 PowerShell 入口；新增原生按钮适配器。旧字符串配方入口保留报错外壳，避免删除方法导致现有脚本编译失败。

新结果按 Profile GUID 保存，停止发布旧配方结果指针；旧结果读取路径明确报迁移错误。必要验收读取逻辑支持指定新 Profile，保留显式选取历史包的能力。提供旧调用受影响清单，不批量改写旧场景脚本。

**阶段三：事务、包校验与交付**

执行顺序固定为：

> 失效本次目标的旧结果指针 → 重新解析与校验 → 创建身份和事务快照 → 唯一暂存目录构建 → 后处理 → 恢复临时状态 → 资源及产物校验 → 发布成功目录与结果 → 按请求运行

- TMP 快照仅覆盖会被原生回调清理的动态字体及相关子资产，保存磁盘和必要内存状态；恢复后继续执行资源完整性检查，不忽略字体目录。
- 所有清理置于外层 `finally`；逐项执行并汇总失败，不能因一个清理异常跳过其余恢复。
- BuildInfo 增加 Profile 身份及计划摘要，新生成包采用新 schema；保留旧 schema 的显式历史包读取能力。
- 取证归档纳入所选 Profile 和计划记录，延续本次 BuildId，不从历史包补文件。
- 包校验覆盖 EXE、BuildInfo、真实编译能力、测试程序集、Steam AppID 分发规则及必要取证文件。
- 失败或取消的产物进入隔离目录，不发布成功标记、结果指针或启动 Player。
- 不向 Unity 传 `AutoRunPlayer`；Direct 包全部完成后启动最终实际 EXE。启动失败单独报告，不伪称构建失败。
- 结果发布失败也撤销成功状态，不退回上次成功包。

**阶段四：文档与迁移交付**

更新 [构建指南](/F:/UnityStore/MonsterSupergroup/docs/build-guide.md)、工具说明和验收记录，提供新模板清单、新命令、准备流程，以及“旧入口／受影响脚本／建议去向”的审查表。

修改集中于构建工具、[Tools](/F:/UnityStore/MonsterSupergroup/Tools)、构建身份、必要回调和对应测试；运行时代码仅调整宏迁移影响及身份校验。

## 5. 验收与待实测边界

先完成规则和迁移测试，再执行代表性真实构建：

- **持久化与只读性**：重启工程后配置恢复；预览不改文件、宏或活动 Profile。
- **入口一致性**：同一 Profile 经原生窗口、自定义窗口、命令行得到相同内容配置与能力。
- **符号与 Editor**：用途切换能增删受管理宏、保留插件宏；域重载后生效；Editor 不因切换取证 Profile 自动采集。
- **负向校验**：缺少组件、错误 schema、错误平台／场景、符号不一致、版本覆盖冲突、未就绪 Profile、非法 Shipping、过期计划全部拒绝。
- **原生能力**：验证 Development、脚本调试、Profiler、深度分析、压缩，以及 Player／Graphics／Quality 覆盖和继承的实际效果。
- **专项能力**：验证通用 Gameplay、Wisp 注入、专项 Test 的测试能力，以及 Sandbox／Nordic 的程序集差异。
- **真实 Player**：启动日志报告实际编译类型、能力、版本与 BuildId，并与计划比较；增加篡改 BuildInfo 的负向检查，避免 JSON 自证。
- **失败与运行**：覆盖取消、前处理／后处理异常、非空 TMP 缓存、恢复失败、结果发布失败；失败产物不能被默认选中，不能启动。
- **代表构建**：KCP Dev 用三个入口验证一致性；补产品 Test、Evidence、Profiler 和 Wisp／专项 Test 的必要真实包验证，不重复完整战斗验收。

原生按钮传参、设置覆盖效果、Profile 构建与插件回调配合、测试程序集及 TMP 内存恢复均属于**实施阶段待实测项**。本轮只确认代码与 API，不将其写为已经通过。

Shipping 成功验收必须基于包含本次改造的干净提交；当前脏工作区仅验证拒绝行为，不自动提交，也不放宽规则。Steam 分发包的最终双账号启动与联机保留为人工验收。

## 6. 断流接续进度（2026-09-23）

第 1–5 节保留原计划及当时的只读审计结论。最新实施状态、实际验证和剩余问题以 [接续验收记录](build-profiles-validation.md) 为准，不能把原计划或历史报告当作本轮通过证明。

- 已确认当前目录与原任务一致：`F:\UnityStore\MonsterSupergroup`，`master`，HEAD `cb276527a3be1e3789fae05bf5a714d641a7aa0d`。这是主工作区；旁边的 `MonsterSupergroup_validation_exit_6000_3_21` 是另一处 detached worktree。本轮未切换目录／分支，暂存区为空。
- 已实现但未整体验收：11 个原生模板、业务组件、解析／符号准备、三个构建入口、事务恢复／包校验、迁移说明均已有代码或文件。已有构建和战斗取证改动继续保留。
- 历史证据实际为：EditMode **69 通过、1 失败**；两次 KCP CLI 构建均未发布。第二次失败快照仅 `ProjectSettings/ProjectSettings.asset` 的文件摘要变化，具体字段和原因仍无法确认。
- 本轮只推进原计划第二阶段“新 Profile 结果读取、显式历史包保留”的最小验收：自动选择强制 schema 3 及有效配置／输入摘要；显式 schema 2 历史包选择仍可用。新增缺失 schema 用例先失败，修复后 PowerShell 7.6.5 和 Windows PowerShell 5.1 各 **44 项通过**。仅修改结果读取、对应测试与本进度文档，没有启动 Unity。
- 下一步：沿用原任务“诊断测量期间暂缓 Unity 实测”的安排。恢复实测后，先捕获 `ProjectSettings.asset` 在构建前后的原始内容，定位输入摘要变化并修复具体原因，保留输入变化拒绝机制；重跑 EditorTools 测试和一次 KCP CLI 构建，成功后再推进其他入口与代表包验收。

## 7. 输入漂移诊断准备（2026-09-23）

已按随后确认的最小计划完成文件证据采集与离线验收，继续暂缓 Unity 实测：

- 共享服务已接入初始计划、Unity 调用前／返回或抛错后、身份清理后、TMP 恢复后、最终计划六个采集点。初始／最终快照来自输入摘要计算的同一次读取；预览不创建采集器。
- 仅归档 `ProjectSettings/ProjectSettings.asset` 原始字节，记录 Profile／BuildId、时间、长度及 SHA-256；输出相邻阶段与首尾差异。每次尝试使用独立日志目录，阶段报告不覆盖，结束时写最终报告。
- 文件缺失、读／写失败与未执行阶段分别记录。`Complete` 仅指证据齐全，不表示无漂移或构建成功。采集失败不改变构建异常、清理及发布判定。
- 本轮实际离线测试 **20/20，306 个断言**；既有结果选择回归在 PowerShell 7 和 Windows PowerShell 5.1 各 **44 项通过**。证据：`Logs/BuildProfilesResume/input-diagnostics-20260923-171954-158`，过程失败也保留。
- Unity 接入仅做静态核对，**尚未经过 Unity 编译及生命周期实测**；未复现历史漂移、未生成 KCP 包。下一项仍是恢复实测后，用此采集复现一次 KCP CLI 构建，根据实际字段变化制定定点修复。

详细接口边界、运行方法及本轮证据见 [接续验收记录](build-profiles-validation.md#2026-09-23-输入漂移诊断准备)。
