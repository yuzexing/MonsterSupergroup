using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit development fixture input and passive evidence collection, through production lifecycles.
    // No fixture input runs in the continuous preview or ordinary gameplay.
    public sealed class LimboImpObservation : MonoBehaviour
    {
        private StreamWriter log;
        private NetworkEnemySimulationWorld world;
        private readonly Dictionary<EnemyProjectileKey, (EnemyProjectileLaunch launch, BulletProjectile bullet)> flights = new();
        private readonly Dictionary<uint, string> phases = new();
        private readonly HashSet<uint> initialized = new();
        private bool fixture, positioned, paused, pauseFinished, handoffWarning, handoffFlight, removedShooter;
        private float nextSample, pauseEnd;
        private int quadrant = -1;
        private Vector2 anchor, desired;
        private EnemyProjectileLaunch latest;
        private ulong dodged;
        private readonly Dictionary<NetworkEnemySimulationAgent, Action<EnemyAttackPresentationEdge>> listeners = new();

        private void Start()
        {
            fixture = LimboReferenceLaunch.Profile == "imp-v0" || LimboReferenceLaunch.Profile == "imp-v1";
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory, "imp-observation.jsonl")) { AutoFlush = true };
            Write("mode", fixture ? "ISOLATED: weapons disabled, explicit positioning, healing, reset, pause and target switching" : "CONTINUOUS: passive observation only");
        }
        private void Write(string kind, string detail = "", object data = null)
        {
            log?.WriteLine(JsonUtility.ToJson(new Row { kind = kind, detail = detail,
                realtime = Time.realtimeSinceStartupAsDouble, combat = EnemySimulationClock.CombatNow,
                elapsed = NetworkCombatWorld.Instance != null ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Elapsed : 0,
                payload = data == null ? "" : JsonUtility.ToJson(data) }));
        }
        private void Update()
        {
            if (world == null && NetworkEnemySimulationWorld.Instance != null)
            {
                world = NetworkEnemySimulationWorld.Instance;
                world.EnemyProjectileAccepted += Accepted;
                world.EnemyProjectilePresented += Presented;
                world.EnemyProjectileTerminated += Terminated;
                world.ReferenceProjectileHitGranted += Granted;
            }
            if (world == null || NetworkClient.localPlayer == null || NetworkCombatWorld.Instance == null) return;
            double elapsed = NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Elapsed;
            var enemies = FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None)
                .Where(e => e.ProductEnemyInitialized && e.Birth.Enabled && e.Birth.SourceEnemy == "Imp").ToArray();
            foreach (var enemy in enemies)
            {
                var action = enemy.CaptureCurrentCheckpoint();
                string phase = enemy.Assignment.Epoch + "/" + action.Movement.Runtime.Action.ActionId + "/" + action.Movement.Runtime.Action.Phase;
                if (!phases.TryGetValue(enemy.netId, out var previous) || previous != phase)
                { phases[enemy.netId] = phase; Write("phase", enemy.netId + "/" + (enemy.Authority.RunsCombatDecisions ? "simulator" : "replica"), action); }
                if (initialized.Add(enemy.netId))
                {
                    Action<EnemyAttackPresentationEdge> listen = edge => Write("presentation-edge", enemy.netId.ToString(), edge);
                    listeners[enemy] = listen; enemy.AttackPresentationChanged += listen;
                    var attack = enemy.GetComponent<EnemyProjectileAttack>();
                    Write("attack-config", enemy.netId.ToString(), new AttackRow { w = attack.WarningTime, a = attack.AttackTime, r = attack.RecoveryTime,
                        damage = enemy.GetComponent<EnemyController>().stats.Damage });
                }
            }
            if (Time.realtimeSinceStartup >= nextSample)
            {
                nextSample = Time.realtimeSinceStartup + .2f;
                foreach (var pair in flights)
                    if (pair.Value.bullet != null)
                    {
                        bool hasView = world.TryReadReferenceFlightView(pair.Key, out var view);
                        var rig = FindFirstObjectByType<AstralShift.HellMaiden.CameraFX.GameplayCameraRig>();
                        Write("flight", "", new FlightRow { key = pair.Key, position = pair.Value.bullet.transform.position,
                            age = EnemySimulationClock.CombatNow - pair.Value.launch.FiredAt, instance = pair.Value.bullet.GetInstanceID(),
                            hasServerView = hasView, serverView = view, localView = rig != null ? GameplayCameraGeometry.ViewBounds(rig.GameCamera) : default });
                    }
            }
            if (fixture && elapsed > 1 && elapsed < 88) DriveFixture(elapsed, enemies);
        }
        private void DriveFixture(double elapsed, NetworkEnemySimulationAgent[] enemies)
        {
            var local = NetworkClient.localPlayer;
            var player = local.GetComponent<PlayerMovement>();
            local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
            player.StopMovement();
            if (!positioned) { anchor = local.transform.position; desired = anchor; positioned = true; Write("fixture-position", anchor.ToString()); }
            if (local.GetComponent<CombatantBehaviour>().CurrentHealth < 250)
            { Write("fixture-heal", "IncreaseHealth(500), maximum remains 500"); player.IncreaseHealth(500); }

            // Four directions, nine seconds each; at least two uninterrupted cycles per quadrant.
            int q = Math.Min(3, (int)((elapsed - 2) / 9));
            var enemy = enemies.FirstOrDefault(e => e.IsCanonicalAlive);
            var remote = NetworkServer.active ? NetworkServer.connections.Values.Select(c => c.identity)
                .FirstOrDefault(i => i != null && i.netId != local.netId) : null;
            bool targetRemote = LimboReferenceLaunch.Argument("--limbo-fixture-target=") == "client";
            if (NetworkServer.active && enemy != null && elapsed >= 2 && elapsed < 37 && q != quadrant)
            {
                quadrant = q;
                Vector2[] offsets = { new(6, 3), new(-6, 3), new(-6, -3), new(6, -3) };
                var target = targetRemote && remote != null ? remote : local;
                world.RepositionReferenceEnemy(enemy, (Vector2)target.transform.position + offsets[q]);
                world.RequestTargetChange(enemy.netId, target.netId, EnemyTargetChangeReason.Forced);
                Write("fixture-reset", "quadrant " + q + "; production reposition/reset (not an object respawn)");
            }
            // Dodge after a real launch. Host stays near the aim center; remote viewport is deliberately distant.
            if (elapsed >= 38 && elapsed < 57)
            {
                if (!NetworkServer.active) desired = anchor + new Vector2(30, 0);
                else if (latest.Key.ActionId != 0 && latest.Key.ActionId != dodged)
                { dodged = latest.Key.ActionId; desired = anchor + new Vector2(0, desired.y > anchor.y ? -4 : 4); Write("fixture-dodge"); }
            }
            else if (elapsed >= 57) desired = anchor;
            local.transform.position = desired;
            var body = local.GetComponent<Rigidbody2D>(); if (body != null) { body.position = desired; body.linearVelocity = Vector2.zero; }

            if (!NetworkServer.active) return;
            if (!paused && !pauseFinished && elapsed >= 44 && flights.Count > 0)
            { paused = true; pauseEnd = Time.realtimeSinceStartup + 3; Time.timeScale = 0; Write("fixture-pause"); }
            if (paused && Time.realtimeSinceStartup >= pauseEnd)
            { paused = false; pauseFinished = true; Time.timeScale = 1; Write("fixture-resume"); }
            var current = enemy != null ? enemy.CaptureCurrentCheckpoint().Movement : default;
            if (enemy != null && !enemy.Authority.RunsCombatDecisions) world.Registry.TryGetLatestSnapshot(enemy.netId, out current);
            if (remote != null && enemy != null && elapsed >= 59 && !handoffWarning &&
                current.Runtime.Action.Phase == EnemyAttackPresentationPhase.Warning)
            {
                handoffWarning = true;
                Write("fixture-handoff-warning", world.RequestTargetChange(enemy.netId, targetRemote ? local.netId : remote.netId, EnemyTargetChangeReason.Forced).ToString(), current);
            }
            if (remote != null && enemy != null && elapsed >= 68 && !handoffFlight && flights.Count > 0)
            {
                handoffFlight = true;
                Write("fixture-handoff-flight", world.RequestTargetChange(enemy.netId, targetRemote ? remote.netId : local.netId, EnemyTargetChangeReason.Forced).ToString(), current);
            }
            if (elapsed >= 78 && enemy != null && flights.Count > 0 && !removedShooter)
            {
                removedShooter = true; Write("fixture-destroy-shooter", enemy.netId.ToString());
                NetworkServer.Destroy(enemy.gameObject);
            }
        }
        private void Accepted(EnemyProjectileLaunch value) { latest = value; Write("accepted", "", value); }
        private void Presented(EnemyProjectileLaunch value, BulletProjectile bullet)
        { flights[value.Key] = (value, bullet); latest = value; Write("presented", "instance=" + bullet.GetInstanceID(), value); }
        private void Terminated(EnemyProjectileTermination value)
        { Write("terminated", "", value); flights.Remove(value.Key); }
        private void Granted(EnemyProjectileLaunch value, uint target) { Write("hit-grant", "target=" + target, value); }
        private void OnDestroy()
        {
            if (fixture && paused) Time.timeScale = 1;
            if (world != null)
            {
                world.EnemyProjectileAccepted -= Accepted; world.EnemyProjectilePresented -= Presented;
                world.EnemyProjectileTerminated -= Terminated; world.ReferenceProjectileHitGranted -= Granted;
            }
            log?.Dispose();
            foreach (var pair in listeners) if (pair.Key != null) pair.Key.AttackPresentationChanged -= pair.Value;
        }
        [Serializable] private class Row { public string kind, detail, payload; public double realtime, combat, elapsed; }
        [Serializable] private class AttackRow { public float w, a, r; public int damage; }
        [Serializable] private class FlightRow { public EnemyProjectileKey key; public Vector3 position; public double age; public int instance; public bool hasServerView; public Bounds serverView, localView; }
    }
}
