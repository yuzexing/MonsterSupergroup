# Ghoul 动作观察入口

`5-Ghoul-Motion.cmd` 启动约 65 秒的单人 Host 观察局，维持一只 Ghoul。请手动移动，让它从你的右侧接近，再靠近观察三连击。

重点看：

- 出生后是否正确朝向并播放移动动画。
- 每一击预警时向锁定方向前进，正式挥击时停住；下一击预警重新锁定方向。
- 攻击结束后恢复移动，取消或死亡后没有残留前进。

此入口关闭玩家武器、生命低于 300 时补血；若产生选卡则选择第一项。没有自动走路、定位、跳时和自动截图。它用于观察动作，不作为难度或压力测试。

完整无辅助流程仍由 `1-Solo.cmd` 启动；`2-Host.cmd` 和 `3-Join-Local-Host.cmd` 是完整流程的本机双端入口。

短局日志位于 `TechnicalRuns/<时间>/<角色>`，反馈时可直接压缩对应目录。完整人工局仍使用 `4-Archive-Latest-Logs.cmd` 归档。

如需手动双端观察 Ghoul，在包目录中分别运行：

```powershell
./Start-Technical.ps1 -Profile ghoul-motion -Role host -WaitFor 2 -LogDetail light
./Start-Technical.ps1 -Profile ghoul-motion -Role client -WaitFor 2 -LogDetail light
```

Host 和 Client 使用同一修复版本。本次增加了预警步进的交接字段，不与旧版本混用。
