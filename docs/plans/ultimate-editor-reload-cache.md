# Ultimate 在 Editor 进入 Play Mode 后无法释放（2026-09-11）

## 现场与根因

现场 Editor 日志先记录 `[UltimateDebug] player=2 result=granted`，随后两次为 `already-charged`。因此 F6 已经经过真实 Command 成功授予充能。随后 `CmdUseUltimate → PlayerUltimateRuntime.TryConsume` 抛出 `ArgumentOutOfRangeException(sequenceSeconds)`，Mirror 因处理 Command 异常断开 Host 本地连接。此后 F6 的日志为 `NetworkClient is not ready`，是断线后的结果。相关片段保存在 `Logs/UltimateInputFailure/manual-excerpt.txt`。

`DanteUltimateAttack.EnsureVisualConfiguration` 使用 `mainParticles != null` 判断是否完成了动画时长与粒子缓存初始化。正式 Boot 在 Edit Mode 加载了 Ultimate Prefab 后，进入 Play Mode 的域重载会将这个原本为 null 的私有数组恢复为空数组，而时长仍为 0。这个非空判断便跳过真实动画配置读取，服务器得到序列时长 0、无敌时长 3，违反原有充能消费校验。

不是本轮手柄映射修改造成的输入冲突。Q／Y 都进入同一 Ultimate 释放链；F6 的服务器权限与充能规则不需要改动。

## 最小修复

将 `DanteUltimateAttack.mainParticles` 标为 `[NonSerialized]`。它是运行时缓存和初始化标记，不能从 Editor 域重载恢复成一个看似已初始化的数组。重载后重新从原动画事件读取完整序列时长 **4.6166666 秒**，保留 **3 秒**无敌时间、两波、起手击退及本地震屏。

不修改按键、Scene、Prefab、充能数量或网络协议，不通过捕获并忽略异常、缩短无敌时间或跳过服务器检查掩盖问题。

## 回归依据

新增 `UltimateEditorLifecycleTests.BootLoadedBeforePlay_ChargeAndUltimateSurviveRepeatedEditorSessions`：在 Edit Mode 打开正式 Boot，再进入 Play Mode、启动真实 Host，通过 PlayerMovement 的 F6／Q 共用入口发出真实 Command，验证充能、释放、两波完成及连接保持；退出 Play Mode 后再次进入，重复完整流程。不注入损坏缓存、不修改正式资产，也不把直接调用这些入口当作实体按键测试。

修复前独立运行两次均失败，Unity 退出码均为 2，异常堆栈与现场一致。第二次额外记录 `cached-particles=0 source-duration=0.000000 protection=3.000000`。证据为 `Logs/UltimateInputFailure/before.xml`、`before.log`、`before-repeat.xml`、`before-repeat.log`。

此前 PlayMode 夹具是在域重载完成后才加载 Boot，因此没有覆盖这个人工启动顺序；本次保留这些原测试并补上 Editor 生命周期测试。

修复后，Unity 6000.3.17f1：

| 验证 | 结果与证据 |
|---|---|
| Editor 生命周期及原资产配置 | **13/13 PASS**，`Logs/UltimateInputFailure/after-editor.xml`、`after-editor.log`。同一用例连续两次进入 Play Mode 均记录 `cached-particles=null source-duration=4.616667 protection=3.000000`，实际两波完成且 Host 保持连接。 |
| 原 Ultimate／Dash PlayMode 回归 | **31/31 PASS**，`Logs/UltimateInputFailure/after-playmode.xml`、`after-playmode.log`。覆盖 Native 双波、原动画时序与震屏回调、F6 权限及重复请求、键盘映射、充能／释放／表现恢复、起手击退与 Dash。 |

两组均无失败或跳过，进程退出码均为 0；没有放宽充能消费参数校验。此次未重跑独立构建多进程矩阵或实体按键操作，实际画面仍按下列步骤人工复核。

## 人工复核

1. 等待脚本编译结束，退出 Play Mode，打开正式 Boot，再进入 Play Mode 并启动 Host。完成选卡，保持 Game 窗口焦点。
2. 按 F6，确认服务器日志为 `granted`；再次按 F6 应为 `already-charged`。
3. 按 Q 或手柄 Y，观察原两波攻击、震屏和击退；连接保持正常。再次 F6 应可补充已消费的充能，活动序列结束前不可重叠释放。
4. 退出 Play Mode，再从 Boot 重复。不能只在同一次 Play Mode 内 Stop／Start 代替域重载验证。

发生过原异常的会话已经断线，不能直接在该现场继续按 F6 验证修复。独立构建若需包含此修改，需重新构建。

## 回退

独立回退本次缓存标记与新增回归，保留既有 F6、Q／Y、Dash 和方向映射。回退后上述 Editor 加载顺序会重新暴露原异常。
