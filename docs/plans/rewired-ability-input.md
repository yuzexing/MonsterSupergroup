# Rewired Dash / Ultimate 开发验收入口

## 范围与参考

正式 Boot→Gameplay。保留 Owner、Mirror、GAS、Build、Dash 充能和 Ultimate 执行/恢复；F5 和选卡键不改。

参考已读取的 HellMaiden `ExportedProject/Assets/Scenes/Game Scenes/Systems.unity` 与 `PlayerController_HMD.cs`：

| 功能 | 参考 Action / 分类 | 参考键 | 正式 Boot 键 |
|---|---|---|---|
| Dash | `R_Trigger` 14 / Normal | Space | 左、右 Shift，按住持续尝试 |
| Ultimate | `Button2` 4 / Normal | Q | Q，仅按下瞬间 |
| 调试 | `DebugAction_2` 51 / Normal | 数字 2，回调为空 | F6，仅按下瞬间 |

2026-09-10 的首轮改动只增加这三个 Button Action 与键盘映射、分类 ID 列表和计数。保留 WASD/E，不导入旧输入管理器。原 Rewired `ignoreInputWhenAppNotInFocus` 保持启用，三个回调也检查 `Application.isFocused`。

## 手柄补齐（2026-09-11）

按用户后续要求，正式 Boot 的 Normal 通用手柄模板增加 **RT：Dash，Y：Ultimate**。参考 HellMaiden Systems 场景的 Xbox One 专用映射（map 20，category 1，hardware GUID `19002688-7406-4f4a-8340-8d25335406c8`）：Action 14 使用 Right Trigger 的正半轴，Action 4 使用 Y。

当前插件的通用模板中，RT 对应元素 13、Y 对应元素 8；将这两个原未分配的项绑定到 Action 14/4，并将 RT 范围设为 Positive。通过原 `InputHandler → PlayerController_HMD → BoundPlayer` 执行，与键盘复用同一 Owner/充能/选择限制。按住 RT 持续尝试 Dash，Y 仅按下瞬间触发 Ultimate。F6 仍是键盘开发充能入口。

验证：已核对参考配置与正式 Boot 的三行变更；复用现有 Dash、Ultimate、键盘映射和交互回归，**14/14 PASS**，Unity 退出码 0，结果见 `Logs/GamepadAbilities/results.xml`、`tests.log`。本轮没有新的实体手柄按压记录，不把技能 API 回归当作物理输入验收。重新从 Boot 启动后用 RT/Y 人工确认；旧独立构建需重新打包才能包含新的 Scene 配置。手柄映射可独立恢复为原未分配状态，键盘入口保留。

## 接线与权限

`Rewired → InputHandler → PlayerController_HMD.BoundPlayer → PlayerMovement`。

Dash/Q 继续原 API；F6 经可选委托进入 `NetworkPlayerUltimate.RequestDebugUltimateCharge → CmdDebugUltimateCharge → ServerGrantCharge → 原状态同步/checkpoint`。Gameplay 不依赖 Mirror。失权、禁用和销毁使用原 `UnbindUltimateInput` 同时释放三个委托。

F6 只传意图。两端均检查 Editor/Development；服务器额外校验发送连接、当前 Avatar、ready、启用、初始化、Build/Ultimate 基线、canonical 存活和选卡状态。Host 不先行本地授予。已满不增加 revision；释放中补充一份不修改释放/无敌截止时间，也不能提前再次释放。

日志：`[UltimateDebug] player=<netId> result=granted|already-charged|rejected reason=...`。公开请求返回 true 只表示已经发送，授予以服务器为准。

## 验证记录

2026-09-10，Unity 6000.3.17f1，主项目编辑器关闭后运行。以下均保留正式 Boot→Gameplay；自动夹具控制输入和目标，不声称已经完成真人按键验收。

| 检查 | 结果与证据（项目相对路径） |
|---|---|
| PlayMode 回归 | **124/124 PASS**，`Logs/RewiredAbilities/regression.xml`。Dash、Ultimate、普通击退、镜头、XP/F5、升级选择、菜单与生命周期。 |
| 最终输入/权限用例 | **11/11 PASS**，`Logs/RewiredAbilities/final-input.xml`。在上表后补测跨连接拒绝；映射、Host 无本地预授予、重复请求、原截止时间、选卡/死亡/禁用/旧 Avatar/跨连接拒绝及委托释放。 |
| Development / Release 构建 | 均成功，Unity 退出码 0；`Logs/RewiredAbilities/build-Development.log`、`build-Release.log`。 |
| Ultimate Host＋Client | PASS，`Logs/RewiredAbilities/Ultimate-Host-7971-20260910-234913-566`。 |
| Ultimate server-only＋双 Client | PASS，`Logs/RewiredAbilities/Ultimate-Dedicated-7972-20260910-234924-390`。两组均验证新 Owner 请求、重复 no-op、本人隔离、持有/已消费重连、双波、Burn、远端表现和禁用恢复。 |
| 非开发 server-only＋双 Client | PASS，`Logs/RewiredAbilities/Ultimate-Dedicated-7973-20260910-235106-681`。服务器记录两名玩家的真实 Command 均被 `non-development-server` 拒绝，持有充能仍为 0；客户端公开 API 也拒绝发送。 |
| Dash Host＋Client / server-only＋双 Client | 均 PASS，`Logs/RewiredAbilities/Dash-Host-7974-20260910-235106-691`、`Dash-Dedicated-7975-20260910-235106-683`。 |

以上五组联机验证共 13 个进程，玩法断言均通过，进程退出码均为 0。最初两次测试编译因新增测试缺少 Rewired DLL 显式引用及 Mirror internal setter 的测试访问失败；已修复测试接线，没有删除用例或放宽断言。

真实键盘：已准备 D3D11 可见开发构建及只读事件记录；当前被新可执行文件的 Windows 防火墙提示阻挡，等待用户处理。未将自动调用 API 的结果计为真实键盘结果。持续按住 Shift、长按 Q/F6、失焦与画面观感仍需人工检查。

## 人工步骤

使用同一版本开发构建（或 Editor），从 Boot 启动 Host＋Client；另验 server-only＋两个 Client。完成选卡后手动开始本局。

1. 两端分别按左右 Shift 或手柄 RT，只控制本人；按住时充能允许便再次冲刺，耗尽后等待恢复。另装备 Dash 武器，观察火径及伤害。
2. 初始 Q/Y 无释放；F6 后 Q/Y 释放两波、起手击退和本人震屏。Q/Y/F6 按住不重复触发；充满时再次 F6 日志为 already-charged。
3. B 充能不改变 A。选卡、死亡、组件禁用及失焦时检查输入限制。选择期间 F5/1/2/3/4 原流程保持有效。
4. 分别持有充能和释放后断线重连，检查资源保留、旧波次不重播。Stop 后重开无旧充能/委托。

可选 `--observe-ability-input` 仅在包含测试程序集的构建安装只读记录器，记录 Rewired Action、实际 Dash 开始/结束位置及 Ultimate 状态 revision；它不启动会话、不按键、不授予资源、不调用技能。

构建入口：`RewiredAbilityValidationBuild.Development` / `.Release`，输出 `Builds/RewiredAbilities/<配置>/RewiredAbilities.exe`。Release 仍包含测试夹具与现有 KCP 验证开关，但没有 Development 标志；实际 `Debug.isDebugBuild == false`。非开发验证有意越过客户端公开 API，发送真实 Command，确认服务器收到并拒绝。

复用 `Tools/Run-UltimateProcessValidation.ps1 -Executable <路径> -LogDirectory Logs/RewiredAbilities`，加 `-Dedicated` 切换双 Client。开发版首份充能由 Owner 的新委托请求，包含重复提交、本人隔离、持有和已消费后的重连，以及原双波/Burn/Observer/禁用恢复测试；Release 分支只检验拒绝，不给失败请求另行充能。

## 回退

独立回退本提交，恢复 Boot 三个新增键入口前的配置、Owner 委托及调试 Command，撤下本次测试扩展。Dash/Ultimate 原执行、充能恢复、F5、选卡及此前缓存修复保留。不同构建版本不混用。
