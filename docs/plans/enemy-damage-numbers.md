# 联网敌人 Damage Numbers Pro 接入

## 行为与边界

普通命中、技能和燃烧/中毒/流血共用伤害事件。攻击者在本地实际扣血大于零时立即显示，其他客户端在服务器实际扣血大于零的可靠确认后显示。数字使用结算值，例如剩余 8 HP 受到 50 点伤害显示 50。服务器拒绝的预测不会撤回已显示数字，也不会向观察者发送数字。

同一 DamageEventId 只提交给插件一次，再由插件执行原版连续伤害合并。合并组为来源玩家、伤害来源、目标、伤害类型、暴击。快照、回血和状态同步不产生历史数字。数字独立于闪白、动画初始化、FSM 和模拟权限。

死亡仍立即销毁敌人。广播前捕获 damagePosition（回退到 hurtBox 中心、根节点），使 Host 在目标已经销毁后仍能显示确认数字；已经显示的数字继续完成动画。去重记录保留至缓存过期或客户端世界重置，模拟权交接和敌人销毁不提前清除数字记录。

## 资源迁入

来源：`F:/DecomplieLatest/HellMaiden/ExportedProject/Assets`。

迁入 `Assets/_Project/Content/DamageNumbers`：六套 `DIVINA … DamageNumbers.prefab` 和 `DamageColorsSO.asset`。普通、火焰、中毒、流血、闪电使用各自原版样式，所有暴击共用原版 Critical 样式。保留字体大小、颜色梯度、动画、合并和池参数（poolSize 50），排序为 Foreground / 1000。

已将源工程中指向反编译 DLL 的 DamageNumberMesh 引用重绑到现有 DamageNumbersPro 脚本；TMP 引用复用现有 LiberationSans SDF 字体及其 2180264 材质子资源。源 Renderer 的外部材质引用也重绑到该字体材质。不引入旧 DLL，不修改插件源码。

正式 Gameplay 与开发 NetworkCombatSandbox 的 PoolManager 已绑定新 palette。联网数字默认启用，不依赖旧 GameDirector.Settings。非联网 Enemy 保留原有伤害数字入口和设置判断。

## 数据与生命周期

- CombatEvent 显式携带 PresentationDamageType；Native、兼容伤害和 DOT 在发布事件前填充。DOT 根据状态类型着色，暴击不从继承的父标签推断。
- CombatResult 增加显示类型、暴击、DamageSourceId；结算值继续使用 Damage。未压缩字段预算由 103 增至 109 字节。
- EnemyHitPresentation 增加 Damage、类型、暴击、来源玩家、来源 ID、Position/HasPosition；未压缩字段预算为 43 字节。Mirror 自动生成序列化，Host/Client 必须使用同一版本。
- 数字成功生成后才记录事件 ID；确认版本单独过滤旧事件。已播预测收到回执时只更新确认记录，失败的预测可由有效回执补播。
- PoolManager 跟踪本局数字实例，在世界停止或场景销毁时清理活动和池内实例。Dedicated Server 只产生数据，不创建数字对象。
- 保留 productMovementOnly 配置、伤害计算、刷新轮次、闪白时长、击退和击杀掉落流程。

## 验证记录

验证产物保存在 `Logs/DamageNumbers`；独立进程详细日志和游戏画面在对应 `Logs/M3Knockback` 运行目录。

2026-09-12 最终验收：

| 验证 | 结果 | 记录 |
| --- | --- | --- |
| EditMode | 171/171 通过 | `Logs/DamageNumbers/edit-final.xml` |
| PlayMode | 35/35 通过 | `Logs/DamageNumbers/play-regression.xml` |
| Windows 构建 | Success | `Logs/DamageNumbers/build.log`、`build-hashes.json` |
| 普通 Host/Client | 两进程退出码 0，零异常 | `Logs/M3Knockback/Host-20260912-145232-583-p7991` |
| 延迟 Host/Client | 两进程退出码 0，零异常 | `Logs/M3Knockback/Host-20260912-145411-741-p7992` |
| Dedicated + 两客户端 | 三进程退出码 0，零异常，服务器数字实例为零 | `Logs/M3Knockback/Dedicated-20260912-145558-095-p7993` |

三轮使用同一个构建，汇总为 `Logs/DamageNumbers/acceptance.json`。逐伤害事件检查生成次数与结算值；普通命中阶段核对服务器接受 ID 集合，七个 DOT 阶段分别核对数字、闪白、血量和 tick 唯一性。来源断开后只核对在线观察者接管期间的确认集合；来源重新连接时确认快照没有重放数字。异常、重复事件、额外预测 tick、扣血不一致均会使夹具或运行器失败。

确定性测试覆盖：同批连续命中、预测与重复回执、旧确认/快照、新局复用 ID、禁用数字后的确认补播、禁用闪白时数字仍显示；真实 `NetworkEnemyBase` 和 `NetworkEnemySkeleton` 保持各自 FSM 配置，并验证服务器把剩余 8 HP 的敌人以 50 点伤害立即销毁后，Host 的迟到确认仍显示 50。资源测试检查六套预制体的脚本、字体和材质引用，验证原版 50+12 合并为 62、不同玩家/类型/暴击/目标不串组，以及池复用和实例清理。既有闪白、模拟权交接、击退、状态和经验掉落测试通过。

人工查看了下列实际游戏相机渲染，攻击者与观察者可见数字 `12`。`numbers-*.png` 使用 URP 相机渲染请求读取画面，隐藏窗口的普通 `*-hit-*.png` 截图不作为视觉通过证据。

- 攻击者：`Logs/M3Knockback/Host-20260912-145232-583-p7991/numbers-client-3.png`
- Host 观察者：`Logs/M3Knockback/Host-20260912-145232-583-p7991/numbers-host-2.png`
- Dedicated 观察者：`Logs/M3Knockback/Dedicated-20260912-145558-095-p7993/numbers-client2-3.png`

早期 `play-numbers.xml` 与 `play-numbers-fixed.xml` 保留测试夹具失败记录：未切换手动生成模式、两个敌人的测试事件 ID 重叠；修正后最终 35 项全部通过。首次 EditMode 启动曾因另一 Unity 构建占用项目而未执行，随后正常运行。未修改其他任务的构建、敌人 Prefab 或插件源码。

本次修改清单和相对实施前的差异保存在 `Logs/DamageNumbers/changed-files.json`、`task.diff`；工作区原有闪白、DOT 修复及其他改动被保留。

复跑独立进程：

```powershell
./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Executable Builds/EnemyHitFlash/EnemyHitFlash.exe -ValidateDamageNumbers -ValidateHitFlash -CaptureFrames -ForceD3D11 -Port 7991
./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Executable Builds/EnemyHitFlash/EnemyHitFlash.exe -ValidateDamageNumbers -ValidateHitFlash -CaptureFrames -ForceD3D11 -ImpairedNetwork -Port 7992
./Tools/Run-OrdinaryKnockbackProcessValidation.ps1 -Executable Builds/EnemyHitFlash/EnemyHitFlash.exe -ValidateDamageNumbers -ValidateHitFlash -CaptureFrames -ForceD3D11 -Dedicated -Port 7993
```
