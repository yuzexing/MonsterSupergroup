# Ghoul 技术验证归档

[实施与验收报告](../../limbo-ghoul-integration.md)说明来源、适配差异、逐项记录及已知失败。

- `verification.json`：来源/最终构建哈希、首局完成统计、GUI 重开 ID、测试结果及限制。
- `evidence-files.json`：证据包每个文件的相对路径、字节数和 SHA256；不包含它自身。
- 完整包：`Logs/LimboGhoulEvidence-20260915.zip`。包含全部 20 个 Ghoul 运行目录（含失败/重跑）、截图、测试 XML、日志、来源/适配配置及资源清单。构建独立保留。
- 包校验值：`Logs/LimboGhoulEvidence-20260915.sha256.json`，在包外记录以避免自引用。

技术运行包含明确记录的辅助，不是玩法压力验收。广域回归 16 项失败尚未解决，不能把专项通过写为全项目通过。原始证据和历史失败保持不变。
