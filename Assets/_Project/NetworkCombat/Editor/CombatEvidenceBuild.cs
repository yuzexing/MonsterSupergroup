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
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Archives the implementation used by the build, including local edits, for later replay against that implementation.</summary>
    public sealed class CombatEvidenceBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string ResourcePath = "Assets/_Project/NetworkCombat/Resources/CombatEvidenceBuild.json";
        private const string CatalogPath = "Assets/_Project/NetworkCombat/Resources/CombatInvestigationCatalog.json";
        private static string pendingManifest, pendingCatalog, archivePath;
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
                        if (relative == ResourcePath || relative == CatalogPath || !(extension == ".cs" || extension == ".asmdef" || extension == ".json" || extension == ".asset")) continue;
                        using var stream = File.OpenRead(path); using var sha = SHA256.Create();
                        string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                        entries.Add(new { path = relative, sha256 = hash });
                        archive.CreateEntryFromFile(path, relative, CompressionLevel.Fastest);
                    }
            if (File.Exists(archivePath)) File.Replace(archivePath + ".tmp", archivePath, null); else File.Move(archivePath + ".tmp", archivePath);
            string files = EvidenceJson.Encode(entries);
            string sourceHash = EvidenceJson.Hash(Encoding.UTF8.GetBytes(files));
            pendingCatalog = CaptureNameCatalog(ProjectBuildIdentity.Active.BuildId, ProjectBuildService.ActivePlan.ContentHash, sourceHash);
            pendingManifest = EvidenceJson.Encode(new { version = 1, buildId = ProjectBuildIdentity.Active.BuildId, sourceConfigurationHash = sourceHash,
                investigationCatalog = new { resource = "CombatInvestigationCatalog", sha256 = EvidenceJson.Hash(Encoding.UTF8.GetBytes(pendingCatalog)) },
                protocol = SteamLobbyMetadata.ProtocolValue, logFormat = 2, replicationProtocol = DiagnosticReplicator.Version, replayFormat = 2,
                unity = UnityEngine.Application.unityVersion, buildUtc = DateTime.UtcNow.ToString("o"), files = entries,
                gameplayDependencyHash = AssetDatabase.GetAssetDependencyHash("Assets/_Project/Scenes/Gameplay.unity").ToString(),
                bootDependencyHash = AssetDatabase.GetAssetDependencyHash("Assets/_Project/Scenes/Boot.unity").ToString() });
            // The native Profile is outside Assets/_Project. Keep its actual configuration with the replay sources.
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var plan = ProjectBuildService.ActivePlan;
                archive.CreateEntryFromFile(plan.ProfilePath, plan.ProfilePath, CompressionLevel.Fastest);
                archive.CreateEntryFromFile(plan.ProfilePath + ".meta", plan.ProfilePath + ".meta", CompressionLevel.Fastest);
                using (var writer = new StreamWriter(archive.CreateEntry("build-plan.json").Open(), new UTF8Encoding(false))) writer.Write(plan.ToJson());
                using (var writer = new StreamWriter(archive.CreateEntry("combat-investigation-catalog.json").Open(), new UTF8Encoding(false))) writer.Write(pendingCatalog);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ResourcePath)); File.WriteAllText(ResourcePath, pendingManifest, new UTF8Encoding(false));
            File.WriteAllText(CatalogPath, pendingCatalog, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ResourcePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(CatalogPath, ImportAssetOptions.ForceSynchronousImport);
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
            if (File.Exists(CatalogPath) || File.Exists(CatalogPath + ".meta")) AssetDatabase.DeleteAsset(CatalogPath);
            if (archivePath != null && File.Exists(archivePath + ".tmp")) File.Delete(archivePath + ".tmp");
            pendingManifest = pendingCatalog = null; archivePath = null;
        }
        public static string CaptureNameCatalog(string buildId, string contentVersion, string sourceConfigurationHash)
        {
            var names = new List<object>();
            foreach (string kind in new[] { "WeaponData", "EquipmentData", "PerkData" })
                // Runtime databases also reference migrated content in Assets/MonoBehaviour.
                // The catalog follows content type across Assets, not its current folder layout.
                foreach (string path in AssetDatabase.FindAssets("t:" + kind, new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    uint id; string category; LocalizedString title;
                    if (asset is WeaponData weapon) { id = weapon.ID; title = weapon.LocalizedTitle; category = "weapon"; }
                    else if (asset is EquipmentData equipment) { id = equipment.ID; title = equipment.LocalizedTitle; category = "equipment"; }
                    else if (asset is PerkData perk) { id = perk.ID; title = perk.LocalizedTitle; category = "blessing"; }
                    else continue;
                    var translations = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    var collection = title != null && !title.IsEmpty ? LocalizationEditorSettings.GetStringTableCollection(title.TableReference) : null;
                    if (collection != null)
                        foreach (var locale in LocalizationEditorSettings.GetLocales().OrderBy(l => l.Identifier.Code, StringComparer.Ordinal))
                        {
                            var table = collection.GetTable(locale.Identifier) as StringTable;
                            var key = title.TableEntryReference;
                            var entry = key.ReferenceType == TableEntryReference.Type.Id ? table?.GetEntry(key.KeyId) : table?.GetEntry(key.Key);
                            if (entry != null && !string.IsNullOrWhiteSpace(entry.LocalizedValue)) translations.Add(locale.Identifier.Code, entry.LocalizedValue);
                        }
                    names.Add(new { kind = category, contentId = id, identityValid = id != 0, assetName = asset.name,
                        assetGuid = AssetDatabase.AssetPathToGUID(path), assetPath = path, names = translations,
                        nameStatus = translations.Count > 0 ? "Localized" : "MissingLocalization" });
                }
            // Duplicate IDs remain distinct entries with asset identities. A viewer must not choose one silently.
            return EvidenceJson.Encode(new { schemaVersion = 1, buildId, contentVersion, sourceConfigurationHash,
                identity = "kind/contentId; duplicate entries require asset identity", entries = names });
        }
    }
}
