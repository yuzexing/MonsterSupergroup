using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Options
{
    /// <summary>One process-local language state, backed by Unity Localization tables and fonts.</summary>
    public static class GameLocalization
    {
        public const string MenuTable = "MonsterMenus", ContentTable = "MonsterContent", FontTable = "MonsterFonts";
        public const string DefaultLanguage = "zh-CN";
        public static event Action Changed;
        public static bool IsReady { get; private set; }
        public static string Language { get; private set; } = DefaultLanguage;
        public static Font UIFont { get; private set; }
        public static TMP_FontAsset TMPFont { get; private set; }
        public static CultureInfo Culture => LocalizationSettings.SelectedLocale?.Identifier.CultureInfo ?? CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, AsyncOperationHandle<StringTable>> tables = new();
        private static readonly Dictionary<string, AsyncOperationHandle<Font>> fonts = new();
        private static readonly Dictionary<string, AsyncOperationHandle<TMP_FontAsset>> tmpFonts = new();
        private static readonly HashSet<string> missing = new();
        private static MonoBehaviour runner;
        private static int revision;
        private static bool applying;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Shutdown(); Changed = null; Language = DefaultLanguage; }
        public static IReadOnlyList<Locale> Locales => LocalizationSettings.AvailableLocales.Locales;
        public static string NormalizeLanguage(string code) => !string.IsNullOrWhiteSpace(code) &&
            LocalizationSettings.AvailableLocales.GetLocale(code) != null ? code : DefaultLanguage;

        public static IEnumerator Initialize(MonoBehaviour host, string code)
        {
            runner = host;
            yield return LocalizationSettings.InitializationOperation;
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            yield return Change(NormalizeLanguage(code), ++revision);
        }
        public static void Select(string code)
        {
            code = NormalizeLanguage(code);
            if (runner != null) runner.StartCoroutine(Change(code, ++revision));
        }
        private static IEnumerator Change(string code, int request)
        {
            yield return Load(DefaultLanguage);
            if (code != DefaultLanguage) yield return Load(code);
            if (request != revision || runner == null) yield break;
            if (!HasTables(DefaultLanguage) || !HasTables(code))
            { Debug.LogError("[Localization] Required string tables failed to load for " + code); yield break; }
            Language = code;
            UIFont = ReadFont(fonts, code) ?? ReadFont(fonts, DefaultLanguage);
            TMPFont = ReadFont(tmpFonts, code) ?? ReadFont(tmpFonts, DefaultLanguage);
            applying = true;
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
            applying = false;
            IsReady = true;
            ApplySceneFonts();
            Debug.Log("[Localization] Applied locale=" + code + " revision=" + request);
            Changed?.Invoke();
        }
        private static T ReadFont<T>(Dictionary<string, AsyncOperationHandle<T>> source, string code) where T : UnityEngine.Object =>
            source.TryGetValue(code, out var handle) && handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;
        private static bool HasTables(string code) => new[] { MenuTable, ContentTable }.All(name =>
            tables.TryGetValue(code + "/" + name, out var handle) && handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded);

        private static IEnumerator Load(string code)
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale == null) yield break;
            foreach (string name in new[] { MenuTable, ContentTable })
            {
                string key = code + "/" + name;
                if (!tables.TryGetValue(key, out var handle))
                {
                    handle = LocalizationSettings.StringDatabase.GetTableAsync(name, locale);
                    tables[key] = Addressables.ResourceManager.Acquire(handle);
                }
                yield return handle;
            }
            if (!fonts.TryGetValue(code, out var font))
            {
                font = LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<Font>(FontTable, "ui.font", locale);
                fonts[code] = Addressables.ResourceManager.Acquire(font);
            }
            if (!tmpFonts.TryGetValue(code, out var tmp))
            {
                tmp = LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<TMP_FontAsset>(FontTable, "ui.tmp", locale);
                tmpFonts[code] = Addressables.ResourceManager.Acquire(tmp);
            }
            yield return font; yield return tmp;
        }
        private static void OnLocaleChanged(Locale locale)
        { if (!applying && locale != null && locale.Identifier.Code != Language) Select(locale.Identifier.Code); }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplySceneFonts();
        public static void ApplySceneFonts()
        {
            if (UIFont != null)
                foreach (var label in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) label.font = UIFont;
            if (TMPFont != null)
                foreach (var label in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) label.font = TMPFont;
        }
        public static string Menu(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (TryText(MenuTable, key, out string value, args)) return value;
            int separator = key.IndexOf('_');
            bool semanticKey = key.StartsWith("ui.", StringComparison.Ordinal) ||
                separator >= 2 && separator <= 7 && key.Take(separator).All(c => c >= 'A' && c <= 'Z');
            if (semanticKey) return TryText(MenuTable, "ui.content.unavailable", out value) ? value : "…";
            return key;
        }
        private static string Unknown(uint id) => IsReady ? Menu("ui.content.unknown", id) : "…";
        public static string Content(string key, uint id = 0, params object[] args) => Text(ContentTable, key, Unknown(id), args);
        public static string Resolve(LocalizedString text, uint id = 0, params object[] args)
        {
            if (text == null || text.IsEmpty) return Unknown(id);
            return Text(text.TableReference.TableCollectionName, text.TableEntryReference, Unknown(id), args);
        }
        public static bool TryText(string table, TableEntryReference key, out string result, params object[] args)
        {
            var entry = Entry(Language, table, key) ?? Entry(DefaultLanguage, table, key);
            result = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.LocalizedValue)) return false;
            try
            {
                result = entry.IsSmart ? entry.GetLocalizedString(Culture, args) :
                    args == null || args.Length == 0 ? entry.LocalizedValue : string.Format(Culture, entry.LocalizedValue, args);
                return true;
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException ||
                error is UnityEngine.Localization.SmartFormat.Core.Formatting.FormattingException ||
                error is UnityEngine.Localization.SmartFormat.Core.Parsing.ParsingErrors)
            {
                if (missing.Add(table + "/" + key)) Debug.LogError("[Localization] Invalid template " + table + "/" + key + ": " + error.Message);
                return false;
            }
        }
        private static StringTableEntry Entry(string code, string table, TableEntryReference key)
        {
            if (!tables.TryGetValue(code + "/" + table, out var handle) || !handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded) return null;
            var entry = key.ReferenceType == TableEntryReference.Type.Id ? handle.Result.GetEntry(key.KeyId) : handle.Result.GetEntry(key.Key);
            return string.IsNullOrWhiteSpace(entry?.LocalizedValue) ? null : entry;
        }
        private static string Text(string table, TableEntryReference key, string fallback, object[] args) =>
            TryText(table, key, out string result, args) ? result : fallback;
        public static void Shutdown()
        {
            ++revision;
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            foreach (var handle in tables.Values) if (handle.IsValid()) Addressables.Release(handle);
            foreach (var handle in fonts.Values) if (handle.IsValid()) Addressables.Release(handle);
            foreach (var handle in tmpFonts.Values) if (handle.IsValid()) Addressables.Release(handle);
            tables.Clear(); fonts.Clear(); tmpFonts.Clear(); missing.Clear();
            runner = null; IsReady = false; UIFont = null; TMPFont = null;
        }
    }
}
