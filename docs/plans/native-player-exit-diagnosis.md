# M3 Player 原生退出崩溃：定位与临时处理

2026-09-10，在同一台 Windows 机器、同一个 M3 build-5 上复测。Unity 6000.3.17f1，RTX 4080，显示驱动 32.0.15.9186。没有重新构建 Player，也没有修改 Gameplay、Mirror、Scene、Prefab、图形 API 项目设置或系统驱动。

## 当前结论

故障定位到 D3D12 Player 退出时的管线缓存保存路径。玩法断言通过后，Host 或 Client 仍可能以 `0xC0000005 / -1073741819` 退出。缓存目录隔离不足以解决：复制相同的旧缓存到各自独立目录后仍能复现。因此不能把根因定为两个进程争写一个文件，也不能仅凭新缓存运行成功宣布修复。

当前可用的测试绕过措施是仅在启动 Player 时指定 D3D11。正式项目保留原图形 API 配置，原生退出码仍参与验收。D3D12 内部究竟是缓存数据、输入内存生命周期、运行时或驱动缺陷，尚未确认；这份交付没有宣称修复 Unity/D3D12 内部实现。

## 定位证据及其限制

1. 原始 `Host-20260910-112202-614` 的两端日志均有玩法 PASS，随后 Client 异常退出。Windows Application Error 指向 `D3D12Core.dll + 0x264831`，不是托管 C# 异常。
2. 读取本地 WER minidump，并加载匹配此 Player 的 Unity 私有符号。异常线程在 D3D12 内发生读访问违规；重复崩溃还有 `D3D12Core.dll + 0xa1f5`，不能把所有轮次说成完全相同的指令地址。
3. 原始、共享缓存复现及独立目录旧缓存复现的栈内，都找到 `D3D12PipelineCacheLibrary::SaveCache + 0xa9`、`D3D12PipelineCache::~D3D12PipelineCache`、`GfxDeviceD3D12::~GfxDeviceD3D12`、`DestroyGfxDevice` 和 `Shutdown` 的返回地址候选。SaveCache 返回点前的实际机器码为间接调用 `[rax+0x60]`，与 PipelineLibrary 序列化调用相符。
4. 当前工具只能可靠展开开头两个 D3D12 栈帧；后续是栈内地址与匹配符号、调用点的交叉验证，**不是已经完整展开的调用栈**。这些证据结合缓存内容对照足以缩小故障范围，不能证明具体的内存释放者或内部竞争条件。

转储只在本机读取，没有上传。原始文件位于 `C:/Users/ADMIN/AppData/Local/CrashDumps/M3Knockback.exe.<pid>.dmp`，本轮分析 PID 为 1660、23972、38364。分析文本位于 `Logs/NativeExit/dump-analysis.txt`、`dump-analysis-23972.txt`、`dump-analysis-38364.txt`；Windows 事件位于 `retest-windows-events.json`、`seeded-isolated-windows-events.json`。

实际共享缓存为 `%TEMP%/DefaultCompany/Monster Supergroup/dx12_pso_cache_lib.bin`。独立目录实验确认每个角色都生成了自己的缓存文件。旧缓存控制样本为 `Logs/NativeExit/pso-seed.bin`，SHA-256：`5E871ED42EDC5C55DD7697288A0785B1D6C4737EC99FACCBFD524A6F4DB76212`。

## 同一构建的实际对照

下表的目录均位于 `Logs/M3Knockback`。PASS 表示所有角色的玩法断言通过且退出码全部为 0；失败轮次也保留玩法结果，未将原生崩溃算作通过。

| 条件 | 运行目录 | 结果 |
| --- | --- | --- |
| D3D12，共享原缓存 | `Host-20260910-115511-129` | PASS |
| D3D12，共享原缓存 | `Host-20260910-115855-592-p7965` | 玩法通过，Client 退出 -1073741819 |
| D3D12，共享原缓存 | `Host-20260910-120010-131-p7965` | PASS |
| D3D12，共享原缓存 | `Host-20260910-120112-276-p7965` | 玩法通过，Host 退出 -1073741819 |
| D3D12，每端全新缓存 | `Host-20260910-115855-592-p7964` | PASS |
| D3D12，每端全新缓存 | `Host-20260910-120010-131-p7964` | PASS |
| D3D12，每端全新缓存 | `Host-20260910-120855-840-p7965` | PASS |
| D3D12，每端复制同一旧缓存 | `Host-20260910-120112-276-p7964` | PASS |
| D3D12，每端复制同一旧缓存 | `Host-20260910-120517-897-p7964` | 玩法通过，Client 退出 -1073741819 |
| D3D12，每端复制同一旧缓存 | `Host-20260910-120517-897-p7965` | 玩法通过，Host 退出 -1073741819 |
| D3D11，原 TEMP，Host + Client | `Host-20260910-115511-128` | PASS |
| D3D11，原 TEMP，Host + Client | `Host-20260910-120750-773-p7964` | PASS |
| D3D11，原 TEMP，server-only + 两 Client，含延迟/丢包 | `Dedicated-20260910-120750-773-p7965` | PASS，三进程退出 0 |
| D3D11，原 TEMP，Host + Client | `Host-20260910-120855-837-p7964` | PASS |

D3D11 共三轮 Host + Client、一轮 server-only + 两 Client，九次进程退出均为 0，其中八次使用实际图形渲染。这是当前机器及此构建上的有效绕过证据，不是所有显卡、驱动和退出场景的保证。D3D12 全新独立缓存三轮通过，但旧缓存独立副本三轮中两轮失败，故不将隔离目录本身作为修复交付。

一次并行实验 `Host-20260910-115820-538` 因运行目录时间戳碰撞而无效，已在该目录写明并排除统计。启动器现将端口纳入目录名，避免不同端口的并行运行共享阶段标记。

D3D11 日志确认实际使用 `Direct3D 11.0 [level 11.1]`。server-only 角色仍无图形。上述联机夹具仍从正式 Boot 进入 Gameplay，覆盖本端、跨端、双人命中、断线接管、重连、ServerAuthoritative 和快照收敛；D3D11 不改变模拟权或伤害准入。

已查看 `Host-20260910-120750-773-p7964/authoritative-settled-client.png`：角色、敌人和 Debug 列表可见，local/canonical HP 同为 9628。静态采样不替代击退方向、追逐恢复和震屏观感验收。生产 C# 未改，本轮未重复运行此前已通过的 336 个 EditMode 和 29 个 PlayMode 测试。

## 继续测试的入口

在项目根目录运行：

```powershell
pwsh -File ./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -CaptureFrames -VisibleWindows -ForceD3D11
pwsh -File ./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Dedicated -ImpairedNetwork -CaptureFrames -VisibleWindows -ForceD3D11
```

这是有画面、自动退出的正式 Boot 多进程测试夹具。手工启动普通 Player 时，也可将 `-force-d3d11` 作为本次进程参数；无需重新构建或更改 ProjectSettings。Unity 官方列出了这个 [Player 启动选项](https://docs.unity3d.com/6000.3/Documentation/Manual/PlayerCommandLineArguments.html)。人工动态观感仍需单独验收，不能用进程退出成功代替。

新增的 `-IsolateTemporaryCache` 和 `-PsoCacheSeed` 仅用于诊断，默认关闭。前者要求 PowerShell 7.4+，只为每个子进程设置 TEMP/TMP；后者把已有缓存样本复制到该角色的项目缓存目录，可重复上述反例。它们不删除原缓存、不永久修改环境变量。`-ForceD3D11` 也默认关闭，便于继续验证正式 D3D12 路径。

启动器现在保存 `run.json`，记录实际请求的图形选项、网络扰动、端口、缓存隔离和样本哈希。退出码文件、玩法 PASS/FAIL 日志和截图继续分开保留。测试用临时目录及日志由用户按需要保留或清理。

## 后续根治与排除项

下一步若要恢复稳定的 D3D12 验收，应以同一缓存样本和复现路径测试候选 Unity 补丁或显示驱动，分别改变一个变量，并覆盖冷缓存、重复热启动和正常退出；未查到能确认覆盖当前故障的具体修复版本，不能直接承诺升级有效。向引擎供应方提交前，应整理最小复现、匹配符号、转储、缓存样本及硬件版本，经授权再上传。

Microsoft 的 [CreatePipelineLibrary 文档](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12device1-createpipelinelibrary)要求输入数据在库的生命周期内保持有效；[Serialize 文档](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12pipelinelibrary-serialize)说明缓存与运行时、驱动和设备有关。这是后续检查数据寿命与兼容性的依据，**不是已经证明 Unity 违反该约定**。

- `ComputeBuffer` 的 GC 清理警告也出现在正常退出轮次。Unity 6000.3 的[公开实现](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Runtime/Export/Shaders/ComputeBuffer.bindings.cs)在终结器路径刻意不调用原生 DestroyBuffer，因此不能把最后一条警告直接当成这次 native crash 的原因。
- `EnemyAIManager / AsyncGPUReadbackQueue` 的异步读回清理存在进一步审计价值；本次未修改，也未证明它导致 PSO 保存崩溃。
- M2 曾记录的 AppUINativePlugin/UnityPlayer 退出问题具有另一组栈证据，D3D11 在本轮通过不能宣布那个问题也已根治。
- Circling 的 local/canonical HP 差异没有在有效 M3 测试中复现，仍不归因于 UI，也不宣称此次退出处理修复了 HP。

本次仅修改测试启动器和诊断文档，可单独回退。M3 游戏逻辑提交 `7e09ee4` 保留；M3 仍待人工动态验收，D3D12 原生退出故障仍保留为已定位、未根治的独立问题。
