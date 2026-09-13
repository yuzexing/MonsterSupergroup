using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using FMODUnity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Opt-in standalone acceptance fixture. References are injected into the build copy of Boot.</summary>
    public sealed class WispPresentationProcessProbe : MonoBehaviour
    {
        public WeaponData Weapon;
        private string directory;
        private Camera captureCamera;
        private ProjectileAttackBehaviour emitter;
        private GameObject ownerRoot;
        private readonly List<ProjectileAttack> projectiles = new List<ProjectileAttack>();
        private bool finished;
        private float peak;
        private FMOD.Studio.EventInstance audioEvent;
        private FMOD.Studio.Bus master, effects, music;
        private float oldMaster, oldEffects, oldMusic;
        private bool busesReady;
        private FMOD.DSP dsp;

        private void Awake()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--wisp-probe="));
            if (arg == null) { Destroy(gameObject); return; }
            directory = arg.Substring("--wisp-probe=".Length);
            Directory.CreateDirectory(directory); DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            if (directory == null) yield break;
            Application.runInBackground = true;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    var current = stack.Peek();
                    if (!current.MoveNext()) { stack.Pop(); continue; }
                    next = current.Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
            Finish(true);
        }

        private IEnumerator Run()
        {
            if (Environment.GetCommandLineArgs().Contains("--dedicated-server"))
            {
                Check(!OptionalAudio.CreateInstance(new EventReference { Guid = new FMOD.GUID {
                    Data1 = -450359305, Data2 = 1330102086, Data3 = -2065969494, Data4 = -435797981 } }).isValid(), "Dedicated server created audio");
                yield break;
            }
            if (StudioListener.ListenerCount == 0) new GameObject("Wisp audio listener").AddComponent<StudioListener>();
            yield return new WaitForSecondsRealtime(3f);
            var studio = RuntimeManager.StudioSystem;
            studio.getBus("bus:/", out master); studio.getBus("bus:/sx", out effects); studio.getBus("bus:/mx", out music);
            Check(master.isValid() && effects.isValid() && music.isValid(), "Required audio buses missing");
            master.getVolume(out oldMaster); effects.getVolume(out oldEffects); music.getVolume(out oldMusic);
            busesReady = true; master.setVolume(1); effects.setVolume(1); music.setVolume(0);
            master.lockChannelGroup(); studio.flushCommands(); master.getChannelGroup(out var group);
            Check(group.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, out dsp) == FMOD.RESULT.OK, "Missing audio output DSP");
            dsp.setMeteringEnabled(true, true);
            foreach (string suffix in new[] { "shot", "loop", "hit" })
            {
                string path = "event:/sx/plr/Sx_plr_slowprojectile_" + suffix;
                audioEvent = RuntimeManager.CreateInstance(path);
                audioEvent.set3DAttributes(RuntimeUtils.To3DAttributes(Vector3.zero));
                master.setVolume(1); effects.setVolume(1); audioEvent.start();
                yield return Meter(1f, suffix != "loop"); float audible = peak;
                Check(audible > .00001f, "Silent audio: " + suffix);
                effects.setVolume(0); yield return new WaitForSecondsRealtime(.25f); yield return Meter(.5f, suffix != "loop");
                Check(peak < audible * .02f, "Effects volume did not mute " + suffix);
                effects.setVolume(1); master.setVolume(0); yield return new WaitForSecondsRealtime(.25f); yield return Meter(.5f, suffix != "loop");
                Check(peak < audible * .02f, "Master volume did not mute " + suffix);
                master.setVolume(1); yield return Meter(1f, suffix != "loop"); Check(peak > .00001f, "Audio did not recover " + suffix);
                audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.release(); audioEvent.clearHandle();
                File.AppendAllText(Path.Combine(directory, "audio.txt"), suffix + " peak=" + audible + "; category/master mute and restore PASS\n");
            }
            if (PoolManager.Instance == null) new GameObject("Wisp probe pool").AddComponent<PoolManager>().Init();
            ownerRoot = new GameObject("Wisp probe owner"); ownerRoot.SetActive(false);
            var owner = ownerRoot.AddComponent<PlayerMovement>();
            emitter = Instantiate(Weapon.WeaponPrefab.gameObject).GetComponent<ProjectileAttackBehaviour>();
            emitter.InitializePresentationReplica(2, owner);
            captureCamera = new GameObject("Wisp capture camera").AddComponent<Camera>();
            captureCamera.orthographic = true; captureCamera.orthographicSize = 4;
            captureCamera.transform.position = new Vector3(0, 0, -20);
            captureCamera.clearFlags = CameraClearFlags.SolidColor; captureCamera.cullingMask = 1 << 31;
            
            foreach (bool light in new[] { false, true })
            {
                captureCamera.orthographicSize = 4f;
                for (int i = 0; i < 3; i++)
                {
                    var spawn = new ProjectilePresentationSpawn(2, new ProjectilePresentationKey((ulong)i + 1, 0),
                        new Vector3((i - 1) * 3, 0), Vector2.right, new[] { AttackElement.Default, AttackElement.Fire, AttackElement.Poison }[i], true, Stats());
                    var projectile = emitter.PlayPresentation(spawn, 0);
                    foreach (Transform child in projectile.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                    projectiles.Add(projectile);
                }
                captureCamera.backgroundColor = light ? new Color(.65f, .65f, .65f) : new Color(.025f, .03f, .05f);
                yield return new WaitForSecondsRealtime(.1f); Capture((light ? "light" : "dark") + "-appear");
                yield return new WaitForSecondsRealtime(.7f); Capture((light ? "light" : "dark") + "-flight");
                foreach (var projectile in projectiles) projectile.TerminatePresentation(ProjectilePresentationPhase.Hit, projectile.transform.position);
                foreach (var impact in FindObjectsByType<AttackHitParticleEffect>(FindObjectsSortMode.None))
                {
                    Check(impact.GetComponentsInChildren<Collider2D>().All(c => !c.enabled), "Remote impact collision is enabled");
                    foreach (Transform child in impact.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                }
                projectiles.Clear();
                yield return new WaitForSecondsRealtime(.08f); Capture((light ? "light" : "dark") + "-impact");
                yield return new WaitForSecondsRealtime(2f);
                Check(FindObjectsByType<AttackHitParticleEffect>(FindObjectsSortMode.None).Length == 0, "Impact did not return to pool");
                RuntimeManager.GetEventDescription("event:/sx/plr/Sx_plr_slowprojectile_loop").getInstanceCount(out int loops);
                Check(loops == 0, "Flight loops survived projectile termination: " + loops);
            }
            for (int i = 0; i < 12; i++)
            {
                var projectile = emitter.PlayPresentation(new ProjectilePresentationSpawn(2, new ProjectilePresentationKey((ulong)i + 10, 0),
                    Vector3.zero, Vector2.right, AttackElement.Default, true, Stats()), 4f, playLaunchSound: false);
                Check(Mathf.Abs(projectile.transform.position.x - 1.6f) < .001f, "Aged projectile position did not restore");
                projectile.TerminatePresentation(ProjectilePresentationPhase.Cancelled, projectile.transform.position);
                Check(projectile.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0), "Pooled particles survived cancellation");
            }
            RuntimeManager.StudioSystem.flushCommands();
            RuntimeManager.GetEventDescription("event:/sx/plr/Sx_plr_slowprojectile_shot").getInstanceCount(out int oldShots);
            Check(oldShots == 0, "Historical restoration replayed a shot");
            yield return new WaitForSecondsRealtime(2f);
            RuntimeManager.GetEventDescription("event:/sx/plr/Sx_plr_slowprojectile_loop").getInstanceCount(out int remaining);
            Check(remaining == 0, "Cancelled loops leaked");
        }

        private static ProjectilePresentationStats Stats() => new ProjectilePresentationStats {
            DamageMultiplierSum = 0, SpeedMultiplierSum = 0, SizeMultiplierSum = 0, DurationMultiplierSum = 0,
            EffectiveSpeed = .4f, Duration = 10, ProjectileCount = 1, BaseProjectileCount = 1 };

        private IEnumerator Meter(float seconds, bool repeat)
        {
            peak = 0; float until = Time.realtimeSinceStartup + seconds, nextShot = 0;
            while (Time.realtimeSinceStartup < until)
            {
                if (repeat && Time.realtimeSinceStartup >= nextShot)
                { audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.start(); nextShot = Time.realtimeSinceStartup + .25f; }
                if (dsp.getMeteringInfo(out var input, out var output) == FMOD.RESULT.OK && output.peaklevel != null)
                    for (int i = 0; i < output.numchannels; i++) peak = Mathf.Max(peak, output.peaklevel[i]);
                yield return null;
            }
        }

        private void Capture(string name)
        {
            captureCamera.orthographicSize = name.EndsWith("impact") ? 7f : 4f;
            var target = RenderTexture.GetTemporary(1200, 800, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderPipeline.SubmitRenderRequest(captureCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                int colored = image.GetPixels32().Count(p => Math.Max(p.r, Math.Max(p.g, p.b)) - Math.Min(p.r, Math.Min(p.g, p.b)) > 30);
                Check(colored > 100, "Empty effect capture: " + name);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); if (image != null) Destroy(image); }
        }

        private void Finish(bool success)
        {
            if (finished) return; finished = true;
            foreach (var projectile in projectiles) if (projectile != null) projectile.TerminatePresentation(ProjectilePresentationPhase.Cancelled, projectile.transform.position);
            if (emitter != null) { emitter.DisposePresentationReplica(); Destroy(emitter.gameObject); }
            if (audioEvent.isValid()) { audioEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); audioEvent.release(); }
            if (busesReady) { dsp.setMeteringEnabled(false, false); master.unlockChannelGroup(); master.setVolume(oldMaster); effects.setVolume(oldEffects); music.setVolume(oldMusic); }
            Debug.Log("[WispProcess] result=" + (success ? "PASS" : "FAIL"));
            File.WriteAllText(Path.Combine(directory, "result.txt"), success ? "PASS" : "FAIL");
            Application.Quit(success ? 0 : 1);
        }

        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
