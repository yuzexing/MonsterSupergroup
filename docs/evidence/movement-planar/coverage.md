# 平面显示资源覆盖清单

2026-09-21。按生产武器数据库、终极技能和既有敌人特效引用递归解析。计数为渲染器材质槽，不是特效实例数或新增材质数。

| Prefab | 本轮替换槽 | 已适配槽 | 保留身体渲染器 | 不支持槽 |
|---|---:|---:|---:|---:|
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Beam/GameObject/PlayerAttack_Dante_DragonsBreath_Fire.prefab` | 20 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Beam/GameObject/PlayerAttack_Dante_DragonsBreath_Poison.prefab` | 21 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Circling/GameObject/PlayerAttack_Dante_Circling.prefab` | 22 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Dash/GameObject/FIRE.prefab` | 4 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Dash/GameObject/FIRE_Poison Variant.prefab` | 4 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Dash/GameObject/FireTrailAttack.prefab` | 4 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Dash/GameObject/FireTrailAttack_Poison.prefab` | 4 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Melee/GameObject/PlayerAttack_Dante_Slash.prefab` | 0 | 7 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate/GameObject/Fire circle_Ultimate.prefab` | 429 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate/GameObject/Ultimate AttackDante.prefab` | 15 | 0 | 0 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Ovid/Summon/GameObject/Ovid_Summon_ButterflyAI.prefab` | 15 | 0 | 3 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Ovid/Summon/GameObject/Ovid_Summon_ButterflyAIFire.prefab` | 15 | 0 | 3 | 0 |
| `Assets/_Project/Content/HellMaiden/NativeGAS/Ovid/Summon/GameObject/Ovid_Summon_ButterflyAIPoison.prefab` | 15 | 0 | 3 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Art/Effects/Enemy_Bomb_ExplosionAttack 1.prefab` | 0 | 1 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Art/Effects/Ghoul_Warning.prefab` | 0 | 3 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Art/Effects/ReferenceFireParticles.prefab` | 0 | 5 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Art/Effects/soul enemy warning.prefab` | 0 | 6 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Dash/ReferenceDashArrow.prefab` | 0 | 8 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Ghoul/ReferenceGhoulAttack.prefab` | 0 | 3 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/LostSoul/ReferenceLostSoulExplosion.prefab` | 0 | 1 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Stage2/Elite_SkeletonAttack.prefab` | 0 | 3 | 0 | 0 |
| `Assets/_Project/Content/NetworkCombat/Limbo/Stage2/SkeletonAttack.prefab` | 0 | 3 | 0 | 0 |
| `Assets/_Project/GameObject/PlayerAttack_Dante_Projectile.prefab` | 0 | 9 | 0 | 0 |
| `Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Fire Variant.prefab` | 0 | 9 | 0 | 0 |
| `Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Impact.prefab` | 0 | 3 | 0 | 0 |
| `Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Poison Variant.prefab` | 0 | 9 | 0 | 0 |

三套召唤外观的茧、身体和脚底阴影共九个渲染器保留。光束自身的长条阴影使用独立 PlanarSummonShadow，保留现有方向渐隐近似，不宣称恢复了缺失的来源 Shader。

环绕 SparksBurst 保留旧 `_USESOFTPARTICLES` 关键字；当前原 Shader 与适配 Shader 实际编译开关为 `SOFTPART_ON`，该旧关键字不激活深度采样。本次不擅自新增软粒子效果。

定向工具在保存每个 Prefab 前比较所有非渲染组件及全部 Transform 的指纹；不允许改变碰撞、粒子模拟、音效和行为配置。现有材质适配值保留；未支持 Shader 必须登记，当前清单为零。
