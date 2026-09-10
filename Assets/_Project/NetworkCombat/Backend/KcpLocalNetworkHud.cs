using System.Globalization;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class KcpLocalNetworkHud : MonoBehaviour
    {
        [SerializeField] private KcpLocalNetworkService service;

        [SerializeField]
        private bool showHud = true;
        
        [SerializeField]
        private KeyCode toggleHudKey = KeyCode.F4;
        
        [Header("HUD")]
        [SerializeField] private Vector2 hudPosition = new Vector2(10f, 10f);
        [SerializeField] private Vector2 hudSize = new Vector2(440f, 300f);
        
        private string addressText;
        private string portText;
        private bool useSimulation;
        private string inputError = string.Empty;

        public KcpLocalNetworkService ConfiguredService => service;

        public void Configure(KcpLocalNetworkService localService)
        {
            service = localService != null
                ? localService
                : throw new System.ArgumentNullException(nameof(localService));
        }

        private void Awake()
        {
            if (service == null)
            {
                service = GetComponent<KcpLocalNetworkService>();
            }
            if (service != null)
            {
                addressText = service.Address;
                portText = service.Port.ToString(CultureInfo.InvariantCulture);
                useSimulation = service.UseSimulation;
            }
        }
        
        private void Update()
        {
            if (Input.GetKeyDown(toggleHudKey))
            {
                showHud = !showHud;
            }
        }

        private void OnGUI()
        {
            if (Application.isBatchMode || service == null ||
                !service.IsInteractiveKcp)
            {
                return;
            }
            // HUD 隐藏时，只留下一个小按钮。
            if (!showHud)
            {
                if (GUI.Button(
                        new Rect(
                            hudPosition.x,
                            hudPosition.y,
                            100f,
                            30f),
                        "Show HUD"))
                {
                    showHud = true;
                }

                return;
            }

            GUILayout.BeginArea(
                new Rect(
                    hudPosition.x,
                    hudPosition.y,
                    hudSize.x,
                    Mathf.Max(hudSize.y, 420f)),
                GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("KCP Local / Boot -> Gameplay");
            if (GUILayout.Button("Hide", GUILayout.Width(60f)))
            {
                showHud = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"State: {service.State}");
            GUILayout.Label(
                $"Mirror: Server={NetworkServer.active}, " +
                $"Client={NetworkClient.active}, " +
                $"Connected={NetworkClient.isConnected}");

            string status = !string.IsNullOrEmpty(inputError)
                ? inputError
                : service.LastError;
            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Label($"Status: {status}");
            }

            bool previousEnabled = GUI.enabled;
            GUI.enabled = service.CanStart;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", GUILayout.Width(60f));
            addressText = GUILayout.TextField(addressText ?? string.Empty);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Port", GUILayout.Width(60f));
            portText = GUILayout.TextField(portText ?? string.Empty);
            GUILayout.EndHorizontal();
            useSimulation = GUILayout.Toggle(
                useSimulation,
                "Network Simulation (LatencySimulation -> KCP)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host"))
            {
                if (TryApplyInput())
                {
                    service.StartHost();
                }
            }
            if (GUILayout.Button("Client"))
            {
                if (TryApplyInput())
                {
                    service.StartClient();
                }
            }
            if (GUILayout.Button("Server only") && TryApplyInput()) service.StartServer();
            GUILayout.EndHorizontal();
            GUI.enabled = previousEnabled;

            if (NetworkServer.active)
            {
                var manager = service.ConfiguredNetworkManager;
                bool canBegin = manager.CanBeginRun(out string startReason);
                GUI.enabled = previousEnabled && canBegin && !manager.Session.IsRosterLocked;
                if (GUILayout.Button(manager.Session.IsRosterLocked ? "Run started" : "Start run"))
                    if (!manager.TryBeginRun(out inputError)) Debug.LogWarning(inputError, this);
                GUI.enabled = previousEnabled;
                if (!canBegin) GUILayout.Label(startReason);
                GUILayout.Label($"Members: {manager.Session.Participants.Count} | After start: original members only");
                var wave = NetworkCombatWorld.Instance != null ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>() : null;
                if (wave != null) GUILayout.Label(NetworkWaveHUD.Format(wave.Snapshot));
            }

            bool canStop = service.State != KcpLocalNetworkState.Disabled &&
                (NetworkServer.active || NetworkClient.active ||
                 service.State == KcpLocalNetworkState.Stopping ||
                 service.State == KcpLocalNetworkState.Connecting ||
                 service.State == KcpLocalNetworkState.Hosting ||
                 service.State == KcpLocalNetworkState.Connected);
            GUI.enabled = canStop;
            if (GUILayout.Button("Stop"))
            {
                service.Stop();
            }
            GUI.enabled = previousEnabled;
            GUILayout.EndArea();
        }

        private bool TryApplyInput()
        {
            inputError = string.Empty;
            if (!ushort.TryParse(
                    portText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ushort port) || port == 0)
            {
                inputError = "Port must be between 1 and 65535.";
                return false;
            }
            if (!service.TrySetConfiguration(
                    addressText,
                    port,
                    useSimulation,
                    out inputError))
            {
                return false;
            }

            addressText = service.Address;
            portText = service.Port.ToString(CultureInfo.InvariantCulture);
            return true;
        }
    }
}
