using MonsterSupergroup.Builds;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class BuildPackagePolicyTests
    {
        [TestCase(BuildKind.Dev, false, false)]
        [TestCase(BuildKind.Dev, true, true)]
        [TestCase(BuildKind.Test, false, false)]
        [TestCase(BuildKind.Test, true, true)]
        [TestCase(BuildKind.Shipping, false, false)]
        [TestCase(BuildKind.Shipping, true, false)]
        public void RuntimeCapabilitiesRequireCompiledGrantAndNeverEnableShipping(BuildKind kind, bool compiled, bool expected)
        {
            Assert.That(BuildFeatures.CapabilityAllowed(kind, compiled, false), Is.EqualTo(expected));
            Assert.That(BuildFeatures.CapabilityAllowed(kind, compiled, true), Is.True, "Editor keeps its own explicit identity");
        }

        [TestCase("Steam", "Direct", "Normal", false, false, false)]
        [TestCase("Steam", "Steam", "Evidence", false, false, true)]
        [TestCase("Steam", "Direct", "Network", false, false, false)]
        [TestCase("Kcp", "Direct", "Normal", true, true, false)]
        public void SnapshotKeepsResolvedPackageConfiguration(string network, string distribution, string diagnostics, bool tests, bool tools, bool evidence)
        {
            var source = Info();
            source.SetConfiguration("gameplay-validation", network, distribution, diagnostics, tests, tools, evidence);
            var copy = BuildInfo.FromJson(source.ToJson());
            Assert.That(copy.Profile, Is.EqualTo("gameplay-validation"));
            Assert.That(copy.Network, Is.EqualTo(network));
            Assert.That(copy.Distribution, Is.EqualTo(distribution));
            Assert.That(copy.Diagnostics, Is.EqualTo(diagnostics));
            Assert.That(copy.TestAssemblies, Is.EqualTo(tests));
            Assert.That(copy.DevelopmentTools, Is.EqualTo(tools));
            Assert.That(copy.Evidence, Is.EqualTo(evidence));
            Assert.That(copy.Validate("0.0.0", false, BuildKind.Test), Is.Null);
            Assert.That(copy.ValidateCapabilities(tools, evidence, diagnostics == "Network"), Is.Null);
            Assert.That(copy.ValidateCapabilities(!tools, evidence, diagnostics == "Network"), Is.Not.Null);
            Assert.That(copy.ValidateCapabilities(tools, !evidence, diagnostics == "Network"), Is.Not.Null);
            Assert.That(copy.ValidateCapabilities(tools, evidence, diagnostics != "Network"), Is.Not.Null);
        }

        [TestCase("Normal", false, false)]
        [TestCase("Evidence", true, true)]
        [TestCase("Evidence", false, false)]
        [TestCase("Normal", true, false)]
        [TestCase("Network", false, false)]
        public void LargeReplayArchiveRequiresExplicitEvidenceConfiguration(string diagnostics, bool evidence, bool expected)
        {
            var info = Info();
            info.SetConfiguration("product", "Steam", "Direct", diagnostics, false, false, evidence);
            Assert.That(CombatEvidenceBuild.ShouldCapture(info), Is.EqualTo(expected));
            if (evidence != (diagnostics == "Evidence")) Assert.That(info.Validate("0.0.0", false, BuildKind.Test), Is.Not.Null);
        }

        [Test]
        public void ShippingNeverIncludesReplayArchiveOrCapabilities()
        {
            var info = Info(BuildKind.Shipping);
            info.SetConfiguration("product", "Steam", "Steam", "Evidence", false, false, true);
            Assert.That(CombatEvidenceBuild.ShouldCapture(info), Is.False);
            Assert.That(info.Validate("0.0.0", false, BuildKind.Shipping), Is.Not.Null);
            info.SetConfiguration("product", "Steam", "Steam", "Normal", false, false, false);
            Assert.That(info.Validate("0.0.0", false, BuildKind.Shipping), Is.Null);
            info.SetConfiguration("product", "Steam", "Steam", "Network", false, false, false);
            Assert.That(info.Validate("0.0.0", false, BuildKind.Shipping), Is.Not.Null);
            info.SetConfiguration("product", "Steam", "Steam", "Normal", false, true, false);
            Assert.That(info.Validate("0.0.0", false, BuildKind.Shipping), Is.Not.Null);
            info.SetConfiguration("product", "Steam", "Direct", "Normal", false, false, false);
            Assert.That(info.Validate("0.0.0", false, BuildKind.Shipping), Is.Not.Null);
        }
        [Test] public void MetadataCannotGrantAnotherCompiledCaptureMode()
        {
            var info = Info();
            info.SetConfiguration("product", "Steam", "Direct", "Network", false, false, false);
            Assert.That(info.ValidateCapabilities(false, false), Is.Not.Null);
            Assert.That(info.ValidateCapabilities(false, false, true), Is.Null);
            Assert.That(info.ValidateCapabilities(false, true, true), Is.Not.Null);
            info.SetConfiguration("product", "Steam", "Direct", "Normal", false, false, false);
            Assert.That(info.ValidateCapabilities(false, false, true), Is.Not.Null);
            Assert.That(BuildFeatures.AutoNetworkDiagnostics, Is.False, "Editor does not start package capture automatically");
        }

        private static BuildInfo Info(BuildKind kind = BuildKind.Test) => new("0.0.0", kind, "fixture", new string('a', 40), true, false,
            "2026-09-22T08:30:00Z", "6000.3.21f1", "StandaloneWindows64", "x86_64", false, "product");
    }
}
