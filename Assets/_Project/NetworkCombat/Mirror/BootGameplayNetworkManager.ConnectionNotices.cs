using MonsterSupergroup.Builds;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class BootGameplayNetworkManager
    {
        private readonly ConnectionNoticeState connectionNotice = new();
        public uint ConnectionAttempt => connectionNotice.Attempt;
        public void BeginConnectionAttempt()
        {
            connectionNotice.Begin(); MenuNotice = string.Empty; PreparationChanged?.Invoke();
        }
        public void SetConnectionNotice(string message, int priority = 100, uint? attempt = null)
        {
            if (!connectionNotice.Set(attempt ?? ConnectionAttempt, message, priority)) return;
            MenuNotice = message;
            Debug.Log($"[ConnectionNotice] attempt={ConnectionAttempt} priority={priority} reason={message}");
            PreparationChanged?.Invoke();
        }
        public bool CheckBuildForConnection()
        {
            if (RuntimeBuildInfo.CanConnect) return true;
            SetConnectionNotice(BuildCompatibility.Encode(BuildRejection.InvalidPackage, null, null, false));
            Debug.LogWarning("[BuildInfo] Connection blocked: " + RuntimeBuildInfo.Error); return false;
        }
        private bool RuntimeBuildAvailable(out string error)
        {
            error = RuntimeBuildInfo.CanConnect ? null : "ui.connection.build.invalidpackage"; return error == null;
        }
        public void NoticeSteamCleanup(string reason)
        {
            if (string.IsNullOrEmpty(reason) || connectionNotice.Intentional) return;
            if (BuildCompatibility.TryDecode(reason, out _, out _, out _, out _)) { SetConnectionNotice(reason); return; }
            if (reason == "Lobby protocol is incompatible.")
                SetConnectionNotice(BuildCompatibility.Encode(BuildRejection.ProtocolMismatch, null, null, true));
            if (reason == "The Lobby host closed the session." || reason == "Lobby owner changed; host migration is not supported." || reason == "The current Lobby was removed.")
                SetConnectionNotice("ui.connection.host_closed", 80);
            else if (reason == "Mirror Client disconnected.")
                SetConnectionNotice(connectionNotice.Connected ? "ui.connection.host_lost" : "ui.connection.connect_failed", 50);
        }
    }
}
