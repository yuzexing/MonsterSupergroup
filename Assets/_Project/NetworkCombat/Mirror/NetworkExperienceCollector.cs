using AstralShift.HellMaiden.Player;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class NetworkExperienceCollector : NetworkBehaviour
    {
        private PlayerMovement player;
        private NetworkModifierSelection progression;
        private float nextRequest;
        private void Awake()
        {
            player = GetComponent<PlayerMovement>();
            progression = GetComponent<NetworkModifierSelection>();
        }
        private void Update()
        {
            if (!isOwned || !NetworkClient.active || player == null || progression == null ||
                !player.isActiveAndEnabled || !player.CombatantBinding.IsAlive || !progression.HasOwnerBaseline ||
                progression.IsSelecting || Time.unscaledTime < nextRequest) return;
            var world = NetworkExperienceWorld.Current;
            if (world == null || string.IsNullOrEmpty(world.RunId)) return;
            nextRequest = Time.unscaledTime + 0.1f;
            float radius = player.PlayerStats.currentStats.pullArea;
            // Send at most one intent per pass; rejection is safe to retry. No predicted claim or root motion.
            foreach (var gem in NetworkExperienceGem.ClientGems)
                if (gem != null && !gem.Claimed && gem.RunId == world.RunId &&
                    ((Vector2)transform.position - (Vector2)gem.transform.position).sqrMagnitude <= radius * radius)
                { RequestCollection(gem.RunId, gem.DropId); break; }
        }
        public void RequestCollection(string run, ulong drop)
        {
            if (isOwned && NetworkClient.active && isActiveAndEnabled) CmdCollect(run, drop);
        }
        [Command]
        private void CmdCollect(string run, ulong drop, NetworkConnectionToClient sender = null)
        {
            if (!isActiveAndEnabled) return;
            var world = NetworkExperienceWorld.Current;
            if (world != null && !world.TryCollect(sender, netIdentity, run, drop, out string reason))
                Debug.Log($"[XP] run={run} drop={drop} requester={netId} rejected={reason}");
        }
        public override void OnStopAuthority() { nextRequest = 0; }
        public override void OnStopClient() { nextRequest = 0; }
    }
}
