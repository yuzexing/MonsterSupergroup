using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class NetworkBackendEditorSettings
    {
        private static void SelectSteam()
        {
            Select(NetworkBackendKind.Steam);
        }


        private static void SelectKcp()
        {
            Select(NetworkBackendKind.Kcp);
        }


        private static void LogInviteDiagnostics()
        {
            (Object.FindFirstObjectByType<SteamLobbyService>() ?? throw new System.InvalidOperationException("Steam service is not ready.")).LogInviteDiagnostics();
        }


        private static void OpenInviteDialog()
        {
            (Object.FindFirstObjectByType<SteamLobbyService>() ?? throw new System.InvalidOperationException("Steam service is not ready.")).OpenLobbyInviteOverlay();
        }



        private static void Select(NetworkBackendKind backend)
        {
            EditorPrefs.SetInt(
                NetworkBackendBootstrap.EditorPreferenceKey,
                (int)backend);
            Debug.Log(
                $"Network backend set to {backend}. It will take effect " +
                "the next time Play Mode starts; Boot.unity was not changed.");
        }
    }
}
