using System;
using System.Linq;

namespace MonsterSupergroup.NetworkCombat
{
    public static class LimboManualOptions
    {
        // An allowlist prevents future fixture flags from silently becoming manual-play inputs.
        private static readonly string[] ManualKeys = { "role", "profile", "output", "port", "wait-for", "windowed", "manual", "log-detail", "version", "session" };
        public static string Validate(string[] arguments)
        {
            string Get(string key) => arguments.FirstOrDefault(a => a.StartsWith("--limbo-" + key + "=", StringComparison.Ordinal))?.Split('=', 2)[1];
            string detail = Get("log-detail");
            if (detail != null && detail != "light" && detail != "detailed") return "Use --limbo-log-detail=light or detailed.";
            string performance = Get("performance-preset");
            if (performance != null && performance != "720p60" && performance != "4k144") return "Unknown performance preset.";
            if (Get("manual") != "true") return null;
            if (Get("profile") != "full" || detail != "light") return "Manual play requires Full and light logging.";
            foreach (string argument in arguments.Where(a => a.StartsWith("--limbo-", StringComparison.Ordinal)))
            {
                string key = argument.Substring(8).Split('=')[0];
                if (!ManualKeys.Contains(key)) return "Manual play refuses auxiliary argument: " + argument;
                if (arguments.Count(a => a.StartsWith("--limbo-" + key + "=", StringComparison.Ordinal)) != 1)
                    return "Duplicate reference argument: " + key;
            }
            if (Get("role") != "host" && Get("role") != "client") return "Manual role must be host or client.";
            if (Get("wait-for") != "1" && Get("wait-for") != "2") return "Manual play supports one or two local participants.";
            if (Get("windowed") != "true" || string.IsNullOrWhiteSpace(Get("output"))) return "Manual play requires a writable output directory and windowed defaults.";
            return null;
        }
    }
}
