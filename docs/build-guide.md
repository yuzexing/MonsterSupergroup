# 使用原生 Build Profiles 构建 MonsterSupergroup

构建配置保存在 Unity 原生 Build Profile 及其 `MonsterBuildSettings` 子资产中。原生 Build、自定义窗口与命令行读取同一份配置。Unity 版本固定为 **6000.3.21f1**。

## 日常操作

1. 在 Project 窗口单击 `Assets/Settings/Build Profiles` 中的 Profile。普通 Inspector 标题下的「项目业务配置」可编辑用途、构建类型、网络、分发和诊断；SchemaVersion 只读，Unity 原生设置继续显示在下方。
2. 字段编辑支持 Undo／Redo；点击「保存 Profile」保存该 Profile 文件及其业务子资产。此按钮在绘制结束后保存，不执行全项目保存，也不应用符号或激活 Profile。多选、缺少业务组件或不支持的 schema 会显示提示；编辑器忙碌时禁用编辑和保存。
3. 打开「MonsterSupergroup → 构建与验收 → 构建配置」，选择同一个 Profile，查看场景、能力和符号差异，点击「应用受管理符号并保存」。该窗口与 Inspector 使用同一业务子资产；该操作保留插件及无关符号，移除旧用途的受管理符号。
4. 首次使用时显式激活 Profile，等待编译和资源导入完成。查看模板不会自动激活，也不会自动接着构建。
5. 刷新只读计划，点击构建；也可以使用原生窗口的 Build。只使用成功结果返回的实际目录。

这里的业务区域位于 **Profile 资产的普通 Inspector**，不在「File → Build Profiles」窗口右侧内置面板中。原生构建窗口的业务区域未接入；业务字段也仍可在项目「构建配置」窗口编辑。查看或编辑业务字段不会自动启动构建。

本功能的编译、真实编辑／撤销／保存及重开检查结果和退出限制，见 [Inspector 验收记录](build-profiles-validation.md#2026-09-24-profile-资产-inspector-业务参数接入)。

原生 Build and Run 与自定义窗口的「完成后运行」只支持 **Direct** 分发。Steam 分发需要通过现有 Steam 上传和启动流程，工具不自动上传，也不替换为其他 Profile。Direct 包完成资源恢复、身份和包校验后才启动实际 EXE；启动失败单独报告。

## 初始模板

模板位于 `Assets/Settings/Build Profiles`。全部为 Windows x64 Player，显式保存场景，默认继承项目的 Player／Graphics／Quality 设置。

| 模板 | 用途／类型 | 网络／分发／诊断 | Development |
|---|---|---|---|
| Windows-Dev-Kcp | 产品 Dev | Kcp／Direct／Normal | 开 |
| Windows-Test-Steam | 产品 Test | Steam／Steam／Normal | 关 |
| Windows-Test-Evidence | 产品 Test | Steam／Steam／Evidence | 关 |
| Windows-Test-Profiler | 产品 Test | Steam／Steam／Normal | 开，连接 Profiler |
| Windows-Shipping | 产品 Shipping | Steam／Steam／Normal | 关 |
| Windows-Dev-Gameplay | 通用 Gameplay 专项 Dev | Kcp／Direct／Normal | 开 |
| Windows-Dev-Wisp | 鬼火专项 Dev | Kcp／Direct／Normal | 开 |
| Windows-Dev-Options | 设置专项 Dev | Kcp／Direct／Normal | 开 |
| Windows-Dev-Handoff | 交接专项 Dev | Kcp／Direct／Normal | 开 |
| Windows-Dev-Sandbox | 网络沙盒 Dev | Kcp／Direct／Normal | 开 |
| Windows-Dev-Nordic | Nordic 静态样例 Dev | Kcp／Direct／Normal | 开 |

Dev 和 Profiler 使用 LZ4，其余产品模板使用 LZ4HC。深度分析、等待调试器和生成解决方案默认关闭。模板可以复制为自定义 Profile，例如专项 Test 或 Steam Direct；它们仍须满足同一套用途规则，不通过命令行覆盖业务选项。

产品 Test 无开发辅助和测试程序集，打开 Development 也不会获得这些能力。专项 Test 保留专项能力；Sandbox／Nordic 不包含测试程序集。Wisp 只在对应构建场景副本中注入探针。产品 KCP 只允许 Dev；Evidence 只允许产品 Test。Shipping 要求产品、Steam 网络、Steam 分发、Normal、非 Development 及干净 Git 工作区。

## 版本与配置来源

游戏版本只来自全局 Player Settings 的 `bundleVersion`。保留独立的版本更新按钮，普通构建不递增。存在 Player Settings 覆盖时，其版本必须与全局一致，否则拒绝。

平台、场景、Development、调试、分析、压缩和设置覆盖由原生 Profile 管理；业务子资产只保存 SchemaVersion、BuildKind、Network、Distribution、Diagnostics、PurposeId。测试程序集与编译符号由用途规则派生。

构建前必须保存场景、Profile 和有未保存状态的动态字体。预览只读；配置或输入在预览后变化时须刷新。Profile 符号与 Player Settings 符号叠加；Player Settings 不得残留本工具的构建专用符号。

## 命令行

在项目根目录执行，显式指定 Unity 或设置 `UNITY_EDITOR_PATH`：

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player `
  -BuildProfile 'Assets/Settings/Build Profiles/Windows-Test-Steam.asset' `
  -Unity 'D:/RealSoftware/6000.3.21f1/Editor/Unity.exe'

./Tools/Invoke-ProjectTool.ps1 -ToolId build.player `
  -BuildProfile 'Assets/Settings/Build Profiles/Windows-Dev-Kcp.asset' `
  -Output 'Builds/MyTest/Custom Player.exe' -CleanBuildCache -RunAfterBuild
```

底层启动参数为 `-activeBuildProfile <路径> -executeMethod MonsterSupergroup.EditorTools.NativeBuildEntry.Batch`；批处理方法不会临时切换 Profile。构建参数只有 Profile 路径、输出、清理缓存和完成后运行。`-Output` 是基础目录和文件名，仍会添加唯一构建目录，不能用它猜测最终 EXE；读取结果的 `artifacts[0]`。

旧 `-Profile product`、BuildKind／Development／Network／Distribution／Diagnostics、ScriptsOnly、UniqueOutput 均明确拒绝。非构建工具中的 Profile／Scenario 参数保持原义。

## 成功结果、失败恢复与历史包

构建先写入输出基础目录下的 `.staging`。外层流程执行所有清理，恢复 TMP 字体及图集，再检查正式资源、构建输入、BuildInfo、程序集能力和分发文件。成功后发布唯一目录及按 Profile GUID 保存的结果指针；异常或取消的产物隔离到 `.failed`，不发布成功结果。

包中包含 `build-plan.json`、`build-request.json`、`build-complete.json` 以及 StreamingAssets 中的 BuildInfo。新身份 schema 记录 Profile GUID／路径和配置／工程输入摘要。BuildId 每次生成，产品版本独立。

自动结果选择通过 `Tools/ProjectTools.psm1` 的 `Resolve-ProjectBuildExecutable -BuildProfile <路径>`，核对身份、成功标记及所需能力。旧 Recipe 指针不能继续自动选择；旧脚本需要显式历史 EXE／BuildDirectory，或等后续审查迁移。显式历史包仍可用 `Tools/Export-ProjectBuild.ps1 -BuildDirectory <目录>` 归档，不为旧包补写新身份。

当前产物编译能力检查读取 Mono Player 实际程序集；无法取得编译能力证明的包拒绝发布。Unity 升级后必须先验证原生序列化适配层。

Player 启动时还会将 BuildInfo 的版本、开发构建标记、构建类型及能力与实际 Player／编译配置比较；不一致时显示无效包并禁止联机，程序可以继续显示菜单。这里不重算整个安装包的 `contentHash`／`inputHash`，这两个字段用于构建计划与结果记录的一致性核对，不能视为运行时整包防篡改或数字签名。已有 KCP 单字段版本修改的[实际验收记录](build-profiles-validation.md#2026-09-24-kcp-player-身份与单字段修改拒绝)。

## 迁移与验证记录

`Window-dev` 和 `Window-test` 已移除。旧 JSON 配方和脚本保留用于审查，已退出构建配置来源；旧方法保留报错外壳，不做静默映射。详见 [旧入口审查清单](build-profiles-migration.md)。

本次验证结果见 [原生 Profile 验收记录](build-profiles-validation.md)。历史 [统一构建验收记录](build-unification-validation.md) 保留原日期，不代表本轮重新通过。Windows-Test-Steam 已完成构建／包检查及用户确认的双账号人工验收，具体包与证据范围见 [Steam 人工验收记录](build-profiles-validation.md#2026-09-24-windows-test-steam-双账号人工验收)；不将结果泛化为所有 Steam 包或后续源码均已验证。Shipping 成功构建仍需包含改造的干净提交。
