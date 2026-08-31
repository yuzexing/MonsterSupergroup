using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SteamLobbyTests
    {
        private const ulong ValidHostSteamId = 76561198000000001ul;

        [Test]
        public void ReadyMetadata_ParsesExpectedHostSteamId()
        {
            bool parsed = SteamLobbyMetadata.TryGetReadyHostSteamId(
                SteamLobbyMetadata.GameValue,
                SteamLobbyMetadata.ProtocolValue,
                SteamLobbyMetadata.ReadyState,
                ValidHostSteamId.ToString(),
                out ulong hostSteamId,
                out string error);

            Assert.That(parsed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(hostSteamId, Is.EqualTo(ValidHostSteamId));
        }

        [Test]
        public void ReadyMetadata_RejectsProtocolMismatchAndInvalidSteamId()
        {
            Assert.That(
                SteamLobbyMetadata.TryGetReadyHostSteamId(
                    SteamLobbyMetadata.GameValue,
                    "2",
                    SteamLobbyMetadata.ReadyState,
                    ValidHostSteamId.ToString(),
                    out _,
                    out string protocolError),
                Is.False);
            Assert.That(protocolError, Does.Contain("protocol"));

            Assert.That(
                SteamLobbyMetadata.TryGetReadyHostSteamId(
                    SteamLobbyMetadata.GameValue,
                    SteamLobbyMetadata.ProtocolValue,
                    SteamLobbyMetadata.ReadyState,
                    "123",
                    out _,
                    out string steamIdError),
                Is.False);
            Assert.That(steamIdError, Does.Contain("SteamID64"));
        }

        [Test]
        public void SearchSummary_RequiresProjectMetadataReadyStateAndOpenSlot()
        {
            Assert.That(
                SteamLobbyMetadata.TryCreateSummary(
                    42ul,
                    SteamLobbyMetadata.GameValue,
                    SteamLobbyMetadata.ProtocolValue,
                    SteamLobbyMetadata.ReadyState,
                    ValidHostSteamId.ToString(),
                    "Host's Lobby",
                    1,
                    4,
                    out SteamLobbySummary summary),
                Is.True);
            Assert.That(summary.HostSteamId, Is.EqualTo(ValidHostSteamId));
            Assert.That(summary.MemberCount, Is.EqualTo(1));
            Assert.That(summary.MemberLimit, Is.EqualTo(4));

            Assert.That(
                SteamLobbyMetadata.TryCreateSummary(
                    42ul,
                    "spacewar_other_game",
                    SteamLobbyMetadata.ProtocolValue,
                    SteamLobbyMetadata.ReadyState,
                    ValidHostSteamId.ToString(),
                    "Other Lobby",
                    1,
                    4,
                    out _),
                Is.False);
            Assert.That(
                SteamLobbyMetadata.TryCreateSummary(
                    42ul,
                    SteamLobbyMetadata.GameValue,
                    SteamLobbyMetadata.ProtocolValue,
                    SteamLobbyMetadata.StartingState,
                    ValidHostSteamId.ToString(),
                    "Starting Lobby",
                    1,
                    4,
                    out _),
                Is.False);
            Assert.That(
                SteamLobbyMetadata.TryCreateSummary(
                    42ul,
                    SteamLobbyMetadata.GameValue,
                    SteamLobbyMetadata.ProtocolValue,
                    SteamLobbyMetadata.ReadyState,
                    ValidHostSteamId.ToString(),
                    "Full Lobby",
                    4,
                    4,
                    out _),
                Is.False);
        }

        [Test]
        public void RejectedOperation_PreservesTheOperationAlreadyInFlight()
        {
            GameObject root = new GameObject("Steam Lobby State Test");
            SteamLobbyService service = root.AddComponent<SteamLobbyService>();
            try
            {
                SetAutoProperty(service, nameof(service.IsSteamInitialized), true);
                SetAutoProperty(service, nameof(service.State),
                    SteamLobbyState.Creating);

                service.RequestLobbyList();

                Assert.That(service.State, Is.EqualTo(SteamLobbyState.Creating));
                Assert.That(service.LastError, Does.Contain("already active"));

                SetAutoProperty(service, nameof(service.State),
                    SteamLobbyState.Hosting);
                service.JoinLobby(0ul);
                Assert.That(service.State, Is.EqualTo(SteamLobbyState.Hosting));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RemoteSessionTermination_CleansBackToIdleWithReason()
        {
            GameObject root = new GameObject("Steam Lobby Cleanup State Test");
            SteamLobbyService service = root.AddComponent<SteamLobbyService>();
            try
            {
                MethodInfo cleanup = typeof(SteamLobbyService).GetMethod(
                    "CleanupInternal",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(cleanup, Is.Not.Null);

                cleanup.Invoke(
                    service,
                    new object[] { "The Lobby host closed the session.", true, false });

                Assert.That(service.State, Is.EqualTo(SteamLobbyState.Idle));
                Assert.That(service.LastError, Does.Contain("host closed"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void SetAutoProperty<T>(
            SteamLobbyService service,
            string propertyName,
            T value)
        {
            FieldInfo field = typeof(SteamLobbyService).GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(service, value);
        }
    }
}
