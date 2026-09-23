# 旧构建入口审查清单

本轮按用户决定：旧构建调用暂不兼容，文件保留待审查。新执行链只读取原生 Profile 和 MonsterBuildSettings；catalog.json 的 builds/buildAliases 留作历史记录。

| 入口 | 当前处理 | 后续审查方式 |
|---|---|---|
| ProjectBuildService 的字符串 Build／Legacy／LegacyBatch | 明确迁移错误，不构建 | 调用者改用明确的 Profile，或删除该旧入口 |
| Invoke-ProjectTool 的旧构建参数 | 参数检查拒绝 | 选择新 Profile 或复制合法变体 |
| Resolve-ProjectBuildExecutable 的 Recipe 默认选择 | 拒绝读取旧配方指针 | 改用 BuildProfile；显式历史包仍可选择 |
| 旧 JSON 配方和别名 | 不参与新构建解析 | 决定是否单独归档或删除 |
| Window-dev／Window-test | 已删除，未沿用 GUID | 使用新的规范模板 |

现有专项用途的规则、场景校验、Wisp 注入和测试程序集区别已迁入共享用途规则。此处不把旧脚本文件的存在解释为其旧构建参数仍可用。

## 受影响文件

- `Assets/_Project/NetworkCombat/Editor/BootGameplayProcessValidationBuildUtility.cs`
- `Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Imp.cs`
- `Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.LustSinner.cs`
- `Assets/_Project/NetworkCombat/Editor/EnemyPrefabVariantMigration.Validation.cs`
- `Assets/_Project/NetworkCombat/Editor/KcpDevelopmentBuildUtility.cs`
- `Assets/_Project/NetworkCombat/Editor/NetworkEnemyProcessValidationBuildUtility.cs`
- `Assets/_Project/NordicSample/Editor/NordicStaticSampleBuilder.cs`
- `Tools/New-LimboManualPackage.ps1`
- `Tools/Run-EnemyDefinitionValidation.ps1`
- `Tools/Run-LimboDashMatrix.ps1`
- `Tools/Run-LimboReference.ps1`
- `Tools/Run-LimboRepositionMatrix.ps1`
- `Tools/Run-LimboSpatialMatrix.ps1`
- `Tools/Run-LimboStage2Matrix.ps1`
- `Tools/Run-LimboTimedAttackMatrix.ps1`
- `Tools/Run-RegressionClosure.ps1`
- `Tools/Scenarios/Export-SteamEvidence.ps1`
- `Tools/Scenarios/Run-AllurePrototypeValidation.ps1`
- `Tools/Scenarios/Run-BeamProcessValidation.ps1`
- `Tools/Scenarios/Run-BootGameplayProcessValidation.ps1`
- `Tools/Scenarios/Run-CirclingProcessValidation.ps1`
- `Tools/Scenarios/Run-DashProcessValidation.ps1`
- `Tools/Scenarios/Run-EnemyHandoffValidation.ps1`
- `Tools/Scenarios/Run-EnemySimulationProcessValidation.ps1`
- `Tools/Scenarios/Run-ExperienceProcessValidation.ps1`
- `Tools/Scenarios/Run-GameplayCameraProcessValidation.ps1`
- `Tools/Scenarios/Run-HealthHUDProcessValidation.ps1`
- `Tools/Scenarios/Run-ImpProjectileValidation.ps1`
- `Tools/Scenarios/Run-MeleeProcessValidation.ps1`
- `Tools/Scenarios/Run-ModifierSelectionProcessValidation.ps1`
- `Tools/Scenarios/Run-MusicPrototypeValidation.ps1`
- `Tools/Scenarios/Run-NordicGameplayValidation.ps1`
- `Tools/Scenarios/Run-NordicStaticSampleValidation.ps1`
- `Tools/Scenarios/Run-OptionsValidation.ps1`
- `Tools/Scenarios/Run-OrdinaryKnockbackProcessValidation.ps1`
- `Tools/Scenarios/Run-PlayerDebugProcessValidation.ps1`
- `Tools/Scenarios/Run-PreparationMenuValidation.ps1`
- `Tools/Scenarios/Run-RuntimeBoundaryProcessValidation.ps1`
- `Tools/Scenarios/Run-SteamDiagnosticsBenchmark.ps1`
- `Tools/Scenarios/Run-SummonProcessValidation.ps1`
- `Tools/Scenarios/Run-TimelineWaveValidation.ps1`
- `Tools/Scenarios/Run-UltimateProcessValidation.ps1`
- `Tools/Scenarios/Run-UpgradeSelectionProcessValidation.ps1`
- `Tools/Scenarios/Run-WaveProcessValidation.ps1`
- `Tools/Scenarios/Run-WispProcessValidation.ps1`
- `Tools/Scenarios/Start-PrototypePlaytest.ps1`
- `Tools/Scenarios/Start-SteamDiagnostics.ps1`

## 旧配置去哪里了

以下仅记录旧调用含义，供决定保留或删除；这些 ID 和默认参数不再参与执行，也不自动映射到新 Profile。

| 旧 ID | 新配方 | 默认类型／网络／分发／诊断 |
|---|---|---|
| `player-development`、`boot-process` | `product` | Dev／Steam／Direct／Normal |
| `player-release` | `product` | Shipping／Steam／Steam／Normal |
| `kcp-development` | `product` | Dev／Kcp／Direct／Normal |
| `steam-evidence` | `product` | Test／Steam／Steam／Evidence |
| `menu-development` | `gameplay-validation` | Dev／Steam／Direct／Normal |
| `menu-release` | `gameplay-validation` | Test／Steam／Direct／Normal |
| `player-debug-release`、`rewired-release` | `gameplay-validation` | Test／Kcp／Direct／Normal |
| `enemy-variants`、`imp`、`lust-sinner`、`enemy-hit-flash`、`camera`、`experience`、`waves`、`health-hud`、`modifier-selection`、`knockback`、`timeline-waves`、`player-debug-development`、`rewired-development`、`beam`、`circling`、`dash`、`melee`、`summon`、`ultimate`、`runtime-boundary` | `gameplay-validation` | Dev／Kcp／Direct／Normal |
| `nordic-gameplay` | `gameplay-validation` | Dev／Kcp／Direct／Normal，保留地图校验 |
| `wisp` | `wisp-validation` | Dev／Kcp／Direct／Normal，保留场景注入 |
| `options` | `options-validation` | Dev／Kcp／Direct／Normal |
| `enemy-handoff-development` | `handoff-validation` | Dev／Kcp／Direct／Normal |
| `enemy-handoff-release` | `handoff-validation` | Test／Kcp／Direct／Normal |
| `sandbox` | `sandbox` | Dev／Kcp／Direct／Normal |
| `nordic` | `nordic` | Dev／Kcp／Direct／Normal |

历史名称中的 `release` 不一定代表 Shipping，例如 `menu-release` 一直是程序验收用途。判断包用途以构建摘要和 BuildInfo 为准。

