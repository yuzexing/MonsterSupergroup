# 鬼火表现资源补全

鬼火对应 `WeaponData_Dante_SlowProjectile`，武器 ID 为 **2**。此次补全普通、火焰、毒素三种投射物及命中特效，资源来自 `F:/DecomplieLatest/HellMaiden/ExportedProject/Assets`。

## 使用与重新导入

Unity 菜单 `Tools > HellMaiden Migration`：

- `Import Dante Projectile Presentation`：补齐依赖、修复三种预制体及命中特效。
- `Validate Dante Projectile Presentation`：检查粒子层数、脚本、材质、动画绑定和音效引用。
- `Capture Dante Projectile Presentation Preview`：输出深浅背景的三种出场／飞行预览至 `Logs/Wisp/Previews`。

可用环境变量 `HELLMAIDEN_SOURCE_PROJECT` 指定另一份源工程。导入器只读取明确列出的依赖，不复制源脚本、DLL、数据库或占位着色器。

现有预制体位于 `Assets/_Project/GameObject`，原路径和 GUID 保留；新增出场动画及 16 个鬼火专用材质位于 `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Projectile`。已有贴图、网格和 Sprite 按原 GUID 复用。专用材质使用派生 GUID，避免覆盖根目录中可能被其它武器使用的材质。完整路径与 GUID 见 [资源清单](hellmaiden-wisp-resources.json)。重复导入检查要求所有输出文件和预制体逐字节相同。

原 `DanteNativeGasMigration` 中指向 `Assets/GameObject` 的三条旧路径已修正。美术导入不执行原工具中的装备、数据库或初始武器重建。

## 美术

- 每种投射物保留源预制体的 **11 个粒子系统**，命中特效保留 **4 个粒子系统**。
- 出场动画为原 `PlayerAttack_Dante_Projectile_In.anim`，长度约 **0.333 秒**，从投射物根节点播放，移动和攻击不会等待出场动画完成。
- 原导出动画中的材质属性哈希，与源着色器属性名 CRC32 的低 28 位匹配后恢复：`0x8B19FAF2 → _Alpha`、`0x80A0AD36 → _AlphaTintFade`、`0x84BAC14A → _AlphaTintMinAlpha`。后二者映射到 AllIn1 的 `_AlphaOutlineBlend` 和 `_AlphaOutlineMinAlpha`。
- AllIn1 材质使用项目已安装的 Sprite Shader；旧内置粒子及局部扰动使用现有 URP VFX 兼容着色器。按源数据保留透明／加色混合、贴图、粒子颜色及序列动画。
- `ProjectileVisualState` 为每个实例保留独立材质，回池复用恢复材质、Sprite 颜色、粒子初始颜色及显隐状态，不重置由 GAS 控制的缩放。

源工程导出的着色器方法体不完整。屏幕折射采用局部噪声扰动近似；CartoonCoffee 边缘染色采用 AllIn1 Alpha Outline 近似，并非原着色器逐像素复刻。三种鬼火脚下的蓝色投影来自源贴图和原动画，不是缺失贴图占位色。

## 音效与命中

复用已导入的 FMOD 音频库，不重复复制 bank：

| 阶段 | 事件 |
|---|---|
| 发射 | `event:/sx/plr/Sx_plr_slowprojectile_shot` |
| 飞行循环 | `event:/sx/plr/Sx_plr_slowprojectile_loop` |
| 命中 | `event:/sx/plr/Sx_plr_slowprojectile_hit` |

三种投射物统一由 `ProjectileAttack` 驱动这三个事件。移除了鬼火预制体中失效的 `Phase` 参数触发器和旧 emitter，避免发射／命中重复播放。循环音在命中终止、过期、取消、回池、Dispose 时停止并释放；空事件、缺失事件及专用服务器沿用 `OptionalAudio` 跳过播放。

源行为的 `hitCount=999` 保持不变。由于鬼火会贯穿，新增可配置的 `playPiercingHitPresentation`，仅在鬼火预制体启用：贯穿接触播放一次命中声音和纯粒子反馈，投射物继续飞行，原伤害只结算一次。真正结束的命中仍沿用原伤害流程。

## 联机

- `ProjectilePresentationPhase.Impact = 4` 表示不结束投射物的贯穿接触，沿用可靠批次与原投射物键。服务器要求接触对应仍获准的攻击，不把接触当作销毁或伤害消息。
- 远端命中只调用纯表现入口，禁用命中特效碰撞，不创建攻击快照或调用伤害结算。
- 服务器保留已准入的存活投射物描述，在终止、攻击完成和玩家离开时清除。新观察者就绪后请求存活视图；恢复时按时间推进位置、出场动画与粒子，播放飞行循环，抑制历史发射音。
- 该新增阶段需要联机各端使用更新后的同一游戏版本。

## 初始武器与范围

武器 ID、伤害 15、Normal 伤害类型、Attack/Projectile 标签、贯穿次数和元素切换规则均保持。仓库原有 `NetworkPlayer.prefab` 的 `initialWeaponId` 实际是 **6**，此次没有把它改成 2；可在现有武器选择中选择鬼火。

未移植菜单卡片、角色插画或其它武器内容。其它 5 件武器仍使用原来的资源。

## 验证

证据集中在 `Logs/Wisp`，独立版本输出到 `Builds/WispValidation/MonsterSupergroup.exe`。

- 资源重导入、动画曲线绑定、三种颜色、材质隔离和资源完整性检查。
- 运行测试覆盖对象池复用、完整命中和贯穿命中、循环音释放、历史音效抑制和缺失事件。
- 独立运行探针 `--wisp-probe=<输出目录>` 记录三种 FMOD 事件的实际输出峰值，检查音效／总音量静音与恢复，输出深浅背景出场、飞行、命中截图，并检查连续回池和历史恢复。
- `Tools/Run-WispProcessValidation.ps1` 验证房主／客户端；添加 `-Dedicated` 验证专用服务器与两个客户端。测试使用内存中的无暴击、三发、单次命中配置做确定性伤害与重连检查，不写入项目武器资产；正式的多次贯穿由运行单元测试覆盖。

已知既有测试差异：三项旧资产测试固定要求初始武器为 ID 2，但 HEAD 中原配置已为 ID 6。保留用户配置，不为使测试通过而更改默认武器。

### 本次实际结果（2026-09-12）

- EditMode：100 项中 97 项通过；鬼火新增 4 项全部通过。其余 3 项失败均为上文记录的既有初始武器 ID 断言差异，见 `Logs/Wisp/editmode.xml`。
- PlayMode：其它武器及共用表现的 59 项回归通过。鬼火 6 项最终全部通过，覆盖原值 999 次贯穿时的一次伤害和非终止命中、远端纯表现、连续回池、动画年龄恢复、循环音释放和缺失事件。首次新测试遗漏挂载节点，补齐测试配置后重跑通过，见 `Logs/Wisp/playmode-final.xml` 与 `Logs/Wisp/wisp-final.xml`。
- 最终资源重复导入：40 个文件 SHA-256 完全一致；输出目录 19 个 GUID（含目录）在项目内没有重复。基线见 `Logs/Wisp/import-final-hashes.json`。
- 编辑器预览：12 张深／浅背景的三种出场与飞行图，位于 `Logs/Wisp/Previews`；已人工查看普通深背景、火焰浅背景和毒素出场图。
- 房主／客户端预验证：3 发各造成 15 点伤害，服务器与客户端一致；远端 6 条生成／结束消息全部回收。断线清理和重连恢复 3 枚存活鬼火通过，历史发射音被抑制。日志位于 `Logs/Wisp/NetworkPreview/Process-Host-7928-20260912-200529-465`（使用同份运行代码的当次 MenuDevelopment 验证构建）。

- 最终 Windows Development 构建成功：`Builds/WispValidation/MonsterSupergroup.exe`，构建日志 `Logs/Wisp/build-verified.log`。验证组件仅在测试构建中打入，并且需要专用命令行参数才会启动；正常菜单流程不自动触发探针。
- 最终独立运行检查 **PASS**：`Logs/Wisp/StandaloneFinal/result.txt`。发射／循环／命中 FMOD 实际输出峰值分别为 **0.08185351 / 0.08443582 / 0.1554263**，三项均通过音效总线与根总线静音、恢复测试，见同目录 `audio.txt`。连续 12 次历史恢复／回池没有叠加循环音、遗留粒子或历史发射音。
- 独立运行预览：`Logs/Wisp/StandaloneFinal/{dark,light}-{appear,flight,impact}.png`，每张同时展示普通／火焰／毒素；已查看深浅背景的飞行与命中。命中特效与源工程一致，三个元素共用蓝色命中表现。
- 最终房主／客户端检查 **PASS**：`Logs/Wisp/Network/Process-Host-7928-20260912-200734-748`。最终专用服务器加两个客户端检查 **PASS**：`Logs/Wisp/Network/Process-Dedicated-7929-20260912-200734-752`。两种模式均覆盖确定性伤害、远端表现、攻击期间断线、清理、冷却保留、存活投射物重连恢复及历史发射音抑制。
- 最终专用服务器音频检查 **PASS**：`Logs/Wisp/DedicatedAudioFinal/result.txt`，不初始化音频系统、不创建循环实例。联机验收使用 KCP 本地多进程，未验证跨公网 Steam 传输；未更改 Steam 好友邀请流程。

快速预览：[三种鬼火飞行](../Logs/Wisp/StandaloneFinal/dark-flight.png) · [浅背景命中](../Logs/Wisp/StandaloneFinal/light-impact.png)。
