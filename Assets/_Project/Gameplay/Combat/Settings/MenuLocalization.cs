using System;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Options
{
    public static class MenuLocalization
    {
        public const string TableName = GameLocalization.MenuTable;
        public static event Action Changed { add => GameLocalization.Changed += value; remove => GameLocalization.Changed -= value; }
        public static bool IsReady => GameLocalization.IsReady;
        public static string Language => GameLocalization.Language;
        public static void Shutdown() => GameLocalization.Shutdown();
        public static void Select(string code) => GameLocalization.Select(code);
        public static string Get(string key, params object[] arguments)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (MonsterSupergroup.Builds.BuildCompatibility.TryDecode(key, out var reason, out var client, out var host, out bool steam))
            {
                string message = GameLocalization.Menu("ui.connection.build." + reason.ToString().ToLowerInvariant(), client, host);
                if (reason == MonsterSupergroup.Builds.BuildRejection.ClientOlder || reason == MonsterSupergroup.Builds.BuildRejection.HostOlder)
                    message += " " + GameLocalization.Menu(steam ? "ui.connection.update_steam" : "ui.connection.update_local");
                return message;
            }
            if (key.StartsWith("连接失败：", StringComparison.Ordinal) && key != "连接失败：{0}")
                return Get("连接失败：{0}", Get(key.Substring("连接失败：".Length)));
            return GameLocalization.Menu(key, arguments);
        }
        public static void Bind(Text label, string key, params object[] arguments)
        {
            if (string.IsNullOrEmpty(key)) return;
            var binding = label.GetComponent<LocalizedMenuText>();
            if (binding == null) binding = label.gameObject.AddComponent<LocalizedMenuText>();
            binding.Set(key, arguments);
        }
    }
}
