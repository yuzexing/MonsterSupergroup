using System;
using System.Globalization;
using Mirror;
using MonsterSupergroup.GAS;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkDiagnosticsObservation
    {
        [Serializable] private sealed class Capabilities
        {
            public int version = 1;
            public bool lightweightNetworkEnabled, fullCombatEvidenceEnabled, injectionEnabled;
            public bool sendResults = true, batchMembers = true, connectionLifetime = true, businessOutcomes = true;
            public double samplingIntervalSeconds = 1;
        }
        [Serializable] private sealed class Configuration
        {
            public string evidenceMode, appliedCaptureMode;
            public bool lightweightNetworkEnabled, injectionEnabled, automaticCapture;
        }
        private long networkSequence;
        private int captureFailures, parseFailures;
        private Action<DiagnosticRecord> lightweightSink;
        private static NetworkDiagnosticsObservation activeLightweight;
        private string NextNetworkSequence() => (++networkSequence).ToString(CultureInfo.InvariantCulture);
        private string ReadExecutablePath()
        {
            try { return process.MainModule?.FileName; }
            catch (Exception) { return null; }
        }
        private static string ReadLocalSteamIdentity()
        {
#if !DISABLESTEAMWORKS
            try { return Steamworks.SteamUser.GetSteamID().m_SteamID.ToString(CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
#else
            return null;
#endif
        }
        private void BeginLightweightCapture()
        {
            activeLightweight = this;
            lightweightSink = WriteNetworkRecord;
            Diagnostics.NetworkMessageEvidence.LightweightSink = lightweightSink;
            GatewayEvidenceDecision.LightweightSink = lightweightSink;
            NetworkLightEvidence.Reset();
        }
        internal static void ReportNetworkCaptureFailure(string stage, Exception error)
        {
            var active = activeLightweight;
            if (active == null) return;
            active.captureFailures++;
            // A final counter remains authoritative if even the small gap marker cannot serialize.
            try { active.WriteNetworkRecord(new DiagnosticRecord { stage = "network.capture_gap", outcome = "CaptureFailed",
                reason = stage + ":" + error.GetType().Name, critical = true }); }
            catch (Exception) { }
        }
        private void WriteNetworkRecord(DiagnosticRecord source)
        {
            if (log == null || source == null) return;
            try
            {
                var record = source.Copy();
                if (record.stage == "network.evidence" && record.outcome == "Gap") captureFailures++;
                record.schemaVersion = SchemaVersion;
                record.captureId = captureId;
                record.recordSequence = NextNetworkSequence();
                record.runId = NetworkCombatWorld.Instance?.GetComponent<NetworkWaveProgress>()?.Snapshot.RunId ?? "boot";
                record.round = NetworkCombatWorld.CurrentRound;
                record.role ??= NetworkServer.active ? "Host" : NetworkClient.active ? "Client" : "Offline";
                record.utc = DateTime.UtcNow.ToString("o");
                record.monotonicTime = Time.realtimeSinceStartupAsDouble;
                record.networkTime = NetworkTime.time;
                record.frame = Time.frameCount;
                if (record.target != 0 && record.entityGeneration == null) record.entityGeneration = NetworkLightEvidence.Birth(record.target);
                // Serialize synchronously to detach all arrays before the writer enqueues the string.
                var envelope = JObject.FromObject(new { kind = "network_event", record });
                var payload = envelope["record"]?["input"] as JObject;
                if (payload?["parseFailure"]?.Type == JTokenType.String || payload?["complete"]?.Value<bool>() == false)
                    parseFailures++;
                log.WriteLine(envelope.ToString(Formatting.None));
            }
            catch (Exception) { captureFailures++; }
        }
        private void EndLightweightCapture()
        {
            if (Diagnostics.NetworkMessageEvidence.LightweightSink == lightweightSink)
                Diagnostics.NetworkMessageEvidence.LightweightSink = null;
            if (GatewayEvidenceDecision.LightweightSink == lightweightSink) GatewayEvidenceDecision.LightweightSink = null;
            if (activeLightweight == this) activeLightweight = null;
            if (log == null) return;
            try
            {
                log.WriteLine(JsonConvert.SerializeObject(new {
                    kind = "end", schemaVersion = SchemaVersion, captureId, recordSequence = NextNetworkSequence(),
                    utc = DateTime.UtcNow.ToString("o"), time = Time.realtimeSinceStartupAsDouble,
                    normalClose = normalCloseRequested, captureFailures, parseFailures,
                    writerFailures = log.Failure == null ? 0 : 1, writerFailure = log.Failure,
                    pendingBytes = LimboObservationLog.PendingBytes,
                    complete = normalCloseRequested && captureFailures == 0 && log.Failure == null,
                    completenessRequiresClosedStatus = true
                }));
            }
            catch (Exception) { captureFailures++; }
        }
    }
}
