# 玩家界面本地化维护

Unity Localization 1.5.13 是唯一运行时本地化系统。正式启用简体中文 `zh-CN` 和英文 `en`，默认简中。网络只传内容 ID、等级和稀有度，各客户端使用自己的语言。

## 翻译来源

打开 **Window → Asset Management → Localization Tables**：

| Collection | 用途 |
| --- | --- |
| MonsterMenus | 首页、准备房间、好友页、ESC、选项、结束页、HUD、选卡、提示，以及保留的旧 UI |
| MonsterContent | 武器、装备、Perk、大招、角色与地图的名称、说明、引言 |
| MonsterFonts | `ui.font` 对应 UGUI Font；`ui.tmp` 对应 TMP Font Asset |

表格资源位于 `Assets/_Project/Localization/Tables`。本目录 CSV 是导出副本，不是自动生成表格的输入。构建只校验，不改译文。旧 `MenuStrings.json`、I2 插件、语言表与适配接口已从 Assets 移除。

## 修改和增加内容

1. 修改现有翻译：直接编辑表格；保留 Key 和 Id。
2. 新增玩法内容：先创建资产和分配稳定内容 ID，选中资产后执行 **MonsterSupergroup → 工具中心 → 创建内容本地化条目**，再填写各语言名称与说明。
3. 装备等级共用参数化说明；确有不同文案时，在该等级的 `LocalizedDescription` 绑定专用条目。留空表示使用装备公共说明。
4. 修改名称不修改键。例如 `weapon.2.name`；Perk 稀有度和装备等级不改变网络内容 ID。
5. 执行 **MonsterSupergroup → 校验 → 本地化**，再使用 **工具中心 → 导出本地化 CSV** 更新本目录副本。

## 说明参数

装备和 Perk 说明启用 **Smart**。参数从当前等级／稀有度的 GAS 配置读取；翻译不保存数值。示例：`伤害提高 {damagePercent:0.##}%。`

| 参数 | 来源和单位 |
| --- | --- |
| damagePercent / attackSpeedPercent / sizePercent / durationPercent / knockbackPercent | 对应增幅 × 100，百分比 |
| critChancePercent / critDamagePercent | 对应增量 × 100，百分点 |
| projectileCount | 射弹数量增量，整数 |
| chancePercent / hitCount / interval | 已支持的燃烧效果概率、执行次数、间隔秒数 |

参数转换集中在 `ContentText.Arguments`。使用现有 GAS 类型的新内容无需改代码；新增效果类型时，应在这里增加明确的参数和单位映射，并补充格式验证，不能从译文中的 `%` 猜单位。读取文本不会推进战斗状态。

## CSV 往返

从上述导出菜单获得 UTF-8 CSV，保留 Key、Id 和列名。批量修改后，在对应 Collection 的 CSV 菜单使用 **Import (Merge)**。不要删除未编辑的语言列、修改条目 Id，或将不同 Collection 的 CSV 混用。

Smart 标记属于插件表格元数据，不是默认 CSV 的一列。Merge 会保留已有标记；新增装备／Perk 条目通过内容工具创建并启用 Smart。提交表格、Shared Data 和 CSV；不要只提交 CSV。

## 新增语言

添加 Locale，在两个 String Collection 中添加该 Locale 的表、补齐译文，在 MonsterFonts 中配置字体。选项列表从已配置 Locale 自动生成，无需修改中英判断代码。所有正式启用语言均参与构建缺译校验。

Locale 的 **Locale Name** 是语言选择项显示的名称（例如“简体中文”“English”），Sort Order 控制顺序；语言代码用于保存设置，修改显示名称不修改代码。

字体已随项目保存。UGUI 采用覆盖中英的字体以显示中文昵称；英文 TMP 使用拉丁字体并回退到中文 TMP，保留符号字体回退。新增语言需要覆盖其字符的 UGUI 和 TMP 字体。

运行时先加载默认语言和目标语言的文本、字体，再更新 SelectedLocale 和界面。连续切换只应用最后一次请求。内容缺译回退简中；完全找不到内容时显示“未知内容＋ID”；加载期间显示省略号。

## 迁移来源和清理

初次迁移参考 Hellmaiden 的中英语言资源，保留本项目已有命名；说明按当前 GAS 效果重写。`Fonts/Chinese.otf` 来自参考工程的 REEJI-LiLing-Gothic-GB Regular 字体。现在编辑、运行和构建不需要外部 Hellmaiden 目录。

I2 一次性提取与导入实现已在 EditorTool 整理中退役删除。当前翻译维护直接使用 Unity Localization 表格或 CSV，不再从旧清单重新导入。历史可恢复副本仍位于 `Logs/LocalizationMigration/removed`，不进入 Unity 导入或发布包。工具入口见 [EditorTool 使用指南](../editor-tools/README.md)。

Debug 文案、日志、昵称、SteamID、ParticipantId 和端口不是翻译目标。

## 验证与验收包

在项目根目录执行 `./Tools/Run-LocalizationValidation.ps1`，验证表格、Smart 参数、CSV Merge、第三种测试语言、界面切换与选卡状态。增加 `-BuildPlayers` 更新 Development 和非 Development 验收包；增加 `-Network` 运行 Host 中文、两个客户端英文的本地 KCP 验证。运行 Unity 批处理前请关闭此项目的 Editor。

可视化检查使用 `Tools/Run-PreparationMenuValidation.ps1` 的 `-Visible -Language en -Width 1920 -Height 1080`，或 `-Language zh-CN -Width 1280 -Height 720`。`-Profile combat-menu-solo` 检查 ESC 与选卡；`-Profile invites` 使用模拟好友 API 检查列表，不发送真实邀请。`-MixedLanguages` 用于多进程，一台房主简中、其他客户端英文；与 `-Language` 同时指定时以它为准。

包路径为 `Builds/MenuDevelopment/MonsterSupergroup.exe` 和 `Builds/MenuRelease/MonsterSupergroup.exe`。请保留可执行文件旁的整个目录，不能单独复制 exe。

本次结果和限制见 [2026-09-13 验收记录](acceptance-2026-09-13.md)。
