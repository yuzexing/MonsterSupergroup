using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Combat.Traps;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Passive evidence only. No health, position, clock, RNG, or spawn changes.
    [DefaultExecutionOrder(200)]
    public sealed partial class LimboSpatialObservation : MonoBehaviour
    {
        private LimboObservationLog output;
        private readonly Dictionary<uint, BarrierPhase> phases = new Dictionary<uint, BarrierPhase>();
        private readonly Dictionary<uint, double> settledCaptures = new Dictionary<uint, double>();
        private string run;
        private double nextSample;
        private bool completed;
        private double nextRenderSample;
        [Serializable] private class FireRenderSample
        {
            public string name, shader;
            public int particles, layer;
            public bool playing, visible, enabled;
            public Vector3 position;
            public Vector3 firstParticleWorld;
            public int simulationSpace;
            public Bounds bounds;
        }
        [Serializable] private class Sample
        {
            public string kind, role, run;
            public double elapsed, combat, realtime;
            public uint id;
            public ReferenceTrapSnapshot state;
            public Vector3[] worldEdgePoints;
            public float edgeRadius;
            public uint localPlayer;
            public Vector2 localPosition, localVelocity;
            public bool localPlayerTouchesBarrier;
            public float timeScale;
            public int slots, states, collisionAreas, livingWarningRoots;
            public Vector3 cameraPosition;
            public float cameraFov;
            public FireRenderSample[] fireRenderers;
        }
        private void LateUpdate()
        {
            var world = NetworkEnemySimulationWorld.Instance;
            var combat = NetworkCombatWorld.Instance;
            if (world == null || combat == null || !NetworkClient.active) return;
            var progress = combat.GetComponent<NetworkWaveProgress>().Snapshot;
            if (string.IsNullOrEmpty(progress.RunId)) return;
            if (output == null)
            {
                Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
                output = new LimboObservationLog(Path.Combine(LimboReferenceLaunch.OutputDirectory, "spatial-observation.jsonl"));
            }
            if (run != progress.RunId) { run = progress.RunId; phases.Clear(); settledCaptures.Clear(); completed = false; nextSample = 0; ResetValidation(); }
            TickValidation(world, progress);
            bool sample = EnemySimulationClock.CombatNow >= nextSample;
            if (sample) nextSample = EnemySimulationClock.CombatNow + (LimboReferenceLaunch.Light ? 1 : .25);
            foreach (uint id in phases.Keys.Where(id => !world.ReferenceTraps.ContainsKey(id)).ToArray())
            {
                output.WriteLine(JsonUtility.ToJson(Record("removed", progress.Elapsed, id, default)));
                phases.Remove(id);
                settledCaptures.Remove(id);
            }
            foreach (var pair in world.ReferenceTraps)
            {
                if (pair.Value.Round != NetworkEnemySimulationWorld.CurrentRound) continue;
                bool changed = !phases.TryGetValue(pair.Key, out var phase) || phase != pair.Value.Phase;
                if (!LimboReferenceLaunch.Light && !changed && settledCaptures.TryGetValue(pair.Key, out var captureAt) && EnemySimulationClock.CombatNow >= captureAt)
                {
                    // Phase-entry captures can precede the first emitted particle. Also retain the rendered phase after emission.
                    ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory, $"trap-{run}-{pair.Key}-{pair.Value.Phase}-settled.png"));
                    settledCaptures.Remove(pair.Key);
                }
                if (!sample && !changed) continue;
                var record = Record(changed ? "phase" : "sample", progress.Elapsed, pair.Key, pair.Value);
                if (!LimboReferenceLaunch.Light && world.TryGetReferenceBarrier(pair.Key, out var barrier))
                {
                    var edge = barrier.GetComponentInChildren<EdgeCollider2D>();
                    record.worldEdgePoints = edge.points.Select(p => edge.transform.TransformPoint(p + edge.offset)).ToArray();
                    record.edgeRadius = edge.edgeRadius;
                    if (changed || Time.realtimeSinceStartupAsDouble >= nextRenderSample)
                    {
                        nextRenderSample = Time.realtimeSinceStartupAsDouble + 5;
                        var camera = Camera.main;
                        if (camera != null) { record.cameraPosition = camera.transform.position; record.cameraFov = camera.fieldOfView; }
                        record.fireRenderers = barrier.GetComponentsInChildren<ParticleSystem>()
                            .OrderBy(ps => ps.transform.position.y).Where(ps => ps.particleCount > 0).Take(6).Select(ps =>
                            {
                                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                                var first = new ParticleSystem.Particle[1]; ps.GetParticles(first);
                                var main = ps.main;
                                Vector3 particlePosition = main.simulationSpace == ParticleSystemSimulationSpace.Local ? ps.transform.TransformPoint(first[0].position) :
                                    main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null ? main.customSimulationSpace.TransformPoint(first[0].position) : first[0].position;
                                return new FireRenderSample { name = ps.name, particles = ps.particleCount, playing = ps.isPlaying,
                                    visible = renderer.isVisible, enabled = renderer.enabled, position = ps.transform.position,
                                    firstParticleWorld = particlePosition, simulationSpace = (int)main.simulationSpace,
                                    bounds = renderer.bounds, shader = renderer.sharedMaterial?.shader.name, layer = renderer.sortingLayerID };
                            }).ToArray();
                    }
                    if (NetworkClient.localPlayer != null)
                        record.localPlayerTouchesBarrier = NetworkClient.localPlayer.GetComponentsInChildren<Collider2D>()
                            .Any(c => !c.isTrigger && edge.IsTouching(c));
                }
                output.WriteLine(JsonUtility.ToJson(record));
                if (changed)
                {
                    phases[pair.Key] = pair.Value.Phase;
                    if (!LimboReferenceLaunch.Light)
                    {
                        settledCaptures[pair.Key] = EnemySimulationClock.CombatNow + .35;
                        ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory, $"trap-{run}-{pair.Key}-{pair.Value.Phase}.png"));
                    }
                }
            }
            if (!completed && (progress.Phase == WavePhase.Completed || BootGameplayNetworkManager.CombatHasEnded))
            {
                completed = true;
                var record = Record("completed-cleanup", progress.Elapsed, 0, default);
                record.states = world.ReferenceTraps.Count;
                record.collisionAreas = FindObjectsByType<BarrierTrap>(FindObjectsSortMode.None).Count(t => t.NetworkCollisionEnabled);
                record.livingWarningRoots = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                    .Count(ps => ps.name.StartsWith("ReferenceTrapWarning", StringComparison.Ordinal) && ps.IsAlive(true));
                output.WriteLine(JsonUtility.ToJson(record));
                if (!LimboReferenceLaunch.Light) ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory, $"spatial-completed-{run}.png"));
            }
        }
        private Sample Record(string kind, double elapsed, uint id, ReferenceTrapSnapshot state) => new Sample {
            kind = kind, role = LimboReferenceLaunch.Argument("--limbo-role="), run = run, elapsed = elapsed,
            combat = EnemySimulationClock.CombatNow, realtime = Time.realtimeSinceStartupAsDouble, id = id, state = state,
            timeScale = Time.timeScale,
            localPlayer = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0,
            localPosition = NetworkClient.localPlayer != null ? (Vector2)NetworkClient.localPlayer.transform.position : Vector2.zero,
            localVelocity = NetworkClient.localPlayer != null && NetworkClient.localPlayer.TryGetComponent<Rigidbody2D>(out var body) ? body.linearVelocity : Vector2.zero,
            slots = NetworkEnemySimulationWorld.Instance.ReferenceTraps.Values.Count(s => s.Occupied),
            states = NetworkEnemySimulationWorld.Instance.ReferenceTraps.Count };
        private void OnDestroy() { output?.Dispose(); output = null; }
    }
}
