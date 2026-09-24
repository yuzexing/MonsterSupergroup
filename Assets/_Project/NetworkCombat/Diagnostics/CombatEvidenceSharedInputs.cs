using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public sealed partial class CombatEvidenceStore
    {
        private static List<IDisposable> AcquireSharedLeases(MonsterSupergroup.GAS.DiagnosticRecord record)
        {
            if (!ContainsShared(record.input) && !ContainsShared(record.before) && !ContainsShared(record.after)) return null;
            var leases = new List<IDisposable>();
            try
            {
                SharedEvidencePayload.CollectLeases(record.input, leases);
                SharedEvidencePayload.CollectLeases(record.before, leases);
                SharedEvidencePayload.CollectLeases(record.after, leases);
                return leases;
            }
            catch { ReleaseSharedLeases(leases); throw; }
        }
        private static bool ContainsShared(object value, int depth = 0)
        {
            if (depth > 32) throw new InvalidDataException("SharedEvidenceGraphLimit");
            switch (value)
            {
                case SharedEvidencePayload _: return true;
                case object[] array:
                    foreach (var item in array) if (ContainsShared(item, depth + 1)) return true;
                    return false;
                case ReplayOutResult result: return ContainsShared(result.result, depth + 1) || ContainsShared(result.outValues, depth + 1);
                case CanonicalReceiveEvidence received: return ContainsShared(received.batch, depth + 1);
                default: return false;
            }
        }
        private static void ReleaseSharedLeases(List<IDisposable> leases)
        { using var releasing = EvidenceServiceTiming.Measure(EvidenceServiceStage.LeaseRelease); if (leases != null) foreach (var lease in leases) lease.Dispose(); }

        private void MaterializeSharedPayloads(Source source, MonsterSupergroup.GAS.DiagnosticRecord record)
        {
            using var materializing = EvidenceServiceTiming.Measure(EvidenceServiceStage.SharedInputs);
            using var resolution = SharedEvidencePayloadReferenceConverter.BeginResolution(shared => {
                if (!shared.TryGetReference(source.Directory, out string reference) ||
                    !durableFiles.ContainsKey(Path.Combine(source.Directory, reference).Substring(Root.Length + 1).Replace('\\', '/')))
                {
                    reference = SavePayload(source, "inputs", EvidenceJson.EncodeBounded(shared.ValueForWriter));
                    shared.SetReference(source.Directory, reference);
                }
                source.HeldReferences.Add(reference); return reference;
            });
            // Source.Append may defer encoding of an input until its completion arrives. Remove every
            // handle now, while the queued record still owns leases, so pending records hold only refs.
            if (ContainsShared(record.input)) record.input = JToken.Parse(EvidenceJson.EncodeBounded(record.input));
            if (ContainsShared(record.before)) record.before = JToken.Parse(EvidenceJson.EncodeBounded(record.before));
            if (ContainsShared(record.after)) record.after = JToken.Parse(EvidenceJson.EncodeBounded(record.after));
        }

        private string SavePayload(Source source, string folder, string payload)
        {
            using var saving = EvidenceServiceTiming.Measure(EvidenceServiceStage.Payload);
            byte[] raw;
            using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadUtf8)) raw = Encoding.UTF8.GetBytes(payload);
            string relative;
            using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadHash)) relative = folder + "/" + EvidenceJson.Hash(raw) + ".json.gz";
            string path = Path.Combine(source.Directory, relative);
            string key = path.Substring(Root.Length + 1).Replace('\\', '/');
            if (durableFiles.ContainsKey(key)) { source.HeldReferences.Add(relative); return relative; }
            using (EvidenceServiceTiming.Measure(EvidenceServiceStage.DirectoryCreate)) Directory.CreateDirectory(Path.Combine(source.Directory, folder));
            if (!File.Exists(path))
            {
                byte[] compressed;
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadCompress)) compressed = EvidenceJson.Compress(raw, Metrics);
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadReserve)) Reserve(source.Directory, compressed.Length);
                FileStream opened;
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadOpen)) opened = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var blob = opened;
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadWrite)) blob.Write(compressed, 0, compressed.Length);
                using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadFlush)) blob.Flush(true);
            }
            using (EvidenceServiceTiming.Measure(EvidenceServiceStage.PayloadIndex)) IndexFile(path);
            source.HeldReferences.Add(relative); return relative;
        }

        private void ShareKnockbackSettings(Source source, JToken value)
        {
            if (value is not JContainer container) return;
            foreach (var property in container.Descendants().OfType<JProperty>().Where(p => p.Name == "KnockbackSettings").ToArray())
            {
                if (property.Value is not JObject settings || settings["CurveKeys"] is not JArray keys || keys.Count == 0) continue;
                string reference = SavePayload(source, "inputs", EvidenceJson.EncodeBounded(settings));
                property.Value = new JObject { ["$evidenceRef"] = reference };
            }
        }

        private static void AddInlineReferences(object value, HashSet<string> references)
        {
            if (value == null) return;
            JToken token = value as JToken ?? JToken.FromObject(value);
            if (token is JObject root && root.Count == 1 && root["$evidenceRef"] != null) references.Add((string)root["$evidenceRef"]);
            if (token is JContainer container)
                foreach (var child in container.Descendants().OfType<JObject>())
                    if (child.Count == 1 && child["$evidenceRef"] != null) references.Add((string)child["$evidenceRef"]);
        }

        private static void ExpandReferences(string source, HashSet<string> references)
        {
            var pending = new Queue<string>(references); var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                string relative = pending.Dequeue(); if (!visited.Add(relative)) continue;
                if (visited.Count > 65536) throw new InvalidDataException("SharedInputDependencyLimit");
                string path = Path.GetFullPath(Path.Combine(source, relative));
                if (!path.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new InvalidDataException("MissingSharedInputDependency");
                byte[] raw = EvidenceJson.Decompress(File.ReadAllBytes(path), 16 << 20);
                if (Path.GetFileName(path) != EvidenceJson.Hash(raw) + ".json.gz") throw new InvalidDataException("SharedInputHashMismatch");
                var nested = new HashSet<string>(StringComparer.Ordinal);
                AddInlineReferences(JToken.Parse(Encoding.UTF8.GetString(raw)), nested);
                foreach (string child in nested) if (references.Add(child)) pending.Enqueue(child);
            }
        }
    }
}
