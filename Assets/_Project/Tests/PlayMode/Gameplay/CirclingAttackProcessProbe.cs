using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    // Files coordinate independent processes; roots, damage and orbit replicas use production Mirror paths.
    [DefaultExecutionOrder(-31900)]
    public sealed class CirclingAttackProcessProbe : MonoBehaviour
    {
        private static readonly PropertyInfo IsPresentationOnly = typeof(BasePlayerAttack)
            .GetProperty("IsPresentationOnly", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo NativeAttackSnapshot = typeof(BasePlayerAttack)
            .GetProperty("NativeAttackSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        private string role, directory;
        private ushort port;
        private float deadline;
        private bool finished, remoteOrbitMoved;
        private readonly Dictionary<int, Vector3> firstRemoteOrbPositions = new Dictionary<int, Vector3>();
        private BootGameplayNetworkManager manager;
        private CirclingAttackBehaviour ownedCircling;
        private NetworkWeaponCombatAdapter firstRemoteAdapter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--circling-role="));
            if (value == null || FindFirstObjectByType<CirclingAttackProcessProbe>() != null) return;
            var probe = new GameObject("Circling Process Validation").AddComponent<CirclingAttackProcessProbe>();
            probe.role = value.Split('=')[1];
            probe.directory = args.First(a => a.StartsWith("--circling-sync=")).Substring("--circling-sync=".Length);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--circling-port=")).Split('=')[1]);
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 160;
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
            try
            {
                if (NetworkClient.localPlayer != null)
                    NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().SetWeaponExecutionEnabled(false);
                ObserveRemoteCircling();
                if (Time.realtimeSinceStartup > deadline)
                    throw new InvalidOperationException("Timed out in role " + role);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(false);
            }
        }

        private void ObserveRemoteCircling()
        {
            if (!NetworkClient.active) return;
            var remote = NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned &&
                p.GetComponent<NetworkRunParticipant>() != null);
            if (remote == null) return;
            var adapter = remote.GetComponent<NetworkWeaponCombatAdapter>();
            if (ReferenceEquals(firstRemoteAdapter, null)) firstRemoteAdapter = adapter;
            foreach (AnimatedAttack orb in remote.GetComponentsInChildren<AnimatedAttack>()
                .Where(a => a.gameObject.activeInHierarchy && a.GetComponentInParent<CirclingAttackBehaviour>() != null))
            {
                Require((bool)IsPresentationOnly.GetValue(orb) && NativeAttackSnapshot.GetValue(orb) == null,
                    "Remote orbit acquired an executing GAS snapshot.");
                int instanceId = orb.GetInstanceID();
                Vector3 position = orb.transform.localPosition;
                if (!firstRemoteOrbPositions.TryGetValue(instanceId, out Vector3 first))
                    firstRemoteOrbPositions.Add(instanceId, position);
                else if (Vector3.Distance(first, position) > 0.02f) remoteOrbitMoved = true;
            }
            if ((role == "host" || role == "client2") && adapter.ReplicaOrbSpawnCount >= 4 && adapter.ReplicaActiveOrbCount == 2)
                Mark("spectator-sees-second-" + role);
        }

        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    spawner.enabled = false;
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Boot manager missing.");
            var database = FindFirstObjectByType<RuntimeDB>();
            Require(database != null && database.TryGetWeaponData(6, out var circling), "Migrated circling is absent from the weapon database.");
            var weapons = Instantiate(database.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != 6) return definition;
                var copy = Instantiate(definition);
                var stats = copy.BaseStats;
                stats.damage = 12;
                stats.critRate = 0;
                stats.projectileCount = 2;
                stats.duration = 1.2f;
                stats.speed = 1f / 30;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weapons);
            manager.playerPrefab.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(6);
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
                if (role == "client") yield return DisconnectDuringCirclingAndReconnect();
                while (!Has("stop")) yield return null;
                VerifyRemoteDisconnectCleanup();
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active)
                yield return null;
            yield return null;
            Require(FindObjectsByType<NetworkPlayerBootstrap>(FindObjectsSortMode.None).Length == 0, "Avatar survived scene shutdown.");
            Require(!FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Any(a => a.gameObject.activeInHierarchy), "Active circling survived scene shutdown.");
            Mark("stopped-" + role);
        }

        private NetworkIdentity[] ServerPlayers() => NetworkServer.spawned.Values
            .Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();

        private IEnumerator VerifyServer()
        {
            while (ServerPlayers().Length != 2 || ServerPlayers().Any(p => !p.GetComponent<PlayerBuildRuntime>().IsBuildActive)) yield return null;
            NetworkIdentity[] players = ServerPlayers();
            manager.BeginRun();
            foreach (NetworkIdentity player in players)
            {
                Require(player.GetComponent<PlayerBuildRuntime>().InitialWeapon is CirclingAttackBehaviour, "Server Build has the wrong weapon family.");
                NetworkCombatWorld.Instance.Gateway.Ledger.RegisterEntity(TargetId(player.netId), 1000,
                    CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            }
            VerifyServerHasNoVisuals();
            while (!Has("owner-ready-client") || !Has(role == "host" ? "owner-ready-host" : "owner-ready-client2")) yield return null;
            Mark("fire");
            while (!Has("verified-client") || !Has(role == "host" ? "verified-host" : "verified-client2")) yield return null;
            foreach (NetworkIdentity player in players)
            {
                string[] result = File.ReadAllText(Path.Combine(directory, "owner-result-" + player.netId)).Split(',');
                int hits = int.Parse(result[0]);
                int damage = int.Parse(result[1]);
                Require(hits >= 4 && damage == hits * 12, "Owner did not observe repeat physics hits of twelve damage from both orbs.");
                var gateway = NetworkCombatWorld.Instance.Gateway;
                while (!gateway.Ledger.TryGetState(TargetId(player.netId), out var state) || state.Health != 1000 - damage ||
                       gateway.Attacks.ActiveCount(player.netId) != 0) yield return null;
                var adapter = player.GetComponent<NetworkWeaponCombatAdapter>();
                Require(adapter.AcceptedCooldownReportCount == 1 && adapter.RejectedAttackCount == 0, "Continuous circling hits must share one admitted root.");
                Require(adapter.RejectedOrbitPresentationCount == 0, "Server rejected a legal orbit edge.");
                Debug.Log($"[CirclingProcess] canonical player={player.netId} hits={hits} damage={damage} hp={1000 - damage} roots=0 admitted=1");
            }
            VerifyServerHasNoVisuals();
            NetworkIdentity returning = players.OrderBy(p => p.GetComponent<NetworkRunParticipant>().ParticipantId)
                .First(p => p.connectionToClient is not LocalConnectionToClient);
            uint oldAvatar = returning.netId;
            ulong participantId = returning.GetComponent<NetworkRunParticipant>().ParticipantId;
            double readyAt = returning.GetComponent<NetworkWeaponCombatAdapter>().CaptureCooldowns().Single().ReadyAt;
            while (NetworkTime.time < readyAt + 0.1) yield return null;
            Mark("cancel-circling");
            while (returning.GetComponent<NetworkWeaponCombatAdapter>().AcceptedCooldownReportCount < 2) yield return null;
            while (!Has("spectator-sees-second-" + (role == "host" ? "host" : "client2"))) yield return null;
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(oldAvatar) == 1, "Second circling must still be active when its spectator sees it.");
            VerifyServerHasNoVisuals();
            Mark("circling-admitted");
            while (!Has("disconnected-client") || NetworkServer.spawned.ContainsKey(oldAvatar)) yield return null;
            Require(!NetworkCombatWorld.Instance.Gateway.Attacks.RequiresAdmission(oldAvatar), "Disconnected avatar retained attack admission roots.");
            var retained = manager.Session.Participants.Single(p => p.Id == participantId);
            Require(retained.Checkpoint != null && retained.Checkpoint.WeaponCooldowns.Single().SequenceSeconds > 1.1f,
                "Disconnect during the circling lost its frozen duration before cooldown.");
            Mark("reconnect");
            while (!Has("resumed-client")) yield return null;
            Require(retained.AvatarId != 0 && retained.AvatarId != oldAvatar, "Reconnect reused the old Avatar.");
            PlayerBuildRuntime restored = NetworkServer.spawned[retained.AvatarId].GetComponent<PlayerBuildRuntime>();
            Require(restored.InitialWeapon is CirclingAttackBehaviour && restored.InitialWeapon.ProjectileCountValue == 2,
                "Reconnect did not restore the original circling Build.");
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(retained.AvatarId) == 0,
                "Reconnect replayed an old circling root.");
            Debug.Log($"[CirclingProcess] reconnect participant={participantId} oldAvatar={oldAvatar} newAvatar={retained.AvatarId} sequenceRetained=true");
            Mark("stop");
            while (!Has("stopped-client") || (role == "server" && !Has("stopped-client2"))) yield return null;
        }

        private IEnumerator VerifyOwner()
        {
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                   !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive) yield return null;
            NetworkIdentity owner = NetworkClient.localPlayer;
            PlayerBuildRuntime build = owner.GetComponent<PlayerBuildRuntime>();
            build.SetWeaponExecutionEnabled(false);
            ownedCircling = build.InitialWeapon as CirclingAttackBehaviour;
            Require(ownedCircling != null, "Owner did not receive the circling Build.");
            Mark("owner-ready-" + role);
            while (!Has("fire")) yield return null;
            var targetObject = new GameObject("Circling physics target");
            targetObject.layer = LayerMask.NameToLayer("EnemyHitbox");
            targetObject.transform.position = owner.transform.position;
            targetObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var collider = targetObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 30;
            var target = targetObject.AddComponent<CirclingProcessTarget>();
            target.Initialize(TargetId(owner.netId));
            var sourceEnds = new List<OrbitPresentationTermination>();
            Action<OrbitPresentationTermination> observeEnd = sourceEnds.Add;
            ownedCircling.PresentationTerminated += observeEnd;
            try
            {
                StartOwnedCircling();
                float observeUntil = Time.realtimeSinceStartup + ownedCircling.GetAttackSequenceDuration() + 5f;
                while ((sourceEnds.Count < 2 || ownedCircling.ActiveOrbCount > 0) && Time.realtimeSinceStartup < observeUntil)
                    yield return null;
            }
            finally { ownedCircling.PresentationTerminated -= observeEnd; }
            Require(target.HitCount >= 4 && target.TotalDamage == target.HitCount * 12,
                $"Physics must produce repeat hits of twelve damage; got {target.HitCount} hits, {target.TotalDamage} damage.");
            Require(target.OrbHitCounts.Count == 2 && target.OrbHitCounts.Values.All(count => count >= 2),
                "Both authored orbs must produce repeat real physics hits.");
            Require(target.UnattributedHitCount == 0, "A hit did not originate at a live authored orb.");
            Require(target.Roots.Count == 1 && target.HitEvents.Count == target.HitCount,
                "A circling must retain one GAS root and create a distinct event for each overtime hit.");
            Require(sourceEnds.Count == 2 && sourceEnds.Select(edge => edge.Key).Distinct().Count() == 2 &&
                    sourceEnds.All(edge => edge.WeaponId == 6u && target.Roots.Contains(edge.Key.AttackEventId)),
                "Both source orbs must terminate exactly once under the original GAS root.");
            Require(target.SourcePlayers.SetEquals(new[] { owner.netId }), "Circling damage used another owner's runtime.");
            var adapter = owner.GetComponent<NetworkWeaponCombatAdapter>();
            var remote = NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned && p.GetComponent<NetworkRunParticipant>() != null);
            Require(remote != null, "Remote Avatar missing.");
            var replica = remote.GetComponent<NetworkWeaponCombatAdapter>();
            float deliveryDeadline = Time.realtimeSinceStartup + 5f;
            while ((adapter.SentOrbitPresentationCount < 6 || replica.ReceivedOrbitPresentationCount < 6 ||
                    replica.ReplicaActiveOrbCount > 0) && Time.realtimeSinceStartup < deliveryDeadline) yield return null;
            Require(adapter.SentOrbitPresentationCount == 6,
                $"Owner must send two spawn/hiding/termination sequences; sent={adapter.SentOrbitPresentationCount}.");
            Require(replica.ReceivedOrbitPresentationCount == 6 && replica.ReplicaActiveOrbCount == 0,
                $"Remote must receive all orb phases and release both orbs; received={replica.ReceivedOrbitPresentationCount}, active={replica.ReplicaActiveOrbCount}.");
            // Aged Hide may return an orb before its reliable termination arrives. In that
            // legal ordering the edge is received, but TryTerminate has no remaining instance.
            Require(replica.ReplicaOrbSpawnCount == 2 && replica.ReplicaOrbHidingCount == 2 &&
                    replica.RejectedOrbitPresentationCount == 0,
                $"Both orbs must actually replay; spawned={replica.ReplicaOrbSpawnCount}, hiding={replica.ReplicaOrbHidingCount}, ended={replica.ReplicaOrbTerminationCount}, rejected={replica.RejectedOrbitPresentationCount}.");
            Require(firstRemoteOrbPositions.Count >= 2 && remoteOrbitMoved,
                "Remote replicas must change their actual orbital positions without owner aiming input.");
            if (!remote.isServer) Require(!remote.GetComponent<PlayerBuildRuntime>().IsBuildActive, "Remote executes another player's GAS Build.");
            Debug.Log($"[CirclingProcess] owner={owner.netId} hits={target.HitCount} damage={target.TotalDamage} rootCount=1 sourceEnds={sourceEnds.Count} orbHits={string.Join(";", target.OrbHitCounts.OrderBy(pair => pair.Key).Select(pair => pair.Key + ":" + pair.Value))} remoteEdges=6 remoteEffectiveEnds={replica.ReplicaOrbTerminationCount} remoteMoved=true remoteDamage=0 remoteActive=0 colliderDisabledHits={target.ColliderDisabledHitCount} firstHitSeconds={target.FirstHitSeconds:F3} lastHitSeconds={target.LastHitSeconds:F3}");
            File.WriteAllText(Path.Combine(directory, "owner-result-" + owner.netId), target.HitCount + "," + target.TotalDamage);
            Destroy(targetObject);
            Mark("verified-" + role);
        }

        private void StartOwnedCircling()
        {
            ownedCircling.Attack();
        }

        private IEnumerator DisconnectDuringCirclingAndReconnect()
        {
            while (!Has("cancel-circling")) yield return null;
            NetworkIdentity oldPlayer = NetworkClient.localPlayer;
            uint oldAvatar = oldPlayer.netId;
            ushort oldEpoch = oldPlayer.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            ownedCircling = (CirclingAttackBehaviour)oldPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;
            StartOwnedCircling();
            while (!Has("circling-admitted")) yield return null;
            Require(ownedCircling.ActiveOrbCount == 2 && ownedCircling.GetComponentsInChildren<AnimatedAttack>().Length == 2,
                "Fixture must disconnect with a live circling after its spectator receives the spawn.");
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(!FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Any(a => a.gameObject.activeInHierarchy),
                "Disconnect left an active owned or remote circling.");
            Mark("disconnected-client");
            while (!Has("reconnect")) yield return null;
            manager.StartClient();
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                   !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive) yield return null;
            NetworkIdentity restored = NetworkClient.localPlayer;
            Require(restored.netId != oldAvatar && restored.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch != oldEpoch,
                "Reconnect reused the old Avatar or attack epoch.");
            var circling = restored.GetComponent<PlayerBuildRuntime>().InitialWeapon as CirclingAttackBehaviour;
            Require(circling != null && circling.ProjectileCountValue == 2 && circling.ActiveOrbCount == 0 &&
                    circling.GetCooldown() - circling.LastAttackElapsedTime > 10,
                "Reconnect reset the circling Build, replayed its visual or lost remaining cooldown.");
            Debug.Log($"[CirclingProcess] resumed oldAvatar={oldAvatar} newAvatar={restored.netId} remaining={circling.GetCooldown() - circling.LastAttackElapsedTime:F3} oldCirclingReplayed=false");
            Mark("resumed-client");
        }

        private void VerifyServerHasNoVisuals()
        {
            if (role != "server") return;
            Require(!NetworkClient.active, "Server-only process became a client.");
            Require(FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Length == 0, "Server-only process created attack visuals.");
        }

        private void VerifyRemoteDisconnectCleanup()
        {
            if (role != "host" && role != "client2") return;
            Require(!ReferenceEquals(firstRemoteAdapter, null) && firstRemoteAdapter.ReplicaOrbSpawnCount == 4,
                "Spectator must have replayed the disconnecting player's second circling.");
            Require(firstRemoteAdapter.ReplicaActiveOrbCount == 0, "Disconnect left the old Avatar's circling replica active.");
        }

        private static uint TargetId(uint player) => 0x6ffc0000u + player;
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log("[CirclingProcess] result=" + (passed ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(passed ? 0 : 1);
        }
    }

    public sealed class CirclingProcessTarget : MonoBehaviour, IDamageable, INativeGasDamageable
    {
        private CombatantBehaviour combatant;
        private float initializedAt;
        public int HitCount { get; private set; }
        public int TotalDamage { get; private set; }
        public int ColliderDisabledHitCount { get; private set; }
        public int UnattributedHitCount { get; private set; }
        public readonly Dictionary<int, int> OrbHitCounts = new Dictionary<int, int>();
        public float FirstHitSeconds { get; private set; }
        public float LastHitSeconds { get; private set; }
        public readonly HashSet<ulong> Roots = new HashSet<ulong>();
        public readonly HashSet<ulong> HitEvents = new HashSet<ulong>();
        public readonly HashSet<uint> SourcePlayers = new HashSet<uint>();
        public void Initialize(uint id)
        {
            initializedAt = Time.time;
            combatant = gameObject.AddComponent<CombatantBehaviour>();
            combatant.Initialize(1000);
            combatant.ConfigureEntityId(id);
        }
        public bool ResolveNativeGasHit(NativeGasHit hit)
        {
            float elapsed = Time.time - initializedAt;
            if (HitCount == 0) FirstHitSeconds = elapsed;
            LastHitSeconds = elapsed;
            // BasePlayerAttack passes its own transform position. Identify that live orb
            // without replacing its real hitbox callback or manufacturing any hit.
            AnimatedAttack orb = hit.PresentationWeapon.GetComponentsInChildren<AnimatedAttack>()
                .Where(value => value.gameObject.activeInHierarchy)
                .OrderBy(value => ((Vector2)value.transform.position - hit.AttackPosition).sqrMagnitude).FirstOrDefault();
            if (orb == null || ((Vector2)orb.transform.position - hit.AttackPosition).sqrMagnitude > 0.0001f)
                UnattributedHitCount++;
            else
            {
                int instanceId = orb.GetInstanceID();
                OrbHitCounts.TryGetValue(instanceId, out int count);
                OrbHitCounts[instanceId] = count + 1;
                PlayerAttackOvertimeHitBox box = orb.GetComponentInChildren<PlayerAttackOvertimeHitBox>();
                // Record the authored exit-grace window; do not change its damage semantics.
                if (box != null && box.collider != null && !box.collider.enabled) ColliderDisabledHitCount++;
            }
            var result = hit.Runtime.ResolveHitDetailed(hit.Attack, combatant);
            HitCount++;
            TotalDamage += result.ResolvedDamage.Value;
            Roots.Add(result.HitContext.RootEventId.Value);
            HitEvents.Add(result.HitContext.EventId.Value);
            SourcePlayers.Add(result.HitContext.SourcePlayerId);
            return true;
        }
        public int GetID() => (int)combatant.EntityId;
        public Vector2 GetPosition() => transform.position;
        public bool IsActive() => combatant.IsAlive;
        public void Damage(Vector2 position, WeaponBehaviour weapon, LegacyDamageType type) => throw new InvalidOperationException("Legacy damage was invoked.");
        public void Damage(int value, LegacyDamageType type) => throw new InvalidOperationException("Legacy damage was invoked.");
    }
}
