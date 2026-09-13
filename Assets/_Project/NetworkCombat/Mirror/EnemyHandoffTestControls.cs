#if UNITY_EDITOR || MONSTER_ENEMY_HANDOFF_VALIDATION
using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Explicit opt-in mutations. The Enemy Debug panel remains read-only.</summary>
    public sealed class EnemyHandoffTestControls : MonoBehaviour
    {
        private uint enemyId;
        private bool cycle;
        private int targetIndex, frequencyIndex;
        private double nextRequest;
        private Vector2 scroll;
        private readonly List<NetworkEnemySimulationEndpoint> players = new List<NetworkEnemySimulationEndpoint>();
        private readonly int[] frequencies = { 1, 5, 20 };
        private string result = "Choose an enemy";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--enemy-handoff-controls") < 0) return;
            DontDestroyOnLoad(new GameObject("Enemy Handoff Test Controls").AddComponent<EnemyHandoffTestControls>().gameObject);
        }
#if UNITY_EDITOR

        private static void ShowInEditor()
        {
            if (Application.isPlaying && FindFirstObjectByType<EnemyHandoffTestControls>() == null)
                DontDestroyOnLoad(new GameObject("Enemy Handoff Test Controls").AddComponent<EnemyHandoffTestControls>().gameObject);
        }
#endif
        private void Update()
        {
            if (!cycle || !NetworkServer.active || NetworkTime.time < nextRequest) return;
            nextRequest = NetworkTime.time + 1d / frequencies[frequencyIndex];
            var world = NetworkEnemySimulationWorld.Instance;
            if (world == null) return;
            world.GetEligiblePlayers(players);
            players.Sort((a, b) => (a.GetComponent<NetworkRunParticipant>()?.ParticipantId ?? a.netId)
                .CompareTo(b.GetComponent<NetworkRunParticipant>()?.ParticipantId ?? b.netId));
            if (players.Count == 0) return;
            targetIndex %= players.Count;
            result = world.RequestTargetChange(enemyId, players[targetIndex].netId, EnemyTargetChangeReason.Forced).ToString();
            targetIndex++;
        }
        private void OnGUI()
        {
            if (BootGameplayNetworkManager.CombatHasEnded || MonsterSupergroup.Gameplay.Combat.GameplayMenuInput.IsOpen) return;
            if (!NetworkServer.active || !NetworkClient.active || NetworkEnemySimulationWorld.Instance == null) return;
            var world = NetworkEnemySimulationWorld.Instance;
            float width = Mathf.Clamp(Screen.width - 1080f, 200f, 350f);
            GUILayout.BeginArea(new Rect(Mathf.Max(0, (Screen.width - width) / 2), 12, width, Mathf.Min(420, Screen.height * .55f)), GUI.skin.box);
            GUILayout.Label("Host handoff controls");
            if (GUILayout.Button("Close controls")) { cycle = false; Destroy(gameObject); }
            cycle = GUILayout.Toggle(cycle, "Cycle targets");
            frequencyIndex = GUILayout.SelectionGrid(frequencyIndex, new[] { "1 Hz", "5 Hz", "20 Hz" }, 3);
            GUILayout.Label($"Enemy {enemyId} | {result}");
            world.GetEligiblePlayers(players);
            foreach (var player in players)
                if (GUILayout.Button($"Target P{player.GetComponent<NetworkRunParticipant>()?.ParticipantId} / avatar {player.netId}"))
                    result = world.RequestTargetChange(enemyId, player.netId, EnemyTargetChangeReason.Forced).ToString();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var identity in NetworkServer.spawned.Values.OrderBy(value => value.netId))
                if (identity != null && identity.TryGetComponent(out NetworkEnemySimulationAgent agent) && agent.IsCanonicalAlive &&
                    GUILayout.Button($"Enemy {identity.netId} → {agent.Assignment.AggroTargetPlayerId}")) enemyId = identity.netId;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
#endif
