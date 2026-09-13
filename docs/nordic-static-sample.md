# Nordic Midgard 静态样板

打开 `Assets/_Project/Scenes/NordicStaticSample.unity`，直接进入 Play 即可查看。
使用 WASD 或方向键移动，R 回到起点。样板使用 Nordic 默认角色 Axeldor 的原始头、身体、手脚组合，以及原始待机、行走动画；没有引用 MonsterSupergroup 的角色美术，不实例化正式玩家，也不启动联机与战斗。

角色按 Nordic 导出 Prefab 的原始尺寸展示，外层缩放为 1，待机起始姿态的网格高度约 2.15 个世界单位（排除脚下阴影）。外层节点只调整脚底对齐和左右朝向；原始部件变换与动画路径保持对应。角色随待机、行走姿态略微起伏。此尺寸依据导出 Prefab，还未核验原游戏运行时是否另有缩放。

角色视觉来自 `Viking (Character Variant).prefab` 的 Scaler、Shadow 和 PlayerSprite 子树；不导入采集器、HUD、战斗控制或战斗特效。两个动画只移除原脚步/音效事件，通过样板专用 Idle / Walk 控制器切换，不依赖原版角色脚本。

## 固定配置

- Ground：64×40，中心沿用 Gameplay 的 `(0, 0.99, 0)`；其透明 SpriteRenderer 提供边界，雪地是独立视觉层。
- 相机：透视、FOV 80、Z=-10、旋转为零。ProCamera2D 跟随预览角色，Numeric Boundaries 读取 Ground.bounds；不启用变焦、震屏。
- 镜头重置和窗口变化后，样板组件按实际视野再做边界约束，避免瞬移绕过插件的移动增量限制。
- 1920×1080 时，Z=0 平面可见高度约 16.782、宽度约 29.835。
- Linear 色彩空间；标准 URP 2D Lit / Unlit 材质；白色环境光强度 0.9。没有复制原版占位 Shader，没有新增后处理要求。
- 地面使用原始 PPU、网格与贴图。雪地以 5.8 单位步长叠铺，向边界外多铺；泥地、碎石作为固定贴片。
- 地图实例及排序保存于场景和 `Assets/_Project/Content/Nordic/NordicStaticMap.prefab`，运行时不生成地图。

| 内容 | Sorting Layer |
|---|---|
| 雪地 | BackgroundBack |
| 泥地、碎石 | Background |
| 草花 | BackgroundFront |
| 角色、树石、遗迹、火把主体 | Props |

高物件通过根部 SortingGroup 排序，以 `-RoundToInt(脚底/根部世界 Y × 100) × 8 + 稳定槽位` 确定次序。角色使用槽位 0，同一脚底行的静态高物件使用不同的槽位 1～7，避免同值时再次按透视镜头距离排序。预览角色实时更新；静态物件数值保存到场景。源 Sprite Pivot 和组合局部变换不变。没有改变共享玩家 Prefab 或全局排序层。

每个草花组合也有独立 SortingGroup。组合内先保留原有不同排序值的先后关系，再按固定的根部 Y 和层级顺序消除并列值；Axeldor 则按阴影、腿、后手、身体、头、前手明确排序。移动镜头不再参与决定这些部件之间的先后。

预览角色采用每帧 Update 移动并执行静态障碍碰撞查询，同一帧同步刚体位置与显示 Transform，镜头继续在 LateUpdate 跟随。原先 FixedUpdate 的 50 Hz 位移没有插值，配合每帧平滑镜头会产生相对倒跳；仅更新 Rigidbody2D.position 仍会让显示位置等待物理帧同步，所以两者必须一起更新。这个无动力学交互的预览不需要把镜头移到 FixedUpdate，也没有修改项目的物理步长。Axeldor 的源部件已经带有翻转，正 X 缩放表示面向左，负 X 缩放表示面向右。

## 构建与资源来源

Unity 菜单 `Tools > Nordic` 提供资源校验、重建场景、场景校验和独立预览构建入口。重建场景会重写本样板的场景和地图 Prefab；若已手动编辑，请先另存场景或在版本控制中保存修改。

调整布景后，可使用 `5. Refresh Sample Sorting (Keep Layout)` 重新保存稳定排序，同时保留物件坐标和当前角色尺寸；这也会更新地图 Prefab。同一 0.01 单位脚底行若放置超过 7 个静态高物件，会明确报错，请调整根部排序锚点。

`Assets/_Project/Content/Nordic/ImportManifest.json` 记录源路径、目标路径、GUID 映射、贴图/精灵来源哈希，以及各 Prefab 移除的组件类型。内容包括 Midgard 三种地面、35 条非季节装饰引用和 Axeldor 基础外观、两个动画的视觉依赖；共享资源按引用去重。角色条目单独记录为 character，不计入 35 条地图装饰。

本次共迁入 41 个 Sprite、8 张必要贴图，未生成衍生贴图。源 Sprite 直接引用打包图集，保留被引用的图集纹理容器以维持原网格和 UV；没有复制其他地图、其他角色或整套项目资源。地图组合 264 个节点加角色视觉 13 个节点的局部位置、旋转、缩放均与源数据一致。

需要从 Nordic 导出工程重新导入时，使用 Python（需安装 PyYAML）：

```powershell
python Tools/Import-NordicStaticSample.py --source F:/DecomplieLatest/NordicAshes/ExportedProject --stage full
```

先用 `--stage smoke` 可导入雪地、Tree_1 和 Composition_Grass_1；随后调用编辑器方法 `MonsterSupergroup.NordicSample.Editor.NordicStaticSampleBuilder.SmokeCheck`，生成 3×3 拼接检查图。完整场景构建要求恢复 `--stage full`。

导入器只重写自己拥有的 GUID；目标路径存在其他资源时直接报错。原脚本、DLL、地图配方管理器、季节变化、掉落、破坏与遮罩行为不迁入。运行已生成场景不再读取 Nordic 源目录。

## 验证

先从菜单构建独立预览，然后执行：

```powershell
./Tools/Run-NordicStaticSampleValidation.ps1
```

检查结果和截图位于 `Logs/NordicStaticSample/Acceptance/`。自动验证包含视野、四边四角、窗口比例变化、移动速度、Nordic 待机/行走与朝向、树前后排序、树根碰撞、火把动态、地面露底像素与静态实例数量；地面检测先用空画面确认能识别露底，再检查实际场景。截图另外用于人工检查拼缝、素材比例与遮挡。

独立程序位于 `Builds/NordicStaticSample/NordicStaticSample.exe`。普通启动供手动查看；只有显式传入 `--nordic-validate=<输出目录>` 才会执行自动巡检并退出。

2026-09-12 在 Unity 6000.3.17f1 / Windows 独立程序中完成 78 项运行检查，错误数为 0。场景有 1023 个 SpriteRenderer、39 个排序锚点、5 套持续粒子和 10 个火把灯光。检查涵盖 1920×1080 与 1440×1080 两种窗口尺寸，以及 60 / 120 fps 两种帧率；没有声称验证所有分辨率或设备。

原来的 64 项检查没有覆盖移动镜头时的重叠部件跳变和逐帧倒跳。现在新增渲染区域对齐比较、组合及根部排序冲突检查、实际左右朝向截图、60 / 120 fps 连续运动检查。详见 [这次修复的原因与对比数据](nordic-static-sample/render-fixes.md)。

验收存档：

- [运行检查结果](nordic-static-sample/acceptance.json)
- [中央视野与 Axeldor](nordic-static-sample/sample-center.png)
- [角色在树前](nordic-static-sample/tree-front.png) / [角色在树后](nordic-static-sample/tree-behind.png)
- [向左移动](nordic-static-sample/facing-left.png) / [向右移动](nordic-static-sample/facing-right.png)
- [遗迹与火把](nordic-static-sample/ruins-torches.png)
- [全图布局](nordic-static-sample/layout-overview.png)（更新排序后的编辑器远景；运行镜头仍为 Z=-10）

## 表现边界

这是按原美术重新构图的固定布局，不是某一局 Nordic 地图的坐标复原。使用 Linear 和标准材质，颜色不以原版逐像素匹配为验收条件。只实现前后排序，不实现树冠淡出、轮廓或原版遮罩。可破坏物保留静态外观；火把采用简化动态，不恢复原材质行为。

正式 Gameplay、默认构建场景、全局排序层、渲染管线和现有玩家流程均不需要本样板的修改。

项目已有的全局 GameOptionsService 和 URP 调试服务仍可能自动初始化；它们不代表样板启动了网络会话或正式玩家。样板没有新增这类全局初始化，也没有修改其设置文件。
