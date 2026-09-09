# M2：正式 Gameplay 的 ProCamera2D 多人适配

状态：**实现完成；相机行为重测通过，实际截图可用；可见 Player 退出崩溃未解决，整轮验收尚未通过**。本次只实施 M2，M3–M6 的范围与未批准玩法建议不变。静态画面和自动检查不能代替连续观感及人工输入验收。

## 基线和本轮明确的决定

- 代码基线为 `42a36ce`（`enmey UI`）。开始时已有四个 `AD`：`Assets/_Project/Gameplay/{GameDirector.cs.meta,Player.meta,Pooling.meta,QTI.meta}`，保持原状，排除在 M2 提交之外。
- 用户在实施期间把正式 Gameplay 的 Ground 改成 100×100，并明确指定以它为相机边界。保留该修改；以 `SpriteRenderer.bounds` 为依据，包含 Sprite、pivot、父级位置和缩放，不能只读 localScale。
- 这份用户场景改动先单独记录为 `ab74b48`（包含 Unity 保存时的既有组件块重排），M2 相机移植在其后独立提交；这样回退 M2 不会撤销 Ground 或该重排。
- 用户随后明确批准增加“仅本地玩家实际受伤 → PlayerHit”表现。它是本轮新增的本地事件绑定，**不是**旧关卡既有受击震屏的复刻；旧 `CameraEffects.Health()` 只触发屏幕特效。
- 正式相机保留原有正交尺寸 **5**、位置深度 **-19.13**、投影、剔除、URP、后处理、AudioListener。参考相机正交尺寸为 **12**，没有复制其视野尺寸和渲染设置。

## 源场景和配置映射

用户给出的项目内路径 `Assets/Scenes/Game Scenes/Circle 1 - Limbo/Level_Limbo.unity` **在当前项目不存在**。本轮实际读取的是此前参考代码所在导出项目中的同名场景：

`F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/Scenes/Game Scenes/Circle 1 - Limbo/Level_Limbo.unity`

以下结论来自这份可读取的 YAML、它引用的资产和当前已安装插件源码，不根据类名或默认参数推断。源场景相机层级是 `Loaders / Camera`；GameObject 23、Transform 82、Camera 90。挂载 ProCamera2D 107、Shake 134、Numeric Boundaries 125、Zoom To Fit Targets 143、URP 相机数据 151、FMOD listener 153，均启用。

| 项目 | 参考实际配置 | 正式 Gameplay 处理 |
| --- | --- | --- |
| 核心插件 | 源资源的脚本引用来自 `26b351c820759d7982e88d93348ad777` DLL | 直接复用 `Assets/ProCamera2D/Runtime` 已安装源码；序列化脚本引用重绑到对应 MonoScript，未改插件实现。 |
| 主相机 | 正交 12；位置 (0.2,2,-10)；Depth 0；剔除 655351；URP、FMOD listener | 在既有 `Main Camera` 上加组件。保留正交 5、深度 -19.13、Depth -1、剔除 4294967295 和原 URP 数据；不导入 FMOD listener，不新增 Camera/AudioListener。 |
| 跟随 | XY；LateUpdate；水平/垂直开启；smoothness 各 0.15；offset 0/0；relative offset 开启；CenterTargetOnStart 关闭；IgnoreTimeScale 关闭 | 复制上述插件参数；`GameCamera` 和各扩展 `_pc2D` 明确引用正式相机组件。绑定 Owner 时调用插件 Reset。 |
| 目标来源 | 场景 `CameraTargets=[]`；旧 `PlayerLoader.LoadAsync` 经 `GameDirector.Instance.Player` 清空、添加并居中；旧 BarrierTrap 还能临时加入两个边缘目标 | 只接既有 `NetworkPlayerBootstrap → LocalPlayerInputBinding`；目标仅本地 Owner。未移植 PlayerLoader、Trap 目标、Leveler、PlayerHand、PauseManager 或旧 SceneMaster 初始化。 |
| Shake | 有序列表 SmallExplosion / GunShot / LargeExplosion / PlayerHit；constant 列表 EarthquakeHard；无自动 constant preset | 迁移五份预设到 `Assets/_Project/Content/GameplayCamera`；保留值和顺序，重绑脚本，不修改源资产。默认不启动常驻震屏。 |
| Numeric Boundaries | 组件启用，但 `UseNumericBoundaries=0`，四边开关均关闭；top/right=180、bottom/left=0；soft 开启，Softness=0.5、SoftAreaSize=0.1；无进入延迟 | 四边启用。Ground 当前世界范围 **左 -50、右 50、下 -49.01、上 50.99**。场景值与该范围一致，Awake 按明确 Ground 引用重新读取。保留 soft 参数；没有复制 Limbo 的坐标。 |
| Zoom To Fit Targets | border in/out 各 1；smoothness in=2、out=1；MaxZoomInAmount=1、MaxZoomOutAmount=12；DisableWhenOneTarget=1；CompensateForCameraPosition=0 | 复制这些参数。插件以初始半高为基准，倍数对应正式理论范围 5～60；当前仅一个目标时回到基础半高 5，不把远端玩家或怪物加入集合。 |

Shake 预设的关键值（其余角度、平滑、随机、旋转和 time scale 参数也按原资产保留）：

| 索引/类型 | 预设 | Strength / Intensity | Duration / Vibrato |
| --- | --- | --- | --- |
| 0 | SmallExplosion | (1,1,0) | 0.253 / 10 |
| 1 | GunShot | (0.5,0,0) | 0.04 / 1 |
| 2 | LargeExplosion | (3,3,0) | 0.777 / 19 |
| 3 | PlayerHit | (0.5,0.5,0) | 0.04 / 1 |
| constant 0 | EarthquakeHard | Intensity 38.25；一层频率 0.001～0.001、振幅 H/V/D=0.3/0.3/0.2 | 源场景未自动启动；本轮不发明常驻地震玩法入口。 |

## 权限、生命周期与触发链

```text
BootGameplayNetworkManager → additive Gameplay → 既有 Main Camera
NetworkPlayerBootstrap.OnStartAuthority
  → LocalPlayerInputBinding.Bind / Refresh
  → GameplayCameraRig.BindOwner
  → ProCamera2D 的单个目标 + PlayerMovement 的输入坐标转换相机

PlayerDamageInteraction → PlayerHitbox 的本地 Owner → PlayerMovement.Damage / Hurt
  → PlayerCombatantBinding.ApplyDamage → CombatantBehaviour.DamageReceived（实际伤害 > 0）
  → 当前 Owner 的 GameplayCameraRig → PlayerHit

既有 Ultimate 授权/释放 → Owner DanteUltimateAttack
  → 原动画 DanteUltimate_BaseAnim：0.21666667s、2.1166666s 的 ShakeCamera(2)
  → TimelineEffects 的既有本地回调
  → NetworkPlayerBootstrap.PlayLocalCameraShake → Owner 输入绑定 → LargeExplosion

OnStopAuthority / OnStopClient / OnDestroy → LocalPlayerInputBinding.Dispose
  → 解除伤害订阅、清空插件目标、停止震屏、重置偏移、释放输入相机
相机禁用 / Gameplay 卸载 → GameplayCameraRig.OnDisable 同样清理
```

- 相机先出现、Owner 先出现、相机重新启用都由既有 Owner 的 Refresh 完成绑定，不新增玩家发现循环。
- 目标销毁时用插件 `RemoveAllCameraTargets`，避免 `RemoveCameraTarget` 对已销毁 Transform 调用 GetInstanceID。重复 Refresh 不重复订阅或添加目标；旧 Owner 的迟到释放不能清掉新 Owner。
- `DamageReceived` 只来自本地实际扣血；恢复血量、无敌拦截和 `ApplyCanonicalHealth` 回写不会触发它。订阅只存在于绑定 Owner 上，远端普通受伤不会触发本端相机。状态造成的实际伤害也属于该本地受伤入口。
- Host 只使用 Owner 动画表现回调。已有远端 Ultimate replica 继续 `BindCamera(null)`，不会走旧 CameraEffects 单例；本轮没有新增 RPC、Command、SyncVar、相机网络协议或修改战斗事件身份。
- 通过局部 `shakeEnabled` 控制迁移后的震屏；不引入旧 `GameDirector.Instance.Settings` 依赖。旧关卡的全屏特效、设置菜单、FMOD listener 不在本轮移植范围。
- Shake 插件创建的父容器仍在 Gameplay 层级内，随场景卸载。它叠加的是本地镜头偏移；边界限制的是插件基础视野，震屏时可短暂在边缘外显露背景，结束后恢复。未通过移动玩家/怪物补偿相机。
- **已有缺口**：Ultimate 的充能发放目前只有测试调用 `ServerGrantCharge()`，尚无普通玩法充能入口。本轮保留它，不新增充能规则。普通游玩的可验收震屏入口是用户已批准的本地实际受伤。

## 改动清单

| 文件 | 改动 |
| --- | --- |
| `Assets/_Project/Scenes/Gameplay.unity` | 在原 Main Camera 添加四个插件组件和一个本地适配；重绑相机、Ground、预设；包含用户已确认的 Ground 100×100。 |
| `Assets/_Project/Gameplay/Combat/CameraFX/GameplayCameraRig.cs`、`.meta` | Owner 目标和伤害订阅、Ground 边界、局部震屏与释放；无网络状态。 |
| `Assets/_Project/Gameplay/Combat/Player/LocalPlayerInputBinding.cs` | 通过原 Owner 生命周期绑定/释放正式 rig，保留原输入控制器流程和鼠标坐标相机。 |
| `Assets/_Project/NetworkCombat/Mirror/NetworkPlayerBootstrap.cs` | 只有带 Owner 检查的本地震屏转发入口。 |
| `Assets/_Project/NetworkCombat/Mirror/NetworkPlayerUltimate.cs` | 为既有 Native Ultimate 相机回调传入该入口；不改释放判定、充能、命中或同步。 |
| `Assets/_Project/Content/GameplayCamera/` 及 metas | 五个源预设；只重绑脚本引用。 |
| `Assets/_Project/Tests/PlayMode/Gameplay/GameplayCameraTests.cs` 及 `.meta` | 正式 Boot Host、四角、单目标缩放、输入方向、释放/重绑、受击去重、原 Ultimate 事件、Host 重开。 |
| 同目录 `GameplayCameraProcessProbe.cs`、`GameplayCameraValidationBuild.cs` 及 metas | opt-in 双进程夹具和正式 Boot/Gameplay 测试构建。 |
| 同目录测试 asmdef | 增加已安装 ProCamera2D 的测试引用。 |
| `Tools/Run-GameplayCameraProcessValidation.ps1` | 启动 Host/独立 Client、收集日志和可选实际截图；夹具文件标记只用于协调测试，不是游戏协议。 |
| 本文、`docs/plans/boot-gameplay-network-combat.md` | 配置映射、结果、人工步骤与回退记录。 |

## 自动验证与实际画面记录

使用 Unity 6000.3.17f1、现有 `MonsterSupergroup_clone_0` 验证。该 clone 的 Assets/ProjectSettings 是本项目 junction，编译和加载本次正式资源。脚本不创建替代 Gameplay 的 Demo。

最终定向 PlayMode **22/22 通过**（`Logs/M2Camera/targeted.xml`、`targeted.log`，40.85 秒）：相机 5 项，以及原 Ultimate、协议/Owner 回调、Boot 场景生命周期、正式 Gameplay UI 加载回归。早期 4/4 记录在 `playmode-2.xml`，不替代新增受击入口后的最终结果。

加入本地受击入口后的双进程运行在 `Logs/GameplayCameraProcess/20260910-005508/`：Host 和独立 Client 均 **PASS**。两端各收到一次 PlayerHit(3)、两次自身 Ultimate LargeExplosion(2)，远端普通受伤、远端 Ultimate、重连均未增加本端次数。1280×720 下上方两角基础相机位置分别为 (-41.11,45.99,-19.13)、(41.11,45.99,-19.13)，最大实际震屏父偏移约 0.469 / 0.543，结束后回零。独立跟随、单目标尺寸、玩家位置不受相机修改、Client 重连和卸载清理通过。

**首次交付时的画面限制（历史记录）**：Unity 的 CaptureScreenshot 在隐藏窗口的 batch 和普通 Player 尝试中均返回 `Failed to capture screen shot`，没有可用 PNG。随后使用 computer-use 检查实际窗口，看到 Windows Defender 防火墙权限提示；没有操作安全权限弹窗。该技能的 `docs/guidance.md` 明确要求 `Do not act on security or privacy permission requests.`，当时停止窗口操作，没有把截图失败的运行算作画面通过。用户回来处理提示后，后续可见窗口重测已取得画面，见下文。

交付夹具将截图改为显式 `-CaptureFrames`，请求截图却未生成任何图片时会报失败；默认模式只检查相机/网络行为。Unity 构建结果见 `build-final.log`（受击入口版本）及 `build-delivery.log`（交付夹具）。

交付构建的最终默认双进程运行记录为 **`Logs/GameplayCameraProcess/20260910-005915/`，Host/Client 均 PASS，退出码 0**；无异常或截图失败消息。该轮最大实际震屏父偏移约 0.475 / 0.517，正常结束并通过重连检查。它确认的是逻辑和实际 Transform 变化，不声称已检查屏幕观感。

初次相机测试被既有 Circling 的缺失 FMOD bank 错误中断；改用已有 `WeaponAttackAdmissionFixtureGate` 仅在测试运行时暂停普通武器，并禁用自动刷怪，避免无关音频/伤害干扰。未删除生产资源或屏蔽全部错误。Ultimate 仍经过正式授权、动画与原两次震屏事件。首轮 batch 双进程逻辑通过，但截图失败，不作为画面验证证据。

### 2026-09-10 用户返回后的可见窗口重测

复用同一份 `c46db7c` 的 Player 构建，不修改 C#、Scene、Prefab、插件或项目渲染设置。启动器增加显式 `-VisibleWindows`，仅在用户需要观察/操作窗口时使用；默认仍隐藏运行。增加退出码记录及进程句柄保留，非零退出码仍判失败。`-ForceD3D11` 仅用于启动参数对照。

| 运行目录（均在 `Logs/GameplayCameraProcess/`） | 模式 | 相机/联机断言 | 截图 | 最终退出结果 |
| --- | --- | --- | --- | --- |
| `20260910-010553` | 隐藏窗口，要求截图 | 执行到重连后，因无 PNG 触发失败 | 0 | 截图验收失败，终止等待中的 Host。 |
| `20260910-010653` | 可见窗口，默认 D3D12 | Host/Client 都记录 PASS | 9 PNG | 启动器检测到非零退出码；未判整轮通过。 |
| `20260910-010820` | 可见窗口，默认 D3D12，显式记录退出码 | Host/Client 都记录 PASS | 9 PNG | Host `-1073741819`（0xC0000005），Client `-1073740771`（0xC000041D）。 |
| `20260910-011023` | 可见窗口，命令行强制 D3D11 | Host/Client 都记录 PASS | 9 PNG | 相同退出码；切换图形 API 未消除崩溃。 |

两端各一次实际受伤 PlayerHit(3)、各两次自身 Ultimate LargeExplosion(2)，远端事件无额外本端震屏；独立目标、边界、单目标尺寸、实际角色位置、Client 断线/重连和 Gameplay 清理断言通过。D3D12 最后一次运行的最大震屏父偏移为 Host 0.919、Client 0.979，D3D11 为 0.966 / 0.976，均正常回零。已直接查看实际游戏窗口和受伤、Ultimate、重连 PNG；静态图能够确认图像输出、UI、Owner 标记和重连后的新 local player，不能替代连续震屏手感和手动鼠标测试。

退出崩溃发生在夹具记录 PASS 并调用 Application.Quit 后。Windows 事件确认故障模块为 `UnityPlayer.dll`，偏移 `0xC2D6E9`；Client 原生堆栈经过 `USER32/COMCTL32` 和 `AppUINativePlugin`。日志及事件证据在 `20260910-010820/client.log`、`windows-crash-events.xml`，D3D11 对照堆栈在 `20260910-011023/client.log`。这是真实原生崩溃，不能只依据夹具 PASS 判定整轮成功。

发现 M2 之前的 `Crash_2026-09-06_121256241/client.log`（旧 `ModifierSelectionValidation`）也有同类 Unity 窗口关闭 / AppUINativePlugin 堆栈，已保留副本 `Logs/M2Camera/retest-20260910/pre-M2-ModifierSelection-crash.log`。这证明该故障路径早于 M2；当前证据不足以确定根因，本轮没有修改原生插件或将它归咎于相机代码。

运行复现：

1. Unity 中执行 `MonsterSupergroup.Gameplay.Tests.GameplayCameraTests`；补充回归集合为 `DanteUltimateNativeAttackTests`、`NetworkPlayerUltimatePlayModeTests`、`BootGameplaySceneLifecycleTests`、`GameplayHealthHUDLoadingTests`。
2. 以 `GameplayCameraValidationBuild.Build` 构建 Development Player（包含测试程序集，场景仍只有正式 Boot/Gameplay）；默认输出 `Builds/GameplayCameraValidation/GameplayCameraValidation.exe`。
3. 执行 `Tools/Run-GameplayCameraProcessValidation.ps1`，使用 KCP 7905，生成 `Logs/GameplayCameraProcess/<时间>/host.log`、`client.log`。人工观察及截图使用 `-CaptureFrames -VisibleWindows`；可加 `-ForceD3D11` 做诊断对照。当前可见模式会完成相机断言、保存 PNG，随后因退出原生崩溃返回失败；请保留该失败，不忽略退出码。

## 人工验收（仍须实际完成）

1. 从正式 Boot 分别启动 Host 和独立 Client，移动到不同位置。每端自己的角色居中，远端进入、移动、断开和重新加入都不改变本端目标。每端只有一个启用的主相机和 AudioListener。
2. 正常玩法让一端玩家实际受伤，观察该端 PlayerHit；另一端画面不震。无敌期间被攻击、回血和权威 HP 回写不应额外震屏。持续伤害产生各次实际伤害对应的表现，而不是重复订阅叠加。
3. 原 Ultimate 参考效果可先通过双进程夹具观察：服务器授予一份 charge，然后调用既有正式 RequestUse，两次 LargeExplosion，只在释放者一端发生。普通游玩的充能缺口保持记录，不能把夹具授予当作已实现充能玩法。
4. 在 Ground 四边和四角行走，检查相机基础视野留在 Ground 范围。相机限制不限制角色移动；走到范围外时角色可能离开构图，这不是新增碰撞墙。
5. 边界附近受击/运行夹具 Ultimate，观察震屏恢复过程：允许插件的短暂镜头偏移，不能持续抖动、卡位、越震越偏或出现缩放跳变。参考单目标设定保持正交半高 5；远端角色远离不能导致缩放。
6. 在移动和震屏后用鼠标瞄准、释放技能，确认世界方向与光标一致；无重复相机抢输入坐标。
7. Client 断线重连、Host 停止并重开、Gameplay 卸载再加载，检查旧相机容器/目标消失，新相机只绑定新本地角色；每次本地实际伤害仍只触发一次。

上述观感验收和人工输入操作不能由截图或参数断言代替。

## 独立提交与回退

M2 单独提交，排除已有四个 AD meta；用户 Ground 场景输入已单独记录为 `ab74b48`。本轮保存的只读基线记录在 `Logs/M2Camera/baseline-status.txt`、`user-worktree.patch`、`user-index.patch` 和 `Gameplay.before.unity`（该 Scene 快照已包含用户 Ground=100×100）。

仅回退 M2 的提交即可恢复移植前的相机组件与 Owner 绑定，并保留前置提交中的 Ground=100×100；没有协议/数据迁移。不运行整库 reset/clean，不覆盖别人的未提交改动。回退前有后续修改时，逐个合并相关文件，不机械覆盖整个 Scene。

M3 普通命中击退、M4 武器/增益选择、M5 持续多人波次、M6 XP 拾取与成长反馈均保持原计划；本轮不修改此前 Circling 的 local/canonical HP 差异问题。
