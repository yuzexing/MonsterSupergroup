using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Shared production pickup lifecycle, preserving the public XP entry points.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkCombatWorld))]
    public sealed partial class NetworkExperienceWorld : NetworkBehaviour
    {
        [SerializeField] private GameplayExperienceRules rules;
        [SerializeField] private NetworkExperienceGem gemPrefab;
        [SerializeField] private GameplayPickupRules pickupRules;
        [SyncVar] private string runId;
        [SyncVar] private uint round;
        private NetworkCombatWorld combat;
        private PickupDropSchedule schedule;
        private System.Random dropRandom;
        private readonly Dictionary<ulong, NetworkExperienceGem> drops = new Dictionary<ulong, NetworkExperienceGem>();
        private ulong sequence;
        private string configurationError;
        public ExperienceParameters Parameters { get; private set; }
        public string RunId => runId;
        public uint Round => round;
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
            endedCleared = false;
            combat = GetComponent<NetworkCombatWorld>();
            runId = (NetworkManager.singleton as BootGameplayNetworkManager)?.Session.RunId;
            round = (NetworkManager.singleton as BootGameplayNetworkManager)?.Session.Round ?? 0;
            if (pickupRules != null && pickupRules.experience != null) rules = pickupRules.experience;
            if (rules == null) configurationError = "Gameplay XP rules are missing.";
            else if (rules.TryCapture(out var captured, out configurationError))
            { Parameters = captured; schedule = new PickupDropSchedule(captured,
                pickupRules != null ? pickupRules.itemWeight : 0, pickupRules != null ? pickupRules.lowHealthBias : 0); }
            if (gemPrefab == null) configurationError = "Network XP prefab is missing.";
            // World is spawned before any avatar/enemy. Capture death data before their presentation callbacks.
            combat.Gateway.ConfirmedKillProduced += OnConfirmedKill;
            CapturePickupDefinitions();
            dropRandom = new System.Random(pickupRules != null ? pickupRules.randomSeed : 14303);
            combat.Gateway.ValidatePickupReceipt = ValidateHealthReceipt;
            combat.Gateway.PlayerHealthReportAccepted += CommitHealthReceipt;
            combat.Gateway.PlayerHealthReportRejected += RejectedHealthReceipt;
            PickupAudit.Emit("run", runId, 0, "seed=" + (pickupRules != null ? pickupRules.randomSeed : 14303));
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
            float fraction = float.NaN;
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            if (NetworkServer.spawned.TryGetValue(kill.KillerPlayerId, out var killer) && killer.connectionToClient != null &&
                manager.Session.TryGetConnection(killer.connectionToClient.connectionId, out var member) && member.AvatarId == killer.netId &&
                combat.Gateway.Ledger.TryGetState(killer.netId, out var health) && health.MaxHealth > 0)
                fraction = (float)health.Health / health.MaxHealth;
            var decision = schedule.Consume(kill.TargetEntityId, kill.TargetStateVersion, amount, fraction,
                HealthCount, healthDefinition?.WorldLimit ?? 0, () => (float)dropRandom.NextDouble());
            PickupAudit.Emit("drop-decision", runId, 0, $"enemy={kill.TargetEntityId};death={kill.TargetStateVersion};killer={kill.KillerPlayerId};health={fraction};reason={decision.Reason};p={decision.Probability};roll={decision.Roll}");
            if (decision.Effect == PickupEffect.None)
            {
                return;
            }
            SpawnPickup(decision.Effect, amount, position, identity.gameObject.scene);
        }

        private NetworkExperienceGem SpawnPickup(PickupEffect effect, float amount, Vector2 position, Scene scene)
        {
            var definition = effect == PickupEffect.RestoreHealth ? healthDefinition : xpDefinition;
            if (definition == null && effect != PickupEffect.Experience) return null;
            if (definition != null && definition.WorldLimit > 0 && CountEffect(effect) >= definition.WorldLimit) return null;
            var prefab = definition?.Prefab ?? gemPrefab;
            var gem = RentEntity(prefab, position, definition?.IdleCapacity ?? 500);
            SceneManager.MoveGameObjectToScene(gem.gameObject, scene);
            gem.Initialize(runId, ++sequence, effect == PickupEffect.Experience ? amount : definition.Value,
                effect, definition?.Id ?? 1);
            drops.Add(sequence, gem);
            NetworkServer.Spawn(gem.gameObject);
            PickupAudit.Emit("spawn", runId, sequence, $"effect={effect};amount={gem.RawExperience};count={drops.Count}");
            return gem;
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
                progression.IsSelecting || progression.PendingEventId != 0 || combat.Gateway.Ledger.IsPlayerSelectingUpgrade(avatar.netId))
            { reason = "selecting-or-unready"; return false; }
            float radius = player.PlayerStats.currentStats.pullArea;
            if (!ExperienceParameters.Finite(radius) || radius <= 0 ||
                ((Vector2)avatar.transform.position - (Vector2)gem.transform.position).sqrMagnitude > radius * radius)
            { reason = "distance-or-stats"; return false; }
            if (gem.Effect == PickupEffect.RestoreHealth) return ReserveHealth(gem, member, avatar, out reason);
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
            PickupAudit.Emit("xp-collected", runId, dropId, $"collector={avatar.netId};raw={gem.RawExperience};awarded={amount}");
            drops.Remove(dropId);
            gem.ServerPresentCollection(avatar.netId);
            RecycleEntity(gem);
            reason = "collected";
            if (AstralShift.DebugTools.DBL.VerboseEnabled) Debug.Log($"[XP] run={runId} drop={dropId} collector={avatar.netId} raw={gem.RawExperience} awarded={amount}");
            return true;
        }

        private void ClearServer()
        {
            if (combat != null)
            {
                combat.Gateway.ConfirmedKillProduced -= OnConfirmedKill;
                combat.Gateway.PlayerHealthReportAccepted -= CommitHealthReceipt;
                combat.Gateway.PlayerHealthReportRejected -= RejectedHealthReceipt;
                combat.Gateway.ValidatePickupReceipt = null;
            }
            claims.Clear(); committedClaims.Clear();
            foreach (var gem in new List<NetworkExperienceGem>(drops.Values))
                if (gem != null && NetworkServer.active) RecycleEntity(gem);
            drops.Clear(); schedule = null; Parameters = null; sequence = 0; runId = null; round = 0;
        }
        public void ResetForNextRun() { ClearServer(); InitializeRun(); }
        public override void OnStopServer() { ClearServer(); ClearPools(); }
        public void PrepareClientPickupPools() { CapturePickupDefinitions(); RegisterPickupPools(); }
        public override void OnStartClient() => PrepareClientPickupPools();
        public override void OnStopClient() { if (!NetworkServer.active) runId = null; ClearPools(); }
        private void OnDisable() { if (isServer) ClearServer(); ClearPools(); }
    }
}
