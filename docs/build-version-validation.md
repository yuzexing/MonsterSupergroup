# 构建版本与联机提示验证记录

日期：2026-09-22。当前工作区包含本次改动及另一路 Steam 邀请修复；已保留，未自动提交。

## 自动化结果

| 检查 | 实际结果 | 记录 |
|---|---|---|
| 最终定向 EditMode | 115 / 115，通过 | `Logs/BuildIdentity/editmode-final.xml` |
| 最终相关 PlayMode | 28 / 28，通过 | `Logs/BuildIdentity/playmode-final.xml` |
| 归档脚本 | 通过：冻结不可覆盖、哈希、快照一致、缺失快照拒绝、嵌套输出拒绝、带空格路径 | `Tools/Tests/Test-ProjectBuildArchive.ps1` |
| 旧 Limbo 便携入口 | 通过：辅助隔离、引号路径、观察入口及失败/不完整日志分类 | `Tools/Tests/Test-LimboManualPackage.ps1` |

覆盖三档递增、独立应用与旧值拒绝、非法版本/溢出、构建类型与网络分离、Shipping 配置限制、外部构建类型符号冲突、包内快照一致、版本数字比较、原协议检查、提示优先级、本地主动退出、旧连接通知拒绝、菜单/房间重新打开及当前 Steam 邀请清理路径。

这不是全项目广域回归。未重复完整 Limbo 长局。

## 保留的失败与修复

- 首轮编译在新 Steam 邀请测试缺少 Steamworks 程序集引用处中止；后续工作区已补齐引用，本轮在合并状态复验通过。
- `playmode-retry.xml` 为 27 / 28。失败在菜单已开始异步加载时，`EnsureMainMenu` 第二次调用提前返回，调用方找不到菜单。现已等待当前加载完成，原断言保留；最终同组 28 / 28。
- 新构建信息写入诊断日志时补齐记录类型字段；修复前编译记录留在 `import.log`。

## 实际包与进程验证

| 实际构建 | 游戏版本 | BuildId | Development | 用途 |
|---|---|---|---|---|
| Dev | 0.0.0 | `20260922T084101475Z-c37821f2` | true | `menu-development`，显式含测试程序集，后台联机回归 |
| Test | 0.0.0 | `20260922T084630455Z-4acb0dc1` | false | `player-development` 场景配置，测试程序集 0，交付候选 |

两次完整构建都成功；版本未递增，BuildId 不同。当前 HEAD 为 `44426c235a0e1b13592c82cf9c34fb2012a3adb5`，两包如实记录 dirty=true。构建结果与一致性记录：`build-dev-result.json`、`build-test-result.json`、`snapshot-checks.json`、`build-hashes.json`（均在 `Logs/BuildIdentity`）。

Shipping 实际执行已在构建前被拒绝：原因是工作区未提交，错误记录明确列出路径；`shipping-rejection.json` 的 success=false、artifacts 为空。未生成 Shipping 包，不宣称完成 Shipping Player 实测。ScriptsOnly 与冲突编译符号的拒绝由定向测试覆盖。

Dev 实际 Host + 两个 Client 的 `local-party` 场景全部 PASS，覆盖准备房、战斗、退出和重开。记录：`Logs/PreparationMenu/20260922-164605-local-party-p18312`。这是后台 KCP 验证，不是 Steam 双账号。

Test 实际 Player 使用 D3D11（RTX 4080）、1280×720 短时启动，版本 UI 组件实际创建的文字为 `v0.0.0-test · 20260922T084630455Z-4acb0dc1`，与包内快照和日志相同。通过既有 `pickup-observe/smoke` 入口进入 Gameplay、执行带标注的短时检查并正常清理，记录 `pickup-smoke-passed`。此运行有伤害/物品注入，不是玩法压力验收；未截图或操作桌面。记录：`Logs/BuildIdentity/player-smoke`。最终文字排版仍待人工确认。

上述 Player 退出日志仍有既有 ComputeBuffer 释放警告，未将它隐去，也未将本次结果描述为项目零警告。短时观察入口直接调用 StopHost，会记录 host_lost；正常玩家通过离开按钮的路径已在 PlayMode 和 local-party 中分别验证。

### 交付

- 包目录：`F:/UnityStore/Build Deliveries/MonsterSupergroup-v0.0.0-test-20260922T084630455Z-4acb0dc1`
- ZIP：同名 `.zip`；完整文件清单及逐文件 SHA256 在包内 `package-manifest.json`。
- ZIP SHA256：`1F34559AEA77A1658FC8D2B607B657E6A3920F02F5D76CC1789FCD71251A7FCF`。
- EXE 名称保持 `MonsterSupergroup.exe`；未改 Steam/KCP 默认选择。

此 Test 候选关闭 Development，按既有 Steam 打包规则不含 `steam_appid.txt`，真实 Steam 验收应从对应 Steam 安装/测试分支启动；未自动上传或发布。独立本地开发验证可用 Dev 构建，或显式既有 KCP 验证入口，不能据此宣称 Steam 验收通过。

包内 BuildInfo、成功标记及归档标识已核对。临时 `CombatEvidenceBuild.json` 与 meta 已清理；旧包未覆盖。

## 人工待确认

- 主菜单、准备房、Gameplay、结算右下角文字的最终排版。
- 两个真实 Steam 账号：较旧客户端、较旧房主、同版本不同 BuildId、协议/内容不一致、邀请/列表/重连入口。
- 房主主动退出、进程意外结束、客户端主动退出，以及提示后再次加入。

后台 KCP / 编辑器测试不替代真实 Steam 核验；本轮没有使用 Computer Use。Shipping 未在脏工作区强行制作，版本修改需正常提交后再发行构建。
