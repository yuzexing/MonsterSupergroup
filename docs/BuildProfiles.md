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

构建前后的 `contentHash`／`inputHash` 用于记录差异，不要求两次相等才能发布；出现差异时保存构建后计划并报告警告。是否成功仍由 Unity 构建结果、清理及实际包校验决定。此项按 2026-09-24 用户最新要求调整，替代下面历史进度中的“输入摘要变化即拒绝”安排。

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

## 8. Unity 编译与生命周期复现门槛（2026-09-23）

用户已解除 Unity 实测暂缓安排。本轮仍使用原主工作区 `master`，实际 HEAD 已是 `4e473e28f483105426a3df532a560aa13c622ad6`；第 6、7 节保留各轮历史基线。上轮归档的 9 项实现／文档哈希与本轮开始时一致，暂存区为空，保留现有 5 项诊断相关改动。

- **编译已验证**：Unity 6000.3.21f1 在本轮实际重新编译 EditorTools 与其测试程序集；编译响应文件包含采集组件，没有 C# 编译错误，未作兼容性修订。
- **完整测试未通过**：显式激活 Windows-Dev-Kcp，直接运行 `MonsterSupergroup.EditorTools.Tests` EditMode；实际执行 46 项，45 通过、1 失败、0 跳过，Unity 真实退出码 2。三类测试均包含在 XML 内，没有沿用历史 69/70。
- **当前阻塞**：`BuildIdentityTests.ApplyUsesLiveVersionAndIsIndependentOfBuild` 在 `UpdateVersion` 的 `AssetDatabase.SaveAssets()` 路径产生非预期错误日志；Unity 无法将 `Temp/UnityTempFile-*` 替换为 `ProjectSettings/ProjectSettings.asset`。日志共 4 次替换失败，没有底层系统错误码，文件锁／权限或其他文件系统原因尚未确认。未放宽断言或修复范围外问题。
- **生命周期与构建未完成**：按计划的测试门槛停止，未执行 KCP CLI 构建，没有本轮 BuildId 或六阶段构建证据，也未启动 Player。测试前后设置原始字节一致，不能据此推断构建期间不会漂移。
- **保护与交付**：本轮冻结的 91 个文件在 Unity 退出后哈希全部一致，原有改动保留；仅追加本计划与验收记录。证据位于 `Logs/BuildProfilesResume/unity-lifecycle-20260923-202642-841`，包含准确命令、PID／时间／退出码、XML、Unity 日志、原始设置副本、受测源码及程序集哈希、失败分析和本轮文档差异。

下一项先定位并解除设置文件替换失败这一测试阻塞：捕获失败时的系统 I/O 结果及占用／权限证据，再制定最小处理；不能把退出后可读或哈希未变当作写入能力通过。随后重跑完整 EditorTools 测试，全部通过且退出 0 后，才继续一次 KCP CLI 构建和六阶段漂移定位。历史漂移原因、当前字体清理路径、完整非空 TMP／故障注入矩阵、其他入口及代表包均仍待实测。详情见 [本轮验收记录](build-profiles-validation.md#2026-09-23-unity-编译与测试门槛实测)。

## 9. 文件替换探针与单测复现（2026-09-23）

本轮执行随后确认的定位计划，证据目录为 `Logs/BuildProfilesResume/file-replace-20260923-210518-132`。原主工作区／分支／HEAD 未变，既有 7 项改动继续保留。

- 已完成日志目录及 `ProjectSettings` 唯一临时子目录的 **52 组隔离探针**，覆盖继承／复制源文件权限、创建／改名／覆盖移动／替换、关闭句柄／允许删除共享／禁止删除共享／只读及空格路径。16 组关闭句柄操作全部成功；受控占用和只读场景取得 Win32 5／32，不能把受控失败计为测试未通过或把它们直接归因到 Unity。160 份原始探针快照重新核验长度和 SHA 全部一致。
- 正式设置未用于试探性覆盖或删除；两个临时输入目录已清理，正式设置原始字节未变。当前结果证明隔离文件的基本替换操作可工作，不能排除正式路径在 Unity 运行时存在占用或其他限制。
- **失败单测再次复现**：`ApplyUsesLiveVersionAndIsIndependentOfBuild` 实际 0/1，Unity PID 34576，退出 2，再次出现四次设置文件替换错误。未修改保存逻辑或断言。
- **系统取证受阻**：WPR FileIO／Minifilter 启动返回 `0xc5585011`，提示无法启用系统性能分析策略；没有生成 ETL，退出后 WPR 未在记录。该错误只说明本轮采集能力不足，不说明 Unity 写入失败的原因。
- 按已确认的停止条件，**完整测试未重新执行，KCP 构建／六阶段生命周期仍未执行**。91 个冻结文件在单测后哈希一致；期间出现两个非本轮新增的战斗取证测试文件，已记录时间和哈希并原样保留，不宣称整个仓库无并发变化。

下一项需要先在能启用系统 I/O 跟踪的会话中取得 Unity 实际失败操作、返回状态和关联占用证据；Unity 自身保持原执行权限，避免改变复现条件。仅在证据确认原因后处理，再重跑完整 EditorTools 测试；全套通过且退出 0 后恢复一次 KCP 生命周期复现。本轮未自动提升权限、安装工具、调整系统策略或扩大修复范围。详见 [本轮定位记录](build-profiles-validation.md#2026-09-23-文件替换探针与失败单测复现)。

## 10. 双会话采集与单测跟踪（2026-09-23—24）

用户已确认可提供管理员终端。新档案为 `Logs/BuildProfilesResume/io-trace-20260923-233528-332`；管理员采集器只负责 WPR／logman，普通权限控制器负责隔离自检和通过门槛后的 Unity 单测，不自动提升 Unity 权限。

- 开始时检测到另一任务正在运行诊断基准，未与之并行启动本轮采集／Unity。负载退出后重新冻结 **4165 个实际输入文件**，包括未跟踪源码、程序集、Packages／ProjectSettings、Profile 及相关执行工具；不再使用上轮 91 项局部清单作为本轮基线。
- 管理员两次实际启动采集器，WPR FileIO／Minifilter 启动成功，仅管理本轮命名实例；普通权限控制器执行探针和 Unity。首次控制器在空差异数组序列化处失败，尚未执行探针或 Unity；修复仅在日志脚本内，原失败及超限 ETL 保留。增加空数组／null 用例后 **21/21** 本地门槛检查通过；此前 **3/3** 负向启动检查保留各自运行记录。
- **采集自检已通过**：`retry-02/smoke-01` 的正常改名对应 NTSTATUS `0x00000000`；禁止删除共享的操作在打开源文件时返回 NTSTATUS `0xc0000043`，调用端另记 Win32 `32`。tracerpt 导出 5,307,018 条事件，汇总丢事件为 0；原始 XML 与本机 xperf 的路径、PID／TID、IRP、原生状态及时间交叉核对一致。ETL 为 424,673,280 字节；验收只覆盖这两个已核对操作，不表示所有导出事件均可解析。
- **失败单测仍未通过**：自检放行后普通权限 Unity PID **21800**，实际 1 项、0 通过、1 失败、0 跳过，真实退出 **2**，未超时或强制终止。仍有四次设置文件替换错误。4165 项输入和正式设置前后未变；没有有据可依的项目保存逻辑修复。
- **单测跟踪不完整**：虽然 WPR 正常停止、停止前报告丢事件 0，最终 ETL 为 **6,573,522,944 字节（约 6.12 GiB）**，超过 1 GiB。采集器的实时目录长度检查没有触发上限，最终文件检查已拒绝作为完整证据；`stopped.json.boundReached=false` 不能代替最终大小核验。保留超限工件，只做有限定位分析。
- **定位取得直接进展，根因未确认**：从保留 ETL 的测试时段核对四个日志临时文件，每个均有 100 对 `FileIoRename → FileIoOpEnd`，共 **400 对**，Unity PID 21800／线程 26880，均返回原始 **NTSTATUS `0xc0000022`（STATUS_ACCESS_DENIED）**。并未测得 Unity 的即时 Win32 错误码；不能将这个状态写成某个过滤驱动、权限配置或项目句柄泄漏已被证实。完整访问掩码、全程句柄关联及满足体积上限的跟踪仍是缺口。
- 按停止边界，**完整 EditorTools 测试未重跑，KCP 构建／六阶段生命周期未执行**；历史 45/46 不能计作本轮全套结果。管理员采集器及本轮 Unity 均已退出，未启动 Player、未提交或更改正式工具。

下一项先修正并实际验证采集体积控制，查明实时计数漏报／停止合并增量的来源，保留失败完成状态及关联事件所需的采集配置；改变配置后重新自检，再取得有界的 Unity 单测跟踪。仅在证据支持时处理具体原因；完整测试通过且输入稳定后，才继续一次 KCP 生命周期复现。准确命令、失败过程和剩余缺口见 [双会话实测记录](build-profiles-validation.md#2026-09-23-双会话采集准备) 与档案 `README.md`。

## 11. 收缩诊断与恢复测试（2026-09-24）

本节替代第 10 节的下一步：不扩张采集框架，不把体积控制、完整权限／句柄链或外围工具自检作为获得下一条诊断信息的前提。证据目录：`Logs/BuildProfilesResume/save-contrast-20260924-105656-392`。仍在原主工作区 `master / 4e473e28`，保留既有及其他任务改动，暂存区为空。

- **历史事实重新核对**：`BuildProfilesImplementation/editmode.xml` 的版本更新单测在 2026-09-23 15:37（北京时间）确实通过；随后三轮出现设置替换失败，其中一次取得 NTSTATUS `0xc0000022`。不能描述为“一直不能保存”。
- **本轮最少对照**：同一当前保存代码、项目、Profile 和普通权限启动，仅对比是否传 `-nographics`。两个独立 Unity 进程均 **1/1 通过，退出 0**；没有修改保存方法、ACL、防护或清理缓存。不能据此把原因归到 `-nographics`。
- **完整测试已恢复**：调整下述摘要门槛后实际重新编译 EditorTools，完整测试 **46/46，0 跳过，退出 0**：BuildIdentity 23、BuildRecipe 15、ProjectTool 8。两次单测及全套均未出现设置替换错误。本轮未复现，不宣称历史原因已修复；环境与临时项目／路径状态仍待区分，不再凭此修改保存实现。
- **用户要求的最小产品改动**：共享服务不再因构建前后 `ContentHash`／`InputHash` 不同而抛错；继续保存 `build-plan-after.json`，改报警告。保存方法、摘要算法、BuildInfo schema 及包内身份一致性规则未改。该发布分支尚待实际构建验证，不能用 EditMode 通过替代。
- **下一步**：本轮测试至此结束。下一项仅执行一次 Windows-Dev-Kcp CLI 构建，记录六阶段设置差异和实际包结果，允许前后摘要不同；不追加 WPR 自检，不启动 Player。若替换故障再现，再在相同项目与 Profile 下用普通终端执行同一命令对照自动化启动；仍失败时才单独比较旧直接保存与当前适配保存，每次只改一个变量。无需预先完成完整故障矩阵。

本轮未进行 KCP 构建，未宣称有新包或历史漂移根因已确认。具体命令、进程与结果见 [验收记录](build-profiles-validation.md#2026-09-24-最小启动对照与完整测试恢复)。

## 12. 一次 KCP CLI 构建完成（2026-09-24）

本轮执行第 11 节下一项，仍为原主工作区 `master / 4e473e28`，保留全部既有改动。没有修改产品源码、重跑全套测试或增加系统采集；启动前相关源码／Profile 与最近 46/46 的归档一致，该测试结果仍标作上一轮。

- **构建交付成功**：仅一次 Unity，PID 59120，04:05:46.6463421–04:07:25.6351758 UTC，退出 **0**。BuildId `20260924T040614035Z-ba480bf3`；Dev／Kcp／Direct／Normal，未清缓存、安装模板或运行 Player。
- **六阶段证据完整**：六份快照均为 Captured，长度、SHA、时间顺序及初始／最终计划的对应输入条目独立核验通过。采集状态 Complete 仅描述证据；交付成功另由实际包核验确认。
- **历史摘要变化已取得具体内容**：`preloadedAssets` 在 Unity 调用前后新增 GUID `99f9c9493070a9d4c979a8fec7c5a8d3`，对应 Behavior 包的 `Behavior App UI Settings.asset`。设置 SHA 从 `3c2d8d83…` 到 `64027d29…`，与历史 KCP 记录完全一致；在最终计划仍存在，进程退出后恢复。App UI 插件源码的增删逻辑与之吻合，但未单独确认具体写回时点，不把阶段区间当成回调因果证明。
- **新摘要规则实际生效**：contentHash 相同、inputHash 不同，唯一变化输入为 ProjectSettings.asset；只报告警告并保存构建后计划，实际成功发布。包内初始身份、计划及结果指针继续一致。
- **替换拒绝本次未复现**：未触发普通终端追加构建或旧／新保存对照。历史拒绝原因仍未确认，不能将它与本次设置摘要变化混为一件事。

交付目录：`Builds/ProfileValidation/KcpLifecycle-20260924-120526-189-3aa35027/MonsterSupergroup-v0.0.1-dev-20260924T040614035Z-ba480bf3/`。EXE、BuildInfo、完成标记、Profile 指针及实际 DLL 能力已核对；暂存产物已移出，没有本次失败隔离目录。

证据目录：`Logs/BuildProfilesResume/kcp-once-20260924-120526-189-3aa35027`。包含进程日志、六阶段及外部快照、独立差异、包核验、相关插件源码副本和哈希。本轮按验收终点停止；Player 运行、其他入口／代表包、非空 TMP 和故障注入矩阵仍未执行。详见 [本轮验收](build-profiles-validation.md#2026-09-24-一次-kcp-cli-构建与生命周期核验)。

## 13. 三入口验收进行中，预览及原生 Build 通过（2026-09-24）

本轮档案：`Logs/BuildProfilesResume/kcp-entries-20260924-131415-902-2c273891`。原主工作区仍为 `master / 4e473e28`，暂存区为空；已冻结 108 项相关文件及既有 Git 差异，并留存成功 CLI BuildId `20260924T040614035Z-ba480bf3` 的结果指针，不重新运行 CLI。

- 已启动可见 Unity 6000.3.21f1，PID **45688**，未传 `-activeBuildProfile`；原生窗口显示 Windows-Dev-Kcp 为活动 Profile。尚不能据此替代“原生构建后正常关闭并重开”的持久化验收。
- 自定义“构建配置”窗口已打开，但恢复在主屏左侧之外；电脑控制工具点击其最大化按钮返回坐标越界。已请求用户仅将窗口移回可见区域，未修改产品代码或资产来绕过界面。
- 当前 49 项 Profile／设置文件与预览前基线一致，但 **KCP → Test-Steam → KCP 浏览序列尚未执行，预览只读检查未通过验收**。两次按钮构建均未启动，没有新 BuildId、包或六阶段证据。
- Editor 暂时保持打开以便移回窗口；进程尚未退出，退出码未取得。恢复后按原顺序完成只读预览、原生 Build、归档、正常重开、自定义构建。若实际入口失败则保留现场并停止，不追加 CLI、Player 或修复。

当前为界面操作阻塞，不能记作构建失败，也不能记作三入口一致性通过。详细状态见 [本轮记录](build-profiles-validation.md#2026-09-24-三入口验收启动与界面阻塞)。

**后续进展（以上为初次启动时状态）**：用户接管全部窗口操作，完成 KCP → Windows-Test-Steam → KCP 预览，确认 KCP 显示“当前已激活”、Test-Steam 显示“尚未激活”。独立复核 49 项 Profile／设置文件，数量和 SHA 全部一致；相关构建源码亦与冻结基线一致。预览的文件字节与活动状态检查通过，证据保存于同档案 `manual-preview/`。原 Editor PID 45688 已于 05:50:51 UTC 正常退出 0；当前用户从 Hub 重开的 Editor PID 64304 未传活动 Profile 参数，另以 `native-manual/` 记录其后续进程和构建证据。尚未执行原生／自定义构建；下一步由用户点击原生 Build，使用本轮 Native 输出目录，完成即先归档。本次重开早于原生构建，不代替计划中“原生构建后重开”的持久化检查。

**原生 Build 已完成并核验**：用户通过真实按钮完成一次构建，日志调用栈包含 `BuildProfileWindow.OnBuildButtonClicked → NativeBuildEntry.Build`，BuildId `20260924T075801388Z-66f2461b`。实际包、初始计划、BuildInfo、完成标记、独立归档指针及 DLL 能力的 16 项核对全部通过。与既有 CLI 的有效配置字段及 contentHash 全部一致；工程输入清单有 10 项其他诊断／测试源码差异，已逐项归档，不要求不同次构建 inputHash 相等。

原生六阶段全部 Captured，长度、SHA、时间顺序及首尾计划输入条目独立复核通过；变化仍仅为 `preloadedAssets` 增加已知 GUID。Editor 当前未退出，退出后恢复尚未验证。产物位于本轮 `Builds/ProfileValidation/KcpEntries-20260924-131415-902-2c273891/Native/MonsterSupergroup-v0.0.1-dev-20260924T075801388Z-66f2461b/`，证据在档案 `native-manual/`。TMP Fallback 资产导入不一致日志保留，不扩展修复；构建与发布均成功。下一步由用户正常关闭并从 Hub 重开同一项目，确认 KCP 活动状态与 Dev／Kcp／Direct／Normal 配置恢复，再做一次自定义按钮构建。尚不能宣称三个入口整体验收完成。

**正常重开检查进展**：原生构建所在 Editor PID 64304 已于 08:10:15.5831295 UTC 正常退出 **0**；退出后设置回到 A，49 项 Profile／设置文件与预览前均一致。用户从 Hub 重开 PID 53588，未传 `-activeBuildProfile`；截图确认 KCP 仍为 Active。证据在 `restart-check/`，原生生命周期报告已补入退出后快照并保留退出前版本。自定义窗口配置的界面核对待用户完成，随后才执行自定义按钮构建。

**原生 Inspector 展示缺口**：用户明确要求业务参数在原生 Profile 的 Inspector 内显示。核查确认目前只完成业务子资产及其 CustomEditor，并由独立 ProjectBuildWindow 绘制；没有接入原生 Build Profiles 内置面板。Unity 6000.3.21f1 的 CreateComponent 只提供子资产存储，内置面板的非公开设置提供器使用固定列表，不会自动展示任意业务组件。不能把“配置已嵌入原生 Profile”写成“原生 Inspector 展示已实现”，也不能泛称 Unity 完全不支持自定义 Inspector。此展示要求单列**尚未完成**，本次只读核查不修改实现、不追加构建；既有原生 Build 成功与界面展示缺口分别记录。详情在档案 `native-inspector-gap.md`。

**持久化检查完成**：用户进一步确认重开后自定义窗口仍显示 Dev／Kcp／Direct／Normal；结合 KCP Active 截图、正常退出 0 以及 49 项 Profile／设置字节一致，本轮现有配置持久化检查通过。用户确认与独立文件结果保存在 `restart-check/ui-confirmation.json`。当前 PID 53588 的自定义构建前设置、Profile、相关源码哈希和原生结果指针已留存至 `custom-manual/`；下一步由用户刷新只读计划、关闭清缓存及运行选项，向独立 Custom 目录点击一次“按此计划构建”。自定义构建尚未执行，原生 Inspector 展示缺口仍单列。

**自定义按钮已执行一次，交付成功但存在布局异常**：BuildId `20260924T083722193Z-933b24a4`；16 项包检查和内部六阶段核验通过，三个入口的有效配置／contentHash／实际 DLL 能力一致。日志先报告发布成功，随后出现 `EndLayoutGroup: BeginLayoutGroup must be called first.`。当前自定义窗口在 OnGUI 内同步构建，原生按钮则经 delayCall 调度；前者是有依据的排查方向，尚未通过修复验证根因。问题和最小调度修复建议在 `custom-manual/layout-issue.md`，三入口比较在 `three-entry-comparison.json`。本轮不重试、不修产品代码；当前只待用户正常关闭 Editor 后补核对退出码和退出后设置。包一致性通过与窗口缺陷分别记录，不能宣称界面无缺陷或 Build Profile 全计划完成。

## 14. 三入口本轮收尾（2026-09-24）

用户已正常关闭最后一个 Editor。PID 53588 于 **08:44:59.7545403 UTC 退出 0**；退出后 ProjectSettings.asset 恢复为 A，49 项 Profile／设置的数量和 SHA 均与预览前一致，核查时无 Unity／Player 进程。自定义生命周期已补入真实退出快照，退出前报告保留，最终三入口比较在档案 `three-entry-comparison.json`，摘要在 `RESULTS.md`。

- **本轮完成**：两次真实按钮各构建一次并成功交付；与既有 CLI 的业务配置、场景、原生选项、受管理符号、contentHash 及实际 DLL 编译能力一致。各次 BuildId、包路径和指针独立归档。预览只读、重开后的现有配置持久化、六阶段及两次按钮进程退出后设置恢复均已核验。
- **保留问题**：自定义窗口在成功发布后出现一次 EndLayoutGroup 错误，尚未修复；业务参数在原生 Profile Inspector 的展示尚未实现。不得把包一致性通过扩大为界面无缺陷或整个原计划完成。
- **下一项最小工作建议**：先单独处理自定义窗口在 OnGUI 中同步构建的调度问题，保留点击时请求和实际执行时校验，做针对性的真实按钮回归；根因与修复效果须以结果确认。原生 Inspector 展示另列界面接入任务，避免混入本轮已归档的包比较。
- **后续原计划仍保留**：成功包的真实 Player 身份／篡改拒绝、代表 Profile、非空 TMP／必要故障对照、旧入口迁移收尾；Shipping 干净提交和 Steam 双账号验收仍单列。本轮没有启动 Player、追加 CLI／全套测试、修改产品源码、提交、reset 或 clean。

详细结果见 [验收记录](build-profiles-validation.md#本轮最终收尾结论)，现有及并发改动继续保留。

## 15. 自定义窗口调度最小修复（2026-09-24，回归及退出核验完成）

用户要求处理布局错误。仍在原主工作区 `master / 4e473e28`，暂存区为空，保留已有及其他任务改动。新档案为 `Logs/BuildProfilesResume/layout-dispatch-20260924-165242-984-2128ffae`，冻结 108 项相关输入和继承差异。

- **已实现**：仅修改 `ProjectBuildWindow.cs`，按钮不再在 OnGUI 内同步构建。点击时捕获 Profile、输出、清缓存／运行选项和预览摘要，通过 `EditorApplication.delayCall` 在绘制事件结束后调用原服务；待执行／执行期间禁止窗口重复操作，OnDisable 撤销未执行回调，回调检查自身仍有效，完成／失败后释放状态并重绘。
- **保持原有校验**：共享服务仍在实际执行时校验忙碌状态、活动 Profile、未保存输入、符号和预览摘要；没有修改公共接口、BuildInfo、摘要、恢复或发布规则。原生 Inspector 展示问题本轮不处理。
- **本轮实际验证**：Unity 6000.3.21f1 重新编译后，现有完整 EditorTools **46/46，0 跳过，退出 0**；BuildIdentity 23、BuildRecipe 15、ProjectTool 8。PID 55612，08:53:09.7255679–08:53:55.5226663 UTC。相关输入只有窗口源码变化，设置前后 SHA 均为 A。
- **仍待确认**：上述测试不包含真实窗口构建，不能证明布局错误已消失，也不把未单独执行的取消／重复点击行为写为实测通过。可见 Editor PID 65012 已启动，窗口操作继续由用户执行；下一步仅自定义按钮回归一次，输出 `Builds/ProfileValidation/KcpLayoutFix-20260924-165242-984-2128ffae/MonsterSupergroup.exe`，不清缓存、不运行 Player。完成后核对无 EndLayoutGroup 错误、有效包、六阶段及正常退出。

**真实按钮回归结果（更新以上待确认状态）**：用户完成一次自定义按钮构建并报告无报错，BuildId `20260924T090326117Z-7d442108`。独立日志核验确认成功调用栈为 `QueueBuild → Internal_CallDelayFunctions`，不含 OnGUI；截至 09:14:35 UTC 的完整日志中布局错误和 C# 编译错误均为 **0**。本轮场景中调度修复有效，未重复构建。

实际包的 **16 项核对全部通过**；Dev／Kcp／Direct／Normal、初始计划、BuildInfo、完成标记、归档指针及实际 DLL 能力一致。与既有 CLI 除 inputHash／inputFiles 外所有计划字段一致。内部六阶段全部 Captured，长度／SHA／时间顺序／首尾计划输入摘要独立复算通过；唯一设置变化仍是 `preloadedAssets` 增加 GUID `99f9c9493070a9d4c979a8fec7c5a8d3`，不要求首尾摘要相等。证据在本轮档案 `button-regression/`。

**仍保留的边界**：完整日志另有 TMP Fallback 导入不一致提示，以及发布成功之后的 `Assets/AddressableAssetsData/link.xml` 导入版本错误；它们不计为布局复发，也未因用户未看到报错而省略，本轮不扩展修复。Editor PID 65012 尚未正常退出，退出码与退出后设置恢复待补；没有启动 Player。下一步只正常关闭 Editor、归档退出证据，不再构建。

**最终收尾**：用户正常关闭 Editor，PID 65012 于 **09:16:44.5880766 UTC（北京时间 17:16:44）退出 0**。退出后设置恢复为 A，当前正式文件与退出快照相同；六阶段加退出快照完整记录 A → B → A。最终日志布局错误仍为 **0**，受测窗口源码未变，成功指针仍指向本次包。相对测试后冻结的 108 项相关文件，退出后仅两份进度文档发生变化。最终日志、退出码、哈希和独立差异在本轮档案 `button-regression/`，退出前报告已保留。

本次调度修复已完成 **46/46 测试、一次真实按钮构建、16/16 包核验、六阶段及正常退出核验**，在本次场景中未再复现布局错误；没有扩大为所有 GUI 分支均已验证。原生 Inspector 展示仍未实现，TMP／Addressables 导入问题单列。无需继续重试或追查历史替换拒绝；下一项可单独处理原生 Inspector 业务字段展示，其余 Player／代表 Profile／事务恢复／迁移收尾维持原计划。本次不再启动 Unity、构建或 Player，没有提交、reset 或 clean。

## 16. Profile 资产 Inspector 业务参数接入（2026-09-24，功能验收与收尾完成）

用户确认展示位置是 **Project 窗口选中 Profile 后的普通 Inspector**，并允许直接编辑。Unity 6000.3.21f1 的公开 `Editor.finishedDefaultHeaderGUI` 回调已在本机程序集及官方源码核对；原生 Build Profiles 窗口的右侧内置面板没有公开设置注册接口，本项不替换或注入该面板。

实施基线已更新为原主工作区 `master / fb28b76e9588e01006debe0e4f8277f94634fa2d`，暂存区为空；另外两份诊断文档的既有改动保留。档案 `Logs/BuildProfilesResume/profile-inspector-20260924-180837-627-460e4ef4` 留存继承差异、输入副本及哈希。

- **已实现**：新增普通 Inspector 业务区，直接编辑原有 MonsterBuildSettings 子资产；复用既有字段绘制，SchemaVersion 只读。保存 Profile 延后至绘制结束，仅保存当前资产；执行前重新校验目标和忙碌状态。多选、缺组件、schema 不支持均提示，忙碌时禁用编辑；不自动应用宏、激活或构建。不修改公共 API、数据 schema 或构建服务。
- **本轮实际验证**：修改后 Unity 编译及完整 EditorTools **46/46，0 跳过，退出 0**；BuildIdentity 23、BuildRecipe 15、ProjectTool 8。PID 51008，10:10:23.0533522–10:11:04.5945642 UTC；正式设置和模板未变。这是编译及规则回归，不能代替真实 Inspector 验收。
- **等待用户界面验收**：可见 Editor PID 59360 已打开，没有强制活动 Profile 参数。先完成 KCP → Test-Steam → KCP 的只读查看，再用模板目录外的 `Assets/BuildProfileInspectorValidation-460e4ef4/Kcp-Inspector-Edit.asset` 检查 Network 修改、Undo／Redo、保存及重开恢复。另一个临时 Profile 仅用于缺组件提示；两份均不会用于构建。
- **停止边界**：不打包、不运行 Player、不追加故障矩阵；界面由用户操作。真实显示、编辑、撤销、持久化及临时副本清理完成前，本项标记部分完成。Build Profiles 内置面板仍未接入，不能与普通 Inspector 混为一项。

**界面显示已确认，查看流程需重新核对**：用户报告显示正常，随后确认曾在正式 KCP 上修改用途、点击“保存 Profile”、改回用途并再次保存。文件独立比较显示用途／业务字段和宏列表均已恢复，仅 `m_HasScriptingDefines` 从 0 变成 1。Unity 6000.3.21f1 的 ValidateDataConsistency 会在非空符号列表存在时补齐此标记，与本次显式保存结果一致；不修改代码、不自动还原用户保存。此次不能作为“仅查看不写入”的验收，原证据保留，并以用户保存后的 49 项文件新建 `preview-recheck-before.json`。接下来仅浏览复核，再在临时副本上完成 Undo／Redo、保存及重开检查。

**只读复核与编辑保存已完成**：用户确认补做的 1–4 项均正常：只读浏览与 KCP Active、多选／缺组件提示、临时副本 Network 的 Undo／Redo／保存，以及自定义窗口读取相同值。10:31:44 UTC 独立核对确认 49 项正式文件相对用户保存后基线没有变化，受测源码没有变化；临时副本业务字段仅 Network 从 Kcp 改 Steam，原有符号数组不变。副本显式保存同时将原生 `m_HasScriptingDefines` 从 0 持久化为 1；其他临时文件均未变。完整日志布局／编译／保存错误均为 0，证据在本轮档案 `edit-check/`。尚待正常关闭并重开核对持久化、区域无重复和 KCP Active，之后清理本轮临时副本，不执行构建。

**关闭等待状态**：用户报告已关闭窗口，但本轮 Editor PID 59360 仍存活，退出码尚未取得。日志最后更新于 10:34:16 UTC，已出现 Input System Shutdown；不能将其当作进程正常退出。临时副本保存值和正式 ProjectSettings 均保持不变；期间 Sandbox Profile 的 `m_HasScriptingDefines` 另从 0 变为 1，其余内容未变，保存触发原因未独立确认，已归档并保留。当前不启动第二个同项目 Editor、不强制终止、不清理临时副本，等待确认是否存在退出提示及取得真实退出结果；重开持久化仍未完成。

**已授权异常恢复并重开，界面待确认**：等待另一构建测试任务结束后，用户明确授权结束本轮残留进程并继续重开。已核对 PID、创建时间及父子关系，仅结束 Editor 59360 和导入 worker 46252／25180；Editor 于 10:49:55.9277440 UTC 退出 **-1**，本次正常退出未通过，不推断关闭停滞原因。11:00:23 UTC 核对：46 项受测源码未变，已保存临时副本和正式设置与退出快照一致，49 项正式文件相对上次基线仅保留已知 Sandbox 原生标记变化。证据为 `inspector-manual/forced-recovery.json`、`execution.json`、`post-force-audit.json`。

已于 11:00:28.5779007 UTC 使用 `open-editor.ps1 -Label inspector-restart` 重开可见 Editor **PID 55716**，未指定活动 Profile；新日志及进程记录位于 `inspector-restart/`。下一步仅由用户确认临时副本仍为 Dev／Steam／Direct／Normal、业务区域仅一处且原生设置可见、KCP 仍 Active。随后核对文件并正常关闭、清理本轮临时资产；本项在这些检查结束前仍为部分完成，不追加构建或测试矩阵。

**重开后的功能检查通过**：用户确认保存值仍为 Dev／Steam／Direct／Normal，业务区域仅一处、原生设置可见，正式 KCP 仍 Active。11:09:01 UTC 独立复核：49 项正式文件相对重开前基线不变，46 项受测源码不变，临时副本及 meta 均与预期保存版本一致；重开日志布局错误和 C# 编译错误均为 0。证据在 `inspector-restart/confirmed/`。本项已完成显示、只读浏览、编辑、Undo／Redo、保存及异常恢复后的持久化验证；首次异常退出仍保留为未正常退出，不能改写。当前仅待用户正常关闭 PID 55716、核对退出结果并清理已归档的本轮临时副本。

**最终收尾完成**：用户正常关闭重开后的 Editor；PID 55716 于 **11:13:31.6227111 UTC 退出 0**。退出后正式设置与重开前相同，49 项正式配置及 46 项受测源码均无新增变化；完整重开日志的布局错误、C# 编译错误均为 0。两份临时 Profile、各自 meta 及目录 meta 已逐项核对归档，并于 11:20:33 UTC 仅删除本轮临时目录内的四个文件、空目录和对应目录 meta，未清理其他输入。证据与结果汇总为本轮档案 `final/exit-audit.json`、`final/cleanup.json`、`RESULTS.md`。

Inspector 展示、编辑、Undo／Redo、保存和重开恢复功能已通过；使用指南已区分普通资产 Inspector 与未接入的 Build Profiles 内置面板。**保留限制**：第一次 Editor 关闭停滞原因未确认，实际恢复经过用户授权的强制结束；后一次正常退出 0 不补写为“首次正常关闭再重开”通过。正式 KCP 与 Sandbox 各自仅保留 `m_HasScriptingDefines: 0 → 1` 的已归档变化，业务参数及宏数组没有变化；前者来自用户明确保存，后者保存触发未独立确定。

本项到此停止，不追加重开、构建或 Player。原计划的后续顺序仍为成功包的真实 Player 身份／篡改拒绝、代表 Profile、非空 TMP／必要事务恢复对照、旧入口迁移收尾；这些不是本轮已完成的验收，不自动执行。未提交、reset 或 clean，其他任务改动保留。

## 17. KCP Player 身份与单字段修改拒绝（2026-09-24，最小对照通过）

复用已成功的 KCP 包 **BuildId `20260924T090326117Z-7d442108`**。用户提供正常／修改副本的运行日志，实施者只做事后文件与日志核对，没有重新构建、启动 Player 或改动运行时实现。当前仍为原主工作区 `master / fb28b76e`，暂存区为空。档案为 `Logs/BuildProfilesResume/player-identity-20260924-204742-160-98fa4781`。

- **正常身份通过**：日志实际报告 Dev／Kcp、tools=True、evidence=False、development=True、version=0.0.1、同一 BuildId、`valid=True`；对应包路径和 BuildInfo 均匹配。日志含本地房间 ready 记录，但部分栈行交错，不将其扩展为完整多进程联机验收。
- **版本不一致拒绝通过**：独立副本 `KcpIdentity-Tampered` 的 BuildInfo 仅将 gameVersion 从 0.0.1 改为 0.0.2。日志报告实际 Player 仍为 0.0.1、`valid=False`、版本／构建配置不一致及无效身份显示。用户明确确认点击“创建本地主机”后出现无效包提示、未进入房间；本地拒绝分支不单独写日志，界面确认独立保存。
- **文件证据通过**：两份包各 482 个文件，全部 SHA 对比仅 BuildInfo 不同，并精确匹配一次版本字符串替换。原包 7 项历史归档关键文件均未变化，Profile 成功指针仍指向原包；测试副本没有发布为新成功结果。运行时相关 5 份源码与该包初始计划中的哈希一致。
- **边界明确**：手动运行未由观察器取得 PID、准确命令、起止时间或退出码，不从 Shutdown 日志推定退出 0。当前 Player 比较的是元数据与实际版本／编译能力，不重算安装包的 contentHash／inputHash；本次通过不等同于任意文件或哈希字段篡改均可被检测。

本项最小身份对照验收完成，原包和修改副本均保留，详细结果见 [Player 验收记录](build-profiles-validation.md#2026-09-24-kcp-player-身份与单字段修改拒绝)。下一项转到代表 Profile 的分批实测：先产品 Test，再 Profiler／Evidence，随后 Wisp／专项 Test，并核对相应宏切换与 Editor 行为。非空 TMP／必要事务恢复、旧入口迁移收尾、Shipping 干净提交及 Steam 双账号继续单列；不追加通用防篡改机制或故障矩阵。

## 18. Windows-Test-Steam 代表包（2026-09-24，构建与包核验通过，保留输入基线限制）

用户要求推进 Windows-Test-Steam，本轮仅通过现有 CLI 入口执行一次真实构建。原主工作区 `master / fb28b76e`，暂存区为空；档案 `Logs/BuildProfilesResume/test-steam-20260924-211326-666-7a238c6b` 冻结启动前 4171 项输入、Profile／设置和相关源码副本。构建工具相关 46 项源码与上次 Inspector 测试归档一致，**没有重复完整测试或将历史 46/46 计为本轮重跑**。

- **编译与交付通过**：Unity 6000.3.21f1、PID 60792，13:14:20.4286306–13:17:41.3199838 UTC，真实退出 **0**，未超时或强制结束。BuildId **`20260924T131532148Z-d824d3a7`**，实际包位于 `Builds/ProfileValidation/TestSteam-20260924-211326-666-7a238c6b/MonsterSupergroup-v0.0.1-test-20260924T131532148Z-d824d3a7/`。
- **19 项包核对通过**：Test／Steam／Steam／Normal、版本 0.0.1、Development 关闭、LZ4HC、Boot→MainMenu→Gameplay；实际 DLL 为 Test／Steam，工具和取证能力均为 false，无测试程序集。Steam 原生库存在，steam_appid.txt 和取证清单不存在；EXE、BuildInfo、初始计划、完成标记及 Profile 指针一致。KCP 原指针未变化。
- **宏与生命周期证据通过**：Editor 缓存从 KCP Dev 宏切至 MONSTER_BUILD_TEST；实际 Editor／Player 编译响应均确认该业务宏，Player 没有 DEVELOPMENT_BUILD／UNITY_INCLUDE_TESTS。六阶段完整，长度、SHA、时间顺序及初始／最终计划对应关系核对通过；仍仅出现已知 preloadedAssets 条目，退出后恢复，不以摘要前后相等作为发布条件。
- **输入基线限制**：启动期间 `CombatEvidenceIntegrityTests.cs` 在 13:15:25 UTC 变化，初始计划（13:15:31 UTC）与最终计划均记录修改后的同一 SHA；它属于 Editor 测试程序集，未进入 Player。构建退出后的 13:20:53 UTC，另有 CombatEvidence.cs／CombatEvidenceRuntime.cs 并发改动，当前工作区已不同于包内计划。原样保留，不把这次结果当作启动前全输入冻结验收或后续源码已经验证，不自动追加构建。

日志 C# 编译错误及布局错误均为 0；保留两条已知 TMP Fallback 导入不一致、启动许可握手／访问令牌更新错误，不泛称所有日志无错误。实际成功及退出 0 与这些记录分开报告。本轮未启动 Player、上传 Steam、改为 Direct 或添加本地 AppID 文件；Steam 启动／双账号体验及交互式 Editor 运行行为仍待相应实测。后续先在并发改动稳定后重新建立输入基线，再分批推进 Profiler／Evidence，不借用本包验证尚未纳入的代码。详细命令和结果见 [本轮验收记录](build-profiles-validation.md#2026-09-24-windows-test-steam-代表包构建)。

**人工运行验收补充（2026-09-24）**：用户随后确认前述 Steam 双账号验收“已完成验收”，本项记为**人工验收通过（用户确认）**。本机 Player 日志独立确认相同 BuildId `20260924T131532148Z-d824d3a7`、Test／Steam、有效身份、Steam AppID 4886160 初始化成功及建房记录。双端入房、游戏同步、退出重入的结果来自用户确认；未取得对端日志、双端完整时序或真实进程退出码，不将人工通过写成双端日志独立核验通过。档案为 `Logs/BuildProfilesResume/test-steam-manual-20260924-214701-c6b9ddf9`，包含用户确认、原始本机日志及 SHA。本轮没有追加构建、启动 Player 或修改产品源码；原有并发输入限制仍适用。Windows-Test-Steam 的构建／包核对／生命周期与本次人工运行验收到此完成；后续先为当前源码建立新基线，再推进 Windows-Test-Profiler，其后 Windows-Test-Evidence，不自动开始下一次构建。
