# Timeline 敌人波次

参考 HellMaiden 的 ProgressionTimeline 将片段转换为生成任务的设计。新增网络专用 Track/Clip 仅保存编排数据；开始新局时编译为不可变事件表，由现有服务器时钟执行。播放或拖动 Timeline 游标不会生成敌人，客户端只接收 Mirror 生成与波次进度。

## 默认编排

配置：`Assets/_Project/Content/NetworkCombat/GameplayWaveRules.asset`。Timeline：同目录的 `GameplayEnemyWaves.playable`。

| 波次 | 内容 | 本波生成时刻（秒） |
|---|---|---|
| 1 | EnemyBase ×6 | 0、2、4、6、8、10 |
| 2 | EnemyBase ×3，LustSinner ×3 | Base 0、4、8；LustSinner 2、6、10 |
| 3 | Skeleton ×3，LustSinner ×3 | Skeleton 0、4、8；LustSinner 2、6、10 |
| 4 | Imp ×3，LustSinner ×3 | Imp 0、4、8；LustSinner 2、6、10 |
| 5 及以后 | LustSinner ×6 | 0、2、4、6、8、10 |

Timeline 固定长度 150 秒，每波 30 秒；最后 30 秒永久重复，波数继续递增。各数量是计划名额：存活上限仍为 30，位置无效、满员或慢帧错过的机会不补刷。切波保留旧怪物，故第 5 波以后指新增种类，不会清除前几波的存活怪物。四种敌人的数值和 Variant 关系不改。

## 编辑步骤

1. Unity 菜单 `Monster Supergroup > Network Combat > Waves > Open Timeline`，或在 GameplayWaveRules Inspector 点击 Open Timeline。
2. 在 NetworkEnemySpawnTrack 上添加 NetworkEnemySpawnClip，绑定已经注册的联网敌人 Prefab，并设置 count。片段起点是首只生成时间，间隔为片段长度/count，终点不会额外生成一只。
3. 拖动片段调整时间，调整片段长度改变间隔。轨道和父分组静音会排除所有对应事件。片段无混合、clip-in 和时间缩放；本轮支持均匀生成，不接曲线、剧情和 Boss 轨道。
4. Timeline 使用 Fixed Length，总时长必须是 Wave Duration 的正整数倍。最后一个完整波次窗口的事件按原顺序重复；跨波片段按每条事件的实际时间归属波次。
5. 使用 Validate and Export Preview 核对配置、Boot 注册和前六波编译结果；输出 `Logs/TimelineWaves/compiled-six-waves.csv`。缺失引用和非法配置会阻止开局。
6. Create Default Timeline 只在资产缺失时建立默认内容，已有有效编排不重写。运行期间的资产修改留到下一局生效。

## 运行实现

- 原统一 enemiesPerWave/spawnInterval 已由片段取代。GameplayWaveRules 保留波长、容量和出生位置规则；旧 PerMember 接口保留供明确选择它的夹具使用。
- 同一时刻的事件保持轨道、片段、片段内部顺序，分别生成、计数及扣除容量。慢帧只保留最近到期的一组，早先组记为跳过；不会在下一波的空白起始区补出上一波敌人。
- NetworkGameplayEnemySpawner 根据事件选取 Prefab，按该 Prefab 的身体圆形碰撞器半径、缩放及偏移检查 Ground 边界。
- 不新增网络消息；WaveProgressSnapshot 的 Planned 改为当前波实际事件数。生成事件以本局的递增序号区分，循环不复用旧序号。重连不回放已消费事件，新局重新编译并从第 1 波开始。

## 验收记录

实施期间保留基线和日志于 `Logs/TimelineWaves`。

- EditMode：`editmode.xml`，15/15 通过。包含原服务器调度回归与新增 Timeline 时间、类型、序号、静音、重复段、慢帧、冻结和非法配置检查。
- 资产工具：`repeat.txt` 通过；连续执行两次不改变已经制作的 Timeline、规则和敌人资产。`prefab-baseline-check.json` 确认 Base、Skeleton、SkeletonExample、LustSinner、Imp 及各自 .meta 与迁移前逐字节一致。
- 编译预览：`compiled-six-waves.csv` 包含前六波 36 个事件，最后六个事件为第六波的 LustSinner，时间 150、152、154、156、158、160 秒。
- PlayMode：`playmode.xml`，28/28 通过。含真实四种 Prefab 六波生成、各自身体边界、切波保留旧怪、释放容量后的重复段、同刻上限、未注册引用拒绝、配置冻结、新局重置、原 PerMember 与既有 Variant 行为回归。
- 构建：`build-attempt-1.log`，成功，退出码 0；产物 `Builds/TimelineWaves/TimelineWaves.exe`。可执行文件与网络程序集哈希保存在 `build-hashes.json`。
- 独立六波进程：以下三组全部通过，各组均实际生成 36 只，36 个序号/netId 唯一，所有客户端身份一致；第六波为六只 LustSinner。骷髅和 LustSinner 均进入近战生效阶段，Imp 有确认发射与确认终止；Dedicated 不创建本地弹体。七个进程全部退出 0，异常日志为 0，汇总见 `process-results.json`。
- 独立受击回归：`Logs/M3Knockback/Host-20260912-200919-199-p7993`，Imp Host/Client 均 PASS，退出码均为 0。覆盖双方命中、同时命中、击退、模拟接管、三种 DOT、迟到/重复/到期确认、来源断线后的服务器 DOT、闪白与伤害数字去重；使用保留的 PerMember 验证入口。
- 环境启动记录：首次迁入、首次 PlayMode 和数次构建启动在执行入口前以退出码 1 结束；PlayMode/构建同期确认有其他 Unity 任务占用同一项目，一次启动还报告了项目读写占用。等待占用结束后重试成功。原始启动日志保留，没有将这些启动记为通过。

| 独立进程配置 | 日志目录（`Logs/TimelineWaves/` 下） | 实际结果 |
|---|---|---|
| 普通 Host + Client | `process-20260912-200919-host-normal` | 六波、36 次生成，PASS |
| 延迟 Host + Client | `process-20260912-200919-host-impaired` | 六波、36 次生成，PASS |
| Dedicated + 双客户端，延迟网络 | `process-20260912-200919-dedicated-impaired` | 六波、36 次生成，PASS |

延迟配置为 150 ms 基础延迟、最多 20 ms 抖动、10% 不可靠消息丢包。每组保留服务器生成清单 `manifest.json`、逐端日志、参数和退出码。六波进程使用正式 Boot → Gameplay，但夹具显式跳过准备房间并禁用自动玩家攻击、保护玩家生命，以保证编排测试期间容量只按夹具控制变化。战斗反馈另由上述 M3 进程和 PlayMode 检查。

这些进程采用无图形模式，本轮没有人工画面验收；未单独运行波次中途新客户端加入的独立场景。重连/交接在 M3 中复验，新局重置在 PlayMode 中复验；波次事件不新增客户端执行路径。

## 修改范围与复验

- 新资产：`GameplayEnemyWaves.playable`；原 `GameplayWaveRules.asset` 增加对它的引用。Gameplay 场景已引用同一规则资产，无需改动场景或敌人 Prefab。
- 配置：新增 `NetworkEnemySpawnTrack`、`NetworkEnemySpawnClip`、`NetworkWaveTimelineCompiler` 和 `WaveSpawnProgram`；修改 `GameplayWaveRules`、`ServerWaveSchedule`、`NetworkGameplayEnemySpawner`。
- 制作工具：新增 `NetworkWaveTimelineEditorUtility` 及规则 Inspector；相关程序集添加现有 `Unity.Timeline` 依赖。
- 验证入口：EditMode 的 `WaveScheduleTests|TimelineWaveTests`，PlayMode 的 `GameplayWavePlayModeTests|EnemyPrefabVariantPlayModeTests`。
- 独立进程构建入口：`MonsterSupergroup.Gameplay.Tests.TimelineWaveValidationBuild.Build`。构建完成后运行 `Tools/Run-TimelineWaveValidation.ps1`，可附加 `-Impaired` 或 `-Dedicated -Impaired`；不同并发运行使用不同 `-Port`。
- 独立进程夹具使用正式 30 秒波长与上限 30；第五波第六只生成后销毁最初六只以释放容量，确保第六波实际生成而非被容量上限遮蔽。夹具记录并核对每端所有 36 个 netId/assetId、波次、时刻、序号、近战生效阶段和 Imp 发射/终止。
