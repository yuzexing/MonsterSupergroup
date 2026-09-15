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
        public PlayerWeaponCooldownSnapshot[] WeaponCooldowns = Array.Empty<PlayerWeaponCooldownSnapshot>();
        public PlayerDashSnapshot? Dash;
        public PlayerUltimateSnapshot? Ultimate;
        public PlayerSummonMaturitySnapshot[] SummonMaturities = Array.Empty<PlayerSummonMaturitySnapshot>();
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
    public sealed partial class RunSession
    {
        private readonly Dictionary<string, RunParticipant> byIdentity =
            new Dictionary<string, RunParticipant>(StringComparer.Ordinal);
        private readonly Dictionary<int, RunParticipant> byConnection =
            new Dictionary<int, RunParticipant>();
        private readonly List<RunParticipant> participants = new List<RunParticipant>();
        private readonly HashSet<ulong> launchRoster = new HashSet<ulong>();
        private readonly List<RunParticipant> launchParticipants = new List<RunParticipant>();

        private ulong nextParticipantId = 1;
        public string RunId { get; private set; } = Guid.NewGuid().ToString("N");
        public uint Round { get; private set; } = 1;
        public bool IsRunEnded { get; private set; }
        public bool IsRosterLocked { get; private set; }
        public bool IsRunStarted { get; private set; }
        public IReadOnlyList<RunParticipant> Participants =>
            (IsRosterLocked ? launchParticipants : participants).AsReadOnly();

        public void BeginRun()
        {
            if (IsRunEnded) throw new InvalidOperationException("The run has ended.");
            if (participants.Count == 0)
                throw new InvalidOperationException("A run requires at least one admitted participant.");
            if (!IsRosterLocked)
                SealRoster(participants.FindAll(p => p.ConnectionState == RunConnectionState.Connected).ConvertAll(p => p.Id));
            IsRunStarted = true;
        }

        public void SealRoster(IEnumerable<ulong> ids)
        {
            if (IsRosterLocked) return;
            var requested = new HashSet<ulong>(ids ?? throw new ArgumentNullException(nameof(ids)));
            if (requested.Count == 0) throw new InvalidOperationException("The launch roster is empty.");
            foreach (ulong id in requested)
                if (!participants.Exists(p => p.Id == id && p.ConnectionState == RunConnectionState.Connected))
                    throw new InvalidOperationException("The launch roster contains a missing or disconnected participant.");
            foreach (var participant in participants)
                if (requested.Contains(participant.Id)) { launchRoster.Add(participant.Id); launchParticipants.Add(participant); }
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
                if (IsRosterLocked && !launchRoster.Contains(participant.Id))
                { participant = null; error = "This participant was not in the launch roster."; return false; }
                if (participant.ConnectionState == RunConnectionState.Connected)
                { participant = null; error = "Participant is already connected."; return false; }
                participant.ConnectionId = connectionId;
                participant.ConnectionState = RunConnectionState.Connected;
            }
            else
            {
                if (IsRosterLocked)
                { error = "This run only accepts its original participants."; return false; }
                participant = new RunParticipant(nextParticipantId++, authenticatedIdentity, connectionId);
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

        public bool TryEndRun(CombatLedger ledger)
        {
            if (!IsRunStarted || IsRunEnded || ledger == null) return false;
            int online = 0;
            foreach (var participant in Participants)
            {
                if (participant.ConnectionState != RunConnectionState.Connected) continue;
                online++;
                // An admitted/reconnecting avatar with no restored baseline is unknown, not dead.
                if (participant.AvatarId == 0 || !ledger.TryGetState(participant.AvatarId, out var state) || state.Alive)
                    return false;
            }
            if (online == 0) return false;
            IsRunEnded = true;
            return true;
        }

        public bool TryCompleteStage()
        {
            if (!IsRunStarted || IsRunEnded) return false;
            IsRunEnded = true;
            return true;
        }

        public void BeginNextRound()
        {
            if (!IsRunEnded) throw new InvalidOperationException("Only an ended run can be replaced.");
            RunId = Guid.NewGuid().ToString("N"); Round++;
            IsRunStarted = false; IsRunEnded = false; IsRosterLocked = false;
            launchRoster.Clear(); launchParticipants.Clear();
            foreach (var participant in participants)
            {
                participant.AvatarId = 0; participant.ConnectionEpoch = 0;
                participant.Checkpoint = null; participant.LifeState = RunPlayerLifeState.Active;
            }
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
