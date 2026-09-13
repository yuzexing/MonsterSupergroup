# LustSinner 联网 Variant 移植记录

## 资产关系与行为

```text
Assets/_Project/Content/NetworkCombat/NetworkEnemyBase.prefab
├─ NetworkEnemySkeleton.prefab
│  └─ NetworkEnemySkeletonExample.prefab
└─ NetworkEnemyLustSinner.prefab
```

LustSinner 是 Base 的直接 Prefab Variant。复用单一 EnemyController、CombatantBehaviour、GAS、移动和联网组件，增加现有 EnemyAttackMelee 与 NetworkEnemyMeleeReplica。没有新敌人 Controller 或网络消息。

这是预警后生效的定向区域攻击，没有飞行弹体。预警开始锁定朝向；观察端沿用可靠阶段消息与本机玩家命中窗口。玩家所有者结算生命，继续遵守已有受击间隔和无敌规则。正式波次未加入新敌人。

| 配置 | 值 |
|---|---:|
| 基础生命 / 伤害 / 经验 | 15 / 5 / 6 |
| 速度 / 攻击距离 / 冷却 | 4 / 3 / 0.5 秒 |
| 预警 / 生效 / 恢复（Unity 读取） | 0.514285684 / 0.107142858 / 0.435714304 秒 |
| 脚本附加阶段时间 | 全部 0 |
| productMovementOnly / 接触伤害 | false / 关闭 |
| 飞行 / 使用寻路 / 停下攻击 | 开启 / 开启 / 开启 |
| GUID | 05b4427c6fb40c84fb712ac16d351623 |
| Mirror assetId | 3943459166 |

沿用现有 EnemyAnimator 以 LeftUp 动画长度计算阶段时间的规则。左右原片段保留各自的帧数与速度，同侧 Up/Down 绑定同一原片段；源右向片段长度略有不同，未重采样。保留死亡立即销毁。

## 资源与引用

来源：`F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/GameObject/Enemy_LustSinner.prefab`。

新资源位于 `Assets/_Project/Content/HellMaiden/Enemies/LustSinner`。包括 12 个角色动画、所需 Sprite / Texture2D、原始斩击粒子与阴影，以及 `LustSinner_Warning Variant`。现有同 GUID 的公共图片、材质、物理材质和音频库继续复用。源模板仅用于编辑器迁移，实际生成的是 NetworkEnemyLustSinner。

脚本引用映射到当前工程与 Packages 中的类，Animancer / Astar 的导出 DLL 引用转换为当前 MonoScript。没有导入旧 DLL。源缺失的 EnemyAnimator、Animancer、Renderer、攻击预警和 PlayerDamageInteraction 引用已显式绑定。

迁入资源共 55 项（不计 `.meta`）：12 个动画、31 个 Sprite 资产、6 张纹理、4 个材质和 2 个编辑器迁移/攻击 Prefab。公共依赖按 GUID 复用，逐项映射记录在日志目录。

角色与粒子使用已安装 AllIn1SpriteShader；原生 RenderAs2D 合成层使用当前 URP 的 `2D/RenderAs2D-Flattening`。仅服务旧粒子 Shader 的 SetParticleSystemTransformInShader 被移除。角色材质启用 HITEFFECT_ON，以支持现有受击闪白。粒子继续保留原发射参数、贴图与颜色，使用兼容混合模式。

**预警简化约定：** 使用源预警的静态图形、颜色、缩放、朝向与六点 PolygonCollider2D。Warning 显示图形并关闭伤害节点；Active 隐藏 Renderer 并开启伤害节点。未接入节点不匹配的 LustSinner Path 动画。隐藏图形不会关闭其下的伤害区域。

详细记录：`Logs/LustSinner/resource-mapping.txt`、`prefab-report.json`。后者包含实际值与 Property Override 清单。必要差异涉及属性、飞行/寻路、碰撞与刚体、Sprite/动画/粒子/挂点、音效、主动攻击、接触开关、productMovementOnly 和独立 assetId。

## 制作与验证入口

1. Unity 菜单 `Monster Supergroup > Network Combat > Migrate LustSinner Variant` 执行首次迁入。首次迁入需要上述源工程路径；完成后再次执行只校验并修复缺失绑定，无需读取源工程。
2. 从 NetworkEnemyLustSinner 创建 Prefab Variant，调整 EnemyController 的基础属性、动画和表现。保持唯一主动攻击槽引用 EnemyAttackMelee，攻击资源放在其 attackPrefab。
3. 新具体敌人需有独立 NetworkIdentity.assetId，并注册到 Boot 与 Sandbox 的 NetworkManager.spawnPrefabs。迁移工具自动处理本次 LustSinner 的身份与注册。
4. Sandbox 可以直接把 NetworkEnemySandboxSpawner.enemyPrefab 指向 LustSinner，或启动时传 `--enemy-sandbox-prefab=NetworkEnemyLustSinner`。默认选择不变。
5. 验证构建入口：`MonsterSupergroup.NetworkCombat.Editor.EnemyPrefabVariantMigration.BuildLustSinnerValidation`，生成 `Builds/LustSinner/LustSinner.exe`。
6. 独立进程可选参数：`Run-EnemyHandoffValidation.ps1 -SkeletonPrefab NetworkEnemyLustSinner`；`Run-OrdinaryKnockbackProcessValidation.ps1 -EnemyPrefab NetworkEnemyLustSinner`。同时传入新构建的 `-Executable`。
7. 画面夹具启动参数：`--lust-visual=绝对输出目录`，从正式 Boot 进入 Gameplay，捕获左右角色、预警、攻击、恢复与死亡掉落。

迁移工具重复执行不会覆盖有效属性与动画。原有迁移/设置入口能保留新 Variant 关系。Host/Client 需使用同一份构建。

## 生命周期修补

定向区域复用了现有近战组件，测试发现直接停用根对象时仍会结算待处理碰撞，且原生攻击实例未归还。EnemyAttackMelee 现在在停用时清除攻击实例；PlayerDamageInteraction 检查敌人根对象的激活状态，停用不补扣血。使用 GameObject 激活状态判定，避免父子 OnDisable 回调顺序导致 Behaviour 状态尚未更新。

Unity 禁止在父对象停用回调内部改变子对象父级，因此先立即关闭伤害和表现，下一帧再归还攻击池；若原场景/池已经销毁则销毁剩余实例。观察端复用同一归还路径。正常活动窗口的结算规则不变。

## 本轮验证记录

2026-09-12 实际结果如下。失败日志保留用于定位，不计入通过结果。

| 验证 | 结果 | 记录 |
|---|---|---|
| 迁移前 PlayMode 基线 | 8/8 | `Logs/LustSinner/baseline-playmode.xml` |
| 最终 EditMode | 43/43 | `Logs/LustSinner/editmode-acceptance.xml` |
| 生命周期、闪白、数字、状态、击退 PlayMode | 31/31 | `Logs/LustSinner/playmode-verified-2.xml` |
| 经验与掉落 PlayMode | 9/9 | `Logs/LustSinner/playmode-experience.xml` |
| 最终 Windows 验证构建 | Success | `Logs/LustSinner/build-acceptance.log`、`build-hashes.json` |
| 普通 Host＋双客户端交接 | 三端通过、退出 0 | `Logs/EnemyHandoff/20260912-175624-normal` |
| 延迟 Host＋双客户端交接 | 三端通过、退出 0 | `Logs/EnemyHandoff/20260912-180119-impaired` |
| 普通 Host/Client 伤害、闪白、数字、DOT | 两端通过、退出 0、零异常 | `Logs/M3Knockback/Host-20260912-180736-473-p7980` |
| 延迟 Host/Client，同上，包含可见画面 | 两端通过、退出 0、零异常 | `Logs/M3Knockback/Host-20260912-180844-675-p7982` |
| Dedicated＋双客户端，同上 | 三端通过、退出 0、零异常；服务端数字实例为零 | `Logs/M3Knockback/Dedicated-20260912-180736-473-p7981` |
| 实际左右动画、预警、斩击、致死掉落画面 | 通过、退出 0、零异常 | `Logs/LustSinner/visual-acceptance` |

三轮最终伤害/DOT 运行及最终画面使用同一构建。燃烧、中毒、流血、迟到/重复/到期后确认和来源断线接管均通过逐事件断言；不接受多余预测 tick、重复数字、重复闪白或扣血差异。原始源值的两只 LustSinner 经服务器各接受 50 点致死伤害后立即销毁，各生成 raw=6 的经验掉落，见画面日志中的 `[XP]` 记录。

已人工查看有效游戏画面：`visual-acceptance/02-left-right-warning.png`、`03-left-right-active.png`、`05-death-and-drops.png`；攻击者与观察者数字 `12` 分别见延迟运行目录的 `numbers-client-3.png`、`numbers-host-2.png`。画面是正式 Boot → Gameplay 的手动生成夹具，因此波次 UI 显示停止；没有改正式波次。

日志根目录为 `Logs/LustSinner`。本轮工作区基线为 `baseline-files.json` 和 `before/`，不以 Git HEAD 代替用户已有改动。Base、Skeleton、Example 及正式 Gameplay 场景与基线逐字节一致；Boot 与 Sandbox 各只增加一条 LustSinner 注册。

交接压力夹具保留既有做法：为三个阶段各附加 1.5 秒，保证定向触发在预警、生效、恢复中的交接；本轮压力段各运行 8 秒。正式 Prefab 的附加时间始终为 0，原始阶段时长另由 PlayMode 和实际画面验证。击退/DOT 夹具暂时把生成实例的攻击距离设为 0，并在击退样本之间暂停追逐；服务端权威阶段仍验证实际移动恢复。这些都是测试实例配置，不写回资源。

早期失败记录包括：Unity 编译缺失命名空间/跨程序集返回类型、几何测试中玩家位置被自身移动同步、直接停用敌人时的待处理伤害及父子重挂接限制、验证夹具未切换手动生成、击退样本采集期间完整 FSM 恢复追逐、RenderAs2D 合成材质误用 Sprite Shader。上述均未通过忽略异常或放宽伤害断言处理。

一次带图形作业的 D3D11 启动在进入 Boot 脚本前发生 UnityPlayer 原生崩溃；后续图形验证使用启动参数 `-force-d3d11 -force-gfx-direct` 和独立临时缓存。项目正式图形配置未改；不宣称修复 Unity 原生启动问题。黑帧和被错误材质铺满的白帧均不计为画面通过证据。

未运行跨机器互联网、长时压力与所有延迟组合下原始 0.107 秒窗口的逐帧远端命中测试。原始窗口的区域命中由 PlayMode 验证，交接采用上述延长阶段夹具；不能把交接通过解释为任意延迟下都有完整远端命中窗口。现有过期阶段不补结算伤害的联网语义保持不变。

## 本轮修改清单

- 新增 NetworkEnemyLustSinner Variant、55 项源资源及对应 `.meta`。
- 新增 `EnemyPrefabVariantMigration.LustSinner.cs`；现有迁移类抽出共用差异复制，增补可选 LustSinner 校验与注册。
- Sandbox 生成器增加可选 Prefab 命令行选择，保留默认选择。
- EnemyAttackMelee、EnemyAttackPrefab、NetworkEnemyMeleeReplica、PlayerDamageInteraction 补齐停用时的攻击实例和待处理碰撞清理。
- 新增 LustSinner EditMode、PlayMode 与图形进程夹具；现有独立进程工具增加敌人选择和图形诊断选项。
- Boot、Sandbox 各增补一条注册。其他既有敌人和正式 Gameplay 未修改。

详细文件清单在 `Logs/LustSinner/changed-existing-files.json`；源资源和覆盖清单在 `resource-mapping.txt`、`shader-mapping.txt`、`prefab-report.json`。
