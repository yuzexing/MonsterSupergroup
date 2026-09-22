using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using Mirror.FizzySteam;
using Steamworks;
using UnityEngine;
using MonsterSupergroup.Builds;

namespace MonsterSupergroup.NetworkCombat
{
    public struct RunIdentityRequest : NetworkMessage { public string ResumeToken; public string DisplayName; public string Version; }
    public struct RunIdentityResponse : NetworkMessage { public bool Accepted; public string ResumeToken; public string Error; }

    /// <summary>Steam uses transport peer identity. KCP uses a server-issued, session-scoped bearer token.</summary>
    [DisallowMultipleComponent]
    public sealed class RunSessionAuthenticator : NetworkAuthenticator
    {
        private readonly Dictionary<string, string> clientTokens = new Dictionary<string, string>();
        private BootGameplayNetworkManager Manager => GetComponent<BootGameplayNetworkManager>();
        private string ClientEndpoint
        {
            get
            {
                var backend = Manager.GetComponent<NetworkBackendBootstrap>();
                ushort port = backend != null && backend.TryGetKcpPort(out ushort configured)
                    ? configured : (Manager.transport is PortTransport transport ? transport.Port : (ushort)0);
                return Manager.networkAddress + ":" + port;
            }
        }

        public override void OnStartServer() =>
            NetworkServer.RegisterHandler<RunIdentityRequest>(ReceiveIdentity, false);
        public override void OnStopServer() => NetworkServer.UnregisterHandler<RunIdentityRequest>();
        public override void OnStartClient() =>
            NetworkClient.RegisterHandler<RunIdentityResponse>(ReceiveResult, false);
        public override void OnStopClient() => NetworkClient.UnregisterHandler<RunIdentityResponse>();

        public override void OnServerAuthenticate(NetworkConnectionToClient connection) =>
            StartCoroutine(AuthenticationTimeout(connection));

        public override void OnClientAuthenticate()
        {
            if (!Manager.CheckBuildForConnection()) { ClientReject(); return; }
            clientTokens.TryGetValue(ClientEndpoint, out string token);
            var steam = Manager.GetComponent<SteamLobbyService>();
            string name = steam != null && steam.IsSteamInitialized ? SteamFriends.GetPersonaName() : Environment.UserName;
            NetworkClient.Send(new RunIdentityRequest { ResumeToken = token, DisplayName = name,
                Version = SteamLobbyMetadata.ProtocolValue + ":" + RuntimeBuildInfo.Version });
        }

        private void ReceiveIdentity(NetworkConnectionToClient connection, RunIdentityRequest request)
        {
            if (connection.isAuthenticated) return;
            if (!RuntimeBuildInfo.CanConnect)
            { RejectWithReason(connection, BuildCompatibility.Encode(BuildRejection.InvalidPackage, null, null, Manager.transport is FizzySteamworks)); return; }
            var versionResult = BuildCompatibility.CheckIdentity(request.Version, RuntimeBuildInfo.Version, SteamLobbyMetadata.ProtocolValue, out string clientVersion);
            if (Manager.UsePreparationRoom && versionResult != BuildRejection.None)
            {
                Debug.LogWarning($"[RunSession] admission={versionResult} client={clientVersion} host={RuntimeBuildInfo.Version} hostBuild={RuntimeBuildInfo.Current?.BuildId}");
                RejectWithReason(connection, BuildCompatibility.Encode(versionResult, clientVersion, RuntimeBuildInfo.Version, Manager.transport is FizzySteamworks)); return;
            }
            if (Manager.UsePreparationRoom && (Manager.ServerRoom?.Phase == PreparationPhase.Loading || Manager.ServerRoom?.Phase == PreparationPhase.Transitioning))
            { RejectWithReason(connection, "房间正在加载，请稍后重连。"); return; }
            if (Manager.UsePreparationRoom && Manager.ServerRoom?.Phase == PreparationPhase.Preparing &&
                Manager.ServerRoom.Count >= PreparationRoom.Capacity)
            { RejectWithReason(connection, "房间已满（最多四人）。"); return; }
            string identity;
            string token = null;
            if (Manager.transport is FizzySteamworks)
            {
                ulong steamId;
                if (connection is LocalConnectionToClient)
                    steamId = SteamUser.GetSteamID().m_SteamID;
                else if (!ulong.TryParse(connection.address, out steamId))
                { ServerReject(connection); return; }
                CSteamID peer = new CSteamID(steamId);
                if (!peer.IsValid() || !peer.BIndividualAccount())
                { ServerReject(connection); return; }
                identity = "steam:" + steamId;
            }
            else
            {
                // A client cannot select a participant ID. Only a token issued by this run can resume one.
                token = request.ResumeToken;
                if (token == null || token.Length != 64 || !Manager.Session.ContainsIdentity("token:" + token))
                    token = NewToken();
                identity = "token:" + token;
            }
            if (!Manager.Session.TryConnect(identity, connection.connectionId, out RunParticipant participant, out string error))
            {
                Debug.LogWarning("[RunSession] Connection rejected: " + error, this);
                RejectWithReason(connection, Manager.Session.IsRosterLocked ? "游戏已经开始，仅允许本局成员重连。" : error);
                return;
            }
            string displayName = (request.DisplayName ?? string.Empty).Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " ");
            if (displayName.Length > 32) displayName = displayName.Substring(0, 32);
            if (!Manager.AdmitPreparation(connection, participant, displayName, out error))
            {
                Manager.Session.Disconnect(connection.connectionId, null);
                RejectWithReason(connection, error); return;
            }
            connection.authenticationData = participant;
            connection.Send(new RunIdentityResponse { Accepted = true, ResumeToken = token });
            ServerAccept(connection);
        }

        private void ReceiveResult(RunIdentityResponse result)
        {
            var lobby = Manager.GetComponent<SteamLobbyService>();
            if (lobby != null && lobby.CurrentLobbyId != 0)
                Debug.Log($"[SteamInvite] stage=authentication lobby={lobby.CurrentLobbyId} host={lobby.HostSteamId64} accepted={result.Accepted} reason={result.Error}");
            if (!result.Accepted) { Manager.SetConnectionNotice(result.Error); ClientReject(); return; }
            if (!string.IsNullOrEmpty(result.ResumeToken)) clientTokens[ClientEndpoint] = result.ResumeToken;
            ClientAccept();
        }

        private IEnumerator AuthenticationTimeout(NetworkConnectionToClient connection)
        {
            double until = Time.realtimeSinceStartupAsDouble + 15;
            while (!connection.isAuthenticated && NetworkServer.active &&
                NetworkServer.connections.TryGetValue(connection.connectionId, out var current) &&
                ReferenceEquals(current, connection) && Time.realtimeSinceStartupAsDouble < until)
                yield return null;
            if (!connection.isAuthenticated && NetworkServer.active &&
                NetworkServer.connections.TryGetValue(connection.connectionId, out var remaining) &&
                ReferenceEquals(remaining, connection)) ServerReject(connection);
        }

        private static string NewToken()
        {
            var bytes = new byte[32];
            using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        private void RejectWithReason(NetworkConnectionToClient connection, string reason)
        {
            connection.Send(new RunIdentityResponse { Accepted = false, Error = reason });
            StartCoroutine(RejectAfterDelivery(connection));
        }
        private IEnumerator RejectAfterDelivery(NetworkConnectionToClient connection)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            if (NetworkServer.connections.TryGetValue(connection.connectionId, out var current) && ReferenceEquals(current, connection))
                ServerReject(connection);
        }
    }
}
