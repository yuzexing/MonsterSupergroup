#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using MonsterSupergroup.Gameplay.Options;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.CSV;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class ContentLocalizationTests
    {
        private GameOptionsService service;
        private string preference, language;
        private bool hadPreference;
        [UnitySetUp] public IEnumerator SetUp()
        {
            hadPreference = PlayerPrefs.HasKey(GameOptionsService.PreferenceKey);
            preference = PlayerPrefs.GetString(GameOptionsService.PreferenceKey);
            service = GameOptionsService.EnsureInitialized();
            yield return Wait(() => GameLocalization.IsReady);
            language = GameLocalization.Language;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            service.SetLanguage(language); yield return Wait(() => GameLocalization.Language == language);
            if (hadPreference) PlayerPrefs.SetString(GameOptionsService.PreferenceKey, preference);
            else PlayerPrefs.DeleteKey(GameOptionsService.PreferenceKey);
            PlayerPrefs.Save();
        }
        public static IEnumerator Wait(Func<bool> condition)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 25;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(condition(), Is.True, "Localization must complete within 25 seconds.");
            yield return null;
        }
        private static T[] Assets<T>() where T : Object => AssetDatabase.FindAssets("t:" + typeof(T).Name)
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).ToArray();

        [UnityTest] public IEnumerator AllContentLanguagesLevelsAndRaritiesFormatWithoutChangingGameplay()
        {
            var weapons = Assets<WeaponData>(); var equipment = Assets<EquipmentData>(); var perks = Assets<PerkData>();
            Assert.That(weapons.Length, Is.EqualTo(6)); Assert.That(equipment.Length, Is.EqualTo(8)); Assert.That(perks.Length, Is.EqualTo(7));
            var all = weapons.Cast<Object>().Concat(equipment).Concat(perks).Concat(Assets<UltimateData>()).ToArray();
            var before = all.Select(a => EditorJsonUtility.ToJson(a)).ToArray();
            foreach (string code in new[] { "zh-CN", "en" })
            {
                service.SetLanguage(code); yield return Wait(() => GameLocalization.Language == code);
                Assert.That(LocalizationSettings.SelectedLocale.Identifier.Code, Is.EqualTo(code));
                Assert.That(GameLocalization.UIFont, Is.Not.Null); Assert.That(GameLocalization.TMPFont, Is.Not.Null);
                foreach (var weapon in weapons) { Check(weapon.GetTitle()); Check(weapon.GetDescription()); if (weapon.GetQuote(out string quote)) Check(quote); }
                foreach (var item in equipment)
                {
                    Check(item.GetTitle());
                    for (uint level = 0; level < item.Levels.Length; level++) Check(item.GetDescription(level));
                }
                foreach (var perk in perks)
                {
                    Check(perk.GetTitle());
                    foreach (PerkRarity rarity in Enum.GetValues(typeof(PerkRarity)))
                        if (perk.GetAllRarities().Any(r => r.Rarity == rarity)) Check(perk.GetDescription(rarity));
                }
                foreach (var ultimate in Assets<UltimateData>()) { Check(ultimate.GetTitle()); Check(ultimate.GetDescription()); }
                var damage = equipment.Single(e => e.ID == 2);
                var values = ContentText.Arguments(damage.Levels[0].Modifiers);
                Assert.That(damage.GetDescription(0), Does.Contain(Convert.ToSingle(values["damagePercent"]).ToString("0.##", GameLocalization.Culture) + "%"));
                var knockback = equipment.Single(e => e.ID == 304);
                Assert.That(knockback.GetDescription(0), Does.Contain("100%").And.Contain("20%"));
                string text = string.Concat(weapons.Select(w => w.GetTitle())).Replace("\n", "");
                Assert.That(GameLocalization.TMPFont.HasCharacters(text, out uint[] missing, true, true), Is.True, "Missing glyphs: " + string.Join(",", missing ?? Array.Empty<uint>()));
            }
            Assert.That(all.Select(a => EditorJsonUtility.ToJson(a)).ToArray(), Is.EqualTo(before), "Text reads must not mutate content or native parameters.");
        }

        [UnityTest] public IEnumerator LatestLanguageWinsAndMissingContentNeverExposesAKey()
        {
            var subtitle = new AstralShift.Cinematics.Timeline.TimelineSubtitleBehaviour();
            subtitle.SetTranslatedText("游戏");
            service.SetLanguage("en"); service.SetLanguage("zh-CN"); service.SetLanguage("en");
            yield return Wait(() => GameLocalization.Language == "en"); yield return new WaitForSecondsRealtime(.1f);
            Assert.That(GameLocalization.Language, Is.EqualTo("en"));
            Assert.That(ContentText.Name(LocalizedContentKind.Weapon, 987654), Is.EqualTo("Unknown content #987654"));
            Assert.That(GameLocalization.Menu("ui.missing.test_entry"), Is.EqualTo("Content not ready"));
            Assert.That(subtitle.Text, Is.EqualTo(GameLocalization.Menu("游戏")));
            service.SetLanguage("zh-CN"); yield return Wait(() => GameLocalization.Language == "zh-CN");
            Assert.That(ContentText.Name(LocalizedContentKind.Weapon, 987654), Is.EqualTo("未知内容 #987654"));
            Assert.That(subtitle.Text, Is.EqualTo("游戏"));
        }

        [UnityTest] public IEnumerator ThirdLocaleUsesTheSameServiceAndFallsBackToChinese()
        {
            var locale = Locale.CreateLocale("fr"); locale.LocaleName = "Français";
            var provider = new TemporaryLocaleTables();
            var oldStrings = LocalizationSettings.StringDatabase.TableProvider;
            var oldAssets = LocalizationSettings.AssetDatabase.TableProvider;
            LocalizationSettings.AvailableLocales.AddLocale(locale);
            LocalizationSettings.StringDatabase.TableProvider = provider;
            LocalizationSettings.AssetDatabase.TableProvider = provider;
            try
            {
                service.SetLanguage("fr"); yield return Wait(() => GameLocalization.Language == "fr");
                Assert.That(GameLocalization.Locales.Any(l => l.Identifier.Code == "fr"), Is.True);
                Assert.That(GameLocalization.Menu("游戏"), Is.EqualTo("Jouer"));
                Assert.That(ContentText.Name(LocalizedContentKind.Weapon, 2), Is.EqualTo("Flamme de test"));
                Assert.That(ContentText.Name(LocalizedContentKind.Weapon, 1), Is.EqualTo("炽焰之笔"));
                Assert.That(GameLocalization.UIFont, Is.Not.Null);
            }
            finally
            {
                LocalizationSettings.StringDatabase.TableProvider = oldStrings;
                LocalizationSettings.AssetDatabase.TableProvider = oldAssets;
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("zh-CN");
                GameLocalization.Shutdown();
                foreach (string table in new[] { GameLocalization.MenuTable, GameLocalization.ContentTable }) LocalizationSettings.StringDatabase.ReleaseTable(table, locale);
                LocalizationSettings.AssetDatabase.ReleaseTable(GameLocalization.FontTable, locale);
                LocalizationSettings.AvailableLocales.RemoveLocale(locale);
                provider.Dispose(); Object.Destroy(locale);
                service.StartCoroutine(GameLocalization.Initialize(service, "zh-CN"));
            }
            yield return Wait(() => GameLocalization.IsReady && GameLocalization.Language == "zh-CN");
        }
        private sealed class TemporaryLocaleTables : ITableProvider, IDisposable
        {
            private readonly List<LocalizationTable> created = new();
            public AsyncOperationHandle<T> ProvideTableAsync<T>(string tableCollectionName, Locale locale) where T : LocalizationTable
            {
                if (locale.Identifier.Code != "fr") return default;
                LocalizationTable clone;
                if (typeof(T) == typeof(StringTable))
                {
                    var source = LocalizationEditorSettings.GetStringTableCollection(tableCollectionName).GetTable("en") as StringTable;
                    var table = Object.Instantiate(source); table.Clear();
                    if (tableCollectionName == GameLocalization.MenuTable) table.AddEntry("游戏", "Jouer");
                    else table.AddEntry("weapon.2.name", "Flamme de test");
                    clone = table;
                }
                else clone = Object.Instantiate(LocalizationEditorSettings.GetAssetTableCollection(tableCollectionName).GetTable("en"));
                clone.LocaleIdentifier = locale.Identifier; created.Add(clone);
                return Addressables.ResourceManager.CreateCompletedOperation((T)clone, null);
            }
            public void Dispose() { foreach (var table in created) Object.Destroy(table); }
        }
        private static void Check(string text)
        {
            Assert.That(text, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("…"));
            Assert.That(text, Does.Not.Contain("{").And.Not.Contain("Unknown content").And.Not.Contain("未知内容").And.Not.Contain("WPN_"));
        }
    }
}
#endif
