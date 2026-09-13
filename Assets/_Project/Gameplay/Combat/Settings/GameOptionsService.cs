using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MonsterSupergroup.Gameplay.Options
{
    /// <summary>Process-local presentation settings. Never changes a network simulation or an asset on disk.</summary>
    [DefaultExecutionOrder(-20000)]
    public sealed class GameOptionsService : MonoBehaviour
    {
        public const string PreferenceKey = "MonsterSupergroup.Options.v1";
        public static readonly int[] FrameLimits = { 30, 60, 120, 144, 240, -1 };
        private static GameOptionsService instance;
        public static GameOptionsService Instance => instance;
        public static event Action Changed;
        public static bool ScreenShakeEnabled => instance == null || instance.current.ScreenShake;
        public GameOptionsData Current => current.Copy();
        public GameOptionsData Defaults => defaults.Copy();
        public bool IsDisplayConfirmationPending => previousDisplay != null;
        public float DisplaySecondsRemaining => Mathf.Max(0, (float)(displayDeadline - Time.realtimeSinceStartupAsDouble));
        public bool AudioAvailable { get; private set; }
        public static bool DisplayChangesSupported => !Application.isEditor;
        public UniversalRenderPipelineAsset RuntimePipeline => runtimePipeline;

        private GameOptionsData current, defaults, previousDisplay;
        private RenderPipelineAsset originalQualityPipeline, originalDefaultPipeline;
        private UniversalRenderPipelineAsset runtimePipeline;
        private int originalVsync, originalFrameLimit, originalTextureLimit;
        private FMOD.Studio.Bus masterBus, musicBus, effectsBus;
        private double nextAudioAttempt, displayDeadline;
        private bool audioWarning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; Changed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!GameplayRuntimeEnvironment.IsDedicatedServer) EnsureInitialized();
        }

        public static GameOptionsService EnsureInitialized()
        {
            if (instance == null && !GameplayRuntimeEnvironment.IsDedicatedServer)
                new GameObject("Local game options").AddComponent<GameOptionsService>();
            return instance;
        }

        private void Awake()
        {
            if (GameplayRuntimeEnvironment.IsDedicatedServer || (instance != null && instance != this))
            { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject);
            originalQualityPipeline = QualitySettings.renderPipeline;
            originalDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
            originalVsync = QualitySettings.vSyncCount; originalFrameLimit = Application.targetFrameRate;
            originalTextureLimit = QualitySettings.globalTextureMipmapLimit;
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (source != null)
            {
                runtimePipeline = Instantiate(source);
                runtimePipeline.name = source.name + " (local options)";
                runtimePipeline.hideFlags = HideFlags.DontSave;
                QualitySettings.renderPipeline = runtimePipeline;
                GraphicsSettings.defaultRenderPipeline = runtimePipeline;
            }
            var refresh = Screen.currentResolution.refreshRateRatio;
            defaults = new GameOptionsData { DisplayMode = Screen.fullScreenMode,
                Width = Math.Max(640, Screen.width), Height = Math.Max(360, Screen.height),
                RefreshNumerator = refresh.numerator, RefreshDenominator = Math.Max(1u, refresh.denominator),
                RenderScale = source != null ? source.renderScale : 1, Msaa = source != null ? source.msaaSampleCount : 1,
                TextureLimit = Mathf.Clamp(originalTextureLimit, 0, 2) };
            current = Parse(PlayerPrefs.GetString(PreferenceKey, ""), defaults);
            NormalizeGraphics(current);
            ApplyGraphicsRuntime();
            if (DisplayChangesSupported && PlayerPrefs.HasKey(PreferenceKey)) ApplyDisplay(current);
            GameLocalization.Changed += OnLanguageApplied;
            StartCoroutine(GameLocalization.Initialize(this, current.Language));
        }

        public static GameOptionsData Parse(string json, GameOptionsData fallback)
        {
            var data = fallback.Copy();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, data);
                    if (data.Version != 1) data = fallback.Copy();
                }
                catch (ArgumentException) { data = fallback.Copy(); }
            }
            data.Language = GameLocalization.IsReady ? GameLocalization.NormalizeLanguage(data.Language) :
                string.IsNullOrWhiteSpace(data.Language) ? GameLocalization.DefaultLanguage : data.Language;
            data.MasterVolume = FiniteClamp(data.MasterVolume, 0, 1, fallback.MasterVolume);
            data.MusicVolume = FiniteClamp(data.MusicVolume, 0, 1, fallback.MusicVolume);
            data.EffectsVolume = FiniteClamp(data.EffectsVolume, 0, 1, fallback.EffectsVolume);
            data.RenderScale = FiniteClamp(data.RenderScale, .5f, 1.5f, fallback.RenderScale);
            data.TextureLimit = Mathf.Clamp(data.TextureLimit, 0, 2);
            if (Array.IndexOf(FrameLimits, data.FrameLimit) < 0) data.FrameLimit = 60;
            if (data.DisplayMode != FullScreenMode.Windowed && data.DisplayMode != FullScreenMode.FullScreenWindow &&
                data.DisplayMode != FullScreenMode.ExclusiveFullScreen) data.DisplayMode = fallback.DisplayMode;
            if (data.Width < 640 || data.Height < 360 || data.Width > 16384 || data.Height > 16384 ||
                data.RefreshNumerator == 0 || data.RefreshDenominator == 0) data.CopyDisplayFrom(fallback);
            return data;
        }

        private static float FiniteClamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        public List<Resolution> AvailableResolutions(GameOptionsData display = null)
        {
            display ??= current;
            var result = Screen.resolutions.Where(r => r.width >= 640 && r.height >= 360)
                .GroupBy(r => (r.width, r.height, r.refreshRateRatio.numerator, r.refreshRateRatio.denominator))
                .Select(g => g.First()).ToList();
            // A custom window size is valid even if it is not an exclusive fullscreen mode.
            if (result.Count == 0 || display.DisplayMode == FullScreenMode.Windowed)
                if (!result.Any(r => MatchesResolution(display, r))) result.Add(ToResolution(display));
            return result.OrderBy(r => r.width).ThenBy(r => r.height).ThenBy(r => r.refreshRateRatio.value).ToList();
        }

        public static RefreshRate ClosestRefreshRate(GameOptionsData display, IEnumerable<Resolution> modes)
        {
            double preferred = (double)display.RefreshNumerator / display.RefreshDenominator;
            return modes.Where(r => r.width == display.Width && r.height == display.Height)
                .OrderBy(r => Math.Abs(r.refreshRateRatio.value - preferred))
                .ThenByDescending(r => r.refreshRateRatio.value).First().refreshRateRatio;
        }

        public static bool MatchesResolution(GameOptionsData data, Resolution resolution) =>
            data.Width == resolution.width && data.Height == resolution.height &&
            (ulong)data.RefreshNumerator * resolution.refreshRateRatio.denominator ==
            (ulong)resolution.refreshRateRatio.numerator * data.RefreshDenominator;

        private static Resolution ToResolution(GameOptionsData data) => new Resolution { width = data.Width, height = data.Height,
            refreshRateRatio = new RefreshRate { numerator = data.RefreshNumerator, denominator = data.RefreshDenominator } };

        public int[] SupportedMsaa()
        {
            if (runtimePipeline == null) return new[] { 1 };
            var result = new List<int> { 1 };
            foreach (int count in new[] { 2, 4, 8 })
            {
                var descriptor = new RenderTextureDescriptor(128, 128,
                    runtimePipeline.supportsHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, 24) { msaaSamples = count };
                if (SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor) >= count) result.Add(count);
            }
            return result.ToArray();
        }

        private void NormalizeGraphics(GameOptionsData data)
        {
            data.Msaa = SupportedMsaa().Where(v => v <= data.Msaa).DefaultIfEmpty(1).Max();
            if (DisplayChangesSupported && data.DisplayMode != FullScreenMode.Windowed && Screen.resolutions.Length > 0 &&
                !Screen.resolutions.Any(r => MatchesResolution(data, r)))
            {
                var resolution = Screen.currentResolution;
                data.Width = resolution.width; data.Height = resolution.height;
                data.RefreshNumerator = resolution.refreshRateRatio.numerator;
                data.RefreshDenominator = Math.Max(1u, resolution.refreshRateRatio.denominator);
            }
        }

        public void SetLanguage(string code)
        {
            current.Language = GameLocalization.NormalizeLanguage(code);
            MenuLocalization.Select(current.Language); Save(); Changed?.Invoke();
        }

        private void OnLanguageApplied()
        {
            current.Language = GameLocalization.Language;
            Save(); Changed?.Invoke();
        }

        public void SetScreenShake(bool enabled)
        { current.ScreenShake = enabled; Save(); Changed?.Invoke(); }

        public void SetAudio(float master, float music, float effects)
        {
            current.MasterVolume = FiniteClamp(master, 0, 1, 1);
            current.MusicVolume = FiniteClamp(music, 0, 1, 1);
            current.EffectsVolume = FiniteClamp(effects, 0, 1, 1);
            ApplyAudio(); Save(); Changed?.Invoke();
        }

        public void ApplyGraphics(GameOptionsData draft)
        {
            if (IsDisplayConfirmationPending) RevertDisplay();
            var sanitized = Parse(JsonUtility.ToJson(draft), defaults);
            NormalizeGraphics(sanitized);
            if (!DisplayChangesSupported) sanitized.CopyDisplayFrom(current);
            if (!sanitized.SameDisplay(current))
            { previousDisplay = current.Copy(); displayDeadline = Time.realtimeSinceStartupAsDouble + 15; }
            current.CopyGraphicsFrom(sanitized); ApplyGraphicsRuntime();
            if (IsDisplayConfirmationPending) ApplyDisplay(current);
            Save(); Changed?.Invoke();
        }

        private void ApplyGraphicsRuntime()
        {
            QualitySettings.vSyncCount = current.VSync ? 1 : 0;
            Application.targetFrameRate = current.FrameLimit;
            QualitySettings.globalTextureMipmapLimit = current.TextureLimit;
            if (runtimePipeline != null)
            { runtimePipeline.renderScale = current.RenderScale; runtimePipeline.msaaSampleCount = current.Msaa; }
        }

        private static void ApplyDisplay(GameOptionsData data) => Screen.SetResolution(data.Width, data.Height, data.DisplayMode,
            new RefreshRate { numerator = data.RefreshNumerator, denominator = data.RefreshDenominator });

        public void ConfirmDisplay()
        { if (previousDisplay == null) return; previousDisplay = null; Save(); Changed?.Invoke(); }

        public void RevertDisplay()
        {
            if (previousDisplay == null) return;
            current.CopyDisplayFrom(previousDisplay); previousDisplay = null;
            ApplyDisplay(current); Save(); Changed?.Invoke();
        }

        public void ResetGeneral() { SetLanguage(defaults.Language); SetScreenShake(defaults.ScreenShake); }
        public void ResetAudio() => SetAudio(defaults.MasterVolume, defaults.MusicVolume, defaults.EffectsVolume);

        private void Save()
        {
            var saved = current.Copy();
            // An unconfirmed display mode must never survive a crash or a simultaneous audio/language edit.
            if (previousDisplay != null) saved.CopyDisplayFrom(previousDisplay);
            PlayerPrefs.SetString(PreferenceKey, JsonUtility.ToJson(saved)); PlayerPrefs.Save();
        }

        private void Update()
        {
            if (previousDisplay != null && Time.realtimeSinceStartupAsDouble >= displayDeadline) RevertDisplay();
            if ((!AudioAvailable || !masterBus.isValid() || !musicBus.isValid() || !effectsBus.isValid()) &&
                Time.realtimeSinceStartupAsDouble >= nextAudioAttempt)
            { nextAudioAttempt = Time.realtimeSinceStartupAsDouble + 1; ApplyAudio(); }
        }

        private void ApplyAudio()
        {
            bool wasAvailable = AudioAvailable;
            try
            {
                var studio = RuntimeManager.StudioSystem;
                AudioAvailable = studio.getBus("bus:/", out masterBus) == FMOD.RESULT.OK &&
                    studio.getBus("bus:/mx", out musicBus) == FMOD.RESULT.OK && studio.getBus("bus:/sx", out effectsBus) == FMOD.RESULT.OK;
                if (AudioAvailable)
                    AudioAvailable = masterBus.setVolume(current.MasterVolume) == FMOD.RESULT.OK &&
                        musicBus.setVolume(current.MusicVolume) == FMOD.RESULT.OK && effectsBus.setVolume(current.EffectsVolume) == FMOD.RESULT.OK;
            }
            catch (SystemNotInitializedException) { AudioAvailable = false; }
            if (!AudioAvailable && !audioWarning)
            { audioWarning = true; Debug.LogWarning("[Options] Audio buses are not ready; saved volumes will apply when FMOD banks load."); }
            if (AudioAvailable) audioWarning = false;
            if (wasAvailable != AudioAvailable) Changed?.Invoke();
        }

        private void OnApplicationQuit() { RevertDisplay(); }
        private void OnDestroy()
        {
            if (instance != this) return;
            RevertDisplay();
            QualitySettings.renderPipeline = originalQualityPipeline; GraphicsSettings.defaultRenderPipeline = originalDefaultPipeline;
            QualitySettings.vSyncCount = originalVsync; Application.targetFrameRate = originalFrameLimit;
            QualitySettings.globalTextureMipmapLimit = originalTextureLimit;
            if (runtimePipeline != null) Destroy(runtimePipeline);
            GameLocalization.Changed -= OnLanguageApplied;
            MenuLocalization.Shutdown();
            instance = null;
        }
    }
}
