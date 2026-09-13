using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkWeaponCombatAdapter
    {
        private readonly Dictionary<ProjectilePresentationKey, NetworkProjectilePresentationEdge> serverProjectileViews =
            new Dictionary<ProjectilePresentationKey, NetworkProjectilePresentationEdge>();

        private void RememberProjectileViews(NetworkProjectilePresentationEdge[] edges)
        {
            foreach (var edge in edges)
            {
                if (edge.Phase == ProjectilePresentationPhase.Impact) continue;
                if (edge.Phase != ProjectilePresentationPhase.Spawn) serverProjectileViews.Remove(edge.Key);
                else if (serverProjectileViews.Count < 2048) serverProjectileViews.TryAdd(edge.Key, edge);
            }
        }

        private void RetireProjectileViews(ulong attackEventId)
        {
            var remove = new List<ProjectilePresentationKey>();
            foreach (var key in serverProjectileViews.Keys) if (key.AttackEventId == attackEventId) remove.Add(key);
            foreach (var key in remove) serverProjectileViews.Remove(key);
        }

        private IEnumerator RequestProjectileViewsWhenReady()
        {
            while (isActiveAndEnabled && NetworkClient.active && (NetworkClient.localPlayer == null || !NetworkClient.ready)) yield return null;
            if (isActiveAndEnabled && NetworkClient.active && !isOwned) CmdRequestProjectileViews();
        }

        [Command(requiresAuthority = false, channel = Channels.Reliable)]
        private void CmdRequestProjectileViews(NetworkConnectionToClient sender = null)
        {
            if (sender == null || !sender.isAuthenticated || sender.identity == null ||
                !netIdentity.observers.ContainsKey(sender.connectionId)) return;
            var chunk = new List<NetworkProjectilePresentationEdge>(32);
            foreach (var edge in serverProjectileViews.Values)
            {
                chunk.Add(edge);
                if (chunk.Count < 32) continue;
                TargetReceiveProjectileViews(sender, chunk.ToArray()); chunk.Clear();
            }
            if (chunk.Count > 0) TargetReceiveProjectileViews(sender, chunk.ToArray());
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetReceiveProjectileViews(NetworkConnectionToClient target, NetworkProjectilePresentationEdge[] edges)
        {
            if (isOwned || !isActiveAndEnabled || edges == null || edges.Length > 32) return;
            var database = playerBootstrap != null ? playerBootstrap.ResolveSharedRuntimeDatabase() : null;
            if (database == null || playerMovement == null) return;
            presentationReplica ??= new ProjectilePresentationReplica(playerMovement, database);
            foreach (var edge in edges)
            {
                if (!edge.IsValid || edge.SourcePlayerId != netId || edge.Phase != ProjectilePresentationPhase.Spawn) continue;
                float age = (float)Math.Max(0d, NetworkTime.time - edge.EventNetworkTime);
                if (presentationReplica.TrySpawn(edge.ToSpawn(), age, playLaunchSound: false)) ReplicaSpawnCount++;
            }
        }
    }
}
