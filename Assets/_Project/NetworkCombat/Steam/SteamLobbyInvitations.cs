using System;
using System.Collections.Generic;
using System.Linq;

namespace MonsterSupergroup.NetworkCombat
{
    public enum SteamFriendPresence { Offline, Online, Busy, Away, Snooze, LookingToTrade, LookingToPlay, Unknown }

    public readonly struct SteamInviteResult
    {
        public bool Submitted { get; }
        public string Code { get; }
        public string Message { get; }
        public SteamInviteResult(bool submitted, string code, string message)
        { Submitted = submitted; Code = code; Message = message; }
    }

    public sealed class SteamInviteFriend
    {
        public ulong SteamId { get; }
        public string Name { get; }
        public SteamFriendPresence Presence { get; }
        public bool InLobby { get; }
        public double RetryAt { get; }
        public SteamInviteResult LastResult { get; }
        public string PresenceLabel => Presence switch {
            SteamFriendPresence.Offline => "离线", SteamFriendPresence.Online => "在线",
            SteamFriendPresence.Busy => "忙碌", SteamFriendPresence.Away => "离开",
            SteamFriendPresence.Snooze => "休息", SteamFriendPresence.LookingToTrade => "想交易",
            SteamFriendPresence.LookingToPlay => "想玩游戏", _ => "状态未知"
        };
        internal SteamInviteFriend(ulong id, string name, SteamFriendPresence presence, bool inLobby,
            double retryAt, SteamInviteResult result)
        { SteamId = id; Name = string.IsNullOrEmpty(name) ? id.ToString() : name; Presence = presence;
            InLobby = inLobby; RetryAt = retryAt; LastResult = result; }
    }

    public sealed class SteamInviteFriendsSnapshot
    {
        public ulong LobbyId { get; }
        public IReadOnlyList<SteamInviteFriend> Friends { get; }
        public string Error { get; }
        public bool CanSend => string.IsNullOrEmpty(Error) && LobbyId != 0;
        internal SteamInviteFriendsSnapshot(ulong lobbyId, IEnumerable<SteamInviteFriend> friends, string error = "")
        { LobbyId = lobbyId; Friends = Array.AsReadOnly(friends.ToArray()); Error = error; }
    }

    internal readonly struct SteamInviteContext
    {
        internal readonly ulong LobbyId;
        internal readonly bool Initialized, Preparing;
        internal SteamInviteContext(ulong lobbyId, bool initialized, bool preparing)
        { LobbyId = lobbyId; Initialized = initialized; Preparing = preparing; }
    }
    internal readonly struct SteamInviteFriendInfo
    {
        internal readonly ulong Id;
        internal readonly string Name;
        internal readonly SteamFriendPresence Presence;
        internal SteamInviteFriendInfo(ulong id, string name, SteamFriendPresence presence)
        { Id = id; Name = name; Presence = presence; }
    }
    internal sealed class SteamInviteLobbyInfo
    {
        internal ulong[] Members = Array.Empty<ulong>();
        internal int Limit;
        internal bool Preparing, ValidHost;
    }
    // This boundary deliberately contains no overlay API. Tests never call native Steam.
    internal interface ISteamInviteApi
    {
        bool LoggedOn { get; }
        ulong LocalUser { get; }
        IEnumerable<SteamInviteFriendInfo> ReadFriends();
        bool IsFriend(ulong user);
        SteamInviteLobbyInfo ReadLobby(ulong lobby);
        bool Send(ulong lobby, ulong friend);
    }

    internal sealed class SteamLobbyInvitations
    {
        private readonly ISteamInviteApi api;
        private readonly Func<SteamInviteContext> context;
        private readonly Func<double> now;
        private readonly Dictionary<ulong, double> retryAt = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, SteamInviteResult> results = new Dictionary<ulong, SteamInviteResult>();
        private ulong cachedLobby;
        internal SteamInviteFriendsSnapshot Snapshot { get; private set; } = new SteamInviteFriendsSnapshot(0, Array.Empty<SteamInviteFriend>());
        internal bool HasRoomContext { get { var c = context(); return c.Initialized && c.Preparing && c.LobbyId != 0; } }
        internal SteamLobbyInvitations(ISteamInviteApi api, Func<SteamInviteContext> context, Func<double> now)
        { this.api = api; this.context = context; this.now = now; }

        internal void Clear()
        { cachedLobby = 0; retryAt.Clear(); results.Clear(); Snapshot = new SteamInviteFriendsSnapshot(0, Array.Empty<SteamInviteFriend>()); }
        internal void SynchronizeContext()
        {
            var c = context();
            if (!HasRoomContext || cachedLobby != c.LobbyId) Clear();
            if (HasRoomContext) cachedLobby = c.LobbyId;
        }
        private SteamInviteResult Validate(ulong expectedLobby, out SteamInviteLobbyInfo lobby)
        {
            lobby = null;
            var c = context();
            if (!c.Initialized || !api.LoggedOn) return Reject("steam_unavailable", "Steam 尚未连接，请登录 Steam 后重试。");
            if (expectedLobby == 0 || expectedLobby != c.LobbyId) return Reject("stale_lobby", "房间已变化，请返回当前房间重新打开好友列表。");
            if (!c.Preparing) return Reject("not_preparing", "当前房间已停止邀请，请返回房间查看状态。");
            lobby = api.ReadLobby(c.LobbyId);
            if (!lobby.ValidHost) return Reject("host_left", "房主已离开或大厅信息无效，无法发送邀请。");
            if (!lobby.Preparing) return Reject("not_preparing", "大厅已开始加载或关闭，无法发送邀请。");
            if (!lobby.Members.Contains(api.LocalUser)) return Reject("not_member", "你已不在这个 Steam 大厅中，请返回房间。");
            if (lobby.Limit <= 0) return Reject("capacity_unknown", "尚未取得大厅容量，请刷新后重试。");
            if (lobby.Members.Length >= lobby.Limit) return Reject("full", "房间已满，无法发送邀请。");
            return default;
        }
        internal SteamInviteFriendsSnapshot Refresh()
        {
            SynchronizeContext();
            ulong id = context().LobbyId;
            try
            {
                var validation = Validate(id, out var lobby);
                // A full room still shows friends and membership; native queries require Steam login.
                if (lobby == null || !HasRoomContext)
                    return Snapshot = new SteamInviteFriendsSnapshot(id, Array.Empty<SteamInviteFriend>(), validation.Message);
                var members = new HashSet<ulong>(lobby.Members);
                var friends = api.ReadFriends().Where(f => f.Id != 0 && f.Id != api.LocalUser)
                    .GroupBy(f => f.Id).Select(group => group.First())
                    .Select(f => new SteamInviteFriend(f.Id, f.Name, f.Presence, members.Contains(f.Id),
                        retryAt.TryGetValue(f.Id, out var at) ? at : 0,
                        results.TryGetValue(f.Id, out var result) ? result : default))
                    .OrderBy(f => f.Presence == SteamFriendPresence.Offline || f.Presence == SteamFriendPresence.Unknown ? 1 : 0)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.SteamId).ToArray();
                return Snapshot = new SteamInviteFriendsSnapshot(id, friends, validation.Message);
            }
            catch (Exception)
            { return Snapshot = new SteamInviteFriendsSnapshot(id, Array.Empty<SteamInviteFriend>(), "无法读取 Steam 好友或大厅，请刷新后重试。"); }
        }
        internal SteamInviteResult Send(ulong expectedLobby, ulong friend)
        {
            SynchronizeContext();
            SteamInviteResult result;
            try
            {
                result = Validate(expectedLobby, out var lobby);
                if (result.Code != null) return result;
                if (friend == 0 || friend == api.LocalUser || !api.IsFriend(friend))
                    return Reject("not_friend", "对方已不是有效的 Steam 好友，请刷新好友列表。");
                if (lobby.Members.Contains(friend)) return Reject("already_member", "这位好友已在大厅。");
                if (retryAt.TryGetValue(friend, out double deadline) && now() < deadline)
                    return Reject("cooldown", "邀请已提交，请等待 5 秒后再试。");
                bool submitted = api.Send(expectedLobby, friend);
                result = submitted ? new SteamInviteResult(true, "submitted", "邀请已提交，等待好友接受") :
                    Reject("api_rejected", "Steam 未能提交邀请，请手动重试。");
                if (submitted) retryAt[friend] = now() + 5;
            }
            catch (Exception exception)
            { result = Reject("api_exception:" + exception.GetType().Name, "Steam 邀请调用失败，请手动重试。"); }
            results[friend] = result;
            return result;
        }
        private static SteamInviteResult Reject(string code, string message) => new SteamInviteResult(false, code, message);
    }
}
