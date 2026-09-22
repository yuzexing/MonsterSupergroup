# 如何打包 MonsterSupergroup

2026-09-22：打包配置已收敛为一个产品配方和六个专项配方。**平时给朋友测试，打开「MonsterSupergroup → 构建与验收 → 构建配置」，使用「日常打包」默认的 Test / Steam / Steam 分发 / 普通日志。** 不需要选择敌人、武器或历史 Phase 名称。

## 我该选什么

| 目的 | 页面／配方 | 类型 | 网络／分发 | 说明 |
|---|---|---|---|---|
| 上传 Steam 测试分支，正常游玩 | 日常打包／产品 | Test | Steam／Steam 分发 | 默认；无作弊、调试面板和机制验证入口 |
| 收集 Steam 联机故障 | 日常打包／产品，开启故障取证 | Test | Steam／Steam 分发 | 增加战斗证据，不开放作弊；不是普通试玩默认项 |
| 开发调试 | 日常打包／产品 | Dev | Steam 或 KCP／按需求选择 | 开发入口；Development 默认开启 |
| 本机直接运行 Steam 开发包 | 日常打包／产品 | Dev 或 Test | Steam／本地直接分发 | 包含经过校验的 steam_appid.txt；仍需要登录 Steam |
| 正式发行 | 日常打包／产品 | Shipping | Steam／Steam 分发 | 提交版本、代码和资源改动后构建；禁止专项配方、KCP、取证及脏工作区 |
| 执行程序机制用例 | 专项验收／对应配方 | Dev 或 Test | 通常 KCP／本地直接分发 | 包含明确测试能力；不能用于正式发行 |

构建类型、Unity 的 Development Build、网络和分发方式分别管理。Test 即使在高级选项打开 Development，也不会因此获得普通产品包不允许的作弊能力。Shipping 强制关闭 Development。

产品配方的 KCP 只允许 Dev；普通产品 Test 使用 Steam。专项 Dev／Test 仍可使用 KCP。故障取证只允许产品 Test，拒绝 Dev＋取证，避免把开发辅助混入故障记录。

Steam 分发包不携带 `steam_appid.txt`，应上传 Steam 后从 Steam 启动。KCP 包也不携带该文件。不能再用“是否 Development”猜测应该怎样启动。

## 一次正常打包

1. 如需升级游戏版本，在版本区域选择普通小更新／重大更新／大版本更新，检查预览，再点击「应用版本更新」。无需改版本则跳过。
2. 在「日常打包」选择 Test，保持 Steam、Steam 分发和普通日志。
3. 检查构建摘要中的版本、类型、网络、分发、Development、场景及调试／取证能力。
4. 点击构建，只使用成功结果给出的目录。每次构建都会新建唯一目录，不覆盖上一次包。
5. 将该目录交给现有 Steam 上传流程；工具不会自动上传。两端从 Steam 更新，核对右下角版本和 BuildId。

游戏版本只来自 `PlayerSettings.bundleVersion`；普通构建不递增。`1.2.3` 的三个更新选项分别得到 `1.2.4`、`1.3.0`、`2.0.0`。同版本连续构建，版本不变、BuildId 不同。

右下角显示 `v0.0.0-test · <BuildId>`。包内 BuildInfo、启动日志、成功标记和归档使用同一标识。修改编辑器配置不会改变已有包。联机版本检查不比较 BuildId／后缀，但协议与资源指纹仍必须兼容。

## 专项验收和资产维护

| 配方 ID | 用途 | 必须保留的区别 |
|---|---|---|
| `gameplay-validation` | 通用 Gameplay 验收 | Boot → MainMenu → Gameplay；敌人、武器、波次等在运行时选择用例，包含 Nordic 地图校验 |
| `wisp-validation` | 鬼火表现验收 | 独立构建时场景注入；不能用普通包替代 |
| `options-validation` | 设置验收 | 专用设置验证能力 |
| `handoff-validation` | 模拟权交接验收 | 专用交接验证能力 |
| `sandbox` | 网络战斗沙盒 | 独立 Sandbox 场景；不是正式关卡 |
| `nordic` | Nordic 静态样例 | 独立静态样例场景；不是产品的 Nordic Gameplay |

专项配方只能用 Dev／Test；其 Test 是**有测试能力的验收包**，与日常产品 Test 分开。Sandbox／Nordic 不需要测试程序集的差异仍保留。Wisp 的注入仅发生在对应构建场景副本。

「创建／迁移／修复」是维护操作，可能修改资源；「构建」只校验并打包已有资源。缺包时不要重新执行 CreateAndBuild 来尝试修复。若资源校验失败，先阅读失败原因，再选择明确的维护操作，不让打包自动覆盖 Prefab 或场景。

Limbo 的 `opening`、`full`、`fixture` 是运行用例，不是 Dev／Test／Shipping 构建类型。程序机制脚本默认使用通用 Gameplay 验收包；如需不含测试程序集的便携 Limbo 参考包，显式构建 **产品 Dev／KCP／本地直接分发**，再调用现有便携包装工具。普通 Test／Shipping 不通过启动参数解锁 Limbo 机制入口。

## 命令行与成功结果

在工程根目录执行；首次指定 `-Unity`，或设置 `UNITY_EDITOR_PATH` 指向项目匹配版本的 Unity。

```powershell
# 普通 Steam 试玩包；不需要历史 Profile 名称
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile product

# 故障取证：只有取证用途增加战斗证据与本地源码回放归档
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile product -BuildKind Test -Network Steam -Distribution Steam -Diagnostics Evidence

# 本地开发，使用 KCP
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile product -BuildKind Dev -Network Kcp -Distribution Direct

# 构建一次通用验收包，再分别选择测试用例
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile gameplay-validation
./Tools/Invoke-ProjectTool.ps1 -ToolId test.beam
./Tools/Invoke-ProjectTool.ps1 -ToolId test.local-room -Profile party

# Shipping：失败时按列出的原因处理，不会自动提交改动
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile product -BuildKind Shipping

# 用完整成功目录归档，身份取自包内快照
./Tools/Export-ProjectBuild.ps1 -BuildDirectory '构建成功结果给出的目录'
```

运行脚本不再猜测 `Builds/Phase02` 或历史日期目录。默认读取 `Library/ProjectTools/BuildResults/<配方>.json`，核对成功状态、实际 EXE、BuildInfo、成功标记及用例需要的能力／网络。构建开始即清除该配方旧指针，失败不会偷偷运行上次包；旧包本身仍保留。显式 `-Executable` 或 `-BuildDirectory` 可以选择冻结的历史包，责任与默认选择分开。

构建工具的结果 `artifacts[0]` 是本次实际 EXE，不能由 `-Output` 推测最终目录。`-Output` 指定文件名和基础位置，工具仍添加唯一构建目录。旧 `-UniqueOutput` 参数保留兼容，所有构建始终唯一。拒绝 ScriptsOnly 和绕过统一服务的构建。

## 旧配置去哪里了

原 36 个 ID 继续有明确去向；其中 `sandbox`、`nordic` 仍是正式配方，其余 34 个是兼容别名。调用旧别名会提示新配方。以下默认值用于保持旧调用用途；日常 UI 不再列出旧名称。

| 旧 ID | 新配方 | 默认类型／网络／分发／诊断 |
|---|---|---|
| `player-development`、`boot-process` | `product` | Dev／Steam／Direct／Normal |
| `player-release` | `product` | Shipping／Steam／Steam／Normal |
| `kcp-development` | `product` | Dev／Kcp／Direct／Normal |
| `steam-evidence` | `product` | Test／Steam／Steam／Evidence |
| `menu-development` | `gameplay-validation` | Dev／Steam／Direct／Normal |
| `menu-release` | `gameplay-validation` | Test／Steam／Direct／Normal |
| `player-debug-release`、`rewired-release` | `gameplay-validation` | Test／Kcp／Direct／Normal |
| `enemy-variants`、`imp`、`lust-sinner`、`enemy-hit-flash`、`camera`、`experience`、`waves`、`health-hud`、`modifier-selection`、`knockback`、`timeline-waves`、`player-debug-development`、`rewired-development`、`beam`、`circling`、`dash`、`melee`、`summon`、`ultimate`、`runtime-boundary` | `gameplay-validation` | Dev／Kcp／Direct／Normal |
| `nordic-gameplay` | `gameplay-validation` | Dev／Kcp／Direct／Normal，保留地图校验 |
| `wisp` | `wisp-validation` | Dev／Kcp／Direct／Normal，保留场景注入 |
| `options` | `options-validation` | Dev／Kcp／Direct／Normal |
| `enemy-handoff-development` | `handoff-validation` | Dev／Kcp／Direct／Normal |
| `enemy-handoff-release` | `handoff-validation` | Test／Kcp／Direct／Normal |
| `sandbox` | `sandbox` | Dev／Kcp／Direct／Normal |
| `nordic` | `nordic` | Dev／Kcp／Direct／Normal |

历史名称中的 `release` 不一定代表 Shipping，例如 `menu-release` 一直是程序验收用途。判断包用途以构建摘要和 BuildInfo 为准。

## 当前验证边界

本次只需检查受影响工具、能力矩阵和代表性真实包，不为 36 个旧 ID 分别构建。脚本默认路径的失败／身份／能力检查与便携 Limbo 文件名兼容测试已独立保存；实际 Player 和 Unity 测试结果由本轮交付报告记录。Steam 测试分支的双账号体验与最终画面由人工确认，不能以 KCP 或静态检查替代。

本轮实际构建、测试与待人工确认项见 [统一构建验收记录](build-unification-validation.md)。
