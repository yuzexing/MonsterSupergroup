using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class BootGameplayNetworkManager
    {
        private readonly HashSet<int> cleanupWaiting = new HashSet<int>();
        private uint clientCleanupRound;
        private double cleanupDeadline;
        private bool cleanupRunning;
        private PreparationMember[] nextParty;
        public bool IsRunEndScreen => RoomSnapshot.Phase == PreparationPhase.GameOver ||
            RoomSnapshot.Phase == PreparationPhase.Transitioning;
        public static bool CombatHasEnded => singleton is BootGameplayNetworkManager manager &&
            (NetworkServer.active ? manager.Session.IsRunEnded : manager.IsRunEndScreen);
        private bool CanCreateGameplayAvatar => !UsePreparationRoom ||
            ServerRoom?.Phase == PreparationPhase.Loading || ServerRoom?.Phase == PreparationPhase.InGame;

        private void UpdateRunEnd()
        {
            if (!NetworkServer.active || roomStopping || ServerRoom == null) return;
            if (ServerRoom.Phase == PreparationPhase.InGame &&
                Session.TryEndRun(NetworkCombatWorld.Instance?.Gateway.Ledger))
            {
                ServerRoom.EndRun();
                pendingPlayerConnections.Clear(); remoteGameplayLoadRequests.Clear();
                NetworkCombatWorld.Instance.Gateway.StopCombat();
                if (TryGetGameplaySpawner(out var spawner, out _)) spawner.StopWaveRun();
                NetworkEnemySimulationWorld.Instance?.StopRunSimulation();
                foreach (var connection in NetworkServer.connections.Values)
                    connection.identity?.GetComponent<NetworkModifierSelection>()?.ServerCancelPending();
                PublishRoom();
                Debug.Log($"[RunEnd] ended run={Session.RunId} round={Session.Round} reason=all-online-downed");
            }
            if (cleanupRunning && Time.realtimeSinceStartupAsDouble >= cleanupDeadline)
            {
                cleanupRunning = false;
                AbortPreparation("清理超过 120 秒，请重新创建房间。");
            }
        }

        public void ChooseRunEndAction(RunEndAction action)
        {
            if (!NetworkClient.isConnected || RoomSnapshot.Phase != PreparationPhase.GameOver) return;
            NetworkClient.Send(new RequestRunEndAction { RunId = RoomSnapshot.RunId, Round = RoomSnapshot.Round, Action = action });
        }

        [Server]
        internal void CompleteReferenceStage(string reason)
        {
            if (!Session.TryCompleteStage()) return;
            ServerRoom?.EndRun(reason);
            pendingPlayerConnections.Clear(); remoteGameplayLoadRequests.Clear();
            NetworkCombatWorld.Instance.Gateway.StopCombat();
            NetworkEnemySimulationWorld.Instance?.StopRunSimulation();
            foreach (var connection in NetworkServer.connections.Values)
                connection.identity?.GetComponent<NetworkModifierSelection>()?.ServerCancelPending();
            if (ServerRoom != null) PublishRoom();
            Debug.Log("[RunEnd] reference-stage-complete " + reason);
        }

        private void ReceiveRunEndAction(NetworkConnectionToClient connection, RequestRunEndAction request)
        {
            if (!RequestActor(connection, request.RunId, out ulong id) || request.Round != Session.Round) return;
            if (!ServerRoom.RequestEndAction(id, request.Action, out var error)) { Notify(connection, error); return; }
            nextParty = ServerRoom.Launch.Members.Where(m => Session.Participants.Any(p =>
                p.Id == m.ParticipantId && p.ConnectionState == RunConnectionState.Connected)).ToArray();
            cleanupRunning = true; cleanupDeadline = Time.realtimeSinceStartupAsDouble + 120;
            cleanupWaiting.Clear();
            ++serverSceneGeneration; pendingPlayerConnections.Clear(); remoteGameplayLoadRequests.Clear();
            PublishRoom();
            // Remove avatars through Mirror before unloading their scene. Authentication stays intact.
            foreach (var client in NetworkServer.connections.Values.ToArray())
            {
                if (!client.isAuthenticated) { client.Disconnect(); continue; }
                cleanupWaiting.Add(client.connectionId);
                if (client.identity != null) NetworkServer.RemovePlayerForConnection(client, RemovePlayerOptions.Destroy);
            }
            if (TryGetGameplayScene(out var scene))
                foreach (var identity in NetworkServer.spawned.Values.ToArray())
                    if (identity != null && identity.gameObject.scene == scene) NetworkServer.Destroy(identity.gameObject);
            foreach (var client in NetworkServer.connections.Values.ToArray())
                if (client.isAuthenticated)
                    client.Send(new RunCleanupRequest { RunId = Session.RunId, Round = Session.Round });
            Debug.Log($"[RunEnd] cleanup run={Session.RunId} round={Session.Round} action={request.Action} peers={cleanupWaiting.Count}");
            StartCoroutine(CompleteRoundTransition(serverSceneGeneration, request.Action));
        }

        private void ReceiveCleanupRequest(RunCleanupRequest request)
        {
            if (request.RunId != RoomSnapshot.RunId || request.Round != RoomSnapshot.Round ||
                RoomSnapshot.Phase != PreparationPhase.Transitioning || clientCleanupRound == request.Round) return;
            clientCleanupRound = request.Round;
            ++clientSceneGeneration; readyAvatarSent = 0;
            StartCoroutine(CleanupLocalRound(request));
        }

        private IEnumerator CleanupLocalRound(RunCleanupRequest request)
        {
            // The Host shares its scene with the server; this is the single local unload owner.
            BeginGameplayUnload();
            while (IsGameplayTransitioning || IsGameplayLoaded)
            {
                if (!NetworkClient.isConnected || clientCleanupRound != request.Round) yield break;
                yield return null;
            }
            NetworkCombatWorld.Instance?.ResetClientRound();
            NetworkEnemySimulationWorld.Instance?.ResetClientRound();
            if (NetworkClient.isConnected && RoomSnapshot.RunId == request.RunId)
                NetworkClient.Send(new RunCleanupReady { RunId = request.RunId, Round = request.Round });
        }

        private void ReceiveCleanupReady(NetworkConnectionToClient connection, RunCleanupReady ready)
        {
            if (!cleanupRunning || !RequestActor(connection, ready.RunId, out _) || ready.Round != Session.Round ||
                ServerRoom.Phase != PreparationPhase.Transitioning) return;
            cleanupWaiting.Remove(connection.connectionId);
        }

        private IEnumerator CompleteRoundTransition(uint generation, RunEndAction action)
        {
            while (cleanupRunning && !roomStopping && IsCurrentServerGeneration(generation))
            {
                cleanupWaiting.RemoveWhere(id => !NetworkServer.connections.ContainsKey(id));
                if (cleanupWaiting.Count == 0 && !IsGameplayLoaded && !IsGameplayTransitioning) break;
                yield return null;
            }
            if (!cleanupRunning || roomStopping || !IsCurrentServerGeneration(generation)) yield break;
            cleanupRunning = false;
            var party = nextParty.Where(m => Session.Participants.Any(p => p.Id == m.ParticipantId &&
                p.ConnectionState == RunConnectionState.Connected)).ToArray();
            nextParty = null;
            try
            {
                Session.BeginNextRound();
                NetworkCombatWorld.Instance.ResetServerRound();
                NetworkEnemySimulationWorld.Instance.ResetServerRound();
                NetworkExperienceWorld.Current.ResetForNextRun();
                NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>()?.ResetForNextRun();
                ServerRoom = new PreparationRoom(Session.RunId, gameplayScene, NetworkServer.listen,
                    PreparationMenuCatalog.Load().AllowedWeaponIds, Session.Round);
                ServerRoom.RestoreParty(party, action == RunEndAction.Restart);
                MenuNotice = string.Empty; readyAvatarSent = 0;
                PublishRoom();
                Debug.Log($"[RunEnd] next run={Session.RunId} round={Session.Round} action={action} members={party.Length}");
                if (action == RunEndAction.Restart) LaunchGameplay();
            }
            catch (System.Exception error)
            {
                Debug.LogError("[RunEnd] Failed to initialize next round: " + error);
                AbortPreparation("换局失败，请重新创建房间。");
            }
        }

        private void ResetRunEndFlow()
        {
            cleanupRunning = false; cleanupWaiting.Clear(); nextParty = null; clientCleanupRound = 0;
        }
    }
}
