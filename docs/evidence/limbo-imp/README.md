# Imp 阶段一运行证据（2026-09-14）

对应 [实施与验收报告](../../limbo-imp-integration.md)。只覆盖 Brotchi + Imp、隔离机制配置和 105 秒预览，不是完整 Limbo 或原游戏压力对照。

## 文件

- `validation.json`：逐运行、角色、round 的统计与原始文件索引；`files` 中保存压缩前文件的 SHA256。
- `*.jsonl.gz`：原始出生／生命／波次审计及攻击阶段、发射、命中授予、飞行采样、终止、测试操作记录的无损压缩副本。
- `*.csv.gz`：服务端生成机会、成功、位置失败、死亡等事件。文件名保留所属运行和角色。
- `editmode-final.xml`：最终 32 项 EditMode 回归，32 通过、0 失败。
- `full-gate-player.log`：最终构建的 Full 门槛运行日志；Elite_Skeleton 为 ImplementationPending，0 次生成。
- `screenshots/`：实际 D3D11 游戏窗口截图副本，未编辑游戏画面。
- `artifact-hashes.json`：以上随报告保存的文件哈希。

运行路径为 `Logs/LimboReference/<run>/<role>/`，构建与测试完整日志为 `Logs/ImpReference/`。压缩文件可用 Python 标准库 `gzip.open(path, 'rt', encoding='utf-8')` 读取；不要求额外依赖。

`imp-v0-first`、`imp-v0-pair`、`imp-v1-pair`、`imp-v0-remote`、`imp-v1-remote` 是显式机制测试，存在关闭武器、定位／重置、必要补血、暂停和销毁射手等测试操作。`imp-preview-host` 与 `imp-preview-pair` 为连续预览，只使用自动移动输入与正常选卡。

双端连续预览包含两局：`4d3b0e8d76cb452f82c6b5ae335a66f9` 已完成至 105 秒；`31356193920d4c5cbb0250ced65125e9` 为重复开局观察至约 80 秒的未完成局。必须分组分析。两端第一局较长时间停在选卡界面，不能用于压力等价比较。

`phase` 是实际每帧观察的阶段边界，误差约一帧；`attack-config` 是恢复配置／有效期限。`hit-grant` 是服务端唯一命中授予，实际扣血还受玩家无敌影响；生命同值同步回调不计为第二次受伤。结束清理入口不会为每颗残余子弹产生单独的 `terminated` 记录。

## 画面

| 图片 | 画面含义 |
|---|---|
| [v0-host-attack.png](screenshots/v0-host-attack.png) | v0 单人机制测试中的攻击画面 |
| [v0-client-flight.png](screenshots/v0-client-flight.png) | v0 以 Client 为目标的投射物机制测试 |
| [v1-client-attack.png](screenshots/v1-client-attack.png) | v1 以 Client 为目标，已发生两次各 70 点扣血 |
| [preview-host-combat.png](screenshots/preview-host-combat.png) | 单人连续预览中 Brotchi + Imp 实际战斗 |
| [preview-host-result.png](screenshots/preview-host-result.png) | 单人到达 105 秒后的结算 |
| [preview-pair-host-result.png](screenshots/preview-pair-host-result.png) | 双端第一局 Host 结算 |
| [preview-pair-client-result.png](screenshots/preview-pair-client-result.png) | 同一局 Client 结算 |
| [preview-pair-restart.png](screenshots/preview-pair-restart.png) | 双端重新开始后约 42 秒的新一局 |
| [full-gate.png](screenshots/full-gate.png) | 最终构建 Full 被正确拦截，提示数据已恢复但精英行为待接入／验证 |

两次恢复资源快照的一致性属于此前恢复工作。本轮只读取这些证据，没有再次运行原游戏；本轮画面均来自 MonsterSupergroup。
