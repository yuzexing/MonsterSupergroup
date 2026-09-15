using System;
using System.Collections.Generic;
using System.Linq;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Server-owned preparation data. No Avatar, Build or gameplay side effects.</summary>
    public sealed class PreparationRoom
    {
        public const int Capacity = 4;
        public const uint Character = 1;
        public const uint DefaultWeapon = 2;
        private readonly HashSet<uint> allowedWeapons;
        private readonly Dictionary<ulong, PreparationMember> members = new Dictionary<ulong, PreparationMember>();
        public string RunId { get; }
        public uint Round { get; }
        public string EndReason { get; private set; }
        public RunEndAction NextAction { get; private set; }
        public uint Revision { get; private set; } = 1;
        public PreparationPhase Phase { get; private set; } = PreparationPhase.Preparing;
        public bool Online { get; }
        public string Map { get; }
        public string Difficulty => "中等";
        public int Count => members.Count;
        public RunLaunchConfig Launch { get; private set; }
        public PreparationRoom(string runId, string map, bool online, IEnumerable<uint> weapons, uint round = 1)
        {
            RunId = runId; Map = map; Online = online; Round = round;
            allowedWeapons = new HashSet<uint>(weapons);
            if (!allowedWeapons.Contains(DefaultWeapon)) throw new ArgumentException("Default weapon must be available.");
        }
        public bool Join(ulong id, string name, bool host, out string error)
        {
            error = null;
            if (members.ContainsKey(id)) return true;
            if (Phase != PreparationPhase.Preparing) { error = "游戏已经开始，仅允许本局成员重连。"; return false; }
            if (Count >= Capacity) { error = "房间已满（最多四人）。"; return false; }
            int seat = Enumerable.Range(0, Capacity).First(i => !members.Values.Any(m => m.Seat == i));
            members.Add(id, new PreparationMember { ParticipantId = id, Seat = seat,
                DisplayName = string.IsNullOrWhiteSpace(name) ? $"玩家 {id}" : name,
                IsHost = host, CharacterId = Character, WeaponId = DefaultWeapon, LoadoutRevision = 1 });
            Revision++;
            return true;
        }
        public void Leave(ulong id)
        {
            if (Phase == PreparationPhase.Preparing && members.Remove(id)) Revision++;
        }
        public bool SetLoadout(ulong id, uint character, uint weapon, out string error)
        {
            if (!Editable(id, out var member, out error)) return false;
            if (character != Character || !allowedWeapons.Contains(weapon))
            { error = "该角色或初始武器尚未开放。"; return false; }
            if (member.CharacterId == character && member.WeaponId == weapon) return true;
            member.CharacterId = character; member.WeaponId = weapon; member.Ready = false;
            member.LoadoutRevision++; members[id] = member; Revision++;
            return true;
        }
        public bool SetReady(ulong id, uint loadoutRevision, bool ready, out string error)
        {
            if (!Editable(id, out var member, out error)) return false;
            if (member.LoadoutRevision != loadoutRevision)
            { error = "选择已经更新，请确认当前配置后重新准备。"; return false; }
            if (member.Ready == ready) return true;
            member.Ready = ready; members[id] = member; Revision++;
            return true;
        }
        public bool CanStart(ulong id) => Phase == PreparationPhase.Preparing &&
            members.TryGetValue(id, out var self) && self.IsHost && Count > 0 &&
            (Count == 1 || members.Values.All(m => m.Ready));
        public bool Start(ulong id, uint revision, out string error)
        {
            error = null;
            if (!members.TryGetValue(id, out var member) || !member.IsHost)
            { error = "只有房主可以开始游戏。"; return false; }
            if (Phase == PreparationPhase.Loading || Phase == PreparationPhase.InGame) return true;
            if (Phase != PreparationPhase.Preparing) { error = "本局已结束。"; return false; }
            if (Revision != revision) { error = "房间信息已更新，请重新确认开始。"; return false; }
            if (!CanStart(id)) { error = "等待所有玩家准备完成。"; return false; }
            Launch = new RunLaunchConfig(RunId, Map, Difficulty, OrderedMembers());
            Phase = PreparationPhase.Loading; Revision++;
            return true;
        }
        public bool MarkGameplayReady(ulong id)
        {
            if (Phase != PreparationPhase.Loading || !members.TryGetValue(id, out var member) || member.GameplayReady) return false;
            member.GameplayReady = true; members[id] = member; Revision++; return true;
        }
        public bool AllGameplayReady => Count > 0 && members.Values.All(m => m.GameplayReady);
        public void BeginCombat()
        {
            if (Phase != PreparationPhase.Loading || !AllGameplayReady) throw new InvalidOperationException("Loading barrier incomplete.");
            Phase = PreparationPhase.InGame; Revision++;
        }
        public PreparationRoomSnapshot Snapshot(ulong self) => new PreparationRoomSnapshot {
            RunId = RunId, Round = Round, EndReason = EndReason, NextAction = NextAction,
            Revision = Revision, SelfId = self, Phase = Phase, Online = Online,
            Map = Map, Difficulty = Difficulty, Members = OrderedMembers() };
        private PreparationMember[] OrderedMembers() => members.Values.OrderBy(m => m.Seat).ToArray();
        public void EndRun(string reason = null)
        {
            if (Phase != PreparationPhase.InGame) return;
            Phase = PreparationPhase.GameOver; EndReason = reason ?? "所有在线玩家均已倒地"; Revision++;
        }
        public bool RequestEndAction(ulong actor, RunEndAction action, out string error)
        {
            error = null;
            if (!members.TryGetValue(actor, out var member) || !member.IsHost)
            { error = "只有房主可以选择下一步。"; return false; }
            if (Phase != PreparationPhase.GameOver || (action != RunEndAction.ReturnToRoom && action != RunEndAction.Restart))
            { error = "本次操作已处理或当前阶段不可操作。"; return false; }
            NextAction = action; Phase = PreparationPhase.Transitioning; Revision++; return true;
        }
        public void RestoreParty(IEnumerable<PreparationMember> party, bool restart)
        {
            if (Phase != PreparationPhase.Preparing || Count != 0) throw new InvalidOperationException("Expected an empty new room.");
            foreach (var source in party)
            {
                var member = source; member.Ready = false; member.GameplayReady = false; member.LoadoutRevision++;
                members.Add(member.ParticipantId, member);
            }
            if (restart)
            {
                if (Count == 0 || !members.Values.Any(m => m.IsHost)) throw new InvalidOperationException("A host is required.");
                Launch = new RunLaunchConfig(RunId, Map, Difficulty, OrderedMembers());
                Phase = PreparationPhase.Loading;
            }
            Revision++;
        }
        private bool Editable(ulong id, out PreparationMember member, out string error)
        {
            member = default; error = null;
            if (Phase != PreparationPhase.Preparing) { error = "正在开局，无法修改配置。"; return false; }
            if (!members.TryGetValue(id, out member)) { error = "尚未加入房间。"; return false; }
            return true;
        }
    }
}
