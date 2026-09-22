using System;
using System.IO;
using MonsterSupergroup.NetworkCombat.Editor;
using MonsterSupergroup.Builds;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SteamAppIdBuildTests
    {
        private string outputDirectory;

        [SetUp]
        public void SetUp()
        {
            outputDirectory = Path.Combine(Path.GetTempPath(), "MonsterSupergroupSteamBuild-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(outputDirectory, true);
        }

        [TestCase("Steam", "Direct", true, true)]
        [TestCase("Steam", "Direct", false, true)]
        [TestCase("Steam", "Steam", true, false)]
        [TestCase("Steam", "Steam", false, false)]
        [TestCase("Kcp", "Direct", true, false)]
        [TestCase("Kcp", "Direct", false, false)]
        public void ReusedOutput_AppIdFollowsDistributionAndNetworkNotDevelopment(string network, string distribution, bool development, bool expected)
        {
            string appIdPath = Path.Combine(outputDirectory, "steam_appid.txt");
            File.WriteAllText(appIdPath, "480");

            SteamAppIdBuildPostprocessor.ConfigureAppIdFile(
                BuildTarget.StandaloneWindows64,
                Path.Combine(outputDirectory, "Monster Supergroup.exe"),
                Info(network, distribution, development));

            if (expected)
                Assert.That(File.ReadAllText(appIdPath).Trim(), Is.EqualTo("4886160"));
            else
                Assert.That(File.Exists(appIdPath), Is.False);
        }

        [Test]
        public void CleanSteamOutput_DoesNotCreateAppIdOverride()
        {
            SteamAppIdBuildPostprocessor.ConfigureAppIdFile(
                BuildTarget.StandaloneWindows64,
                Path.Combine(outputDirectory, "Monster Supergroup.exe"),
                Info("Steam", "Steam", false));

            Assert.That(Directory.GetFiles(outputDirectory), Is.Empty);
        }

        [Test]
        public void ProjectRootOutput_RejectsBuildWithoutDeletingEditorAppId()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = Path.Combine(projectRoot, "steam_appid.txt");
            string original = File.ReadAllText(source);

            Assert.Throws<BuildFailedException>(() => SteamAppIdBuildPostprocessor.ConfigureAppIdFile(
                BuildTarget.StandaloneWindows64,
                Path.Combine(projectRoot, "Monster Supergroup.exe"),
                Info("Steam", "Steam", false)));

            Assert.That(File.ReadAllText(source), Is.EqualTo(original));
        }

        [Test]
        public void MissingSnapshotCannotInferDistributionFromDevelopment()
        {
            Assert.Throws<BuildFailedException>(() => SteamAppIdBuildPostprocessor.ConfigureAppIdFile(
                BuildTarget.StandaloneWindows64, Path.Combine(outputDirectory, "Monster Supergroup.exe"), null));
        }

        private static BuildInfo Info(string network, string distribution, bool development)
        {
            var info = new BuildInfo("0.0.0", BuildKind.Test, "fixture", new string('a', 40), true, false,
                "2026-09-22T08:30:00Z", "6000.3.21f1", "StandaloneWindows64", "x86_64", development, "product");
            info.SetConfiguration("product", network, distribution, "Normal", false, false, false);
            return info;
        }
    }
}
