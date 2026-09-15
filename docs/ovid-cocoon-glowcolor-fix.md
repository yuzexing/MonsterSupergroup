# Ovid 茧材质 `_GlowColor` 绑定修复

日期：2026-09-15。验证环境：Unity 6000.3.21f1。

## 根因与出现时机

`caccoon.mat` 的 AllIn1 Sprite Shader 将 `_GlowColor` 正确定义为 Color。错误来自 `Ovid_Butterfly_Birth.anim` 的四条材质动画曲线：HellMaiden 导出文件丢失了颜色通道名称，留下 `material.path_0x…` 占位名。播放孵化动画时，Unity 将这些绑定按标量材质属性处理，因而尝试以 Float／Range 访问颜色，报告：

```text
Material 'caccoon' with Shader 'AllIn1SpriteShader/AllIn1SpriteShader' doesn't have a float or range property '_GlowColor'
```

历史日志 `Logs/M4/Host-20260910-152834-749-p7983/host.log:2168` 已有同样错误，堆栈经过 `AnimancerGraph.Evaluate → SummonAIBehaviour.ReplayParticlePhase`。本次单人记录 `Logs/LimboReference/dash-preview-solo-20260915/host/player.log:6257` 再次出现。因此这是移植时保留的动画绑定缺陷；首局第一次播放孵化动画即可暴露，并非本次运行才损坏了材质，也不能归因于近期鬼火资源修改。

修复前，该动画与 HellMaiden 原导出文件字节完全相同，SHA-256 为 `93232E60DB2DEE4E8DC44CE021AE93A076E2F2EB4E6F88A5E35EF9656473FB39`。原检查覆盖资源、动画阶段和时长，但没有验证渲染器上的实际颜色变化，故未提前拦截此缺陷。

## 修复

动画目标均为 `Caccon/CacoonSort/cacoon` 的 MeshRenderer：

| 原导出绑定 | 修复后绑定 |
| --- | --- |
| `material.path_0x4C4EFF0C_IUsrjIN` | `material._GlowColor.r` |
| `material.path_0x5C4EFF0C_UIKhIqH` | `material._GlowColor.g` |
| `material.path_0x6C4EFF0C_sQKuHIJ` | `material._GlowColor.b` |
| `material.path_0x7C4EFF0C_oMsokqL` | `material._GlowColor.a` |

动画文件仅替换上述四个名称；字节比较确认其余内容与原导出文件一致。关键帧、时长、GUID、三种预制体、材质及战斗数值均未修改。

`OvidSummonNativeGasMigration.Import()` 现在包含此修复，并在修改后强制重新导入动画以重建 Unity 的运行时绑定。校验入口同时检查四条 RGBA 曲线和三个变体对应 Shader 的 Color 类型，防止重新移植带回错误。重复执行无变化，不重写动画。

## 验证结果

- 修复前回归测试失败：孵化动画第 1 秒，红通道应为 `0.46698105`，实际仍为 `1`，证实错误同时影响颜色表现。
- 修复后 PlayMode **36/36 通过**，涵盖召唤物表现与生命周期。新增检查使用普通、火焰、毒素三个实际预制体，读取渲染器 MaterialPropertyBlock，确认第 1 秒 RGBA 为 `(0.46698105, 0.98875374, 1, 1)`；对象池复用重新孵化时恢复白色，且没有意外日志。
- EditMode **33/33 通过**，包括动画绑定检查、重复修复后的文件字节与 GUID 不变，以及既有移植检查。
- 修复后的两份测试日志未出现该 `_GlowColor` 类型错误。

证据：`Logs/OvidGlow/before.xml`、`Logs/OvidGlow/final-playmode.xml`、`Logs/OvidGlow/editmode.xml` 及同目录日志。

本次未重新打包 Windows 游戏；上述运行验证在编辑器 PlayMode 完成。已有可执行文件包含旧动画，需要重新构建后才会带入修复。
