# Limbo 序列帧美术恢复

**后续接入更新（2026-09-15）：** LostSoul E8身体、10个独立动画及v1原LUT现已接入，预警和爆炸已从展示验证接入战斗。累计8套身体、16种外观/变体组合、371项依赖（84动画、16视觉模板、24材质、182 Sprite、60纹理、5 Shader映射），11份换色输出双次一致。当前构建为 `Builds/LimboLostSoul20260915`，已完成449.916666…秒单人/双端技术运行及GUI重开；详见 [LostSoul验收报告](limbo-lostsoul-integration.md)。下面七套身体、325项依赖和LostSoul暂缓的描述为前一交付的历史范围；目前只剩Ghoul身体未接入。

本文件记录 2026-09-15 的实施及证据。原游戏导出工程、攻击恢复快照保持只读；运行和适配资源位于 MonsterSupergroup。**已接入七套身体、十四个组合及八组提示；已完成双端技术回归和 389.6 秒连续运行、GUI 重开。原定制 Shader 的完整效果仍有明确差异。**

## 范围和可追溯清单

批准的 E1–E7：Brotchi、Brotchi_Dash、Imp、Skeleton、Elite_Skeleton、Slime、Rusher。覆盖 14 个外观／变体组合；LostSoul、Ghoul 身体仍暂缓，Full 门槛保持不变。

批准的 V1–V8：Dash 箭头、普通／精英近战预警、Imp 子弹、LostSoul 预警及爆炸、Ghoul 预警、B 出生火焰、围栏火焰。B／围栏共用来源 FireParticles。

- 清单：[ArtSource.json](../Assets/_Project/Content/NetworkCombat/Limbo/Art/ArtSource.json)，包括来源路径、GUID、SHA-256、引用者、目标位置和处理方式。
- 去重后 **325 项依赖记录**：74 AnimationClip、15 视觉模板 Prefab、24 材质、149 Sprite 资产、58 纹理、5 个 Shader 适配记录。74 个动画 = 66 个身体动画 + 2 个阴影动画 + 6 个预警动画。
- 处理方式：174 项补迁，45 项独立适配，101 项复用，5 项 Shader 映射。Shader 记录指向项目现有实现；不是复制五个可用的原始 Shader。
- 上述是依赖记录数，包含复用项和适配项，不等于新增文件数、磁盘增长或显存占用。十张运行时换色输出另见 BakedPalettes.json。
- 不迁移来源音频、玩家、武器、地图、UI、掉落、敌人管理及伤害脚本。

来源绑定固定为 `docs/evidence/hellmaiden-attacks/runtime-assets.json`，SHA-256：`4dcefbcae22fb6e8cd067618cd0ba782eb5a23d07b20a31a3ecf3323cd6d5c38`。

## 实现与实际修复

1. `Tools/Export-LimboArt.py` 只提取批准的视觉依赖，模板剥离 MonoBehaviour、音频和碰撞。已有 GUID 对应资源复用；材质、特效模板和预警动画生成独立 GUID。重跑不覆盖已有适配文件。
2. `LimboArtAssets.ApplyOpeningBatch/ApplyAllBatch` 定向更新独立参考 Prefab，沿恢复的 Transition 字段绑定动作，保留事件时间及 DeathAnimationShadowFade 阴影回调，省去来源音频回调。身体与攻击几何分开；普通／精英攻击根仍分别为 1／1.5。
3. 原始换色 Shader 在导出中是占位代码，因此在隔离的原游戏运行副本中使用实际编译 Shader 生成图集。`original-palette-a`、`original-palette-b` 两次独立进程的十组结果逐字节一致，`Import-LimboArtPalettes.py` 校验后才导入。不是按贴图名称猜色或手工染色。
4. 网络出生／重置沿已同步的来源身份与变体选择 LUT。没有增加颜色 RPC、第二份 HP 或攻击管理器。换色缓存按使用者释放，最后一名使用者离开后清理生成的 Sprite；烘焙图集是资源资产，不随敌人销毁。
5. 第一批画面发现旧 `Assets/Sprite/Sprite.controller` 的 EnemyBaseSample 仍将 Sprite 写成 Circle、颜色写成 `(255,0,0)`。仅参考外观移除该控制器，恢复原方向动画。原先为空的移动动画入口通过参考配置开关启用；Replica 沿已有目标和动作状态显示移动，不驱动位移或伤害。
6. 接触 Prefab 原先未锁旋转，碰撞会令身体和偏移接触圈一起歪转。来源 Brotchi／Slime／Rusher 均锁旋转；参考 Prefab 补齐 FreezeRotation，不改变半径、偏移或数值。
7. 出生留边、多人屏外距离采用明确的身体 SpriteRenderer bounds，排除阴影和预警粒子。身体仍有一部分可见时不因脚底点出屏而处理。
8. Slime 后半段变体测试发现重置后恢复成旧 Circle 默认帧，并触发压缩纹理运行时换色异常。原因是移除旧控制器时恢复了缓存的默认 Sprite；定向工具在移除控制器后再次写入来源 Prefab 的默认身体帧。新增默认帧检查，失败局保留，Slime／Rusher 重跑。
9. 火焰保留六个粒子系统。为避免某一端离屏后暂停粒子、导致生命周期永不释放，参考火焰使用 AlwaysSimulate 和缩放时间；仍由既有陷阱阶段及战斗时钟管理。火焰渲染层从来源背景层适配为 EnemyAttack，避免被 Nordic 地面遮住；发射参数和碰撞保持独立。

10. 实际死亡补验发现网络端立即销毁会截断尾部；仅参考 Prefab 等待既有死亡展示完成后回收。死亡在 canonical ledger 中立即生效，因此尸体不进入 C／L 存活计数，经验仍由既有确认击杀入口处理。
11. 接触敌人的 movement-only 初始化没有主动攻击 FSM。参考路径补播身体移动及死亡，不创建第二套 FSM。另修复本地预测致死在伤害结算深度内延迟后未启动展示的问题；嵌套结算完成后启动一次，重复确认不重播。死亡时关闭接触、身体碰撞和受击区。
12. 近距离精英补验发现 Replica 攻击方向没有写回供死亡动画选择的朝向，可能使 Host／Client 选择不同死亡方向。参考路径沿现有动作方向更新显示朝向，并在开始死亡时锁定最后实际展示的方向，阻止延迟导航／动作覆盖死亡动画，不新增网络消息；连续局中离屏 Animator 的 CullCompletely 仍可能不采样帧，不能把离屏日志当成可见死亡验证。
13. `art-death` 是显式展示补验配置：通过现有入口重定位一次、使用正常装备武器、关闭经验拾取倍率并在低血时补血。它不强制击杀、不改敌人 HP，也不能称为普通玩法或敌人对象池复用。用于补齐连续区间之外的变体死亡帧。

## 材质与展示差异

- 现有 AllIn1 Shader 承担支持的换色、闪白、透明度及溶解能力。来源定制 URP 灯光、自动像素尺寸及部分第三方粒子 Shader 不具备完整可重建代码，不能宣称所有材质已逐像素还原。
- 匿名动画属性 `0x8B19FAF2`、`0x8D9D63DA` 分别恢复为 `_Alpha`、`_FadeAmount`。名称的 CRC 与来源一致；Unity 重新序列化后的六个动画中，相关原生绑定 ID 也保持一致。审计结果在 dependency-audit.json。
- 精英改为独立原动画；右侧恢复画面约 0.28 秒，逻辑 Recovery 继续从来源左侧绑定取得约 0.43 秒。
- 当前 Nordic 地图、镜头、玩家武器输出与 XP 修正 2 保持本轮基线。本轮技术辅助及并发测试不用于玩法压力判断。
- 共享第三方材质的混合、火焰亮度、像素边缘和层次仍需要逐组画面核对；未知效果不标为恢复通过。
- 运行采集的换色输出是 R8G8B8A8_SRGB；本项目沿用关联纹理的导入设置，Windows 构建为 BC7。源 PNG 字节一致不代表构建后的压缩像素完全相同。这一差异已记录，不修改公共图集的压缩设置。

## 验证记录

| 记录 | 已证实结果 | 限制 |
|---|---|---|
| 两次原始换色采集 | 10 组 PNG 完全一致 | 属于带采集代码的原游戏副本，不是原游戏正常流程压力对照 |
| opening-tests/edit-3.xml | 首批 14 项通过 | 后续增加了移动、接触旋转及其余资源，需整组回归 |
| art-opening-dash0-r2 | 发现红色身体；保留截图和动作日志 | 失败外观，不算画面通过 |
| art-dash-diagnostic | 捕获 Circle、RGB(255,0,0)，定位旧控制器 | 调试证据 |
| art-fixed-dash-0-client、art-fixed-dash-1-host | 两端均运行至 150 秒；出生属性 0 不匹配；截图显示粉／紫变体 | 本轮首批技术夹具；最终构建仍需回归 |
| art-fixed-body-Brotchi0/1-solo/pair | 两变体双端及单人记录完整，出生属性 0 不匹配 | 暴露接触旋转问题；修复后重测，不据此标为全部通过 |
| all-edit.xml / all-edit-2.xml | 曾发现火焰裁剪、地图渲染层两项失败 | 保留失败记录，均已修复 |
| all-edit-3.xml / all-play.xml | 52/52 EditMode、3/3 PlayMode 通过 | 包括恢复绑定、烘焙覆盖、精英时序、Full 门槛及粒子生命周期 |
| art-release-tests.xml | 美术专项 9/9 通过，含阴影回调目标、共享缓存切换和释放 | 与上面的测试有重叠，不相加作为独立测试总数 |
| art-final-spatial-* | B 暂停、两种取消，围栏四阶段取消及两种互斥共 9 个双端运行；9/9 状态检查通过 | 画面显示紫色来源火焰；原 Shader 混合效果仍属适配差异 |
| art-delivery-imp0 / art-delivery-imp1 | 两变体双端分别有 17／23 次一致发射；实际扣血 50／70、0 重复发射；7 张定时截图／端 | 非自然流程，最终死亡尾部另补验 |
| art-retest-body-Slime0/Slime1/Rusher2/Rusher1-solo/pair | 修复默认 Circle 后 12 个进程运行完成；来源属性一致，无压缩纹理异常 | 此记录早于死亡尾部修复 |
| art-retest-spatial-* | B 暂停与两种互斥重跑均完成，两端清理为 0 | 源 Shader 仍是适配显示 |
| art-death-preview-*、art-release2-* | 分别暴露 movement-only 死亡缺失和 Host 本地预测未启动死亡；保存失败日志 | 不作为最终通过证据 |
| death4-tests.xml | 53/53 EditMode 通过，增加嵌套致死结算与重复调用检查 | 自动化不能代替画面 |
| art-final-effects-pair | 两端完成 24 秒视觉隔离流程，0 敌人出生，保存出现／尾部画面 | Ghoul／LostSoul 仍未启用；显式展示时长不代表敌人攻击时序 |
| art-final-dash-0-client / art-final-dash-1-host | 双端均至 150 秒；四方向各 7–8 个完成恢复的动作；实际伤害 50／80；Stats 绑定和错误阶段命中均为 0 | 隔离技术辅助；有旧／新构建哈希，不作为压力结论 |

每个运行目录位于 `Logs/LimboReference/<记录名>/host|client`。出生与数值见 `*-audit.jsonl`；动作、命中与交接见既有 observation 日志；`-ArtObserve` 额外保存实际 Sprite、纹理、LUT、颜色、材质、动作时间、身体边界、帧时间、粒子数及内存采样。

身体／变体、B／围栏与 V5／V6 画面样本已整理到 [逐组画面索引](limbo-art-gallery.md)。死亡尾部修复构建已完成连续流程与 GUI 重开；最终朝向补丁已做双端定向回归。各构建与证据分别列出，未将不同版本混写成同一次运行。人工压力、剩余敌人行为、完整 Limbo 及原游戏同条件压力对照不在本轮完成范围。

## 已完成的 389.6 秒技术流程（Final2 构建）

记录：`art-release3-preview-solo`、`art-release3-preview-pair`。两局均经真实界面“重新开始”进入新 RunId；Host／Client 同轮第一局 1375 份出生完全一致，0 属性不匹配。单人 1352 份出生也全部匹配。两端同一构建，含原始身体、LUT、提示和死亡尾部修复。

| 指标 | 单人 Host | 双端同局 |
|---|---:|---:|
| 成功生成 | 1352 | 1375 |
| 确认死亡 | 1162 | 1346 |
| 无经验淘汰 | 136 | 0 |
| 结束存活（清理前快照） | 54 | 29 |
| 结束后伤害区／预警 | 0／0 | 两端均 0／0 |
| 陷阱占用／状态／碰撞／预警根 | 均 0 | 两端均 0 |

C：Brotchi0=250、Imp0=15、精英0=1、Skeleton0=150、Skeleton2=193、Slime0=150、精英1=1、Brotchi1=58，共 818；C 原始预算 917 中部分在 389.6 秒被截断。L：单人 Rusher=429、Dash=105；双端分别 456、101。这些是累计生成，不是同时存活目标。两局包含明确技术辅助，不能作为压力或多人平衡对照。

1 Hz 采样的帧耗时 p50 均约 16.67 ms；p95 单人／双端 Host／Client 约 18.40／17.05／17.02 ms。Unity 已分配内存范围分别约 236–317／233–310／235–298 MB；粒子数峰值约 6115／4887／5281。期间并发运行多个测试进程，含截图与日志开销；这不是完整逐帧剖析、GPU 显存统计或恢复前后同条件性能比较，不能据此保证目标机帧率。

原始记录与两轮 RunId：`Logs/LimboArt/preview-review.json`；首轮和重开分别统计。较早 `art-preview-solo` 的结束样本曾有一个预警未清零，不能沿用它的完成判定；后续 Final2 单人、双端均为零。旧记录保留。

## 逐组验收边界

| 分组 | 资源完整性 | 已有画面与行为证据 | 原始画面差异 |
|---|---|---|---|
| E1 Brotchi、E2 Dash | 原身体、阴影、方向绑定及两变体 LUT 接入 | 双端动作、正常武器致死、重置、攻击回收；Dash 原箭头与身体命中分离 | 灯光、纹理压缩沿项目渲染；无来源音频 |
| E3 Imp | 原动画及两变体 LUT 补齐 | v0／v1 子弹、充能、飞行、终止、交接与死亡记录 | 子弹使用项目支持的材质效果 |
| E4 Skeleton、E5 精英 | 独立身体、阴影、绑定和两种 LUT；普通与精英几何独立 | 逐变体四方向攻击、精英右恢复关系及双端死亡补验 | 不再使用普通 Skeleton 代替精英；部分原灯光不同 |
| E6 Slime、E7 Rusher | 两套原始身体及各两种实际组合 | 接触、重置、默认帧、L 补怪、两端死亡与清理 | Rusher 外观配 Slime v1 仍按独立组合记录 |
| V1 Dash | 原 Arrow、地面填充、3 个粒子系统及动画 | 起点固定；伤害区随身体移动；取消、交接及尾部独立 | 定制材质亮度／混合仍是适配 |
| V2／V3 近战提示 | 原层级、预警动画和现有材质属性映射 | 0.08 秒伤害窗与约 0.166667 秒退场分开；世界几何不因换图改变 | Shader 像素、发光效果不宣称完全相同 |
| V4 Imp 子弹 | 现有原素材复用、充能／飞行／结束核对 | 50／70 伤害、重复发射为 0、充能无伤害 | 原始 Shader 完整代码仍不可用 |
| V5／V6 LostSoul／Ghoul | 原视觉依赖已准备，独立展示 Prefab | 双端无敌人出生的 24 秒显示流程；出现、持续、退场截图 | **仅视觉准备，不表示攻击行为已启用** |
| V7／V8 B／围栏 | 原 FireParticles 六个系统及关联纹理 | 两端预警、占用、实际碰撞、慢动作、暂停、互斥、取消和释放记录 | 渲染层改为 EnemyAttack；原混合、亮度仍有适配差异 |

“资源完整”指本轮批准的视觉依赖均有目标位置、绑定与来源，不表示已恢复被简化的原始 Shader。未确认的材质细节继续保留待对照状态；没有用任意效果冒充来源。详细画面按组见 [画面索引](limbo-art-gallery.md)。

## 最终交付与复验结果

- 可运行构建：`Builds/LimboArtFinal4_20260915/MonsterSupergroupLimbo.exe`；构建成功记录 `Logs/LimboArt/art-final4-build.json`。
- 最终代码 EditMode **55/55**：`death-facing-tests.xml`，包括恢复绑定、实际几何、状态门槛、调度、缓存、致死结算和方向选择。火焰与协程 PlayMode **3/3** 的批量美术接入记录为 `all-play.xml`；两类结果不是同一批运行，不相加为一个测试套件。
- **Final3 完整技术回归**：`art-release4-preview-solo` 成功生成／确认死亡／淘汰／结束存活为 **1149／891／185／73**；`art-release4-preview-pair` 为 **1472／1425／0／47**。两局至 389.6 秒，Host／Client 同局 1472 份出生一致，属性不匹配 0；伤害区、预警、陷阱状态及碰撞全部清零，并通过实际结算按钮重开。首轮和重开分开记录于 `preview-review.json`。
- **Final4 最后显示补丁的定向回归**：`art-release5-death-Elite0-solo/pair` 与 `art-release5-dash-1-host`。精英两端均为 `ElSkeleton_DeadDownRight`，Dash 两端均为 `brochipink_death_left`，动作进度持续推进；所有记录中死亡敌人均 canonical dead，伤害区 0；结束清理为 0。Dash 150 秒流程实际伤害 80，Stats 绑定及错误阶段命中为 0。
- Final4 相对 Final3 仅增加死亡开始时锁定最后展示方向、防止延迟动作覆盖及其检查，**没有再将 Final4 完整跑至 389.6 秒**。完整流程证据明确对应 Final3，最终交付版本的这项新增显示行为由上面的单人及双端夹具覆盖；波次、数值、协议和资源没有随此补丁修改。
- V7／V8 最后资源回归 `art-release3-spatial-spatial-b-pause-all`、`art-release3-spatial-spatial-barrier-cancel-Stopping` 均完成，状态检查无失败；更完整九项矩阵及三项修复重跑记录保留。
- `dependency-audit.json`：325 项源文件哈希未变、目标均存在，恢复快照哈希未变，六个预警动画原生材质绑定保持一致。十张换色输出两次原游戏副本采集逐字节一致。

完整技术运行含普通输入自动移动、确定性选卡、既有补血保护；死亡夹具另含显式定位。部分死亡截图被玩家、武器或升级卡覆盖，日志仍记录实际 Clip、时间和伤害区状态；没有将遮挡画面称为原图逐像素对照。看得见的源身体／变体、攻击预警与火焰已经按画面索引检查；原 Shader 精确混合效果和正常玩法压力仍不在通过结论内。

Final3 连续局 1 Hz 采样 p95 帧耗时单人／双端 Host／Client 约 **17.13／17.04／17.05 ms**；Unity 分配内存约 **235–296／234–313／235–302 MB**，粒子数采样峰值 **4590／5656／5566**。同期有其他测试窗口及构建，数据只用于回归记录；不作为恢复前后性能提升或稳定帧率承诺。

资源依赖清单、字段快照、逐组画面索引、构建哈希、历史失败与最终验证记录均保留。**LostSoul／Ghoul 仅准备特效，身体与攻击未启用；Full、人工压力、同条件原游戏正常流程和 Spine 接入均未完成。** Spine 继续作为后续独立任务。

## 可运行交付

可玩入口为 `dash`，不带测试辅助；`dash-validation` 是自动移动、确定性选卡和既有测试保护的技术流程。`art-effects` 是资源展示，`stage2-fixture -FixtureMode art-death` 是正常武器致死展示补验。普通游戏默认配置没有改成 Limbo。

```powershell
./Tools/Run-LimboReference.ps1 -Profile dash -Role host -Windowed -BuildDirectory Builds/LimboArtFinal4_20260915 -RunName art-manual-new
```

构建和逐次运行由 art-build.json 记录 SHA-256；`Logs/LimboArt/build-hashes-supplement.json` 补齐早期清单漏掉的实际 Gameplay 程序集名称，原清单没有覆盖。所有历史失败局保留。验收摘要为 `Logs/LimboArt/acceptance.json`；`evidence-index.json` 对 2838 个文件逐项记录路径、大小与 SHA-256（就地归档约 3.27 GB，未复制一份日志）。

## 可重跑入口

```powershell
python Tools/Export-LimboArt.py
python Tools/Import-LimboArtPalettes.py
python Tools/Audit-LimboArt.py
# Unity -executeMethod MonsterSupergroup.NetworkCombat.Editor.LimboArtAssets.ApplyAllBatch
./Tools/Run-LimboReference.ps1 -Profile dash-validation -Role host -ArtObserve -Windowed -AutoWalk -BuildDirectory Builds/LimboArtFinal4_20260915 -RunName <新目录名>
./Tools/Run-LimboReference.ps1 -Profile art-effects -Role host -ArtObserve -Windowed -BuildDirectory Builds/LimboArtFinal4_20260915 -RunName <新目录名>
```

`art-effects` 是 24 秒、无敌人出生的显示夹具，依次展示 Ghoul 预警、LostSoul 预警和爆炸，每组八秒；其中预警使用显式一秒展示速度，**不能作为敌人攻击时序或行为验收**。原敌人仍禁用。

## 后续 Spine 接入（本轮未实施）

沿用已批准的程序接入估算：公共接入 4–7 人日；三套接触外观 1.5–3；Imp／Skeleton／精英 3–6；Dash 1.5–3；首个玩家 2–4；双端及密集场景回归 3–5。七套敌人加一个玩家合计 **15–28 人日**；LostSoul／Ghoul 另加 **3–6**，九套敌人加一个玩家约 **18–34 人日**。

条件：用户提供成品骨骼、图集和完整动作；只计算程序与验证，不包括美术制作、技能、换装。实际接入前锁定与成品导出匹配的 Runtime 版本。本次未引入 Spine 包或骨骼同步。

Spine 只接身体显示；现有战斗状态继续决定时序、位移、伤害和自毁。网络继续传动作／进度，不传逐骨骼状态；攻击提示、子弹及围栏仍独立。高密度性能须用成品骨骼实测，不能从当前序列帧结果推断千只 Spine 的帧率。
