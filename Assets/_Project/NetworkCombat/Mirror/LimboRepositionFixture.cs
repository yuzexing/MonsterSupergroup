using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit rendered spatial fixture. Positions/disabled attacks/injury are test inputs,
    // never normal play or pressure evidence. Automatic offscreen handling remains production code.
    [DefaultExecutionOrder(220)]
    public sealed class LimboRepositionFixture : MonoBehaviour
    {
        private readonly Dictionary<uint, uint> initialEpochs = new Dictionary<uint, uint>();
        private readonly HashSet<uint> injured = new HashSet<uint>();
        private readonly HashSet<int> captured = new HashSet<int>();
        private string run;
        private int sample = -1;
        private Vector2 anchor;
        private bool anchored;
        private GameObject placementBlock;
        public static string Case => LimboReferenceLaunch.Argument("--limbo-reposition-case=") ?? "timeout";
        public static string RulesName => Case == "expiry" ? "SpatialRepositionExpiry" : Case == "framing" ? "SpatialRepositionFraming" : "SpatialReposition";
        private void LateUpdate()
        {
            if (NetworkClient.localPlayer == null || NetworkCombatWorld.Instance == null) return;
            var progress = NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot;
            if (string.IsNullOrEmpty(progress.RunId) || progress.Elapsed < 1 || BootGameplayNetworkManager.CombatHasEnded) return;
            if (run != progress.RunId) { run = progress.RunId; initialEpochs.Clear(); injured.Clear(); captured.Clear(); sample = -1; anchored = false; }
            var local = NetworkClient.localPlayer;
            if (!anchored) { anchor = local.transform.position; anchored = true; }
            local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
            local.GetComponent<PlayerMovement>().StopMovement();
            var localPosition = anchor + (NetworkServer.active ? Vector2.zero : Vector2.right * 30);
            local.transform.position = localPosition; local.GetComponent<Rigidbody2D>().position = localPosition;
            var world = NetworkEnemySimulationWorld.Instance;
            if (Case == "placement-failure" && NetworkServer.active)
            {
                if (progress.Elapsed >= 38 && progress.Elapsed < 43 && placementBlock == null)
                {
                    placementBlock = new GameObject("Explicit placement-failure fixture obstacle");
                    placementBlock.layer = LayerMask.NameToLayer("Obstacles");
                    var blocker = placementBlock.AddComponent<BoxCollider2D>(); blocker.size = Vector2.one * 200;
                    blocker.excludeLayers = ~0; // Queries see it; fixture bodies are not pushed by it.
                    Physics2D.SyncTransforms();
                }
                if (progress.Elapsed >= 43 && placementBlock != null) { Destroy(placementBlock); placementBlock = null; }
            }
            foreach (var enemy in FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None))
            {
                if (!enemy.Birth.Enabled || !enemy.ProductEnemyInitialized || !enemy.IsCanonicalAlive) continue;
                var controller = enemy.GetComponent<EnemyController>();
                controller.enabled = false; controller.Movement?.StopMovement();
                if (!initialEpochs.ContainsKey(enemy.netId))
                {
                    if (NetworkServer.active)
                    {
                        var remote = NetworkServer.connections.Values.Select(c => c.identity).FirstOrDefault(i => i != null && i.netId != local.netId);
                        if (remote == null) continue;
                        world.AssignRepositionFixtureSimulator(enemy, local.netId,
                            LimboReferenceLaunch.Argument("--limbo-fixture-target=") == "client" ? remote.netId : 0);
                    }
                    initialEpochs[enemy.netId] = enemy.Assignment.Epoch;
                }
                if (progress.Elapsed < 5) initialEpochs[enemy.netId] = enemy.Assignment.Epoch;
                if (NetworkServer.active && progress.Elapsed >= 3 && injured.Add(enemy.netId))
                    NetworkCombatWorld.Instance.ApplyRepositionFixtureInjury(enemy.netId, local.netId);
                if (enemy.Authority.RunsNavigation && ((Case != "distance" && progress.Elapsed < 35) || enemy.Assignment.Epoch == initialEpochs[enemy.netId]))
                {
                    if (!NetworkClient.spawned.TryGetValue(enemy.Assignment.AggroTargetPlayerId, out var target)) continue;
                    Vector2 origin = target.transform.position;
                    Vector2 offset = progress.Elapsed < 30 ? Vector2.right * 30 :
                        progress.Elapsed < 33 ? new Vector2(15, 18) : progress.Elapsed < 35 ? Vector2.zero : new Vector2(15, 18);
                    if (Case == "distance" && progress.Elapsed >= 28) offset = new Vector2(15, 60);
                    if (Case == "framing" && progress.Elapsed >= 30) offset = new Vector2(15, 15);
                    Vector2 position = origin + offset + (enemy.Birth.SourceEnemy == "Elite_Skeleton" ? Vector2.right : Vector2.zero);
                    enemy.transform.position = position; var body = enemy.GetComponent<Rigidbody2D>(); body.position = position; body.linearVelocity = Vector2.zero;
                }
            }
            int next = (int)(Time.realtimeSinceStartup * 4);
            if (next == sample) return; sample = next;
            foreach (var enemy in FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Where(e => e.Birth.Enabled && e.ProductEnemyInitialized))
            {
                var row = new Sample { run = run, role = LimboReferenceLaunch.Argument("--limbo-role="), elapsed = progress.Elapsed, combat = EnemySimulationClock.CombatNow,
                    id = enemy.netId, source = enemy.Birth.SourceEnemy, epoch = enemy.Assignment.Epoch, reset = enemy.ReferenceResetVersion,
                    position = enemy.transform.position, hp = enemy.GetComponent<CombatantBehaviour>().CurrentHealth,
                    speed = enemy.GetComponent<EnemyController>().stats.Speed, simulator = enemy.Assignment.Host.ToString(),
                    fixture = Case, view = GameplayCameraGeometry.ViewBounds(FindFirstObjectByType<GameplayCameraRig>().GameCamera),
                    models = NetworkServer.active ? FindFirstObjectByType<NetworkGameplayEnemySpawner>().CaptureRepositionFixtureViews() : null };
                File.AppendAllText(Path.Combine(LimboReferenceLaunch.OutputDirectory, "reposition-fixture.jsonl"), JsonUtility.ToJson(row) + "\n");
            }
            int second = (int)progress.Elapsed;
            if ((second == 29 || second == 34 || second == 43 || (Case == "framing" && (second == 32 || second == 33 || second == 35))) && captured.Add(second))
                ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory, $"reposition-{run}-{second}.png"));
        }
        private void OnDestroy() { if (placementBlock != null) Destroy(placementBlock); }
        [Serializable] private class Sample { public string run, role, source, simulator, fixture; public double elapsed, combat; public uint id, epoch, reset; public Vector2 position; public int hp; public float speed; public Bounds view; public FixtureView[] models; }
    }

    [Serializable] public struct FixtureView { public uint player; public Bounds view; }
    public sealed partial class NetworkGameplayEnemySpawner
    {
        internal FixtureView[] CaptureRepositionFixtureViews() => activeParticipants.Select(p => new FixtureView { player = p.AvatarId, view = ReferenceView(p) }).ToArray();
    }

    public sealed partial class NetworkEnemySimulationWorld
    {
        internal void AssignRepositionFixtureSimulator(NetworkEnemySimulationAgent enemy, uint target, uint owner)
        {
            if (!NetworkServer.active || LimboReferenceLaunch.Profile != "spatial-reposition") return;
            var assignment = owner == 0 ? Registry.AssignServerAuthoritative(enemy.netId, target) : Registry.AssignClientOwner(enemy.netId, owner, target);
            // Publish also updates the existing handoff acknowledgement/timeout state.
            PublishHandoff(enemy, assignment, EnemyTargetChangeReason.Forced);
        }
    }

    public sealed partial class NetworkCombatWorld
    {
        internal void ApplyRepositionFixtureInjury(uint enemy, uint source)
        {
            if (!NetworkServer.active || LimboReferenceLaunch.Profile != "spatial-reposition") return;
            var result = Gateway.Ledger.ApplyServerStatusDamage(enemy, 10, 1, source);
            if (result.Accepted) Broadcast(Gateway.CreateEntityUpdate(result.State));
        }
    }
}
