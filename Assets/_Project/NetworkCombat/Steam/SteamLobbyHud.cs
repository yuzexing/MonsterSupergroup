using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class SteamLobbyHud : MonoBehaviour
    {
        [SerializeField] private SteamLobbyService service;

        private Vector2 lobbyScroll;

        public SteamLobbyService ConfiguredService => service;

        public void Configure(SteamLobbyService lobbyService)
        {
            service = lobbyService != null
                ? lobbyService
                : throw new System.ArgumentNullException(nameof(lobbyService));
        }

        private void Awake()
        {
            if (service == null)
            {
                service = GetComponent<SteamLobbyService>();
            }
        }

        private void OnGUI()
        {
            if (Application.isBatchMode || service == null ||
                !service.IsSteamBackendSelected)
            {
                return;
            }

            float height = Mathf.Min(520f, Screen.height - 20f);
            GUILayout.BeginArea(new Rect(10f, 10f, 520f, height), GUI.skin.box);
            GUILayout.Label("Steam Lobby / FizzySteamworks");
            GUILayout.Label($"State: {service.State}");
            GUILayout.Label(
                $"Steam: {(service.IsSteamInitialized ? "Ready" : "Unavailable")}");

            if (!string.IsNullOrEmpty(service.LastError))
            {
                GUILayout.Label($"Status: {service.LastError}");
            }

            if (!service.IsSteamInitialized)
            {
                if (!service.IsValidationBypass && GUILayout.Button("Retry Steam"))
                {
                    service.TryInitializeSteam();
                }

                GUILayout.EndArea();
                return;
            }

            if (service.CurrentLobbyId != 0ul)
            {
                DrawSession();
                GUILayout.EndArea();
                return;
            }

            bool previousEnabled = GUI.enabled;
            GUI.enabled = service.CanStartOperation;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Lobby"))
            {
                service.CreateLobby();
            }
            if (GUILayout.Button("Refresh"))
            {
                service.RequestLobbyList();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"Lobbies: {service.Lobbies.Count}");
            lobbyScroll = GUILayout.BeginScrollView(
                lobbyScroll,
                GUILayout.Height(Mathf.Max(120f, height - 165f)));
            for (int i = 0; i < service.Lobbies.Count; i++)
            {
                SteamLobbySummary lobby = service.Lobbies[i];
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(
                    $"{lobby.Name}\n" +
                    $"{lobby.MemberCount}/{lobby.MemberLimit}  " +
                    $"Host {lobby.HostSteamId}",
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Join", GUILayout.Width(80f)))
                {
                    service.JoinLobby(lobby.LobbyId);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUI.enabled = previousEnabled;
            GUILayout.EndArea();
        }

        private void DrawSession()
        {
            string role = service.IsHostSession ? "Host" : "Client";
            string mirrorState = service.IsHostSession
                ? $"Server={NetworkServer.active}, LocalClient={NetworkClient.active}"
                : $"Active={NetworkClient.active}, Connected={NetworkClient.isConnected}";
            GUILayout.Label($"Role: {role}");
            GUILayout.Label($"LobbyID: {service.CurrentLobbyId}");
            GUILayout.Label($"Host SteamID64: {service.HostSteamId64}");
            GUILayout.Label($"Mirror: {mirrorState}");

            string button = service.IsHostSession
                ? "Stop Host & Leave"
                : "Disconnect & Leave";
            if (GUILayout.Button(button))
            {
                service.LeaveAndStop();
            }
        }
    }
}
