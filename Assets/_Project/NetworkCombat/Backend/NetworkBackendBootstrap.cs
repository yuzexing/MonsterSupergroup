using System;
using Mirror;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MonsterSupergroup.NetworkCombat
{
    public enum NetworkBackendKind : byte
    {
        Steam = 0,
        Kcp = 1
    }

    public enum NetworkRuntimePurpose : byte
    {
        Interactive = 0,
        AutomatedValidation = 1,
        Test = 2
    }

    public readonly struct NetworkBackendSelection
    {
        public NetworkBackendSelection(
            NetworkBackendKind backend,
            NetworkRuntimePurpose purpose,
            string source)
        {
            Backend = backend;
            Purpose = purpose;
            Source = source ?? string.Empty;
        }

        public NetworkBackendKind Backend { get; }
        public NetworkRuntimePurpose Purpose { get; }
        public string Source { get; }
    }

    /// <summary>
    /// Resolves the process-wide network backend before Mirror's NetworkManager
    /// caches Transport.active. All runtime transport selection goes through this
    /// component so gameplay code remains backend agnostic.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class NetworkBackendBootstrap : MonoBehaviour
    {
        public const string EditorPreferenceKey =
            "MonsterSupergroup.NetworkCombat.EditorBackend";
        public const string ValidationRolePrefix = "--boot-gameplay-role=";
        public const string DefaultKcpAddress = "127.0.0.1";
        public const ushort DefaultKcpPort = 7777;

        [SerializeField] private BootGameplayNetworkManager networkManager;
        [SerializeField] private Transport steamTransport;
        [SerializeField] private Transport kcpTransport;
        [SerializeField] private Transport latencySimulation;

        private static NetworkBackendBootstrap instance;

        public NetworkBackendSelection Selection { get; private set; }
        public bool IsInitialized { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public bool UseSimulation { get; private set; }

        public BootGameplayNetworkManager ConfiguredNetworkManager =>
            networkManager;
        public Transport ConfiguredSteamTransport => steamTransport;
        public Transport ConfiguredKcpTransport => kcpTransport;
        public Transport ConfiguredLatencySimulation =>
            latencySimulation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        public void Configure(
            BootGameplayNetworkManager manager,
            Transport productSteamTransport,
            Transport localKcpTransport,
            Transport simulation)
        {
            networkManager = manager != null
                ? manager
                : throw new ArgumentNullException(nameof(manager));
            steamTransport = productSteamTransport != null
                ? productSteamTransport
                : throw new ArgumentNullException(nameof(productSteamTransport));
            kcpTransport = localKcpTransport != null
                ? localKcpTransport
                : throw new ArgumentNullException(nameof(localKcpTransport));
            latencySimulation = simulation != null
                ? simulation
                : throw new ArgumentNullException(nameof(simulation));
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            Selection = ResolveCurrentSelection();
            if (!ResolveReferences(out string error))
            {
                LastError = error;
                Debug.LogError($"[NetworkBackend] {error}", this);
                return;
            }

            if (Selection.Backend == NetworkBackendKind.Kcp)
            {
                UseSimulation = Selection.Purpose ==
                    NetworkRuntimePurpose.AutomatedValidation;
                SelectKcpTransport(
                    DefaultKcpAddress,
                    DefaultKcpPort,
                    UseSimulation,
                    false);
            }
            else
            {
                SelectSteamTransport(false);
            }

            IsInitialized = true;
            Debug.Log(
                $"[NetworkBackend] backend={Selection.Backend} " +
                $"purpose={Selection.Purpose} source={Selection.Source} " +
                $"transport={networkManager.transport.GetType().Name}.",
                this);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public bool TryPrepareKcp(
            string address,
            ushort port,
            bool useSimulation,
            out string error)
        {
            error = null;
            if (!IsInitialized || Selection.Backend != NetworkBackendKind.Kcp)
            {
                error = "The process did not select the KCP backend.";
                return false;
            }
            if (!CanPrepareTransport(out error))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(address))
            {
                error = "KCP address is required.";
                return false;
            }
            if (port == 0)
            {
                error = "KCP port must be between 1 and 65535.";
                return false;
            }

            ShutdownTransport(networkManager.transport);
            UseSimulation = useSimulation;
            SelectKcpTransport(address.Trim(), port, useSimulation, true);
            if (!networkManager.transport.Available())
            {
                error = "The selected KCP transport is unavailable.";
                return false;
            }

            return true;
        }

        public bool TryPrepareSteam(out string error)
        {
            error = null;
            if (!IsInitialized || Selection.Backend != NetworkBackendKind.Steam)
            {
                error = "The process did not select the Steam backend.";
                return false;
            }
            if (!CanPrepareTransport(out error))
            {
                return false;
            }

            ShutdownTransport(steamTransport);
            SelectSteamTransport(true);
            if (!steamTransport.Available())
            {
                steamTransport.enabled = false;
                error = "The configured Steam transport is unavailable.";
                return false;
            }

            return true;
        }

        public void RestoreIdleTransport()
        {
            if (!IsInitialized || NetworkServer.active || NetworkClient.active)
            {
                return;
            }

            if (Selection.Backend == NetworkBackendKind.Kcp)
            {
                string address = string.IsNullOrWhiteSpace(
                        networkManager.networkAddress)
                    ? DefaultKcpAddress
                    : networkManager.networkAddress;
                ushort port = TryGetKcpPort(out ushort current)
                    ? current
                    : DefaultKcpPort;
                SelectKcpTransport(address, port, UseSimulation, false);
            }
            else
            {
                SelectSteamTransport(false);
            }
        }

        public void ShutdownActiveTransport()
        {
            if (!IsInitialized)
            {
                return;
            }

            ShutdownTransport(networkManager.transport);
        }

        public bool TryGetKcpPort(out ushort port)
        {
            if (kcpTransport is PortTransport portTransport)
            {
                port = portTransport.Port;
                return true;
            }

            port = 0;
            return false;
        }

        public static NetworkBackendSelection ResolveSelection(
            string[] arguments,
            bool isEditor,
            bool isKcpDevelopmentBuild,
            NetworkBackendKind editorPreference)
        {
            if (HasArgumentPrefix(arguments, ValidationRolePrefix))
            {
                return new NetworkBackendSelection(
                    NetworkBackendKind.Kcp,
                    NetworkRuntimePurpose.AutomatedValidation,
                    "legacy-validation-argument");
            }
            if (HasArgument(arguments, "-runTests"))
            {
                return new NetworkBackendSelection(
                    NetworkBackendKind.Kcp,
                    NetworkRuntimePurpose.Test,
                    "test-runner");
            }
            if (isKcpDevelopmentBuild)
            {
                return new NetworkBackendSelection(
                    NetworkBackendKind.Kcp,
                    NetworkRuntimePurpose.Interactive,
                    "kcp-development-build");
            }
            if (isEditor && editorPreference == NetworkBackendKind.Kcp)
            {
                return new NetworkBackendSelection(
                    NetworkBackendKind.Kcp,
                    NetworkRuntimePurpose.Interactive,
                    "editor-preference");
            }

            return new NetworkBackendSelection(
                NetworkBackendKind.Steam,
                NetworkRuntimePurpose.Interactive,
                isEditor ? "editor-default" : "player-default");
        }

        private static NetworkBackendSelection ResolveCurrentSelection()
        {
            bool isKcpDevelopmentBuild = false;
#if MONSTER_KCP_DEVELOPMENT_BUILD && !UNITY_EDITOR
            isKcpDevelopmentBuild = true;
#endif
            NetworkBackendKind editorPreference = NetworkBackendKind.Steam;
#if UNITY_EDITOR
            int stored = EditorPrefs.GetInt(
                EditorPreferenceKey,
                (int)NetworkBackendKind.Steam);
            if (stored == (int)NetworkBackendKind.Steam ||
                stored == (int)NetworkBackendKind.Kcp)
            {
                editorPreference = (NetworkBackendKind)stored;
            }
#endif
            return ResolveSelection(
                Environment.GetCommandLineArgs(),
                Application.isEditor,
                isKcpDevelopmentBuild,
                editorPreference);
        }

        private bool ResolveReferences(out string error)
        {
            error = null;
            if (networkManager == null)
            {
                networkManager = GetComponent<BootGameplayNetworkManager>();
            }
            if (steamTransport == null && networkManager != null)
            {
                steamTransport = networkManager.transport;
            }
            if (kcpTransport == null)
            {
                Transport[] transports = GetComponents<Transport>();
                for (int i = 0; i < transports.Length; i++)
                {
                    Transport candidate = transports[i];
                    if (candidate != null && candidate != steamTransport &&
                        candidate != latencySimulation &&
                        candidate is PortTransport)
                    {
                        kcpTransport = candidate;
                        break;
                    }
                }
            }

            if (networkManager == null || steamTransport == null ||
                kcpTransport == null || latencySimulation == null)
            {
                error = "Boot network backend references are not configured.";
                return false;
            }
            if (!(kcpTransport is PortTransport))
            {
                error = "The configured KCP transport does not expose a port.";
                return false;
            }

            return true;
        }

        private bool CanPrepareTransport(out string error)
        {
            error = null;
            if (networkManager == null)
            {
                error = "BootGameplayNetworkManager is unavailable.";
                return false;
            }
            if (NetworkServer.active || NetworkClient.active ||
                networkManager.mode != NetworkManagerMode.Offline ||
                networkManager.IsGameplayLoaded)
            {
                error = "The network backend cannot change while Mirror is active.";
                return false;
            }

            return true;
        }

        private void SelectKcpTransport(
            string address,
            ushort port,
            bool useSimulation,
            bool enableTransport)
        {
            steamTransport.enabled = false;
            PortTransport portTransport = (PortTransport)kcpTransport;
            portTransport.Port = port;

            Transport selected;
            if (useSimulation)
            {
                kcpTransport.enabled = enableTransport;
                latencySimulation.enabled = enableTransport;
                selected = latencySimulation;
            }
            else
            {
                latencySimulation.enabled = false;
                kcpTransport.enabled = enableTransport;
                selected = kcpTransport;
            }

            networkManager.networkAddress = address;
            networkManager.transport = selected;
            Transport.active = selected;
        }

        private void SelectSteamTransport(bool enableTransport)
        {
            latencySimulation.enabled = false;
            kcpTransport.enabled = false;
            steamTransport.enabled = enableTransport;
            networkManager.transport = steamTransport;
            Transport.active = steamTransport;
        }

        private static void ShutdownTransport(Transport transport)
        {
            if (transport == null)
            {
                return;
            }

            try
            {
                transport.Shutdown();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NetworkBackend] Transport shutdown failed: " +
                    exception.Message);
            }
        }

        private static bool HasArgumentPrefix(
            string[] arguments,
            string prefix)
        {
            if (arguments == null)
            {
                return false;
            }
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] != null && arguments[i].StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasArgument(string[] arguments, string expected)
        {
            if (arguments == null)
            {
                return false;
            }
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(
                        arguments[i],
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
