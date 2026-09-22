using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using MonsterSupergroup.EditorTools;
using MonsterSupergroup.Builds;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Archives the implementation used by the build, including local edits, for later replay against that implementation.</summary>
    public sealed class CombatEvidenceBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string ResourcePath = "Assets/_Project/NetworkCombat/Resources/CombatEvidenceBuild.json";
        private static string pendingManifest, archivePath;
        public int callbackOrder => -100;
        public static bool ShouldCapture(BuildInfo info) => info != null && info.Evidence && info.Diagnostics == "Evidence" &&
            !string.Equals(info.Kind, "shipping", StringComparison.OrdinalIgnoreCase);
        public void OnPreprocessBuild(BuildReport report)
        {
            // A previous interrupted build must not leak its Resources manifest into a normal package.
            Cleanup();
            string output = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
            string oldManifest = Path.Combine(output, "combat-build.json");
            if (File.Exists(oldManifest)) File.Delete(oldManifest);
            if (!ShouldCapture(ProjectBuildIdentity.Active)) return;
            ProjectBuildIdentity.BuildFinished -= Cleanup;
            ProjectBuildIdentity.BuildFinished += Cleanup;
            string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var entries = new List<object>();
            Directory.CreateDirectory(output);
            string archiveDirectory = Path.Combine(root, "Logs", "CombatEvidenceBuilds", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(archiveDirectory);
            archivePath = Path.Combine(archiveDirectory, "combat-replay-sources.zip");
            if (File.Exists(archivePath + ".tmp")) File.Delete(archivePath + ".tmp");
            using (var archive = ZipFile.Open(archivePath + ".tmp", ZipArchiveMode.Create))
                foreach (string directory in new[] { "Assets/_Project", "Assets/Mirror/Transports/FizzySteamworks", "ProjectSettings", "Packages" })
                    foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                    {
                        string relative = path.Substring(root.Length + 1).Replace('\\', '/');
                        string extension = Path.GetExtension(path);
                        if (relative == ResourcePath || !(extension == ".cs" || extension == ".asmdef" || extension == ".json" || extension == ".asset")) continue;
                        using var stream = File.OpenRead(path); using var sha = SHA256.Create();
                        string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                        entries.Add(new { path = relative, sha256 = hash });
                        archive.CreateEntryFromFile(path, relative, CompressionLevel.Fastest);
                    }
            if (File.Exists(archivePath)) File.Replace(archivePath + ".tmp", archivePath, null); else File.Move(archivePath + ".tmp", archivePath);
            string files = EvidenceJson.Encode(entries);
            pendingManifest = EvidenceJson.Encode(new { version = 1, buildId = ProjectBuildIdentity.Active.BuildId, sourceConfigurationHash = EvidenceJson.Hash(Encoding.UTF8.GetBytes(files)),
                protocol = SteamLobbyMetadata.ProtocolValue, logFormat = 2, replicationProtocol = DiagnosticReplicator.Version, replayFormat = 2,
                unity = UnityEngine.Application.unityVersion, buildUtc = DateTime.UtcNow.ToString("o"), files = entries,
                gameplayDependencyHash = AssetDatabase.GetAssetDependencyHash("Assets/_Project/Scenes/Gameplay.unity").ToString(),
                bootDependencyHash = AssetDatabase.GetAssetDependencyHash("Assets/_Project/Scenes/Boot.unity").ToString() });
            Directory.CreateDirectory(Path.GetDirectoryName(ResourcePath)); File.WriteAllText(ResourcePath, pendingManifest, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ResourcePath, ImportAssetOptions.ForceSynchronousImport);
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            if (pendingManifest == null || archivePath == null) { Cleanup(); return; }
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath)), "combat-build.json");
            string metadata = EvidenceJson.Encode(new { buildGuid = report.summary.guid.ToString(), buildId = ProjectBuildIdentity.Active.BuildId, manifest = pendingManifest,
                replaySources = "Logs/CombatEvidenceBuilds/" + Path.GetFileName(Path.GetDirectoryName(archivePath)) + "/" + Path.GetFileName(archivePath),
                result = report.summary.result.ToString() });
            EvidenceJson.AtomicWrite(path, metadata);
            EvidenceJson.AtomicWrite(Path.Combine(Path.GetDirectoryName(archivePath), "combat-build.json"), metadata);
            Cleanup();
        }
        private static void Cleanup()
        {
            ProjectBuildIdentity.BuildFinished -= Cleanup;
            if (File.Exists(ResourcePath) || File.Exists(ResourcePath + ".meta")) AssetDatabase.DeleteAsset(ResourcePath);
            if (archivePath != null && File.Exists(archivePath + ".tmp")) File.Delete(archivePath + ".tmp");
            pendingManifest = null; archivePath = null;
        }
    }
}
