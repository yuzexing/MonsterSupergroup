using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.Builds;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEngine;

namespace MonsterSupergroup.EditorTools.Tests
{
    public sealed class BuildRecipeTests
    {
        [Test] public void DailyDefaultIsAnUnaidedSteamTestProduct()
        {
            var b = ProjectBuildResolver.Resolve();
            Assert.That(b.Kind, Is.EqualTo(BuildKind.Test));
            Assert.That(b.Recipe.id, Is.EqualTo("product"));
            Assert.That(b.Network, Is.EqualTo(BuildNetwork.Steam));
            Assert.That(b.Distribution, Is.EqualTo(BuildDistribution.Steam));
            Assert.That(b.Development || b.Tools || b.Evidence || b.Recipe.testAssemblies, Is.False);
            Assert.That(b.Defines, Is.EqualTo(new[] { "MONSTER_BUILD_TEST" }));
        }

        [Test] public void All36LegacyIdsResolveWithoutDuplicateRecipeDefinitions()
        {
            var catalog = ProjectToolCatalog.Load();
            Assert.That(catalog.builds.Length, Is.EqualTo(7));
            Assert.That(catalog.buildAliases.Length, Is.EqualTo(34));
            foreach (string id in catalog.buildAliases.Select(a => a.id).Concat(new[] { "sandbox", "nordic" }))
            {
                var b = ProjectBuildResolver.Resolve(id);
                Assert.That(b.Recipe.scenes, Is.Not.Empty, id);
                if (id.EndsWith("release") && id != "player-release") Assert.That(b.Kind, Is.EqualTo(BuildKind.Test), id);
            }
        }
        [TestCase("menu-development", "gameplay-validation", BuildNetwork.Steam)]
        [TestCase("imp", "gameplay-validation", BuildNetwork.Kcp)]
        [TestCase("boot-process", "product", BuildNetwork.Steam)]
        [TestCase("wisp", "wisp-validation", BuildNetwork.Kcp)]
        [TestCase("options", "options-validation", BuildNetwork.Kcp)]
        [TestCase("enemy-handoff-release", "handoff-validation", BuildNetwork.Kcp)]
        public void LegacyAliasesRetainNecessaryNetworkAndHooks(string id, string recipe, BuildNetwork network)
        {
            var b = ProjectBuildResolver.Resolve(id);
            Assert.That(b.Recipe.id, Is.EqualTo(recipe)); Assert.That(b.Network, Is.EqualTo(network));
        }
        [Test] public void GenericValidationRetainsNordicCheckAndSpecialRecipesRetainSymbols()
        {
            Assert.That(ProjectBuildResolver.Resolve("gameplay-validation").Recipe.validations, Does.Contain("validate.nordic-gameplay"));
            Assert.That(ProjectBuildResolver.Resolve("options-validation").Defines, Does.Contain("MONSTER_OPTIONS_VALIDATION"));
            Assert.That(ProjectBuildResolver.Resolve("handoff-validation").Defines, Does.Contain("MONSTER_ENEMY_HANDOFF_VALIDATION"));
        }
        [TestCase("true")][TestCase("false")]
        public void DevelopmentOverrideNeverGrantsProductTestTools(string development)
        {
            var b = ProjectBuildResolver.Resolve("product", "Test", development);
            Assert.That(b.Tools, Is.False); Assert.That(b.Defines, Does.Not.Contain("MONSTER_BUILD_TOOLS"));
            Assert.That(b.Development, Is.EqualTo(bool.Parse(development)));
        }
        [Test] public void EvidenceHasIndependentCompiledCapabilityAndNoTestAssembly()
        {
            var b = ProjectBuildResolver.Resolve("steam-evidence");
            Assert.That(b.Evidence, Is.True); Assert.That(b.Tools || b.Recipe.testAssemblies, Is.False);
            Assert.That(b.Defines, Does.Contain("MONSTER_BUILD_EVIDENCE"));
            Assert.That(b.Defines, Does.Contain("MONSTER_COMBAT_EVIDENCE"));
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Dev", diagnostics: "Evidence"));
        }
        [TestCase("gameplay-validation")][TestCase("wisp-validation")][TestCase("options-validation")]
        [TestCase("handoff-validation")][TestCase("sandbox")][TestCase("nordic")]
        public void ShippingRejectsEverySpecialRecipe(string id) =>
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve(id, "Shipping"));
        [Test] public void ShippingRejectsUnsafeProductCombinations()
        {
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Shipping", "true"));
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Shipping", network: "Kcp"));
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Shipping", distribution: "Direct"));
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Shipping", diagnostics: "Evidence"));
            var b = ProjectBuildResolver.Resolve("player-release");
            Assert.That(b.Kind, Is.EqualTo(BuildKind.Shipping)); Assert.That(b.Tools || b.Evidence || b.Development, Is.False);
        }
        [Test] public void ShippingDoesNotTrustAProductLabelOnASandbox()
        {
            var catalog = ProjectToolCatalog.Load();
            catalog.builds.Single(b => b.id == "product").scenes = new[] { "Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity" };
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Shipping", catalog: catalog));
        }
        [TestCase("MONSTER_MENU_VALIDATION")][TestCase("MONSTER_COMBAT_EVIDENCE")]
        [TestCase("MONSTER_BUILD_TOOLS")][TestCase("MONSTER_KCP_DEVELOPMENT_BUILD")]
        public void GlobalSymbolsCannotLeakIntoNormalProduct(string define) =>
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.ValidateProjectDefines(ProjectBuildResolver.Resolve(), new[] { define }));
        [Test] public void KcpDistributionCannotPretendToBeASteamDepot()
        {
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Dev", network: "Kcp", distribution: "Steam"));
            Assert.That(ProjectBuildResolver.Resolve("kcp-development").Distribution, Is.EqualTo(BuildDistribution.Direct));
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve("product", "Test", network: "Kcp"));
        }
        [Test] public void LegacyNonDevelopmentValidationRetainsTools()
        {
            var b = ProjectBuildResolver.Resolve("enemy-handoff-release");
            Assert.That(b.Development, Is.False); Assert.That(b.Tools && b.Recipe.testAssemblies, Is.True);
        }
        [Test] public void PointerInvalidationDoesNotDeleteOldFrozenPackage()
        {
            string id = "test-" + Guid.NewGuid().ToString("N"), path = ProjectBuildResults.PathFor(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "old pointer");
            try { ProjectBuildResults.Invalidate(id); Assert.That(File.Exists(path), Is.False); }
            finally { if (File.Exists(path)) File.Delete(path); }
            Assert.Throws<ArgumentException>(() => ProjectBuildResults.PathFor("../outside"));
        }
        [Test] public void RejectedBuildAttemptInvalidatesItsEarlierSuccess()
        {
            string[] paths = { ProjectBuildResults.PathFor("sandbox") };
            string backup = File.Exists(paths[0]) ? File.ReadAllText(paths[0]) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(paths[0])); File.WriteAllText(paths[0], "previous success");
            try
            {
                Assert.Throws<BuildFailedException>(() => ProjectBuildService.Build("sandbox", buildKind: "Shipping"));
                Assert.That(File.Exists(paths[0]), Is.False);
                Assert.That(ProjectBuildIdentity.LastInfoPath, Is.Null);
            }
            finally { if (backup != null) File.WriteAllText(paths[0], backup); else if (File.Exists(paths[0])) File.Delete(paths[0]); }
        }
        [Test] public void FailedPointerPublicationDoesNotLeavePartialSuccess()
        {
            var catalog = ProjectToolCatalog.Load();
            var alias = catalog.buildAliases.First(a => a.id == "imp");
            string aliasId = "test-" + Guid.NewGuid().ToString("N"), recipeId = "test-" + Guid.NewGuid().ToString("N");
            alias.id = aliasId; alias.recipe = recipeId;
            catalog.builds.Single(b => b.id == "gameplay-validation").id = recipeId;
            var build = ProjectBuildResolver.Resolve(aliasId, catalog: catalog);
            string blocked = ProjectBuildResults.PathFor(recipeId);
            Directory.CreateDirectory(blocked);
            try
            {
                var info = new BuildInfo("0.0.0", BuildKind.Dev, "id", "", false, true, DateTime.UtcNow.ToString("O"), "Unity", "Windows", "x64", true, recipeId);
                Assert.Throws<IOException>(() => ProjectBuildResults.Save(build, "Temp/unused.exe", info));
                Assert.That(File.Exists(ProjectBuildResults.PathFor(aliasId)), Is.False);
                Assert.That(File.Exists(ProjectBuildResults.PathFor(aliasId) + ".tmp"), Is.False);
                Assert.That(File.Exists(blocked + ".tmp"), Is.False);
            }
            finally { Directory.Delete(blocked); ProjectBuildResults.Invalidate(aliasId, recipeId); }
        }
    }
}
