using MonsterSupergroup.Gameplay.Options;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class BuildConnectionLocalization
    {
        public static void Apply()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(GameLocalization.MenuTable);
            var zh = (StringTable)collection.GetTable("zh-CN"); var en = (StringTable)collection.GetTable("en");
            void Entry(string key, string chinese, string english)
            {
                if (zh.GetEntry(key) == null) zh.AddEntry(key, chinese);
                if (en.GetEntry(key) == null) en.AddEntry(key, english);
            }
            Entry("ui.connection.build.clientolder", "您的版本过低：本地 v{0}，房主 v{1}。", "Your version is older: local v{0}, host v{1}.");
            Entry("ui.connection.build.hostolder", "房主版本过低：房主 v{1}，本地 v{0}。请房主更新并重新创建房间。", "The host's version is older: host v{1}, local v{0}. Ask the host to update and create a new room.");
            Entry("ui.connection.build.unknownversion", "无法确认双方版本兼容性，请双方安装相同的完整发布版本。", "Version compatibility could not be verified. Both players should install the same complete release.");
            Entry("ui.connection.build.protocolmismatch", "联机兼容信息不一致，请双方更新到相同发布版本后重试。", "Network compatibility differs. Update both games to the same release and try again.");
            Entry("ui.connection.build.invalidpackage", "包内构建信息缺失或不一致，暂不能联网。请重新安装完整游戏包。", "Build information is missing or inconsistent. Reinstall the complete game package before connecting.");
            Entry("ui.connection.update_steam", "需要更新的一方请退出游戏，在 Steam 完成更新；未出现更新时可重启 Steam。", "The player with the older version should exit the game and update in Steam. Restart Steam if no update appears.");
            Entry("ui.connection.update_local", "请需要更新的一方安装对应的新包后重试。", "Install the matching newer package on the outdated computer and try again.");
            Entry("ui.connection.content_mismatch", "游戏资源配置不一致，请双方确认使用相同发布内容。", "Game content differs. Make sure both players use the same released content.");
            Entry("ui.connection.host_closed", "主机已断开连接，房间已关闭。", "The host disconnected. The room has closed.");
            Entry("ui.connection.host_lost", "与主机的连接已断开，主机可能已退出或网络中断。", "Connection to the host was lost. The host may have left or the network may be interrupted.");
            Entry("ui.connection.connect_failed", "未能连接房主，请确认房间仍开放并检查网络后重试。", "Could not connect to the host. Check that the room is open and your connection is available.");
            EditorUtility.SetDirty(zh); EditorUtility.SetDirty(en); EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssets();
        }
    }
}
