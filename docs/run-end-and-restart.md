# 全员倒地、回大厅与重新开始

所有在线成员的倒地状态经服务端确认后，本局结束。掉线成员的存活存档不阻止结束；尚未完成恢复的在线角色不当作倒地。

房主选择“回到大厅”会保留当前连接和队伍，返回准备页，所有人重新准备。“重新开始”沿用本局开局的角色、武器、地图和难度，从初始状态加载新局。客户端等待房主，也可以确认离开自己的会话。Esc 无法关闭结束页或恢复旧局。

## 人工核验

1. 使用相同版本的验收包打开 Host 与两个客户端，分别选择初始武器，准备并开局。Steam 测试需真实账号进入同一大厅。
2. 从 Player Debug 记录各人的 ParticipantId、Avatar netId、生命、等级与武器。让角色在战斗中被怪物击倒；在仍有一人存活时，确认没有结束页，最后一人倒地后，三个窗口都应显示“游戏结束”。
3. 在 ESC 菜单、选项或选卡显示时触发战败，确认结束页接管焦点。客户端看见“等待房主选择下一步”；Esc 不返回战斗。“离开房间”的确认框默认选择取消。
4. 房主点击“重新开始”，检查加载结束后 ParticipantId 保持不变、Avatar netId 更换，生命与成长恢复新开局状态，初始武器与原选择一致。至少连续重开三次，实际移动、攻击、冲刺并获得经验。
5. 战败后选择“回到大厅”，确认仍在原 Steam 大厅，队伍和个人选择保留，所有准备取消。修改武器、重新准备并开局。
6. 一名存活客户端离开，剩余成员全部倒地，确认立即结束；掉线成员的存档不能使本局恢复。
7. 结束页等待期间让原成员重连，确认只进入结束页，不生成战斗角色。转换或加载时加入应给出提示。
8. 测试客户端离开和房主退出：客户端离开不关闭其他人的会话；房主退出后其他人回首页。之后可以重新建房。
9. 在 1280×720 与 1920×1080 下检查按钮、中文/英文、键盘导航、结束页和返回准备页。开发包与非开发包均执行一次。

Steam 的大厅保留、真实邀请和重连结果必须使用真实账号核验，KCP 自动化不能代替该项。

## 可重复自动化

```powershell
./Tools/Run-RunEndValidation.ps1 -Scenario party
./Tools/Run-RunEndValidation.ps1 -Scenario solo
./Tools/Run-RunEndValidation.ps1 -Executable Builds/MenuRelease/MonsterSupergroup.exe -Scenario party
./Tools/Run-RunEndValidation.ps1 -Executable Builds/MenuRelease/MonsterSupergroup.exe -Scenario solo -Visible -Width 1920 -Height 1080
```

验收脚本通过专用测试启动参数运行，不会自动向 Steam 好友发送邀请。三进程检查每一局新模拟者实际发送且服务端实际接受的移动快照，以及武器攻击被服务端接受；单人检查六种初始武器。脚本使用测试实例的伤害入口触发倒地，结束和换局使用正式接口与界面按钮。

结果、逐阶段日志与非无图形运行时的截图保存在 `Logs/PreparationMenu/<时间>-run-end*`。日志前缀 `[RunEnd]` 包含旧局结束、新局 RunId、局次、操作和成员数量；`[MenuProcess]` 记录验收阶段、身份、Avatar、Epoch 和实际接收快照数。

## 运行边界

- 游戏结束后停止战斗状态提交与推进，保留网络和界面运行，不修改全局时间倍率。
- 旧局次、旧 Avatar、旧 Epoch 的消息不会恢复旧状态；清理完成确认与新局加载确认分别处理。
- 清理和加载分别最多等待 120 秒。加载阶段成员断线或流程失败时，说明原因并清理会话返回首页。
- 仅当前在线队伍进入下一局，旧断线存档全部作废。身份编号在同一会话内不会分配给另一人。
- 协议版本随新消息格式升级，所有成员应使用同一批验收包。

## 本次验证结果

2026-09-12 本机验收。以下区分自动化结果与需要真实账号完成的 Steam 验收。

| 项目 | 结果 |
|---|---|
| 战败规则、操作权限、下一局状态与 Steam 阶段单元测试 | 7 / 7 通过 |
| NetworkCombat EditMode 回归（含上述 7 项） | 469 / 469 通过，`Logs/RunEnd-EditMode-Regression.xml` |
| Gameplay、准备房间、ESC 菜单、冲刺、波次 PlayMode 回归 | 53 / 53 通过，`Logs/RunEnd-PlayMode-Regression.xml` |
| Development 三进程及六种初始武器 | 最新重建包全部通过，记录见下方 |
| 非 Development 三进程及六种初始武器 | 通过；同一最终包连续两次三进程复测通过 |
| 1280×720 与 1920×1080 实际窗口 | 两种包均通过；检查结束页、按钮焦点及返回准备页，截图尺寸经过核对 |
| Steam 真实账号保留大厅、返回准备页及重开 | 待人工核验 |

逐场景结果：

| 场景 | 结果与证据 |
|---|---|
| 全员倒地、有人存活不结束、离线存活存档不阻止结束 | 单元测试及三进程通过 |
| 清理屏障、旧局清理确认、旧 Canonical 消息 | PlayMode 延迟并注入旧消息，旧消息不能释放屏障或恢复实体 |
| 连续三次重开 | 三进程通过；每局检查 RunId、Avatar、Epoch 更新及 ParticipantId、席位、连接保持 |
| 新局确实能战斗 | 每局由三个模拟端产生移动，服务端确认位置变化和新快照；三个玩家的武器攻击均有新的服务端接收记录；经验授予成功 |
| 成长重置 | PlayMode 首局实际领取升级后再战败，后续新局恢复初始 Build；进程测试验证 HP、等级、经验、装备、Perk 与待确认冲刺清空 |
| 返回同一准备房间 | 通过；Gameplay 已卸载，无 Avatar，保留武器选择，准备取消，随后可再开局 |
| 战败后重连 | Client A 在第二轮战败后重连；ParticipantId 不变，无 Avatar，不加载 Gameplay，随后进入新局 |
| 客户端伪造重开、重复/冲突操作 | 通过；客户端收到拒绝，房主相反的第二次请求不创建第二次转换 |
| 客户端离开确认 | 默认焦点为取消；取消仍保持连接；最终轮确认离开仅关闭自己的连接 |
| ESC 菜单接管 | 战败前打开菜单，战败后菜单关闭；Esc 无法恢复战斗 |
| 清理超时与再次建房 | PlayMode 通过；说明超时、关闭会话并可再次建房 |

最终非开发包记录：

- 构建：`Logs/RunEnd-Build-Release-Diagnostic2.log`，Success。
- 三进程：`Logs/PreparationMenu/20260912-200203-run-end`、`20260912-200254-run-end`，Host / A / B 全部 PASS。
- 六种武器和 1280×720：`Logs/PreparationMenu/20260912-200125-run-end-solo`，PASS。
- 六种武器和 1920×1080：`Logs/PreparationMenu/20260912-200203-run-end-solo`，PASS。

最终开发包记录：

- 构建：`Logs/RunEnd-Build-Development-Verified2.log`，Success。
- 三进程：`Logs/PreparationMenu/20260912-200533-run-end`，Host / A / B 全部 PASS。
- 六种武器和 1280×720：`Logs/PreparationMenu/20260912-200533-run-end-solo`，PASS。
- 六种武器和 1920×1080：`Logs/PreparationMenu/20260912-200605-run-end-solo`，PASS。

机器可读汇总与验收包核心程序集校验值见 `Logs/RunEnd-Validation-Summary.json`。

中间构建记录也保留：`20260912-195544-run-end` 与 `20260912-195630-run-end-solo` 曾在首次加载超时；加入逐项加载诊断并重新构建后的上述非开发包未复现。尚不能单凭重建后通过认定原超时根因，人工核验如再次遇到加载停留，请保留完整日志。验收脚本现在每 5 秒记录尚未就绪的角色基线及开局检查结果。

验收包位于 `Builds/MenuDevelopment`、`Builds/MenuRelease`，启动 `MonsterSupergroup.exe`；复制给其他测试机时保留整个目录中的运行依赖。专用测试入口仅在提供测试启动参数时运行，正常双击从首页开始。
