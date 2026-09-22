# Steam 好友邀请与直接加入

## 当前接入（2026-09-22）

正式 AppID 为 **4886160**。准备房间使用 `FriendsOnly`：房内任意成员均可邀请自己的好友；好友可通过 Steam 好友列表加入，包括与房主没有好友关系的人。大厅不加入公开搜索。

每名完成游戏认证的成员都在准备阶段、有空位时发布 `connect=+connect_lobby <大厅ID>`。游戏内“邀请朋友”仍打开项目自己的好友页，通过 `SteamFriends.InviteUserToGame` 发送这一连接字符串，保留五秒重试间隔。Steam 桌面好友菜单和 Shift+Tab 好友菜单可以使用同一加入信息。满员、开始游戏、退出和断线时清除加入信息；Steam UI 可能暂时缓存入口，最终仍按房间容量和本局成员资格检查。

玩家主动接受邀请或点击“加入游戏”后，直接退出当前会话，完成网络和场景清理后加入目标房间，不增加确认框。房主这样操作会解散原房间。收到通知本身不会退房。连续接受不同邀请以最后一次为准，同一邀请不重复连接；清理超过 30 秒取消自动加入。加入失败留在菜单说明原因，不自动创建本地单人房。

保留旧式大厅邀请的 `GameLobbyJoinRequested_t`；连接字符串邀请使用 `GameRichPresenceJoinRequested_t`。启动时读取 Steam `GetLaunchCommandLine`（没有参数时兼容进程启动参数），运行中处理新 URL 启动参数。支持 `+connect_lobby <ID>` 和 `--connect-lobby=<ID>`，必须为有效大厅 ID。

**“邀请已提交”不是送达确认。** 必须由接收账号核实 Steam 通知或聊天中的真实邀请，再核实接受后的游戏回调和最终准备房间。不存在由本地发送返回值推导出来的送达回执。

## 根因边界

- 旧实现创建 Private 大厅，仅支持邀请加入；没有发布 `connect`，没有处理 Rich Presence 加入回调，好友列表入口不完整。
- 旧实现遇到已有会话直接拒绝邀请。真实历史日志 `Logs/SteamCrashDiagnosis/20260922-094145/steam-2045/Player.log` 中有 `session_busy`；退出原房间后同一邀请加入成功。
- 非房主“提交成功但接收方完全没收到”尚未取得同场接收端证据，不能归因为房主权限。新实现统一邀请路径，并记录两端链路供三账号复测；自动化不会替代此项。
- 现有 Steam 安装包与此前构建不被本次验收构建覆盖，可保留作为修改前对照；比较时记录实际 Build ID 和版本。

## 三账号人工验收

使用同一完整 Windows 包、正式 Steam 应用入口。A 与 B 是好友，B 与 C 是好友，A 与 C 不是好友；三人均登录 Steam 并运行本项目。

| 操作 | 必须观察到的结果 |
| --- | --- |
| A 建房，B 加入；B 在游戏内好友页邀请 C | C 的 Steam 通知或聊天实际出现邀请；接受后 A/B/C 大厅 ID 相同 |
| C 退出；B 在 Steam 桌面好友菜单邀请 C | 不点击游戏内邀请按钮也能发送，C 接受后进入同一房间 |
| C 退出；在 Shift+Tab 好友菜单重复上述操作 | 同样收到真实邀请并成功加入 |
| C 无邀请，直接右键房内 B 选择“加入游戏” | 桌面与叠加层均有可用入口；C 不要求与 A 成为好友 |
| C 已建另一房间、已加入另一房间，或正在战斗时接受 | 自动清理旧会话再进入目标房，无重复角色或残留连接；C 为房主时原成员被通知离房 |
| 连续接受两个不同大厅邀请 | 最终仅连接最后接受的大厅，旧回调不覆盖新房间 |
| 房间满员、关闭、版本不同、邀请后房主离开 | 显示对应失败原因，不创建单人房，不停留在假成功状态 |
| 已开局时原成员断线 | 原成员仍可用原大厅邀请重连；非本局成员无法加入战斗 |
| C 未运行游戏时接受 | Steam 启动同一游戏包并传入大厅信息，随后加入目标房 |

正常准备、选择武器、开局及退回菜单也需回归。桌面通知关闭或勿扰可能影响弹窗，验收同时检查聊天记录；不能只看是否弹窗。

冷启动的 Steamworks 安装配置应指向实际交付程序。若后台启用“Use launch command line”，游戏通过 `GetLaunchCommandLine` 接收参数；修改 Steam 后台配置由应用管理员执行，不由测试自动化代替。

## 证据与日志

默认日志为 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Monster Supergroup/Player.log`；每台机器应在退出或再次启动前归档。亦可使用 `-logFile <绝对路径>` 指定独立日志。保存测试时间、A/B/C 角色、Build ID、大厅 ID，以及接收端通知/聊天和好友菜单截图。

按 `[SteamInvite]` 搜索：

| 阶段 | 关键记录 |
| --- | --- |
| 加入信息 | `stage=presence`：发布/清除的 connect 及结果；失败限频重试 |
| 实际发送 | `stage=native_send`：实际 AppID、发送账号、房主、大厅、目标、成员资格、InviteUserToGame 返回值 |
| 界面结果 | `stage=send`：host/member 角色、房间阶段、submitted 或拒绝原因 |
| 旧邀请到达 | `stage=received source=lobby`：仅旧式大厅邀请触发；新式连接字符串邀请不能用它判断送达 |
| 接受/直接加入 | `stage=join_requested`、`stage=accept`：来源、目标、当前会话、排队/重复/无效 |
| 切换 | `stage=cleanup`、`stage=switch`、`stage=stale_callback`：清理、超时或旧结果退厅 |
| 入房 | `stage=join_lobby` → `stage=lobby_enter` → `stage=game_connected` → `stage=authentication` → `stage=preparation_snapshot` |

非房主送达问题须在修改前后各完成一次 B→C，不能用 A→C 通过替代。若只有发送日志而 C 没有通知，保存两端 Steam 及游戏日志继续定位送达层；若有通知但无接受回调，检查启动参数和 Steam 应用入口；有回调则继续检查入厅、版本、认证和快照。

`Open Lobby Invite Dialog` 仍是旧式大厅叠加层诊断入口。叠加层激活只证明面板打开，不证明邀请发送或对方加入。

## 自动化与构建

- EditMode：`SteamLobbyConnectionTests`、`SteamLobbyInvitationTests`、`SteamLobbyTests`、`PreparationRoomTests`。
- PlayMode：`SteamInvitationSwitchPlayModeTests`、`SteamLobbyCleanupPlayModeTests`、`SteamInviteUiTests`。
- 测试使用模拟 Steam 边界；不向真实账号发送邀请。
- 构建使用统一 `build.player` 工具和独立输出目录，交付整个目录。Development 包包含 AppID 调试文件；上传 Steam depot 时排除 `steam_appid.txt`。
- 本轮实际执行结果记录在 `docs/steam-social-fix-validation.md`；真实送达、原生好友菜单与三账号验收单独记录，不与模拟测试混算。

## 官方参考

- [FriendsOnly 与大厅邀请](https://partner.steamgames.com/doc/api/ISteamMatchmaking)
- [SetRichPresence、InviteUserToGame 与加入回调](https://partner.steamgames.com/doc/api/ISteamFriends)
- [Steam 启动参数](https://partner.steamgames.com/doc/api/ISteamApps#GetLaunchCommandLine)
