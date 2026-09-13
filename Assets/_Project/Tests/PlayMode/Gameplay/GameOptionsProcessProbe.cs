#if UNITY_EDITOR || MONSTER_OPTIONS_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using FMODUnity;
using Mirror;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Standalone graphics/audio/restart checks. Only installed with an explicit validation argument.</summary>
    public sealed class GameOptionsProcessProbe : MonoBehaviour
    {
        [Serializable] private sealed class SavedPreference { public bool Exists; public string Json; }
        private GameOptionsService options;
        private string directory, phase;
        private bool finished;
        private float deadline, measuredPeak;
        private FMOD.Studio.EventInstance audioEvent;
        private BootGameplayNetworkManager manager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string phase = Arg("--options-probe=");
            if (phase == null || FindFirstObjectByType<GameOptionsProcessProbe>() != null) return;
            var probe = new GameObject("Options validation probe").AddComponent<GameOptionsProcessProbe>();
            probe.phase = phase; probe.directory = Arg("--options-artifacts=") ?? "Logs/OptionsStandalone";
            Directory.CreateDirectory(probe.directory); DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true; deadline = Time.realtimeSinceStartup + 200;
            var routines = new Stack<IEnumerator>(); routines.Push(Run());
            while (routines.Count > 0)
            {
                object next;
                try
                {
                    var current = routines.Peek();
                    if (!current.MoveNext()) { routines.Pop(); continue; }
                    next = current.Current;
                    if (next is IEnumerator nested) { routines.Push(nested); continue; }
                }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
            Finish(true);
        }

        private IEnumerator Run()
        {
            if (phase == "dedicated")
            {
                Check(GameOptionsService.Instance == null && !MenuLocalization.IsReady && !GameOptionsPanel.IsOpen,
                    "Dedicated server created local presentation settings");
                yield break;
            }
            options = GameOptionsService.EnsureInitialized();
            if (phase == "restore") { RestorePreference(); yield break; }
            yield return Wait(() => MenuLocalization.IsReady && options.AudioAvailable, "localization and audio");
            if (phase == "restart")
            {
                var expected = JsonUtility.FromJson<GameOptionsData>(File.ReadAllText(Path.Combine(directory, "expected.json")));
                var actual = options.Current;
                Check(actual.Language == expected.Language && actual.ScreenShake == expected.ScreenShake && actual.SameGraphics(expected), "Preferences did not survive restart");
                Check(Mathf.Approximately(actual.MasterVolume, expected.MasterVolume) && Mathf.Approximately(actual.MusicVolume, expected.MusicVolume) &&
                    Mathf.Approximately(actual.EffectsVolume, expected.EffectsVolume), "Audio preferences did not survive restart");
                Check(MenuLocalization.Get("选项") == "Options", "Saved locale did not reach the string database");
                RestorePreference(); yield break;
            }
            var saved = new SavedPreference { Exists = PlayerPrefs.HasKey(GameOptionsService.PreferenceKey), Json = PlayerPrefs.GetString(GameOptionsService.PreferenceKey) };
            File.WriteAllText(Path.Combine(directory, "preference-backup.json"), JsonUtility.ToJson(saved));
            options.SetLanguage("zh-CN");
            yield return Wait(() => GameLocalization.Language == "zh-CN", "initial Chinese locale");
            options.SetAudio(1, 1, 1); options.SetScreenShake(true);
            yield return Wait(() => NetworkManager.singleton is BootGameplayNetworkManager, "network manager");
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true); yield return manager.EnsureMainMenu();
            yield return Wait(() => FindFirstObjectByType<PreparationMenuView>() != null, "main menu");
            yield return Wait(() => UnityEngine.Rendering.SplashScreen.isFinished, "splash screen");
            Click("选项"); yield return null;
            Check(GameOptionsPanel.IsOpen && !NetworkClient.active, "Main menu options started a network session");
            yield return Shot("options-general-zh");
            options.SetLanguage("en"); yield return Wait(() => GameLocalization.Language == "en", "English locale");
            Check(MenuLocalization.Get("选项") == "Options", "English table was released during locale switching");
            yield return Shot("options-general-en");
            Click("Options.Tab.1"); yield return null;
            yield return Shot("options-audio-en");
            yield return VerifyAudioOutput();
            Click("Options.Tab.2"); yield return null;
            var draft = options.Current; draft.RenderScale = .75f; draft.VSync = false; draft.FrameLimit = 120; draft.TextureLimit = 2;
            draft.Msaa = options.SupportedMsaa().Last(); options.ApplyGraphics(draft);
            Check(options.RuntimePipeline.renderScale == .75f && options.RuntimePipeline.msaaSampleCount == draft.Msaa, "Graphics did not reach the runtime pipeline");
            Check(QualitySettings.globalTextureMipmapLimit == 2 && Application.targetFrameRate == 120, "Texture/FPS settings were ignored");
            GameOptionsPanel.HandleBack(); yield return null; Click("选项"); Click("Options.Tab.2");
            yield return Shot("options-video-en");
            var resolutionChoice = FindObjectsByType<OptionsArrowChoice>(FindObjectsSortMode.None).Single(d => d.name == "Options.Resolution");
            Check(resolutionChoice.Options.All(o => !o.Contains("Hz")) && resolutionChoice.Options.Distinct().Count() == resolutionChoice.Options.Count,
                "Resolution list still combines refresh rates or duplicates sizes");
            resolutionChoice.Step(1); yield return Shot("options-resolution-choice");
            var refreshChoice = FindObjectsByType<OptionsArrowChoice>(FindObjectsSortMode.None).Single(d => d.name == "Options.RefreshRate");
            Check(refreshChoice.Options.Count > 0, "Separate refresh-rate choices are missing");
            refreshChoice.Step(1); yield return Shot("options-refresh-rate-choice");
            var slow = options.Current; slow.FrameLimit = 30; options.ApplyGraphics(slow);
            yield return new WaitForSecondsRealtime(.5f);
            int firstFrame = Time.frameCount; float firstTime = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(2);
            float actualFps = (Time.frameCount - firstFrame) / (Time.realtimeSinceStartup - firstTime);
            Check(actualFps >= 10 && actualFps <= 36, "30 FPS cap did not affect frame pacing: " + actualFps);
            Debug.Log("[OptionsProcess] Measured frame rate with 30 FPS cap: " + actualFps);
            slow.FrameLimit = 120; options.ApplyGraphics(slow);
            yield return VerifyDisplay();
            GameOptionsPanel.HandleBack(); yield return null;
            options.SetLanguage("en"); yield return Wait(() => GameLocalization.Language == "en", "English home locale");
            yield return Shot("home-en");
            Check(manager.TryStartOfflineRoom(out var error), error);
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "solo room");
            yield return Shot("room-en");
            var weaponButton = FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name.EndsWith("  ›", StringComparison.Ordinal));
            weaponButton.onClick.Invoke(); yield return null;
            yield return Shot("weapon-selection-en"); Click("返回房间"); yield return null;
            manager.StartPreparedGame();
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame, "battle");
            var combat = FindFirstObjectByType<NetworkGameplayMenuController>();
            combat.OpenMenu(); yield return null;
            yield return Shot("combat-menu-en");
            Click("CombatMenu.SpellTab"); yield return Shot("combat-spells-en"); Click("CombatMenu.CharacterTab");
            Click("CombatMenu.Options"); yield return null;
            Check(GameOptionsPanel.IsOpen && NetworkClient.localPlayer.GetComponent<PlayerMovement>().IsMenuInputBlocked, "Combat options lost the owner input gate");
            options.SetLanguage("zh-CN"); yield return Wait(() => GameLocalization.Language == "zh-CN", "Chinese combat locale");
            yield return Shot("combat-options-zh");
            var camera = FindFirstObjectByType<GameplayCameraRig>();
            int shakes = 0; camera.ShakePlayed += _ => shakes++;
            options.SetScreenShake(false); camera.PlayShake(camera.BoundPlayer, 2);
            Check(shakes == 0, "Disabled shake still played");
            options.SetScreenShake(true); camera.PlayShake(camera.BoundPlayer, 2);
            Check(shakes == 1, "Reenabled shake did not play");
            GameOptionsPanel.HandleBack(); yield return null;
            Check(combat.IsOpen, "Returning from options closed the combat menu");
            yield return Shot("combat-menu-zh");
            Click("CombatMenu.SpellTab"); yield return Shot("combat-spells-zh");
            combat.CloseMenu(); manager.LeavePreparationRoom();
            yield return Wait(() => !NetworkClient.active && !manager.IsGameplayLoaded && !manager.IsLeavingRoom, "return to menu");
            options.SetLanguage("en"); yield return Wait(() => GameLocalization.Language == "en", "saved English locale");
            options.SetAudio(.7f, .3f, .4f); options.SetScreenShake(false);
            File.WriteAllText(Path.Combine(directory, "expected.json"), JsonUtility.ToJson(options.Current));
        }

        private IEnumerator VerifyDisplay()
        {
            var previous = options.Current;
            var draft = previous.Copy(); draft.DisplayMode = FullScreenMode.FullScreenWindow;
            options.ApplyGraphics(draft);
            Check(options.IsDisplayConfirmationPending, "Display changes did not require confirmation");
            options.SetAudio(.8f, 1, 1);
            var disk = GameOptionsService.Parse(PlayerPrefs.GetString(GameOptionsService.PreferenceKey), options.Defaults);
            Check(disk.SameDisplay(previous), "An audio edit persisted an unconfirmed display mode");
            yield return new WaitForSecondsRealtime(1);
            Check(Screen.fullScreenMode == FullScreenMode.FullScreenWindow, "Borderless fullscreen did not apply");
            yield return Shot("display-confirmation");
            yield return Wait(() => !options.IsDisplayConfirmationPending, "15 second display rollback");
            Check(options.Current.SameDisplay(previous), "Timed-out display mode did not revert");
            draft = options.Current; draft.DisplayMode = FullScreenMode.ExclusiveFullScreen;
            options.ApplyGraphics(draft); yield return new WaitForSecondsRealtime(1);
            Check(Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen, "Exclusive fullscreen did not apply");
            options.ConfirmDisplay();
            draft = options.Current; draft.DisplayMode = FullScreenMode.Windowed; draft.Width = 1024; draft.Height = 768;
            options.ApplyGraphics(draft); yield return new WaitForSecondsRealtime(1);
            Check(Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == 1024 && Screen.height == 768, "Window resolution did not apply");
            options.ConfirmDisplay(); yield return Shot("options-1024x768");
            draft.Width = 1280; draft.Height = 720; options.ApplyGraphics(draft);
            yield return new WaitForSecondsRealtime(1); options.ConfirmDisplay();
        }

        private IEnumerator VerifyAudioOutput()
        {
            var studio = RuntimeManager.StudioSystem;
            Check(studio.getBus("bus:/", out var master) == FMOD.RESULT.OK, "Master bus missing");
            master.lockChannelGroup(); studio.flushCommands();
            Check(master.getChannelGroup(out var group) == FMOD.RESULT.OK, "No master channel group");
            // HEAD is closest to the output; TAIL would measure the input before the master volume fader.
            Check(group.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, out var dsp) == FMOD.RESULT.OK, "No master output DSP");
            dsp.setMeteringEnabled(true, true);
            foreach (string path in new[] { "event:/mx/mx_maintheme", "event:/sx/plr/Sx_plr_slash", "event:/sx/plr/Sx_plr_hurt",
                "event:/sx/ui/PauseMenu/sx_ui_optionSelectSuccess" })
            {
                bool music = path.StartsWith("event:/mx/", StringComparison.Ordinal);
                options.SetAudio(1, 1, 1); audioEvent = RuntimeManager.CreateInstance(path);
                if (!music) audioEvent.set3DAttributes(RuntimeUtils.To3DAttributes(Vector3.zero));
                audioEvent.start(); yield return Meter(dsp, music ? 3 : 1, !music);
                Check(measuredPeak > .00001f, "No audible output from " + path);
                float audible = measuredPeak;
                options.SetAudio(1, music ? 0 : 1, music ? 1 : 0);
                audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.start();
                yield return new WaitForSecondsRealtime(.5f); yield return Meter(dsp, 1, !music);
                Check(measuredPeak < audible * .02f, "Category volume did not silence " + path);
                options.SetAudio(0, 1, 1); audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.start();
                yield return new WaitForSecondsRealtime(.5f); yield return Meter(dsp, 1, !music);
                Check(measuredPeak < audible * .02f, "Master volume did not silence " + path);
                options.SetAudio(1, 1, 1); audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.start();
                yield return Meter(dsp, music ? 3 : 1, !music);
                Check(measuredPeak > .00001f, "Restoring volume did not restore audio output");
                audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.release(); audioEvent.clearHandle();
                Debug.Log("[OptionsProcess] Audio output verified: " + path + " peak=" + audible);
            }
            dsp.setMeteringEnabled(false, false); master.unlockChannelGroup();
        }

        private IEnumerator Meter(FMOD.DSP dsp, float seconds, bool repeat)
        {
            measuredPeak = 0; float end = Time.realtimeSinceStartup + seconds, nextShot = 0;
            while (Time.realtimeSinceStartup < end)
            {
                if (repeat && Time.realtimeSinceStartup >= nextShot)
                { audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.start(); nextShot = Time.realtimeSinceStartup + .25f; }
                if (dsp.getMeteringInfo(out var input, out var output) == FMOD.RESULT.OK && output.peaklevel != null)
                    for (int channel = 0; channel < output.numchannels; channel++)
                        measuredPeak = Mathf.Max(measuredPeak, output.peaklevel[channel]);
                yield return null;
            }
        }

        private void RestorePreference()
        {
            string path = Path.Combine(directory, "preference-backup.json");
            if (!File.Exists(path)) return;
            var previous = JsonUtility.FromJson<SavedPreference>(File.ReadAllText(path));
            if (previous.Exists) PlayerPrefs.SetString(GameOptionsService.PreferenceKey, previous.Json);
            else PlayerPrefs.DeleteKey(GameOptionsService.PreferenceKey);
            PlayerPrefs.Save();
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(.3f);
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (!texture.GetPixels32().Any(p => p.a != 0))
            {
                // Hidden Windows players have no readable swapchain. Render the same camera and live canvases
                // into a texture for layout review; display-mode assertions still use the real Screen APIs.
                Destroy(texture); texture = CaptureOffscreen();
                Debug.Log("[OptionsRender] Offscreen layout capture (hidden-window backbuffer unavailable): " + name);
            }
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            bool visible = texture.GetPixels32().Any(p => p.r > 80 || p.g > 80 || p.b > 80);
            Destroy(texture); Check(visible, "Screenshot is blank: " + name);
        }

        private Texture2D CaptureOffscreen()
        {
            var camera = Camera.allCameras.OrderByDescending(c => c.depth).First();
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var cameras = canvases.Select(c => c.worldCamera).ToArray();
            var distances = canvases.Select(c => c.planeDistance).ToArray();
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                foreach (var canvas in canvases)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + 1;
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                var result = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); result.Apply(); return result;
            }
            finally
            {
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceOverlay;
                    canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i];
                }
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target); Canvas.ForceUpdateCanvases();
            }
        }
        private IEnumerator Wait(Func<bool> condition, string context)
        {
            float until = Time.realtimeSinceStartup + 35;
            while (!condition()) { Check(Time.realtimeSinceStartup < until, "Timed out: " + context); yield return null; }
        }
        private static void Click(string name)
        {
            var button = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name && b.isActiveAndEnabled);
            Check(button != null && button.interactable, "Button unavailable: " + name); button.onClick.Invoke();
        }
        private void Update() { if (!finished && Time.realtimeSinceStartup > deadline) Finish(false); }
        private void Finish(bool success)
        {
            if (finished) return; finished = true;
            if (audioEvent.isValid()) { audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.release(); }
            if (!success) { options?.RevertDisplay(); RestorePreference(); }
            Debug.Log("[OptionsProcess] result=" + (success ? "PASS" : "FAIL") + " phase=" + phase);
            File.WriteAllText(Path.Combine(directory, phase + "-result.txt"), success ? "PASS" : "FAIL");
            Application.Quit(success ? 0 : 1);
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static string Arg(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
    }
}
#endif
