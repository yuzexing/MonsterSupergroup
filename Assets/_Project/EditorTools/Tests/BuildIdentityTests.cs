using System;
using System.IO;
using MonsterSupergroup.Builds;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace MonsterSupergroup.EditorTools.Tests
{
    public sealed class BuildIdentityTests
    {
        [TestCase(VersionUpdate.Patch, "1.2.4")]
        [TestCase(VersionUpdate.Minor, "1.3.0")]
        [TestCase(VersionUpdate.Major, "2.0.0")]
        public void PreviewDoesNotChangePlayerVersion(VersionUpdate update, string expected)
        {
            string before = PlayerSettings.bundleVersion;
            Assert.That(ProjectBuildWindow.PreviewVersion("1.2.3", update), Is.EqualTo(expected));
            Assert.That(PlayerSettings.bundleVersion, Is.EqualTo(before));
        }
        [Test] public void ApplyUsesLiveVersionAndIsIndependentOfBuild()
        {
            string before = PlayerSettings.bundleVersion;
            try
            {
                PlayerSettings.bundleVersion = "1.2.3";
                ProjectBuildWindow.ApplyVersion("1.2.3", VersionUpdate.Minor);
                Assert.That(PlayerSettings.bundleVersion, Is.EqualTo("1.3.0"));
                Assert.Throws<InvalidOperationException>(() => ProjectBuildWindow.ApplyVersion("1.2.3", VersionUpdate.Patch));
                Assert.That(PlayerSettings.bundleVersion, Is.EqualTo("1.3.0"));
            }
            finally { PlayerSettings.bundleVersion = before; AssetDatabase.SaveAssets(); }
        }
        [TestCase(null)] [TestCase("")] [TestCase("1.0")] [TestCase("1.2.3-dev")]
        [TestCase("01.2.3")] [TestCase("-1.2.3")] [TestCase("1.2.2147483648")]
        public void InvalidVersionsAreRejected(string value) => Assert.That(GameVersion.TryParse(value, out _), Is.False);
        [Test] public void OverflowIsNotWrapped() => Assert.Throws<OverflowException>(() => GameVersion.Parse("1.2.2147483647").Increment(VersionUpdate.Patch));
        [Test] public void BuildIdsAreUniqueAndDoNotAdvanceGameVersion()
        {
            string version = PlayerSettings.bundleVersion;
            Assert.That(ProjectBuildIdentity.NewId(), Is.Not.EqualTo(ProjectBuildIdentity.NewId()));
            Assert.That(PlayerSettings.bundleVersion, Is.EqualTo(version));
        }
        [TestCase(BuildKind.Dev, true, "dev")]
        [TestCase(BuildKind.Test, false, "test")]
        [TestCase(BuildKind.Shipping, false, "shipping")]
        public void SnapshotRetainsIdentityAfterEditorConfigurationChanges(BuildKind kind, bool development, string suffix)
        {
            var info = Info(kind, development); string json = info.ToJson();
            var copy = BuildInfo.FromJson(json);
            Assert.That(copy.Display, Is.EqualTo("v1.2.3-" + suffix + " · fixed-id"));
            Assert.That(copy.Validate("1.2.3", development, kind), Is.Null);
            Assert.That(copy.Validate("2.0.0", development, kind), Is.Not.Null);
            Assert.That(copy.ToJson(), Is.EqualTo(json));
            Assert.That(copy.ArtifactName, Does.Contain("v1.2.3-" + suffix + "-fixed-id"));
        }
        [Test] public void ShippingRejectsDevelopmentTestsAndValidationSymbols()
        {
            Assert.Throws<BuildFailedException>(() => ProjectBuildIdentity.ValidateOptions(BuildKind.Shipping, true, false, Array.Empty<string>()));
            Assert.Throws<BuildFailedException>(() => ProjectBuildIdentity.ValidateOptions(BuildKind.Shipping, false, true, Array.Empty<string>()));
            Assert.Throws<BuildFailedException>(() => ProjectBuildIdentity.ValidateOptions(BuildKind.Shipping, false, false, new[] { "MONSTER_MENU_VALIDATION" }));
            Assert.DoesNotThrow(() => ProjectBuildIdentity.ValidateOptions(BuildKind.Shipping, false, false, new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }));
        }
        [Test] public void BuildKindDoesNotChooseTransport()
        {
            var profile = new ProjectBuildProfile { id = "kcp", kind = "Test", development = true, defines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" } };
            Assert.That(ProjectBuildIdentity.ResolveKind(profile), Is.EqualTo(BuildKind.Test));
            Assert.That(ProjectBuildIdentity.ResolveKind(profile, "Shipping"), Is.EqualTo(BuildKind.Shipping));
            Assert.That(profile.defines, Is.EqualTo(new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }));
        }
        [TestCase(BuildKind.Dev)] [TestCase(BuildKind.Test)] [TestCase(BuildKind.Shipping)]
        public void ExternalBuildKindDefinesCannotOverrideTheSelectedKind(BuildKind kind) =>
            Assert.Throws<BuildFailedException>(() => ProjectBuildIdentity.ValidateOptions(kind, false, false, new[] { "MONSTER_BUILD_SHIPPING" }));
        [Test] public void ScriptsOnlyFailsBeforeAnyBuildOutput() => Assert.Throws<BuildFailedException>(() => ProjectBuildService.Build("player-development", scriptsOnly: true));
        [Test] public void FailedOverwriteCannotRetainSuccessfulMetadata()
        {
            string dir = Path.Combine("Temp", "BuildIdentity-" + Guid.NewGuid().ToString("N"));
            string exe = Path.Combine(dir, "Game.exe"), path = ProjectBuildIdentity.InfoPath(exe);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "old");
            File.WriteAllText(Path.Combine(dir, "build-complete.json"), "old");
            try
            {
                ProjectBuildIdentity.Begin(Info(BuildKind.Dev, true), exe);
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(Path.Combine(dir, "build-complete.json")), Is.False);
            }
            finally { ProjectBuildIdentity.End(); Directory.Delete(dir, true); }
        }
        private static BuildInfo Info(BuildKind kind, bool development) => new("1.2.3", kind, "fixed-id", new string('a', 40), true, false,
            "2026-09-22T08:30:00Z", "6000.3.21f1", "StandaloneWindows64", "x86_64", development, "fixture");
    }
}
