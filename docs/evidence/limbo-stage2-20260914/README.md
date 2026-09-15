# 阶段二证据归档说明

`recordings.zip` 包含 34 个明确选择的运行目录、564 个日志／快照／截图等文件；`manifest.json` 提供每个文件和归档本身的 SHA-256。原始 Logs 仍保留。归档包含失败与中断，不表示全部通过。

- `s2-matrix-*` 是十个组合的单人和双端机制记录。精英方向以 `s2-direction-*` 补验为准；早期名义象限不能替代实际方向。补验后段的旧武器 API 调用失败不计入死亡通过项。
- `s2-l-hold`／`s2-l-kill`／`s2-l-boundary` 分别是保持存活、武器击杀和显式销毁后的补充实验。它们使用隔离配置；边界实验的销毁不是武器击杀。
- `s2-recycle-*` 为显式 Active 回收验证，不是自然死亡。
- `s2-continuous-solo-b` 首局正常达到 209.05 秒，11 次选卡记录完整；第二局为结算界面重开后的短运行。RunId 必须分别统计。被遮挡的 `ui-restart-capture-occluded-invalid.jpg` 没有放入归档，不作为有效画面证据。
- `s2-preview-solo` 为较早的受限完成局；`s2-continuous-solo` 为中断局；两个 `s2-continuous-pair*` 因输入冲突和长时间选择停留中断，均不是双端连续验收通过记录。

manifest 的 provenance 是归档时的来源／配置／构建快照。旧机制运行使用各自当时的构建，不能从这一份 DLL 哈希推断所有历史局使用同一构建。正常单人本次构建哈希、逐项结果和保留项见 `../../limbo-stage2-integration.md`。
