using AstralShift.HellMaiden.Player;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Keeps Mirror's reliable transform protocol, with the per-player server lock.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerTransformReliable : NetworkTransformReliable
    {
        private PlayerMovement player;

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerMovement>();
        }

        private bool MovementLocked => player != null && player.IsUpgradeSelectionLocked;

        protected override void OnClientToServerSync(
            Vector3? position, Quaternion? rotation, Vector3? scale)
        {
            // OnDeserialize has already consumed delta compression state. Only
            // discard the movement, so subsequent packets still decode correctly.
            if (MovementLocked)
            {
                serverSnapshots.Clear();
                return;
            }
            base.OnClientToServerSync(position, rotation, scale);
        }

        protected override void UpdateServer()
        {
            if (MovementLocked)
            {
                serverSnapshots.Clear();
                return;
            }
            base.UpdateServer();
        }
    }
}
