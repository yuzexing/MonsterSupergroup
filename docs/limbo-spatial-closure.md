# Limbo 空间机制收尾记录

**美术状态更新（2026-09-15）：** 七套参考身体及八组提示已接入来源资源；本页下方的红点、普通 Skeleton 替代精英、橙色线条描述属于当时构建。最新美术验证、保留差异与待验项见 [序列帧美术恢复](limbo-art-restoration.md)。本页已有的数值／行为证据继续保留，不能自动视为新美术构建的运行证据。

本批从 2026-09-15 开始。人工玩法压力测试推迟至整体接入完成；本文只记录规则、生命周期与联网技术验证，不评价难度。

## 已复现并修复

`EnemyHandoffGameplayTests.ReferenceRepositionAppliesPoseEvenWhenServerRemainsSimulator` 通过真实 Boot/Host 创建敌人，再将模拟方设为服务端并调用现有重定位入口。

- 修复前：请求接受、epoch 更新，但 Transform 与目的地仍相距 **9.0**，测试失败。原始结果：`Logs/LimboReference/spatial-reposition-regression-before/results.xml`。
- 原因：服务端继续模拟的优化分支跳过了检查点位置应用。
- 修复：在既有原因枚举末尾添加 `ReferenceReposition`；只有明确重定位不走上述优化，正常换目标行为保留。
- 修复后：Transform、Rigidbody、epoch、重置版本及移速倍率检查通过。原始结果：`Logs/LimboReference/spatial-regression-after/play.xml`。

## 多人屏外适配

已按用户选择改为全员屏外才处理：任何活跃参与者可见即清除计时；全员屏外持续 5 秒或到所有视野的最小距离达到 20 才处理，出生宽限仍为 30 秒。无参与者不处理。远端使用现有同尺度视野模型，并共用地图限位、围栏镜头推拉的几何；单人使用 Host 实际视野。

这是一项明确的多人适配，不声称原游戏存在同样的多人机制。位置失败不移动或重置，到期仍须满足屏外条件，精英不套用普通重置标记。

## 当前检查结果

| 类型 | 实际结果 | 限制 |
|---|---|---|
| EditMode | Imp／Stage2／Reference／Spatial 33 项通过；新增占用诊断后 Spatial 10/10 通过，共 34 个不同用例 | `spatial-final-regression-20260915/edit.xml`、`occupancy-edit.xml` |
| PlayMode | 重定位回归、原生陷阱生命周期、逐帧生成协程，4/4 通过 | 同目录 `play.xml`；不替代双窗口 |
| B／围栏暂停与取消 | 首轮 8 组双端完成；取消补录 6 组，客户端实际收到取消前的阶段 | `spatial-matrix-20260915-*`、`spatial-cancel-settled-20260915-*` |
| 围栏界面重开 | Host 点击结算界面“重新开始”，两端进入新轮次，新旧两轮均完成 85 秒及清理 | `spatial-barrier-pause-clean-20260915`；两轮角色、runId 记录独立 |
| 全员屏外、重新入屏、普通／精英重置 | 服务端模拟、客户端模拟各一组双端完成 55 秒 | `spatial-reposition-server-final-20260915`、`spatial-reposition-client-final-20260915` |
| 找点失败 | 38–43 秒测试障碍令合法性查询失败，未移动／重置；43.008 秒解除障碍后处理 | `spatial-boundary-20260915-placement-failure` |
| 到期无经验淘汰 | 普通敌人片段到期后保留至 40.270 秒屏外处理；精英不淘汰、不恢复生命 | `spatial-boundary-20260915-expiry` |
| 距离入口 | 出生约 1.03 秒，首次处理 31.044 秒；不额外等待 5 秒。两端后续次数与状态一致 | `spatial-distance-once-20260915` |
| 围栏镜头模型 | 双端 110 秒完成；扩大视野重新看见敌人后，首次屏外处理推迟至 39.110 秒 | `spatial-frame-visible-20260915-framing`；过渡帧按战斗时钟对齐，不能仅用低频波次快照 elapsed 比较 |
| B 阻止围栏尝试 | 5–15 秒占用时不掷随机数，概率始终 6.67%；16、17 秒失败递增，18 秒按 20% 尝试生成 | `spatial-occupied-20260915-spatial-overlap-occupancy`；3、25 秒 B 跳过，65 秒 B 正常生成，两端接触样本 107/97 |

`--limbo-spatial-case=pause-all` 在各可观察阶段由服务端暂停 1.2 秒真实时间；`cancel-Delay/Framing/Building/Shrinking/Stopping` 在对应阶段暂停后走现有结束与清理入口。动作写入 `spatial-actions.jsonl`，与被动 `spatial-observation.jsonl` 分开。默认 `observe` 不改变运行。

本轮空间技术矩阵通过，已将覆盖的 B／围栏生成机制设为 Ready；敌人行为门槛独立保留，Full 继续禁用。Dash 随后的接入与389.6秒单人／双端技术预览已完成，见 [Dash验收记录](limbo-dash-integration.md)。人工玩法压力与同条件原游戏正常流程对照仍未完成。

## 记录与限制

- 普通 Skeleton 的测试受伤值为 40/50，精英为 1190/1200。首次超时处理分别在约 40.168、40.285 秒：中途重新入屏确实推迟处理。普通每次重定位恢复至 50 HP、速度 2；精英保持 1190 HP、出生速度倍率，重置版本为 0。
- 测试禁止敌人移动／攻击，分别指定服务端或客户端模拟，并使用现有 Ledger 施加一次 10 点测试伤害。站位、禁用武器和测试障碍均是隔离输入，不作为玩法压力记录。并非敌人池复用证明。
- 33–35 秒早期测试把普通敌人放在 Host 身体中心，Replica 物理分离产生最大约 0.182 世界单位偏差。分析单列这个强制重叠段；重定位后的正常样本用于检查位姿一致。
- `spatial-boundary-20260915-distance` 的旧测试仍在首次重定位后反复把敌人放回远处，产生 63/59 次重定位。这不是正常重定位频率的证据；由 `spatial-distance-once-20260915` 替代。旧报告和归档保留，收录不表示通过。
- 早期手动模拟方指定没有走交接发布入口，曾触发超时回退；早期直接修改本地生命没有广播。已修正采集工具，旧 `spatial-reposition-server-b-20260915` 仅保留诊断价值。
- 第一次覆盖旧构建目录出现 Addressables 本地化 CRC 不匹配；全新构建目录解决，未关闭 CRC 检查。含错误面板的旧画面不作为图形验收。

四份已归档证据位于 `Logs/LimboReference/Archive-spatial-{baseline,closure,boundary,distance}-20260915`。各自 `manifest.json` 记录所用构建 DLL、来源快照、文件及压缩包哈希；不同构建不混称同一版本。底层逐帧浮点结果不强行钳制为半径 10，简化橙色展示保持原先适配，原碰撞结构未改薄。

最终占用与镜头用例另存 `Archive-spatial-occupancy-20260915`。`Archive-spatial-boundary-20260915` 中旧距离用例的自动摘要在归档时尚未识别测试反复定位问题；本报告撤回该旧用例通过结论，采用新的一次性距离输入记录。原文件和压缩包均保留，不回写历史。
