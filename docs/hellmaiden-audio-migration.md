# HellMaiden 音频移植

## 资源与配置

原工程 `F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/StreamingAssets/Desktop` 的 13 个 FMOD 音频库已复制到 `Assets/_Project/Audio/FMODBanks`，共 297,401,424 字节，保留原始内容和事件 GUID。

包含 `Master`、`Master.strings`、`amb`、`boss`、`cin`、`dlg_ENG`、`en`、`env`、`int`、`mx`、`npc`、`plr`、`ui`，可加载 365 个事件。原始 `boss`、`npc` 库没有事件，仍保留原库结构。`dlg_JPN.bank` 与英语库的事件冲突（FMOD `ERR_EVENT_ALREADY_LOADED`），因此只配置英语库。

`FMODStudioSettings.asset` 使用上述相对路径作为预构建音频库来源，启动时自动加载。FMOD 插件负责复制到 `Assets/StreamingAssets/HellMaiden` 并随构建打包；该目录的 `.bank` 和对应 `.meta` 是现有忽略规则覆盖的生成文件。需要版本管理的是 `_Project/Audio/FMODBanks` 内的源文件。

`NetworkPlayer.prefab` 从原工程 `Systems.unity` 恢复以下引用：

| 字段 | 原始 FMOD 事件 |
| --- | --- |
| dashAudio | `event:/sx/plr/Sx_plr_dash` |
| hurtSound | `event:/sx/plr/Sx_plr_hurt` |
| deadSound | `event:/sx/cin/GameOver/sx_cin_dante_dying` |
| teleportSound | `event:/sx/cin/Cutscene 1 Opening/sx_cin_portal_in` |

当前项目资源中的 16 个非空音效 GUID 全部能在这些库中解析。剩余 53 处空引用包含可选字段，保留为空，播放时跳过；不使用不相关音效填充。

## 缺失音效处理

- 游戏的 FMOD 创建、单次播放和附着播放入口使用 `FMODUnity.OptionalAudio`。空引用直接跳过；不存在的事件返回无效句柄，同一事件只输出一次项目提示，不抛出 `EventNotFoundException` 打断游戏逻辑。首次查询时 FMOD 自身可能另有诊断提示。
- 失败查询会缓存，加载或卸载音频库后允许重试。`UNITY_SERVER` 或 `--dedicated-server` 下跳过这些音效请求。
- `StudioEventEmitter` 和 `StudioParameterTrigger` 在初始化、预加载和播放前检查事件，缺失时安全跳过。
- 桌面平台加载前检查文件是否存在；自动加载列表中某个库缺失不妨碍其余库加载，采样加载请求和加载计数也会正确清理。
- 本地玩家绑定时启用 `StudioListener`，解除绑定时停用。监听位置位于玩家所在平面，使 3D 音效的距离衰减与玩家位置一致。
- Unity `AudioPlayOneShotInteraction` 缺片段时仍完成交互；对话不会为无效事件分配回调句柄；视频缺音轨时以视频时间推进时间线；音乐缺事件时不会将无效实例登记为已播放曲目。

FMOD SDK 的改动集中于新增 `OptionalAudio.cs` 以及 `RuntimeManager.cs`、`StudioEventEmitter.cs`、`StudioParameterTrigger.cs`。升级 FMOD 插件时需保留这些项目容错改动。

## 验证

`AudioMigrationPlayModeTests` 覆盖现有 16 个音效、非静音混音输出、空引用、不存在的 GUID/路径、缺失事件组件、音频库重新加载、自动加载列表首尾缺库以及缺片段时继续交互。混音测试临时关闭编辑器测试环境的静音，结束后恢复原设置。

`GameplayCameraTests` 验证本地玩家监听器的创建、释放、重新绑定及避免重复添加。原先预期“缺失 FMOD 音效报错”的武器和联网测试已改为验证音效可以加载。

2026-09-10，Unity 6000.3.17f1 下上述音频、武器表现、联网启动及相机回归共 78 项 PlayMode 测试全部通过（0 失败、0 跳过）。结果：`Logs/AudioMigration/final-playmode.xml`。13 个源库、移植文件及生成的 StreamingAssets 副本的 SHA-256 校验全部一致；尚未另行执行独立播放器构建。
