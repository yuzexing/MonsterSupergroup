# Windows UI Automation 退出崩溃与 Unity 补丁升级

## 已确认的故障

2026-09-13 的显示设置人工复测，PID 85536 在首页退出时先记录访问违规 `0xC0000005`，随后退出 `0xC000041D`，模块偏移相同。原日志 `Logs/DisplaySettings/manual-development.log` 确认实际 D3D11。最后一行 `PlayerConnection::Cleanup` 不是根因定位。

匹配 6000.3.17f1 Development 符号后，异常指令在 `ExternalGPUProfiler::GetGameViewWindowHandle+0x9`，访问地址 `0x138`。栈内地址扫描包含 `PlatformAccessibilityManager::GetProviderForUIA`、窗口过程和退出清理路径；扫描地址不是完整展开调用栈。历史非 Development PID 63424 的异常落在 `RuntimeStatic<PlatformAccessibilityManager>::StaticDestroy+0x51`，访问 `0xe0`。

Unity [6000.3.21f1 官方发布说明](https://unity.com/releases/editor/whats-new/6000.3.21f1)列出 UUM-146676：Windows Player 曾被 UI Automation 客户端访问后，在关闭阶段崩溃。当前符号和实际触发条件与该缺陷吻合。自动化可能触发，读屏等其他 UIA 客户端也可能触发，不能认为正式发行包天然不受影响。

## 本机旧版对照

以下均为 6000.3.17f1，D3D11，可见窗口正常进入首页；实际执行 `sky.get_window_state(include_text:true)` 后点击同一首页退出按钮。原 `menu-*` 包含测试程序集，明确作为旧基线。

| 包 | PID | 退出耗时 | 原始退出码 | Windows 事件偏移 |
| --- | --- | --- | --- | --- |
| MenuDevelopment | 69440 | 1.14 秒 | -1073741819 / 0xC0000005 | UnityPlayer.dll + 0xC2D6E9 |
| MenuRelease | 41000 | 1.55 秒 | -1073741819 / 0xC0000005 | UnityPlayer.dll + 0xB1B0A1 |

证据目录 `Logs/NativeExitUpgrade/baseline-development-uia-visible`、`baseline-release-uia-visible`。两种新转储均再次解析到各自相同的异常函数。转储保留于本机 Windows CrashDumps，不上传。

最初隐藏启动遇到 DXGI 显示切换失败和无法操作首页，且记录器曾有 UTC 时间与 Windows 事件字段解析问题。这些轮次没有计入退出对照。记录器修正后，从原始事件独立匹配上述两个 PID，保存于 `windows-event-followup.json`；早期 result 中 `crashEvents=0` 不能当作没有崩溃的证据。

测试临时将本项目保存的显示设置改为窗口模式，原二进制设置值备份于 `Logs/NativeExitUpgrade/options-backup.json`；不会清空整个 PlayerPrefs。等待安装权限确认前已恢复并逐字节核对原值。

## 升级策略

保留原 6000.3.17f1 安装、构建、日志与转储。隔离副本从当前提交 `9cacc56d` 建立，使用独立 Library 和构建输出。新版本 6000.3.21f1，changeset `c02631ffc030`。Mono、图形配置及第三方插件版本保持；包解析及序列化变化单独审核。

新增 `player-development`、`player-release`，移除测试程序集和验收宏，用正式流程完整构建 Addressables 与 Player。正式验证说明见 [退出验证工具](../player-exit-validation.md)。本轮不修改网络退出逻辑，不强制返回成功退出码，不删除无障碍功能。

## 当前执行结果

退出记录器 8 项自检通过，工具目录与文档一致性检查通过，两种旧包真实崩溃已复现。新安装包签名有效（Unity Technologies SF），6000.3.21f1 已安装完成，安装器退出码为 0；已核对编辑器文件版本为 `6000.3.21f1_c02631ffc030`。

隔离副本首次编译完成。第一次调用正式资源校验时仍有导入任务，统一工具按保护规则拒绝执行，退出码为 1；这不属于 Player 退出崩溃，也不记为资源校验通过。随后重新执行校验。首次依赖解析约 343 秒；此前两次人工中断的导入记录保留，不纳入退出对照。

新引擎内置包依赖使 Burst 从 1.8.29 解析为 1.8.30、Collections 从 2.6.6 解析为 2.6.8，`manifest.json` 未改变。首次导入还触发 Localization 插件按照 Locale 名称重新归组 Addressables，以及 DOTween 插件自动添加 `DOTWEEN` 宏；这些自动序列化变化单独核对，不能当作退出修复的代码改动或未经核验带回主项目。

后续正式资源校验通过，`player-development`、`player-release` 完整构建均成功，Unity 构建进程均正常退出。报告分别位于 `Logs/ProjectTools/20260913-211041-096-validate.all`、`20260913-211155-584-build.player`、`20260913-211431-880-build.player`。

新版 Development 的首轮真实窗口复测已读取 UIA 元素并确认首页，但用户按物理 Escape 中止了窗口控制。随后仅清理本轮启动的验收 Player，并逐字节恢复原显示设置。`Logs/NativeExitUpgrade/new-development-uia/run-1/exclusion.txt` 记录取消原因；该轮退出码 -1 来自中断后的强制清理，不能用于判断新版本是否仍有原生退出崩溃，也不计为正常退出通过。没有继续执行桌面输入。

用户随后恢复复测，完成 25 次真实首页退出：开发/发布各 5 次 UIA、各 5 次用户人工、默认 D3D12 各 1 次、设置三条路径 3 次。全部退出码为 0，无对应 Windows 崩溃事件。本机该 UIA 退出故障已验证修复，主项目版本统一为 6000.3.21f1，旧编辑器和旧包保留。

设置、三进程会话清理与连续重开、正常和异常网络下四组 120 秒交接压力验证通过。扩展 EditMode 的四项历史迁移失败在旧版同样复现，未修改玩法资产规避。详细范围、原始结果位置及排除记录见[本轮复测结果](../windows-exit-validation-results.md)。

旧 [D3D12 PSO 缓存故障](native-player-exit-diagnosis.md)单独保留。Steam、FMOD、AppUI 插件与 ComputeBuffer 警告不能仅凭相邻日志认定为本次根因。
