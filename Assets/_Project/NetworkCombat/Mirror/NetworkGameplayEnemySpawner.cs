using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    public enum GameplayEnemySpawnMode : byte { PerMember, Waves }

    [DisallowMultipleComponent]
    public sealed class NetworkGameplayEnemySpawner : MonoBehaviour
    {
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField, Min(0.1f)] private float spawnDistance = 5f;
        [SerializeField] private GameplayEnemySpawnMode spawnMode;
        [SerializeField] private GameplayWaveRules waveRules;
        [SerializeField] private SpriteRenderer boundaryGround;

        private readonly HashSet<ulong> spawnedForPlayers = new HashSet<ulong>();
        private readonly List<NetworkEnemySimulationEndpoint> playerBuffer = new List<NetworkEnemySimulationEndpoint>(4);
        private readonly List<RunParticipant> activeParticipants = new List<RunParticipant>(4);
        private NetworkEnemySimulationWorld world;
        private NetworkWaveProgress progress;
        private BootGameplayNetworkManager manager;
        private ServerWaveSchedule schedule;
        private WaveParameters settings;
        private string boundRunId, stoppedRunId;
        private double nextPublish;
        private ulong lastTargetParticipant;
        private Bounds groundBounds;
        private Vector2 colliderOffset;
        private float colliderRadius;
        private int runtimeMinimumSpawnHealth;
        private bool legacySubscribed;

        public GameObject EnemyPrefab => enemyPrefab;
        public int SpawnedPlayerCount => spawnedForPlayers.Count;
        public bool UsesWaves => spawnMode == GameplayEnemySpawnMode.Waves;
        public WaveProgressSnapshot ServerProgress => schedule != null ? schedule.State : WaitingSnapshot();
        public SpriteRenderer BoundaryGround => boundaryGround;

        internal void ConfigureRuntimeMinimumSpawnHealth(int minimumHealth) => runtimeMinimumSpawnHealth = Mathf.Max(0, minimumHealth);

        // Explicit legacy mode for older combat fixtures and rollback, never a missing-config fallback.
        public void Configure(GameObject prefab, float distance)
        {
            if (schedule != null) throw new System.InvalidOperationException("Cannot change an active wave run.");
            enemyPrefab = prefab != null ? prefab : throw new System.ArgumentNullException(nameof(prefab));
            spawnDistance = Mathf.Max(0.1f, distance);
            spawnMode = GameplayEnemySpawnMode.PerMember;
        }

        private void Update()
        {
            if (!NetworkServer.active) return;
            if (manager == null) manager = NetworkManager.singleton as BootGameplayNetworkManager;
            if (manager == null || !manager.IsGameplayLoaded || manager.IsGameplayTransitioning) return;
            if (world == null) world = NetworkEnemySimulationWorld.Instance;
            if (world == null) return;
            if (boundRunId != manager.Session.RunId)
            {
                Unsubscribe();
                boundRunId = manager.Session.RunId;
                schedule = null; settings = null; stoppedRunId = null;
                spawnedForPlayers.Clear(); lastTargetParticipant = 0; nextPublish = 0;
            }
            if (!UsesWaves)
            {
                if (!legacySubscribed)
                {
                    world.ServerPlayerRegistered += HandlePlayerRegistered;
                    legacySubscribed = true;
                    world.GetEligiblePlayers(playerBuffer);
                    foreach (var endpoint in playerBuffer) SpawnForPlayer(endpoint);
                }
                return;
            }
            if (!BindProgress()) return;
            double now = NetworkTime.time;
            if (schedule != null && schedule.State.Phase != WavePhase.Stopped)
            {
                CollectActiveParticipants();
                int alive = CountCanonicalEnemies();
                var before = schedule.State;
                if (schedule.Tick(now, activeParticipants.Count > 0, alive, out var opportunity))
                {
                    long missed = schedule.State.TotalSkipped - before.TotalSkipped;
                    if (missed > 0) Debug.Log($"[Waves] run={boundRunId} wave={opportunity.Wave} firstEvent={opportunity.Sequence - missed} lastEvent={opportunity.Sequence - 1} skipped={missed} reason=MissedClockSlots", this);
                    bool spawned = false;
                    string reason = "AliveLimit";
                    uint enemyId = 0, targetId = 0;
                    if (alive < settings.Limit)
                    {
                        var target = NextTarget();
                        targetId = target.AvatarId;
                        if (TryChoosePosition(target, opportunity.Sequence, out var position))
                        {
                            enemyId = SpawnEnemy(position, targetId);
                            spawned = enemyId != 0;
                            reason = spawned ? "Spawned" : "SpawnFailed";
                        }
                        else reason = "NoLegalPosition";
                    }
                    schedule.Resolve(opportunity, spawned);
                    Debug.Log($"[Waves] run={boundRunId} wave={opportunity.Wave} slot={opportunity.Index} event={opportunity.Sequence} reason={reason} enemy={enemyId} target={targetId} alive={schedule.State.Alive}", this);
                }
                if (before.Phase != schedule.State.Phase)
                    Debug.Log($"[Waves] run={boundRunId} phase={schedule.State.Phase} elapsed={schedule.State.Elapsed:F3}", this);
            }
            if (now >= nextPublish)
            {
                progress.Publish(this, ServerProgress);
                nextPublish = now + 0.2;
            }
        }

        private bool BindProgress()
        {
            if (progress == null) progress = NetworkCombatWorld.Instance != null
                ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>() : null;
            if (progress == null || !progress.isServer) return false;
            if (progress.TryClaim(this)) return true;
            Debug.LogError("Gameplay has more than one wave producer; refusing duplicate scheduling.", this);
            enabled = false; return false;
        }

        public bool CanBeginWaveRun(out string error)
        {
            error = null;
            if (!UsesWaves) return true;
            if (!isActiveAndEnabled) { error = "Gameplay wave spawner is disabled."; return false; }
            if (stoppedRunId != null) { error = "This wave run was stopped; stop the session before starting a new run."; return false; }
            if (waveRules == null) { error = "Gameplay wave rules are missing."; return false; }
            if (!waveRules.TryCapture(out _, out error)) return false;
            if (boundaryGround == null || boundaryGround.bounds.size.x <= 0 || boundaryGround.bounds.size.y <= 0)
            { error = "Gameplay requires the approved Ground boundary."; return false; }
            var controller = enemyPrefab != null ? enemyPrefab.GetComponent<EnemyController>() : null;
            if (controller == null || !(controller.collider is CircleCollider2D) ||
                enemyPrefab.GetComponent<NetworkEnemySimulationAgent>() == null || enemyPrefab.GetComponent<NetworkIdentity>() == null)
            { error = "Gameplay requires its registered network Enemy with a circle body collider."; return false; }
            if (NetworkManager.singleton == null || !NetworkManager.singleton.spawnPrefabs.Contains(enemyPrefab))
            { error = "Gameplay Enemy prefab is not registered with Mirror."; return false; }
            return true;
        }

        internal bool BeginWaveRun(string runId, out string error)
        {
            if (schedule != null && schedule.State.RunId == runId && schedule.State.Phase != WavePhase.Stopped)
            { error = null; return true; }
            if (!CanBeginWaveRun(out error)) return false;
            if (!UsesWaves) return true;
            if (!BindProgress()) { error = "The network wave World is not ready or already has a producer."; return false; }
            waveRules.TryCapture(out settings, out error);
            boundRunId = runId;
            var circle = (CircleCollider2D)enemyPrefab.GetComponent<EnemyController>().collider;
            colliderRadius = circle.radius * Mathf.Max(Mathf.Abs(circle.transform.lossyScale.x), Mathf.Abs(circle.transform.lossyScale.y));
            colliderOffset = circle.transform.TransformPoint(circle.offset) - enemyPrefab.transform.position;
            groundBounds = boundaryGround.bounds;
            schedule = new ServerWaveSchedule(runId, settings, NetworkTime.time);
            lastTargetParticipant = 0;
            progress.Publish(this, schedule.State);
            nextPublish = NetworkTime.time + 0.2;
            Debug.Log($"[Waves] run={runId} begin duration={settings.Duration} count={settings.Count} interval={settings.Interval} cap={settings.Limit}", this);
            return true;
        }

        private WaveProgressSnapshot WaitingSnapshot()
        {
            return new WaveProgressSnapshot { RunId = boundRunId, Phase = stoppedRunId != null ? WavePhase.Stopped : WavePhase.Waiting };
        }

        private void CollectActiveParticipants()
        {
            activeParticipants.Clear();
            var ledger = NetworkCombatWorld.Instance.Gateway.Ledger;
            foreach (var participant in manager.Session.Participants)
                if (participant.ConnectionState == RunConnectionState.Connected && participant.AvatarId != 0 &&
                    NetworkServer.spawned.TryGetValue(participant.AvatarId, out var identity) && identity != null &&
                    identity.GetComponent<NetworkEnemySimulationEndpoint>() != null &&
                    ledger.TryGetState(participant.AvatarId, out var state) && state.Alive)
                    activeParticipants.Add(participant);
            activeParticipants.Sort((left, right) => left.Id.CompareTo(right.Id));
        }

        public int CountCanonicalEnemies()
        {
            if (!NetworkServer.active || NetworkCombatWorld.Instance == null) return 0;
            int count = 0;
            var ledger = NetworkCombatWorld.Instance.Gateway.Ledger;
            foreach (var pair in NetworkServer.spawned)
                if (pair.Value != null && pair.Value.gameObject.scene == gameObject.scene &&
                    pair.Value.GetComponent<NetworkEnemySimulationAgent>() != null &&
                    ledger.TryGetState(pair.Key, out var state) && state.Alive) count++;
            return count;
        }

        private RunParticipant NextTarget()
        {
            var chosen = activeParticipants[0];
            foreach (var participant in activeParticipants)
                if (participant.Id > lastTargetParticipant) { chosen = participant; break; }
            lastTargetParticipant = chosen.Id;
            return chosen;
        }

        private bool TryChoosePosition(RunParticipant target, long sequence, out Vector2 position)
        {
            Vector2 center = NetworkServer.spawned[target.AvatarId].transform.position;
            for (int i = 0; i < settings.Attempts; i++)
            {
                float angle = (float)((sequence * 137.508) % 360) + 360f * i / settings.Attempts;
                position = center + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * settings.Radius;
                Vector2 bodyCenter = position + colliderOffset;
                if (bodyCenter.x - colliderRadius < groundBounds.min.x || bodyCenter.x + colliderRadius > groundBounds.max.x ||
                    bodyCenter.y - colliderRadius < groundBounds.min.y || bodyCenter.y + colliderRadius > groundBounds.max.y) continue;
                bool clear = true;
                foreach (var participant in activeParticipants)
                    if (Vector2.Distance(position, NetworkServer.spawned[participant.AvatarId].transform.position) < settings.PlayerClearance)
                    { clear = false; break; }
                if (clear) return true;
            }
            position = default; return false;
        }

        private void HandlePlayerRegistered(NetworkEnemySimulationEndpoint endpoint) => SpawnForPlayer(endpoint);
        private void SpawnForPlayer(NetworkEnemySimulationEndpoint endpoint)
        {
            if (UsesWaves || !isActiveAndEnabled || !NetworkServer.active || endpoint == null ||
                !endpoint.IsEligibleSimulationOwner || enemyPrefab == null) return;
            var participant = endpoint.GetComponent<NetworkRunParticipant>();
            ulong key = participant != null && participant.ParticipantId != 0 ? participant.ParticipantId : ((ulong)1 << 32) | endpoint.PlayerEntityId;
            if (!spawnedForPlayers.Add(key)) return;
            SpawnEnemy((Vector2)endpoint.transform.position + DirectionFor(endpoint.PlayerEntityId) * spawnDistance, endpoint.PlayerEntityId);
        }

        private uint SpawnEnemy(Vector2 position, uint targetId)
        {
            GameObject enemy = Instantiate(enemyPrefab, position, Quaternion.identity);
            var agent = enemy.GetComponent<NetworkEnemySimulationAgent>();
            if (agent == null) { Destroy(enemy); return 0; }
            agent.ConfigureRuntimeMinimumHealthOverride(runtimeMinimumSpawnHealth);
            agent.ConfigureInitialServerTarget(targetId);
            SceneManager.MoveGameObjectToScene(enemy, gameObject.scene);
            NetworkServer.Spawn(enemy);
            return agent.netId;
        }

        private static Vector2 DirectionFor(uint id)
        {
            switch (id % 4) { case 0: return Vector2.right; case 1: return Vector2.up; case 2: return Vector2.left; default: return Vector2.down; }
        }
        internal void StopWaveRun()
        {
            if (boundRunId != null) stoppedRunId = boundRunId;
            schedule?.Stop();
            activeParticipants.Clear();
            playerBuffer.Clear();
            if (progress != null) progress.Release(this);
        }
        private void Unsubscribe()
        {
            if (world != null) world.ServerPlayerRegistered -= HandlePlayerRegistered;
            legacySubscribed = false;
        }
        private void OnDisable() { StopWaveRun(); Unsubscribe(); }
        private void OnDestroy() { StopWaveRun(); Unsubscribe(); }
    }
}
