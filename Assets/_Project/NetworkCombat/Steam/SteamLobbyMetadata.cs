using System;
using System.Globalization;
using Steamworks;

namespace MonsterSupergroup.NetworkCombat
{
    public readonly struct SteamLobbySummary
    {
        public SteamLobbySummary(
            ulong lobbyId,
            ulong hostSteamId,
            string name,
            int memberCount,
            int memberLimit)
        {
            LobbyId = lobbyId;
            HostSteamId = hostSteamId;
            Name = name;
            MemberCount = memberCount;
            MemberLimit = memberLimit;
        }

        public ulong LobbyId { get; }
        public ulong HostSteamId { get; }
        public string Name { get; }
        public int MemberCount { get; }
        public int MemberLimit { get; }
    }

    public static class SteamLobbyMetadata
    {
        public const string GameKey = "game";
        public const string GameValue = "monster_supergroup";
        public const string ProtocolKey = "protocol";
        public const string ProtocolValue = "1";
        public const string StateKey = "state";
        public const string StartingState = "starting";
        public const string ReadyState = "ready";
        public const string ClosedState = "closed";
        public const string HostSteamIdKey = "host_steam_id";
        public const string NameKey = "name";

        public static bool TryGetReadyHostSteamId(
            string game,
            string protocol,
            string state,
            string hostSteamId,
            out ulong parsedHostSteamId,
            out string error)
        {
            parsedHostSteamId = 0ul;
            error = null;

            if (!string.Equals(game, GameValue, StringComparison.Ordinal))
            {
                error = "Lobby belongs to another game.";
                return false;
            }
            if (!string.Equals(protocol, ProtocolValue, StringComparison.Ordinal))
            {
                error = "Lobby protocol is incompatible.";
                return false;
            }
            if (!string.Equals(state, ReadyState, StringComparison.Ordinal))
            {
                error = "Lobby host is not ready.";
                return false;
            }
            if (!ulong.TryParse(
                    hostSteamId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out parsedHostSteamId) ||
                !IsValidHostSteamId(parsedHostSteamId))
            {
                parsedHostSteamId = 0ul;
                error = "Lobby host SteamID64 is missing or invalid.";
                return false;
            }

            return true;
        }

        public static bool TryCreateSummary(
            ulong lobbyId,
            string game,
            string protocol,
            string state,
            string hostSteamId,
            string name,
            int memberCount,
            int memberLimit,
            out SteamLobbySummary summary)
        {
            summary = default;
            if (lobbyId == 0ul || memberCount < 0 || memberLimit <= 0 ||
                memberCount >= memberLimit ||
                !TryGetReadyHostSteamId(
                    game,
                    protocol,
                    state,
                    hostSteamId,
                    out ulong parsedHostSteamId,
                    out _))
            {
                return false;
            }

            string displayName = string.IsNullOrWhiteSpace(name)
                ? $"Lobby {lobbyId}"
                : name;
            summary = new SteamLobbySummary(
                lobbyId,
                parsedHostSteamId,
                displayName,
                memberCount,
                memberLimit);
            return true;
        }

        public static bool IsValidHostSteamId(ulong steamId)
        {
            if (steamId == 0ul)
            {
                return false;
            }

            var id = new CSteamID(steamId);
            return id.IsValid() && id.BIndividualAccount();
        }
    }
}
