using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public struct AllureCastResult
    {
        public bool Accepted;
        public string Reason;
        public uint SourcePlayerId, TargetPlayerId, OtherPlayerNetId;
        public uint[] EnemyIds;
        public Vector2[] Positions;
        public Vector2 DecoyPosition;
        public double DecoyExpiresAt;
        public int AffectedCount => EnemyIds?.Length ?? 0;
        public static AllureCastResult Reject(string reason) => new AllureCastResult {
            Reason = reason, EnemyIds = Array.Empty<uint>(), Positions = Array.Empty<Vector2>()
        };
    }

    public sealed partial class NetworkCombatWorld
    {
        // Admission and cooldown belong to NetworkPlayerAllure; target selection stays authoritative here.
        [Server]
        public AllureCastResult ServerCastAllure(uint caster, AllureAction action, ulong castId, AllureParameters p)
        {
            var simulations = NetworkEnemySimulationWorld.Instance;
            if (!PrototypesEnabled || Gateway.CombatStopped || BootGameplayNetworkManager.CombatHasEnded ||
                !p.IsValid || castId == 0 || action > AllureAction.Decoy || simulations == null ||
                !simulations.TryGetEligiblePlayer(caster, out var casterEndpoint))
                return AllureCastResult.Reject("Allure unavailable");
            var players = new List<NetworkEnemySimulationEndpoint>();
            simulations.GetEligiblePlayers(players);
            uint other = 0;
            if (action != AllureAction.Decoy)
            {
                var session = (NetworkManager.singleton as BootGameplayNetworkManager)?.Session;
                // A three-player run does not become a two-player prototype when someone is downed.
                if (players.Count > 2 || (session != null && session.Participants.Count > 2))
                    return AllureCastResult.Reject("R / T prototype supports two players only");
                if (players.Count != 2) return AllureCastResult.Reject("A living teammate is required");
                other = players[0].netId == caster ? players[1].netId : players[0].netId;
            }
            uint source = action == AllureAction.Take ? other : caster;
            uint target = action == AllureAction.Throw ? other : caster;
            if (!simulations.TryGetPlayerView(source, out var view))
                return AllureCastResult.Reject("Waiting for source player's current camera view");
            if (!simulations.TryGetEligiblePlayer(source, out var sourceEndpoint))
                return AllureCastResult.Reject("Source player unavailable");
            Vector2 origin = sourceEndpoint.transform.position;
            var candidates = new List<NetworkEnemySimulationAgent>();
            var positions = new Dictionary<uint, Vector2>();
            foreach (var identity in NetworkServer.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent<NetworkEnemySimulationAgent>(out var enemy) ||
                    !enemy.isActiveAndEnabled || !enemy.ProductEnemyInitialized || enemy.SimulationMode != EnemySimulationMode.NormalClient ||
                    enemy.Assignment.AggroTargetPlayerId != source || !identity.TryGetComponent<EnemyController>(out var controller) ||
                    !controller.isActiveAndEnabled || controller.isElite ||
                    !Gateway.Ledger.TryGetState(enemy.netId, out var life) || !life.Alive) continue;
                Bounds body = simulations.ServerEnemyBodyBounds(enemy);
                if (!PlayerViewReport.Intersects(view, body)) continue;
                candidates.Add(enemy);
                positions.Add(enemy.netId, body.center);
            }
            candidates.Sort((left, right) => {
                int distance = (positions[left.netId] - origin).sqrMagnitude.CompareTo((positions[right.netId] - origin).sqrMagnitude);
                return distance != 0 ? distance : left.netId.CompareTo(right.netId);
            });
            if (candidates.Count == 0) return AllureCastResult.Reject("No eligible ordinary enemies in the source player's view");
            if (candidates.Count > p.MaximumTargets) candidates.RemoveRange(p.MaximumTargets, candidates.Count - p.MaximumTargets);
            Vector2 decoy = casterEndpoint.transform.position;
            double expiry = NetworkTime.time + p.DecoyDuration;
            if (action == AllureAction.Decoy) simulations.ServerClearAllureDecoy(caster, 0);
            var affected = new List<uint>(candidates.Count);
            var effectPositions = new List<Vector2>(candidates.Count);
            foreach (var enemy in candidates)
            {
                bool applied = action == AllureAction.Decoy
                    ? simulations.ServerApplyAllureDecoy(enemy.netId, caster, castId, decoy, expiry)
                    : simulations.ServerRedirectAllure(enemy.netId, target, caster);
                if (!applied) continue;
                affected.Add(enemy.netId); effectPositions.Add(positions[enemy.netId]);
            }
            if (affected.Count == 0) return AllureCastResult.Reject("Targets no longer available");
            return new AllureCastResult {
                Accepted = true, SourcePlayerId = source, TargetPlayerId = target, OtherPlayerNetId = other,
                EnemyIds = affected.ToArray(), Positions = effectPositions.ToArray(), DecoyPosition = decoy,
                DecoyExpiresAt = action == AllureAction.Decoy ? expiry : 0
            };
        }

        [Server]
        public void ServerCancelAllure(uint casterNetId, ulong decoyCastId) =>
            NetworkEnemySimulationWorld.Instance?.ServerClearAllureDecoy(casterNetId, decoyCastId);
    }
}
