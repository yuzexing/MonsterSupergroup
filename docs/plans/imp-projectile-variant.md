# Imp Variant 与敌人联网弹体

## 行为和资产

`NetworkEnemyImp.prefab` 是 `NetworkEnemyBase.prefab` 的直接 Variant，与 Skeleton、LustSinner 并列。继承唯一 EnemyController、生命/GAS、移动、受击、死亡和 NetworkIdentity；唯一主动攻击槽绑定 EnemyProjectileAttack，并增加通用 NetworkEnemyProjectileAdapter。productMovementOnly=false，默认接触伤害关闭，未加入正式波次。

源资产为 `F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/GameObject/Enemy_Imp.prefab` 与 `EnemyBulletAttackImp.prefab`。新资源目录为 `Assets/_Project/Content/HellMaiden/Enemies/Imp`。不导入旧程序集。

| 配置 | 本轮值 |
|---|---|
| 生命 / 伤害 / 经验 | 12 / 5 / 6，源基础配置 |
| 移动速度 / 攻击距离 / 冷却参数 | 2 / 10 / 2 秒，源配置 |
| 预警 / 攻击 / 恢复 | Unity 读取约 0.85 / 0.29 / 0.43 秒；额外阶段时间均为 0 |
| 弹体 | 单发直线，速度 6 单位/秒，寿命 5 秒，命中消耗，Projectile 伤害类型 |
| 弹体速度、寿命来源 | 用户选定的移植补充值，不宣称为原版参数 |
| GUID / Mirror assetId | d2f06231a6b99b448a2eacb121addd48 / 2965718819 |

左右动画保留原片段与速度，同侧 Up/Down 共用源动画。预警开始锁定方向；蓄力只显示表现，不启用碰撞。弹体无 NetworkIdentity，不按帧同步位置。各客户端收到发射通知后从通知起点完整飞行，不按延迟推进。各端的时间允许相差网络延迟。

源导出的弹体脚本序列化字段缺失，迁入工具显式补齐视觉、粒子、伤害交互和旋转节点；没有弹体动画片段时使用原粒子及无片段的结束回调。角色材质启用 HITEFFECT_ON。旧 CartoonCoffee 粒子 Shader 映射到已安装兼容 Sprite Shader，原生 RenderAs2D 继续使用 URP Flattening Shader。

## 发射、终止与伤害

**按用户后续补充，本轮同时同步发射和终止事件。** 不发送弹体运动或持续状态。

- 发射唯一键为“敌人 ID + 原 ActionId + 弹体序号”；Imp 的序号为 0。事件携带发射起点、方向、Prefabs 类型、速度、寿命及伤害/眩晕参数副本。适配组件只替换联网生成入口，非联网 EnemyProjectileAttack 保持原调用路径。
- 模拟方立即生成本地弹体，经拥有者 Endpoint 将发射事件提交服务器。服务器检查模拟方、Epoch、敌人存活、配置与数据；接受后可靠转发。Host 的本机回执不会重复生成。
- 每端只检测本机玩家；远端玩家不会消耗弹体。PlayerDamageInteraction 的专用弹体模式在入口过滤，不进入会误消耗弹体的旧通用结束回调。重复碰撞只处理一次，沿用玩家无敌和受击间隔。
- 任一客户端命中、寿命结束或主动停用/销毁弹体，立即本地结束，并可靠提交终止事件；服务器对已确认弹体转发一次。其他端结束同一 ID 的本地弹体，不再次结算伤害，不因远端命中回滚本地已发生的伤害。
- 终止可以先于发射到达；客户端保留终止记录，阻止随后生成。服务器对短暂先到的终止有界等待发射确认。终止不要求射手仍然存在。
- 多端在通知抵达前同时命中，仍可能各自扣血一次。这是各客户端自行碰撞和扣血的既有约定，不提供全局首个玩家挡弹或伤害回滚。
- 弹体由世界持有，脱离射手，使用参数副本；射手死亡、击退、停用和交接只取消未发射蓄力，不清除已发射弹体。已销毁射手的迟到确认仍可通过注册的敌人 Prefab 解析资源。
- 去重与终止记录保留 120 秒并限制容量。交接不清除；客户端停止/世界卸载清理本局对象池、弹体和缓存。Dedicated 不创建弹体表现或本地碰撞对象。
- 迟到加入不补出历史弹体。普通移动快照、动画恢复和交接基线不会被当成新的发射事件。

交接检查点携带锁定方向与发射进度。服务器保存已确认发射，旧检查点不能回退该记录。接管仍在预警时恢复预警；仍在攻击且未确认发射时使用原键提交；已经恢复或结束不补历史发射。旧模拟方的失效提交不会转发，本地预测在提交前断线不保证成为确认发射。

可靠 EnemyAttackPresentationBatch 新增 ProjectileLaunches 与 ProjectileTerminations。Mirror 对整数和数组长度使用变长编码：发射为 34 字节浮点/序号字段，加五个变长整数及检查点；终止为 3 字节序号/原因，加两个变长整数；Action 方向与发射标记增量为 9 字节。估算按实际字段值计算，包含数组与批次头，并通过序列化测试核对真实字节数。Host/Client 必须使用同一版本。

## 制作步骤

1. 首次执行 Unity 菜单 `Monster Supergroup > Network Combat > Migrate Imp Variant`。源目录只在首次迁入时需要；重复执行保留有效动画、物理和属性覆盖，只补齐缺失绑定并校验。
2. 同类敌人从 NetworkEnemyImp 创建 Variant，修改 EnemyController 属性、EnemyAnimator 动画和 EnemyProjectileAttack.bulletPrefab。更换弹体时保留必需的 BulletProjectile、PlayerDamageInteraction 和触发器绑定；本轮支持单发直线，不实现追踪或三连发。
3. 新具体敌人使用独立 assetId 并注册到 NetworkManager.spawnPrefabs。Imp 已增补 Boot 与 Sandbox 注册。Sandbox 可用 `--enemy-sandbox-prefab=NetworkEnemyImp`，原默认选择不变。
4. 验证构建入口 `EnemyPrefabVariantMigration.BuildImpValidation`，输出 `Builds/Imp/Imp.exe`，会连续执行两次迁移并逐字节检查稳定性。
5. `Tools/Run-ImpProjectileValidation.ps1` 支持 `-Dedicated`、`-Impaired`、`-Graphics`。图形验证临时使用 D3D11 direct 启动参数，不更改项目图形配置。
6. 既有交接与伤害回归工具可选择 NetworkEnemyImp。交接压力夹具会延长实例攻击阶段，原 Prefab 阶段不变。

## 验证记录

日志位于 `Logs/Imp`；失败运行保留且不计为通过。初始工作区基线为 `baseline-files.json`、`before/`。下列为 2026-09-12 的实际记录，后续最终构建验收见末尾。

| 验证 | 实际结果 |
|---|---|
| Unity Prefab、参数与引用核对 | PASS；`prefab-report.json` 保存父资产、数值、实际动画时间及 Override 清单 |
| 连续两次迁移 | PASS；`migration-repeat.txt`；Base、Skeleton、Example、LustSinner、Imp、弹体与注册保持稳定 |
| EditMode | `editmode-accepted.xml`：61/61；含变长编码的真实序列化大小核对 |
| PlayMode | `playmode-accepted.xml`：36/36；包含实际 Physics2D 弹体命中、复用、射手销毁、无敌、闪白、数字及经验回归 |
| Host + 双客户端 | `process-20260912-192026-host-normal`：3 个进程退出 0，每端 3 次唯一发射与 3 次唯一终止 |
| Dedicated + 双客户端 | `process-20260912-192026-dedicated-normal`：3 个进程退出 0，Dedicated 弹体实例为 0 |
| 延迟网络 Host + 双客户端 | `process-20260912-192236-host-impaired`：PASS；150 ms 延迟、20 ms 抖动、10% 不可靠包丢失；完整角色取景待最终构建复核 |
| 普通与延迟交接压力 | `Logs/EnemyHandoff/20260912-192253-normal`、`20260912-192443-impaired` 均 PASS；包括 Warning/Active/Recovery、掉线接管、重连、旧权限包、群体交接及倒地 |

独立弹体夹具通过真实 Boot → Gameplay 与网络传输运行。为固定跨端事件时序，其命中环节直接调用实际 PlayerDamageInteraction 并重复投递碰撞，Physics2D 触发器另由 PlayMode 实测。交接压力夹具临时延长实例的攻击阶段；源 Prefab 时间保持不变。

保留的失败及处理：首次资源迁入发现缺失 CartoonCoffee Shader 映射后补齐；首次进程夹具文件竞争改为互斥访问；图形运行的黑帧判失败后去除图形模式下的 batchmode；带宽测试的固定整数估算改为 Mirror 实际变长编码。第一次 Imp 伤害/位移回归在持续追击时做静止位置断言失败，将夹具已有的 LustSinner 暂停规则推广至完整 FSM 敌人后重跑。`build-3.log` 是主动中止的构建，以免覆盖正在验收的可执行文件，不计为成功构建。

## 修改与资源清单

- 通用玩法接入：EnemyProjectileAttack、EnemyController.Simulation、EnemySimulationCheckpointState、BulletProjectile、EnemyAttackPrefab、PlayerDamageInteraction，以及 IEnemyProjectileExecution。
- 网络能力：NetworkEnemyProjectileAdapter；EnemyProjectileLaunch/Termination/History；NetworkEnemySimulationWorld 的发射、终止与生命周期接入；Endpoint 可靠批次；Registry 交接发射记录；检查点与带宽估算。
- 制作与验证：EnemyPrefabVariantMigration.Imp 与现有资源迁入助手、Boot/Sandbox 注册、Imp 编辑器/运行/进程测试、交接和伤害夹具的可选敌人兼容。
- 资源：12 个动画、25 个 Sprite 等子资源、9 个材质、11 个纹理、2 个源角色/弹体 Prefab，另有正式 NetworkEnemyImp Variant。逐项 GUID 映射见 `Logs/Imp/resource-mapping.txt`。
- 本轮未改 Base、Skeleton、Example、LustSinner 的 Prefab 内容；与初始备份逐字节核对。工作区并行存在结算/新局开发，保留其修改；仅为共用网络批次补齐当前轮次字段，避免新局门控拒绝弹体消息。

尚未运行跨物理机器、长期压力与完整人工游玩验收。独立进程中的两只 Imp 同时发射、发射通知定向延迟至射手销毁后、每个攻击阶段逐键统计弹体交接的组合矩阵尚未全部单独执行；已有单位/运行测试与通用交接压力覆盖其中部分条件，不将其等同于完整矩阵验收。

最终核心实现复验：`build-final.log` 构建成功；`playmode-final-code.xml` 为 13/13（最终 Variant 测试，包括新增的旧 Epoch、错误拥有者、参数和非有限值拒绝断言）。`process-20260912-193144-host-normal`、`process-20260912-193144-dedicated-normal`、`process-20260912-193206-host-impaired` 全部通过，9 个进程退出码均为 0。图形运行保存了完整角色、左右蓄力及弹体飞行画面；人工查看 `a-warning-5.png` 与 `b-flight-2.png`，非黑帧、角色未裁切。夹具暂停常规刷怪并手动生成测试敌人，画面中的运行状态 HUD 不作为正式菜单验收。

Imp 伤害回归 `Logs/M3Knockback/Host-20260912-193252-162-p7996` PASS（Host、Client 退出 0），覆盖双来源、交接、重连、服务器权威、燃烧/中毒/流血及迟到/重复确认、闪白、伤害数字与扣血、位移断言。之前并行负载下的 `Host-20260912-193206-747-p7996` 在交接后首个命中与继承中的击退重叠，按旧战斗规则只应用 4/5 次击退而失败；保留该失败日志，并让独立命中夹具等待已交接的前一段击退结束，不改变重叠击退规则或降低断言。

验收夹具修正后的构建为 `build-acceptance-retry.log`（退出 0，程序集哈希见 `acceptance-build-hash.txt`）。此前 `build-acceptance.log` 在启动阶段退出 1、未执行构建，单独保留而不计为通过。`process-20260912-193622-dedicated-impaired` PASS：Dedicated 加延迟网络双客户端，三个进程均退出 0。最后的既有 Prefab 保留检查见 `existing-prefab-preservation.json`，本轮改动的既有文件清单见 `modified-existing-by-imp.json`。

最后一轮 Dedicated 加延迟网络的 Imp 伤害回归 `Logs/M3Knockback/Dedicated-20260912-193622-650-p7998` PASS，Server、Client、Client2 全部退出 0、无异常日志。交接后独立击退 5/5，通过后继续完成服务器权威、三类 DOT、迟到/重复确认、闪白、数字及血量/位移断言；Dedicated 不生成伤害数字或弹体对象。本轮已执行验收至此结束，未执行的组合仍按上文标注。
