using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class NetworkBackendEditorSettings
    {
        private const string SteamMenuPath =
            "Monster Supergroup/Network Combat/Editor Backend/Steam";
        private const string KcpMenuPath =
            "Monster Supergroup/Network Combat/Editor Backend/KCP Local";

        [MenuItem(SteamMenuPath)]
        private static void SelectSteam()
        {
            Select(NetworkBackendKind.Steam);
        }

        [MenuItem(KcpMenuPath)]
        private static void SelectKcp()
        {
            Select(NetworkBackendKind.Kcp);
        }

        [MenuItem(SteamMenuPath, true)]
        private static bool ValidateSteam()
        {
            Menu.SetChecked(
                SteamMenuPath,
                Current == NetworkBackendKind.Steam);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(KcpMenuPath, true)]
        private static bool ValidateKcp()
        {
            Menu.SetChecked(KcpMenuPath, Current == NetworkBackendKind.Kcp);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static NetworkBackendKind Current
        {
            get
            {
                int stored = EditorPrefs.GetInt(
                    NetworkBackendBootstrap.EditorPreferenceKey,
                    (int)NetworkBackendKind.Steam);
                return stored == (int)NetworkBackendKind.Kcp
                    ? NetworkBackendKind.Kcp
                    : NetworkBackendKind.Steam;
            }
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
