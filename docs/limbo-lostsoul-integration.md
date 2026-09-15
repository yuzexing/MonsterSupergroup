# LostSoul 接入与 449.916666… 秒参考预览

**LostSoul v0/v1 的行为与原始美术已接入并完成本轮技术验证；449.9166666666667 秒预览已在同一最终构建完成有画面的单人 Host、Host/Client 连续运行及结算界面重开。** 不形成玩法压力结论；Ghoul 和 Full 保持禁用。

来源为 `docs/evidence/hellmaiden-attacks/runtime-assets.json`，SHA-256 `4dcefbcae22fb6e8cd067618cd0ba782eb5a23d07b20a31a3ecf3323cd6d5c38`。原始导出与恢复证据未修改。

## 来源与适配

| 内容 | 实现基准 |
|---|---|
| 属性 | v0 150/50/3.6；v1 250/50/3.6；XP 7；击退 1 |
| 时序 | W 1.0333333、A 0.146666661、R 0.75333333；冷却 1；距离 2 |
| 预警 | 6.2 × 当前速度倍率，跟随追击；原 BuildUp 长 1，播放速度 1/W |
| 爆炸 | Normal、当前 Stats；九边形；定位点 y=1.74000025；伤害子节点缩放 0.5 |
| 终止 | Recovery 期限到达后服务端无奖励注销；正常玩家击杀保留现有确认死亡与经验入口 |
| 美术 | 十个独立身体动画、共享阴影、原 v1 LUT；复用已迁入的预警和爆炸素材 |

原始字段转录在 `Assets/_Project/Content/NetworkCombat/Limbo/LostSoul/LostSoulSource.json`，适配在同目录 `LostSoulAdapted.json`。资源工具保留已有适配值；修正首次生成错误截止点属于已知生成错误迁移，不覆盖其他参数。

原 `CancelAttack` 的再次攻击调用替换为显式清理；网络不使用本地 `Kill(true,false)` 作为完成依据。现有动作检查点增加三布尔值和一个 Vector2，共 11 字节未压缩字段；不增加伤害 RPC。服务端根据已接受动作完成一次性终止，客户端仅恢复剩余展示和本地玩家命中区。

预警 Hide 后立即归池，与来源调用顺序一致。粒子不负责决定命中窗口或自毁时间。原始音频不迁入；现有 AllIn1 Shader 适配的混合/发光差异继续保留。

## 预览范围

`[0,449.9166666666667)` 直接引用主 Timeline，14 片段、10 个 C、4 个 L，C 原名义预算 967。截止点不启动 Ghoul。新增 LostSoul0 L46（389.6–419.6）、Imp1 L10（419.6 至截止）、Dash1 C50（420 至截止）。旧片段和残留照常；XP 分母 841.5766649882。

入口：`lostsoul` 无测试辅助；`lostsoul-validation` 带测试保护和确定性选卡；`lostsoul-fixture` 选择 `-LostSoulVariant 0/1`、`-FixtureTarget host/client` 与 `-LostSoulCase main/burst/limited/boundary/reuse/expiry/barrier`。旧入口保留。

机制夹具明确修改时间/目标/定位：main 单敌人 L 目标1、时长150；limited 将来源 L46 片段平移到1秒；burst 将来源 B10 平移到1秒。它们不代表正常压力。自毁后产生的新敌人不是敌人池复用；只有实际实例 ID 重现才能证明预警/爆炸池复用。

## 验收状态

运行记录归档至 `Logs/LimboLostSoul` 和独立 `Logs/LimboReference/lostsoul-*` 目录；历史失败保留。两轮原运行副本换色在 `Logs/LimboArt/lostsoul2-palette-a/b`，11 组现有及新增输出全部字节一致。

隔离机制已审阅：四组 v0/v1 × Host/Client 四象限、三阶段交接、50 扣血、无经验自毁、正常武器击杀、L/B、同对象重置及攻击池复用均有记录。2026-09-15 执行独立的行为验收操作，仅放行 LostSoul 对应主片段；Full 继续受 Ghoul 门槛阻止。同最终构建的连续运行及界面重开也已完成，数据见下表。

### 中间记录（不代替最终构建验收）

- `lostsoul-probe1-*` 保留首轮结果，定位夹具存在目标切换异步顺序问题，不计为指定目标四象限通过。
- `lostsoul-probe2-v0-host/v0-client/v1-host/v1-client`：各目标端四象限均有至少 9 次完整 W/A/R，实际伤害 50；三阶段交接、暂停及 0.25 慢动作有记录。四局服务端自毁数分别为 65、64、63、65；自然武器确认死亡分别为 2、1、1、1。自毁没有混入确认死亡。
- 该批构建归档于 `Builds/LimboLostSoulProbe2Archive`。主夹具启用正常武器前不产生击杀，期间累计补充由自爆引起；这是单存活目标夹具，不能替代 L46 验收。
- 过期预警交接复现：`play6.xml` 三项失败；修正后只开放尚未过期的 Active，所有过期情况保留一次性终止。过期 Recovery 的 FSM 留在 Recovery 等待服务端回收，避免先回 Moving 又发起攻击。
- 恢复期间 canonical 死亡的测试发现待自毁标记未清；死亡/注销路径增加爆炸清理。`play12.xml` 九项通过，包括实际 Mirror Host 中的过期终止及死亡竞争清理。该死亡测试使用明确注入的 GAS 伤害，不作为自然武器击杀录像。
- 新增 `boundary`（45 秒、内外点及预警期间换位）、`reuse`（40 秒、v0→v1→v0）、`expiry`（45 秒、明确停止移动以观察宽限和到期屏外淘汰）夹具。它们只使用独立测试 Timeline，不改主流程。
- `edit4.xml`：网络 EditMode 543/543；`play15.xml`：LostSoul、旧敌人、近战展示、围栏、协程 PlayMode 28/28。回归中复现了共用近战退出在强制死亡仍有正 HP 时错误结算碰撞的问题，改为读取既有死亡请求状态；不改生命来源或敌人属性。失败记录为 `play13/14.xml`。
- Build3 的 `lostsoul-probe3-burst`：同一时间 3.019385915 秒生成 10 只，来源计数全部为 0；10 次无经验自毁，结束存活为 0。预警及双端截图均归档，出生速度倍率范围 0.9802205–1.019879，预警速度检查无不匹配。
- Build3 的 `lostsoul-probe3-limited`：累计生成 649、自毁 649、玩家击杀 0，生成时存活最大 46；最后出生 30.983123558 秒（片段到 31 秒）。它证明自爆补怪，不代表关卡固定敌人数。
- Build3 的 `lostsoul-probe3-expiry`：1.0183054 秒出生，片段到 6 秒后仍保留；31.0256473 秒进入到期屏外淘汰，未计击杀或自爆，结束数量为 0。
- Build3 的 `lostsoul-probe3-reuse`：两端实际同一爆炸实例 ID 跨 v0/v1/v0 使用；不将新出生敌人称为敌人池复用。`lostsoul-probe3-main` 在敌人 id65 被正常武器打到 HP131 后，对同一对象执行了生产重定位/重置。
- Build3 边界夹具在名义 Warning 截止与 AttackEnter 之间回到了原点，导致先发生真实碰撞，再被移到“外侧”。该批外侧记录不算通过；后续夹具保持远离位置直到真实爆炸状态出现，另记录命中时玩家实际位置。

玩家保持 HP500、速度4.55、武器1、XP修正2、当前镜头及 Nordic。Spine、人工压力、Ghoul、完整 Limbo、完整断线重连/倒地复活矩阵及原游戏同条件正常压力对照均不属于本轮完成项。

### 本次收尾复核

- `lostsoul-probe4-boundary` / `lostsoul-probe5-boundary-client`：Host / Client 各10个内侧样本命中50、各10个外侧样本不命中；非目标端没有重复伤害。两端各20次自毁。
- `lostsoul-probe4-main-v1`：69次出生/自毁，69次客户端50伤害，绑定无不匹配；自然武器击杀证据仍采用前述单独记录，不将本局自毁称为击杀。
- `lostsoul-probe5-barrier` 未启动：测试说明放在 Ready 状态触发门槛。改为显式 ValidationPending 夹具后，`lostsoul-probe6-barrier` 双端运行60秒，25次出生/无经验自毁；实际火焰围栏经历入场、收缩、退出，双方记录0.25及1时钟倍率，结束槽位/碰撞/伤害区/预警均为0。入场慢动作期间未捕获 Active 帧，Active 慢动作以主夹具114–120秒的显式慢动作记录补充，不声称本局覆盖了该交集。
- `edit5.xml` 545/545，通过独立夹具门槛、混合预览各 Prefab GUID 身份唯一性及全部网络 EditMode 回归；PlayMode `play15.xml` 28/28。

## 最终构建与连续运行

构建：`Builds/LimboLostSoul20260915/MonsterSupergroupLimbo.exe`，D3D11、Unity 6000.3.21f1、现有 Mirror KCP 开发构建。`Logs/LimboLostSoul/build-final.log` 成功。源码、恢复数据与适配数据随项目保留；原始值和适配值当前相同，行为修正和 Shader 差异由本文另记。

从项目根目录启动无辅助单人预览：

```powershell
./Tools/Run-LimboReference.ps1 -Profile lostsoul -Role host -WaitFor 1 -BuildDirectory Builds/LimboLostSoul20260915 -Windowed
```

技术运行加 `-Profile lostsoul-validation -AutoWalk -ArtObserve`；双端分别启动 host/client，使用相同 `-Port`、`-WaitFor 2` 和独立 `-RunName`。不要只复制 exe，保留整个构建目录。

三端运行的7个记录文件哈希全部一致，具体在各端 `art-build.json`。其中 NetworkCombat DLL 为 `6ED1E4910DF229A3015C9DC9CFA348633D3CFD847BB11317A88D1897ED5BFAE7`，resources.assets 为 `E298569984A765784DA1EA7ECF5E395823AF1E3B2F925DCB9BC351194B1A717B`。

| 首轮结果 | 单人 Host | 双端 Host/Client |
|---|---:|---:|
| 终点 | 449.9166666666667 | 双方449.9166666666667 |
| 实际出生 | 1618 | 双方1581 |
| 确认击杀 | 1553 | 双方1546 |
| 到期无经验淘汰 | 41 | 7 |
| 无经验自爆 | 0 | 1 |
| 结束时存活快照 | 24 | 双方27 |
| 生成事件处采样的存活峰值 | 198 | 186 |
| LostSoul 出生/玩家击杀/自爆 | 116/116/0 | 152/151/1 |
| C 实际出生 / 原名义预算 | 966/967 | 966/967 |
| L 实际累计出生 | 652 | 615 |
| 出生属性不匹配 | 0 | 0；两端1581个ID及出生属性完全一致 |
| 新终点处 Ghoul 出生 | 0 | 0 |
| 结束伤害区/预警/陷阱占用 | 0/0/0 | 双方0/0/0 |

两局均覆盖14个原始片段。最后 Dash v1 C 片段实际各49次：最后出生分别449.7731145164、449.8082182612，下一次相对等待最早约450.371/450.407，已超过预览截止点。保留逐次等待，不为凑齐50而补发或压缩间隔。其他9个 C 共917次全部完成。L累计量不能当作同时存活目标。

首轮原始数据在 `Logs/LimboReference/lostsoul-final-solo` 和 `lostsoul-final-pair`，`first-round-audit.json` 保存重开前审计。`final-audit.json` 含首轮、界面重开和新局观察记录；新局只验证初始化与清理，随后停止测试进程，未再运行至终点。单人新 RunId 为 `601851f1818349949d37429a62a6b3e1`；双端为 `ac161449723f4b7dbfee4a0081cc14ad`。双方新局RunId一致、生命500、等级1，原世界释放时旧伤害区/预警/爆炸实例均为0。归档的 `gui-end.jpg`、`gui-restart.jpg` 来自实际界面点击；重开后汇总的全部出生ID/属性也无双端差异。

首轮选卡31/24/21段（单人/双端Host/Client），最长停留0.211/0.184/0.202秒，累计3.144/2.382/2.321秒，无未关闭选卡。具体卡牌在 `test-card` 行，开闭时刻在 `selection` 行。连续局的补血、普通自动移动、确定性第一张选卡是明确测试辅助，不用于压力判断。

代表击杀用时来自首次有效受伤到 GAS ConfirmedKill，见审计的 `firstHitToConfirmedDeath`，没有使用出生寿命。首轮 LostSoul 有效样本分别116/147/151个；双端Host有4个缺少首次受伤观察的样本，未补造时长。两端本地观察时刻受同步影响，不要求该时长逐毫秒相同。

首轮每秒一次的帧时间采样（不含加载），单人/双端Host/Client中位数均约16.67ms，P95为17.05/17.04/16.99ms，最大采样80.30/113.45/63.80ms。最大已分配内存约319.6/304.5/304.6MB，最大粒子采样6009/4688/4620。三个窗口同时运行；这不是逐帧Profiler统计或容量承诺。界面重开已检查缓存、粒子和旧对象释放，所有原始性能行保留。

## 逐项技术验收与证据入口

| 项目 | 本轮结论及主要证据 |
|---|---|
| v0/v1原始身体、LUT、绑定、几何 | 通过；10个身体动画，v1烘焙引用原LUT，双次原运行副本输出一致。累计371项美术依赖，源文件及证据哈希未变 |
| 两变体×目标端×四象限 | 通过；probe2四局，每象限至少9个完整W/A/R样本；probe3/4补充最终逻辑修正后的观察 |
| 九边形内外、50伤害、Active截止 | 通过；probe4/5各目标端10内侧命中、10外侧不命中；预警无伤害；正常关窗与取消分开处理 |
| 三阶段交接、过期交接、暂停 | 通过；主夹具Warning/Active/Recovery交接记录，加PlayMode过期W/A/R及服务端终止复现/回归；不补打过期窗口 |
| 慢动作 | 通过；显式0.25主夹具及probe6实际围栏时钟记录；实际围栏入场与Active的交集未捕获，未将其单独标为通过 |
| 自毁无XP与正常击杀 | 通过；L46夹具649自毁/0击杀；最终混合双端1次自爆与151次正常LostSoul击杀分账；死亡竞争单测明确为注入GAS伤害 |
| 变体重绑、实际攻击池复用 | 通过；probe3-reuse实际同一爆炸对象跨0→1→0；probe3-main同一受伤敌人重置；新敌人生成不称为敌人池复用 |
| 屏外及到期淘汰 | 通过本次LostSoul新增路径；probe3-expiry越过片段结束保留到出生30秒宽限后无经验淘汰；沿用既有全员屏外规则 |
| LostSoul v1 B批次 | 通过；probe3-burst同批10次、来源计数0、真实存活10、U(.98,1.02)；占用及清理按既有生命周期 |
| 最终连续流程及GUI重开 | 通过；同构建首轮单人/双端均至终点，14片段；新局时钟、生命、等级和对象清理见final-audit及GUI截图 |

世界命中区快照：`Logs/LimboLostSoul/world-geometry-v0.json`、`world-geometry-v1.json`，包含实际记录路径、动作、中心及九个世界顶点。逐组原始截图在各运行目录；v0例为 `lostsoul-probe3-main/client/lostsoul-0a77dbe8bff948b4bfabf0a0e8edc851-time10.png`，v1例为 `lostsoul-probe4-main-v1/client/lostsoul-e787f77acf9b44b49f58e070ead49d60-time130.png`。最终混合画面在 `lostsoul-final-pair/client/gui-lostsoul-live.jpg`。

复核命令：`Tools/Audit-LimboLostSoulRuns.py <运行目录> --output <审计路径>`；`Tools/Analyze-LimboLostSoul.py <运行目录>` 提供更细的动作/池实例统计；`Tools/Audit-LimboArt.py` 核对依赖与源哈希。上述程序只读取已有运行证据，不驱动游戏或伪造验收。

当前已接入7种来源敌人身份、8套身体及16种外观/变体组合。人工压力、Spine、Ghoul、完整720.9秒流程、完整断线重连/倒地复活矩阵、同条件原游戏正常流程对照继续保留。现有地图、镜头、XP修正2、武器/升级选择和Shader的差异仍存在，不能据此宣称原游戏完整战斗压力或像素级效果已复刻。
