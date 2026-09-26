# Steam 双端诊断采集

两端使用同一个已发布完整包、不同 Steam 账号。`product`、`operator-kit` 和自动生成的 `logs` 同级。发布状态以 `release.json` 为准，模板本身不能启动游戏。

在 `operator-kit` 打开 PowerShell，Host 执行：

```powershell
.\Host-Local.ps1
```

Client 执行：

```powershell
.\Client-Local.ps1
```

到达菜单后，在各自第二个 PowerShell 窗口的同一个 `operator-kit` 目录执行：

```powershell
.\Verify-Running.ps1
```

检查通过后建立普通 Steam 房间，完成一轮连续 **10 分钟战斗**。记录实际角色、装备、关卡、画质、分辨率以及异常对象和大致时刻。保持原采集终端打开。

默认 Diagnostic：队列上限 512 MiB，其中 4 MiB 为关键记录预留；诊断计费预算 768 MiB；队列观测为 1024 个 1 秒窗口。缓存按需使用，总计费预算不等于整个游戏的内存占用上限。Profiler 关闭。

正常退出后等待保存和采集完成。Diagnostic 不会因为经过 30 秒而自动放弃保存；界面显示待处理缓存及当前阶段。出现“记录不完整”时按界面明确退出，并保留全部日志。确需中断保存时，可以在游戏界面确认放弃未保存记录，但该轮不能视为完整采集。不要把强制结束进程当作正常退出。

原采集窗口显示 `Capture saved` 后执行：

```powershell
.\Export-Case.ps1
```

提交显示的完整导出目录，包含正常、失败或主动放弃状态。无参数校验／导出使用最近一次成功预检后记录的 `current-case.json`；失败的新启动会使该标记失效，避免误选上一轮。旧 case 可通过 `-PackageDirectory` 和 `-CaseDirectory` 显式选择。

需要基线对照时，两端快捷入口均支持 `-EvidenceProfile Standard`。Standard 保持 32 MiB 队列、128 MiB 预算、100 ms 窗口及现有 30 秒保存期限。一般采集继续使用默认 Diagnostic。

运行校验同时核对包、实时进程身份，以及启动记录中的实际配置。仅出现启动参数不代表配置已生效；到达菜单后若首次校验提示启动记录尚未落盘，等待片刻重新执行即可。身份和配置通过，不代表记录完整或游戏行为正确；最终完整性须离线检查。

本交付为第一批缓存与退出修复。弱机 10 分钟完整性验收后，再做重复维护、小附件和刷新成本的独立对照；本包不宣称已解决死亡后阻挡。
# Steam 网络专项快捷入口

本轮定位 Steam 发送拒绝时使用 `Host-Network.ps1`／`Client-Network.ps1`。两端必须复制同一完整交付目录，分别在 `operator-kit` 中运行对应入口，无需填写路径。它们开启轻量网络记录并关闭完整战斗取证、日志网络复制和故障注入，不改变现有发送行为。

建议把 `product` 和 `operator-kit` 直接放在 `C:\SteamNet` 下，避免解压产生重复嵌套的包名目录。轻量 writer 的状态文件比日志文件名更长，过长路径可能导致空日志；新入口会在启动游戏之前检查这个限制。

无需打开终端：Host 双击 `Host-Network.cmd`，Client 双击 `Client-Network.cmd`；到菜单后双击 `Verify-Running.cmd`。正常退出、原启动窗口显示收集完成后，双击 `Export-Case.cmd`。启动窗口等待游戏退出属于正常行为，请保持打开。网络导出使用较短的 `network-export-…` 父目录，内部保留原 case 名称。

到菜单后运行 `Verify-Running.ps1`，校验实际包、进程及轻量采集配置；正常退出并等待采集器完成后运行 `Export-Case.ps1`，提交它输出的完整目录。首次测试约八分钟，重点是第 280–330 秒、高怪量，以及玩家倒下后队友继续战斗。出现异常仍可操作时再观察约三十秒。

旧 `Host-Local.ps1`／`Client-Local.ps1` 的完整取证行为保留。网络专项没有自动恢复结论，日志未正常关闭或写入失败时仍应保全导出，不能判为完整。
