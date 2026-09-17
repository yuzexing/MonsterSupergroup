using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using FMODUnity;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit, local presentation fixture. No damage, network attack admission or pressure claims.
    public sealed class WeaponAudioObservation : MonoBehaviour
    {
        private LimboObservationLog log;
        private WeaponBehaviour emitter;
        private WeaponData data;
        private PlayerMovement owner;
        private readonly List<ProjectileAttack> projectiles = new();
        private readonly List<BeamPresentationKey> beams = new();
        private readonly Dictionary<long, FMOD.Studio.EventInstance> sounds = new();
        private AttackElement element = AttackElement.Fire;
        private string run;
        private ulong attackId;
        private float nextSample;
        private bool smokeEnding;
        private bool Wisp => LimboReferenceLaunch.Profile == "audio-wisp";
        private IEnumerator Start()
        {
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new LimboObservationLog(Path.Combine(LimboReferenceLaunch.OutputDirectory, "weapon-audio.jsonl"));
            AttackAudioAudit.Changed += Audio;
            Write("fixture", "Local presentation only; normal weapon execution disabled. No damage, XP, auto-input or volume changes. Triple breath is an explicit presentation stress case.");
            if (LimboReferenceLaunch.Argument("--limbo-audio-case=") != "smoke") yield break;
            Write("test-assistance", "Explicit smoke: automated element/count/contact/cancel; exits when done. Not manual listening acceptance.");
            float deadline=Time.realtimeSinceStartup+90;
            while(emitter==null && Time.realtimeSinceStartup<deadline) yield return null;
            if(emitter==null) { Write("smoke-failed","Owner/presentation initialization timeout"); Application.Quit(1); yield break; }
            foreach(var variant in Wisp ? new[]{AttackElement.Default,AttackElement.Fire,AttackElement.Poison} : new[]{AttackElement.Fire,AttackElement.Poison})
            {
                element=variant; Fire(1);
                yield return new WaitForSeconds(0.5f);
                if(Wisp) { Contact(false); yield return new WaitForSeconds(0.2f); Contact(true); }
                yield return new WaitForSeconds(3);
                Fire(3); yield return new WaitForSeconds(0.3f); Cancel();
            }
            smokeEnding=true; Release(); yield return new WaitForSecondsRealtime(2);
            var manager=FindFirstObjectByType<BootGameplayNetworkManager>();
            if(NetworkServer.active) manager.StopHost(); else manager.StopClient();
            deadline=Time.realtimeSinceStartup+10;
            while(manager!=null && (manager.IsGameplayLoaded || manager.IsGameplayTransitioning) && Time.realtimeSinceStartup<deadline) yield return null;
            SampleSounds();
            bool clean=errors==0 && soundsPlaying==0 && starts>0 && duplicateStarts==0 && !NetworkClient.active && !NetworkServer.active;
            Write(clean?"smoke-passed":"smoke-failed",$"errors={errors};playing={soundsPlaying};starts={starts};duplicateStarts={duplicateStarts}");
            log.Flush(); Application.Quit(clean?0:1);
        }
        private void Write(string kind, string detail) => log?.WriteLine(JsonUtility.ToJson(new Row
            { kind = kind, detail = detail, run = run, role = LimboReferenceLaunch.Argument("--limbo-role="),
                realtime = Time.realtimeSinceStartupAsDouble, attack = attackId }));
        [Serializable] private class Row { public string kind, detail, run, role; public double realtime; public ulong attack; }
        private void Audio(AttackAudioAudit.Entry e)
        {
            Write("sound-edge", JsonUtility.ToJson(e));
            if(e.Result!=FMOD.RESULT.OK) errors++;
            if(e.Operation=="start" || e.Operation=="start-shot")
            { starts++; if(!started.Add(e.Instance+"/"+e.Generation)) duplicateStarts++; }
            if (e.Handle != 0) sounds[e.Handle] = new FMOD.Studio.EventInstance(new IntPtr(e.Handle));
        }
        private int errors, soundsPlaying, starts, duplicateStarts;
        private readonly HashSet<string> started=new();
        private void Update()
        {
            if (Time.realtimeSinceStartup >= nextSample) { nextSample=Time.realtimeSinceStartup+1; SampleSounds(); }
            if(smokeEnding) return;
            if (NetworkClient.localPlayer == null || NetworkCombatWorld.Instance == null || BootGameplayNetworkManager.CombatHasEnded)
            { Release(); return; }
            var currentRun = NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.RunId;
            if (emitter==null || owner != NetworkClient.localPlayer.GetComponent<PlayerMovement>() || run != currentRun)
            {
                Release(); run = currentRun; owner = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
                var db = owner.GetComponent<NetworkPlayerBootstrap>().ResolveSharedRuntimeDatabase();
                if (db == null || !db.TryGetWeaponData(Wisp ? 2u : 3u, out data)) return;
                emitter = Instantiate(data.WeaponPrefab, owner.transform);
                if (emitter is ProjectileAttackBehaviour p) p.InitializePresentationReplica(2, owner);
                if (emitter is PlayerBeamAttackBehaviour b) b.InitializePresentationReplica(3, owner);
                Write("round", "Equipped isolated presentation " + data.ID);
            }
            owner.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
            if (emitter is PlayerBeamAttackBehaviour beam) beam.TickPresentation(Time.deltaTime);
        }
        private void SampleSounds()
        {
            int playing = 0, stopping = 0;
            var expired = new List<long>();
            foreach (var pair in sounds)
                if (pair.Value.getPlaybackState(out var state) != FMOD.RESULT.OK) expired.Add(pair.Key);
                else if (state == FMOD.Studio.PLAYBACK_STATE.STOPPING) stopping++;
                else if (state != FMOD.Studio.PLAYBACK_STATE.STOPPED) playing++;
            foreach (long id in expired) sounds.Remove(id);
            soundsPlaying=playing+stopping;
            var camera = FindFirstObjectByType<GameplayCameraRig>();
            Vector3 position = camera != null && camera.LocalAudioListener != null ? camera.LocalAudioListener.transform.position : Vector3.zero;
            Write("sound-sample", $"tracked={sounds.Count};playing={playing};stopping={stopping};listeners={StudioListener.ListenerCount};listener={position};timeScale={Time.timeScale}");
        }
        private void Fire(int count)
        {
            if (emitter == null) return;
            attackId++;
            Write("attack", $"element={element};count={count};presentation-only=true");
            var stats = new ProjectilePresentationStats { DamageMultiplierSum=1, SpeedMultiplierSum=1,
                SizeMultiplierSum=1, DurationMultiplierSum=1, EffectiveSpeed=data.BaseStats.speed,
                Duration=data.BaseStats.duration, ProjectileCount=count, BaseProjectileCount=count };
            Vector2 direction = owner.attackDirection.sqrMagnitude > .001f ? owner.attackDirection.normalized : Vector2.right;
            for (ushort i=0;i<count;i++)
                if (emitter is ProjectileAttackBehaviour p)
                {
                    Vector2 aim = Quaternion.Euler(0,0,(i-(count-1)*.5f)*18) * direction;
                    var shot = p.PlayPresentation(new ProjectilePresentationSpawn(2,new ProjectilePresentationKey(attackId,i),owner.transform.position,aim,element,true,stats),0,x=>projectiles.Remove(x));
                    if (shot != null) projectiles.Add(shot);
                }
                else if (emitter is PlayerBeamAttackBehaviour b)
                {
                    var key = new BeamPresentationKey(attackId,i);
                    b.PlayPresentation(new BeamPresentationSpawn(3,key,count,direction,element,data.BaseStats.duration,stats),0,_=>beams.Remove(key));
                    beams.Add(key);
                }
        }
        private void Contact(bool final)
        {
            Write("controlled-contact", final ? "final" : "intermediate; visual only, no sound or damage");
            foreach (var p in projectiles.ToArray()) if (p != null)
                p.TerminatePresentation(final ? ProjectilePresentationPhase.Hit : ProjectilePresentationPhase.Impact,p.transform.position);
        }
        private void Cancel()
        {
            Write("cancel", "Explicit fixture cancellation");
            foreach (var p in projectiles.ToArray()) if (p != null) p.TerminatePresentation(ProjectilePresentationPhase.Cancelled,p.transform.position);
            if (emitter is PlayerBeamAttackBehaviour b) foreach (var key in beams.ToArray()) b.TerminatePresentation(key);
            projectiles.Clear(); beams.Clear();
        }
        private void Release()
        {
            if (emitter != null)
            {
                Cancel();
                if (emitter is ProjectileAttackBehaviour p) p.DisposePresentationReplica();
                if (emitter is PlayerBeamAttackBehaviour b) b.DisposePresentationReplica();
                Destroy(emitter.gameObject); emitter=null;
            }
            owner=null;
        }
        private void OnGUI()
        {
            if (emitter == null) return;
            GUILayout.BeginArea(new Rect(12,12,420,255), GUI.skin.box);
            GUILayout.Label(Wisp ? "WISP AUDIO - local listening fixture" : "BREATH AUDIO - local listening fixture");
            GUILayout.Label("No combat damage. Move / aim normally. Settings keep your volume.");
            GUILayout.BeginHorizontal();
            if (Wisp && GUILayout.Button("Normal")) element=AttackElement.Default;
            if (GUILayout.Button("Fire")) element=AttackElement.Fire;
            if (GUILayout.Button("Poison")) element=AttackElement.Poison;
            GUILayout.EndHorizontal();
            GUILayout.Label("Selected: " + element);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Single")) Fire(1);
            if (GUILayout.Button("Three (test)")) Fire(3);
            GUILayout.EndHorizontal();
            if (Wisp)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Piercing contact")) Contact(false);
                if (GUILayout.Button("Final hit")) Contact(true);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Cancel active sounds / effects")) Cancel();
            GUILayout.EndArea();
        }
        private void OnDestroy() { Release(); AttackAudioAudit.Changed-=Audio; log?.Dispose(); }
    }
}
