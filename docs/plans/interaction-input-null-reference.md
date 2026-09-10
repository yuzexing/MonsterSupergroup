# Boot→Gameplay 交互按键空引用

## 定位（2026-09-11）

报告的手柄 B 调用链：`InputHandler.Button1 → PlayerController_HMD.Button1 → BoundPlayer.Interact`。

正式 `NetworkPlayer.prefab` 第 278 行将 `interactionFinder` 配置为 `{fileID: 0}`。已有 `Interactor` 只提供交互主体 Transform，与负责搜索和执行交互的 `Interaction2DFinder` 不是同一组件。

`PlayerMovement.Interact()` 原本只检查选卡锁定，随即对空 finder 调用 `TryInteract()`。键盘 E 与手柄映射到 Action 0 的输入都可以触发。该代码和空引用配置在 `524805b` 的键盘修改前已经存在。

## 修复

`Interact()` 的原 guard 增加 `interactionFinder == null`。使用 Unity 对象空值判断，未配置以及已销毁的 finder 都不再调用；正常 finder 和已有选卡锁定保持原行为。没有给正式玩家补挂旧交互系统，也没有改变 Scene、Prefab 或手柄映射。

## 修复前后证据

Unity 6000.3.17f1，正式 Boot 启动 Host。测试取得实际 Owner 和当前 InputHandler，将 `ButtonJustPressed` 交给原回调，未人为清空 Prefab 引用。这是回调级自动测试，不是实体手柄按键测试。

| 用例 | 修复前 | 修复后 |
|---|---|---|
| `Host_InteractionButtonWithoutOptionalFinderDoesNotThrow` | 失败，完全相同的 `PlayerMovement.cs:1253 → PlayerController_HMD.cs:239 → InputHandler.cs:202` 空引用 | 连续三次交互均无异常 |
| `InteractionKeepsConfiguredFinderAndSelectionLockButIgnoresDestroyedFinder` | 配置正常时可交互且选卡阻止交互，但销毁 finder 后还调用了旧目标，次数为 3，期望为 2 | 正常调用/选卡限制有效，销毁后次数保持 2 |

修复前 `Logs/InteractionInput/before.xml`：2 个用例均失败，Unity 退出码 2。保留同一断言，修复后 `Logs/InteractionInput/after.xml`、`after-run.log`：**24/24 PASS**，Unity 退出码 0。覆盖上述两项、Boot 场景生命周期、玩家运行时边界、上一轮输入/Ultimate 和选择锁定。

第一次修复后启动在测试诊断代码中使用了错误大小写 `controllers.joysticks`，编译失败；按已安装 Rewired API 改为 `controllers.Joysticks` 后完成上述回归。未放宽或跳过断言。

## 当前手柄配置

Boot 仅有 `DualAnalogGamepad` 模板的 Normal map。运行时实际识别为 **Xbox One Controller**，读取当前启用的映射得到：

| 设备元素 | Action | 行为 |
|---|---|---|
| A | 0 `Button1` | 交互，键盘对应 E |
| 左摇杆右/左 | 1 `L_Stick_Horizontal` | 水平移动 |
| 左摇杆上 | 3 `L_Stick_Vertical` | 向上移动 |
| 左摇杆下 | 1 `L_Stick_Horizontal` 的负向 | **原有错配：向左** |
| Dash / Ultimate / Debug Charge | 14 / 4 / 51 | **没有手柄映射**；目前只有上一轮增加的键盘 Shift / Q / F6 |

证据见 `after-run.log` 的 `[InteractionInput]` 行，以及 Boot 的 `joystickMaps`。`R_Trigger` 是 Action 名称，不代表已经配置了 RT。

用户报告实体 B 触发交互，堆栈能确认触发了 Action 0，但运行时映射名称是 A。实际设备型号、按键印字/驱动布局是否互换尚待用户补充，不能据此断定标准 Xbox B 被绑定为交互。摇杆向下错配作为独立发现记录，本次修复没有更改按键约定。

人工复核：从 Boot 进入 Gameplay，在未选卡锁定时反复按键盘 E 及设备上触发交互的按钮，Console 不再出现该异常。按下后没有交互效果是当前正式 Prefab 未配置 finder 的结果，不代表新增了交互玩法。实体 A/B 对应关系和向下错配需要分别验证。

回退仅撤销本次 `Interact` guard 与两项测试扩展；保留 Rewired Shift/Q/F6 及此前联机改动。
