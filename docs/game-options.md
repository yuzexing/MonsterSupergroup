# 游戏选项与菜单本地化

从主菜单的“选项”或战斗菜单的“选项”进入同一个面板。战斗中打开选项仍保持原有规则：拦截本地移动／手动操作，战斗和自动攻击继续，角色仍可能受伤。

## 设置的实际效果

- **常规：** 简体中文／英文即时切换；镜头震动同时控制本地玩家受伤和大招震动，关闭后停止当前震动。
- **音频：** 总音量控制 FMOD `bus:/`，音乐控制 `bus:/mx`，音效控制 `bus:/sx`（包括武器、受伤、环境和菜单音效）。音频库暂未就绪时保留设置，库加载后自动应用；不绕过 Unity 编辑器的 Mute Audio。
- **画面：** 窗口／无边框／独占全屏、显示设备支持的分辨率与刷新率、垂直同步、帧率上限、50%～150% 渲染比例、设备支持的 MSAA、纹理 mipmap 限制。MSAA 主要处理几何边缘，纹理质量只影响支持 mipmap 限制的纹理。

音量、语言、震动即时应用并保存。画面编辑先保留草稿，点击“应用”才生效；关闭面板丢弃未应用的画面草稿。“恢复此页默认值”只恢复当前页，画面页仍需应用。

显示模式／分辨率变更需在 15 秒内确认，否则自动恢复。确认前保存其他设置不会持久化试用中的显示模式。编辑器内禁用显示模式和分辨率控件，应在独立运行的游戏中验证。垂直同步开启时，帧率由显示器控制；关闭后恢复先前选择的帧率上限。

默认简体中文、音量 100%、震动开启、60 FPS、垂直同步关闭，初始显示和画质沿用项目配置。设置使用独立的本地 PlayerPrefs 键 `MonsterSupergroup.Options.v1`，不依赖 Steam，不改写旧 `SettingsData`。损坏或不支持的设置会回退；专用服务器不创建本地设置服务。URP 设置作用于运行时副本，退出播放时恢复原资源引用与质量参数。

## 本地化维护

TheSpellBrigade 确认使用 Unity Localization，但当前导出程序集缺少方法体，语言表缺少条目；本项目重新接入相应功能，没有复制空程序集或空语言表。

本项目使用 Unity Localization 1.5.13，Localization Tables 是唯一翻译来源。维护、CSV 合并和新增语言见 [本地化维护说明](localization/README.md)。构建只校验表格并构建 Addressables，不生成或覆盖译文。

翻译覆盖选项、主菜单／准备房间／好友列表、战斗菜单中的固定文案、动态提示和当前目录中的武器／装备名。参数化条目保留人数、数值和大厅编号；玩家昵称原样显示。新增菜单内容名通过 `weapon.<ID>` 或 `equipment.<ID>` 条目提供翻译。缺失条目回退中文或原文。战斗 HUD、升级卡片及旧 I2 系统不在此次迁移范围内。

## 验证入口

- PlayMode：`GameOptionsTests`、`GameplayMenuTests`、`GameplayCameraTests`，覆盖设置解析、真实 FMOD 总线、语言刷新、运行时画质、草稿丢弃、菜单返回、镜头反馈。
- Windows 验证构建：执行 `MonsterSupergroup.Gameplay.Tests.GameOptionsValidationBuild.Build`；输出 `Builds/OptionsValidation/MonsterSupergroup.exe`。这是带验证代码的 KCP 开发版。
- 运行 `Tools/Run-OptionsValidation.ps1`：检查音频 DSP 输出、全屏与窗口切换、15 秒回退、主菜单／战斗菜单、跨场景及独立进程重启恢复，输出截图与日志至 `Logs/OptionsStandalone/<时间>/`。工具结束时恢复运行前的设置。
- `Tools/Run-PreparationMenuValidation.ps1` 可以针对该验证构建继续运行本地单人、多人菜单和模拟好友邀请回归；模拟邀请不需要真实好友操作。

本次不提供 3D 阴影、各向异性过滤、手柄震动、主播模式或数据收集开关。

## 验证记录（2026-09-12）

按测试名称去重，共 50 项自动化测试通过。最终 Windows 验证构建成功，构建日志为 `Logs/OptionsBuildFinal.log`。

- `Logs/OptionsFinalPlayMode.xml`：13 项通过，涵盖设置、菜单及镜头；随后补充“恢复此页默认值”的检查，`Logs/OptionsResetPlayMode.xml` 的 5 项设置测试全部通过。
- `Logs/OptionsDropdownPlayMode.xml`：5 项设置测试再次通过，新增实际打开语言下拉框并选择条目的检查，确认关闭列表后的键盘焦点仍在语言控件上。
- `Logs/OptionsNetworkRegression.xml`：36 项房间状态与 Steam 大厅／邀请测试通过。
- `Logs/OptionsStandalone/20260912-123054/`：Windows 独立版完整验证通过；30 FPS 上限实测约 29.96 FPS，验证了无边框／独占／1024×768 窗口、15 秒超时回退、确认后保存、中英主菜单／角色及武器统计、跨场景、重启恢复和专用服务器跳过。测试完成后恢复原有用户偏好。
- `Logs/PreparationMenu/20260912-123211-party/` 与 `20260912-123211-combat-menu/`：多人准备房间、满员拒绝、掉线重连、载入中断清理和战斗菜单回归通过。
- `Logs/PreparationMenu/20260912-123533-invites/` 与 `Logs/OptionsInvitePlayMode.xml`：独立版和编辑器的模拟好友邀请检查通过；语言切换保持搜索框、光标、昵称及滚动位置，邀请提交、重试和房间切换清理正常。
- Windows 独立版已验证音乐、武器挥击、受伤、菜单确认四类声音的分类静音、总静音与恢复。检测位置为 FMOD 总线输出端 `HEAD`，而非音量处理前的输入端，见 [FMOD 官方说明](https://fmod.com/docs/api/content/generated/FMOD_ChannelControl_GetDSPClock.html)。
- 验证程序会先尝试读取真实屏幕；隐藏窗口没有可读缓冲区时，临时使用当前相机和菜单画布离屏渲染，日志明确标记 `Offscreen layout capture`。这类图片用于检查布局；全屏模式、窗口宽高、帧率和回退检查仍读取实际运行状态。离屏图中的文字也会经过画面渲染比例缩放，与正常屏幕叠加 UI 的清晰度可能不同。

验证构建使用 KCP；Steam 邀请回归使用模拟接口，不会向真实好友发送消息或邀请。
