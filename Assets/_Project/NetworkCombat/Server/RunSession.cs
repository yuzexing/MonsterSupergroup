using System;
using System.Collections.Generic;
using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat
{
    public enum RunPlayerLifeState : byte { Active, Downed }
    public enum RunConnectionState : byte { Connected, Disconnected }

    /// <summary>Data checkpoint, never a second executing Build or StatusController.</summary>
    [Serializable]
    public sealed class PlayerRuntimeCheckpoint
    {
        public uint PreviousAvatarId;
        public double CapturedAt;
        public PlayerBuildSnapshot Build;
        public PlayerProgressionSnapshot Progression;
        public ServerEntityCheckpoint Health;
        public CanonicalStatusState[] Statuses = Array.Empty<CanonicalStatusState>();
        public RunPlayerLifeState LifeState;
    }

    public sealed class RunParticipant
    {
        internal RunParticipant(ulong id, string identity, int connectionId)
        {
            Id = id;
            Identity = identity;
            ConnectionId = connectionId;
        }

        public ulong Id { get; }
        internal string Identity { get; }
        public int ConnectionId { get; internal set; }
        public uint AvatarId { get; internal set; }
        public ushort ConnectionEpoch { get; internal set; }
        public RunConnectionState ConnectionState { get; internal set; }
        public RunPlayerLifeState LifeState { get; internal set; }
        public PlayerRuntimeCheckpoint Checkpoint { get; internal set; }
    }

    /// <summary>One server run. Identity is supplied by the authenticated transport boundary.</summary>
    public sealed class RunSession
    {
        private readonly Dictionary<string, RunParticipant> byIdentity =
            new Dictionary<string, RunParticipant>(StringComparer.Ordinal);
        private readonly Dictionary<int, RunParticipant> byConnection =
            new Dictionary<int, RunParticipant>();
        private readonly List<RunParticipant> participants = new List<RunParticipant>();

        public string RunId { get; } = Guid.NewGuid().ToString("N");
        public bool IsRosterLocked { get; private set; }
        public IReadOnlyList<RunParticipant> Participants => participants.AsReadOnly();

        public void BeginRun()
        {
            if (participants.Count == 0)
                throw new InvalidOperationException("A run requires at least one admitted participant.");
            IsRosterLocked = true;
        }

        public bool ContainsIdentity(string identity) =>
            identity != null && byIdentity.ContainsKey(identity);

        public bool TryConnect(string authenticatedIdentity, int connectionId,
            out RunParticipant participant, out string error)
        {
            participant = null;
            error = null;
            if (string.IsNullOrWhiteSpace(authenticatedIdentity) || connectionId < 0)
            { error = "Missing authenticated identity."; return false; }
            if (byConnection.TryGetValue(connectionId, out participant))
            {
                if (participant.Identity == authenticatedIdentity) return true;
                participant = null;
                error = "Connection is already assigned.";
                return false;
            }
            if (byIdentity.TryGetValue(authenticatedIdentity, out participant))
            {
                if (participant.ConnectionState == RunConnectionState.Connected)
                { participant = null; error = "Participant is already connected."; return false; }
                participant.ConnectionId = connectionId;
                participant.ConnectionState = RunConnectionState.Connected;
            }
            else
            {
                if (IsRosterLocked)
                { error = "This run only accepts its original participants."; return false; }
                participant = new RunParticipant((ulong)participants.Count + 1, authenticatedIdentity, connectionId);
                participants.Add(participant);
                byIdentity.Add(authenticatedIdentity, participant);
            }
            byConnection.Add(connectionId, participant);
            return true;
        }

        public bool TryGetConnection(int connectionId, out RunParticipant participant) =>
            byConnection.TryGetValue(connectionId, out participant);

        public void AttachAvatar(int connectionId, uint avatarId, ushort connectionEpoch)
        {
            if (avatarId == 0 || connectionEpoch == 0 ||
                !byConnection.TryGetValue(connectionId, out RunParticipant participant))
                throw new InvalidOperationException("An avatar requires an admitted connection and combat epoch.");
            if (participant.AvatarId != 0 && participant.AvatarId != avatarId)
                throw new InvalidOperationException("Participant already has an avatar.");
            participant.AvatarId = avatarId;
            participant.ConnectionEpoch = connectionEpoch;
        }

        public void Disconnect(int connectionId, PlayerRuntimeCheckpoint checkpoint)
        {
            if (!byConnection.TryGetValue(connectionId, out RunParticipant participant)) return;
            if (checkpoint != null)
            {
                participant.Checkpoint = checkpoint;
                participant.LifeState = checkpoint.LifeState;
            }
            participant.AvatarId = 0;
            participant.ConnectionId = -1;
            participant.ConnectionState = RunConnectionState.Disconnected;
            byConnection.Remove(connectionId);
        }
    }
}
