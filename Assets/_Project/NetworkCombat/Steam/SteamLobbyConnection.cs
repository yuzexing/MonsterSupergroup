using System;
using System.Globalization;
using Steamworks;

namespace MonsterSupergroup.NetworkCombat
{
    internal static class SteamLobbyConnection
    {
        internal static bool IsValidLobby(ulong value)
        {
            var id = new CSteamID(value);
            return id.IsValid() && id.IsLobby();
        }

        internal static string Format(ulong lobby)
        {
            if (!IsValidLobby(lobby)) throw new ArgumentException("Invalid Steam lobby ID.", nameof(lobby));
            return "+connect_lobby " + lobby.ToString(CultureInfo.InvariantCulture);
        }

        internal static ulong Parse(string commandLine) => string.IsNullOrWhiteSpace(commandLine) || commandLine.Length > 4096
            ? 0 : Parse(commandLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        internal static ulong ParseStartup(string steamCommandLine, string[] processArguments)
        {
            // Steam URL parameters take precedence when they contain a lobby request. Ordinary
            // Steam launch options must not hide the legacy +connect_lobby OS argument.
            bool hasRequest = steamCommandLine != null &&
                (steamCommandLine.Contains("+connect_lobby") || steamCommandLine.IndexOf("--connect-lobby=", StringComparison.OrdinalIgnoreCase) >= 0);
            return hasRequest ? Parse(steamCommandLine) : Parse(processArguments);
        }

        internal static ulong Parse(string[] arguments)
        {
            ulong result = 0;
            for (int i = 0; arguments != null && i < arguments.Length; i++)
            {
                string argument = arguments[i] ?? string.Empty;
                string value;
                if (argument == "+connect_lobby") value = ++i < arguments.Length ? arguments[i] : null;
                else if (argument.StartsWith("--connect-lobby=", StringComparison.OrdinalIgnoreCase))
                    value = argument.Substring("--connect-lobby=".Length);
                else continue;
                if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) ||
                    !IsValidLobby(id) || (result != 0 && result != id)) return 0;
                result = id;
            }
            return result;
        }
    }

    // Publishing is independent of host authority: each authenticated member advertises their own presence.
    internal sealed class SteamLobbyPresence
    {
        private readonly Func<string, bool> publish;
        private readonly Action<string> log;
        private string desired, published;
        private double retryAt;
        internal SteamLobbyPresence(Func<string, bool> publish, Action<string> log)
        { this.publish = publish; this.log = log; }

        internal static string ConnectionFor(ulong lobby, bool authenticated, bool preparing, bool validMember,
            int steamMembers, int roomMembers, int capacity) => SteamLobbyConnection.IsValidLobby(lobby) &&
            authenticated && preparing && validMember && capacity > 0 && steamMembers > 0 &&
            roomMembers > 0 && Math.Max(steamMembers, roomMembers) < capacity ? SteamLobbyConnection.Format(lobby) : string.Empty;

        internal void Update(string connection, double now)
        {
            connection ??= string.Empty;
            if (desired != connection) { desired = connection; retryAt = 0; }
            if (published == desired || now < retryAt) return;
            try
            {
                bool success = publish(desired);
                log($"stage=presence connect=\"{desired}\" result={(success ? "published" : "failed")}");
                if (success) published = desired;
            }
            catch (Exception error) { log($"stage=presence result=exception type={error.GetType().Name}"); }
            retryAt = now + 5;
        }
    }

    internal sealed class SteamLobbyJoinQueue
    {
        internal ulong Target { get; private set; }
        private double deadline;
        internal bool Request(ulong lobby, double now)
        {
            if (!SteamLobbyConnection.IsValidLobby(lobby) || Target == lobby) return false;
            Target = lobby; deadline = now + 30; return true;
        }
        internal void Clear() => Target = 0;
        internal ulong Take(bool cleanupComplete, double now, out bool timedOut)
        {
            timedOut = Target != 0 && now >= deadline;
            if (timedOut) { Clear(); return 0; }
            if (!cleanupComplete) return 0;
            ulong target = Target; Clear(); return target;
        }
    }
}
