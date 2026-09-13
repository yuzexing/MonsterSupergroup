using System.Collections;
using System.Linq;
using FMODUnity;
using MonsterSupergroup.Gameplay.Options;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GameOptionsTests
    {
        private GameOptionsService service;
        private GameOptionsData original;
        private string preference;
        private bool hadPreference;
        private GameObject canvas, input;
        private Font font;

        [UnitySetUp] public IEnumerator SetUp()
        {
            hadPreference = PlayerPrefs.HasKey(GameOptionsService.PreferenceKey);
            preference = PlayerPrefs.GetString(GameOptionsService.PreferenceKey);
            service = GameOptionsService.EnsureInitialized(); original = service.Current;
            float deadline = Time.realtimeSinceStartup + 20;
            while (!MenuLocalization.IsReady && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(MenuLocalization.IsReady, Is.True, "Unity Localization tables must actually load.");
            service.SetLanguage("zh-CN"); yield return WaitForLanguage(() => GameLocalization.Language == "zh-CN");
            canvas = new GameObject("Options test canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 5000;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            if (EventSystem.current == null) input = new GameObject("Options test input", typeof(EventSystem), typeof(StandaloneInputModule));
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 20);
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            GameOptionsPanel.CloseFor(canvas.transform);
            service.RevertDisplay(); service.ApplyGraphics(original); service.ConfirmDisplay();
            service.SetLanguage(original.Language); service.SetScreenShake(original.ScreenShake);
            service.SetAudio(original.MasterVolume, original.MusicVolume, original.EffectsVolume);
            if (hadPreference) PlayerPrefs.SetString(GameOptionsService.PreferenceKey, preference);
            else PlayerPrefs.DeleteKey(GameOptionsService.PreferenceKey);
            PlayerPrefs.Save();
            Object.Destroy(canvas); if (input != null) Object.Destroy(input); Object.Destroy(font);
            yield return null;
        }

        [Test] public void CorruptFutureAndOutOfRangePreferencesRecover()
        {
            var defaults = new GameOptionsData { Width = 1280, Height = 720, RefreshNumerator = 60 };
            Assert.That(GameOptionsService.Parse("invalid-json", defaults).Width, Is.EqualTo(1280));
            Assert.That(GameOptionsService.Parse("{\"Version\":999,\"MasterVolume\":0}", defaults).MasterVolume, Is.EqualTo(1));
            var parsed = GameOptionsService.Parse("{\"MasterVolume\":-1,\"MusicVolume\":4,\"FrameLimit\":7,\"Language\":\"missing\",\"RenderScale\":9,\"Width\":0}", defaults);
            Assert.That(parsed.MasterVolume, Is.Zero); Assert.That(parsed.MusicVolume, Is.EqualTo(1));
            Assert.That(parsed.FrameLimit, Is.EqualTo(60)); Assert.That(parsed.Language, Is.EqualTo("zh-CN"));
            Assert.That(parsed.RenderScale, Is.EqualTo(1.5f)); Assert.That(parsed.Width, Is.EqualTo(1280));
        }

        [UnityTest] public IEnumerator LanguageUpdatesExistingTextAndPreservesArguments()
        {
            var label = new GameObject("Bound label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(canvas.transform); label.font = font;
            MenuLocalization.Bind(label, "大厅 {0}    ·    选择一位好友发送邀请，接受后加入准备房间。", "TEST-123");
            Assert.That(label.text, Does.StartWith("大厅 TEST-123"));
            service.SetLanguage("en"); yield return WaitForLanguage(() => GameLocalization.Language == "en");
            Assert.That(label.text, Does.StartWith("Lobby TEST-123"));
            Assert.That(MenuLocalization.Get("连接失败：房主已结束会话。"), Is.EqualTo("Connection failed: The host ended the session."));
            Assert.That(MenuLocalization.Get("unknown-key"), Is.EqualTo("unknown-key"));
            Assert.That(GameOptionsService.Parse(PlayerPrefs.GetString(GameOptionsService.PreferenceKey), service.Defaults).Language, Is.EqualTo("en"));
            service.SetLanguage("zh-CN"); yield return WaitForLanguage(() => GameLocalization.Language == "zh-CN");
            Assert.That(label.text, Does.StartWith("大厅 TEST-123"));
        }

        [UnityTest] public IEnumerator GraphicsAffectRuntimeAndKeepSourceAssetIntact()
        {
            var runtime = service.RuntimePipeline;
            Assert.That(runtime, Is.Not.Null);
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            float sourceScale = asset.renderScale; int sourceMsaa = asset.msaaSampleCount;
            Assert.That(runtime, Is.Not.SameAs(asset));
#endif
            var draft = service.Current; draft.RenderScale = .75f; draft.TextureLimit = 2; draft.VSync = true; draft.FrameLimit = 120;
            service.ApplyGraphics(draft); yield return null;
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.SameAs(runtime));
            Assert.That(runtime.renderScale, Is.EqualTo(.75f)); Assert.That(QualitySettings.globalTextureMipmapLimit, Is.EqualTo(2));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1)); Assert.That(Application.targetFrameRate, Is.EqualTo(120));
            draft.VSync = false; service.ApplyGraphics(draft); yield return null;
            Assert.That(QualitySettings.vSyncCount, Is.Zero); Assert.That(Application.targetFrameRate, Is.EqualTo(120));
            var loaded = GameOptionsService.Parse(PlayerPrefs.GetString(GameOptionsService.PreferenceKey), service.Defaults);
            Assert.That(loaded.RenderScale, Is.EqualTo(.75f)); Assert.That(loaded.TextureLimit, Is.EqualTo(2));
#if UNITY_EDITOR
            Assert.That(asset.renderScale, Is.EqualTo(sourceScale)); Assert.That(asset.msaaSampleCount, Is.EqualTo(sourceMsaa));
#endif
        }

        [UnityTest] public IEnumerator PanelDiscardsDraftAndAppliesOnlyOnRequest()
        {
            var start = service.Current; bool closed = false;
            GameOptionsPanel.Open(canvas.transform, font, () => closed = true);
            Find<Button>("Options.Tab.2").onClick.Invoke(); yield return null;
            Find<Slider>("Options.RenderScale").value = .6f;
            Assert.That(service.Current.RenderScale, Is.EqualTo(start.RenderScale));
            Find<Dropdown>("Options.VSync").value = 1; yield return null;
            Assert.That(Find<Dropdown>("Options.FrameLimit").interactable, Is.False);
            Assert.That(Find<Dropdown>("Options.DisplayMode").interactable, Is.EqualTo(!Application.isEditor));
            Find<Button>("Options.Back").onClick.Invoke(); yield return null;
            Assert.That(closed, Is.True); Assert.That(GameOptionsPanel.IsOpen, Is.False);
            Assert.That(service.Current.RenderScale, Is.EqualTo(start.RenderScale));
            GameOptionsPanel.Open(canvas.transform, font, null); Find<Button>("Options.Tab.2").onClick.Invoke(); yield return null;
            Find<Slider>("Options.RenderScale").value = .8f;
            Find<Button>("Options.Apply").onClick.Invoke(); yield return null;
            Assert.That(service.RuntimePipeline.renderScale, Is.EqualTo(.8f).Within(.001f));
            service.SetLanguage("en"); yield return WaitForLanguage(() => GameLocalization.Language == "en");
            Assert.That(Find<Button>("Options.Back").GetComponentInChildren<Text>().text, Is.EqualTo("Back"));
            Find<Button>("Options.Tab.1").onClick.Invoke(); yield return null;
            service.SetAudio(.2f, .3f, .4f);
            Find<Button>("Options.Reset").onClick.Invoke(); yield return null;
            Assert.That(service.Current.MasterVolume, Is.EqualTo(1));
            Assert.That(service.Current.MusicVolume, Is.EqualTo(1)); Assert.That(service.Current.EffectsVolume, Is.EqualTo(1));
            Find<Button>("Options.Tab.0").onClick.Invoke(); yield return null;
            service.SetScreenShake(false); Find<Button>("Options.Reset").onClick.Invoke(); yield return null;
            Assert.That(service.Current.Language, Is.EqualTo("zh-CN")); Assert.That(service.Current.ScreenShake, Is.True);
            var languageDropdown = Find<Dropdown>("Options.Language"); languageDropdown.Show(); yield return null;
            var english = languageDropdown.GetComponentsInChildren<Toggle>().Single(t => t.GetComponentsInChildren<Text>().Any(label => label.text == GameLocalization.Locales.Single(l => l.Identifier.Code == "en").LocaleName));
            english.isOn = true; yield return null;
            Assert.That(service.Current.Language, Is.EqualTo("en"));
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null);
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("Options.Language"), "Selecting a language must retain keyboard focus after the dropdown closes.");
            Find<Button>("Options.Tab.2").onClick.Invoke(); yield return null;
            Find<Button>("Options.Reset").onClick.Invoke(); yield return null;
            Assert.That(service.RuntimePipeline.renderScale, Is.EqualTo(.8f).Within(.001f), "Video reset must remain a draft until Apply.");
            Assert.That(canvas.GetComponentInChildren<GameOptionsPanel>().Draft.SameGraphics(service.Defaults), Is.True);
        }

        [UnityTest] public IEnumerator AudioControlsRealFmodBusesIndependently()
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!service.AudioAvailable && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(service.AudioAvailable, Is.True);
            var studio = RuntimeManager.StudioSystem;
            Assert.That(studio.getBus("bus:/", out var master), Is.EqualTo(FMOD.RESULT.OK));
            Assert.That(studio.getBus("bus:/mx", out var music), Is.EqualTo(FMOD.RESULT.OK));
            Assert.That(studio.getBus("bus:/sx", out var effects), Is.EqualTo(FMOD.RESULT.OK));
            service.SetAudio(.7f, 0, .4f); yield return null;
            master.getVolume(out float masterVolume); music.getVolume(out float musicVolume); effects.getVolume(out float effectsVolume);
            Assert.That(masterVolume, Is.EqualTo(.7f).Within(.001)); Assert.That(musicVolume, Is.Zero);
            Assert.That(effectsVolume, Is.EqualTo(.4f).Within(.001));
            service.SetAudio(0, .6f, .4f); yield return null;
            master.getVolume(out masterVolume); music.getVolume(out musicVolume); effects.getVolume(out effectsVolume);
            Assert.That(masterVolume, Is.Zero); Assert.That(musicVolume, Is.EqualTo(.6f).Within(.001));
            Assert.That(effectsVolume, Is.EqualTo(.4f).Within(.001));
            service.SetAudio(1, 1, 1); yield return null;
            master.getVolume(out masterVolume); Assert.That(masterVolume, Is.EqualTo(1));
        }

        private static IEnumerator WaitForLanguage(System.Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 25;
            while (!predicate() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(predicate(), Is.True); yield return null;
        }

        private T Find<T>(string name) where T : Component => canvas.GetComponentsInChildren<T>().Single(c => c.name == name);
    }
}
