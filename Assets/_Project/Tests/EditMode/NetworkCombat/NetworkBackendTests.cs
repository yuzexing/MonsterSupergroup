using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class NetworkBackendTests
    {
        [Test]
        public void Selection_DefaultsToSteamForEditorAndPlayer()
        {
            NetworkBackendSelection editor =
                NetworkBackendBootstrap.ResolveSelection(
                    new string[0],
                    true,
                    false,
                    NetworkBackendKind.Steam);
            NetworkBackendSelection player =
                NetworkBackendBootstrap.ResolveSelection(
                    new string[0],
                    false,
                    false,
                    NetworkBackendKind.Kcp);

            Assert.That(editor.Backend, Is.EqualTo(NetworkBackendKind.Steam));
            Assert.That(player.Backend, Is.EqualTo(NetworkBackendKind.Steam));
            Assert.That(editor.Purpose,
                Is.EqualTo(NetworkRuntimePurpose.Interactive));
        }

        [Test]
        public void Selection_UsesEditorPreferenceAndPerBuildDefine()
        {
            NetworkBackendSelection editor =
                NetworkBackendBootstrap.ResolveSelection(
                    new string[0],
                    true,
                    false,
                    NetworkBackendKind.Kcp);
            NetworkBackendSelection build =
                NetworkBackendBootstrap.ResolveSelection(
                    new string[0],
                    false,
                    true,
                    NetworkBackendKind.Steam);

            Assert.That(editor.Backend, Is.EqualTo(NetworkBackendKind.Kcp));
            Assert.That(editor.Source, Is.EqualTo("editor-preference"));
            Assert.That(build.Backend, Is.EqualTo(NetworkBackendKind.Kcp));
            Assert.That(build.Source, Is.EqualTo("kcp-development-build"));
        }

        [Test]
        public void Selection_PreservesValidationAndTestPurposes()
        {
            NetworkBackendSelection validation =
                NetworkBackendBootstrap.ResolveSelection(
                    new[] { "--boot-gameplay-role=host" },
                    false,
                    false,
                    NetworkBackendKind.Steam);
            NetworkBackendSelection tests =
                NetworkBackendBootstrap.ResolveSelection(
                    new[] { "-runTests" },
                    true,
                    false,
                    NetworkBackendKind.Steam);

            Assert.That(validation.Backend, Is.EqualTo(NetworkBackendKind.Kcp));
            Assert.That(validation.Purpose,
                Is.EqualTo(NetworkRuntimePurpose.AutomatedValidation));
            Assert.That(tests.Backend, Is.EqualTo(NetworkBackendKind.Kcp));
            Assert.That(tests.Purpose,
                Is.EqualTo(NetworkRuntimePurpose.Test));
        }

        [Test]
        public void KcpLaunchOptions_ParseDefaultsAndExplicitValues()
        {
            Assert.That(
                KcpLocalLaunchOptions.TryParse(
                    new string[0],
                    out KcpLocalLaunchOptions defaults,
                    out string defaultError),
                Is.True,
                defaultError);
            Assert.That(defaults.Role, Is.EqualTo(KcpLocalRole.None));
            Assert.That(defaults.Address,
                Is.EqualTo(NetworkBackendBootstrap.DefaultKcpAddress));
            Assert.That(defaults.Port,
                Is.EqualTo(NetworkBackendBootstrap.DefaultKcpPort));
            Assert.That(defaults.UseSimulation, Is.False);

            Assert.That(
                KcpLocalLaunchOptions.TryParse(
                    new[]
                    {
                        "--kcp-role=client",
                        "--kcp-address=192.168.1.25",
                        "--kcp-port=7788",
                        "--kcp-simulation=true"
                    },
                    out KcpLocalLaunchOptions explicitOptions,
                    out string explicitError),
                Is.True,
                explicitError);
            Assert.That(explicitOptions.Role, Is.EqualTo(KcpLocalRole.Client));
            Assert.That(explicitOptions.Address, Is.EqualTo("192.168.1.25"));
            Assert.That(explicitOptions.Port, Is.EqualTo(7788));
            Assert.That(explicitOptions.UseSimulation, Is.True);
        }

        [TestCase("--kcp-role=server")]
        [TestCase("--kcp-role=")]
        [TestCase("--kcp-port=0")]
        [TestCase("--kcp-port=")]
        [TestCase("--kcp-port=invalid")]
        [TestCase("--kcp-address=")]
        [TestCase("--kcp-simulation=")]
        [TestCase("--kcp-simulation=maybe")]
        public void KcpLaunchOptions_RejectInvalidValues(string argument)
        {
            Assert.That(
                KcpLocalLaunchOptions.TryParse(
                    new[] { argument },
                    out _,
                    out string error),
                Is.False);
            Assert.That(error, Is.Not.Empty);
        }
    }
}
