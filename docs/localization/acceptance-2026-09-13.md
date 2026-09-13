# 本地化迁移验收记录 · 2026-09-13

## 交付范围

玩家界面统一由 Unity Localization 1.5.13 提供文字和字体。MonsterMenus 有 965 个条目，MonsterContent 有 48 个条目；简中和英文均无空译文。已迁移正式数据库的 6 种武器、8 种装备、7 种 Perk 和大招，以及角色、地图名称和说明。

I2 插件、示例、编辑器代码、空语言表、两个适配组件及旧 MenuStrings.json 已从 Assets 移除；玩法资产 GUID、内容 ID、图标与配置保留。迁移前副本仅在本机 `Logs/LocalizationMigration/removed` 中，用于恢复，不进入 Unity 导入或构建。未删除参考工程中的原始资源。

表格是翻译来源，CSV 是导出副本。普通构建执行校验和 Addressables 构建，不重新生成译文。内容说明按当前 GAS 等级／稀有度生成参数，增幅与百分点分别处理。

## 自动化结果

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| PlayMode 内容、CSV、选项、ESC、选卡、好友 UI | 22/22 通过，无跳过 | `Logs/LocalizationValidation/20260913-105649/results.xml` |
| 最后一次代码调整后的内容测试 | 3/3 通过 | `Logs/LocalizationMigration/content-final.xml` |
| 正式内容的所有装备等级、Perk 稀有度 | 格式化通过；无未替换参数；读取前后配置相同 | ContentLocalizationTests |
| 第三个 Locale | 内存测试 Locale 和表、字体提供器生效，缺条目回退简中；未增加业务分支 | ContentLocalizationTests |
| CSV 往返 | Merge 重复导入后 Id、译文、参数及已有 Smart 元数据保持 | LocalizationCsvTests |
| I2 清理和资源引用扫描 | 已删除脚本／资源 GUID 无剩余场景、Prefab、资产或程序集引用 | `Logs/LocalizationMigration/audit-final.json` |
| 玩法资产保护 | 22 个内容资产的非本地化配置对比无变化 | 同上及 `content-invariants-before.json` |
| Development / 非 Development 构建 | 两种构建成功 | `Logs/LocalizationMigration/build-final-development.log`、`build-final-release.log` |
| 构建不覆盖表格 | 初轮与两次构建前后共 8 个字符串表相关文件哈希一致 | `Logs/LocalizationMigration/table-hashes-after-build.json` |
| KCP Host 中文＋两个英文客户端 | 三进程均 PASS；五次开局、回大厅、重开、Downed 重连、身份与实际移动检查通过，各端语言独立 | `Logs/PreparationMenu/20260913-105954-local-party-p7796` |

最后的英文邀请文案从“sent”改为“submitted”，语言显示名改为“简体中文 / English”。这两处是资源修改，随后再次运行完整测试、导出 CSV 并更新两种包；最终运行目录见本文末尾补充记录。

## 实际界面检查

以下区分真实键鼠操作和自动截图核对；不把无图形模式的断言当作视觉检查。

| 页面／行为 | 方式与分辨率 | 结果 |
| --- | --- | --- |
| 首页、选项、语言列表 | Development，真实鼠标、方向键、Enter、Esc，1920×1080 | 中英切换立即更新；按钮及布局完整，未显示内部 key |
| 本地准备房间、六种武器选择 | Development，真实鼠标，英文 1920×1080 | 角色、地图、武器名称和说明正确；席位和端口保留 |
| 波次／经验 HUD、ESC 角色页 | Development，真实 Esc，英文 1920×1080 | 文案和单位正确；战斗继续，生命变化正常 |
| 游戏结束、回大厅 | Development，真实鼠标，英文 1920×1080 | 结束页覆盖旧菜单，返回同一准备房间，语言保留 |
| ESC 法术、装备、确认框、选卡恢复 | Development 中文 1280×720；非 Development 英文 1920×1080，实际渲染截图核对 | 名称、等级、增幅／百分点、空槽位、无装备显示正常；布局不越界 |
| 好友列表、搜索、提交提示 | 模拟 API，中文 1280×720／英文 1920×1080，实际渲染截图核对 | 昵称原样保留；搜索、滚动、焦点及邀请状态更新正常；未发送真实邀请 |
| 非 Development 首页 | 中文 1280×720，实际渲染截图核对 | 新增开发 KCP 控件隐藏；首页正常 |

可视化原始图片分别在：

- `Logs/PreparationMenu/20260913-110746-combat-menu-solo-p7797`：中文 ESC、装备、卡片、确认框。
- `Logs/PreparationMenu/20260913-110812-invites-p7797`：中文好友页、搜索、提交状态。
- `Logs/PreparationMenu/20260913-110843-local-release-p7797`：非 Development 首页。
- `Logs/PreparationMenu/20260913-110911-combat-menu-solo-p7797`：非 Development 英文 ESC、装备、卡片、确认框。
- `Logs/PreparationMenu/20260913-110938-invites-p7797`：英文好友页。

选卡图中的英文 Player Debug / Enemy Debug 属于明确排除的调试文案。英文好友页中的中文测试昵称和“验收模拟好友”测试标记不是正式译文遗漏。

已保留代表性截图：[中文装备页](screenshots/zh-equipment-1280.png)、[中文选卡](screenshots/zh-card-1280.png)、[中文好友页](screenshots/zh-friends-1280.png)、[非 Development 首页](screenshots/zh-release-home-1280.png)、[英文装备页](screenshots/en-equipment-1920.png)、[英文选卡](screenshots/en-card-1920.png)、[英文退出确认](screenshots/en-confirm-1920.png)。

最终提示文案另在 1280×720 复核：[Invite submitted](screenshots/en-invite-submitted-1280.png)。按钮和提交提示没有裁切；测试日志为 `Logs/PreparationMenu/20260913-111356-invites-p7797`，功能断言 PASS，完整进程退出仍出现 `0xC000041D`。

## 尚未通过／需要人工核验的部分

可视化程序在功能断言全部 PASS、退出流程完成后，仍会在 Unity 原生退出阶段崩溃。以上五次可视化运行退出码为 `0xC0000005`，其中中文好友页为 `0xC000041D`。测试脚本保留非零退出判定，没有忽略或伪装为通过。该问题在本次迁移前的可视化验收中也已出现；本次未解决。无图形模式的三进程与 PlayMode 测试不受此退出问题影响。返回首页和返回准备房间成功不代表完整退出程序已经通过。

没有使用真实 Steam 好友发送邀请，也没有把模拟 API 结果当作 Steam 端到端验收。真实双账号中英混合、全部页面在两个分辨率的交叉人工复查仍可按下述步骤进行。当前证据覆盖核心页面，但不是每种语言×每个页面×每个分辨率的全部组合。

## 人工复查步骤

1. 启动 Development 包，选项中切换简体中文／English，用方向键、Tab、Enter、Esc 操作；在画面选项分别设置 1280×720 和 1920×1080。
2. 打开三个程序。Host 设置简中，另外两个设置英文；创建／加入同一个本地 KCP 房间。核对同一武器的不同语言名称，个人席位、准备状态不被语言切换修改。
3. 逐个选择 6 种初始武器开局，在 ESC 法术统计中核对初始标记和名称。角色页检查存活状态、经验、冲刺属性；Debug 保留原文。
4. 升级进入奖励选择和装备目标选择，打开 ESC → 选项切换语言，再关闭 ESC。确认当前步骤、选择、待确认请求保留，名称和说明更新；装备等级从 1 开始，Perk 不出现虚构等级。
5. 增加／升级装备、获得 Perk 后，对照表格和运行时数值检查百分比及百分点。反复开关菜单不改变 Build 或冷却。
6. 全员倒地，检查结束提示；回大厅或重新开始，语言保持。客户端离开、Host 结束分别核对提示和会话清理。完整退出时注意上述原生退出问题。
7. 使用真实 Steam 双账号复查好友页和受邀加入，发送成功仅显示“邀请已提交 / Invite submitted”。不要把提交结果当作对方已收到。

维护、CSV 导入导出和新增语言步骤见 [维护说明](README.md)。

## 最终构建补充

最终完整运行目录：`Logs/LocalizationValidation/20260913-111119`。22 项 PlayMode 测试全部通过，无跳过；表格校验、Development 和非 Development 两次构建均以 0 退出。`development.log`、`release.log` 和 `results.xml` 是最终资源版本的依据。

`Logs/LocalizationMigration/table-hashes-final-build.json` 记录最终构建前后 8 份字符串表相关文件哈希全部一致。两种包的 Managed 目录未包含 I2 程序集。最终英文好友提交提示已重新截图，完整图形进程退出的原生崩溃仍未解决。

最终 Development 包另以真实键盘复核了语言列表：显示“简体中文 / English”，方向键＋Enter 可切换，Esc 返回首页。手动测试结束时已恢复简体中文。
