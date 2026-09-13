using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class NetworkRunParticipant : NetworkBehaviour
    {
        [SyncVar] private ulong participantId;
        [SyncVar] private string runId;
        [SyncVar] private uint initialWeaponId = PreparationRoom.DefaultWeapon;
        public uint InitialWeaponId => initialWeaponId;
        public ulong ParticipantId => participantId;
        public string RunId => runId;
        public uint AvatarId => netId;
        public ushort ConnectionEpoch => GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;

        internal void Prepare(string sessionId, RunParticipant participant)
        {
            runId = sessionId;
            participantId = participant.Id;
        }

        internal void PrepareInitialWeapon(uint weaponId)
        {
            initialWeaponId = weaponId;
            GetComponent<MonsterSupergroup.Gameplay.Combat.PlayerBuildRuntime>().ConfigureInitialWeapon(weaponId);
        }
    }
}
