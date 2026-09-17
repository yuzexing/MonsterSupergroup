# Phase 1：火焰遮挡人工验收

本包只修复 B／围栏共享火焰的排序；音效与血瓶／经验框架分别留到 Phase 2、Phase 3。

- `6-Fire-B.cmd`：约 12 秒的 B 出生预警短局，仍包含原配置的出生敌人。
- `7-Fire-Barrier.cmd`：围栏短局，观察入场、收缩和退场。
- `1-Solo.cmd`：完整无辅助 Full 流程，使用同一份修复资源。

短局仍经过 Boot→准备房→Gameplay→Mirror Host。手动移动，无自动走路、补血、强制暂停或定时截图。短局只用于观察，不作为正常关卡压力结论。

请分别站在火焰的上方与下方，观察人物、附近植物与火焰的前后关系，再通过结算界面重开一次：

1. 火焰主体不再无条件被人物／植物盖住，而是参与世界前后排序。
2. 两层地面光晕仍在人物与火焰主体下方。
3. 火焰位置、形状、围栏收缩和碰撞没有随排序修复改变。
4. 重开后的分层与首次运行一致。

合理的前后遮挡是预期行为；本修复没有把危险提示强制画在所有物体上方。如短局中心附近没有植物，可在 Full 出现火圈时补看该项，无需反复运行完整流程。

日志在 `TechnicalRuns/<时间>/<角色>`；反馈版本、观察入口、问题描述即可，有问题时附该目录或截图。

可选双端观察（同版本，两条命令分别执行；两个窗口手动移动）：

```powershell
./Start-Technical.ps1 -Profile spatial-b -Role host -WaitFor 2 -LogDetail light
./Start-Technical.ps1 -Profile spatial-b -Role client -WaitFor 2 -LogDetail light
```

围栏使用 `-Profile spatial-barrier`。画面结果由人工确认，自动化测试通过不代表人工遮挡验收已完成。
