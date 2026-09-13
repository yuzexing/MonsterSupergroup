# Windows Player 退出验证

## 用途与构建

`test.player-exit` 观察真实窗口进程退出，不调用运行时测试探针，也不根据日志中的 PASS 判断退出成功。原始退出码非零、对应 Application Error 事件，或提交退出后超过 30 秒均失败。超时清理会明确记录为强制清理。

正式验证使用 `player-development`、`player-release`：Boot → MainMenu → Gameplay，分别启用/关闭 Development，两者均不包含测试程序集或验收宏。`menu-development`、`menu-release` 继续用于功能自动化，不替代这两个包。构建前检查正式资源，Addressables 随完整 Player 构建更新。

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile player-development -Unity '<6000.3.21f1>/Editor/Unity.exe'
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile player-release -Unity '<6000.3.21f1>/Editor/Unity.exe'
```

升级对照必须使用独立输出目录与完整构建，不能使用 `-ScriptsOnly` 或复制旧 UnityPlayer.dll。脚本拒绝包含测试程序集的包；旧基线确需使用时显式传 `-AllowValidationBuild`，结果仍记录实际程序集。

## 普通人工组

在本机 PowerShell 中运行以下命令（由人工操作终端）：

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId test.player-exit -Executable Builds/PlayerDevelopment/MonsterSupergroup.exe -Mode Manual -Graphics D3D11 -Iterations 5
```

游戏启动后正常操作；准备退出时按终端提示确认，再于 30 秒内点击游戏**首页的退出按钮**。每轮单独启动。记录器只读取进程、文件和 Windows 日志，不查询窗口元素。操作者不要运行 UI Automation 窗口检查工具；已存在的读屏或其他 UIA 客户端也可能影响对照，应如实记录环境。AI 代点不归入普通人工组。

人工组从终端 Enter 开始计时，包含切回游戏及移动鼠标的时间，因此是退出耗时的保守上界，不是引擎清理耗时。UIA 组从点击前的控制信号计时。结果中的 `timingOrigin` 区分两种口径。

对发布包重复，另外使用 `-Graphics Default` 验证默认图形 API。`-Scenario settings/singleplayer/client/host/run-end` 只标记本轮人工完成的场景，不自动执行这些场景。

## UI Automation 组

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId test.player-exit -Executable Builds/PlayerDevelopment/MonsterSupergroup.exe -Mode UIAutomation -Graphics D3D11 -Iterations 5
```

记录器显示本轮目录并等待外部 UI 操作者。它不是窗口驱动器；AI 应通过获准的窗口控制工具执行以下步骤：

1. 从 `run-N/launch.json` 取得本轮 `runId` 和 `processId`，选定对应的真实游戏窗口，确认首页可正常渲染。
2. **实际读取 UI Automation 元素**。例如 Computer Use 的 `get_window_state(include_text:true)`；仅截图不满足此条件。运行期间读取和临近退出读取均要覆盖，在 `method` 中注明。
3. 读取完成后写 `uia-observed.json`，内容为 `runId`、`processId`、ISO UTC `observedUtc`、实际工具与时机 `method`。
4. 准备点击首页退出时写 `quit-request.json`，内容为相同 `runId`、`processId`、ISO UTC `requestedUtc`；随即点击首页退出按钮。
5. 等待本轮 `result.json`，再操作下一轮。禁止为未发生的元素读取或退出操作预填记录。

旧 runId、错误 PID、过期时间、缺少观察记录会失败。必须使用新产物目录，避免上轮控制文件触发本轮退出。游戏窗口可见启动，避免隐藏启动妨碍 DXGI 显示模式初始化。不要用 Alt+F4、强杀或脚本 `Application.Quit` 替代首页按钮的验收。

## 结果与场景清单

每组输出 `package-manifest.json`（包内文件哈希）、`result.json`，每轮包含启动信息、Player 日志、退出码、耗时、实际图形 API、UIA 观察及匹配 PID 的 Windows 事件。无关进程的崩溃不会归入本轮。转储只保留本机，不自动上传。

| 场景 | 最低次数 |
| --- | --- |
| 无测试代码 Development / Release，人工组 / UIA 组 | 每个组合连续 5 次 |
| 显示设置草稿丢弃、应用确认、超时恢复后退出 | 各路径均检查 |
| 单人退出、KCP 客户端离开后退出、Host 结束后退出、战败返回首页后退出 | 每种 3 次 |
| D3D11 与默认图形 API | 分别记录，不合并故障归因 |

设置、语言、选卡、多人、重开、怪物交接及 Editor 工具的功能回归继续使用原测试入口；其功能结果与进程退出结果分开记录。D3D12 PSO 缓存问题沿用独立诊断文档，不能因本次 UIA 修复而标记已解决。

## 记录器自检

`Tools/Tests/Test-PlayerExitValidation.ps1` 检查 UTC 时间、旧操作信号、错误 PID、部分 JSON，以及新旧 Windows 事件格式。原始基线事件还用于核对真实 PID、异常码和模块偏移，避免记录器漏报。
