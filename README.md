# MonsterSupergroup

Unity 版本以 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) 为准，当前为 **6000.3.21f1**。本项目使用 **Git LFS**；仅获得 Git 提交中的指针文件无法运行。

首次使用、换电脑及插件报错请先阅读 [克隆、检查与故障恢复](docs/project-checkout.md)。

```powershell
git lfs install
git clone https://github.com/yuzexing/MonsterSupergroup.git
cd MonsterSupergroup
git lfs pull --include="" --exclude="" origin
./Tools/Invoke-ProjectTool.ps1 -ToolId validate.checkout
```

检查通过后，用匹配版本的 Unity 打开项目。启动场景为 `Assets/_Project/Scenes/Boot.unity`；首页进入准备房间，再开始 Gameplay。Editor 和 Development 包支持首页的本机 KCP 建房与加入。

人工制作、校验、构建与自动验收统一入口见 [EditorTool 使用指南](docs/editor-tools/README.md)。
