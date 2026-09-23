using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MonsterSupergroup.EditorTools.Tests
{
    public sealed class ProjectToolTests
    {
        [Test] public void AllCatalogEntriesResolveAndAllProfilesHaveExistingScenes() => ProjectToolCatalog.Validate();

        [Test] public void MaintenanceCannotWriteWithoutApply()
        {
            const string registry = "Assets/_Project/GAS/Core/Generated/GeneratedModifierRegistry.g.cs";
            string before = File.ReadAllText(registry);
            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("InvalidOperationException:.*-Apply"));
            var result = ProjectToolRunner.Run("generate.gas-registry");
            Assert.That(result.success, Is.False);
            Assert.That(result.error, Does.Contain("-Apply"));
            Assert.That(File.ReadAllText(registry), Is.EqualTo(before));
            Assert.That(File.Exists(result.report), Is.True);
        }

        [Test] public void MissingSceneFailsBeforeBuildAndDoesNotCreateAsset()
        {
            const string missing = "Assets/_Project/Scenes/ProjectToolMissingScene.unity";
            Assert.That(File.Exists(missing), Is.False);
            Assert.Throws<FileNotFoundException>(() => ProjectBuildService.ValidateProfile(new ProjectBuildProfile {
                id = "missing", scenes = new[] { missing }, output = "Builds/unused.exe"
            }));
            Assert.That(File.Exists(missing), Is.False);
        }

        [Test] public void FormalBuildsIncludeMenuWhileSamplesKeepTheirOwnScenes()
        {
            foreach (var profile in ProjectToolCatalog.Load().builds.Where(b => b.id != "sandbox" && b.id != "nordic"))
                Assert.That(profile.scenes.Select(Path.GetFileNameWithoutExtension), Is.EqualTo(new[] { "Boot", "MainMenu", "Gameplay" }), profile.id);
            Assert.That(ProjectToolCatalog.Load().builds.Single(b => b.id == "sandbox").scenes.Length, Is.EqualTo(1));
        }

        [Test] public void PlayerDeliveryProfilesDoNotContainValidationCode()
        {
            foreach (string id in new[] { "Windows-Dev-Kcp", "Windows-Shipping" })
            {
                var build = ProjectBuildResolver.Resolve(ProjectBuildResolver.Load(ProjectBuildTemplates.Root + "/" + id + ".asset"));
                Assert.That(build.Recipe.id, Is.EqualTo("product"), id);
                Assert.That(build.Recipe.testAssemblies, Is.False, id);
                Assert.That(build.Recipe.defines, Is.Empty, id);
                Assert.That(build.Development, Is.EqualTo(id == "Windows-Dev-Kcp"));
            }
        }

        [Test] public void UnattendedBatchNeverRunsInteractiveDiagnostics()
        {
            if (!UnityEngine.Application.isBatchMode) Assert.Ignore("Batch-specific contract");
            Assert.That(ProjectToolRunner.Availability(ProjectToolCatalog.Find("diagnostic.steam")), Does.Contain("Editor"));
        }

        [Test] public void ReadOnlyRunnerCannotInvokeMaintenance()
        {
            Assert.Throws<InvalidOperationException>(() => ProjectToolRunner.InvokeReadOnly("rebuild.perks"));
        }

        [Test] public void LocalizationValidationProducesACompletedReadOnlyResult()
        {
            var result = ProjectToolRunner.Run("validate.localization");
            Assert.That(result.success, Is.True, result.error);
            Assert.That(result.pending, Is.False);
            Assert.That(File.ReadAllText(result.report), Does.Contain("\"success\": true"));
        }
    }
}
