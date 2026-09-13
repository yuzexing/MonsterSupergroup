using UnityEngine;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Options
{
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedMenuText : MonoBehaviour
    {
        private string key;
        private object[] arguments;
        private System.Func<string> resolver;
        public void Set(string entry, params object[] values) { resolver = null; key = entry; arguments = values; Refresh(); }
        public void SetResolver(System.Func<string> value) { resolver = value; key = null; Refresh(); }
        private void OnEnable() { MenuLocalization.Changed += Refresh; Refresh(); }
        private void OnDisable() => MenuLocalization.Changed -= Refresh;
        private void Refresh()
        {
            var label = GetComponent<Text>();
            if (GameLocalization.UIFont != null) label.font = GameLocalization.UIFont;
            if (resolver != null) label.text = resolver();
            else if (key != null) label.text = MenuLocalization.Get(key, arguments);
        }
    }
}
