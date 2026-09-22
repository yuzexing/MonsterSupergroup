using System;
using System.Collections;
using System.IO;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Opt-in visual fixture, using the real presentation/pool APIs; never admits gameplay damage.
    public sealed class WeaponEffectsObservation : MonoBehaviour
    {
        private LimboObservationLog log;
        private PlayerMovement owner;
        private WeaponBehaviour emitter;
        private WeaponData data;
        private ProjectileAttack shot;
        private ProjectilePresentationStats stats;
        private SummonPresentationState summon;
        private int weapon = 1, angle;
        private float size = 1, nextSample;
        private ulong action;
        private AttackElement element;
        private string run;
        private bool automatic;
        private int observedRenderers;
        // UI order is not the source/database identity (orbit=6, trail=8, summon=402).
        private static readonly uint[] WeaponIds = { 0, 1, 2, 3, 6, 8, 402 };
        private AttackElement[] Elements => weapon == 1 || weapon == 4 || weapon == 7 ? new[] { AttackElement.Default } :
            weapon == 3 || weapon == 5 ? new[] { AttackElement.Fire, AttackElement.Poison } :
            new[] { AttackElement.Default, AttackElement.Fire, AttackElement.Poison };

        private IEnumerator Start()
        {
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new LimboObservationLog(Path.Combine(LimboReferenceLaunch.OutputDirectory, "weapon-effects.jsonl"));
            automatic = LimboReferenceLaunch.Argument("--limbo-effects-case=") == "smoke";
            Note("configuration", "Presentation-only; normal weapons suppressed; explicit direction/size/element/phase controls. No damage, auto-screenshots or pressure evidence.");
            while (owner == null || string.IsNullOrEmpty(run)) yield return null;
            if (!automatic) yield break;
            for (weapon = 1; weapon <= 7; weapon++)
            foreach (var variant in Elements)
            {
                element = variant; angle = (weapon * 45) % 360; Replay();
                yield return new WaitForSecondsRealtime(weapon == 7 ? 5 : 1);
                if (weapon == 6)
                {
                    for (int p = 0; p < 3; p++) { NextSummonPhase(); yield return new WaitForSecondsRealtime(.8f); }
                }
                if (shot != null) shot.TerminatePresentation(ProjectilePresentationPhase.Hit, shot.transform.position);
                yield return new WaitForSecondsRealtime(.3f);
            }
            Release();
            Note("completed", "renderersObserved=" + observedRenderers + "; presentation-only, manual visual acceptance pending");
            log.Flush();
            if (LimboReferenceLaunch.Argument("--limbo-wait-for=") == "2")
            {
                string dir = Directory.GetParent(LimboReferenceLaunch.OutputDirectory).FullName;
                string role = LimboReferenceLaunch.Argument("--limbo-role=");
                File.WriteAllText(Path.Combine(dir, role + "-effects-finished"), "done");
                double deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (!File.Exists(Path.Combine(dir, (role == "host" ? "client" : "host") + "-effects-finished")) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            }
            var manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            if (NetworkServer.active) manager.StopHost(); else manager.StopClient();
            yield return new WaitForSecondsRealtime(2);
            Application.Quit(observedRenderers > 0 ? 0 : 1);
        }

        private void Update()
        {
            if (NetworkClient.localPlayer == null || NetworkCombatWorld.Instance == null || BootGameplayNetworkManager.CombatHasEnded) { Release(); return; }
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            string current = NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.RunId;
            if (owner != player || run != current) { Release(); owner = player; run = current; }
            owner.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
            if (emitter is CirclingAttackBehaviour orbit) orbit.TickPresentation(Time.deltaTime);
            if (emitter is PlayerBeamAttackBehaviour beam) beam.TickPresentation(Time.deltaTime);
            if (emitter is SummonAttackBehaviour pet) pet.TickPresentation(Time.deltaTime);
            if (Time.realtimeSinceStartup >= nextSample)
            {
                nextSample = Time.realtimeSinceStartup + 1;
                int renderers = 0, particles = 0;
                foreach (var effect in FindObjectsByType<GameplayPlanarEffect>(FindObjectsSortMode.None))
                    foreach (var r in effect.GetComponentsInChildren<Renderer>())
                        if (r.enabled && GameplayPlanarEffect.UsesPlanarMaterial(r)) { renderers++; if (r is ParticleSystemRenderer) particles += r.GetComponent<ParticleSystem>().particleCount; }
                observedRenderers += renderers;
                Note("sample", $"weapon={weapon};renderers={renderers};particles={particles};size={size};angle={angle};element={element}");
            }
        }

        private void Replay()
        {
            if (owner == null) owner = NetworkClient.localPlayer?.GetComponent<PlayerMovement>();
            if (owner == null) return;
            Release(); action++;
            var db = owner.GetComponent<NetworkPlayerBootstrap>().ResolveSharedRuntimeDatabase();
            float baseSpeed, baseDuration;
            if (weapon == 7)
            {
                var definition = owner.GetComponent<NetworkPlayerUltimate>().Definition;
                emitter = Instantiate(definition.ultimateAttackWeaponBehaviour, owner.transform);
                baseSpeed = definition.BaseStats.speed; baseDuration = definition.BaseStats.duration;
                ((DanteUltimateAttack)emitter).InitializePresentationReplica(owner);
            }
            else
            {
                if (!db.TryGetWeaponData(WeaponIds[weapon], out data)) throw new InvalidOperationException("Weapon absent: " + WeaponIds[weapon]);
                emitter = Instantiate(data.WeaponPrefab, owner.transform); baseSpeed = data.BaseStats.speed; baseDuration = data.BaseStats.duration;
                switch (emitter)
                {
                    case MeleeAttackBehaviour m: m.InitializePresentationReplica(data.ID, owner); break;
                    case ProjectileAttackBehaviour p: p.InitializePresentationReplica(data.ID, owner); break;
                    case PlayerBeamAttackBehaviour b: b.InitializePresentationReplica(data.ID, owner); break;
                    case CirclingAttackBehaviour o: o.InitializePresentationReplica(data.ID, owner); break;
                    case DashAttackBehaviour d: d.InitializePresentationReplica(data.ID, owner); break;
                    case SummonAttackBehaviour s: s.InitializePresentationReplica(data.ID, owner); s.enabled = false; break;
                }
            }
            stats = new ProjectilePresentationStats { SizeMultiplierSum = size - 1, EffectiveSpeed = baseSpeed,
                Duration = baseDuration, ProjectileCount = 1, BaseProjectileCount = 1 };
            Vector2 aim = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            switch (emitter)
            {
                case MeleeAttackBehaviour m:
                    m.PlayPresentation(new MeleePresentationSpawn(1, new MeleePresentationKey(action, 0), m.prefab.transform.localPosition, aim, -1, stats), 0); break;
                case ProjectileAttackBehaviour p:
                    shot = p.PlayPresentation(new ProjectilePresentationSpawn(2, new ProjectilePresentationKey(action, 0), owner.transform.position, aim, element, true, stats), 0); break;
                case PlayerBeamAttackBehaviour b:
                    b.PlayPresentation(new BeamPresentationSpawn(3, new BeamPresentationKey(action, 0), 1, aim, element, baseDuration, stats), 0); break;
                case CirclingAttackBehaviour o:
                    o.PlayPresentation(new OrbitPresentationSpawn(data.ID, new OrbitPresentationKey(action, 0), 1, angle * Mathf.Deg2Rad, o.baseRadius, o.baseSpeed, baseDuration, stats), 0); break;
                case DashAttackBehaviour d:
                    d.PlayPresentation(new TrailPresentationSpawn(data.ID, action, action, element, owner.transform.position, .6f, baseDuration, .2f, stats), 0);
                    for (uint i = 0; i < 5; i++) d.ApplyPresentationPoint(new TrailPresentationPoint(data.ID, action, i, (Vector2)owner.transform.position + aim * i, i * .1f), 0);
                    d.ApplyPresentationSamplingEnded(new TrailPresentationSamplingEnded(data.ID, action, .6f), 0); break;
                case SummonAttackBehaviour s:
                    summon = new SummonPresentationState { WeaponId = data.ID, PetId = action, PhaseSequence = 1, Phase = SummonPhase.Birth,
                        Element = element, Stats = stats, Pose = new SummonPose { Position = owner.transform.position + (Vector3)aim * 2,
                            RotationPivotEuler = new Vector3(0, 180 - angle, 0), MoveAnimationSpeed = 1 } };
                    s.ApplyPresentationState(summon, 0); break;
                case DanteUltimateAttack u:
                    u.PlayPresentation(new UltimatePresentationSpawn(owner.GetComponent<NetworkPlayerUltimate>().Definition.Id, action, stats), 0); break;
            }
            Note("replay", $"weapon={weapon};sourceId={(weapon == 7 ? 0 : data.ID)};element={element};angle={angle};size={size};source-duration={baseDuration}");
        }
        private void NextSummonPhase()
        {
            if (!(emitter is SummonAttackBehaviour s)) return;
            summon.Phase = summon.Phase < SummonPhase.AttackEnter ? SummonPhase.AttackEnter : summon.Phase == SummonPhase.AttackEnter ? SummonPhase.AttackMain : SummonPhase.AttackExit;
            summon.PhaseSequence++; summon.PhaseElapsedSeconds = 0; summon.AttackEventId = action;
            s.ApplyPresentationState(summon, 0); Note("summon-phase", summon.Phase.ToString());
        }
        private void Release()
        {
            if (emitter == null) return;
            switch (emitter)
            {
                case MeleeAttackBehaviour m: m.DisposePresentationReplica(); break;
                case ProjectileAttackBehaviour p: p.DisposePresentationReplica(); break;
                case PlayerBeamAttackBehaviour b: b.DisposePresentationReplica(); break;
                case CirclingAttackBehaviour o: o.DisposePresentationReplica(); break;
                case DashAttackBehaviour d: d.DisposePresentationReplica(); break;
                case SummonAttackBehaviour s: s.DisposePresentationReplica(); break;
                case DanteUltimateAttack u: u.DisposePresentationReplica(); break;
            }
            Destroy(emitter.gameObject); emitter = null; shot = null;
        }
        private void Note(string kind, string detail) => log?.WriteLine(JsonUtility.ToJson(new Row { kind = kind, detail = detail, run = run, real = Time.realtimeSinceStartupAsDouble }));
        [Serializable] private class Row { public string kind, detail, run; public double real; }
        private void OnGUI()
        {
            if (automatic) return;
            GUILayout.BeginArea(new Rect(15, 90, 440, 265), GUI.skin.box);
            GUILayout.Label("Effects only / no damage / current gameplay camera");
            GUILayout.Label($"{new[] { "", "Pen", "Wisp", "Breath", "Orbit", "Trail", "Summon", "Ultimate" }[weapon]}  {element}  {angle} degrees  size +{(size - 1) * 100}%");
            if (GUILayout.Button("Next weapon")) { weapon = weapon % 7 + 1; element = Elements[0]; Replay(); }
            if (GUILayout.Button("Next element")) { var values = Elements; element = values[(Array.IndexOf(values, element) + 1) % values.Length]; Replay(); }
            if (GUILayout.Button("Direction +45 degrees")) { angle = (angle + 45) % 360; Replay(); }
            if (GUILayout.Button("Size 1 / 2")) { size = size == 1 ? 2 : 1; Replay(); }
            if (GUILayout.Button("Replay / reuse")) Replay();
            if (weapon == 6 && GUILayout.Button("Summon: next attack phase")) NextSummonPhase();
            if (GUILayout.Button("Cancel / clear")) Release();
            GUILayout.EndArea();
        }
        private void OnDestroy() { Release(); log?.Dispose(); }
    }
}
