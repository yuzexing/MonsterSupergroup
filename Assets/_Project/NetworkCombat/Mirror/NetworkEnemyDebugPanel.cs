using System;
using System.Collections.Generic;
using System.Text;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Local diagnostic snapshots only. Never drives combat, selection or network simulation.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkEnemyDebugPanel : MonoBehaviour
    {
        private const double RefreshInterval = 0.2;
        private const double DeathDisplayDuration = 2.0;
        private static readonly EnemyStatusID[] StatusIds = (EnemyStatusID[])Enum.GetValues(typeof(EnemyStatusID));

        [SerializeField] private bool expanded = true;
        [SerializeField] private CardPickMenu cardPickMenu;

        private readonly List<Row> rows = new List<Row>();
        private readonly Dictionary<uint, TimedRow> deaths = new Dictionary<uint, TimedRow>();
        private readonly Dictionary<uint, TimedRow> lastObserved = new Dictionary<uint, TimedRow>();
        private readonly HashSet<uint> spawnedIds = new HashSet<uint>();
        private readonly List<uint> expiredIds = new List<uint>();
        private readonly StringBuilder statusText = new StringBuilder();
        private IReadOnlyList<Row> readOnlyRows;
        private NetworkCombatWorld world;
        private NetworkConnectionToServer connection;
        private double nextRefresh;
        private Vector2 scroll;
        private GUIStyle rowStyle;

        public IReadOnlyList<Row> Rows => readOnlyRows ??= rows.AsReadOnly();
        public string ConnectionText { get; private set; } = "Waiting for connection";
        public bool Expanded => expanded;
        public bool IsContentVisible => expanded && (cardPickMenu == null || !cardPickMenu.IsOpen);

        private void OnEnable()
        {
            if ((!Application.isEditor && !Debug.isDebugBuild) || GameplayRuntimeEnvironment.IsDedicatedServer)
            {
                enabled = false;
                return;
            }
            nextRefresh = 0;
        }

        private void Update()
        {
            if (Application.isFocused && Input.GetKeyDown(KeyCode.F3)) SetExpanded(!expanded);

            // Check lifecycle every frame; enumerate only Mirror's spawned objects at 5 Hz.
            NetworkCombatWorld current = NetworkClient.isConnected ? NetworkCombatWorld.Instance : null;
            if (current != null && !current.isActiveAndEnabled) current = null;
            NetworkConnectionToServer currentConnection = NetworkClient.active ? NetworkClient.connection : null;
            if (!ReferenceEquals(world, current) || !ReferenceEquals(connection, currentConnection))
            {
                ReleaseWorld();
                world = current;
                connection = currentConnection;
                if (world != null) world.Replica.EntityChanged += HandleCanonicalEntity;
                nextRefresh = 0;
            }
            if (Time.unscaledTimeAsDouble < nextRefresh) return;
            RefreshRows();
            nextRefresh = Time.unscaledTimeAsDouble + RefreshInterval;
        }

        public void SetExpanded(bool value)
        {
            expanded = value;
            if (value) nextRefresh = 0;
        }

        private void RefreshRows()
        {
            string role = NetworkServer.active && NetworkClient.active ? "Host" : "Client";
            string localPlayer = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId.ToString() : "unavailable";
            ConnectionText = $"{role} | Connected: {NetworkClient.isConnected} | Local player: {localPlayer}";
            rows.Clear();
            spawnedIds.Clear();
            if (world == null) return;

            foreach (var pair in NetworkClient.spawned)
            {
                NetworkIdentity identity = pair.Value;
                if (identity == null || !identity.TryGetComponent(out NetworkEnemySimulationAgent agent)) continue;
                spawnedIds.Add(pair.Key);
                CanonicalEntityState? canonical = world.Replica.TryGetEntity(pair.Key, out var state) ? state : null;
                Row row = Capture(identity, agent, canonical);
                rows.Add(row);
                lastObserved[pair.Key] = new TimedRow(row, Time.unscaledTimeAsDouble + DeathDisplayDuration);
            }

            // Host destruction can precede its queued canonical RPC. These snapshots are not
            // displayed after ordinary despawn; only a received canonical death can revive a row.
            expiredIds.Clear();
            foreach (var pair in lastObserved)
                if (Time.unscaledTimeAsDouble >= pair.Value.ExpiresAt) expiredIds.Add(pair.Key);
            foreach (uint id in expiredIds) lastObserved.Remove(id);

            expiredIds.Clear();
            foreach (var pair in deaths)
            {
                if (Time.unscaledTimeAsDouble >= pair.Value.ExpiresAt) expiredIds.Add(pair.Key);
                else if (!spawnedIds.Contains(pair.Key)) rows.Add(pair.Value.Snapshot);
            }
            foreach (uint id in expiredIds) deaths.Remove(id);
            rows.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
        }

        private Row Capture(NetworkIdentity identity, NetworkEnemySimulationAgent agent, CanonicalEntityState? canonical, bool recentDeath = false)
        {
            identity.TryGetComponent(out CombatantBehaviour combatant);
            bool ready = agent.ProductEnemyInitialized && combatant != null && combatant.isActiveAndEnabled;
            string localHealth = ready ? $"{combatant.CurrentHealth} / {combatant.MaxHealth}" : "unavailable (runtime not ready)";
            statusText.Clear();
            if (!ready || !canonical.HasValue) statusText.Append("unavailable (waiting for runtime / canonical snapshot)");
            else
            {
                // Query canonical stacks, never the effective/predicted stack count or Advance().
                StatusController statuses = combatant.StatusController;
                foreach (EnemyStatusID id in StatusIds)
                {
                    if (id == EnemyStatusID.None) continue;
                    int count = statuses.GetCanonicalStackCount(id);
                    if (count == 0) continue;
                    if (statusText.Length > 0) statusText.Append(", ");
                    statusText.Append(id).Append(" x").Append(count);
                }
                if (statusText.Length == 0) statusText.Append("none");
            }
            var assignment = agent.Assignment;
            string simulation = assignment.Epoch == 0 || agent.Authority == null
                ? "unavailable (assignment pending)"
                : $"{assignment.Host} | Here: {agent.Authority.Role}\n" +
                  $"Simulator: {assignment.SimulationOwnerPlayerId} | Target: {assignment.AggroTargetPlayerId} | Epoch: {assignment.Epoch}";
            string runtime = $"Runtime: {(agent.ProductEnemyInitialized ? "ready" : "waiting")} | " +
                (agent.ProductMovementOnly ? "MovementOnly" : "Combat simulation");
            return new Row(identity.netId, identity.name, canonical, localHealth, statusText.ToString(), simulation, runtime, recentDeath);
        }

        private void HandleCanonicalEntity(CanonicalEntityState state)
        {
            if (state.Kind != (byte)CombatEntityKind.Enemy) return;
            if (state.Alive) { deaths.Remove(state.EntityId); return; }
            if (deaths.TryGetValue(state.EntityId, out var previous) &&
                previous.Snapshot.Canonical.Value.StateVersion >= state.StateVersion) return;

            // Capture before destruction where possible; Host may have already forgotten the object.
            Row snapshot;
            if (NetworkClient.spawned.TryGetValue(state.EntityId, out var identity) && identity != null &&
                identity.TryGetComponent(out NetworkEnemySimulationAgent agent))
                snapshot = Capture(identity, agent, state, true);
            else if (lastObserved.TryGetValue(state.EntityId, out var observed) &&
                Time.unscaledTimeAsDouble < observed.ExpiresAt)
                snapshot = observed.Snapshot.WithCanonicalDeath(state);
            else return;
            deaths[state.EntityId] = new TimedRow(snapshot, Time.unscaledTimeAsDouble + DeathDisplayDuration);
            nextRefresh = 0;
        }

        private void ReleaseWorld()
        {
            if (!ReferenceEquals(world, null)) world.Replica.EntityChanged -= HandleCanonicalEntity;
            world = null;
            connection = null;
            rows.Clear();
            deaths.Clear();
            lastObserved.Clear();
            spawnedIds.Clear();
            expiredIds.Clear();
            scroll = Vector2.zero;
            ConnectionText = "Waiting for connection";
        }

        private void OnDisable() => ReleaseWorld();

        private void OnGUI()
        {
            if (!isActiveAndEnabled) return;
            float width = Mathf.Min(500f, Screen.width - 24f);
            float height = Mathf.Min(460f, Screen.height - 24f);
            bool showContent = IsContentVisible;
            Rect area = new Rect(Screen.width - width - 12f, 12f, width, showContent ? height : 32f);
            GUILayout.BeginArea(area, GUI.skin.box);
            if (cardPickMenu != null && cardPickMenu.IsOpen) GUILayout.Label("Enemy Debug [F3] - list hidden during selection");
            else if (GUILayout.Button($"Enemy Debug [F3] - {(showContent ? "Hide" : "Show")}")) SetExpanded(!expanded);
            if (showContent)
            {
                GUILayout.Label(ConnectionText);
                GUILayout.Label($"Observed enemies: {spawnedIds.Count} | Rows (incl. recent deaths): {rows.Count}");
                if (world == null) GUILayout.Label(NetworkClient.isConnected ? "Waiting for combat world" : "Waiting for connection");
                else if (rows.Count == 0) GUILayout.Label("No observed enemies");
                rowStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true };
                scroll = GUILayout.BeginScrollView(scroll);
                foreach (Row row in rows) GUILayout.Label(row.Text, rowStyle);
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        public readonly struct Row
        {
            private readonly string name;
            private readonly string runtime;
            public uint EntityId { get; }
            public CanonicalEntityState? Canonical { get; }
            public string LocalHealth { get; }
            public string CanonicalStatuses { get; }
            public string Simulation { get; }
            public string Text { get; }

            internal Row(uint entityId, string name, CanonicalEntityState? canonical, string localHealth,
                string statuses, string simulation, string runtime, bool recentDeath)
            {
                EntityId = entityId;
                this.name = name;
                this.runtime = runtime;
                Canonical = canonical;
                LocalHealth = localHealth;
                CanonicalStatuses = statuses;
                Simulation = simulation;
                string health = canonical.HasValue
                    ? $"{canonical.Value.Health} / {canonical.Value.MaxHealth} | {(canonical.Value.Alive ? "Alive" : "Dead")} | v{canonical.Value.StateVersion}"
                    : "unavailable (waiting for canonical snapshot)";
                Text = (recentDeath ? "Recent death - last observed data\n" : string.Empty) +
                    $"#{entityId} {name}\nCanonical HP: {health}\n" +
                    $"Local HP (may include prediction): {localHealth}\nCanonical GAS (replica): {statuses}\n{simulation}\n{runtime}";
            }

            internal Row WithCanonicalDeath(CanonicalEntityState state) =>
                new Row(EntityId, name, state, LocalHealth, CanonicalStatuses, Simulation, runtime, true);
        }

        private readonly struct TimedRow
        {
            public readonly Row Snapshot;
            public readonly double ExpiresAt;
            public TimedRow(Row snapshot, double expiresAt) { Snapshot = snapshot; ExpiresAt = expiresAt; }
        }
    }
}
