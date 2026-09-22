# Steam 联机卡顿与偶发崩溃采集

2026-09-22 后续：已复现并修复打开 Enemy Debug 后的严重掉帧，新的分页面板和复测包见 [Enemy Debug 性能修复](enemy-debug-performance.md)。下文上午诊断包作为原始取证基线保留。

本次目标是让真实掉帧和偶发崩溃留下可分析的证据，不把本机验证当成真实 Steam 双机结论。用户确认主要是整幅画面掉帧，目前可组织双人联机；昨晚三人负载仍待补测。

## 交付内容

- Steam 试玩诊断包：`Builds/SteamDiagnostics/Monster Supergroup.exe`，使用普通 Boot、菜单及 Steam 后端，不包含测试程序集和自动操作探针。整个目录一起分发，双方使用相同包。
- `Tools/Scenarios/Start-SteamDiagnostics.ps1`：正常打开游戏、记录包哈希和启动信息，退出后按 PID、路径和时间收集 Windows 崩溃事件与转储；不自动加入房间、操作键鼠或结束游戏。
- `--network-diagnostics`：普通运行按需开启 v2 采样。日志文件名包含 UTC、PID、捕获 ID，不覆盖旧场次。
- `Tools/Analyze-NetworkDiagnostics.py`：同时读取 v1/v2 文件，输出分局汇总、慢帧窗口及双端近似时间对齐。
- `Tools/Analyze-UnityDump.py`：只在本机解析 x64 原生转储，需要 Windows Debugging Tools 的 dbghelp.dll 和匹配的 Unity 符号。

Steam 已安装的旧包保留原样。新包还包含当前工作区已有的经验、怪物运动及联机结算改动；旧包和新包不能混连，也不能将新旧表现差异全部归因于诊断代码。包身份以关键二进制清单与实际 buildGuid 为准，不能只比较 Unity 通用启动 EXE 的哈希。

## 两台机器怎么测

先退出各自正在运行的同一游戏，保持 Steam 登录，完整复制同一份诊断包。在包目录双击 `Start-Host-DX12.cmd` 或 `Start-Client-DX12.cmd`，然后在普通菜单建房/加入。DX11 对照使用名称带 DX11 的对应入口。日志位于包目录的 `Logs/SteamSessions/`；退出游戏后保持采集窗口打开，等显示 `Capture saved`。

需要指定轮次或 Profiler 时，也可在项目目录运行以下命令；`-Executable` 可指向复制后的 EXE。

```powershell
powershell -ExecutionPolicy Bypass -File Tools/Scenarios/Start-SteamDiagnostics.ps1 -ExpectedRole client -Graphics D3D12 -Scenario R1
```

主机把 `-ExpectedRole` 改为 `host`。这个参数只标注预期角色，仍由玩家在正常菜单建房或加入好友。不要混用 KCP 原型试玩启动器。

如必须从 Steam 库直接点启动，先由用户将诊断包作为本轮测试版本部署到双方游戏目录，再在 Steam 启动选项加入：

```text
--network-diagnostics -timestamps -force-d3d12
```

这一路径的指标位于 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Monster Supergroup/NetworkDiagnostics/`；Player.log 仍为共享的默认路径，复测前后及时归档。可用采集脚本的 `-AttachProcessId <游戏PID>` 监视进程退出并按指标头的 PID 与启动时间归档。附加不能给已启动进程补上诊断参数，也不会擅自把共享日志归到某个进程；只有命令行明确指定的独立 Player.log 才自动复制。

| 轮次 | 主机/客户端 | 图形接口 | 目标时长 |
|---|---|---|---|
| R1、R2 | 保持昨晚角色 | D3D12 | 各 20 分钟或运行至崩溃 |
| R3 | 交换角色 | D3D12 | 20 分钟或运行至崩溃 |
| R4 | 恢复原角色 | D3D11 | 20 分钟或运行至崩溃 |

固定地图、构筑、画质和帧率上限；覆盖怪物增多、选卡、死亡后队友继续战斗、结算重开。记录体感卡顿时的局内时间。启动器 20 分钟提醒按进程启动计时，包含菜单停留；不会强制退出。正式时长以指标中的对局阶段为准。

每轮关闭游戏后等待采集完成，再把双方本轮目录放到一起。`capture.json` 的 `complete` 只表示采集流程完成；还要检查 `normalExit`、`errors`、崩溃事件、各指标文件的 `.status.json`。默认记录每秒刷新，原生崩溃时最后一小段可能尚未落盘，缺少尾部或 `complete=false` 不等于整份文件不可用。

## 短时 Profiler

先用轻量采样确认掉帧窗口。需要调用级数据时，在下一轮启动器命令后加 `-ProfileSeconds 20`。开发版运行满 60 秒后，首次超过 100 ms 的帧触发一次 20 秒原始 Profiler 文件；在 `metrics/profile-*.raw` 中查找并用 Unity Profiler 加载。没有触发长帧就不会生成文件，已有 Profiler 会话也不会被接管。默认不启用 Deep Profiling；开启 Profiler 的轮次单独标注，不能混入轻量采样开销结论。

## 更完整的原生转储

当前已有转储能识别两条故障路径，但仍不能确定最初的错误来源。`Tools/Scenarios/Set-SteamFullCrashDumps.ps1` 提供针对 `Monster Supergroup.exe` 的 WER 完整转储配置，默认 `Prepare` 只展示设置；`Enable` 会先备份原值，再设置完整转储与最多保留五份，`Restore -BackupFile <备份>` 恢复原值，不持续附加调试器。

此操作按 [Microsoft WER 说明](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps) 需要管理员权限。本轮进程不具备该权限，因此只完成脚本准备和参数检查，未宣称已配置系统。需要时在管理员 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File Tools/Scenarios/Set-SteamFullCrashDumps.ps1 -Mode Enable
```

转储只保留本机 `%LOCALAPPDATA%/CrashDumps/`。WER 对使用自定义崩溃处理器的程序不保证生效，Unity 自身崩溃文件仍同时收集；首次故障后必须检查实际转储是否生成和是否包含完整内存，不能把配置成功当作捕获成功。

## 指标与分析

v2 保留旧字段，增加 UTC、本机单调时间、网络时间、PID、捕获 ID、人数/生命/阶段、帧直方图、进程和托管内存、警告数量、焦点/窗口/覆盖层变化。每个采样窗口最多保存八个 >100 ms 帧的细节，其他长帧仍计数。Windows 进程内存通过系统 API 获取，避免 Mono 的内存属性返回零；`hardware.json` 补充实际驱动版本和日期，指标头的 `driver` 字段是 Unity 提供的图形接口描述。

定点计时的顺序由头部 `areas` 指定：碰撞检查、受伤请求、状态切换、快照发送、快照接收。这些计时可能嵌套，不能相加当作主线程总耗时。累计网络字节和警告做相邻采样差分；没有 Steam 连接采样不代表队列为零。GPU/Profiler 不可用为 -1。渲染/GPU 数值是 FrameTiming 最近可用帧，并带时间戳，不冒充整秒最大值。

```powershell
python Tools/Analyze-NetworkDiagnostics.py <host.jsonl> <client.jsonl> --output report.json --timeline-csv timeline.csv
```

- 同一非零 buildGuid、run、round 才对齐；使用网络时间最近 1.5 秒内的窗口，同时保留 UTC 和本机时间。它是近似关联，不证明因果。CSV 同时列出每秒吞吐、RTT、待发送/未确认字节和警告增量；连接队列列为各连接最大值。
- 主线程、GPU、GC、Present 等待、网络队列和警告风暴分别列为信号，不自动输出根因结论。
- P95/P99 是窗口百分位；报告给出最坏窗口百分位，不能把它当成整局 P95。
- 死亡后受伤问题需结合定点计时及 Profiler 判断影响，不能简单关闭警告就宣布解决卡顿。

## 已有崩溃分析

本轮证据目录：`Logs/SteamLagInvestigation/20260922-101723/`；昨晚原始文件继续保存在 `Logs/SteamCrashDiagnosis/20260922-094145/`。

- **20:45 战斗崩溃**：Unity 与 Windows 两份转储一致指向线程 29692，异常 0x80000003，精确异常位置解析为 `DebugStringToFilePostprocessedStacktrace+0xb1d`。结合原 Player 日志的 Present 失败、不可恢复 GPU 错误，以及栈内 `D3D12SwapChain::Present` / `GfxTaskExecutorD3D12::DoPresent` / `RunTask` 地址线索，定位到 DX12 画面提交失败后的引擎致命退出。另有线程处于等待图形提交的路径。不能据此确定最初无效调用由驱动还是上层代码造成。
- **23:32 退出崩溃**：异常帧为 `D3D12Core!DXBCGetSizeAssumingValidPointer+0x5`，可展开到 `CLibrary::Serialize+0x242`。栈内有 `D3D12PipelineCacheLibrary::SaveCache+0xa9`、图形设备销毁与 Shutdown 地址；SaveCache 返回点前为间接序列化调用。这与仓库已有的 PSO 缓存退出问题方向一致，但不等于已证明相同内部根因。
- 当前 DbgHelp 不能完整展开所有帧；报告对无法验证的后续帧停止展开，并将原始栈地址明确标为候选。不能把这些候选拼成已证实的完整调用链。解析工具校验本机映像的时间戳和映像大小与转储匹配；最终报告为 `steam-2045-symbols-verified.txt`、`steam-2332-symbols-verified.txt`。
- `ComputeBuffer` 的 GC 清理警告也曾出现在正常退出记录，不能将日志最后一条警告直接当作崩溃原因。没有修改驱动、清除缓存或将默认图形接口改为 DX11。

## 已执行验证与待测边界

- 采样窗口 EditMode：3/3；验证长帧上限、统计溢出、关闭/开启计数及重置。相关网络/日志 EditMode 回归 83/83。
- 真实 Boot PlayMode：2/2；验证 v2 输出和正常关闭，以及实际玩家/受伤入口在 Dead 状态下重复 100 次调用产生 100 次非法转换。该受控场景约耗时 20.810 ms（含诊断计时）；这是 Editor 实验，不代表昨晚每帧耗时。暂不更改玩法逻辑，以保留对照。
- 分析器 Python：8/8；覆盖旧格式、崩溃截断尾部、缺失指标、加权均值、跨机器时间、不同包/局隔离、队列/吞吐变化和 CSV 导出。
- 独立 Player 采样开销：相同构建、固定两只普通怪、D3D11、KCP、本机 144 FPS 上限，交替三组关闭/开启，各采样 20 秒。关闭平均 7.0068 ms，开启 7.0029 ms，差异 -0.0558%，未超过 5% 目标；微小负差视为波动，不宣称加速。六进程均正常退出；样本保存在 `overhead/`。高负载 Steam 场景仍需验证。
- 最终版本进一步取消帧率上限、关闭垂直同步，以免等待时间掩盖成本：三组各 20 秒，关闭平均 **1.6187 ms**，开启 **1.6100 ms**，差异 **-0.5424%**，仍低于 5% 目标，负差视为波动。六进程全部退出码 0，共 120 秒有效测量；原始数据和汇总在 `overhead-final/`。测试包 buildGuid 为 `1d2304f06fd446ceb55341782bb0c8ba`；测试探针只存在于验证包。
- Steam 交付包构建成功（普通 `player-development` 配置），buildGuid 为 `16e0b467724f4a05adb148b0df79976c`；不含测试程序集。最终编译通过，构建记录为 `build-delivery-final.json`。采样相关修改不改变联机协议；当前其他工作区改动已使用协议 5，两端须使用此同一交付包。
- Windows 转储分析已在本机完成；数据未上传。采集工具只收集匹配进程的记录，不结束用户游戏。
- 独立 Development Player 的 Profiler 触发验证：在运行满 60 秒后通过验证探针制造 150 ms 停顿，生成一个 47,033,900 字节文件，两秒后自动停止，进程正常退出。原始数据在 `profiler-probe/metrics/`；该人为停顿样本不代表真实卡顿根因。
- Windows 内存接口已在独立 Player 验证，采样得到正数工作集与私有内存；GPU、渲染线程耗时也实际可读。
- 最终退出归档验证：独立进程正常退出，`collector-external-final/capture.json` 的 `complete=true`、`normalExit=true`、`errors=[]`；从另一目录只复制 PID/启动时间匹配的 v2 指标及显式 Player.log。Unity 小转储中的 PID 解析与原故障进程 40836 一致。此前 `profiler-capture/` 记录暴露的 PowerShell 时间类型归档问题已修复，保留失败记录，不冒充成功。

本机数据的分析样例为 `profiler-fixture-report.json` 与 `profiler-fixture-timeline.csv`。只有本机一个 Host，不是双机实测时间线。真实两端样本到齐后，再用相同工具生成对齐报告和有依据的最小修复建议。死亡后反复受伤路径已复现，但其对真实卡顿的占比还没有测量依据。

真实双机四轮、短时 Profiler 的实际卡顿样本、第三名玩家负载仍待人工执行。窗口切换/覆盖层/显示模式各十次的对照仅在图形生命周期线索需要进一步隔离时逐项进行。没有这些数据，暂不宣称已定位整场掉帧根因或修复偶发崩溃。

性能采样遵循 [Unity 独立 Player 性能分析说明](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-profiling-applications.html)；原生符号解析参考 [Unity Windows 调试说明](https://docs.unity3d.com/6000.3/Documentation/Manual/WindowsDebugging.html)。
