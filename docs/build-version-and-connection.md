# 构建版本与联机提示

2026-09-22：日常／专项页面、七个配方及旧 ID 的最新使用方式见 [统一打包指南](build-guide.md)。版本和联机规则继续沿用本文。

## 日常操作

Unity 菜单：**MonsterSupergroup → 构建与验收 → 构建配置…**。

游戏版本只读取 `PlayerSettings.bundleVersion`，本次起始值为 `0.0.0`。选择更新类型只预览；点击“应用版本更新”才写入。普通小更新将末位加一，重大更新将中位加一并清零末位，大版本更新将首位加一并清零其余两位。构建不会自动递增。

构建类型和网络模式分别选择。Dev 默认开启 Development，Test 默认关闭但允许显式调整，Shipping 必须关闭。日常默认 product／Test／Steam／Steam 分发／普通日志；场景和辅助能力由配方与用途解析，不由版本后缀切换网络。

命令行示例（在项目目录运行）：

```powershell
./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile product -BuildKind Test -Network Steam -Distribution Steam -Diagnostics Normal
```

不允许 Unity 默认 Build 按钮或脚本直接绕过项目构建服务，也不允许 ScriptsOnly。失败必须修正后重新完整构建。

## 包的身份

每次实际构建生成独立 UTC 时间加随机码 BuildId。游戏右下角、启动日志、包内 `<EXE名称>_Data/StreamingAssets/BuildInfo.json` 使用同一份快照，例如 `v0.0.0-test · <BuildId>`。Editor 明确显示 Editor，不代表某个已构建包。

快照包括 Git 提交和 dirty 状态、Unity 版本、UTC、平台/架构、实际 Development 标志和构建配置。修改项目中的配置不会改写旧包。包内信息缺失或与实际 Player 标志不符时禁止联网，请重新安装完整包，勿只替换 EXE 或程序集。

只有成功完整构建才写入根目录 `build-complete.json`。归档使用包内快照，不采集当前工作区替代旧包信息：

```powershell
./Tools/Export-ProjectBuild.ps1 -BuildDirectory '完整构建目录'
```

输出包含完整包、逐文件哈希清单、ZIP 和 ZIP 的 SHA256；冻结归档不允许覆盖。历史 Limbo 标签与游戏版本分开，启动参数不能覆盖游戏版本。

## Shipping

Shipping 要求有效 Git HEAD，源码、资源及配置工作区干净。版本更新也须先提交。工具不会自动提交或清理用户改动。检查失败时会列出阻塞文件。

Shipping 只允许产品配方、Steam 网络和 Steam 分发；专项、KCP、本地直启和故障取证不能组合为 Shipping。普通产品 Test 同样没有作弊和机制验证入口，Development 覆盖不会改变这一点。

Shipping 禁止测试程序集、验证编译符号和 Development；调试面板、原型启动器、命令行机制验证及战斗辅助还在执行入口受构建类型的编译配置限制。修改 JSON 或用户调试设置不会解除限制。运行错误和必要启动日志继续保留。

## 联机提示

- 游戏版本按三段整数比较；构建类型及 BuildId 不用于拒绝。协议标识和敌人配置指纹仍须一致。
- 本地版本较低或房主版本较低分别提示双方版本。Steam 用户退出游戏后更新，未出现更新时可重启 Steam；KCP 不显示 Steam 专属说明。
- 缺失或非法版本无法推测高低；同版本协议/资源不同也不能直接联机。
- 主机关闭、已连接后意外断开、尚未连接成功、本地主动离开分别处理。版本/资源拒绝及明确主机关闭原因优先保留，不被后到的通用断线覆盖。
- 重新开始连接会清除旧提示；旧连接尝试的延迟通知不能影响新房间。

排查请同时提供双方的 BuildInfo、Player 日志及具体加入方式（列表/邀请/重连）。仅提供相同版本号不足以确认内容一致。

## 验收边界

实际结果见同目录的 `build-version-validation.md`。真实 Steam 两账号、最终文字排版与点击操作由人工确认；KCP 和编辑器测试不能替代 Steam 核验。此改动不包含主机迁移、自动重连或新内容/存档协议管理。
