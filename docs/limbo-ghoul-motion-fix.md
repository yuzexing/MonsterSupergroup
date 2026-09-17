# 出生动画与 Ghoul 预警前进修复

2026-09-16。范围为公共身体动画控制与 Ghoul 来源预警步进。画面验收交由人工完成，不把本次后台测试或隔离辅助解释为压力测试通过。

## 根因与修改

1. **空攻击状态覆盖移动动画。** `NetworkEnemyMeleeReplica.ApplyAction` 原先用 `hasAction` 判断状态是否变化；出生时 `ActionId=0` 每帧都被当成变化，空朝向触发右侧移动动画，与正常向左的移动动画反复互相重置。真实 Ghoul Prefab 的修复前测试在朝向断言失败。现在空状态不接管身体，结束/取消只释放一次，普通移动继续使用有效方向；恢复移动会通过原有 Transition 恢复播放速度。保留死亡、击退与硬直展示保护。
2. **遗漏来源 `EnemyWarningStep`。** 恢复快照对象 `o00455` 引用 Ghoul 的 `o00134` 攻击与 `o00140` 刚体，参数 `stepDistance=0.4`。此前三连击接入没有挂接此组件，共同时间轴也不会执行其旧 Warning 回调。现在把参数纳入来源/适配清单，通过定向工具配置参考 Prefab，并从 `SequenceEnemyAttack` 的模拟运动接口驱动；共享时间轴不重复订阅旧回调。

Ghoul 的正确表述是：**三连击停止普通导航，每击 Warning 独立前进，Active/Recovery 停止。** 每击开始重新锁向，该击不持续拐弯。第三击特殊动画绑定、三击偏移、50 伤害与原六边形均保留。

## 来源位移与交接

预警每渲染帧请求 `当前位置 + 锁定方向 × 0.4 × Clamp01(本击预警已过时间/预警时长)`；正常离开实际执行过的 Warning，再请求一次 `当前位置 + 锁定方向 × 0.4`，随后冻结刚体。仍用 `Rigidbody2D.MovePosition`，接受物理步、约束和碰撞的处理。**0.4 不是每击总距离。** 未将来源规则改为匀速或固定总距离冲刺。

检查点保存三击的开始/结束处理位、采样战斗时间、锁向和尚待物理提交的目标位置。移交前捕获，旧模拟端释放，新模拟端只恢复未提交请求和仍有效的当前阶段；过期 Warning 不补历史步进或末次前进。重复 epoch 仍由既有交接入口拒绝。只有当前模拟端执行运动，本地攻击入口只控制显示与本地玩家命中。

暂停时不累计请求；同渲染帧/相同战斗时间不重复步进。取消、禁用、死亡和重定位经现有释放链清理请求。检查点使用原 Mirror 序列化通道，新增 28 个未压缩字节；Host/Client 必须使用同一新构建。

原始采集 `raw-evidence.zip/observe-final/events.jsonl` 的 Ghoul `-61808`：第一击预警从 `(1.6055,4.0155)` 前进到约 `(0.0023,2.5661)`，Active 期间位置保持；后两击预警继续前进。这是来源预警运动的历史运行证据，不是本次修复包的画面验收。

## 配置与手动观察

- 原始证据 `docs/evidence/hellmaiden-attacks/runtime-assets.json` 不变；SHA256 `4dcefbcae22fb6e8cd067618cd0ba782eb5a23d07b20a31a3ecf3323cd6d5c38`。
- `GhoulSource.json` 与 `GhoulAdapted.json` 增加原始组件引用与参数；导出重复执行不覆盖已有适配值。`LimboGhoulAssets.UpdateWarningStep` 只补缺失组件/观察资产。
- `ghoul-motion`：65 秒，1 只 Ghoul 的存活目标配置，手动移动，关闭武器，生命低于 300 时补至上限；不定位、不自动移动、不截图。属于观察辅助，不是正常压力局。
- 包内 `5-Ghoul-Motion.cmd` 启动上述单人 Host 配置；日志在 `TechnicalRuns/<时间>/<角色>`。`Start-Technical.ps1 -Profile ghoul-motion -Role host -WaitFor 2` 和 `-Role client` 可供同版本双端人工观察。
- `1-Solo.cmd`、`2-Host.cmd`、`3-Join-Local-Host.cmd` 继续启动无辅助 Full，敌人数值、31 片段、玩家基线和镜头不变。
- 工程入口：`Tools/Run-LimboReference.ps1 -Role host -Profile ghoul-motion -LogDetail light -BuildDirectory <修复构建目录> -RunName <新记录名称>`。

人工观察建议：让敌人从你的右侧接近，检查朝左播放而非右侧滑行；靠近后侧移，检查每击预警前进、该击锁向，出手时停止。攻击结束后应恢复移动动画。新包构建与实际测试结果见同目录证据报告。

## 验证边界

本次只运行新增复现、相关协议/资源测试与一次受影响的集成回归；没有操作游戏窗口，没有重跑多轮 720.9 秒流程。批处理测试为了检查真实 Sprite 动画，显式将测试对象设为 AlwaysAnimate；生产资源仍保留原屏外动画裁剪。

受控测试分别按 30/60/144 个渲染采样、50 Hz 物理步检查请求公式、三击位移、暂停、取消、障碍和迟到恢复。来源规则使不同帧率及慢动作的累计位移可能不同，这一限制保留给后续人工评估。结果不意味着已完成所有帧率/双窗口画面、压力或原游戏同条件对照。

## 本次自动化记录

记录位于 [证据目录](evidence/limbo-ghoul-motion/results.json)，保留首次失败与修正测试夹具的中间记录。

| 记录 | 结果 |
|---|---|
| 修复前真实 Ghoul 出生朝向复现 | 1/1 失败，错误地播放右侧动画 |
| 修复后出生动画、Sprite 实帧与攻击池相关检查 | 通过；包含在最终 PlayMode 集成集内 |
| 受影响 EditMode 集成集 | 105/105 |
| 受影响 PlayMode 集成集 | 74/74 |
| 0.25 慢动作的独立补测 | 1/1 |
| 最终 Ghoul 配置/协议检查，含注册 ID | 14/14；完整检查点 280 字节 |

物理测试采用受控渲染采样和实际 Physics2D 50 Hz 步长。三击测试先向右、再向上、再向右：30/60/144 FPS 对应总位移约 `(2.91,2.38)`、`(4.84,3.64)`、`(4.99,3.91)`；60 FPS、0.25 时速约 `(4.95,3.90)`。这是指定采样/物理顺序下的测试输出，不是正常对局中的保证距离。

测试夹具曾缺少 Combatant 生命初始化、无画面时的动画求值，以及手动物理步与 Unity fixed-time 的对齐；这些中间失败按具体原因归档，未通过删除业务断言或调整来源参数放行。

## 修复构建

版本 `GM-20260916-01`，Windows KCP development，未包含测试程序集。输出 `Builds/LimboGhoulMotion-20260916-01/MonsterSupergroupLimbo.exe`，独立交付目录 `F:/Limbo Acceptance/LimboManual-GM-20260916-01`。旧包保留。构建使用当前地图/Gameplay 场景，不回退用户改动。

构建在独立工程副本完成。首次被现有 AppID 校验拦下：副本根文件为默认 480；同步主工程原有 `steam_appid.txt`（4886160）后成功，没有修改 Steam 运行代码或绕过校验。`Logs/GhoulMotionFix/build.log` 与 `build-final.log` 位于独立验证工程，分别保留失败和成功结果。

包内 `technical-verification.json` 记录本次自动化范围、构建摘要与待人工画面验收状态；不能引用旧包的全流程画面通过结论作为本包的新验收。
