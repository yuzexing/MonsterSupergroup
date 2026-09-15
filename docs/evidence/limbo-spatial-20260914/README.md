# 空间机制后台验证证据

- `verification.json`：来源哈希、31 片段静态匹配结论、自动化结果及未通过的画面门槛。
- `spatial-edit-release.xml`：30 项 EditMode 通过。
- `spatial-play-final.xml`：17 项 PlayMode 通过；包含围栏原生收缩、粒子退出、暂停取消，以及现有机制回归。
- `spatial-build.log`：后台构建成功记录。
- `build-files.json`：当前 Host 与 Client 相同的 489 个文件及 SHA-256。
- `spatial-preserved-before-build.json`：重复资源生成前的适配／来源及 Prefab 哈希；生成后逐项一致。

以上是后台验证。本批没有新的空间机制画面、Host／Client 实玩或原游戏压力对照；Full 保持禁用。阶段二实际运行证据另见 `../limbo-stage2-20260914`。
