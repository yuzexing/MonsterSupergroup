# Steam 官方直接邀请验收

## 当前实现与验收边界

准备房间的“邀请朋友”现在打开游戏内好友页。选中一位好友后，本机调用 `SteamMatchmaking.InviteUserToLobby(expectedLobbyId, friendSteamId)`。普通邀请入口不读取叠加层开关，也不打开 Steam 好友面板。

好友页显示所有已建立关系的好友，排除自己；在线优先，名称、SteamID 稳定排序。支持名称搜索、滚动条、鼠标滚轮、刷新、返回。每 2 秒更新好友缓存，保留搜索、滚动位置和按 SteamID 识别的选择焦点。键盘使用 Tab / Shift+Tab、方向键、Enter；Esc 返回房间。正在输入时可按 Tab 移到邀请按钮。

已在大厅的好友禁用邀请。离线好友仍可手动邀请。每次发送前重新校验 Steam 登录、准备阶段、页面对应的大厅、自己及房主的大厅成员资格、容量、好友关系和对方是否已入厅。房主和已加入的客户端均可邀请。已提交的同一好友邀请有 5 秒重试间隔；失败可以手动重试，不结束当前房间、不自动改成离线房间、不预占席位。

**“邀请已提交，等待好友接受”只表示 Steam 接受提交，不表示对方收到或加入。** 完成标准仍是另一真实账号收到邀请、接受后进入同一准备房间并能继续开局。[官方 API 的返回值与接受行为](https://partner.steamgames.com/doc/api/ISteamMatchmaking#InviteUserToLobby)

本次范围为：两个账号均已登录 Steam、已成为好友，并且都已启动本项目。保持 AppID **480**，不改变 Steam 登录、启动策略。保留 `+connect_lobby` / `--connect-lobby=` 解析，但不验收从 Steam 自动启动本项目程序。不要把账号测试用的 Spacewar 启动行为当作本项目已启动。

## 两个真实账号的人工步骤（待用户执行）

1. 两台电脑或各自独立的 Steam 会话分别登录 A、B；使用同一版本的完整验收包，两个游戏都停在首页。B 不要先创建其他准备房间。
2. A 点击“游戏”，进入 Steam 好友准备房间。确认页面显示大厅 ID。点击空席位“邀请朋友”，在游戏内搜索 B。
3. A 点击 B 的“邀请”。预期显示“邀请已提交，等待好友接受”，按钮 5 秒内不可重复提交。整个发送过程不应唤起叠加层。
4. **由 B 核实真实 Steam 大厅邀请已到达**，再通过 Steam 的邀请通知接受。接受后依次显示加入大厅、连接房主、身份验证/准备房间同步进度。收到邀请和点击接受是人工步骤，不由自动化发送或代点。
5. 双方核对准备页大厅 ID 一致、两名成员各有唯一 P 编号。分别选择武器、准备，由 A 开始，确认两边进入战斗。
6. 关闭 Steam 游戏内叠加界面，再重新建房并执行 2–5。可在游戏外的 Steam 通知/聊天中接受邀请；直接发送不依赖叠加层。
7. 分别核验重复邀请、B 已在大厅、房间满员、A 开始加载、A 离开后 B 接受旧邀请。应禁用或给出原因，无重复连接、席位预占或错误离线回退。
8. B 已在另一会话时接受邀请，应提示先离开。重复接受当前大厅邀请，应忽略而不重启连接。

验收包路径：

- Development：`Builds/MenuDevelopment/MonsterSupergroup.exe`
- 非 Development：`Builds/MenuRelease/MonsterSupergroup.exe`

请复制整个目录（包含 Data、UnityPlayer.dll、steam_appid.txt 等），不要只复制 exe。两个包均包含显式命令行启用的验收探针；正常双击不会加载模拟好友，不会自动邀请。

## 日志定位

正常启动的日志：`%USERPROFILE%/AppData/LocalLow/DefaultCompany/Monster Supergroup/Player.log`。Unity 编辑器查看 Console。也可给进程指定 `-logFile <绝对路径>`。自动化日志和截图放在项目 `Logs/PreparationMenu/<时间>-invites/`。

搜索 `[SteamInvite]`，按顺序核对：

| 阶段 | 日志及含义 |
| --- | --- |
| 发送 | `stage=send lobby=... friend=... result=submitted`：仅提交成功；其他 result/reason 表示拒绝或失败 |
| 接受回调 | `stage=join_requested lobby=... friend=...`：B 的 Steam 接受回调已到游戏 |
| 请求入厅 | `stage=join_lobby lobby=... result=requested` |
| 入厅结果 | `stage=lobby_enter expected=... response=... ioFailure=...` |
| 游戏连接 | `stage=game_connected lobby=... host=...`：Mirror 传输已连接；认证尚需独立确认 |
| 身份认证 | `stage=authentication ... accepted=True`；失败显示 reason，不输出恢复 token |
| 准备快照 | `stage=preparation_snapshot lobby=... participant=... run=... members=...` |
| 异常及清理 | `stage=failed`、`stage=cleanup`，包含阶段、大厅和原因 |

如果 A 只有 submitted、B 没有 join_requested，先人工确认 B 是否真实收到并接受；不能用 A 的返回值替代 B 的结果。如果 B 有回调但未入房，继续按大厅加入、传输、认证、快照的日志定位。

`Monster Supergroup → Network Combat → Steam → Open Lobby Invite Dialog` 只保留为叠加层诊断入口，调用 `OpenLobbyInviteOverlay()`；它与正式按钮分开。`overlay-active=True` 仅表示叠加层激活，永远不作为邀请成功判据。

## 可重复自动化

EditMode 筛选 `SteamLobbyInvitationTests`、`SteamLobbyTests`；PlayMode 筛选 `SteamInviteUiTests`。好友查询、大厅查询、发送均替换为内存适配器；没有自动发送真实 Steam 邀请的测试。

打包方法继续使用 `MonsterSupergroup.Gameplay.Tests.PreparationMenuValidationBuild.Build`，环境变量 `MENU_RELEASE=1` 为非 Development；其他值为 Development。

```powershell
# 在项目目录运行，显示测试游戏窗口并保存布局截图；不依赖 Steam 登录或叠加层。
./Tools/Run-PreparationMenuValidation.ps1 -Profile invites -Visible -Width 1280 -Height 720
./Tools/Run-PreparationMenuValidation.ps1 -Profile invites -Visible -Width 1920 -Height 1080 -Executable Builds/MenuRelease/MonsterSupergroup.exe
# 现有通用联机/开局回归
./Tools/Run-PreparationMenuValidation.ps1 -Profile party
```

模拟好友页明确标注验收数据，覆盖 60 个好友、离线好友、已有成员、搜索、定时刷新、焦点与滚动保留、键盘发送、5 秒重试、失败保房、旧大厅事件及关闭清理。测试只证明本地逻辑和界面；真实邀请送达、Steam 回调和双账号入房仍需上面的人工核验。

## 2026-09-11 本轮执行结果

| 项目 | 实际结果与证据 |
| --- | --- |
| EditMode | **25/25 通过**：`Logs/SteamDirectInvite-EditMode.xml`，含直接邀请规则及已有 Steam 元数据/状态测试 |
| PlayMode | **2/2 通过**：`Logs/SteamDirectInvite-PlayMode.xml`，含真实 Boot/MainMenu 好友页及 Steam 清理回归 |
| Development 构建 | **成功**：`Logs/SteamDirectInvite-DevelopmentBuild.log`；包在 `Builds/MenuDevelopment` |
| 非 Development 构建 | **成功**：`Logs/SteamDirectInvite-ReleaseBuild.log`；包在 `Builds/MenuRelease` |
| Development 1280×720 | **通过并检查截图**：`Logs/PreparationMenu/20260911-203135-invites/` |
| Development 1920×1080 | **通过并检查截图**：`Logs/PreparationMenu/20260911-203209-invites/` |
| 非 Development 1920×1080 | **通过并检查截图**：`Logs/PreparationMenu/20260911-203251-invites/` |
| 非 Development 1280×720 | **通过并检查截图**：`Logs/PreparationMenu/20260911-203334-invites/` |
| KCP 多进程回归 | **5 个进程全部通过**：`Logs/PreparationMenu/20260911-203209-party/`；Host + 3 Client + 被拒绝的第五人，覆盖准备、开局、武器基线、稳定身份/Downed 重连、加载中断清理及再次单人开局 |
| 真实 Steam 邀请送达、接受入房和开局 | **待用户使用两个真实账号执行**；本轮没有向任何真实好友发送邀请 |

首次窗口检查发现默认滚动位置在列表末尾、底部提示裁切；修正后重新构建、运行并检查了表中的最终窗口。自动化还验证持续刷新可见行不会推迟每 2 秒的好友采集。截图包括 `friends-list.png`、`friends-search.png`、`friends-scroll-keyboard.png`、`friends-submitted.png`。

本轮包保持 `steam_appid.txt = 480`。构建和进程日志还包含已有第三方资源/包警告及退出时 ComputeBuffer 回收提示；邀请测试未出现未处理异常。两个包的好友来源在正常启动时为真实 Steam，在显式 `--menu-profile=invites` 验收模式时才替换为模拟数据。

## 历史记录：2026-09-11 叠加层方案复测（直接邀请实现前）

以下保留修改前的实测记录；其中“尚未实现”描述当时版本，不代表上面的当前直接邀请入口。

项目使用 AppID **480**（Spacewar），不是 408。本机已用 480 成功初始化、创建大厅并打开 Steam 叠加层，但未验证大厅邀请发送成功。最初把打开好友列表记录成打开邀请面板，后续实测已纠正这一结论。

| 测试方式 | Steam 初始化与创建房间 | 点击邀请朋友 |
| --- | --- | --- |
| Unity 6000.3.17f1 编辑器，Steam 网络底座 | 成功 | 叠加界面不可用，Shift + Tab 也未打开 |
| 将 `F:\Dev2\Monster Supergroup.exe` 添加为非 Steam 游戏，从 Steam 库启动 | 成功，AppID 仍为 480 | 打开叠加层及普通好友列表，但好友菜单缺少“邀请加入游戏” |

编辑器中点击邀请时的实际诊断日志：

```text
[SteamInvite] initialized=True editor=True backendSteam=True hasLobby=True phase=Preparing graphics=Direct3D12 appId=480 loggedOn=True overlayEnabled=False secondsSinceInit=19.7.
```

用户已确认 Steam 游戏内叠加界面设置开启。以上结果把当前编辑器故障定位到叠加界面未能在编辑器窗口中正常工作，而非 AppID、账号登录或大厅创建失败。独立游戏对照使用现有打包版本，本次未重新打包；没有向任何好友发送邀请，也没有验证另一账号接受邀请后的连接。

后续在游戏运行约八分钟时重新点击“邀请朋友”，仍只打开普通好友列表，因此该现象不能简单归因于刚启动尚未加载。截图中的“邀请观看”和“远程畅玩邀请”分别用于观看和串流，不会让另一个游戏客户端加入本项目大厅；列表上方的“邀请任何人加入游戏”也是 Remote Play Together 功能。

本机 Steam 客户端的好友菜单实现会检查自己的游戏 AppID、大厅信息或连接字符串，再决定是否显示“邀请加入游戏”。项目创建的是私有大厅，调用 `ActivateGameOverlayInviteDialog(CurrentLobbyId)`，没有设置 Rich Presence 的 `connect`，也没有独立调用 `InviteUserToLobby` 的好友选择入口。缺少 `connect` 本身不是大厅邀请 API 的必要条件；不能据此断言添加 Rich Presence 就能修好。现有证据说明普通好友菜单未取得可用的邀请信息，但尚未确定专用邀请对话框没有出现的最终原因。

后续可增加游戏内好友选择入口，通过 `InviteUserToLobby(lobbyId, friendId)` 直接发送大厅邀请，并用另一测试账号确认收到链接以及 `GameLobbyJoinRequested_t` 入房回调。该入口尚未实现；从 Steam 启动已解决叠加层显示问题，尚不能视为完整邀请流程已修复。

Steamworks.NET 官方说明：Unity 编辑器的叠加界面经常不能正常工作；叠加层需要在图形设备初始化前接入，从 Steam 启动游戏可让 Steam 在进程启动时完成接入。单凭 `IsOverlayEnabled() == false` 无法判断用户是否关闭了设置，也可能是尚未加载完成。

### 当时的叠加层诊断步骤（非当前正式邀请流程）

1. 编辑器网络底座：`Monster Supergroup → Network Combat → Editor Backend → Steam`，随后进入播放模式。
2. 主菜单点击“游戏”创建好友准备房间，再点击“邀请朋友”。
3. Unity Console 搜索 `[SteamInvite]`。也可以在播放模式使用 `Monster Supergroup → Network Combat → Steam` 下的菜单：
   - `Log Invite Diagnostics`：输出当前状态。
   - `Open Lobby Invite Dialog`：检查前提并请求打开叠加层邀请面板；当前只作为诊断入口。
4. 正式验收叠加界面时，从 Steam 库启动打包游戏；本机已经添加名为 `Monster Supergroup` 的入口，目标为 `F:\Dev2\Monster Supergroup.exe`。
5. 等待加载完成，按 Shift + Tab 检查叠加界面，再在准备房间点击“邀请朋友”。更换打包目录后，需要更新 Steam 库中的启动目标。

### 旧诊断日志与当时修复

- `initialized=False` 或 `loggedOn=False`：先排查 Steam 初始化或登录。
- `hasLobby=False` 或 `phase` 不是 `Preparing`：先创建或加入准备房间。
- 上述条件正常而 `overlayEnabled=False`：检查启动方式、编辑器限制、叠加界面开关，以及是否尚在加载。不可将该返回值直接解释为设置关闭。
- `Invite dialog requested`：已调用 `ActivateGameOverlayInviteDialog`；该方法没有成功返回值，这行日志本身不代表面板已经显示。
- `overlay-active=True`：收到了 `GameOverlayActivated_t`，表明叠加层已激活。仍需观察是否显示正确的邀请面板；此回调不是好友收到邀请的确认。

本次在 `SteamLobbyService` 增加诊断日志、叠加界面激活回调及对应释放，并修正未登录、未建房、叠加界面不可用的提示。新增菜单只在播放模式且存在服务时可用。Unity 已重新编译，并通过编辑器创建房间、点击邀请的实测验证新日志和新提示。以上新诊断代码需在下次重新打包后才会出现在独立游戏中。

## 参考

- [Steamworks 官方 Spacewar 示例与 AppID 480](https://partner.steamgames.com/doc/sdk/api/example)
- [Steamworks.NET：编辑器与叠加界面限制](https://steamworks.github.io/faq/)
- [ActivateGameOverlayInviteDialog](https://partner.steamgames.com/doc/api/ISteamFriends#ActivateGameOverlayInviteDialog)
- [IsOverlayEnabled](https://partner.steamgames.com/doc/api/ISteamUtils#IsOverlayEnabled)
