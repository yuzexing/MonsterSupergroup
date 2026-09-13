# Nordic Midgard 静态样板

打开 `Assets/_Project/Scenes/NordicStaticSample.unity` 后直接 Play。WASD／方向键移动，R 回到出生点。使用 Nordic Axeldor 原始美术及 Idle／Walk 动画，外层缩放为 1，导出 Prefab 待机网格高度为 2.148753 世界单位。

## 地图和运行

横向四屏、纵向四屏，以 1920×1080、透视 FOV 80、Z=-10 换算为 **119.3386×67.1280** 世界单位。Ground 中心 `(0,0.99)`，边界约为 X ±59.6693、Y -32.5740～34.5540。窗口变化不改变地图尺寸。ProCamera2D 在 LateUpdate 跟随，样板随后约束实际视野；极端宽屏使用留边视口，不改变 FOV 或距离。

地图已保存到 `Assets/_Project/Content/Nordic/NordicStaticMap.prefab`。运行不生成或回收地图块，也不访问 Nordic 源目录。四边非触发 BoxCollider2D 位于 Obstacles 层，内侧与 Ground 对齐，四角重叠封闭。角色使用脚底碰撞体、扫掠和滑动；出生／重置寻找最近的无阻挡网格位置，不删除装饰。移动保持每帧同步刚体与显示位置。

## 原规则和静态适配

`MidgardBuildConfig.json` 保存当前 Midgard 的基础地面、四条贴片配置、九组环境参数、35 条非季节装饰引用和来源哈希。默认种子 20260913。`MidgardLayout.json` 保存噪声偏移、资源路径、Chunk／环境组、坐标、缩放、颜色及未出现资源。

- 原点对齐的 10 单位 Chunk 覆盖 Ground，外围再加一圈；每块四张雪地，局部间隔为 4。
- 使用 `Clamp(PerlinNoise(x/2.6+xRand,y/2.6+yRand),0,0.999)`；贴片取第一条匹配，装饰取全部匹配组，采样步长为 1。
- 仅同组、本块和相邻八块参与间距拒绝，距离平方小于等于配置值平方时拒绝。跨组允许重叠，不额外执行图像或物理避让。
- 每轴正向扰动 0～0.5；根缩放 1～1.25，canRotate 在此路径控制水平镜像。组合内部变换保持来源值。
- 地面保留微小 Y 偏移和最终 ±1 翻转缩放；原代码较早写入的 `_scale=2` 被后续赋值覆盖。

烘焙按 Chunk X、Y、采样 X、Y 和源数组顺序执行，保存并恢复 Unity 随机状态。这是有限区域的确定性适配，不是原游戏某一局的随机序列或协程调度复刻。实体按资源类型组织，逻辑分块记录于清单，运行不需要对象池。祭坛、成长条件和事件结构生成不纳入本次。

本次包含 140 个缓冲后 Chunk、560 张底图、736 张贴片、3622 个装饰组合。按根位置统计，Ground 内为 2056 个组合、34 种类型；包含缓冲区时 35 种均自然出现，Ruin_5 只出现在缓冲区。更换种子不保证全部出现，不自动补放。详见 [布局统计与来源对照](nordic-static-sample/rule-recovery-summary.json)。

## 排序和资源

`NordicRenderer2D.asset` 采用原版 CustomAxis `(0,1,0)`，追加到 UniversalRP 的渲染器列表，仅样板相机选择；默认索引 0 和原 Renderer2D 保持原样。取消旧 Y 编号及人为添加的草花 SortingGroup，恢复源 Pivot／Center、原 SortingGroup 边界和静态部件排序。原版通过可见性回调启用的组在样板中直接启用。Axeldor 保留已验证的部件层次与脚底包装节点。

基础地面及贴片恢复 Floor.prefab 的 Center 排序点。Numeric Boundaries 的自动尺寸覆盖从样板中解除，仅保留位置限制；窗口变化时在跟随前刷新视野缓存。极端宽屏给视口两边各预留一个像素，按实际视口射线限位，避免像素取整把画面推出 Ground。

雪地、贴片分别使用 BackgroundBack、Background；源低层装饰映射 BackgroundFront，主体映射 Props。保持 Linear、标准 URP Sprite-Lit／Unlit、环境光 0.9，以及简化火把动态。不恢复树冠淡出、轮廓、遮罩或原版最终色调。

保留必要图集容器以维持原网格和 UV，没有复制完整资源包。无原游戏脚本、DLL、掉落或破坏行为。资源对照记录在 `Logs/NordicStaticSample/source-art-comparison.json`，核验节点变换、Sprite 颜色／翻转／排序点及碰撞数据。

## 工具和验收

项目工具中心：`sample.nordic-create` 重建，`sample.nordic-validate-imports` 资源检查，`sample.nordic-validate` 场景检查，`sample.nordic-sorting` 保留布局并重绑排序；`build.player` 选择 nordic 配置；`test.nordic` 执行独立程序验收。写入操作沿用工具中心的 Apply 参数。

在 MonsterSupergroup 项目目录运行：

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId sample.nordic-create -Apply -Unity 'D:/RealSoftware/6000.3.17f1/Editor/Unity.exe'
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile nordic -Unity 'D:/RealSoftware/6000.3.17f1/Editor/Unity.exe'
./Tools/Invoke-ProjectTool.ps1 -ToolId test.nordic
```

重新导入命令：`Tools/Import-NordicStaticSample.py --source <Nordic导出工程> --stage full`，随后重建样板。重建会替换样板生成内容。此次变更前备份位于 `Logs/NordicStaticSample/BeforeRuleRecovery-20260913-140830`。

独立程序：`Builds/NordicStaticSample/NordicStaticSample.exe`。只有传入 `--nordic-validate=<输出目录>` 才进行验收并退出。也可调用兼容脚本 `Tools/Run-NordicStaticSampleValidation.ps1`。

运行检查与截图位于 `Logs/NordicStaticSample/Acceptance`，覆盖十六屏区域、四边四角、宽高比变化、大步长阻挡、瞬移、原尺寸角色、朝向、60／120 fps 运动、重叠稳定性、树根碰撞和火把动态。树和火把使用临时验收实例，完成后删除，不向地图补放；测试火把 Prefab 仅复用已导入依赖。

依据为原配置、Prefab 和 GameAssembly 中已定位函数的静态分析；原始证据位于 Nordic 导出工程的 `Docs/SceneAnalysis`。未执行原游戏 DLL，不声称逐像素匹配原版或恢复旧游戏局面。

## 本次验收结果

2026-09-13，Unity 6000.3.17f1 / Windows / D3D11 独立程序通过 **117 项检查，0 项错误**。含缓冲区共 17385 个地图 SpriteRenderer、38 套粒子和 76 个火把灯光。三个重叠区域的像素对照均为 0 次变化；60／120 fps 运动采样均无停帧位移或镜头相对倒跳。全部十六屏区域和边界巡检无露底。

已逐项确认正式 Gameplay、默认 Renderer2D、默认构建场景和 Sorting Layers 与变更前一致；UniversalRP 仅追加一个渲染器引用。资源小样、同种子重复生成、负坐标相邻块间距和源数据对照均通过。

- [运行检查结果](nordic-static-sample/acceptance.json) / [布局、依赖对照与性能采样](nordic-static-sample/rule-recovery-summary.json)
- [中央视野](nordic-static-sample/sample-center.png) / [全图远景](nordic-static-sample/layout-overview.png)
- [树后遮挡](nordic-static-sample/tree-behind.png) / [树前显示](nordic-static-sample/tree-front.png)
- [边界角落](nordic-static-sample/boundary-corner.png) / [极端宽屏的有效视野](nordic-static-sample/extreme-active-viewport.png)
