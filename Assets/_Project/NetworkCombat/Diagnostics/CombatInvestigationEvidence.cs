using System;
using System.Linq;
using MonsterSupergroup.GAS;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    /// <summary>Low frequency investigation facts. This gate never enables evidence or changes game state.</summary>
    public static class CombatInvestigationEvidence
    {
        public const int SchemaVersion = 1;
        private static bool configured;
        private static string requested = "default";
        public static bool Enabled => configured && CombatEvidence.Enabled;
        public static object Configuration => new { schemaVersion = SchemaVersion, requested, enabled = configured,
            stateFormat = "full-domain-snapshot", localRevisionScope = "capture/birth/perspective/domain",
            clock = "record.monotonicTime", protocolChanged = false,
            capabilities = configured ? new[] { "player-identity", "build-public-mutation-boundaries", "server-progression-commits",
                "client-progression-syncvar-observations", "selection-request-publish-apply" } : Array.Empty<string>() };

        public static void Configure(EvidenceProfile profile, string[] args)
        {
            const string prefix = "--combat-evidence-investigation=";
            var values = args?.Where(a => a.StartsWith(prefix, StringComparison.Ordinal)).ToArray() ?? Array.Empty<string>();
            if (values.Length > 1) throw new ArgumentException("Specify combat evidence investigation only once.");
            requested = values.Length == 0 ? "default" : values[0].Substring(prefix.Length);
            if (requested != "default" && requested != "on" && requested != "off")
                throw new ArgumentException("Unknown combat evidence investigation setting: " + requested);
            configured = profile == EvidenceProfile.Diagnostic && requested != "off";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { configured = false; requested = "default"; }

        // Callers constructing payloads should check Enabled first. Publish detaches arbitrary DTOs;
        // Store.TryWrite accounts/releases them through the existing admission path.
        public static bool Record(string stage, string outcome, string reason = null, uint source = 0,
            uint target = 0, object input = null, object before = null, object after = null)
        {
            if (!Enabled) return false;
            var record = new DiagnosticRecord { stage = Stage(stage), outcome = outcome, reason = reason,
                source = source, target = target, role = Mirror.NetworkServer.active ? "Server" : "Client",
                input = input, before = before, after = after, critical = true, estimatedBytes = 4096 };
            return Publish(record);
        }

        public static bool Capture(string stage, string outcome, Func<object> capture, string reason = null,
            uint source = 0, uint target = 0, string perspective = null, int estimatedBytes = 4096)
        {
            if (!Enabled) return false;
            var record = new DiagnosticRecord { stage = Stage(stage), outcome = outcome, reason = reason,
                source = source, target = target, role = perspective ?? (Mirror.NetworkServer.active ? "Server" : "Client"),
                critical = true, estimatedBytes = estimatedBytes };
            try { record.input = capture(); return Publish(record); }
            catch (Exception error) { CombatEvidence.ReportCaptureFailure(record, error); return false; }
        }

        private static bool Publish(DiagnosticRecord record)
        {
            try
            {
                long retained = 512;
                record.input = Detach(record.input, ref retained);
                record.before = Detach(record.before, ref retained);
                record.after = Detach(record.after, ref retained);
                record.estimatedBytes = checked((int)Math.Max(record.estimatedBytes, retained));
                return CombatEvidence.Write(record);
            }
            catch (Exception error) { CombatEvidence.ReportCaptureFailure(record, error); return false; }
        }
        private static object Detach(object value, ref long retained)
        {
            if (value == null) return null;
            // New low-frequency facts have arbitrary nested DTOs; the legacy Freeze fallback does
            // not clone those. Detach once here and conservatively charge every retained token.
            JToken token = value is JToken json ? json.DeepClone() : JToken.FromObject(value, JsonSerializer.Create(EvidenceJson.Settings));
            Charge(token, ref retained);
            return token;
        }
        private static void Charge(JToken token, ref long retained)
        {
            retained += 256;
            if (token is JProperty property) retained += 2L * property.Name.Length;
            if (token is JValue value && value.Value is string text) retained += 2L * text.Length;
            if (retained > 8L << 20) throw new InvalidOperationException("InvestigationPayloadLimitExceeded");
            foreach (JToken child in token.Children()) Charge(child, ref retained);
        }

        private static string Stage(string value) => value != null && value.StartsWith("investigation.", StringComparison.Ordinal)
            ? value : "investigation." + value;
    }
}
