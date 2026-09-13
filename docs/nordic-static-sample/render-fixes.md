# 重叠排序、朝向和跟随抖动修复

本次修复只作用于 Nordic 样板。保留 FOV 80、Z=-10、Axeldor 原 Prefab 的 1 倍缩放、64×40 地面和原有布局。

## 成因与处理

1. **重叠部件切换先后**：许多部件具有相同的 Sorting Layer 和 Order。当前 URP 2D Renderer 在透视相机下会继续使用镜头距离排序，镜头移动就可能改变相同排序值的前后关系。现在对草花组合使用 SortingGroup，给组合内部赋予明确顺序；高物件按脚底 Y 排序，并为同一高度分配稳定槽位。角色的头、身体、手脚也采用明确的内部次序。
2. **左右朝向反转**：原 Axeldor 多个 SpriteRenderer 自带 flipX，正缩放的组合朝左。样板原本假定正缩放朝右，符号用反；现在左移保留正 X，右移使用负 X，保留每个部件的原始翻转。
3. **移动时看起来模糊**：角色原先在 50 Hz 的 FixedUpdate 中直接改变刚体位置，镜头却每帧平滑移动，造成角色相对于镜头周期性倒跳。该预览只使用运动学刚体和静态碰撞查询，因此改为 Update 中完成位移与碰撞，并同步 Rigidbody2D 和显示 Transform，随后由 LateUpdate 跟随。仅移动 Rigidbody2D.position 仍会残留显示位置同步延迟；本次回归检查发现并修复了这一点。

镜头保留 LateUpdate；全局物理步长、Renderer2D 配置和材质采样方式保持原设置。

Unity 的 [2D 排序规则](https://docs.unity.cn/6000.0/Documentation/Manual/2DSorting.html) 描述了透明物件的距离排序；当前安装版本的具体选择逻辑位于 `RendererLighting.GetTransparencySortingMode`。物理更新与显示更新节奏差异也见 [Rigidbody 插值说明](https://docs.unity.cn/Manual/rigidbody-interpolation.html)。

## 实际验证

新版独立程序的 [78 项运行检查](acceptance.json) 全部通过。新增测试分别验证实际渲染、方向和每帧位置，不只检查配置值。

| 检查项 | 修复前 | 修复后 |
|---|---:|---:|
| 静态高物件排序冲突 | 19 | 0 |
| 场景组合内部排序冲突 | 650 | 0 |
| 同一花草区域随镜头移动的明显变化像素 | 2,859 | 0 |
| 60 fps / 60 个采样帧中的位置停顿 | 10 | 0 |
| 60 fps / 60 个采样帧中的镜头相对倒跳 | 10 | 0 |
| 120 fps / 120 个采样帧中的位置停顿 | 70 | 0 |
| 120 fps / 120 个采样帧中的镜头相对倒跳 | 70 | 0 |

花草像素对比将镜头沿 Y 移动恰好 512 个渲染像素，再比较同一世界区域对应的 128×128 像素。RGB 单通道差值超过 6/255 才计为明显变化，避免把浮点取样误差当成排序问题。另对草丛、花丛和可破坏物组合的完整可见区域执行渲染比较，均通过。

运动测试在中央空地持续向右移动，热身后分别采样 60 / 120 帧。修复后每帧世界坐标速度均为 5，速度最大误差小于 0.0001；没有超过 0.2 像素的反向屏幕位移。前后比较使用倒跳次数和世界坐标速度，不把不同系统窗口下的绝对像素幅度当作结论。角色原动画自身的呼吸与摆动继续保留。

原始记录：

- [60 fps 修复前](motion-before-60.csv) / [修复后](motion-after-60.csv)
- [120 fps 修复前](motion-before-120.csv) / [修复后](motion-after-120.csv)
- [布局几何与同区域像素对比](render-fix-comparison.json)：场景 2,124 项、地图 Prefab 2,083 项几何属性保持一致，浮点容差为 0.000001。
- [向左移动截图](facing-left.png) / [向右移动截图](facing-right.png)

以后手动调整布景时，使用 `Tools > Nordic > 5. Refresh Sample Sorting (Keep Layout)` 重算稳定顺序；不需要重新生成整张地图。
