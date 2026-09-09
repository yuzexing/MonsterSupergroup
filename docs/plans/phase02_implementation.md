# Phase 2：武器与攻击迁移验收记录

本文承接 [迁移总计划](F:/UnityStore/MonsterSupergroup/docs/plans/hellmaiden_multiplayer_migration.md) 和 [Phase 0–1 实施记录](F:/UnityStore/MonsterSupergroup/docs/plans/phase01_implementation.md)。**Phase 2 已完成，现停止并交由用户审阅；未进入 Phase 3。** 本阶段接入七个代表性攻击族，保留现有 GAS 与混合战斗权限模型，分离 Owner 执行、服务器准入/结算和 Remote 本地表现。


当前 `NativeGasWeaponDB` 包含 ID 2 / 1 / 3 / 6 / 8 / 402，产品默认初始武器仍为 ID 2。Ultimate 保留独立能力定义，不占用武器槽，不加入 WeaponDB。源资源依据和已确认规则见 [剩余攻击族审计](F:/UnityStore/MonsterSupergroup/docs/plans/phase02_remaining_attack_audit.md)。

## 审阅入口与完成范围

| 攻击族 | 已完成的主要边界 | 验收 |
|---|---|---|
| Projectile，ID 2 | 攻击根准入、冻结快照、命中去重、在途效果租约和冷却恢复 | 既有 Native / 网络链路及阶段回归通过 |
| Melee，ID 1 | 多斩共用根与快照、各斩独立命中、完整序列冷却 | 真实三斩、Host / server-only、重连通过 |
| Beam，ID 3 | 阶段和转向同步、持续命中、取消与回收 | 真实三次命中、Host / server-only、重连通过 |
| Circling，ID 6 | 独立 orb 身份、冻结轨道、原 Duration / Hide / 冷却语义 | 真实命中、原资源画面、Host / server-only、重连通过 |
| Dash，ID 8 | Owner 输入缓冲/预测、服务器充能许可、真实世界轨迹与自然尾迹 | 移动与伤害、原资源画面、Host / server-only、重连通过 |
| Summon，ID 402 | Pet 与攻击身份分离、原 FSM、绝对破茧时间、当前阶段和 Pose 恢复 | 原三变体、真实命中、Host / server-only、重连通过 |
| Ultimate，ID 0 | 独立充能/许可/无敌、原双波与 Burn、起手击退路由 | 原资源画面、真实伤害、Host / server-only、重连及服务器单独停用/启用通过 |

最终回归：EditMode **413/413**、完整 Gameplay PlayMode **316/316**。最后的 Summon 数值边界修复另有 **17/17** 定向回归。最终构建 `Phase2-Build-Final-1.log` 和 `Phase2-Build-Final-2.log` 均退出码 0；前者完成 Summon 最终进程验收，后者仅修正 Ultimate 测试对 Host 共享组件的断言，并完成 Ultimate 最终进程验收。详细报告、命中数和验证脚本见下文。

验收使用 KCP 的 Host + 独立 Client，以及 Windows Development Player 的 server-only + 两个独立 Client。尚未实测两 Steam 账号或 `UNITY_SERVER` 平台构建。原 FMOD bank 缺失、部分导出 shader/曲线缺失，已恢复可用画面并记录近似；不表示音画完全复原。Ultimate 初始充能仍为零，服务器充能 API 已接好，奖励/掉落入口属于后续阶段。

## Dante Dash：接入与验收通过

- ID 8 保留 Damage 9、Speed 1、Duration 1.25、Count 1 和原 Fire / Poison 两种 Trail。玩家移动恢复原 Systems 场景中的 Dash 曲线、障碍/边缘/排除层配置，以及 0.1 秒输入缓冲。`PlayerMovement` 只消费注入的统一提交接口，原 `GameEvents` Dash 通知和异步补充充能路径已移除。
- `PlayerDashRuntime` 使用每次独立的绝对补充截止时间和原 0.15 秒连续冲刺间隔。`NetworkPlayerDash` 分开持有服务器状态和 Owner 预测，包括 Host；确认较早输入时保留后续未确认输入的消耗。容量下降也不丢失已用充能。服务器根据实际定义/曲线计算规范时长，网络接收容差不能缩短充能门槛。
- 一次合法 Dash Use 可为当时 Build 中每个 Dash 武器槽发放一次攻击许可。Dash 使用专门的 root 命令，不能绕入普通武器冷却入口。真实命中继续走既有 Snapshot / GAS / ServerAttackRegistry / Ledger，未新增伤害执行系统。
- 轨迹保留源严格投影距离判断、每帧最多一个世界点、Duration 内继续采样、每个点独立 Duration / 2 伤害寿命和粒子自然尾迹。结束冲刺动作不提前结束 1.25 秒采样。Remote 只重放可靠的实际世界点，使用消息年龄推进原粒子；不按远端 Avatar 插值位置自行造点，不创建 GAS、伤害回调或 NetworkIdentity。
- 重连 checkpoint 只新增 Dash 充能/截止时间数据；未保存或重播旧轨迹。原缺失 FMOD 事件 `{0ddbe74c-0c1e-4afc-a293-557438dbd8e0}` 在播放入口局部记录 `EventNotFoundException`，保留定义并继续无声攻击，不吞掉其他异常。

| 已执行验证 | 证据与范围 |
|---|---|
| 主项目资源导入 | `Logs/Phase02/Dash-Import-1.log`，退出码 0；ID 8 已入库，默认初始武器仍为 ID 2 |
| 资源和协议检查 | `Dash-EditMode-1.xml` 的 315 项中 311 通过；其中 Dash 资产 11、Circling 资产 14、Dash 服务器许可 10、预测确认 13 全部通过。4 项失败来自新 Trail 合同测试的无效 Count fixture，已修正；`Dash-TrailContract-2.xml` 随后 7/7 通过。追加的窗口边界测试已由最终 413 项回归覆盖 |
| 移动 / 充能 / 真实 Boot Host | `Dash-Movement-1.xml`，39/39：12 项纯充能、26 项 Movement、1 项真实 Host 确认/取消/禁用重绑；追加的非法命令集成测试已由最终 316 项 Gameplay 回归覆盖 |
| Native / 真实碰撞 / 生命周期 | `Dash-Native-1.xml`，16/16：15 项 Dash Native 加已有 Host 测试；包括 frozen 9 点伤害、原 Speed 秒间隔、0 点结束、尾迹租约、取消重入、池复用和 Unequip |

后续验证已覆盖上述初轮缺口：

| 验证 | 实际结果 |
|---|---|
| Native / Remote / Host | `Dash-Summon-Native-1.xml` 中 Dash Native **16/16**、真实 Trail Replica **6/6**、Boot Host **2/2**；同次 Summon 初轮测试另有 10 个 fixture counter 未重置失败，不能把整份报告称为通过 |
| Remote 尾迹回收修复 | `Dash-ParticleClock-1.xml` 及日志证明原问题是空 loop 粒子系统停在 Pause 后仍 `IsAlive=true`。现在接收点时补时，随后恢复原始自然播放/StopEmitting；不修改尾迹寿命，不强杀残余粒子。新增 aged-tail 测试通过 |
| 图形预览 | `Dash-Preview-1.log` 退出码 0；已查看 `Dash-Preview-Fire.png` 和 `Dash-Preview-Poison.png`。各 35 个粒子系统、83 个存活粒子、22 个有效 Renderer；Fire / Poison 原变体和黄色 follower 元素保留。缺失自定义 shader 的近似限制仍适用 |
| 验证构建 | `Dash-Build-1.log` 退出码 0，输出 `Builds/Phase02/DashValidation.exe` |
| Host + 独立 Client | `Logs/Phase02/Dash-Host-7954-20260909-160049-680`，两进程 PASS |
| server-only + 两独立 Client | `Logs/Phase02/Dash-Dedicated-7955-20260909-160049-658`，三进程 PASS；服务器没有 Trail/粒子表现 |

两个联机模式中，每个 Owner 通过真实缓冲 `PlayerMovement.Dash()` 产生一次根攻击、10 个原算法采样点；移动结束后仍产生 4–6 个合法采样点。真实碰撞两次各 9 点，服务器确认总伤害 **18**、目标 HP **982**、攻击根最终 **0**。每次消耗一个 Dash Use，普通武器冷却入口计数 **0**。Remote 轨迹保持世界位置、无 GAS 伤害、采样停止后原尾迹自然回收。

第二次合法冲刺后断线，服务器保留两次独立补充截止时间；重连换 Avatar / Epoch 后剩余充能 **0**，Host 模式剩余 **26.976 / 29.750 秒**，server-only 模式 **27.111 / 29.833 秒**，没有退款或旧轨迹重放。测试只在内存中把 recharge 改成 30 秒、Crit 设为 0；产品配置仍为 2.5 秒和原 Crit。日志唯一已知异常为上文缺失 FMOD 事件。

Dash 已纳入阶段整体回归；Summon / Ultimate 的后续验收记录如下。

## Ovid Summon：接入与验收通过

- `Summon-Import-1.log` 主项目导入退出码 0；ID 402 加入 NativeGasWeaponDB，默认武器仍为 ID 2。三变体、六个原 clip 和原 60 秒破茧字段已保留；原导出缺失引用/参数恢复方式见导入器 `RestorationLimits`。
- Gameplay 的 Summon FSM 只使用注入的 Owner、session clock、pet ID 分配器及现有网络敌人视图查询，不运行旧 EnemyManager / GameDirector 路径。召唤物 ID 与每次攻击 root 分离；Enter / Main / Exit 共用冻结 GAS Snapshot。
- 服务器保存每个已装备 Summon 的绝对破茧截止时间，checkpoint 按用户确认在离线期间继续推进。Owner Build baseline 先恢复 maturity/clock 绑定，再恢复普通攻击 cooldown。Dedicated/server-only 不生成 Summon AI 或表现对象。
- 可靠阶段/结束消息与 10 Hz 可丢弃 pose 分开；Remote 按 pet ID 创建本地表现，当前阶段及最新 pose 可给重连观察者补齐。已终止的 pet ID 不能换槽重放，同一攻击的表现 Stats/Element 不能中途更换。
- 实际 prefab 的 Mover 位于子节点，AI 使用原模块持有的显式 Mover / ProgressionScaler 引用；网络 Pose 使用该 Mover 的真实 Rigidbody 根，并在重放后恢复 Owner Z 平面，不改变源层级。原 Exit 在 .33333334 秒关闭 Collider、.6333333 秒失活 Laser 祖先，因此不能额外保留失活后的命中尾巴。
- 同一攻击进入 Positioning 时，用实际完成时间结算原冷却，不把完整 Exit 或网络往返时间重复加到冷却中。服务器准入仍检查破茧、Birth、普通冷却及 Build 槽位，取消只提前结束序列，不退还已使用的冷却。

| 已执行验证 | 实际结果与范围 |
|---|---|
| 资源与协议回归 | `Summon-Ultimate-Edit-1.xml` **410/410**；包含全部 NetworkCombat 测试和七个已迁入攻击族资源测试类。Summon 28 项资源测试通过；原 PolygonCollider 的序列化 `m_IsTrigger:0` 被保留，运行时 HitBox 按原初始化逻辑启用 trigger |
| Native FSM / GAS | `Summon-Ultimate-Play-1.xml` 中 **22/22**；覆盖源计时、组合阶段冻结快照、击中、取消和重建 |
| 真实远端 prefab | 同份报告中 **8/8**；按真实 Mover 根重放 Pose，原 Animator 引用和无 GAS 边界通过 |
| Boot Host 生命周期 | `Summon-Ultimate-Host-1.xml` **4/4**；包括恢复 Birth 后再应用 cooldown，以及新 Avatar 的预生成 checkpoint 准备。该测试不是 socket 断线重连证明，独立进程验收另行记录 |

`Summon-Ultimate-Play-1.xml` 整份为 50 项中 49 通过，唯一失败为 Host fixture 错误创建无主 OwnerFinal 玩家，后续上述 4 项 Host 重跑已修正。初轮 `Summon-EditMode-1.xml` 的三项失败来自错误的序列化 trigger 断言，后续 410 项回归已覆盖。图形和独立进程验收随后完成，见下文最终复测。

独立进程补充：`Summon-Build-2.log` 构建退出码 0，`Logs/Phase02/Summon-Dedicated-7957-20260909-165028-845` 的 Server / Client / Client2 全部 PASS。每个 Owner 的原 OvertimeHitBox 实际五次命中，每次 24、共 120，服务器 HP **880**，一根准入并最终清空；其中一次命中在 collider 关闭的源退出宽限内。Remote 收到六阶段和 291 次 Pose，无 AI / GAS / 伤害执行。茧期间重连换 Avatar 和 Pet，剩余时间由 **17.539 秒减至 15.364 秒**，保持绝对 maturityAt；之后观察者再重连，只收到现有 Pet 的 Positioning 和 Pose，不重播旧攻击。该 fixture 仅在内存将茧期缩为 20 秒、Crit 设为 0，原 Attack clip、24 点基础伤害、Duration 和 5 秒普通冷却不变。

同轮 Host 命中和阶段已通过，但检查把可丢弃通道跨阶段抵达的旧 Pose 计为协议错误。处理现已区分非法消息和不更新历史的正常丢弃，最终 Host 重跑通过。初轮两个模式的命中失败来自测试目标缺少 Mirror Identity 初始化，已按未生成网络对象的 fixture 正确初始化，未放宽产品伤害校验。

图形补充：`Summon-Import-Shadow-2.log` 和 `Summon-Preview-3.log` 均退出码 0，已检查三变体 Main 图。原长阴影仍保留原 Sprite、白 tint、位置和缩放；仅原 Multiply 材质改用有明确近似说明的乘色渐隐 shader。`Summon-Shadow-Evidence-1.log` 的完整 Main / 隔离 Shadow 中灰图已实际查看，长轴六点暗度 **59.33 → 57.33 → 33 → 9.67 → 2 → 0**，尾端 RGB 回到背景 **128/128/128**，增亮像素 **0**。这验证了透明合成与连续渐隐，仍不宣称恢复了缺失的源 SSU rotation/noise 公式。

重连表现补充：首次历史基线及晚到阶段使用原短 clip 的实际激活边补齐粒子时间，后续由 Unity 自然播放。原 Cocoon / Move 不启动粒子，长时间基线不会逐帧遍历 60 秒；Main 超过粒子存活窗口的整周期会跳过。已在场的系统按原动画过渡保留时钟，池归还清空粒子并恢复原始激活状态，过期非循环 burst 不再启动。Owner 在重连后处于历史 Birth 时复用同一补时，不穿越 Native 攻击碰撞阶段，也不重放历史音频。只有当前 Pose 被同步，因此不会宣称逐粒子还原过去移动轨迹或随机身份。

最终同构建复测：`Phase2-Build-Final-1.log` 退出码 0，输出 `Builds/Phase02/Phase2Validation.exe`。`Summon-Host-7956-20260909-171330-218` 两进程及 `Summon-Dedicated-7957-20260909-171330-218` 三进程全部 PASS、退出码 0。实际五至六次合法命中，每次 24，服务器 HP 对应 **880 / 856**；全部共享一根且每次命中事件独立，最终根为零。原动画切换/物理帧和退出宽限决定实际命中次数，测试校验逐次结果与权威总额，不强制写死五次。Remote 六阶段、289–290 次 Pose、无 AI / GAS；茧期间重连剩余时间 Host **19.818 → 17.645 秒**、server-only **17.610 → 15.405 秒**。角色与 Pet 身份更新，现有观察者补当前 Positioning，不重播已结束攻击；旧 Pose 的正常丢弃未改变任何已接受状态。

## Dante Ultimate：接入与验收通过

- `Ultimate-Import-2.log` 主项目导入退出码 0；保留源 Ultimate ID 0、76 项依赖、主节点 20 个 Transform / 10 个 ParticleSystem，以及每波 428 个 ParticleSystem。原动画在 0 和 2.116667 秒触发两波，各波 2.5 秒，完整序列为 4.616667 秒；原 0.25 秒是混合时长，不推迟第二波。
- `NetworkPlayerUltimate` 保存独立服务器充能状态及 Owner 复制视图，即使 Host 也不共用可变状态。统一 `RequestUse()` 只发请求；`ServerGrantCharge()` 留给后续权威奖励入口，本阶段不接旧 `UltimateItem`，也不自动赠送充能。
- 许可成功后才运行 Owner 的原 Animator 和 GAS executor。能力使用独立高位 ID 命名空间和既有攻击准入 Registry，保留共享 Perk multiplier 与源 OnHitBurn（100% 触发、伤害倍率 .1、8 次、间隔 .5 秒）；不生成新 Buff Runtime 或 Build 武器槽。两波共用一个冻结快照及攻击根。
- Remote 只重放原动画和粒子，不创建 GAS、伤害碰撞或 NetworkIdentity。Dedicated/server-only 不创建 Ultimate 攻击表现。主节点直到获准使用才激活，避免无充能时空播原粒子。
- 服务器 checkpoint 只保存充能及绝对 Active / Invulnerable 截止时间，断线继续推进且不重播旧攻击。无敌作为独立原因参与既有 Combatant / Ledger 判断，结束时不会清除升级选择或其他无敌来源。
- 起手半径 5 的脉冲使用源 KnockbackSettings，由服务器按当前 Enemy assignment 发送给唯一模拟者；ClientPlayer 经可靠 Endpoint，ServerFallback / ServerAuthoritative 本地执行。命令包含 assignment epoch 并去重，移交取消旧位移协程。仅修复现有 movement-only Enemy 的旧 FSM 空引用，不迁入 Enemy AI、掉落或 Phase 3 流程。波命中自身仍保留源 Knockback_None。
- Gameplay 当前没有源 ProCamera / presets 配置，因此不通过旧全局 CameraEffects 猜测镜头震动。自定义 shader、三条无法解码的导出曲线和音频恢复限制保留在导入器记录中；真实 PlayMode 视觉采样已完成，见下文。

初轮证据：`Summon-Ultimate-Play-1.xml` 中 Ultimate 纯状态 **7/7**、真实 Native 动画/GAS/取消/远端 **5/5**、真实 Rigidbody movement-only 击退 **4/4**；`Summon-Ultimate-Edit-1.xml` 中 Ultimate 资源 **12/12**、击退协议 **32/32**、独立无敌原因 **2/2**。实际 Boot Host、图形及独立进程证据分别记录在下文，不用这些单元测试替代。

Host 补充：`Ultimate-Host-3.xml` 中五项 `NetworkPlayerUltimatePlayModeTests` 全部通过，覆盖实际 Boot 充能/许可/两波 GAS、版本校验、重复请求、延迟 ACK、组件清理及旧 ACK 不覆盖新充能。该报告另三项真实起手击退暴露原备用寻路组件尚未初始化 Rigidbody；现在只在原 `usesPathfinding` 条件下暂停/恢复该组件。随后 `Ultimate-Knockback-Recovery-1.xml` **8/8**（五项真实移动 + 三项实际 Boot / Enemy prefab 路由）通过，包括 ClientPlayer、ServerFallback、ServerAuthoritative、Frozen、重复和旧 epoch 丢弃、权限移交清理与三份 HP 不变。报告中未包含 Remote 恢复测试：其首次元数据 GUID 非法被 Unity 忽略，已修复并单独重跑，不将过滤名称当作已执行证据。

Remote 补充：`Ultimate-Replica-Recovery-2.xml` **3/3**。短暂禁用只保存正在播放的完整 root 身份，重新启用后收到服务器同一活动根时按当前 age 重建；正常重复、旧 epoch 的同序号、已结束和过期根均不能恢复，结束消息会撤销恢复资格。

真实画面：`Ultimate-Preview-4.log` 在空 PlayMode scene 使用普通 Camera / RenderTexture 完成，并实际查看三个 PNG。0.5 秒有一波；2.3 秒有两波、866 个 ParticleSystem、5217 个存活粒子；第二波由原事件在 **2.116665 秒**触发；4.4 秒剩一波、4486 个存活粒子，完整序列 4.616667 秒释放。Replica 的 GAS runtime、WeaponData 和 wave Snapshot 均为空，碰撞器禁用。前三次预览因 Editor / PreviewScene 抑制 AnimationEvent 而失败，最终改正采样场景，没有向生产代码增加第二波计时兜底。

初轮多进程：使用 `Summon-Build-2.log` 的同一 Development Player，`Logs/Phase02/Ultimate-Host-7958-20260909-165408-630` 两进程、`Logs/Phase02/Ultimate-Dedicated-7959-20260909-165439-580` 三进程均 PASS。原 Animator / Physics2D 产生两次各 100 点命中；原 Burn 在第二波按现有 HighestPriority 刷新规则更新，观测 11 或 12 次各 10 点，服务器逐次结算与 Owner 对齐，最终攻击根和 Status 均清空。Remote 重放两波并随所属玩家移动，无 GAS 或伤害执行；server-only 无攻击粒子。第二次使用中断线保留已消费的充能与绝对截止，重连换 Avatar/epoch、不重播旧攻击，再获服务器充能后仍完成真实两波与 Burn。

最终检查另补服务器独立禁用 coordinator 时的立即退役和可靠 Owner 停止通知，以及 Owner 重新启用不回退较新 ACK 版本；新增行为已分别完成 Host 测试与独立进程验收。

`Ultimate-Lifecycle-4.xml` 随后 **7/7**，包含上述两项追加 Host 回归。

最终多进程：`Phase2-Build-Final-2.log` 构建退出码 0，`Logs/Phase02/Ultimate-Host-7958-20260909-171944-498` 两进程、`Logs/Phase02/Ultimate-Dedicated-7959-20260909-171944-498` 三进程全部 PASS、退出码 0。服务器单独停用 coordinator 后立即退役攻击，独立 Owner 停止 Native 执行，旁观端停止表现；已消费充能与绝对截止时间保留。重新启用不重播旧攻击；到期并由服务器重新充能后，真实两波造成 **200** 直接伤害，既有 Burn 本轮产生 **12 × 10**，服务器 HP **680**，最终攻击根与 Status 均为 **0**。

前一次 Host 验收的失败属于测试角色断言：Host 的服务器组件和远端视图共用一个 Unity 组件，不能要求服务器将其禁用后该组件仍 enabled。最终只修正这条测试断言；独立客户端仍明确要求其自身组件保持 enabled 并响应可靠停止通知。两种进程模式均验证了重新启用后的完整新攻击。

## 阶段整体回归记录

- `Phase2-Edit-Final-2.xml`：**413/413**，全部 NetworkCombat 测试和七个已迁入攻击族资源测试类。前一轮新增 Shadow 资产测试已通过，但三个旧层级检查仍要求所有材质都是 AllIn1；现只对原 Shadow 的精确路径检查专用 shader，其余材质和空引用约束不变。
- `Phase2-Gameplay-Final-1.xml`：316 项中 313 通过。一个旧测试错误要求权限失去再获得后继续保持本地冷却；现明确要求重新采用服务器基线，同权限内重复 baseline 仍保持当前冷却。另外两项涉及晚到 Main 的原动画粒子切换和过期 burst，修复及对照验证已由下一轮覆盖。
- `Phase2-Gameplay-Final-2.xml`：完整 Gameplay PlayMode 程序集 **316/316**，上述冷却与粒子问题已通过。Main 粒子时钟直接对照原 `SetPhase / Animancer.Play` 过渡，未将推算的跨阶段时间累加写成新的产品语义。随后只追加极大有限 age × simulationSpeed 的 double 有界运算，并由下述实际 Replica 和 Host 恢复定向回归覆盖。
- `Summon-Age-Final-3.xml`：上述最后的数值边界修复后 **17/17**，包括 13 项真实 Summon Replica 和 4 项 Boot Host。覆盖极大有限 age、源粒子 simulationSpeed、不会过期重播、Owner 历史 Birth、正常旧 Pose 丢弃与非法 Pose 拒绝。

## Dante Circling：接入与验收

- 定向导入 ID 6 / Pyrrhic Dance 的 55 项源资源，保留 Damage 12、Speed 1、Size 1.1、Duration 3.14、Count 2、Crit 0.05 × 1.4、Knockback 0.2。原 emitter 的半径参数为 2、角速度参数为 2，因此基础实际半径为 2.2、角速度为 2 rad/s；保留 emitter 的 Y=-0.5、X=45° 和 orb 根零旋转。
- 根据源脚本、clip 和层级恢复缺失的 AnimatedAttack 引用，明确采用 Start 自动进入 Main、Main 由 Circling 外部计时结束。原 Show 为 0.016666668 秒，Idle 是零长度循环姿态，Hide 为 0.8 秒。**Duration 包含 Show**；普通冷却在公转结束后开始，与 Hide 重叠，不能改成 Show + Duration + Hide 后才冷却。`SequenceSeconds` 冻结公转 Duration，接回既有剩余冷却 checkpoint。
- `CirclingAttackBehaviour` 一次攻击只创建一个不可变 Native Snapshot/root，每颗 orb 独立持有 lease 和固定 `OrbIndex`、初相、半径、角速度及 Duration；移除一颗不重排其他 orb。在途 Build 修改不重读快照。高攻速下下一根可能在旧 Hide 期间开始，此时先取消旧实例，再为新根初始化，避免跨根复用仍存活的快照。空轨道不额外保留 movement lease，因此最后一颗球被外部移除后不会因空转协程悬挂根许可。
- 公转保留源 `Time.smoothDeltaTime` 积分，包括最后一帧超过 Duration 的实际 elapsed。保留原 OvertimeHitBox 的立即命中、0.75 秒间隔和退出后 0.5 秒宽限；只恢复源 Size 缩放，不把 Speed 增益额外应用到命中间隔。Hide clip 从起始即关闭 collider，但不私自改写源退出宽限语义。
- `OrbitPresentationSpawn / Hiding / Termination` 经 PBR 和既有 NetworkWeaponCombatAdapter 可靠转发。服务器检查已准入根、批次/来源、每颗球的重复和终止状态、同根固定数据及有限数值；最终相位使用该 `OrbIndex` 的真实初相校验。角度组合用 double 计算并显式限制 float 范围，避免不同 Mono 浮点中间精度绕过非有限值检查。
- Remote 根据冻结轨道数据本地公转；可靠 Hiding 携带源实际 `OrbitElapsedSeconds`，校正最后位置，并按消息年龄 seek 原 Hide，之后不再公转。它不需要 Aim 消息，不创建 GAS Runtime、攻击快照、命中回调或额外 NetworkIdentity。远端自然 Hide 可以先于可靠 Termination 结束；收到终止边但已无活动实例属于合法时序，不能要求每次终止消息都实际回收一次。
- `WeaponBehaviour.Deactivate()` 先失活 GameObject 再 Dispose，使 Native 移除和场景生命周期能及时取消现有攻击；网络权限边界继续由所属玩家的 CanAttack 与既有 Build 清理负责。Circling 的外部 orb / parent 禁用通过 Deactivated 通知同步清理计数及回调；自然结束回池，Unity 生命周期栈内的外部取消销毁实例。PBR 在 Deactivate 后才退订表现事件；Adapter 在 root 完成时先 flush 所有表现与战斗批次，再注销许可，单独销毁 Adapter 也释放 Orbit replica。

| 验证 | 当前证据 |
|---|---|
| 真实源资产导入与依赖检查 | [Circling-Import-4.log](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Import-4.log)，退出码 0 |
| NetworkCombat 命名空间及四个已导入 Dante 资源测试类的 EditMode 回归 | [Circling-EditMode-2.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-EditMode-2.xml)，**269/269**；资源类为 Dante Native / Melee / Beam / Circling，明确排除未导入 Dash，不是整个 Migration 程序集 |
| 完整 Gameplay PlayMode 程序集回归 | [Circling-Gameplay-2.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Gameplay-2.xml)，**196/196**：157 项既有回归 + 15 项 Circling Native + 12 项真实 Orbit Replica + 12 项 Dash 纯状态测试 |
| Development Player 构建 | [Circling-Build-1.log](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Build-1.log)，退出码 0 |
| Boot → Host + 独立 Client | [Host](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Host-7948-20260909-145439-884/host.log)、[Client](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Host-7948-20260909-145439-884/client.log)，全部 PASS、退出码 0 |
| Boot → server-only + 两个 Client | [Server](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Dedicated-7949-20260909-145439-882/server.log)、[Client 1](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Dedicated-7949-20260909-145439-882/client.log)、[Client 2](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Dedicated-7949-20260909-145439-882/client2.log)，全部 PASS、退出码 0 |
| 真实资产视觉预览 | [修复后预览](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Preview-2.png) 与 [采样记录](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Circling-Preview-2.txt) 已实际查看；原粒子黑色方块消失。`Circling-Import-6.log` 和 `Circling-Preview-2.log` 均退出码 0 |

进程验证使用真实 ID 6 prefab 和 Physics2D → OvertimeHitBox → GAS → Mirror Ledger，只有测试内存配置改为零暴击、1.2 秒 Duration 和 30 秒普通冷却。每个 Owner 的两颗 orb 各命中三次，共六次、72 伤害，服务器目标 HP 从1000变为928；结果共享一个根且每次命中事件独立。每根源端两个结束、六条 Spawn/Hiding/End 发送及接收边，远端实际沿轨道移动、无 Native 快照或伤害执行，最终活动数为零。

本轮每个 Owner 有两次命中发生在 collider 关闭期间，符合原0.5秒退出宽限；没有为获得测试结果改变原命中规则。远端 `ReplicaOrbTerminationCount` 实际出现0和2两种值：自然 Hide 有时先回收实例，之后可靠 End 仍被接收；全部进程均收到六条边并清空活动实例，因此不能将“可靠 End 必须赢过自然结束”作为验收条件。

等待完整合法冷却后启动第二根，并等旁观端确实看到两颗活动球时断线。服务器注销旧 Avatar 的根、旁观端清理旧球；重连获得新 Avatar/epoch，恢复一个 ID 6 武器的 Build、原 SequenceSeconds 和剩余冷却，未重放旧根。Host 组合的返回 Client 剩余30.933秒，server-only组合为30.963秒。server-only 进程不创建攻击表现；它是 Windows Development Player 的显式服务器角色，尚不等于 UNITY_SERVER 平台构建或两 Steam 账号实测。执行入口为 [Run-CirclingProcessValidation.ps1](F:/UnityStore/MonsterSupergroup/Tools/Run-CirclingProcessValidation.ps1)，协调文件只控制阶段和交换验证计数，实际战斗、表现及恢复走生产 Mirror 路径。

资产验证保留源 Root 粒子容器的特定空材质槽：该 ParticleSystemRenderer 和 emission 原本均禁用，且恰有一个 null material。仅对这个已核对的节点保留原值，其他 renderer 材质、shader、脚本和序列化 GUID 仍严格验证，不以全局忽略缺失引用通过验收。

视觉还原仍有限制：源残缺 shader 使用已安装 AllIn1 替代，Rubfish 渐变材质用源 HighColor/纹理作可见回退，其他材质保留 MainColor；自定义粒子光照、像素尺寸自适应、渐变/噪声/拖尾控制及 SSU 阴影摆动尚未恢复。源卡面图标 GUID 无法定位。原 FMOD 引用、参数及 Play/Stop=None 保留，不推测额外播放时序；bank 缺失，测试只在新实例 Awake 时精确预期 `{840d4d3b-6223-4aab-a508-f0bcd8e4de60}` 的缺失事件，池复用不要求每次 OnEnable 报错。当前资源与测试通过不等于音画已完整还原。黑色方块来自 `FX_MT_FireSparks_03` 的源纹理透明通道全满；仅该映射材质启用 AllIn1 的亮度转透明度，保留源混合参数和纹理字节。源 Rubfish opacity 实现缺失，这一处理明确属于近似恢复，不宣称精确复原。

## Dante Beam：接入与验收

- 定向导入 ID 3 / DragonsBreath，保留 Damage 5、Speed 3、Duration 2、Crit 0.03 × 1.3。默认与 Fire 共用 Fire prefab，Poison 独立；保留原单束开关（`allowMultipleAttacks=false`）、半径 1.5、转向插值参数 4、Fire 18 / Poison 19 个粒子系统。
- 恢复导出中缺失的 `AnimatedAttack` 引用。原 In / Idle / Out 为 0.366667 / 4 / 1 秒，全部非循环；显式采用 Start 自动进入 Main、Main 由 Weapon Duration 控制的恢复配置。它是依据脚本和动画做出的恢复推导，不冒充丢失字段的原始值。Duration 大于四秒时保持 Main 最后一帧，仍在 Duration 到期后进入 Out。
- `PlayerBeamAttackBehaviour` 只创建一次 Native GAS 根；所有束共用同一不可变 Snapshot，各自持有 lease。冻结数量、Duration 和展示参数，固定 ordinal/count；更新 Build 不改变在途束的伤害或排列。修复原首次转向跳向零度和少一束后重新分布的行为。
- 保留原 `PlayerAttackOvertimeHitBox` 立即命中、每 0.5 秒重复命中与退出后 0.3 秒缓退语义；迁入源 Speed 自定义 scaler，按冻结增益改变间隔。异步退出任务只移除自己持有的 token；同步命中回调清空、重新初始化或取消 Build 时，不继续遍历旧列表。
- 普通冷却在最后一束结束后才开始，服务器准入的 `SequenceSeconds` 为 In + Duration + Out，使用既有 checkpoint 保留剩余时间。合法零 Duration 仍有 In/Out，网络合同与 Runtime 都允许非负有限 Duration。默认单束规则不会被 ProjectileCount 增益隐式改为多束。
- `BeamPresentationSpawn / Aim / Termination` 经 PBR 和既有 Adapter 转发；Spawn/End 用可靠批次，朝向按根合并并以最多 10 Hz 不可靠批次发送。服务器只转发已准入根，并维护每束去重及终止状态。丢失、乱序或早于 Spawn 的 Aim 可以丢弃，后续采样修正方向；Aim 永远不能创建束。Remote 每根独立插值，不创建 GAS、碰撞回调或 NetworkIdentity。
- 外部禁用时新增 `AnimatedAttack.Deactivated` 同步通知，Beam 在 Root 完成通知前清除 tracking 并排入 End。取消实例通过 `AttackVariantSet.Discard` 移出列表并销毁，避免在 Unity OnDisable 栈里重挂父级；自然结束仍回池。正常 Return 先解除通知，再 Dispose。执行组件被禁用时直接调用 Attack 仍检查 GameObject 是否激活，防止回调里关闭对象后继续生成剩余束。

| 验证 | 证据 |
|---|---|
| NetworkCombat 与迁移资源 EditMode，含 36 项 Beam 合同和 11 项资源验证 | [Beam-EditMode-2.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-EditMode-2.xml)，**238/238** |
| 完整 Gameplay，含 15 项 Beam Native、8 项真实 Replica、9 项延迟特效及所有既有回归 | [Beam-Gameplay-4.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Gameplay-4.xml)，**157/157** |
| Development Player 构建 | [Beam-Build-1.log](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Build-1.log)，退出码 0 |
| Boot → Host + 独立 Client | [Host](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Host-7928-20260909-135133-341/host.log)、[Client](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Host-7928-20260909-135133-341/client.log)，全部 PASS、退出码 0 |
| Boot → server-only + 两个 Client | [Server](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Dedicated-7929-20260909-135133-358/server.log)、[Client 1](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Dedicated-7929-20260909-135133-358/client.log)、[Client 2](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Dedicated-7929-20260909-135133-358/client2.log)，全部 PASS、退出码 0 |

进程验证仅在内存把 Beam 设为零暴击、1.2 秒 Main、30 秒普通冷却，并选择 ID 3 为测试初始武器。使用产品 prefab、原 collider 动画、真实 Physics2D → OvertimeHitBox → GAS → Mirror → Server Ledger。每个 Owner 对本机隔离目标命中三次、共 15 伤害，服务器 HP 为 985；全部结果共享一根但拥有不同命中事件。Remote 收到 Spawn/End、22～24 次方向采样，实际束的位置发生转向，结束后活动数为零。此次记录的 collider 关闭期间命中数为零；这不改变源 0.3 秒退出缓退规则，也不宣称所有帧率下都不会在该宽限内命中。

等待完整合法冷却后启动第二根，在旁观端确实看到 Beam 存活后断线；旧 Avatar 根注销、旁观表现立即清空。重连创建新 Avatar/epoch，保留原 Build 和 SequenceSeconds，仍等待约 32.25 秒，不重放旧 Beam。纯服务器进程全程不创建攻击表现。协调文件只控制测试阶段和交换验证计数，实际战斗、恢复及表现数据走生产 Mirror 路径。`Tools/Run-BeamProcessValidation.ps1` 的 Dedicated 模式等待第一客户端已就绪才启动第二客户端，避免以启动延时猜测成员顺序。

导入入口为 `DanteBeamNativeGasMigration.Import`，预览入口为 `DanteBeamPresentationPreview.Capture`。该轮完成时 NativeGasWeaponDB 包含 ID 2 / 1 / 3，产品默认初始武器仍为 ID 2。[Fire 预览](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Preview-Fire.png) 与 [Poison 预览](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Beam-Preview-Poison.png) 使用原动画和粒子采样至 1.17 秒，通过 D3D12 渲染，分别有 21 / 20 个存活粒子。源 shader 的自定义光照、渐变、噪声/顶点偏移与屏幕扭曲尚不完整；Idle 的两条未知路径保留，源卡面图标无法定位。原 FMOD 事件与参数保留，bank 缺失；PlayMode 只精确预期这一事件的缺失异常。此轮没有进行两 Steam 账号或 UNITY_SERVER 平台构建验收。

## 延迟命中特效：跨玩家归属与清理

专门回归确认原 `SpawnableHitEffectResolver` 的旧 effect 会读取下一次 Initialize 改写的武器引用：即便原 Owner 已移除，旧快照仍可能通过新 Owner 的武器处理命中。现每次生成捕获独立 source、pool、Snapshot lease 和一次性完成标记；旧 hit/end 回调在完成后失效，销毁 resolver 不影响仍存活的独立 effect。原武器被 Unequip、Runtime 已 Shutdown 但对象尚未到帧末销毁的窗口也不会继续处理伤害。

`AttackHitParticleEffect` 在自然结束、重复 Stop、外部禁用及销毁时统一先清回调和检查任务，再释放 lease；正常完成回池，外部生命周期取消销毁实例。初始化经过失活节点，保证原材质/缩放绑定后才触发 OnEnable。9 项测试覆盖两套真实 PBR/GAS 事件来源、19/31 基础伤害、旧 Snapshot 冻结、换目标、换玩家、重复结束、播放失败，以及真实 Dante Impact。最终证据包含在上述 **157/157** Gameplay 报告中。

## Dante Melee：接入与验收

- 定向导入 ID 1 / Blazing Quill，保留基础伤害 19、暴击率 0.07、暴击倍率 1.3、击退距离 0.6、原斩击层次和 10 层粒子。修复导出时丢失的 AnimatedAttack → Animancer / Clip / HitBox / Scaler 引用。原非循环 clip 长度为 0.666667 秒，命中窗口为 0.066667～0.216667 秒。
- `MeleeAttackBehaviour` 通过既有 `BeginNativeGasAttack → AttackSnapshot → BasePlayerAttack.InitNative → ResolveHitDetailed` 执行。整轮多斩冻结相同快照，burst 和每个 slash 分别持有租约；各 slash 的 HitBox 单独去重。Build 更新不重读在途攻击的伤害和斩击数。
- 独立 `MeleePresentationSpawn / Termination` 表达相对玩家的位置、方向、动画时长和只读缩放数据。PBR 转发事件，既有 NetworkWeaponCombatAdapter 用可靠批次发送；服务器检查已准入根并按根去重 SlashIndex。Remote 只重放动画，不创建 GAS Weapon Runtime、攻击快照或新的 NetworkIdentity。
- `AnimatedAttack` 归还时停止旧动画回调与超时，释放快照、武器/玩家引用并清理 HitBox 回调。近战对象池使用失活的取出节点，防止新实例在完成初始化前触发一次额外的 OnEnable。武器移除、场景失活和销毁取消尚未发出的斩击并回收当前实例。单独销毁网络 Adapter 也会退订 Native / Projectile / Melee 事件并清理两类副本。
- 原多斩最后一次发射间隔仍属于 burst，普通冷却在 burst 结束后开始。现有冷却 checkpoint 增加默认值为零的 `SequenceSeconds`，冻结根创建时的发射时长；改速只调整普通冷却，改变斩击数只影响下一根。重连取消旧 burst 的重放，但等待尚未到期的原截止时间。

| 本轮验证 | 证据 |
|---|---|
| NetworkCombat 与迁移资产 EditMode 回归，含近战协议、原命中窗口、完整发射冷却、旧 checkpoint 兼容 | [Melee-EditMode-2.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Melee-EditMode-2.xml)，**191/191** |
| 完整 Gameplay PlayMode，含快照冻结、对象池、远端无伤害、自然结束、移除、改速恢复、Mirror 序列化、Adapter 单独销毁 | [Melee-Gameplay-PlayMode-1.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Melee-Gameplay-PlayMode-1.xml)，**125/125** |
| Development Player 构建 | [Melee-Build-2.log](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Melee-Build-2.log)，退出码 0 |
| Boot → Host + 独立 Client：近战碰撞、Remote 重放、攻击中断线和恢复 | [Host](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Host-7908-20260909-130742-148/host.log)、[Client](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Host-7908-20260909-130742-148/client.log)，全部 PASS、退出码 0 |
| Boot → server-only + 两个 Client：同一近战场景与重连 | [Server](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7909-20260909-130742-160/server.log)、[Client 1](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7909-20260909-130742-160/client.log)、[Client 2](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7909-20260909-130742-160/client2.log)，全部 PASS、退出码 0 |
| 既有 Runtime/进度恢复进程回归（同一生产代码，Build-1） | [Host](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Host-7910-20260909-130324-550/host.log)、[Client](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Host-7910-20260909-130324-550/client.log)；[Server](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7911-20260909-130324-575/server.log)、[Client 1](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7911-20260909-130324-575/client.log)、[Client 2](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7911-20260909-130324-575/client2.log)，全部 PASS |

独立进程使用产品斩击 prefab、原 collider 动画与真实 Physics2D trigger；仅在内存中设为三斩、零暴击、30 秒基础冷却，并选择 ID 1 为测试初始武器。每位 Owner 命中三次，共 57 伤害；服务器目标 HP 从 1000 变为 943，每轮只准入一根；其他客户端实际创建三个斩击副本，收到六个 spawn/end 事件并清空活动副本。等待完整合法冷却后，再创建一根并在斩击仍存活时断线，验证旧根注销、旁观端清理、新 Avatar/epoch、Build 与剩余冷却恢复。重连时 Client 保留约 30 秒等待，未提前开火。文件只协调测试阶段，战斗与恢复数据走既有 Mirror/GAS 路径。

本轮未运行两 Steam 账号验证或 `UNITY_SERVER` 平台构建；server-only 指显式服务器角色的 Windows Development Player。Build-2 相比 Build-1 只扩充进程验收，生产代码相同。初次测试分别发现对象池提前 OnEnable、缺失原音频 bank，以及旧测试路径/源 Renderer 数量断言问题；最终结果以上表为准。

执行入口为 `DanteMeleeNativeGasMigration.Import`（在主项目执行，验证副本的 ParrelSync 会阻止保存资产）；视觉取证入口为 `DanteMeleePresentationPreview.Capture`。进程验证使用 `Tools/Run-MeleeProcessValidation.ps1`，`-Dedicated` 切换纯服务器加双客户端。NativeGasWeaponDB 已保留 ID 2 并加入 ID 1，产品玩家默认初始武器配置未改。

[实际斩击预览](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Melee-Preview.png) 使用原 clip 和粒子逐帧采样至 0.12 秒，通过 Direct3D12 渲染。它确认当前表现可见，不构成与原游戏完全一致的证明：源导出 shader 已丢失部分实现，目前复用已安装 AllIn1 shader。旧自定义粒子缩放/光照和 Rubfish 噪声插值未恢复；源图标 GUID 在导出中也没有可定位资产。原音效引用保留，但对应 FMOD bank 未导入；测试只识别指定缺失事件，不忽略其他错误。

## 已实施的攻击边界

- `NetworkWeaponCombatAdapter` 使用原 Native 攻击事件，通过可靠 Command 提交槽位、武器定义、根事件、Owner 当前收到的 Build revision 和网络时间。
- 服务器检查真实连接与当前 epoch、服务器 Build 中的槽位/武器、Build revision、生命/选择状态及当前冷却。每次通过都消耗完整间隔；抖动容差不能持续提高攻击频率。
- `ServerAttackRegistry` 只登记通过准入的根攻击及其来源。结果必须属于该根、武器和来源，维持原事件序列与 parent/root 关系；去重仍按每个结果 EventId，允许同一攻击的多个独立结果。
- GAS `BuildId` 保留 Modifier 身份语义，未改成装备版本。已获准的根不因后续 Build 更新而失效。
- 表现 spawn 必须来自已登记根；Remote 继续使用已有本地表现重放，不创建第二套伤害运行时。
- PBR 观察既有 `AttackSnapshot` 的最后租约释放，发出 `NativeAttackCompleted`。网络层先发完表现事件与所有待发战斗批次，再注销根。武器被移除时，延迟效果仍可持有原快照；断线清理该来源登记。
- `AttackSnapshot.IsDisposed` 是只读生命周期观察接口；没有改变 GAS 的引用计数、Modifier 执行、暴击或伤害计算。
- 冷却 checkpoint 使用统一的截止时间换算：改速后立刻断线，也能让 Capture、Owner 恢复和服务器准入一致。没有旧 interval 信息的 checkpoint 保留原截止时间。
- 燃烧等 SourceClient 周期伤害不依赖弹丸快照继续存活。只从服务器接受的 Status 创建后续结果许可，核对来源、目标、原/确认后的 parent 身份、伤害和 tick 数；结束后的短暂接收宽限用于已发送结果。Status 本身仍由已有 `ServerStatusRegistry` 管理和执行权限切换。

## 攻击准入基础的既有验证

| 验证 | 当前证据 |
|---|---|
| 根登记、事件链、BuildId 语义、重复结果、冷却、真实 Burn 后续伤害许可与 NetworkCombat 回归 | [Network-EditMode-3.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Network-EditMode-3.xml) **157/157**；已检查包含新增频率与 Status 许可测试 |
| 攻击租约、移除/替换、改速恢复、真实 Boot Host 准入与批量结果先发后注销 | [Lifetime-PlayMode-3.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Lifetime-PlayMode-3.xml) 14/14 |
| 完整 Gameplay PlayMode 程序集回归 | [Gameplay-PlayMode-1.xml](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Gameplay-PlayMode-1.xml) **110/110** |
| Development Player 构建 | [Build-4.log](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Build-4.log)，退出码 0 |
| Host + 独立 Client 重连/Runtime 回归 | [Host](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Host-7896-20260909-121522-377/host.log)、[Client](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Host-7896-20260909-121522-377/client.log)，两端 PASS |
| server-only + 两个 Client 重连/Runtime 回归 | [Server](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7897-20260909-121522-356/server.log)、[Client](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7897-20260909-121522-356/client.log)、[Client 2](F:/UnityStore/MonsterSupergroup/Logs/Phase02/Process-Dedicated-7897-20260909-121522-356/client2.log)，三端 PASS |
| 原升级选择、Native 命中、击杀 XP、菜单中断、2/1/0 候选、重连保留 Build | [Host](F:/UnityStore/MonsterSupergroup/Logs/ModifierSelectionProcess/20260909-122115/host.log)、[Client](F:/UnityStore/MonsterSupergroup/Logs/ModifierSelectionProcess/20260909-122115/client.log)，两端 PASS，退出码 0；六次合法受控攻击击败产品 Enemy 并增加一次等级 |

上述 Runtime 进程基于 Build-1；Build-4 只调整旧选择测试的等待阶段角色保护、受控目标隔离和诊断日志，生产代码未变。旧测试瞬间连续创建根的方式已替换为合法冷却与生产 Native 入口；等待期间保护静止 Owner，并把目标移离其他玩家的自动弹丸，测试专注结果/击杀 XP。首次超时可确认存在 Host 倒地；第二次超时的具体击杀归属没有原日志证明，不将猜测写作生产缺陷。server-only 仍是 Windows Development Player 的明确服务器角色，不等同于 Steam Dedicated Server 平台构建或两 Steam 账号实测。

测试中真实 Dante prefab 仍引用未导入的原 FMOD bank。生命周期测试只明确预期这一已知异常，不屏蔽其他错误；本轮不宣称音频资源完整。

## 本阶段边界与后续工作

根准入不是完整反作弊裁判。当前仍保留 Owner 模拟、服务器合并结果的混合模型；服务器不重新执行玩家 GAS，也不能证明所有客户端碰撞真实。各攻击族的最大命中数、范围、完整派生 Modifier 约束仍须随实际武器迁移补齐。当前周期伤害验证也不等于已完成全部 Buff/Passive 迁移。

当前正式 `NativeGasWeaponDB` 包含 ID 2 Dante Projectile、ID 1 Dante Melee、ID 3 Dante Beam、ID 6 Dante Circling、ID 8 Dante Dash 和 ID 402 Ovid Summon，默认仍为 ID 2。Dante Ultimate ID 0 使用独立能力定义，不加入武器数据库。

上述延迟命中特效风险已复现并修复，见本文件对应验收记录。Circling 的视觉材质问题已经修复并检查；Dash、Summon 和 Ultimate 的真实场景、GAS 命中、远端表现和重连验收均已通过。完整 Enemy AI/掉落/波次、奖励驱动 Ultimate 充能、正式 HUD/菜单、镜头 presets 和整体游戏流程仍按总体计划在后续阶段处理。

## 本阶段攻击族实施顺序

| 顺序 | 资源与代码 | 必须保持的语义 |
|---|---|---|
| 1. Dante Melee / Blazing Quill，ID 1（已接入） | `MeleeAttackBehaviour`、`Dante_Slash_Behaviour`、`PlayerAttack_Dante_Slash` 与 ID 1 定义 | 已验证共享根与快照、独立去重、完整冷却、Remote 表现和攻击中断线恢复 |
| 2. Beam，ID 3（已接入） | `PlayerBeamAttackBehaviour`、Dante DragonsBreath Fire/Poison 资产 | 已验证共享根、重复命中、独立转向、完整冷却、Remote 重放及攻击中重连 |
| 3. Circling，ID 6（已接入） | `CirclingAttackBehaviour`、Dante Circling 资产、Orbit 合同/Replica | 导入、EditMode、Gameplay、构建及 Host/server-only 进程通过；保留包含 Show 的 Duration、与 Hide 重叠的冷却和独立 orb；黑色粒子背景已局部修复并重新查看预览 |
| 4. Dash，ID 8（已接入） | `PlayerDashRuntime`、`NetworkPlayerDash`、Dante Trail 与 Replica | 只有该 Owner 的合法 Dash 可以触发；原输入缓冲、移动曲线、独立充能截止时间、世界采样点和粒子尾迹已通过 Host/server-only 进程验收 |
| 5. Summon，ID 402（已验收） | Ovid Summon 行为、Butterfly 三变体、Maturity checkpoint | 召唤物生命周期与单次攻击身份分开；绝对破茧时间在断线期间继续推进；保持真实 FSM 和原动画命中阶段 |
| 6. Ultimate（已验收） | Dante Ultimate 定义、原双波动画、独立充能 coordinator | 消耗、许可和独立无敌原因由服务器持有；Owner GAS 和 Remote 原表现分开；镜头配置留待后续表现阶段，不能恢复旧全局 Manager 链 |

上述代表性攻击族的资产均已导入，七个资源测试类已纳入最终 413 项综合 EditMode 回归。Summon 的 60 秒茧阶段和用户确认的离线继续计时规则详见 [剩余攻击族审计](F:/UnityStore/MonsterSupergroup/docs/plans/phase02_remaining_attack_audit.md)。源导出的部分 shader 已退化为纯白/简单采样；本阶段另外检查了真实渲染，并记录近似恢复的边界。

本阶段代表性攻击族已完成生成、GAS 命中、服务器许可、Remote 表现和清理验收。Phase 3–7 继续保留在总体路线图中；本次已在 Phase 2 结束处停止，等待用户审阅后的指示。
