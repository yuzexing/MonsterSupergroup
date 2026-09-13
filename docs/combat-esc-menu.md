# 战斗 ESC 菜单与验收

## 使用

从首页建房、开始战斗后，按 Esc 或点击左上角“菜单 [Esc]”。倒地和等待角色同步时仍可打开。角色统计和法术统计显示自己的当前属性，Host 同样只显示自己。法术按四个武器槽位展示，装备等级从 1 开始。

战斗始终继续：怪物继续移动、角色仍会受伤、自动攻击继续。菜单仅屏蔽本地新输入；已开始的冲刺、大招和攻击继续执行。选项、反馈意见保留入口，暂不执行操作。

“退出本局”打开确认框，默认焦点是“取消”。Esc 先取消确认，再按一次关闭菜单。普通客户端只离开自己；Host 结束整个会话。确认后沿用网络断开、Steam 离厅、Gameplay 卸载流程返回首页，不退出程序。

## 参考与实现边界

参考 `F:/DecomplieLatest/The.Spell.Brigade/ExportedProject/Assets/Scenes/VerdantMeadows.unity` 中 PauseMenu_Canvas 的继续、选项、反馈、退出、统计切换和确认框结构。参考程序集相关方法为空桩，没有复制其暂停运行方式或累计战绩数据。

- CombatUI 根节点挂载 `NetworkGameplayMenuController`，生成一套 UGUI，复用现有 EventSystem。全屏遮罩挡住底层 UI；菜单期间保留但不绘制调试面板，F2/F3 不改变原展开状态。
- `GameplayMenuInput` 连接 Rewired 菜单操作；控制器同时监听原生 Escape，按帧去重，避免同一次键盘输入开关两次。不进入旧 PauseMenuController，不改 Time.timeScale。
- `PlayerMovement.SetMenuInputBlocked` 与加载、倒地、选卡状态分开；只在输入入口拦截，不修改进行中动作的可执行条件，也不授予无敌。
- `CardPickMenu.SetPresentationSuppressed` 保留绑定、奖励队列、待确认请求和装备目标步骤，隐藏时禁止提交、返回和抢焦点，服务器回复继续更新。
- `GameplayMenuSnapshotReader` 每 0.2 秒按非缩放时间采集自己拥有角色。生命与 HUD 同源；等级来自已同步进度，武器属性来自当前 GAS 运行时；冲刺使用只读 ReadSnapshot，普通冷却仅计算现有计时值，不调用推进恢复或更新缓存的采集接口。
- 武器标识复用准备页的图标配置；当前六种武器尚无 Sprite 引用，沿用“斩、焰、息、环、径、召”徽记，配置 Sprite 后自动显示图片。未新增美术资源。
- 不统计累计伤害、DPS、击杀，也不把当前武器属性当作包含条件触发后的最终伤害。

## 可重复自动化

Unity PlayMode 过滤器：`GameplayMenuTests;CardPickMenuTests;PlayerDashMovementTests;PlayerWeaponCooldownRuntimeTests;PlayerDebugSnapshotTests`。

在项目根目录执行：

```powershell
# 三进程 KCP：Host + A + B，后台运行，不使用 Steam 账号。
./Tools/Run-CombatMenuValidation.ps1 -Scenario party

# 真实渲染窗口，采集菜单、属性、选卡恢复、确认框和返回首页图片。
./Tools/Run-CombatMenuValidation.ps1 -Scenario solo -Visible -Width 1280 -Height 720
./Tools/Run-CombatMenuValidation.ps1 -Scenario solo -Visible -Width 1920 -Height 1080

# 非 Development 验收包，同样支持上述场景。
./Tools/Run-CombatMenuValidation.ps1 -Scenario party -Executable Builds/MenuRelease/MonsterSupergroup.exe
```

脚本只管理自己启动的进程。验收探针仅在 Editor 或 `MONSTER_MENU_VALIDATION` 包内启用，普通发布构建保留菜单功能。构建入口复用 `MonsterSupergroup.Gameplay.Tests.PreparationMenuValidationBuild.Build`；环境变量 `MENU_RELEASE=1` 生成非 Development 包，其他值生成 Development 包。

三进程用实际 Boot / MainMenu / Gameplay 和怪物资产，观察服务端收到的快照序号、位置、采样时间以及自动攻击冷却报告；随后验证客户端离开、怪物转移、Host 确认退出、再次建房、菜单打开时 Host 非通知断线以及离线单人重开。

## 人工步骤

1. 正常开局。按 Esc、点继续、再点左上角入口；用方向键、Tab、Shift+Tab、Enter 和滚轮操作。检查角色统计 / 法术统计切换及重复打开后的页签、槽位、滚动位置。
2. 打开菜单前按住移动，菜单内继续尝试移动、瞄准、冲刺、大招和交互；均不应产生新操作。正在执行的冲刺或大招正常结束；自动攻击、怪物移动及受伤继续。
3. 等级奖励选择和装备目标选择时打开菜单，等待新的升级或选择回复，关闭后检查保留正确步骤，无菜单点击穿透。角色处于 Downed 时仍可打开，关闭后不能移动。
4. 对照 HP/XP HUD 及 Player Debug 检查数值；更换武器、升级装备后核对槽位、属性和等级。冲刺武器显示“使用冲刺次数”，空槽位显示“空槽位”。
5. 客户端退出确认框取消后继续战斗；确认后自己回首页，Host 和另一客户端继续。多人 Host 退出提示其他成员也会断开；确认后所有成员回首页。重建房间可继续开局。
6. 在两个真实 Steam 账号中额外验证退出后大厅成员释放、Host 结束后大厅清理和再次邀请加入。此项需真实账号人工核验，KCP 自动化不替代它。

## 结果与产物

2026-09-12，本机 Windows / Unity 6000.3.17f1 实测：

| 检查 | 结果 | 记录 |
|---|---|---|
| PlayMode 回归：菜单、选卡、冲刺、冷却、只读快照 | 62 / 62 通过 | `Logs/CombatMenu-PlayMode-Final.xml` |
| 初始滚动、焦点、选卡恢复修复后回归 | 12 / 12 通过 | `Logs/CombatMenu-UI-Final.xml` |
| 补充受伤、菜单期间升级、倒地、无 Owner、Owner 返回、禁用清理 | 2 / 2 通过 | `Logs/CombatMenu-OwnerLifecycle.xml` |
| Development 构建 | Success | `Logs/CombatMenu-Build-Development.log` |
| 非 Development 构建 | Success | `Logs/CombatMenu-Build-Release.log` |
| Development 三进程完整退出 / 重开流程 | Host、A、B 全部 PASS | `Logs/PreparationMenu/20260912-012231-combat-menu/` |
| 非 Development 三进程完整退出 / 重开流程 | Host、A、B 全部 PASS | `Logs/PreparationMenu/20260912-012334-combat-menu/` |
| Development 1280×720 / 1920×1080 | PASS；已检查截图 | `20260912-012749-combat-menu-solo` / `20260912-012808-combat-menu-solo` |
| 非 Development 1280×720 / 1920×1080 | PASS；已检查截图 | `20260912-012923-combat-menu-solo` / `20260912-012934-combat-menu-solo` |
| Steam 真实大厅退出 / 再邀请 | 待真实账号人工核验 | 按上方人工步骤 6 |

UI 和生命周期回归包含重复测试，不应与 62 项简单相加。窗口截图子目录均位于 `Logs/PreparationMenu/`，包含 `combat-character.png`、`combat-spells.png`、`combat-confirm.png`、`combat-over-cards.png`、`combat-card-restored.png`、`combat-equipment.png` 和 `combat-return-home.png`。

窗口复核修正了首次打开时滚动到底部、按钮焦点不明显，以及菜单入口压住 HP 文字的问题。最终入口位于左上角生命条下方；菜单内隐藏底层 HUD 的绘制，退出菜单恢复原 Canvas 状态，数据更新不受影响。

三进程最终样本：全部菜单打开的 3 秒窗口内，Development 的三个模拟者分别有 58 / 57 / 55 个有效移动快照，位移约 6 单位；非 Development 为 60 / 57 / 58 个，位移约 5.97–6.05 单位。两种包中，每名玩家均新增 1 个服务端接受的周期火球攻击报告。客户端离开后怪物转移，Host 结束、第二次组队时 Host 非通知断线、返回首页再开离线单人均通过。

自动化通过真实 UGUI 按钮、方向 / 提交事件及同一个 Esc 处理入口驱动，并检查同帧重复调用不会双重开关。物理按键操作按人工步骤 1 检查；Steam 核验未用模拟结果替代。

验收包入口：

- `Builds/MenuDevelopment/MonsterSupergroup.exe`
- `Builds/MenuRelease/MonsterSupergroup.exe`

复制或分发时保留整个对应目录，包括 `_Data`、DLL 和 `steam_appid.txt`，不要只复制 exe。正常启动不会自动运行探针。普通启动日志：`%USERPROFILE%/AppData/LocalLow/DefaultCompany/Monster Supergroup/Player.log`；自动化显式指定独立日志目录。
