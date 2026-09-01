using System;
using System.Collections;
using System.Globalization;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum KcpLocalRole : byte
    {
        None = 0,
        Host = 1,
        Client = 2
    }

    public enum KcpLocalNetworkState : byte
    {
        Disabled = 0,
        Idle = 1,
        Connecting = 2,
        Hosting = 3,
        Connected = 4,
        Stopping = 5,
        Error = 6
    }

    public readonly struct KcpLocalLaunchOptions
    {
        public KcpLocalLaunchOptions(
            KcpLocalRole role,
            string address,
            ushort port,
            bool useSimulation)
        {
            Role = role;
            Address = address;
            Port = port;
            UseSimulation = useSimulation;
        }

        public KcpLocalRole Role { get; }
        public string Address { get; }
        public ushort Port { get; }
        public bool UseSimulation { get; }

        public static bool TryParse(
            string[] arguments,
            out KcpLocalLaunchOptions options,
            out string error)
        {
            options = default;
            error = null;

            KcpLocalRole role = KcpLocalRole.None;
            string roleText = FindValue(arguments, "--kcp-role=");
            if (roleText != null)
            {
                if (string.Equals(
                        roleText,
                        "host",
                        StringComparison.OrdinalIgnoreCase))
                {
                    role = KcpLocalRole.Host;
                }
                else if (string.Equals(
                             roleText,
                             "client",
                             StringComparison.OrdinalIgnoreCase))
                {
                    role = KcpLocalRole.Client;
                }
                else
                {
                    error = $"Unknown --kcp-role value: {roleText}.";
                    return false;
                }
            }

            string address = FindValue(arguments, "--kcp-address=");
            if (address == null)
            {
                address = NetworkBackendBootstrap.DefaultKcpAddress;
            }
            else if (string.IsNullOrWhiteSpace(address))
            {
                error = "--kcp-address cannot be empty.";
                return false;
            }

            ushort port = NetworkBackendBootstrap.DefaultKcpPort;
            string portText = FindValue(arguments, "--kcp-port=");
            if (portText != null &&
                (!ushort.TryParse(
                     portText,
                     NumberStyles.None,
                     CultureInfo.InvariantCulture,
                     out port) || port == 0))
            {
                error = $"Invalid --kcp-port value: {portText}.";
                return false;
            }

            bool useSimulation = false;
            string simulationText = FindValue(
                arguments,
                "--kcp-simulation=");
            if (simulationText != null &&
                !bool.TryParse(simulationText, out useSimulation))
            {
                error =
                    $"Invalid --kcp-simulation value: {simulationText}.";
                return false;
            }

            options = new KcpLocalLaunchOptions(
                role,
                address.Trim(),
                port,
                useSimulation);
            return true;
        }

        private static string FindValue(string[] arguments, string prefix)
        {
            if (arguments == null)
            {
                return null;
            }
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument != null && argument.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(prefix.Length);
                }
            }

            return null;
        }
    }

    [DefaultExecutionOrder(-29000)]
    [DisallowMultipleComponent]
    public sealed class KcpLocalNetworkService : MonoBehaviour
    {
        [SerializeField] private NetworkBackendBootstrap backendBootstrap;
        [SerializeField] private BootGameplayNetworkManager networkManager;

        private KcpLocalRole automaticRole;
        private bool applicationQuitting;
        private string stopCompletionMessage = string.Empty;

        public event Action Changed;

        public KcpLocalNetworkState State { get; private set; } =
            KcpLocalNetworkState.Disabled;
        public string LastError { get; private set; } = string.Empty;
        public string Address { get; private set; } =
            NetworkBackendBootstrap.DefaultKcpAddress;
        public ushort Port { get; private set; } =
            NetworkBackendBootstrap.DefaultKcpPort;
        public bool UseSimulation { get; private set; }
        public bool IsHostSession { get; private set; }

        public NetworkBackendBootstrap ConfiguredBackendBootstrap =>
            backendBootstrap;
        public BootGameplayNetworkManager ConfiguredNetworkManager =>
            networkManager;
        public bool IsKcpBackendSelected => backendBootstrap != null &&
            backendBootstrap.IsInitialized &&
            backendBootstrap.Selection.Backend == NetworkBackendKind.Kcp;
        public bool IsInteractiveKcp => IsKcpBackendSelected &&
            backendBootstrap.Selection.Purpose ==
                NetworkRuntimePurpose.Interactive;
        public bool CanStart => IsInteractiveKcp &&
            (State == KcpLocalNetworkState.Idle ||
             State == KcpLocalNetworkState.Error) &&
            networkManager != null &&
            networkManager.mode == NetworkManagerMode.Offline &&
            !NetworkServer.active && !NetworkClient.active &&
            !networkManager.IsGameplayLoaded;

        public void Configure(
            NetworkBackendBootstrap bootstrap,
            BootGameplayNetworkManager manager)
        {
            backendBootstrap = bootstrap != null
                ? bootstrap
                : throw new ArgumentNullException(nameof(bootstrap));
            networkManager = manager != null
                ? manager
                : throw new ArgumentNullException(nameof(manager));
        }

        private void Awake()
        {
            ResolveReferences();
            if (!IsInteractiveKcp)
            {
                State = KcpLocalNetworkState.Disabled;
                return;
            }

            if (!KcpLocalLaunchOptions.TryParse(
                    Environment.GetCommandLineArgs(),
                    out KcpLocalLaunchOptions options,
                    out string error))
            {
                SetError(error);
                return;
            }

            automaticRole = options.Role;
            Address = options.Address;
            Port = options.Port;
            UseSimulation = options.UseSimulation;
            SetState(KcpLocalNetworkState.Idle, string.Empty);
        }

        private IEnumerator Start()
        {
            if (!IsInteractiveKcp || automaticRole == KcpLocalRole.None)
            {
                yield break;
            }

            yield return null;
            if (automaticRole == KcpLocalRole.Host)
            {
                StartHost();
            }
            else
            {
                StartClient();
            }
        }

        private void Update()
        {
            if (!IsKcpBackendSelected)
            {
                return;
            }

            if (State == KcpLocalNetworkState.Connecting &&
                NetworkClient.isConnected)
            {
                SetState(KcpLocalNetworkState.Connected, string.Empty);
                Debug.Log(
                    $"[KcpLocal] Connected to {Address}:{Port}.",
                    this);
            }
            else if (State == KcpLocalNetworkState.Hosting &&
                     !NetworkServer.active)
            {
                BeginUnexpectedStop("The KCP Host stopped unexpectedly.");
            }
            else if ((State == KcpLocalNetworkState.Connecting ||
                      State == KcpLocalNetworkState.Connected) &&
                     !IsHostSession && !NetworkClient.active)
            {
                BeginUnexpectedStop("The KCP Client connection ended.");
            }

            if (State == KcpLocalNetworkState.Stopping &&
                networkManager != null &&
                networkManager.mode == NetworkManagerMode.Offline &&
                !NetworkServer.active && !NetworkClient.active &&
                !networkManager.IsGameplayLoaded)
            {
                CompleteStop();
            }
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
            Stop();
        }

        public bool TrySetConfiguration(
            string address,
            ushort port,
            bool useSimulation,
            out string error)
        {
            error = null;
            if (!CanStart)
            {
                error = "KCP configuration can only change while offline.";
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

            Address = address.Trim();
            Port = port;
            UseSimulation = useSimulation;
            LastError = string.Empty;
            NotifyChanged();
            return true;
        }

        public void StartHost()
        {
            if (!TryBeginStart(out string error))
            {
                SetError(error);
                return;
            }
            if (!backendBootstrap.TryPrepareKcp(
                    Address,
                    Port,
                    UseSimulation,
                    out error))
            {
                SetError(error);
                return;
            }

            try
            {
                IsHostSession = true;
                networkManager.StartHost();
                if (!NetworkServer.active || !NetworkClient.active)
                {
                    throw new InvalidOperationException(
                        "Mirror Host did not become active.");
                }

                SetState(KcpLocalNetworkState.Hosting, string.Empty);
                Debug.Log(
                    $"[KcpLocal] Hosting on {Address}:{Port}; " +
                    $"simulation={UseSimulation}.",
                    this);
            }
            catch (Exception exception)
            {
                StopAfterStartFailure();
                SetError($"KCP StartHost failed: {exception.Message}");
            }
        }

        public void StartClient()
        {
            if (!TryBeginStart(out string error))
            {
                SetError(error);
                return;
            }
            if (!backendBootstrap.TryPrepareKcp(
                    Address,
                    Port,
                    UseSimulation,
                    out error))
            {
                SetError(error);
                return;
            }

            try
            {
                IsHostSession = false;
                networkManager.StartClient();
                if (!NetworkClient.active)
                {
                    throw new InvalidOperationException(
                        "Mirror Client did not become active.");
                }

                SetState(KcpLocalNetworkState.Connecting, string.Empty);
                Debug.Log(
                    $"[KcpLocal] Connecting to {Address}:{Port}; " +
                    $"simulation={UseSimulation}.",
                    this);
            }
            catch (Exception exception)
            {
                StopAfterStartFailure();
                SetError($"KCP StartClient failed: {exception.Message}");
            }
        }

        public void Stop()
        {
            if (!IsKcpBackendSelected ||
                State == KcpLocalNetworkState.Stopping)
            {
                return;
            }

            if (!NetworkServer.active && !NetworkClient.active &&
                (networkManager == null || !networkManager.IsGameplayLoaded))
            {
                backendBootstrap?.ShutdownActiveTransport();
                backendBootstrap?.RestoreIdleTransport();
                IsHostSession = false;
                SetState(KcpLocalNetworkState.Idle, string.Empty);
                return;
            }

            SetState(KcpLocalNetworkState.Stopping, string.Empty);
            try
            {
                StopMirrorAndTransport();
            }
            catch (Exception exception)
            {
                stopCompletionMessage =
                    $"KCP cleanup failed: {exception.Message}";
            }

            if (applicationQuitting)
            {
                CompleteStop();
            }
        }

        private void BeginUnexpectedStop(string message)
        {
            if (State == KcpLocalNetworkState.Stopping)
            {
                return;
            }

            stopCompletionMessage = message;
            SetState(KcpLocalNetworkState.Stopping, message);
            try
            {
                StopMirrorAndTransport();
            }
            catch (Exception exception)
            {
                stopCompletionMessage =
                    $"{message} Cleanup also failed: {exception.Message}";
                SetState(
                    KcpLocalNetworkState.Stopping,
                    stopCompletionMessage);
            }
        }

        private void StopMirrorAndTransport()
        {
            try
            {
                if (networkManager != null)
                {
                    if (NetworkServer.active && NetworkClient.active)
                    {
                        networkManager.StopHost();
                    }
                    else if (NetworkServer.active)
                    {
                        networkManager.StopServer();
                    }
                    else if (NetworkClient.active)
                    {
                        networkManager.StopClient();
                    }
                }
            }
            finally
            {
                backendBootstrap?.ShutdownActiveTransport();
            }
        }

        private void CompleteStop()
        {
            backendBootstrap?.ShutdownActiveTransport();
            backendBootstrap?.RestoreIdleTransport();
            IsHostSession = false;
            string message = stopCompletionMessage;
            stopCompletionMessage = string.Empty;
            SetState(KcpLocalNetworkState.Idle, message);
            Debug.Log("[KcpLocal] Network session stopped.", this);
        }

        private bool TryBeginStart(out string error)
        {
            error = null;
            if (!CanStart)
            {
                error = "Another Mirror operation or Gameplay cleanup is active.";
                return false;
            }

            return true;
        }

        private void StopAfterStartFailure()
        {
            try
            {
                if (NetworkServer.active && NetworkClient.active)
                {
                    networkManager.StopHost();
                }
                else if (NetworkServer.active)
                {
                    networkManager.StopServer();
                }
                else if (NetworkClient.active)
                {
                    networkManager.StopClient();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[KcpLocal] Start failure cleanup also failed: " +
                    exception.Message,
                    this);
            }
            backendBootstrap?.ShutdownActiveTransport();
            backendBootstrap?.RestoreIdleTransport();
            IsHostSession = false;
        }

        private void ResolveReferences()
        {
            if (backendBootstrap == null)
            {
                backendBootstrap = GetComponent<NetworkBackendBootstrap>();
            }
            if (networkManager == null)
            {
                networkManager = GetComponent<BootGameplayNetworkManager>();
            }
        }

        private void SetError(string message)
        {
            SetState(KcpLocalNetworkState.Error, message);
            Debug.LogWarning($"[KcpLocal] {message}", this);
        }

        private void SetState(KcpLocalNetworkState state, string message)
        {
            State = state;
            LastError = message ?? string.Empty;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Changed?.Invoke();
        }
    }
}
