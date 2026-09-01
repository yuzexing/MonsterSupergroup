# Goal: Boot → Gameplay 可切换 KCP / Steam 联机模式

## Objective

在现有 Mirror 联机基础上，增加正式可用的本地 KCP 开发模式，同时保持 Steam/Fizzy 为正常产品路径。

目标运行模式：

```text
Local Development
→ KCP / optional LatencySimulation
→ Mirror
→ BootGameplayNetworkManager
→ Gameplay

Steam
→ Steam Lobby
→ FizzySteamworks
→ Mirror
→ BootGameplayNetworkManager
→ Gameplay
```

KCP 与 Steam 必须共用同一个：

- `Boot.unity`
- `BootGameplayNetworkManager`
- Gameplay additive scene 流程
- NetworkPlayer 生命周期
- NetworkCombat / Gameplay

不得建立两套 Gameplay 联机流程。

---

## Required Behavior

### KCP Local Development

提供独立、可交互的 KCP 开发模式，用于：

- Unity Editor 日常开发
- 本地双进程
- ParrelSync
- Development Build

默认：

```text
Address = 127.0.0.1
Port = 7777
```

提供：

- Host
- Client
- Stop
- Address
- Port
- Network Simulation 开关

默认：

```text
KcpTransport
```

启用 Simulation 后：

```text
LatencySimulation
→ KcpTransport
```

KCP 模式必须：

- 不初始化 Steam。
- 不显示 Steam Lobby HUD。
- 不使用 FizzySteamworks。
- 仍通过现有 `StartHost / StartClient / StopHost / StopClient` 驱动 Mirror。

---

## Steam Mode

保持现有正常产品路径：

```text
Steam initialization
→ SteamLobbyService
→ Create / Search / Join Lobby
→ FizzySteamworks
→ Mirror
```

Steam 模式继续作为普通 Editor / 正式 Steam Build 默认模式。

不得修改现有 Steam Lobby 语义，除非为 Backend 切换所必需。

---

## Backend Selection

Steam / KCP 属于 Network Backend。

Interactive / Validation 属于运行用途，不要将 `KcpValidation` 设计成与 Steam/KCP 同级的平台 Backend。

Backend 必须在进程启动阶段确定，并且联网过程中禁止切换。

Transport 必须在 Mirror 使用或缓存 active Transport 之前确定。

不得依赖多个 MonoBehaviour `Awake()` 的不确定执行顺序完成 Transport 切换。

Backend 切换必须有单一权威入口，不应要求开发者手工启用/禁用多个 Transport 或 GameObject。

---

## Editor / Command Line

提供 Editor 持久选择：

```text
Steam
KCP Local
```

该选择不得修改或保存 `Boot.unity` 场景配置。

支持 KCP 开发参数：

```text
--kcp-role=host|client
--kcp-address=127.0.0.1
--kcp-port=7777
--kcp-simulation=true|false
```

未提供 `--kcp-role` 时停留在 Boot HUD，由开发者手动 Host / Client。

现有：

```text
--boot-gameplay-role=host|client
```

继续作为自动化验收入口，保持现有语义，不改造成日常交互调试入口。

---

## KCP Development Build

新增：

```text
Monster Supergroup
→ Network Combat
→ Build KCP Development Player
```

输出：

```text
Builds/KcpDevelopment/MonsterSupergroupKcp.exe
```

要求：

- Windows x64
- Development Build
- 使用现有启用场景
- Boot 必须为入口
- Gameplay 必须包含在 Build 中

通过：

```text
MONSTER_KCP_DEVELOPMENT_BUILD
```

使该 Build 默认进入 KCP 模式。

该 define 只能影响该构建默认行为，不得永久污染正常 Steam Build 的全局 Scripting Define Symbols。

---

## Preserve

实施前先审计现有：

- Boot
- `BootGameplayNetworkManager`
- KCP
- `LatencySimulation`
- FizzySteamworks
- Steamworks.NET
- SteamLobbyService
- Steam HUD
- Scene wiring / generation tools
- 自动化测试

必须保留：

- Gameplay additive 加载/卸载。
- NetworkPlayer 创建与生命周期。
- NetworkCombat。
- Steam Lobby 流程。
- 现有 KCP 自动验证。
- 无 Steam 环境 PlayMode Test。
- 旧 `--boot-gameplay-role` 行为。

继续禁止使用旧 `NetworkManagerHUD`。

优先最小修改，不重构无关 Gameplay / Combat 系统。

---

## Future Platform Constraint

未来计划支持 PlayStation。

本任务不要实现 PSN 或 PlayStation Transport。

但当前 Backend 边界必须允许未来增加：

```text
PlayStation
→ PSN Session / Invite
→ PlayStation Transport
→ Mirror
```

而无需修改：

- `BootGameplayNetworkManager`
- NetworkPlayer
- NetworkCombat
- Gameplay
- additive scene 生命周期

不得把 Steam-specific API 耦合进这些共享模块。

---

## Validation

### KCP

确认：

- KCP 模式不调用 Steam 初始化。
- Host / Client 可通过 localhost 双进程连接。
- 双方进入 Gameplay。
- Host 有两个连接。
- 双方都生成两个 NetworkPlayer。
- Gameplay / Combat 正常。
- Client Stop / Host Stop 后正确清理。
- LatencySimulation 开关正常。
- 无 Steam 环境可以运行。
- PlayMode Test 继续通过。

### Steam

确认：

- Steam 初始化正常。
- Create / Search / Join Lobby 正常。
- FizzySteamworks 正常建立 Mirror Host / Client。
- Steam 与 KCP 进入 Mirror 后使用完全相同的 Gameplay 流程。
- Leave / Disconnect 正确清理。

### Regression

现有：

- Steam Lobby 测试
- 无 Steam 测试
- KCP 自动验证
- Boot → Gameplay 流程

均继续通过。

---

## Deliverable

完成后报告：

1. 新增/修改文件。
2. Backend 的单一选择入口。
3. Editor 中如何切换 Steam / KCP。
4. KCP Development Build 如何构建。
5. KCP Host / Client 调用链。
6. Steam Host / Client 调用链。
7. Transport 在哪个生命周期阶段确定。
8. Disconnect / Cleanup 流程。
9. 测试结果。
10. 仍需人工配置的 Inspector / Prefab 项。
11. 如何保证未来可增加 PlayStation Backend。