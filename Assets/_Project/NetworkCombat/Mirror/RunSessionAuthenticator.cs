using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using Mirror.FizzySteam;
using Steamworks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public struct RunIdentityRequest : NetworkMessage { public string ResumeToken; }
    public struct RunIdentityResponse : NetworkMessage { public bool Accepted; public string ResumeToken; }

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
            clientTokens.TryGetValue(ClientEndpoint, out string token);
            NetworkClient.Send(new RunIdentityRequest { ResumeToken = token });
        }

        private void ReceiveIdentity(NetworkConnectionToClient connection, RunIdentityRequest request)
        {
            if (connection.isAuthenticated) return;
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
                ServerReject(connection);
                return;
            }
            connection.authenticationData = participant;
            connection.Send(new RunIdentityResponse { Accepted = true, ResumeToken = token });
            ServerAccept(connection);
        }

        private void ReceiveResult(RunIdentityResponse result)
        {
            if (!result.Accepted) { ClientReject(); return; }
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
    }
}
