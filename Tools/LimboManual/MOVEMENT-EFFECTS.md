# 移动显示与平面特效专项

`14-Enemy-Movement.cmd` 为短时敌人移动对照。可切换 60/144 FPS、旧版无插值与修复后插值；Host 可切换敌人和变体。先站立，再正常移动，观察身体轮廓、动画与位置。

该入口显式禁止玩家武器，低于 300 HP 时补血；生成使用来源基础属性、固定出生移速倍率 1。不是压力测试。记录物理位置、渲染位置、相机、帧时间和模拟角色，最多 14000 帧，不自动截图。

同版本双端可以分别运行 `Start-Technical.ps1 -Profile movement-observe -Role host -WaitFor 2` 和 `-Role client -WaitFor 2`。Host 控制生成；插值比较仅影响当前端承担模拟的敌人。观察端沿用现有网络快照处理。

完整无辅助 Full 继续使用原单人／双人入口。画面仍需人工确认，不将技术检查解释为重影或压力人工验收通过。

`15-Weapon-Effects.cmd` 为纯展示入口，可依次切换烈焰之笔、鬼火、吐息、环绕、轨迹、召唤和终极技能，以及元素、八方向和大小。此入口停用正常武器，不执行展示特效的伤害，不自动截图。召唤点击“next attack phase”推进充能、光束及退出，身体模型保持原样。轨迹按钮显示一段明确的测试轨迹，不等同于真实冲刺。

建议先站立观察同一方向，再转到相反方向；特效原有 XY 形状与非对称美术保留，并不保证所有方向外形相同。正常结束后再点 Replay，对照复用；Cancel 检查残留。双端可使用 `Start-Technical.ps1 -Profile effects-observe -Role host -WaitFor 2` 与相同命令的 `-Role client`，两端各自控制本地纯展示。

专项日志分别为 `enemy-motion.jsonl` 和 `weapon-effects.jsonl`，保存在 TechnicalRuns。完整 Full 仍保留当前音效修复、镜头、数值和波次。
