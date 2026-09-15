using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        private sealed class ReferenceFlight
        {
            public EnemyProjectileLaunch Launch;
            public Bounds View;
        }
        private readonly Dictionary<EnemyProjectileKey, ReferenceFlight> serverReferenceFlights = new Dictionary<EnemyProjectileKey, ReferenceFlight>();
        private readonly Dictionary<EnemyProjectileKey, EnemyProjectileLaunch> clientReferenceFlights = new Dictionary<EnemyProjectileKey, EnemyProjectileLaunch>();
        private readonly EnemyProjectileHistory referenceLaunchHistory = new EnemyProjectileHistory();
        private readonly EnemyProjectileHistory referenceHitGrants = new EnemyProjectileHistory();
        private readonly List<EnemyProjectileKey> expiredReferenceFlights = new List<EnemyProjectileKey>();
        public int ActiveReferenceProjectileCount => isServer ? serverReferenceFlights.Count : clientReferenceFlights.Count;
        public event Action<EnemyProjectileLaunch, uint> ReferenceProjectileHitGranted;

        internal bool TryReadReferenceFlightView(EnemyProjectileKey key, out Bounds view)
        {
            if (serverReferenceFlights.TryGetValue(key, out var flight)) { view = flight.View; return true; }
            view = default; return false;
        }

        private void RegisterReferenceFlight(EnemyProjectileLaunch launch)
        {
            if (launch.ExpiryMode != EnemyProjectileExpiryMode.ReferenceOutsideView) return;
            referenceLaunchHistory.Add(launch.Key, EnemySimulationClock.CombatNow);
            var flight = new ReferenceFlight { Launch = launch,
                View = new Bounds(launch.Origin, new Vector3(24 * 16f / 9, 24, 1)) };
            UpdateReferenceFlightView(flight);
            serverReferenceFlights.Add(launch.Key, flight);
        }

        private void UpdateReferenceFlightView(ReferenceFlight flight)
        {
            if (!TryGetEligiblePlayer(flight.Launch.ViewTargetPlayerId, out var target)) return;
            var rig = FindFirstObjectByType<AstralShift.HellMaiden.CameraFX.GameplayCameraRig>();
            Camera camera = rig != null ? rig.GameCamera : Camera.main;
            if (camera != null)
                flight.View = MonsterSupergroup.Gameplay.Combat.GameplayCameraGeometry.ViewBounds(camera);
            if (NetworkClient.localPlayer == null || NetworkClient.localPlayer.netId != target.PlayerEntityId)
                flight.View = new Bounds(target.transform.position, flight.View.size);
        }

        private void UpdateReferenceProjectiles()
        {
            expiredReferenceFlights.Clear();
            foreach (var pair in serverReferenceFlights)
            {
                UpdateReferenceFlightView(pair.Value);
                if (pair.Value.Launch.OutsideViewExpired(EnemySimulationClock.CombatNow, pair.Value.View))
                    expiredReferenceFlights.Add(pair.Key);
            }
            foreach (var key in expiredReferenceFlights)
                ConfirmEnemyProjectileTermination(new EnemyProjectileTermination { Key = key, Reason = EnemyProjectileEndReason.Expired });
        }

        private void ClaimReferenceProjectileHit(EnemyProjectileKey key)
        {
            if (NetworkClient.localPlayer == null || !clientReferenceFlights.ContainsKey(key)) return;
            pendingClientTerminations.Add(new EnemyProjectileTermination {
                Key = key, Reason = EnemyProjectileEndReason.Hit, TargetPlayerId = NetworkClient.localPlayer.netId });
        }

        private void AcceptReferenceProjectileHit(EnemyProjectileTermination claim)
        {
            if (claim.Reason != EnemyProjectileEndReason.Hit || claim.TargetPlayerId == 0 ||
                !serverReferenceFlights.TryGetValue(claim.Key, out var flight) ||
                !TryGetEligiblePlayer(claim.TargetPlayerId, out var target) || target.connectionToClient == null) return;
            // First valid collision consumes the single pierce, including an invulnerable player's collision.
            // Only the granted owner calls the existing PlayerMovement -> Combat/GAS damage path.
            TargetGrantReferenceHit(target.connectionToClient, CurrentRound, flight.Launch, claim.TargetPlayerId);
            ReferenceProjectileHitGranted?.Invoke(flight.Launch, claim.TargetPlayerId);
            ConfirmEnemyProjectileTermination(claim);
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetGrantReferenceHit(NetworkConnectionToClient connection, uint round, EnemyProjectileLaunch launch, uint targetId)
        {
            if (round != CurrentRound || BootGameplayNetworkManager.CombatHasEnded || NetworkClient.localPlayer == null ||
                NetworkClient.localPlayer.netId != targetId || !referenceHitGrants.Add(launch.Key, EnemySimulationClock.CombatNow)) return;
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            player.Damage(launch.Damage, DamageType.Projectile);
            if (launch.StunTime > 0) player.Stun(launch.StunTime);
        }

        private void SendReferenceFlights(NetworkEnemySimulationEndpoint endpoint)
        {
            var pending = new List<EnemyProjectileLaunch>();
            foreach (var flight in serverReferenceFlights.Values)
            {
                pending.Add(flight.Launch);
                if (pending.Count == maximumAttackPresentationEdgesPerBatch)
                { Send(); pending.Clear(); }
            }
            if (pending.Count > 0) Send();
            void Send() => TargetApplyAttackPresentations(endpoint.connectionToClient,
                new EnemyAttackPresentationBatch { Round = CurrentRound, ProjectileLaunches = pending.ToArray() });
        }

        private void ClearReferenceProjectiles()
        {
            serverReferenceFlights.Clear(); clientReferenceFlights.Clear(); referenceLaunchHistory.Clear(); referenceHitGrants.Clear();
        }
    }
}
