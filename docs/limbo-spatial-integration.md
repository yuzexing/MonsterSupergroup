# Limbo 空间机制接入与验证

初次按“先做后台实现，稍后试玩”执行，随后根据用户指示进行了双端有画面验证。新增机制完整门槛保持 ValidationPending；Full 继续禁用。以下后台记录保留其原始时间与构建口径；最新画面结果与人工分工见文末及 `limbo-manual-validation.md`。

## 当前实现

- `ServerWaveSchedule.Reference` 保留 B 捕获后第二次相对等待，并独立发布陷阱释放事件。生成结束只停止预警发射；占用直到实际启动时间加来源片段长度才释放。
- 现有 `NetworkEnemySimulationWorld` 新增陷阱状态同步：轮次、目标、中心、阶段、碰撞、半径、粒子批数、占用及时间。B 中心在第一次等待结束时固定，所有出生点与预警共用椭圆公式。B 不计来源全局敌人数。
- 围栏在服务端运行现有 `BarrierTrap` 协程与 FixedUpdate，客户端只重建同步状态。服务端保管概率、陷阱槽、入场慢动作和结束回收。40 边、20→10、30 秒收缩，保留边缘半径 30 的外侧胶囊碰撞及 45° 变换，没有改成细线圆圈。
- 原生逐粒子入场仍逐次 yield；收缩在入场期间已经进行。停止后立即关碰撞，等实际粒子结束才释放槽；没有用固定两秒代替粒子存活检查。
- 临时保护增加到现有 CombatLedger 并通过既有实体状态广播，不增加另一套伤害结算。退出只清除围栏自己的保护。PauseManager 的即时慢动作请求释放会保留现有暂停，避免取消陷阱时误恢复游戏。
- 轮次结束、停止客户端、停止服务器和世界销毁均回收预警与碰撞、释放本次请求及摄像机临时取景。客户端不能自行释放服务端陷阱槽或推进围栏。

## 新确认的来源规则

围栏片段的 `end-start-shrinkDuration` 用于初始概率，首次失败增加同等概率。源 `Init` 尝试后，同帧 `Progress` 可能再次尝试；后续每个至少一秒的进度更新最多一次，不追赶卡帧期间的所有次数。占用期间不抽签、不加失败概率。

**片段配置结束时间不是强制停止时间。** 来源 `BarrierTrapSpawner` 在原生陷阱回调后才设置 hasEnded；若此前被其他陷阱占用，可能在配置结束之后出现。实际释放还包含入场和粒子退出。证据：`F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/Scripts/Assembly-CSharp/AstralShift/HellMaiden/Combat/Spawners/BarrierTrapSpawner.cs`、同目录 `TrapSpawner.cs`，以及 `Combat/ProgressionTimeline.cs`／`Milestone.cs`。

`FireParticles.prefab` 实际有六个启用粒子系统，生命周期分别为随机 1–2、1–2、1–2、.2–.3、固定 .3、固定 1 秒；不能只读取第一个组件。数值提取及来源哈希保存在 `Assets/_Project/Content/NetworkCombat/Limbo/Spatial/SpatialSource.json`。入场 .25 倍速度另由来源 `Scenes/Game Scenes/Systems.unity` 的 `SlowMoTimeScale: 0.25` 证实。

## 原始值与适配差异

`Tools/Export-LimboSpatial.py` 只读取来源数值；原始包、旧导出工程和攻击恢复快照不改动。`SpatialAdapted.json` 单独保存适配值。已有适配文件及独立 Prefab 不被 Create 覆盖。

| 项目 | 来源及当前适配 |
|---|---|
| 粒子生命与发射率 | 六组分别记录，保留数值；使用现有预警材质和简化形状，没有迁入来源图像、材质或音频 |
| 粒子剔除 | 来源序列化模式 2；当前统一 AlwaysSimulate，使服务端释放不依赖是否存在或朝向渲染摄像机；实际退出时间仍由粒子存活决定 |
| 粒子图形 | 颜色、形状、运动展示为适配，来源其他视觉模块未搬入；不能宣称火焰画面一致 |
| 渲染深度 | 当前地图为 z=0，来源 trapTransform 写入 z=100 |
| 入场镜头 | 使用当前 Nordic 透视镜头，临时取景中心及左右 30 世界单位范围；恢复后回到原基线。来源摄像机插件的插值与构图没有逐帧还原 |
| 多人入场 | 服务端选择的目标收到临时保护；两端战斗时钟同步减速，各自镜头展示同一捕获中心。仅验证正确性，不增加多人倍率 |

## 独立入口与门槛

Unity 后台工具：`MonsterSupergroup.NetworkCombat.Editor.LimboSpatialAssets.CreateAndBuild`。只定向创建参考资源、绑定空间 Prefab 和推进机制门槛至 ValidationPending，保留已有适配配置。旧字段缺失的围栏必须显式设置 lifecycleVersion，不能因枚举默认值 0 而自动成为 Ready。

后续获得前台试玩时间后使用：

```powershell
./Tools/Run-LimboReference.ps1 -Profile spatial-b -RunName spatial-b-first
./Tools/Run-LimboReference.ps1 -Profile spatial-barrier -RunName spatial-barrier-first
./Tools/Run-LimboReference.ps1 -Profile spatial-overlap -RunName spatial-overlap-first
```

这些是显式隔离配置：B 片段被平移到 1 秒；围栏独立配置被平移到 1 秒，重叠配置为 17 秒；不属于主线正常连续运行。原主 Timeline 的 31 个时间、预算与变体没有压缩或改写。Full 中所有 B 的生成机制门槛独立于敌人攻击门槛。

## 验证与待办

后台检查记录在 `Logs/LimboStage2/spatial-*`。本文件只在实际工具结果确认后记录通过数；单元／PlayMode 结果不代表画面验收。

2026-09-14 后台结果：EditMode **30/30**、PlayMode **17/17** 通过；包含既有 Imp／近战／交接／协程回归，以及新增的围栏原生完整收缩与退出检查。首次失败分别为测试漏填 ownerPlayerId，以及暂停采样发生在当前帧已到期的 WaitForSeconds 让出之前；失败日志保留，最终测试在该帧结束后采样，未通过插入额外生成等待改变来源节奏。

独立静态核对确认主 Timeline **31 个片段**的开始、长度、身份、变体、数量、模式、间隔和 C 曲线关键帧仍匹配原始提取。来源 Timeline／EnemyDB／Limbo 场景、空间证据文件及恢复攻击快照哈希未变。重复 Create 后，空间来源与适配 JSON、两份空间 Prefab、Imp／阶段二适配 JSON 均未被覆盖。

构建成功，当前 `Builds/LimboReference` 与 `Builds/LimboReferenceClient` 的 **489 个文件**逐项哈希一致。NetworkCombat DLL SHA-256 为 `d8e2d9480aaaec3e00e37f0b35979ecbeafef5aacade72e23d0b0ec4e61dbbd5`。此前实际单人运行使用的完整旧构建保存在 `Builds/LimboStage2Observed-20260914`；不能将旧画面记录当成新空间构建的画面验收。

验证结果、测试 XML、构建日志及逐文件哈希见 [后台证据](evidence/limbo-spatial-20260914/verification.json)。没有启动新构建试玩，没有解除 Full 或空间机制的画面验证门槛。

下一次空间试玩会被动生成 `spatial-observation.jsonl`：两端阶段与时间、占用、世界碰撞顶点、移除和结束清理，并在阶段变化时保存截图。此观测组件不修改生命、位置、随机数或时钟。

待做：B 两次等待、固定中心、预警可见性、占用释放的双端画面验证；围栏实际玩家碰撞、收缩、慢动作、镜头恢复、取消、重复开局及双端一致性；C/L/B 完整边界与屏外专项矩阵。粒子材质和镜头效果尤其需要真实窗口核对。Dash／LostSoul／Ghoul、720.9 秒转场与原游戏正常流程压力对照仍未接入或完成。

## 后续画面验证与分工更新

上述待办为后台构建时的状态。最新构建 DLL 为 `f08aace608fabff70e7aecae60d29c1d736c55ac589fbda912b983586d8dda7f`；修复预警排序、首局 Client 展示清理标记及快照序列化，补充阶段发射后的截图和实际碰撞采样。

- `Logs/LimboReference/spatial-b-pair-c`：两轮均观察到 Host／Client B 预警，每轮只生成 5 只，出生不增加来源全局计数；通过界面重开，结束占用／状态／存活预警均为 0。
- `Logs/LimboReference/spatial-barrier-pair-a`：两轮均到 85 秒隔离终点，并完成双端界面重开。半径样本 20→9.997864（FixedUpdate 跨阈值），时间倍率包括 .25 与 1；第一轮 Host／Client 实际接触样本为 109／108，第二轮 110／105。停止阶段关闭碰撞，粒子退出后释放占用；两端结束状态、碰撞和存活预警均为 0。
- 当前材料形成较厚的橙色预警环，是适配展示，不能宣称来源火焰外观相同。旧 `spatial-b-pair-a|b` 保留为发现排序／客户端展示问题的失败记录。

B／围栏基础画面闭环已有证据；互斥、取消、完整暂停与 C/L/B／屏外边界矩阵仍待验证。最新数值日志尚需统一归档及进一步交叉分析，不能将基础闭环等同完整空间验收。用户接管正常压力试玩，AI 停止前台操作，继续负责技术核验；见 [人工验证说明](limbo-manual-validation.md)。
