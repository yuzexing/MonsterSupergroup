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
        private string SavePayload(Source source, string folder, string payload)
        {
            byte[] raw = Encoding.UTF8.GetBytes(payload);
            string relative = folder + "/" + EvidenceJson.Hash(raw) + ".json.gz";
            string path = Path.Combine(source.Directory, relative);
            string key = path.Substring(Root.Length + 1).Replace('\\', '/');
            if (durableFiles.ContainsKey(key)) { source.HeldReferences.Add(relative); return relative; }
            Directory.CreateDirectory(Path.Combine(source.Directory, folder));
            if (!File.Exists(path))
            {
                byte[] compressed = EvidenceJson.Compress(raw); Reserve(source.Directory, compressed.Length);
                using var blob = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                blob.Write(compressed, 0, compressed.Length); blob.Flush(true);
            }
            IndexFile(path); source.HeldReferences.Add(relative); return relative;
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
