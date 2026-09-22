using System;

namespace MonsterSupergroup.Builds
{
    public enum BuildRejection { None, ClientOlder, HostOlder, UnknownVersion, ProtocolMismatch, InvalidPackage }
    public static class BuildCompatibility
    {
        private const string Prefix = "@build-notice:";
        public static BuildRejection Compare(string client, string host)
        {
            if (!GameVersion.TryParse(client, out var a) || !GameVersion.TryParse(host, out var b)) return BuildRejection.UnknownVersion;
            int difference = a.CompareTo(b);
            return difference < 0 ? BuildRejection.ClientOlder : difference > 0 ? BuildRejection.HostOlder : BuildRejection.None;
        }
        public static BuildRejection CheckIdentity(string request, string hostVersion, string protocol, out string clientVersion)
        {
            clientVersion = null;
            if (string.IsNullOrEmpty(request) || request.Length > 128) return BuildRejection.UnknownVersion;
            int colon = request.IndexOf(':');
            if (colon < 1) return BuildRejection.UnknownVersion;
            clientVersion = request.Substring(colon + 1);
            if (request.Substring(0, colon) != protocol) return BuildRejection.ProtocolMismatch;
            return Compare(clientVersion, hostVersion);
        }
        // Reuses the existing Error string without changing authentication message layouts.
        public static string Encode(BuildRejection reason, string client, string host, bool steam) =>
            Prefix + reason + "|" + SafeVersion(client) + "|" + SafeVersion(host) + "|" + (steam ? "steam" : "local");
        private static string SafeVersion(string value) => GameVersion.TryParse(value, out _) ? value : "unknown";
        public static bool TryDecode(string value, out BuildRejection reason, out string client, out string host, out bool steam)
        {
            reason = default; client = host = null; steam = false;
            if (value == null || value.Length > 180 || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var parts = value.Substring(Prefix.Length).Split('|');
            if (parts.Length != 4 || !Enum.TryParse(parts[0], out reason) || !Enum.IsDefined(typeof(BuildRejection), reason) || reason == BuildRejection.None ||
                (parts[1] != "unknown" && !GameVersion.TryParse(parts[1], out _)) ||
                (parts[2] != "unknown" && !GameVersion.TryParse(parts[2], out _)) || (parts[3] != "steam" && parts[3] != "local")) return false;
            client = parts[1]; host = parts[2]; steam = parts[3] == "steam"; return true;
        }
    }
}
