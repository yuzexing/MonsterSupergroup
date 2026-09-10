using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Test coordination uses files; attacks, results and replicas use the production Mirror paths.
    [DefaultExecutionOrder(-31900)]
    public sealed class MeleeAttackProcessProbe : MonoBehaviour
    {
        private string role, directory;
        private ushort port;
        private float deadline;
        private bool finished;
        private BootGameplayNetworkManager manager;
        private MeleeProcessTarget target;
        private NetworkWeaponCombatAdapter firstRemoteAdapter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--melee-role="));
            if (value == null || FindFirstObjectByType<MeleeAttackProcessProbe>() != null) return;
            var probe = new GameObject("Melee Process Validation").AddComponent<MeleeAttackProcessProbe>();
            probe.role = value.Split('=')[1];
            probe.directory = args.First(a => a.StartsWith("--melee-sync=")).Substring("--melee-sync=".Length);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--melee-port=")).Split('=')[1]);
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 100;
            SceneManager.sceneLoaded += PrepareGameplay;
            yield return Guard(Run(), () => Finish(true));
        }

        private IEnumerator Guard(IEnumerator routine, Action completed = null)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0 && !finished)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    Finish(false);
                    yield break;
                }
                yield return next;
            }
            if (!finished) completed?.Invoke();
        }

        private void Update()
        {
            if (finished) return;
            if (NetworkClient.localPlayer != null)
                NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().SetWeaponExecutionEnabled(false);
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogError("[MeleeProcess] Timed out in role " + role);
                Finish(false);
            }
        }

        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    { spawner.Configure(spawner.EnemyPrefab, 5); spawner.enabled = false; }
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Boot manager missing.");
            var database = FindFirstObjectByType<RuntimeDB>();
            Require(database != null && database.TryGetWeaponData(1, out var melee), "Migrated melee is absent from the weapon database.");
            // Only the test's in-memory definition changes: deterministic three-slash damage and a long cooldown.
            var weapons = Instantiate(database.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != 1) return definition;
                var copy = Instantiate(definition);
                var stats = copy.BaseStats;
                stats.critRate = 0;
                stats.projectileCount = 3;
                stats.speed = 1f / 30;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weapons);
            manager.playerPrefab.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(1);
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out var error), error);
            if (role == "host") manager.StartHost();
            else if (role == "server") manager.StartServer();
            else manager.StartClient();
            if (role != "server") StartCoroutine(Guard(VerifyOwner()));
            if (role == "host" || role == "server")
            {
                Mark("listening");
                yield return VerifyServer();
                VerifyRemoteDisconnectCleanup();
                if (role == "host") manager.StopHost(); else manager.StopServer();
            }
            else
            {
                if (role == "client") yield return DisconnectDuringBurstAndReconnect();
                while (!Has("stop")) yield return null;
                VerifyRemoteDisconnectCleanup();
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active)
                yield return null;
            yield return null;
            Require(FindObjectsByType<NetworkPlayerBootstrap>(FindObjectsSortMode.None).Length == 0, "Avatar survived scene shutdown.");
            Require(!FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Any(a => a.gameObject.activeInHierarchy), "Active slash survived scene shutdown.");
            Mark("stopped-" + role);
        }

        private NetworkIdentity[] ServerPlayers() => NetworkServer.spawned.Values
            .Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();

        private IEnumerator VerifyServer()
        {
            while (ServerPlayers().Length != 2 || ServerPlayers().Any(p => !p.GetComponent<PlayerBuildRuntime>().IsBuildActive)) yield return null;
            var players = ServerPlayers();
            manager.BeginRun();
            foreach (var player in players)
            {
                Require(player.GetComponent<PlayerBuildRuntime>().InitialWeapon is MeleeAttackBehaviour, "Server Build has the wrong weapon family.");
                NetworkCombatWorld.Instance.Gateway.Ledger.RegisterEntity(TargetId(player.netId), 1000,
                    CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            }
            if (role == "server")
            {
                Require(!NetworkClient.active, "Server-only process became a client.");
                Require(FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Length == 0, "Server-only process created attack visuals.");
            }
            Mark("fire");
            while (!Has("verified-client") || !Has(role == "host" ? "verified-host" : "verified-client2")) yield return null;
            foreach (var player in players)
            {
                var gateway = NetworkCombatWorld.Instance.Gateway;
                while (!gateway.Ledger.TryGetState(TargetId(player.netId), out var state) || state.Health != 943 || gateway.Attacks.ActiveCount(player.netId) != 0)
                    yield return null;
                var adapter = player.GetComponent<NetworkWeaponCombatAdapter>();
                Require(adapter.AcceptedCooldownReportCount == 1 && adapter.RejectedAttackCount == 0, "Three slashes must use one admitted root.");
                Require(adapter.RejectedPresentationCount == 0, "Server rejected a legal slash edge.");
                Debug.Log($"[MeleeProcess] canonical player={player.netId} hp=943 roots=0 admitted=1");
            }
            var returning = players.OrderBy(p => p.GetComponent<NetworkRunParticipant>().ParticipantId)
                .First(p => p.connectionToClient is not LocalConnectionToClient);
            uint oldAvatar = returning.netId;
            ulong participantId = returning.GetComponent<NetworkRunParticipant>().ParticipantId;
            // Use a second genuinely legal root, then disconnect while its first slash is alive.
            double readyAt = returning.GetComponent<NetworkWeaponCombatAdapter>().CaptureCooldowns().Single().ReadyAt;
            while (NetworkTime.time < readyAt + 0.1) yield return null;
            Mark("cancel-burst");
            while (returning.GetComponent<NetworkWeaponCombatAdapter>().AcceptedCooldownReportCount < 2) yield return null;
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(oldAvatar) == 1, "Second root must be admitted and still alive before disconnect.");
            Mark("burst-admitted");
            while (!Has("disconnected-client") || NetworkServer.spawned.ContainsKey(oldAvatar)) yield return null;
            Require(!NetworkCombatWorld.Instance.Gateway.Attacks.RequiresAdmission(oldAvatar), "Disconnected avatar retained attack admission roots.");
            var retained = manager.Session.Participants.Single(p => p.Id == participantId);
            Require(retained.Checkpoint != null && retained.Checkpoint.WeaponCooldowns.Single().SequenceSeconds > 0.29f,
                "Disconnect during the burst lost its frozen launch duration.");
            Mark("reconnect");
            while (!Has("resumed-client")) yield return null;
            Require(retained.AvatarId != 0 && retained.AvatarId != oldAvatar, "Reconnect reused the old Avatar.");
            var restored = NetworkServer.spawned[retained.AvatarId].GetComponent<PlayerBuildRuntime>();
            Require(restored.InitialWeapon is MeleeAttackBehaviour && restored.InitialWeapon.ProjectileCountValue == 3,
                "Reconnect did not restore the original melee Build.");
            Debug.Log($"[MeleeProcess] reconnect participant={participantId} oldAvatar={oldAvatar} newAvatar={retained.AvatarId} sequenceRetained=true");
            Mark("stop");
            while (!Has("stopped-client") || (role == "server" && !Has("stopped-client2"))) yield return null;
        }

        private IEnumerator VerifyOwner()
        {
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                   !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive || !Has("fire")) yield return null;
            var owner = NetworkClient.localPlayer;
            var build = owner.GetComponent<PlayerBuildRuntime>();
            build.SetWeaponExecutionEnabled(false);
            var weapon = build.InitialWeapon as MeleeAttackBehaviour;
            Require(weapon != null, "Owner did not receive the melee Build.");
            var targetObject = new GameObject("Melee physics target");
            targetObject.layer = LayerMask.NameToLayer("EnemyHitbox");
            targetObject.transform.position = owner.transform.position;
            targetObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var collider = targetObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 20;
            target = targetObject.AddComponent<MeleeProcessTarget>();
            target.Initialize(TargetId(owner.netId));
            weapon.Attack();
            float end = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < end) yield return null;
            Require(target.HitCount == 3 && target.TotalDamage == 57, $"Physics hits must be 3 x 19; got {target.HitCount} hits, {target.TotalDamage} damage.");
            Require(target.Roots.Count == 1, "Burst split into multiple GAS roots.");
            var remote = NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned && p.GetComponent<NetworkRunParticipant>() != null);
            Require(remote != null, "Remote Avatar missing.");
            var replica = remote.GetComponent<NetworkWeaponCombatAdapter>();
            firstRemoteAdapter = replica;
            Require(replica.ReceivedMeleePresentationCount == 6 && replica.ReplicaActiveMeleeCount == 0,
                $"Remote must replay three spawns/ends and release every slash; received={replica.ReceivedMeleePresentationCount}, active={replica.ReplicaActiveMeleeCount}.");
            Require(replica.ReplicaMeleeSpawnCount == 3 && replica.RejectedMeleePresentationCount == 0,
                $"Every slash must actually be replayed; spawned={replica.ReplicaMeleeSpawnCount}, rejected={replica.RejectedMeleePresentationCount}.");
            if (!remote.isServer) Require(!remote.GetComponent<PlayerBuildRuntime>().IsBuildActive, "Remote executes another player's GAS Build.");
            Debug.Log($"[MeleeProcess] owner={owner.netId} hits=3 damage=57 remoteEdges=6 remoteActive=0");
            Destroy(targetObject);
            Mark("verified-" + role);
        }

        private IEnumerator DisconnectDuringBurstAndReconnect()
        {
            while (!Has("cancel-burst")) yield return null;
            var oldPlayer = NetworkClient.localPlayer;
            uint oldAvatar = oldPlayer.netId;
            ushort oldEpoch = oldPlayer.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            var weapon = (MeleeAttackBehaviour)oldPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;
            weapon.Attack();
            while (!Has("burst-admitted")) yield return null;
            yield return new WaitForSeconds(0.04f);
            Require(weapon.GetComponentsInChildren<AnimatedAttack>().Length > 0, "Fixture must disconnect with an active slash.");
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(!FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Any(a => a.gameObject.activeInHierarchy),
                "Disconnect left an active owned or remote slash.");
            Mark("disconnected-client");
            while (!Has("reconnect")) yield return null;
            manager.StartClient();
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive) yield return null;
            var restored = NetworkClient.localPlayer;
            Require(restored.netId != oldAvatar && restored.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch != oldEpoch,
                "Reconnect reused the old Avatar or attack epoch.");
            var melee = restored.GetComponent<PlayerBuildRuntime>().InitialWeapon;
            Require(melee is MeleeAttackBehaviour && melee.ProjectileCountValue == 3 &&
                melee.GetCooldown() - melee.LastAttackElapsedTime > 10f, "Reconnect reset the melee Build or remaining cooldown.");
            Debug.Log($"[MeleeProcess] resumed oldAvatar={oldAvatar} newAvatar={restored.netId} remaining={melee.GetCooldown() - melee.LastAttackElapsedTime:F3}");
            Mark("resumed-client");
        }

        private void VerifyRemoteDisconnectCleanup()
        {
            if (role != "host" && role != "client2") return;
            Require(!ReferenceEquals(firstRemoteAdapter, null) && firstRemoteAdapter.ReplicaMeleeSpawnCount > 3,
                "Spectator must have replayed the disconnecting player's second attack.");
            Require(firstRemoteAdapter.ReplicaActiveMeleeCount == 0, "Disconnect left the old Avatar's slash replica active.");
        }

        private static uint TargetId(uint player) => 0x6fff0000u + player;
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log("[MeleeProcess] result=" + (passed ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(passed ? 0 : 1);
        }
    }

    public sealed class MeleeProcessTarget : MonoBehaviour, IDamageable, INativeGasDamageable
    {
        private CombatantBehaviour combatant;
        public int HitCount { get; private set; }
        public int TotalDamage { get; private set; }
        public readonly HashSet<ulong> Roots = new HashSet<ulong>();
        public void Initialize(uint id)
        {
            combatant = gameObject.AddComponent<CombatantBehaviour>();
            combatant.Initialize(1000);
            combatant.ConfigureEntityId(id);
        }
        public bool ResolveNativeGasHit(NativeGasHit hit)
        {
            var result = hit.Runtime.ResolveHitDetailed(hit.Attack, combatant);
            HitCount++;
            TotalDamage += result.ResolvedDamage.Value;
            Roots.Add(hit.Attack.Context.RootEventId.Value);
            return true;
        }
        public int GetID() => (int)combatant.EntityId;
        public Vector2 GetPosition() => transform.position;
        public bool IsActive() => combatant.IsAlive;
        public void Damage(Vector2 position, WeaponBehaviour weapon, LegacyDamageType type) => throw new InvalidOperationException("Legacy damage was invoked.");
        public void Damage(int value, LegacyDamageType type) => throw new InvalidOperationException("Legacy damage was invoked.");
    }
}
