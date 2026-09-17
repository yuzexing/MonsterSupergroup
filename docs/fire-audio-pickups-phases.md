# 人工反馈修复：三个独立阶段

2026-09-17 按用户要求拆分实施、验收与交付。每阶段单独记录构建、测试与人工验收，不将后三项混成一次大范围验收。

| 阶段 | 实施与交付 | 验收边界 | 当前状态 |
|---|---|---|---|
| Phase 1 火焰排序 | 共享 B／围栏火焰资源、生成工具、短局入口与独立构建 | 来源分层、工具幂等、池化／Replica 保持；人工看遮挡 | FS-20260917-01；EditMode 23/23、PlayMode 6/6；用户已确认遮挡解决。收缩火焰连续性单独保留 |
| Phase 2 武器音效 | 原游戏声音绑定补采、参考监听位置、鬼火触发与火龙吐息循环生命周期、独立构建 | 事件次数／参数／释放、双端去重；人工听感 | 已实施，WA-20260917-02 技术候选；听感待用户确认，见 weapon-audio-phase2.md |
| Phase 3 通用拾取 | 在现有经验链上泛化物品定义／领取／池化，接入血瓶原美术与 200 HP 回复 | 共同掉落判定、上限、OwnerFinal 回血、竞争／重连幂等及重开 | 尚未实施 |

Phase 3 首轮只启用 XP 与血瓶；多人低血量加成取确认击杀的归属玩家。磁铁、终极道具、局外金币与其他道具只保留后续扩展边界。

## Phase 1 已查明与改动

来源 `FireParticles.prefab` 四个火焰主体渲染器为 Props/0，Glow 与 GlowFlat 为 BackgroundFront/100；当前恢复工具曾把六者都写为 EnemyAttack，使主体始终处于 Props 的人物、植物下方。

本次恢复来源的两个分层，并在生成工具中保留此规则。Nordic 的 Y 轴排序继续生效；没有把整圈提升到 Foreground，没有改变物理层、粒子模拟、材质平面投影、相机、命中或波次。

新增检查：真实资源逐层比对、模拟旧工具覆盖后修复两次、同一池对象再次借用、围栏 Replica 的独立火焰点。旧测试“全部高于 BackgroundFront”改为明确来源分层，保留粒子数量、寿命、碰撞和流程门槛检查。

交付验收说明随包提供 `FIRE-SORTING.md`。本轮没有运行 Computer Use，画面验收由用户执行。

## Phase 1 定向检查结果

- EditMode：LimboArtTests 与 LimboSpatialTests，23/23 通过。
- PlayMode：LimboTrapLifecycleTests 与 PlanarPresentationTests，6/6 通过；包括同对象池复用、67 个 Replica 火焰点、原有收缩／暂停／释放和平面投影回归。
- 启动脚本：带空格的独立目录、Full 辅助隔离、两个新观察入口及失败／未完整日志归档检查通过；此项模拟进程启动，不冒充实际试玩。
- 没有重跑 720.9 秒完整流程，也没有声称本轮完成真实双窗口或人工遮挡验收。

原始测试文件在 `Logs/FireSortingPhase1/`；来源／适配六层数据与哈希在 `docs/evidence/fire-sorting-phase1/layer-audit.json`。

## Phase 1 交付

版本：`FS-20260917-01`。构建采用现有 `kcp-development` 配置，Boot/MainMenu/Gameplay 三场景；包含 Host／Client，不含测试程序集。

- 构建：`Builds/LimboFireSorting-FS-20260917-01/MonsterSupergroupLimbo.exe`
- 独立人工包：`F:/Limbo Acceptance/LimboManual-FS-20260917-01`
- 单独观察：`6-Fire-B.cmd`、`7-Fire-Barrier.cmd`；完整无辅助流程：`1-Solo.cmd`
- 包内 `build-manifest.json` 固定源码差异、配置与文件哈希；`technical-verification.json` 区分自动化结果与待人工验收项。

Phase 1 的代码、资源与构建已交付，用户已确认遮挡问题解决。随后反馈的收缩火焰连续性不是本次音效改动范围。Phase 2 记录见 [音效修复报告](weapon-audio-phase2.md)；Phase 3 尚未实施。
