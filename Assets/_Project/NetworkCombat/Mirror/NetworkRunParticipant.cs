using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class NetworkRunParticipant : NetworkBehaviour
    {
        [SyncVar] private ulong participantId;
        [SyncVar] private string runId;
        public ulong ParticipantId => participantId;
        public string RunId => runId;
        public uint AvatarId => netId;
        public ushort ConnectionEpoch => GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;

        internal void Prepare(string sessionId, RunParticipant participant)
        {
            runId = sessionId;
            participantId = participant.Id;
        }
    }
}
