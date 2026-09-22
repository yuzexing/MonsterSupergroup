# 统一构建配置验收记录

日期：2026-09-22。本轮只验证构建与能力边界；没有使用 Computer Use，没有重复完整 Limbo 流程，Steam 测试分支及画面由人工核验。

## 已实现

- 1 个产品配方 + 6 个专项配方；34 个旧别名与仍保留的 sandbox/nordic 共覆盖原 36 个 ID。
- 日常默认 product / Test / Steam / Steam 分发 / Normal。版本更新独立确认，构建不递增。
- 普通 Test 无 TOOLS/EVIDENCE，Development 覆盖不授予作弊能力；故障取证仅产品 Test；产品 KCP 仅 Dev。正式单人游戏仍走既有内部 Host 流程。
- Shipping 只允许产品 Steam 分发，检查干净 Git，并在结束前再次核对源码状态。当前工作区有未提交改动，真实 Shipping 请求已明确拒绝，未生成伪成功包。
- 所有实际构建均使用唯一目录；失败前失效旧选择指针，成功后才原子发布。普通构建在统一服务内核对生产资源前后摘要，旧维护混合入口已拆开。
- SteamAppID 文件由分发方式决定；普通包不生成源码回放归档或取证清单。

## 自动化结果

| 检查 | 实际结果 | 记录 |
|---|---|---|
| 初次编译 | 被其他正在实施的 CombatEvidence 分部方法临时缺失阻断；未改写那部分实现 | `Logs/BuildUnification/editmode.log` |
| 第一轮有效定向检查 | 83 / 83 通过 | `editmode-r2.xml` |
| 构建、版本、联机兼容相关集成 | 127 / 127 通过 | `editmode-final.xml` |
| 失败指针与最终源码校验加固后定向复验 | 29 / 29 通过 | `hardening.xml` |
| PowerShell 参数／选择／进程识别 | 35 项通过；92 个脚本语法通过 | `Logs/ProjectTools/BuildResolution-20260922-184948-121/result.json` |
| 便携 Limbo / 冻结包归档脚本 | 通过旧文件名兼容、新实际 EXE 名、辅助隔离及归档检查 | 对应 Tools/Tests 脚本记录 |

这些测试集存在重叠，不将计数相加作为全项目总数。没有宣称完整 EditMode/PlayMode 广域回归重新通过。

实际调用发现并修复两项脚本问题：省略 Development 等可选参数时不能回写到带 ValidateSet 的参数变量；Unity 工程进程必须按独立 projectPath 参数精确比对，不能因隔离副本或日志路径包含本工程字符串而误拒绝。

## 代表性包

以下为配置矩阵验证包；日志开关收尾后的最终交付见末节，旧包保留但不作为最新推荐。

| 用途 | BuildId | 结果 |
|---|---|---|
| 产品 Test / Steam 分发 / 普通日志 | `20260922T104758376Z-d975b96b` | 构建、包内容和归档通过；无 AppID 文件、测试程序集、TOOLS 或 EVIDENCE |
| 产品 Test / 本地直启 / 普通日志 | `20260922T105017943Z-18ecbbe0` | 构建通过；附正确 AppID，无测试程序集或辅助能力；真实 D3D11 Player 启动通过 |
| 产品 Test / 本地直启 / 故障取证 | `20260922T105611077Z-b556139e` | 构建、独立源码归档和取证清单通过；BuildId 一致，临时资源已清理；无测试程序集或 TOOLS |
| 鬼火专项 Dev / KCP / Direct | `20260922T105243346Z-68b861e8` | 构建通过；有测试能力，无 AppID；实际 Player 注入探针执行并正常退出 PASS |

前两个普通包游戏版本均为 `0.0.0`，BuildId 不同。Steam 分发候选包：

`Builds/Packages/MonsterSupergroup-v0.0.0-test-20260922T104758376Z-d975b96b.zip`

SHA-256：`EB857F889BEF4B2040DBDDB1B98B5E739F90466D67E83B175AAE86EA7176F0D6`

包内 `package-manifest.json` 记录全文件哈希，并保留原始 BuildInfo；不是只校验 EXE。

## Player 检查的范围

普通 Test 真实 Player 使用 D3D11，日志确认 Steam Interactive 默认后端、Steam 初始化、版本 UI 文本与快照一致。故意传入 Limbo、Boot/Menu 测试及取证参数，未启动对应辅助。这里只做启动检查，随后受控停止进程，不把它写为自然退出或游戏流程验收。

鬼火专项使用无图形、专用服分支的 opt-in 探针，确认独立构建钩子确实随包执行；没有重复鬼火画面、音效或战斗验收。

普通主菜单保留一条 FMOD 尚无游戏监听器的提示；本轮未调整音频或进入战斗，不将此提示解释成新的声音功能结论。

## 待人工确认与范围

- 将普通 Steam 分发包上传现有测试分支，两账号更新后从 Steam 启动；确认菜单、版本显示及正常联机。未执行上传。
- 干净提交后的 Shipping 成功构建待发行时执行；本轮已验证非法组合与脏工作区拒绝。
- 构建期间工作区包含其他任务的并行代码改动；原始包和历史失败记录均保留。
- 配方收敛不代表全部游戏功能、本轮未运行的专项用例或玩法压力已经重新验收。

使用方法及全部旧 ID 对照见 [统一打包指南](build-guide.md)。

汇总机器记录：`Logs/BuildUnification/verification-summary.json`。取证包本轮只核对编译能力与包内／本地归档，没有新增战斗采集验收。

## 最终交付（普通日志收尾后）

最后审计发现旧 DBL 默认高频输出不受构建能力约束。已接入现有 TOOLS 能力：普通 Test、取证 Test、Shipping 不输出这类状态／XP 调试文本，警告与错误保留；Dev／专项仍可按既有开关使用。没有改动战斗行为。

- 本地直启 Test：`20260922T110059104Z-b09948ec`。
- **Steam 分发 Test（最终交付）：`20260922T110310638Z-c9702186`**。
- 两包均为 `0.0.0`，Development=false，无测试程序集、TOOLS 或 EVIDENCE；实际玩法程序集哈希一致。
- 新直启 Player 的 D3D11 启动、Steam 默认后端、版本 UI 文本及误传辅助参数隔离再次通过；原普通 Controllers 调试文本不再出现。进程在启动核对后受控停止，不称为完整游戏或自然退出验收。
- 该次变更只补编译与受影响的真实 Player 启动检查，没有重复广域测试、专项画面或长局。

最终包路径：`Builds/Packages/MonsterSupergroup-v0.0.0-test-20260922T110310638Z-c9702186.zip`。

最终 ZIP SHA-256：`45C0245C4F793A5E847E71292EFB8D76F2117B1B7115E424A3D313146F8A6133`。包内全文件清单见同名目录的 `package-manifest.json`。
