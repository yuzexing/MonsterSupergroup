using System;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public enum PreparationPhase : byte { None, Preparing, Loading, InGame, GameOver, Transitioning }
    public enum RunEndAction : byte { None, ReturnToRoom, Restart }

    [Serializable]
    public struct PreparationMember
    {
        public ulong ParticipantId;
        public int Seat;
        public string DisplayName;
        public bool IsHost;
        public uint CharacterId;
        public uint WeaponId;
        public uint LoadoutRevision;
        public bool Ready;
        public bool GameplayReady;
    }

    public struct PreparationRoomSnapshot : NetworkMessage
    {
        public string RunId;
        public uint Round;
        public string EndReason;
        public RunEndAction NextAction;
        public uint Revision;
        public ulong SelfId;
        public PreparationPhase Phase;
        public bool Online;
        public string Map;
        public string Difficulty;
        public PreparationMember[] Members;
    }

    public struct RequestSetLoadout : NetworkMessage
    {
        public string RunId;
        public uint CharacterId;
        public uint WeaponId;
    }
    public struct RequestSetReady : NetworkMessage
    {
        public string RunId;
        public uint LoadoutRevision;
        public bool Ready;
    }
    public struct RequestStartGame : NetworkMessage { public string RunId; public uint Revision; }
    public struct GameplayReady : NetworkMessage { public string RunId; public uint AvatarId; public uint WeaponId; }
    public struct PreparationNotice : NetworkMessage { public string Reason; public bool Closing; }
    public struct RequestRunEndAction : NetworkMessage { public string RunId; public uint Round; public RunEndAction Action; }
    public struct RunCleanupRequest : NetworkMessage { public string RunId; public uint Round; }
    public struct RunCleanupReady : NetworkMessage { public string RunId; public uint Round; }

    /// <summary>Copied once when the roster is sealed; never edited by preparation requests.</summary>
    public sealed class RunLaunchConfig
    {
        public string RunId { get; }
        public string Map { get; }
        public string Difficulty { get; }
        public System.Collections.Generic.IReadOnlyList<PreparationMember> Members { get; }
        public RunLaunchConfig(string runId, string map, string difficulty, PreparationMember[] members)
        {
            RunId = runId; Map = map; Difficulty = difficulty;
            Members = Array.AsReadOnly((PreparationMember[])members.Clone());
        }
        public uint WeaponFor(ulong participantId)
        {
            foreach (var member in Members) if (member.ParticipantId == participantId) return member.WeaponId;
            throw new InvalidOperationException("Participant is not in the launch roster.");
        }
    }
}
