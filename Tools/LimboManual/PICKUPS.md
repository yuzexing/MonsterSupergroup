# 经验与血瓶候选版

- `1-Solo.cmd`：正常 Full 人工试玩，无本轮测试辅助。
- `10-Pickup-Observe.cmd`：空场，手动生成经验或血瓶、扣除 300 HP、补满生命、开关拾取与暂停。普通武器暂时关闭。
- `11-Pickup-Drops.cmd`：空场，可生成八个普通敌人，用正常武器击杀；采用实际掉落概率，不保证某一批必出血瓶。

经验领取成功时结算，飞行是展示。血瓶先退后 0.3 秒、飞入 0.5 秒，到达后最多恢复 200 HP；满血不拾取。飞行中满血或倒地会返回地面。地面和领取中的血瓶共用四瓶上限。

双端观察用同版本包，各开一次：

```powershell
.\Start-Technical.ps1 -Role host -WaitFor 2 -Profile pickup-observe -LogDetail light
.\Start-Technical.ps1 -Role client -Profile pickup-observe -LogDetail light
```

只有 Host 能生成共享物品；每端的受伤、回血按钮作用于自己的玩家。以上属于显式测试辅助，不用于判断玩法压力。Full 入口没有这些按钮。

请确认血瓶身体、粒子、飞入节奏及声音是否正常；留意满血时旁边的经验仍可拾取。记录版本、运行编号、问题时间及现象。日志在 `TechnicalRuns`；用归档入口打包。无需战斗中截图或记笔记。

本轮不含金币、磁铁、终极拾取物、精英宝箱或屏外 XP 合并。Phase 2 听感、火圈收缩连续性仍独立验收。
