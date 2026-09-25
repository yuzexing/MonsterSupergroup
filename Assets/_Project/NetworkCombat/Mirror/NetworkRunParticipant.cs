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
        private string investigationBirth;
        private int investigationActiveRoles;
        private bool investigationBirthEnded;
        private uint investigationStoppedAvatar;
        public string InvestigationBirth
        {
            get
            {
                // A reused client identity receives SyncVars before OnStartClient.
                if (investigationBirthEnded && netIdentity != null && netId != 0 && netId != investigationStoppedAvatar)
                    BeginInvestigationSpawn();
                return investigationBirth ??= System.Guid.NewGuid().ToString("N");
            }
        }

        internal void BeginInvestigationSpawn()
        {
            if (!Diagnostics.CombatInvestigationEvidence.Enabled || !investigationBirthEnded) return;
            investigationBirth = null;
            investigationBirthEnded = false;
        }

        private void StartInvestigationRole(int role, string perspective, string outcome)
        {
            if (!Diagnostics.CombatInvestigationEvidence.Enabled) return;
            BeginInvestigationSpawn();
            investigationActiveRoles |= role;
            TraceIdentity(perspective, outcome);
        }
        private void StopInvestigationRole(int role, string perspective, string outcome)
        {
            // Host callbacks share one birth. Authority loss alone is not a new avatar;
            // only the last active lifecycle role ends this component's current birth.
            bool wasActive = (investigationActiveRoles & role) != 0;
            if (!wasActive) return;
            TraceIdentity(perspective, outcome);
            investigationActiveRoles &= ~role;
            if (investigationActiveRoles == 0)
            {
                // Later components may still publish OnStop cleanup. Keep the ending
                // identity until preparation/spawn, independent of component order.
                investigationBirthEnded = true;
                investigationStoppedAvatar = netId;
            }
        }

        private void TraceIdentity(string perspective, string outcome)
        {
            if (!Diagnostics.CombatInvestigationEvidence.Enabled) return;
            Diagnostics.CombatInvestigationEvidence.Capture("identity", outcome, () => new {
                participantId = participantId.ToString(), avatar = netId, run = runId,
                connectionEpoch = GetComponent<MirrorNetworkCombatBridge>()?.ConnectionEpoch ?? 0,
                birth = InvestigationBirth, perspective, initialWeaponId
            }, source: netId, target: netId, perspective: perspective);
        }
        public override void OnStartServer() { base.OnStartServer(); StartInvestigationRole(1, "Server", "Spawned"); }
        public override void OnStartClient() { base.OnStartClient(); StartInvestigationRole(2, "Replica", "Spawned"); }
        public override void OnStartAuthority() { base.OnStartAuthority(); StartInvestigationRole(4, "Owner", "AuthorityStarted"); }
        public override void OnStopAuthority() { StopInvestigationRole(4, "Owner", "AuthorityStopped"); base.OnStopAuthority(); }
        public override void OnStopClient() { StopInvestigationRole(2, "Replica", "Despawned"); base.OnStopClient(); }
        public override void OnStopServer() { StopInvestigationRole(1, "Server", "Despawned"); base.OnStopServer(); }

        internal void Prepare(string sessionId, RunParticipant participant)
        {
            BeginInvestigationSpawn();
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
