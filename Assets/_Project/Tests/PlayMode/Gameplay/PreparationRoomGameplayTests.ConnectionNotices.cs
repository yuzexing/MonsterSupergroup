using System.Collections;
using Mirror;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationRoomGameplayTests
    {
        [UnityTest] public IEnumerator VersionRejectionSurvivesTransportErrorDisconnectAndCleanup()
        {
            manager.BeginConnectionAttempt();
            const string rejection = "@build-notice:ClientOlder|0.0.0|0.0.1|steam";
            manager.SetConnectionNotice(rejection);
            manager.OnClientError(TransportError.ConnectionClosed, "peer closed after rejection");
            manager.OnClientDisconnect();
            manager.NoticeSteamCleanup("Mirror Client disconnected.");
            yield return null;
            Assert.That(manager.MenuNotice, Is.EqualTo(rejection));
            manager.BeginConnectionAttempt();
            Assert.That(manager.MenuNotice, Is.Empty);
        }
        [UnityTest] public IEnumerator HostClosedReasonWinsOverGenericDisconnect()
        {
            manager.BeginConnectionAttempt();
            manager.OnClientError(TransportError.ConnectionClosed, "transport closed");
            manager.NoticeSteamCleanup("The Lobby host closed the session.");
            manager.OnClientDisconnect();
            yield return null;
            Assert.That(manager.MenuNotice, Is.EqualTo("ui.connection.host_closed"));
        }
        [UnityTest] public IEnumerator LocalLeaveHasNoErrorAndNextRoomCanOpen()
        {
            yield return Open();
            uint attempt = manager.ConnectionAttempt;
            manager.LeavePreparationRoom();
            yield return Until(() => !NetworkClient.active && !NetworkServer.active && !manager.IsLeavingRoom);
            manager.OnClientError(TransportError.ConnectionClosed, "late local shutdown");
            Assert.That(manager.MenuNotice, Is.Empty);
            yield return Open();
            manager.SetConnectionNotice("old room error", 100, attempt);
            Assert.That(manager.MenuNotice, Is.Empty);
        }
    }
}
