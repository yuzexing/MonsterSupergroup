#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System.Collections;
using System.IO;
using System.Linq;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationMenuProcessProbe
    {
        private bool localizationProbe;
        private IEnumerator ConfigureLocaleProbe()
        {
            var options = GameOptionsService.EnsureInitialized();
            yield return Wait(() => GameLocalization.IsReady, "localization ready");
            if (role == "host")
            {
                Mark("locale-pref-exists", PlayerPrefs.HasKey(GameOptionsService.PreferenceKey) ? "1" : "0");
                Mark("locale-pref-original", PlayerPrefs.GetString(GameOptionsService.PreferenceKey, ""));
            }
            yield return Wait(() => Seen("locale-pref-original"), "locale preference backup");
            localizationProbe = true;
            string code = Arg("--menu-locale=");
            options.SetLanguage(code); yield return Wait(() => GameLocalization.Language == code, "requested locale");
            var catalog = PreparationMenuCatalog.Load();
            foreach (var weapon in catalog.Weapons)
            {
                Require(weapon.Name == ContentText.Name(LocalizedContentKind.Weapon, weapon.Id), "Content name differs across entry points");
                Require(!weapon.Description.Contains("{") && !string.IsNullOrWhiteSpace(weapon.Description), "Unformatted weapon description");
            }
            Require(catalog.FindWeapon(2).Name == (code == "en" ? "Will-o'-the-Wisp" : "鬼火"), "Wrong content language");
            // A language-only refresh must retain existing buttons and keyboard selection.
            var view = FindFirstObjectByType<PreparationMenuView>();
            yield return null;
            var buttons = view.GetComponentsInChildren<Button>();
            var focus = EventSystem.current.currentSelectedGameObject;
            options.SetLanguage(code == "en" ? "zh-CN" : "en");
            yield return Wait(() => GameLocalization.Language != code, "second locale");
            Require(buttons.All(b => b != null) && EventSystem.current.currentSelectedGameObject == focus, "Locale rebuild stole home focus");
            options.SetLanguage(code); yield return Wait(() => GameLocalization.Language == code, "restored test locale");
            Stage("localized-content", "locale=" + code + " weapon2=" + catalog.FindWeapon(2).Name);
        }
        private void RestoreLocalePreference()
        {
            if (!localizationProbe || !Seen("locale-pref-original")) return;
            if (Read("locale-pref-exists") == "1") PlayerPrefs.SetString(GameOptionsService.PreferenceKey, Read("locale-pref-original"));
            else PlayerPrefs.DeleteKey(GameOptionsService.PreferenceKey);
            PlayerPrefs.Save();
        }
    }
}
#endif
