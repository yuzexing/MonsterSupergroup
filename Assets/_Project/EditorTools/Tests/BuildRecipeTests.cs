using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.Builds;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace MonsterSupergroup.EditorTools.Tests
{
    public sealed class BuildRecipeTests
    {
        private MonsterBuildSettings settings;
        [SetUp] public void SetUp() => settings = ScriptableObject.CreateInstance<MonsterBuildSettings>();
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(settings);
        private void Validate(bool development = false) => ProjectBuildResolver.ValidateBusiness(settings, development, ProjectBuildPurposes.Get(settings.PurposeId).scenes);
        [Test] public void ProductTestHasNoToolsOrTestAssemblies()
        {
            Validate(); Assert.That(ProjectBuildDefines.Expected(settings), Is.EqualTo(new[] { "MONSTER_BUILD_TEST" }));
            Assert.That(ProjectBuildPurposes.Get(settings.PurposeId).testAssemblies, Is.False);
            Validate(true); Assert.That(ProjectBuildDefines.Expected(settings), Does.Not.Contain("MONSTER_BUILD_TOOLS"));
        }
        [Test] public void EvidenceIsOnlyProductTest()
        {
            settings.Diagnostics = BuildDiagnostics.Evidence; Validate();
            Assert.That(ProjectBuildDefines.Expected(settings), Does.Contain("MONSTER_COMBAT_EVIDENCE"));
            settings.BuildKind = BuildKind.Dev; Assert.Throws<BuildFailedException>(() => Validate(true));
            settings.BuildKind = BuildKind.Test; settings.PurposeId = "wisp-validation"; Assert.Throws<BuildFailedException>(() => Validate());
        }
        [TestCase("gameplay-validation")][TestCase("wisp-validation")][TestCase("options-validation")]
        [TestCase("handoff-validation")][TestCase("sandbox")][TestCase("nordic")]
        public void SpecialTestRetainsToolsAndCannotShip(string purpose)
        {
            settings.PurposeId = purpose; settings.Network = BuildNetwork.Kcp; settings.Distribution = BuildDistribution.Direct;
            Validate(); Assert.That(ProjectBuildDefines.Expected(settings), Does.Contain("MONSTER_BUILD_TOOLS"));
            Assert.That(ProjectBuildPurposes.Get(purpose).testAssemblies, Is.EqualTo(purpose != "sandbox" && purpose != "nordic"));
            settings.BuildKind = BuildKind.Shipping; Assert.Throws<BuildFailedException>(() => Validate());
        }
        [Test] public void ShippingAndKcpRejectUnsafeCombinations()
        {
            settings.BuildKind = BuildKind.Shipping; Validate(); Assert.Throws<BuildFailedException>(() => Validate(true));
            settings.Distribution = BuildDistribution.Direct; Assert.Throws<BuildFailedException>(() => Validate());
            settings.Network = BuildNetwork.Kcp; settings.BuildKind = BuildKind.Test; Assert.Throws<BuildFailedException>(() => Validate());
            settings.BuildKind = BuildKind.Dev; Validate(true);
            settings.Distribution = BuildDistribution.Steam; Assert.Throws<BuildFailedException>(() => Validate(true));
        }
        [Test] public void WrongScenesOrSchemaCannotBypassPurposeRules()
        {
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.ValidateBusiness(settings, false, new[] { "Assets/Other.unity" }));
            settings.SchemaVersion = 99; Assert.Throws<BuildFailedException>(() => Validate());
        }
        [Test] public void ApplyingSymbolsRemovesOldGrantsAndPreservesUnrelatedSymbols()
        {
            string[] current = { "MONSTER_BUILD_DEV", "MONSTER_BUILD_TOOLS", "MONSTER_MENU_VALIDATION", "ODIN_INSPECTOR", "MY_VALIDATION" };
            string[] result = ProjectBuildDefines.Applied(current, ProjectBuildDefines.Expected(settings));
            Assert.That(result, Is.EquivalentTo(new[] { "MONSTER_BUILD_TEST", "ODIN_INSPECTOR", "MY_VALIDATION" }));
            Assert.That(ProjectBuildDefines.Difference(result, ProjectBuildDefines.Expected(settings)), Is.Empty);
            Assert.That(ProjectBuildDefines.Difference(current, ProjectBuildDefines.Expected(settings)), Does.Contain("MONSTER_BUILD_TEST"));
            Assert.That(ProjectBuildDefines.Difference(new[] { "MONSTER_BUILD_TEST", "UNITY_INCLUDE_TESTS" }, ProjectBuildDefines.Expected(settings)), Does.Contain("UNITY_INCLUDE_TESTS"));
        }
        [Test] public void AllTemplatesPersistBusinessSettingsAndReadWithoutMutation()
        {
            var profiles = AssetDatabase.FindAssets("t:BuildProfile", new[] { ProjectBuildTemplates.Root });
            Assert.That(profiles.Length, Is.EqualTo(11));
            var active = BuildProfile.GetActiveBuildProfile();
            foreach (string guid in profiles)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var bytes = File.ReadAllBytes(path);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                Assert.That(profile.GetComponent<MonsterBuildSettings>(), Is.Not.Null);
                var plan = ProjectBuildResolver.Resolve(profile); ProjectBuildResolver.ValidateDefines(plan);
                Assert.That(plan.ProfileGuid, Is.EqualTo(guid));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                Assert.That(BuildProfile.GetActiveBuildProfile(), Is.EqualTo(active));
            }
        }
        [Test] public void NativeOptionsAndExecutionAreValidatedIndependently()
        {
            var plan = ProjectBuildResolver.Resolve(ProjectBuildResolver.Load(ProjectBuildTemplates.Root + "/Windows-Test-Steam.asset"));
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.ValidateExecution(plan, new BuildExecutionRequest { runAfterBuild = true }));
            Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.ValidateExecution(plan, new BuildExecutionRequest { expectedContentHash = "stale" }));
            Assert.DoesNotThrow(() => ProjectBuildResolver.ValidateExecution(plan, new BuildExecutionRequest { output = "Builds/Custom.exe", cleanBuildCache = true }));
            Assert.Throws<BuildFailedException>(() => NativeBuildEntry.ValidateOptions(plan.ExpectedOptions | BuildOptions.Development, plan, false));
        }
        [Test] public void LegacyCallsCannotBuildOrReadOldPointers()
        {
            foreach (string id in ProjectToolCatalog.Load().buildAliases.Select(a => a.id)) Assert.Throws<BuildFailedException>(() => ProjectBuildResolver.Resolve(id));
            Assert.Throws<BuildFailedException>(() => ProjectBuildService.Build("product"));
            Assert.Throws<ArgumentException>(() => ProjectBuildResults.PathFor("product"));
        }
        [Test] public void PointerInvalidationIsProfileSpecific()
        {
            string id = Guid.NewGuid().ToString("N"), path = ProjectBuildResults.PathFor(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "old success");
            try { ProjectBuildResults.Invalidate(id); Assert.That(File.Exists(path), Is.False); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
