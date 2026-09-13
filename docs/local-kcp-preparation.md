# 开发环境本机 KCP 联机验收

本地建房、加入和换局功能已实现；核心及多进程测试通过。**完整验收尚未全部通过**：带画面程序最终退出时存在原生崩溃；真实窗口键盘复测已执行，Shift+Tab 反向导航未通过，见文末。

## 使用入口

Development 包：`Builds/MenuDevelopment/MonsterSupergroup.exe`。直接运行程序，首页右侧显示“本地联机 · 开发”。Unity Editor 从 Boot 场景运行时也提供该区域。

1. 在同一台电脑启动三个程序窗口。
2. A 保持端口 `7777`，点击“创建本地主机”。
3. B、C 填写相同端口，点击“加入本地主机”。
4. 核对准备页顶部为“本地 KCP · 127.0.0.1:7777 · 3 / 4”，各自具有不同的 P 编号、正确的房主及“你”标记。
5. 每人选择初始武器并准备，A 点击“开始游戏”。
6. 核对实际移动、攻击、同步；全员倒地后由 A 选择“重新开始”或“回到大厅”。

该路径不创建 Steam 大厅，也不依赖 Steam 好友邀请。原“游戏”按钮继续使用 Steam 优先、不可用时本地单人的流程。非 Development 包不显示新增区域，直接调用新增建房或加入接口也会被拒绝。

地址固定为 `127.0.0.1`。端口范围为 1–65535，默认 7777，使用 KCP UDP 监听；如端口已被占用，可加入该主机或选择另一端口。端口编辑仅在本进程内保留，不写共享设置。同机两组房间可分别使用 7777 与 7778。

## 连接、错误与键盘

- 创建或加入期间，禁用重复操作、端口修改和原“游戏”；点击“取消连接”或按 Esc 取消。
- 完成标准是收到包含自身席位的房间快照。仅建立网络连接不会被当作入房成功。
- 15 秒仍未得到有效房间快照时清理并提示；网络层可能更早报告失败。保留满员、加载中禁止加入等服务端原因。
- Tab / Shift+Tab 可依次选择首页按钮、端口、创建和加入。方向键导航按钮；端口内 Enter 仅结束编辑，按钮上 Enter 才提交。
- 返回首页保留端口文字。完整离开会释放连接与监听，可再次创建或加入；回大厅、重新开始保持已有连接与端口。
- 同进程断线后，使用同一地址和端口重新加入，可复用服务端签发的身份。Downed 重连仍保持倒地限制。关闭程序后的身份恢复不属于此次范围。

## 可重复测试

在项目目录运行：

```powershell
./Tools/Run-LocalRoomValidation.ps1 -Scenario party -Port 7777
./Tools/Run-LocalRoomValidation.ps1 -Scenario solo -Port 7777 -Visible
./Tools/Run-LocalRoomValidation.ps1 -Scenario errors -Port 7777 -Visible
./Tools/Run-LocalRoomValidation.ps1 -Scenario admission -Port 7777
./Tools/Run-LocalRoomValidation.ps1 -Scenario release -Executable Builds/MenuRelease/MonsterSupergroup.exe -Visible
./Tools/Run-LocalRoomValidation.ps1 -Scenario solo -Port 7777 -Width 1920 -Height 1080 -Visible
```

这些脚本使用普通 Interactive Boot 路径，由测试驱动实际端口控件和创建、加入按钮。参数不会提前选定网络后端；本地入口必须自行完成 KCP 切换。测试不自动发送 Steam 好友邀请。

- `party`：Host + 两个客户端，从新 UI 入房、准备、开局；Downed 同进程重连；连续三次重开、回大厅再开局；核对身份、成长重置、新局实际移动/攻击/经验以及退出清理。
- `solo`：通过 KCP 创建单人准备房间，逐一核对六种初始武器和全员倒地后的回大厅、开局。
- `errors`：非法端口、无主机、取消、端口占用、重试和端口文字保留。
- `admission`：四席位、第五人满员拒绝、加载期间加入拒绝。
- `release`：非 Development UI 隐藏和新增接口拒绝。
- 多房间：同时运行两份 `party`，分别指定 7777 和 7778。输出目录包含端口，各组使用独立的进程和同步标记。

日志、阶段标记、可见模式截图在 `Logs/PreparationMenu/<时间>-local-<场景>-p<端口>/`。`[LocalRoom]` 记录提交、端点与成功快照，`[MenuProcess]` 记录校验阶段及最终 PASS/FAIL。普通运行日志为 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Monster Supergroup/Player.log`。

## 本次结果

2026-09-12 已通过：

| 检查 | 结果与记录 |
|---|---|
| 网络核心 EditMode 回归 | 476/476，通过；`Logs/LocalRoom-EditMode.xml` |
| 准备房间、新接口、ESC 菜单、好友页 PlayMode | 最终 18/18，通过；`Logs/LocalRoom-PlayModeDelivered.xml` |
| 三进程 UI、Downed 重连、连续重开及回大厅 | 通过；`20260912-224957-local-party-p7777` |
| 最终 Development 包三进程复测 | 通过；`20260912-231114-local-party-p7815` |
| 四席位、满员与加载中加入拒绝 | 通过；`20260912-225021-local-admission-p7792` |
| 同时运行两组三进程房间 | 通过；`20260912-225923-local-party-p7777`、`20260912-225923-local-party-p7778` |
| 非法端口、无主机、取消、占用、重试 | 通过；`20260912-225857-local-errors-p7790` |
| 端口鼠标命中、好友页清理 | 修正后 2/2，通过；`Logs/LocalRoom-Pointer2.xml` |
| 最终 Development 包错误处理复测 | 通过；`20260912-231114-local-errors-p7816` |
| 最终非 Development 包隐藏入口、接口拒绝 | 通过；`20260912-231211-local-release-p7817` |

上述三进程日志均为 `steamInitialized=True`，大厅 ID 为 0，各进程身份独立。更早的 `20260912-205751-local-party-p7791/host.log` 记录 Steam 不可用时也能经 UI 建立 KCP 准备房间；该早期整组脚本因过早点击准备而失败，不作为三进程通过结果。模拟 Steam 断线回调用于额外验证 KCP 会话不会被误停。

验收中还修正了端口框鼠标命中，以及好友页测试误把首页端口框当作残留搜索框。

## 尚未通过的检查：图形进程退出

当前 Unity 6000.3.17f1 的带画面程序在完成流程并关闭窗口时，仍出现原生退出崩溃，退出码 `0xC0000005`。因此可见脚本即使记录了流程 PASS，整组结果仍按失败处理。返回首页、释放 KCP 端口、回大厅和重开已经通过；问题出现在最终关闭程序阶段。

使用安装目录的匹配 UnityPlayer PDB 解析转储，崩溃发生在 `RuntimeStatic<PlatformAccessibilityManager>::StaticDestroy → ExternalGPUProfiler::GetGameViewWindowHandle`，位于引擎运行时静态清理阶段。相关记录为 `Logs/inspect-unity-symbol.py` 与 Windows CrashDumps；Direct3D 11 和 Direct3D 12 均可复现。

初步怀疑 App UI 的窗口回调后，尝试提前释放及禁用 App UI 原生平台；进一步移走验收副本中的 App UI DLL，退出崩溃仍然出现。这些无效修改已撤回，不把它们作为交付修复，也不修改系统无障碍设置或强行将退出码改成成功。该原生退出问题尚未解决，完整图形进程验收不能标为全部通过。

## 真实窗口键盘复测：2026-09-12 至 2026-09-13

使用最终 Development 包，普通交互启动，1920×1080。通过 Windows 窗口控制发送鼠标和键盘事件，逐步观察游戏画面；未调用 UGUI 按钮回调或网络操作接口代替按键。当前 Steam 未连接。运行日志保存在 `Logs/KeyboardRetest-20260913/Player.log`。

| 操作 | 实际结果 |
|---|---|
| 点击端口，输入 7799，按 Enter | 通过：文字正确，结束编辑，停留首页，没有发起连接 |
| 首页连续 Tab | 通过：游戏 → 反馈 → 选项 → 退出 → 端口 → 创建；端口获得焦点时文字全选，再按 Tab 正常离开编辑 |
| 方向键 Up | 通过：从游戏反向到加入，再到创建 |
| 创建按钮上 Enter | 通过：进入 `本地 KCP · 127.0.0.1:7799 · 1 / 4`，显示 P1、房主、你；日志收到自身 Preparing 快照 |
| 准备页 Esc | 通过：返回首页，保留 7799 |
| 加入按钮上 Enter | 通过：出现“正在连接并同步房间”，原游戏入口及本地操作禁用，显示取消连接 |
| 连接期间 Esc | 通过：取消并返回可操作首页，显示“已取消本地连接”，保留 7799 |
| 取消后再次键盘建房、Esc 返回 | 通过：同端口重新建房成功；返回后没有残留 7799 UDP 监听 |
| Shift+Tab 反向导航 | 未通过：`Shift_L+Tab` 从创建前进到加入，`Shift_R+Tab` 从加入前进到游戏，`Shift+Tab` 从游戏前进到反馈；均表现为正向 Tab |
| 最终包 1920×1080 布局 | 通过：首页角色预览、端口、按钮、取消提示与准备页内容可辨认，无重叠或裁切 |

连接取消测试使用临时绑定 `127.0.0.1:7799` 的 UDP 不响应接收端，让真实 KCP 加入保持等待，避免无监听端口被 Windows 立即拒绝。接收端已释放，随后再次建房成功；未修改系统网络设置。

Shift+Tab 的代码确实读取左右 Shift 的按住状态，但本次工具发送的短组合键未产生预期结果。尚未区分游戏逐帧采样与工具按下/释放时序的影响，不将其推断为所有实体键盘都会失败，也不把它记为通过。仍需定位并验证“持续按住 Shift，再按 Tab”的行为。本次未修改运行时代码或重建验收包。

## 剩余核验

- Shift+Tab 反向导航需进一步定位并复测；当前可用方向键 Up 反向选择。
- 最终开发包 1280×720 的布局、端口及错误文字已在上一轮检查；本轮真实键盘测试范围是 1920×1080。
- 上一轮物理 Esc 引起的工具停止锁定，本轮未再出现；已实际完成上述按键，不再将整个键盘复测列为未执行。
- 测试后游戏停留首页。关闭整个程序的原生退出崩溃仍未解决；“返回首页成功”和“进程正常退出”继续分别记录。

交付构建日志：`Logs/LocalRoom-BuildDevFinalVerified.log`、`Logs/LocalRoom-BuildReleaseFinalVerified.log`，均成功。构建包含可重复验证组件；普通双击启动不会启用测试驱动，仍停留首页。
