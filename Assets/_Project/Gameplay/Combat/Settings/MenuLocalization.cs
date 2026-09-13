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
