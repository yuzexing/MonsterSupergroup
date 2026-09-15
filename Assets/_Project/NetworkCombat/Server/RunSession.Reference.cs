using System;

namespace MonsterSupergroup.NetworkCombat
{
    public enum ReferenceParticipantReadiness : byte { Ready, Selecting, Unready }
    public struct ReferenceTransitionStatus
    {
        public int Connected, Alive, Selecting, Unready;
        public int Waiting => Selecting + Unready;
        public bool CanComplete => Alive > 0 && Waiting == 0;
    }

    public sealed partial class RunSession
    {
        // Reads the existing roster and canonical health. It does not retain another participant state.
        public ReferenceTransitionStatus EvaluateReferenceTransition(CombatLedger ledger,
            Func<uint, ReferenceParticipantReadiness> readiness)
        {
            var result = new ReferenceTransitionStatus();
            if (!IsRunStarted || IsRunEnded) return result;
            foreach (var participant in Participants)
            {
                if (participant.ConnectionState != RunConnectionState.Connected) continue;
                result.Connected++;
                if (participant.AvatarId == 0 || ledger == null || !ledger.TryGetState(participant.AvatarId, out var state))
                { result.Unready++; continue; }
                if (!state.Alive) continue;
                result.Alive++;
                switch (readiness(participant.AvatarId))
                {
                    case ReferenceParticipantReadiness.Selecting: result.Selecting++; break;
                    case ReferenceParticipantReadiness.Unready: result.Unready++; break;
                }
            }
            return result;
        }
    }
}
