using System.Collections.Generic;
using System.Linq;
using Steamworks;

namespace MonsterSupergroup.NetworkCombat
{
    internal sealed class NativeSteamInviteApi : ISteamInviteApi
    {
        public bool LoggedOn => SteamUser.BLoggedOn();
        public ulong LocalUser => SteamUser.GetSteamID().m_SteamID;
        public IEnumerable<SteamInviteFriendInfo> ReadFriends()
        {
            const EFriendFlags flags = EFriendFlags.k_EFriendFlagImmediate;
            int count = SteamFriends.GetFriendCount(flags);
            for (int i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, flags);
                if (!IsFriend(id.m_SteamID)) continue;
                var state = (int)SteamFriends.GetFriendPersonaState(id);
                yield return new SteamInviteFriendInfo(id.m_SteamID, SteamFriends.GetFriendPersonaName(id),
                    state >= 0 && state <= 6 ? (SteamFriendPresence)state : SteamFriendPresence.Unknown);
            }
        }
        public bool IsFriend(ulong user)
        {
            var id = new CSteamID(user);
            return id.IsValid() && id.BIndividualAccount() &&
                SteamFriends.GetFriendRelationship(id) == EFriendRelationship.k_EFriendRelationshipFriend;
        }
        public SteamInviteLobbyInfo ReadLobby(ulong lobby)
        {
            var id = new CSteamID(lobby);
            int count = SteamMatchmaking.GetNumLobbyMembers(id);
            var members = new ulong[count];
            for (int i = 0; i < count; i++) members[i] = SteamMatchmaking.GetLobbyMemberByIndex(id, i).m_SteamID;
            var owner = SteamMatchmaking.GetLobbyOwner(id);
            bool metadataValid = SteamLobbyMetadata.TryGetReadyHostSteamId(
                SteamMatchmaking.GetLobbyData(id, SteamLobbyMetadata.GameKey),
                SteamMatchmaking.GetLobbyData(id, SteamLobbyMetadata.ProtocolKey),
                SteamMatchmaking.GetLobbyData(id, SteamLobbyMetadata.StateKey),
                SteamMatchmaking.GetLobbyData(id, SteamLobbyMetadata.HostSteamIdKey), out ulong host, out _);
            string phase = SteamMatchmaking.GetLobbyData(id, SteamLobbyMetadata.StateKey);
            return new SteamInviteLobbyInfo {
                Members = members, Limit = SteamMatchmaking.GetLobbyMemberLimit(id),
                Preparing = phase == SteamLobbyMetadata.PreparingState || phase == SteamLobbyMetadata.ReadyState,
                ValidHost = metadataValid && owner.m_SteamID == host && members.Contains(host)
            };
        }
        public bool Send(ulong lobby, ulong friend) => SteamMatchmaking.InviteUserToLobby(new CSteamID(lobby), new CSteamID(friend));
    }
}
