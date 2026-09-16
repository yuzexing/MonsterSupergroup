# 平面显示核对（持续补充）

当前画面候选：`Builds/ManualPlayFix20260916-07`。03/04/06 的对照按版本保留；07 相对 06 只修正显式空间验证器的取样，不改变游戏显示和碰撞。以下包含明确辅助的技术局与隔离渲染；不作为人工玩法压力结论。

## 围栏

- 原显示问题：`legacy-barrier-distortion.jpg`（旧版实际游戏窗口）。来源倾斜叠加当前透视，近侧火焰明显放大；不是中心追随玩家。
- 修正后：`barrier-pair03-host.png`、`barrier-pair03-client.png`。同一双端运行在停止阶段均能看到火焰；中心、收缩、碰撞仍走现有网络状态。
- 完整局：`solo4k03-first-barrier.jpg`，画面时间 341.2 秒、实际 3840×2160；屏幕只显示靠近玩家的部分圆弧属于镜头取景。
- `solo4k03-elapsed639.jpg` 是后半段叠加画面，此时没有可见火圈，不能拿它证明第二围栏画面通过。
- `pair72004-host-second-barrier.jpg`（624.4 秒）、`pair72004-client-second-barrier.jpg`（635.0 秒）为第四候选双端实际第二围栏。两端记录中心均固定 `(7.174118,10.966586)`，半径从 20 收缩至约 9.9979；保留来源逐帧终值。火焰与地图相对固定，镜头取景不同，截图不是同一帧的逐像素对比。
- `pair72004-host-first-barrier.jpg` 实际截取在 380.8 秒、第一次围栏已结束；`pair72004-client-late-rusher.jpg` 实际为 498.9 秒，尚未进入 509.65 秒 Rusher。保留文件但明确时间，不能仅凭文件名作为对应机制通过证据。
- `Logs/ManualPlayFix/fire-render/` 是真实火焰资源、NordicRenderer2D、地图背景、FOV 80 的隔离渲染；中心 Y=0/-12，半径 10/20，比较开关火焰前后的实际像素。它不单独证明联机或碰撞正确。

中间候选 02 曾出现 Host 火焰显示边界滞留：对记录中单粒子 Glow，边界中心相对发射点最大偏差约 23.02 世界单位。候选 03 将覆盖限于相机绘制期间，记录中的边界中心与实际粒子世界位置最大差约 0.000002。前者没有采集粒子位置，故两列参考点不同；固定 Glow 的后者说明原生边界继续更新，不是扩大显示边界绕过问题。

## 烈焰之笔与鬼火

`Logs/ManualPlayFix/weapon-directions/pixels.csv` 与图片覆盖烈焰之笔、普通/火/毒鬼火，八方向，大小 1/2。每个姿态先捕获平面材质，再在同一帧切换原材质取对照；不更换粒子状态、命中区或攻击大小。

`weapon-projection-comparison.jpg` 是部分方向的对照汇图，原美术本身的非对称斩击、鬼火尾迹拉伸保留。不能要求上下动画每个像素镜像相等。相同 XY、不同 Z 的标准四边形另由投影检查验证屏幕尺度相同。

`solo4k03-opening.jpg`（33.6 秒）、`solo4k03-elapsed214.jpg`、`solo4k03-elapsed639.jpg` 是实际完整运行画面。武器升级造成的数量和大小变化仍有效，不能把不同等级截图的尺寸差当作投影回归。

## 清理及未完成

`barrier-pair03-ended.jpg`、`barrier-pair03-restart.jpg` 与 `solo4k03-ended.jpg`、`solo4k03-restart.jpg` 对应实际界面操作。日志另核对零伤害区、零预警及新 RunId，不能仅凭结算截图判断无遗留对象。

广域检查 791/791 EditMode、534/534 PlayMode；后续围栏验证器取样定向 3/3。06 的独立 B 两段等待、固定中心与暂停已通过；07 的围栏五阶段暂停和暂停中取消已通过。正常窗口结束与取消的占用、碰撞、粒子清理分别检查，不能用单张图证明生命周期。

07 单人 4K 已完成至 720.90273454 秒；同版本双端在选卡等待后于 729.08669522 秒结算，两者均通过界面重开。按用户要求不追加重复截图矩阵，后续画面由用户人工确认；必要的 Computer Use 必须先说明目的并获得当次确认。未列为通过的组合不视为完成。

## 新候选的补充画面

- `solo4k06-first-barrier.jpg`（342.1 秒）、`solo4k06-second-barrier.jpg`（615.6 秒）：真实 4K 生产镜头，火焰不再因前后深度造成巨大尺度差；中心和碰撞未改。
- `pair72006-host-first-barrier.jpg`、`pair72006-client-first-barrier.jpg`：同版双端第一次围栏；Host 第二围栏另有记录。Client 的 `pair72006-client-elapsed645.jpg` 已在围栏释放后，不作为显示通过证据。
- `barrier07-client-shrinking.jpg`：07 的实际 Client，33.0 秒收缩阶段。`barrier07-cancel-client.jpg` 为取消结算，不能当正常 85 秒完成。
- `b06-client-warning.png`：实际 Client 阶段截图，五处火焰固定阵形；不是编辑器预览。
- `solo4k07-slime306.jpg`：306.2 秒强化骷髅与 Slime；`solo4k07-elapsed397.jpg` 已过第一次围栏，不能证明当时仍有火焰。
- `solo4k07-ended.jpg`、`solo4k07-restart.jpg`：最终单人界面操作；同时由审计确认新 RunId、初始条件及清理。
- `pair72007-host-first-barrier.jpg`、`pair72007-client-first-barrier.jpg` 与 `pair72007-host-second-barrier.jpg`、`pair72007-client-second-barrier.jpg`：最终双端两次围栏实拍；第二围栏约 623.5/632.7 秒，非同一帧。
- `pair72007-client-transition.jpg`：约 724 秒真实选卡等待；结束、重开截图另存，不能混用。重开 Client 截图已到约 52 秒，初始等级由日志确认。
- `manual07-natural-failure.jpg`：无辅助独立包 47.10 秒自然死亡结算，不作为完整通关或玩法压力证据。
