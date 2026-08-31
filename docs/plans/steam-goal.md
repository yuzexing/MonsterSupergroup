# Goal: Steam Lobby + FizzySteamworks 联机闭环

## Objective

在现有 Mirror 联机基础上完成可实际使用的 Steam Lobby 联机闭环：

**Steam 初始化 → 创建/搜索 Lobby → 加入 Lobby → FizzySteamworks 建立 Mirror Host/Client → Gameplay 加载与 NetworkPlayer 创建 → 离开/断线完整清理。**

正常游戏路径必须为：

`SteamLobby → FizzySteamworks → Mirror`

现有 KCP/LatencySimulation 只保留给自动化测试和本地双进程验证，不作为正常游戏入口。

## Existing behavior to preserve

先审计当前 Boot、`BootGameplayNetworkManager`、Transport、Steamworks.NET/FizzySteamworks 和现有测试，再实施修改。

必须保留：

- `BootGameplayNetworkManager` 现有 Gameplay additive 加载/卸载逻辑。
- 现有 Mirror Player 创建与生命周期。
- 现有 NetworkCombat 行为。
- KCP/LatencySimulation 双进程验证能力。
- 无 Steam 环境下现有自动化 PlayMode 测试仍可运行。

不要重新实现 Gameplay/Player 生命周期，只增加 Steam Lobby 编排层。

## Required implementation

### 1. Steam lifecycle

新增一个唯一负责 Steam API 生命周期与 Lobby 操作的服务，例如 `SteamLobbyService`。

Windows/Editor Steam 模式下负责：

- `RestartAppIfNecessary`
- Packsize/DLL 检查
- `SteamAPI.Init()`
- 每帧 `SteamAPI.RunCallbacks()`
- 应用退出时 `SteamAPI.Shutdown()`

开发阶段使用 AppID `480`。

Steam 初始化失败时不得启动 Mirror，应进入可重试错误状态。

检测到现有 KCP 双进程验证启动参数时，跳过 Steam 初始化并继续使用原验证路径。

### 2. Lobby API

至少提供：

- `CreateLobby()`
- `RequestLobbyList()`
- `JoinLobby(ulong lobbyId)`
- `LeaveAndStop()`

并暴露当前初始化状态、Lobby 状态、错误、LobbyID、Host SteamID64、搜索结果，以及状态变化事件。

Lobby 为 Public，容量使用 `NetworkManager.maxConnections`。

Lobby metadata：

- `game=monster_supergroup`
- `protocol=1`
- `state=starting|ready|closed`
- `host_steam_id=<SteamID64>`
- `name=<Steam persona name>'s Lobby`

创建 Lobby 后启动 Mirror Host；只有 `NetworkServer.active` 后才能将 Lobby 标记为 `ready`。

搜索时只返回：

- game/protocol 匹配
- state=ready
- 有空位

避免 AppID 480 的其他 Spacewar Lobby 混入。

### 3. Fizzy + Mirror connection

Boot 正常游戏默认 Transport 改为项目现有 `FizzySteamworks`。

Steam 初始化完成前 Fizzy 不应参与连接。

Host：

`CreateLobby → metadata → enable Fizzy → StartHost`

Client：

`JoinLobby → read host_steam_id → validate against GetLobbyOwner() → set networkAddress → enable Fizzy → StartClient`

不实现 Host migration。

加入成功后的 Gameplay additive 加载和 NetworkPlayer 创建必须继续由现有 `BootGameplayNetworkManager` 完成。

### 4. Cleanup

实现统一、可重复调用的 cleanup。

Host 离开：

`Lobby closed / non-joinable → StopHost → LeaveLobby`

Client 离开：

`StopClient → LeaveLobby`

随后清空 Lobby/Host 状态、networkAddress、pending Steam callbacks，并关闭 Fizzy 的连接状态。

**不要 Shutdown Steam API**，这样用户可以再次 Create/Refresh/Join。

Mirror 意外断线、Lobby 被移除、Lobby Owner 改变时也必须进入同一 cleanup 流程。

现有 `OnStopServer/OnStopClient` 继续负责 Gameplay 卸载和 NetworkPlayer 清理。

### 5. Boot HUD

删除正常入口中的 `NetworkManagerHUD`。

新增简单诊断 Lobby HUD：

Idle：

- Create Lobby
- Refresh
- Lobby list
- Join

Connected：

- Host/Client 身份
- LobbyID
- Host SteamID64
- Mirror connection 状态
- Host: Stop Host & Leave
- Client: Disconnect & Leave

不要做正式 UI，只需可验证功能。

### 6. Validation fallback

保留现有 KCP + LatencySimulation。

只有现有双进程验证启动参数存在时，validation bootstrap 才显式把 NetworkManager Transport 切回 KCP/Latency。

更新任何 Boot 场景生成/修复工具，避免其再次恢复旧 `NetworkManagerHUD` 或覆盖 Fizzy wiring。

Windows 开发构建确保 exe 同目录存在 `steam_appid.txt`，内容为 `480`。

正式 Steam AppID/depot 不属于本任务。

## Reliability requirements

Steam 异步操作必须防止重复 Create/Refresh/Join。

正确处理：

- Steam IO failure
- Create/Join failure
- 非法或缺失 host SteamID
- Lobby owner 与 metadata host 不一致
- Fizzy/Mirror 连接失败或超时
- Host 意外退出
- Client 意外断线

失败后必须能回到可再次 Create/Refresh/Join 的稳定状态。

避免增加第二套 Steam 生命周期管理器。

## Tests

增加必要的 EditMode/PlayMode 测试，至少覆盖：

- Lobby metadata/filter/parser
- SteamID64 validation
- protocol mismatch
- Boot 默认 Transport 为 Fizzy
- NetworkManagerHUD 已移除
- KCP validation fallback 仍然有效
- 无 Steam 的自动化测试仍可通过 KCP 启动 Host、加载 Gameplay、创建 Player

运行现有相关 NetworkCombat 和 Boot 回归测试。

## Acceptance

最终应能用两台 Windows PC、两个 Steam 账号和同一 Windows64 build 完成：

1. A 创建 Lobby。
2. Lobby ready 后 A 成为 Mirror Host。
3. B Refresh 能发现该 Lobby。
4. B Join 后通过 Fizzy 连接 A。
5. Host 上存在两个 Mirror connection，两端都看到两个 NetworkPlayer。
6. B Leave 后 A 继续正常 Host。
7. B 能再次 Late Join。
8. A Stop Host 后 Lobby 不再可搜索。
9. B 自动断线并回到 Boot Idle。
10. 两端 Gameplay、Mirror、Fizzy、Lobby 状态全部正确清理。
11. 清理后双方可以再次创建或加入 Lobby。

## Scope boundaries

本任务只实现：

- Windows 64-bit
- Public Lobby
- AppID 480 开发验证
- Steamworks.NET + FizzySteamworks + Mirror

不要实现：

- Steam 好友邀请
- Private/Friends-only Lobby
- 密码房
- Steam Authentication
- Dedicated Server
- Host Migration
- 正式 AppID/depot 发布流程

优先最小增量修改现有架构。不要为了实现 Lobby 重写现有 Mirror Gameplay、Combat 或 Player 生命周期。