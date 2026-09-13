# Nordic 正式 Gameplay 接入验收报告

2026-09-13，Unity 6000.3.17f1，Windows 11。

## 交付结果

已在原 Gameplay 场景接入 Nordic 静态环境与原尺寸 Axeldor。保留原场景 GUID、Boot／准备房间入口、战斗参数、玩家网络组件顺序和 UI。通过原入口游玩；直接打开 Gameplay 不能替代网络初始化。

- [正式 Gameplay](F:/UnityStore/MonsterSupergroup/Assets/_Project/Scenes/Gameplay.unity)、[NetworkPlayer](F:/UnityStore/MonsterSupergroup/Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab)。
- [正式地图变体](F:/UnityStore/MonsterSupergroup/Assets/_Project/Content/Nordic/NordicGameplayMap.prefab)继承现有 NordicStaticMap，不重新生成布局；17,385 个 SpriteRenderer、105 个地图 Collider2D（含四边）。
- Ground 实测 119.3386078 × 67.1279678，中心 (0, 0.99)；FOV 80、Z=-10，16:9 可见高度约 16.782。
- [烘焙导航](F:/UnityStore/MonsterSupergroup/Assets/_Project/Content/Nordic/GameplayNavigation.bytes)：477 × 268＝127,836 节点，XY、间距 0.25、禁止切角。最大地面敌人阻挡半径 0.46，膨胀包含 0.01 余量；运行时读取缓存。
- 玩家脚底 0.1、受击 0.13；走路阻挡、冲刺合法落点、外边界、出生、重连及既有位置跳转共用地图检查。
- 原 Idle／Walk／Dash／Die、多部件受击、独立朝向节点和纯表现同步已接入。新同步组件追加在原 12 个网络组件之后。
- URP 2D、Linear、Nordic Y 轴渲染器；脚底排序、地面预警和战斗特效分层；正式相机仍只绑定本机玩家并保留原震屏事件。

[依赖与 SHA-256 清单](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/dependencies.json)记录目标资源；运行不访问 Nordic 源目录或 DLL。

## 验收记录

| 范围 | 结果 | 记录 |
|---|---|---|
| 构建及静态校验 | PASS：正式构建、依赖、动画绑定、唯一 Ground、网络组件顺序 | [证据](F:/UnityStore/MonsterSupergroup/Logs/ProjectTools/20260913-154123-794-build.player/result.json) |
| 正式集成 | PASS：193 项，包括角色大小、方向、实际冲刺、树根阻挡、绕树、死亡末帧、倒播恢复、重置、视野和四边四角 | [证据](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/acceptance.json) |
| 图形 Host＋客户端 | PASS：仅跟随本机、远端走路/朝向、独立震屏、重新连接；两端真实窗口渲染 | [证据](F:/UnityStore/MonsterSupergroup/Logs/GameplayCameraProcess/20260913-152139) |
| 四人准备与开局 | PASS：四个席位、各自 Build、倒地重连、加载中断线清理、单人重开 | [证据](F:/UnityStore/MonsterSupergroup/Logs/PreparationMenu/20260913-152216-party-p7998) |
| 专服冲刺 | PASS：专服＋两客户端，消耗、轨迹、命中、取消及重连状态；服务端无粒子表现 | [证据](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Dash-Dedicated-7955-20260913-153239-564) |
| 原六波次 | PASS：专服＋两客户端，原时间表、Prefab 身份、尾部循环、近战与投射物生命周期 | [证据](F:/UnityStore/MonsterSupergroup/Logs/TimelineWaves/process-20260913-152511-dedicated-normal) |
| 模拟交接 | PASS：Host＋两客户端，预警/攻击/恢复中交接、击退、超时接管、包乱序、存活与倒地重连；20/100 敌人各 30 秒压力测试 | [证据](F:/UnityStore/MonsterSupergroup/Logs/EnemyHandoff/20260913-152831-normal) |
| 升级与武器 | PASS：专服＋两客户端，18 级、每人 17 次升级、额外装备、两阶段重连、六种武器攻击及远端表现 | [证据](F:/UnityStore/MonsterSupergroup/Logs/M4/Dedicated-20260913-153844-205-p7972) |
| 结算与返回房间 | PASS：三人连续五轮，死亡结算、新 Avatar/RunId、Build/生命重置、存活成员退出 | [证据](F:/UnityStore/MonsterSupergroup/Logs/PreparationMenu/20260913-153518-run-end-p7998) |
| 编辑器相机与伤害数字 | PASS：原 6 项相机用例通过；伤害数字 2 项复测通过 | [证据](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/damage-numbers-results.xml) |
| 编辑器死亡与恢复 | PASS：原 Die 末帧、逆播、回到 Idle、生命数值保持 | [证据](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/life-results.xml) |

原相机测试结果见 [PlayMode XML](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/playmode-results.xml)：其中初次伤害数字测试暴露的 SortingGroup 空值问题已修复，并由单独的 2 项伤害数字复测覆盖。最终静态资源校验通过，无 Missing Script、丢失 Sprite 或无效新增材质。

覆盖 16:9、4:3、极端宽屏视口；60／120 fps 移动采样均未出现逆向渲染帧。实际敌人绕树保留 [轨迹 CSV](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/enemy-route.csv)。树根接触允许物理求解器约 0.013 单位的瞬时接触误差，未穿过树根。

以上多人实机使用同一电脑的 KCP 独立进程；未将它当作 Steam 跨机器、互联网延迟或异构设备验证。未新增复活玩法；死亡倒播测试仅验证接收“重新存活”状态后的表现，正式死亡／结算由既有三人换局与重连测试覆盖。

## 性能与同步流量

机器：i7-13700KF、RTX 4080 16 GB、约 64 GB RAM；D3D11 开发构建，保留现有调试面板。正式开局请求至本机玩家与地图可用约 **0.651 秒**，为本机缓存条件下的数据，不代表冷启动或网络连接耗时。

完整密度阶段按原波次运行至约 165 秒，敌人数量依次 6／12／18／24／30／30；第六波遵守 30 存活上限而跳过 6 次生成。测试临时开启无敌并禁止玩家武器执行，以保留敌人密度、避免升级菜单中断；敌人移动、攻击、投射物与场景渲染继续运行。未修改波次资产、敌人数值或装饰密度。采样后才清除测试敌人以隔离移动、碰撞和动画检查。

初始游戏显示设置实际为 **3840×2160**，由截图尺寸核实；后续移动测试显式切换为 **1920×1080**。表格为实际帧间隔，包含限帧与系统调度，不是分离的 CPU／GPU Profiler 耗时。内存列为 Unity 已分配内存。

| 阶段 | 存活敌人 | 目标 FPS | 平均 ms | P95 ms | 最大 ms | Unity MiB |
|---|---:|---:|---:|---:|---:|---:|
| 首波 4K | 6 | 120 | 8.64 | 8.75 | 16.63 | 221.6 |
| 完整密度 4K（约 45s） | 12 | 120 | 8.84 | 8.80 | 31.10 | 222.8 |
| 完整密度 4K（约 75s） | 18 | 120 | 9.47 | 10.41 | 47.50 | 224.0 |
| 完整密度 4K（约 105s） | 24 | 120 | 10.20 | 17.54 | 53.69 | 226.1 |
| 完整密度 4K（约 135s） | 30 | 120 | 11.07 | 51.29 | 67.76 | 228.0 |
| 完整密度 4K（约 165s） | 30 | 120 | 11.61 | 63.64 | 71.00 | 228.0 |
| 移动隔离 1080p | 0 | 60 | 16.91 | 19.66 | 21.69 | 228.7 |
| 移动隔离 1080p | 0 | 120 | 8.38 | 8.69 | 9.35 | 228.7 |

30 敌人最后 30 秒平均约 **86.1 FPS**，P95 **63.6 ms**，存在明显尖峰；**完整战斗在该 4K 开发构建中未达到稳定 120 fps**。本次记录现状，没有降低密度或波次来改善指标。进程采样观察到峰值工作集约 **827.9 MiB**；它与 Unity 已分配内存、显存口径不同。详见 [帧间隔与内存](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/acceptance.json)、[进程内存采样](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/process-memory.json)。

新增动画同步：状态载荷 14 字节。双端走路／朝向／重连验证中，客户端约 25.04 秒发送 19 条相关 Command，Mirror 消息合计 399 字节（约 15.9 B/s）；Host 约 32.03 秒为 4 条、84 字节。状态变化限频 20 次/秒。该统计不含传输层报头，也不拆分服务器下行中与既有组件合并的 SyncVar 包，不能视为全链路带宽。[客户端记录](F:/UnityStore/MonsterSupergroup/Logs/GameplayCameraProcess/20260913-152139/client-animation-traffic.json)。

## 画面证据

- [正式游玩／30 敌人上限](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/authored-full-density.png)
- [多人同屏](F:/UnityStore/MonsterSupergroup/Logs/GameplayCameraProcess/20260913-152139/client-multiplayer-walk.png)
- [中心视野](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/final-gameplay.png)
- [树根后方遮挡](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/occlusion-behind.png)
- [树根前方遮挡](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/occlusion-front.png)
- [边界](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/boundary.png)
- [实际敌人绕树](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/enemy-route.png)
- [原版死亡末帧](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Acceptance-Rendered/axeldor-dead.png)

截图已人工查看，中心、边界、树前、树后为不同实时帧。验收入口增加截图新鲜度及差异检查，避免把隐藏窗口导致的旧画面判为成功。`Acceptance-Complete` 中的早期隐藏窗口图形记录不作为最终视觉或性能依据，最终依据为 `Acceptance-Rendered`。

密集战斗中的洋红色扇形沿用 LustSinner 预警原始颜色 (1, 0, 0.61573935)，其材质与资产未修改。几何隔离截图的“本局已结束”是测试关闭波次刷怪器后的状态文案；正常波次运行以 30 敌人截图为准。死亡末帧图是表现隔离用例，因此调试面板仍可显示服务器的存活基线。

## 重建、保留项与恢复

[使用与工具说明](F:/UnityStore/MonsterSupergroup/docs/nordic-gameplay.md)列出 `migrate.nordic-gameplay`、`validate.nordic-gameplay`、`build.player -Profile nordic-gameplay` 和 `test.nordic-gameplay`。`FullSuite` 追加多人回归，`FullPerformance` 按原时间表采集完整密度。

基线哈希确认 **42／42** 保留项一致，包括原 Gameplay GUID、Boot／菜单、ProjectSettings、默认渲染器、原 Nordic 布局与独立样板。所有本次修改的 36 个既有文件都在迁移备份中；新增 28 个实施文件另列（另含本报告）。同时发生的 Steam 工作单独标记并保留。

- [改动清单与修改后 SHA-256](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/baseline-diff.json)
- [保留项核对](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/protected-assets.json)
- [迁移基线备份](F:/UnityStore/MonsterSupergroup/Logs/NordicGameplay/Before-20260913-144449/baseline.zip)

该备份覆盖本次改动范围及这些文件开始时的已有修改，不是完整 Unity 工程归档。恢复时仅按清单提取需要恢复的文件；不要覆盖后续其他工作。
