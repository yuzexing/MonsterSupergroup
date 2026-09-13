using System;
using System.IO;
using MonsterSupergroup.NetworkCombat.Editor;
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

        [TestCase(true)]
        [TestCase(false)]
        public void ReusedOutput_ReplacesDevelopmentAppIdOrRemovesItForSteam(bool developmentBuild)
        {
            string appIdPath = Path.Combine(outputDirectory, "steam_appid.txt");
            File.WriteAllText(appIdPath, "480");

            SteamAppIdBuildPostprocessor.ConfigureAppIdFile(
                BuildTarget.StandaloneWindows64,
                Path.Combine(outputDirectory, "Monster Supergroup.exe"),
                developmentBuild);

            if (developmentBuild)
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
                false);

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
                false));

            Assert.That(File.ReadAllText(source), Is.EqualTo(original));
        }
    }
}
