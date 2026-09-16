using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>The only production XP source. Its lifetime is the server World/run, not an avatar.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkCombatWorld))]
    public sealed class NetworkExperienceWorld : NetworkBehaviour
    {
        [SerializeField] private GameplayExperienceRules rules;
        [SerializeField] private NetworkExperienceGem gemPrefab;
        [SyncVar] private string runId;
        private NetworkCombatWorld combat;
        private ExperienceDropSchedule schedule;
        private readonly Dictionary<ulong, NetworkExperienceGem> drops = new Dictionary<ulong, NetworkExperienceGem>();
        private ulong sequence;
        private string configurationError;
        public ExperienceParameters Parameters { get; private set; }
        public string RunId => runId;
        public int UnclaimedCount => drops.Count;
        public IEnumerable<NetworkExperienceGem> Unclaimed => drops.Values;
        public static NetworkExperienceWorld Current => NetworkCombatWorld.Instance != null
            ? NetworkCombatWorld.Instance.GetComponent<NetworkExperienceWorld>() : null;

        public override void OnStartServer()
        {
            InitializeRun();
        }

        private void InitializeRun()
        {
            combat = GetComponent<NetworkCombatWorld>();
            runId = (NetworkManager.singleton as BootGameplayNetworkManager)?.Session.RunId;
            if (rules == null) configurationError = "Gameplay XP rules are missing.";
            else if (rules.TryCapture(out var captured, out configurationError))
            { Parameters = captured; schedule = new ExperienceDropSchedule(captured); }
            if (gemPrefab == null) configurationError = "Network XP prefab is missing.";
            // World is spawned before any avatar/enemy. Capture death data before their presentation callbacks.
            combat.Gateway.ConfirmedKillProduced += OnConfirmedKill;
        }

        public bool CanGrant(out string error)
        {
            error = configurationError;
            if (!isServer || !isActiveAndEnabled || Parameters == null || schedule == null ||
                gemPrefab == null || string.IsNullOrEmpty(runId) ||
                (NetworkManager.singleton as BootGameplayNetworkManager)?.Session.RunId != runId)
            { error ??= "Waiting for the server XP World."; return false; }
            return error == null;
        }

        private void OnConfirmedKill(ConfirmedKill kill)
        {
            if (BootGameplayNetworkManager.CombatHasEnded || !CanGrant(out _) || !NetworkServer.spawned.TryGetValue(kill.TargetEntityId, out var identity)) return;
            var enemy = identity.GetComponent<EnemyController>();
            var agent = identity.GetComponent<NetworkEnemySimulationAgent>();
            if (enemy == null || agent == null) return;
            float amount = enemy.stats.XP;
            Vector2 position = agent.ServerSpawnPosition;
            if (agent.Assignment.Host == EnemySimulationHost.ServerAuthoritative ||
                agent.Assignment.Host == EnemySimulationHost.ServerFallback) position = identity.transform.position;
            else if (NetworkEnemySimulationWorld.Instance.Registry.TryGetLatestSnapshot(identity.netId, out var snapshot) &&
                snapshot.AssignmentEpoch == agent.Assignment.Epoch) position = snapshot.Position;
            if (!schedule.ConsumeDeath(kill.TargetEntityId, kill.TargetStateVersion, amount, out string reason))
            {
                if (AstralShift.DebugTools.DBL.VerboseEnabled) Debug.Log($"[XP] run={runId} enemy={kill.TargetEntityId} death={kill.TargetStateVersion} cause={kill.CauseEventId} {reason}");
                return;
            }
            var gem = Instantiate(gemPrefab, position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(gem.gameObject, identity.gameObject.scene);
            gem.Initialize(runId, ++sequence, amount);
            drops.Add(sequence, gem);
            NetworkServer.Spawn(gem.gameObject);
            if (AstralShift.DebugTools.DBL.VerboseEnabled) Debug.Log($"[XP] run={runId} drop={sequence} enemy={kill.TargetEntityId} death={kill.TargetStateVersion} cause={kill.CauseEventId} raw={amount} position={position}");
        }

        public bool TryCollect(NetworkConnectionToClient sender, NetworkIdentity avatar, string requestedRun,
            ulong dropId, out string reason)
        {
            if (BootGameplayNetworkManager.CombatHasEnded) { reason = "run-ended"; return false; }
            if (!CanGrant(out reason)) return false;
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            if (sender == null || avatar == null || sender.identity != avatar || avatar.connectionToClient != sender ||
                !sender.isAuthenticated || !sender.isReady || manager == null ||
                !manager.Session.TryGetConnection(sender.connectionId, out var member) || member.AvatarId != avatar.netId)
            { reason = "ownership"; return false; }
            if (requestedRun != runId) { reason = "old-run"; return false; }
            if (!drops.TryGetValue(dropId, out var gem) || gem == null || gem.Claimed)
            { reason = "unavailable"; return false; }
            var progression = avatar.GetComponent<NetworkModifierSelection>();
            var player = avatar.GetComponent<PlayerMovement>();
            if (!combat.Gateway.Ledger.IsAlive(avatar.netId)) { reason = "dead"; return false; }
            if (progression == null || !progression.isActiveAndEnabled || player == null ||
                progression.IsSelecting || combat.Gateway.Ledger.IsPlayerSelectingUpgrade(avatar.netId))
            { reason = "selecting-or-unready"; return false; }
            float radius = player.PlayerStats.currentStats.pullArea;
            float multiplier = player.PlayerStats.currentStats.xpModifier;
            float amount = gem.RawExperience * multiplier;
            if (!ExperienceParameters.Finite(radius) || radius <= 0 ||
                !ExperienceParameters.Finite(amount) || amount <= 0 ||
                ((Vector2)avatar.transform.position - (Vector2)gem.transform.position).sqrMagnitude > radius * radius)
            { reason = "distance-or-stats"; return false; }
            // Reserve before grant: a Host offer callback cannot re-enter and collect the same drop.
            gem.SetClaimed(true);
            if (!progression.TryGrantExperience(amount))
            { gem.SetClaimed(false); reason = "grant-rejected"; return false; }
            drops.Remove(dropId);
            gem.ServerPresentCollection(avatar.netId);
            NetworkServer.Destroy(gem.gameObject);
            reason = "collected";
            if (AstralShift.DebugTools.DBL.VerboseEnabled) Debug.Log($"[XP] run={runId} drop={dropId} collector={avatar.netId} raw={gem.RawExperience} awarded={amount}");
            return true;
        }

        private void ClearServer()
        {
            if (combat != null) combat.Gateway.ConfirmedKillProduced -= OnConfirmedKill;
            foreach (var gem in new List<NetworkExperienceGem>(drops.Values))
                if (gem != null && NetworkServer.active) NetworkServer.Destroy(gem.gameObject);
            drops.Clear(); schedule = null; Parameters = null; sequence = 0; runId = null;
        }
        public void ResetForNextRun() { ClearServer(); InitializeRun(); }
        public override void OnStopServer() => ClearServer();
        public override void OnStopClient() { if (!NetworkServer.active) runId = null; }
        private void OnDisable() { if (isServer) ClearServer(); }
    }
}
