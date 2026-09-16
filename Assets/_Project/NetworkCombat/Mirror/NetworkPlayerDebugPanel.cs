using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerDebugPanel : MonoBehaviour
    {
        private const double RefreshInterval = 0.2;
        [SerializeField] private bool expanded = true;
        [SerializeField] private CardPickMenu cardPickMenu;
        private readonly List<PlayerDebugSnapshot> rows = new List<PlayerDebugSnapshot>();
        private IReadOnlyList<PlayerDebugSnapshot> readOnlyRows;
        private NetworkCombatWorld world;
        private RunSession session;
        private NetworkConnectionToServer connection;
        private uint localAvatar;
        private ulong selectedParticipant;
        private bool clientSelectionInitialized;
        private double nextRefresh;
        private Vector2 scroll;
        private GUIStyle summaryStyle, detailStyle;

        public IReadOnlyList<PlayerDebugSnapshot> Rows => readOnlyRows ??= rows.AsReadOnly();
        public bool Expanded => expanded;
        public ulong SelectedParticipant => selectedParticipant;
        public bool AreDetailsVisible => expanded && (cardPickMenu == null || !cardPickMenu.IsOpen);
        public string ConnectionText { get; private set; } = "Waiting for connection";

        private void OnEnable()
        {
            if (LimboReferenceLaunch.Manual) { enabled = false; return; }
            if (GameplayRuntimeEnvironment.IsDedicatedServer) { enabled = false; return; }
            ClearBinding();
        }

        private void Update()
        {
            if (!BootGameplayNetworkManager.CombatHasEnded && !MonsterSupergroup.Gameplay.Combat.GameplayMenuInput.IsOpen && Application.isFocused && Input.GetKeyDown(KeyCode.F2)) SetExpanded(!expanded);
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            NetworkCombatWorld current = NetworkClient.isConnected ? NetworkCombatWorld.Instance : null;
            if (current != null && !current.isActiveAndEnabled) current = null;
            RunSession currentSession = NetworkServer.active ? manager?.Session : null;
            var currentConnection = NetworkClient.active ? NetworkClient.connection : null;
            uint avatar = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0;
            if (!ReferenceEquals(world, current) || !ReferenceEquals(session, currentSession) ||
                !ReferenceEquals(connection, currentConnection))
            {
                ClearBinding(); world = current; session = currentSession; connection = currentConnection;
            }
            if (localAvatar != avatar) { localAvatar = avatar; clientSelectionInitialized = false; nextRefresh = 0; }
            if (Time.unscaledTimeAsDouble < nextRefresh) return;
            RefreshRows();
            nextRefresh = Time.unscaledTimeAsDouble + RefreshInterval;
        }

        private void RefreshRows()
        {
            rows.Clear();
            bool serverView = NetworkServer.active;
            ConnectionText = $"{(serverView ? "Host / server records" : "Client / self")} | Connected: {NetworkClient.isConnected}";
            if (world == null || !NetworkClient.isConnected) return;
            double now = NetworkTime.time;
            if (serverView)
            {
                if (session == null) { ConnectionText += " | Waiting for run session"; return; }
                RuntimeDB database = null;
                foreach (var participant in session.Participants)
                    if (NetworkServer.spawned.TryGetValue(participant.AvatarId, out var avatar) && avatar != null &&
                        avatar.TryGetComponent(out PlayerBuildRuntime build) && build.BuildDatabase != null)
                    { database = build.BuildDatabase; break; }
                foreach (var participant in session.Participants)
                {
                    if (participant.ConnectionState == RunConnectionState.Disconnected)
                    {
                        int level = participant.Checkpoint?.Progression?.Level ?? 1;
                        int required = NetworkExperienceWorld.Current?.Parameters?.Threshold(level) ?? 0;
                        rows.Add(PlayerDebugSnapshotReader.ReadOffline(participant, database, required));
                    }
                    else
                    {
                        NetworkServer.spawned.TryGetValue(participant.AvatarId, out var avatar);
                        rows.Add(PlayerDebugSnapshotReader.ReadLive(avatar, participant, world, true, localAvatar, now));
                    }
                }
                rows.Sort((a, b) => a.ParticipantId.CompareTo(b.ParticipantId));
            }
            else
            {
                var avatar = NetworkClient.localPlayer;
                if (avatar == null || !avatar.isOwned || !avatar.gameObject.activeInHierarchy) return;
                var row = PlayerDebugSnapshotReader.ReadLive(avatar, null, world, false, localAvatar, now);
                rows.Add(row);
                if (!clientSelectionInitialized && row.ParticipantId != 0)
                { selectedParticipant = row.ParticipantId; clientSelectionInitialized = true; }
            }
        }

        public void SetExpanded(bool value) { expanded = value; if (value) nextRefresh = 0; }

        public void SelectParticipant(ulong id)
        {
            selectedParticipant = selectedParticipant == id ? 0 : id;
            scroll = Vector2.zero;
        }

        private void ClearBinding()
        {
            rows.Clear(); world = null; session = null; connection = null; localAvatar = 0;
            selectedParticipant = 0; clientSelectionInitialized = false; nextRefresh = 0; scroll = Vector2.zero;
            ConnectionText = "Waiting for connection";
        }

        private void OnDisable() => ClearBinding();

        public static Rect CalculateArea(int screenWidth, int screenHeight, bool showContent)
        {
            float margin = Mathf.Min(12, Mathf.Min(screenWidth, screenHeight) * 0.02f);
            float width = Mathf.Min(520, Mathf.Max(0, screenWidth - margin * 2));
            float height = showContent ? Mathf.Min(screenHeight * 0.6f, Mathf.Max(0, screenHeight - margin * 2)) : 32;
            return new Rect(margin, Mathf.Max(margin, screenHeight - margin - height), width, height);
        }

        private void OnGUI()
        {
            if (BootGameplayNetworkManager.CombatHasEnded || MonsterSupergroup.Gameplay.Combat.GameplayMenuInput.IsOpen) return;
            if (!isActiveAndEnabled) return;
            if (NetworkManager.singleton is BootGameplayNetworkManager manager && manager.UsePreparationRoom &&
                manager.RoomSnapshot.Phase != PreparationPhase.InGame) return;
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.08f, 0.1f, 0.14f, 0.88f);
            GUILayout.BeginArea(CalculateArea(Screen.width, Screen.height, expanded), GUI.skin.box);
            GUI.backgroundColor = previous;
            if (GUILayout.Button($"Player Debug [F2] - {(expanded ? "Hide" : "Show")}")) SetExpanded(!expanded);
            if (expanded)
            {
                GUILayout.Label(ConnectionText);
                if (!AreDetailsVisible) GUILayout.Label("Selection open - summaries only");
                summaryStyle ??= new GUIStyle(GUI.skin.button) { alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
                detailStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
                scroll = GUILayout.BeginScrollView(scroll);
                if (rows.Count == 0) GUILayout.Label("Waiting for player / combat world");
                // All summaries remain together above the selected detail, even with many participants.
                foreach (var row in rows)
                    if (GUILayout.Button(row.Summary, summaryStyle)) SelectParticipant(row.ParticipantId);
                if (AreDetailsVisible)
                    foreach (var row in rows)
                        if (selectedParticipant != 0 && row.ParticipantId == selectedParticipant)
                        { GUILayout.Space(6); GUILayout.Label(row.Details, detailStyle); break; }
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }
    }
}
