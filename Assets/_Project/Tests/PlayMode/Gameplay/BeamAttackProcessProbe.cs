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
    // Files coordinate independent processes; roots, damage, aim and replicas use production Mirror paths.
    [DefaultExecutionOrder(-31900)]
    public sealed class BeamAttackProcessProbe : MonoBehaviour
    {
        private static readonly MethodInfo BeamUpdate = typeof(PlayerBeamAttackBehaviour)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        private string role, directory;
        private ushort port;
        private float deadline, ownerAttackStarted;
        private bool finished, sawRemoteHeading, remoteHeadingChanged;
        private Vector3 firstRemoteHeading;
        private BootGameplayNetworkManager manager;
        private PlayerBeamAttackBehaviour ownedBeam;
        private NetworkWeaponCombatAdapter firstRemoteAdapter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--beam-role="));
            if (value == null || FindFirstObjectByType<BeamAttackProcessProbe>() != null) return;
            var probe = new GameObject("Beam Process Validation").AddComponent<BeamAttackProcessProbe>();
            probe.role = value.Split('=')[1];
            probe.directory = args.First(a => a.StartsWith("--beam-sync=")).Substring("--beam-sync=".Length);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--beam-port=")).Split('=')[1]);
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 120;
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
                if (ownedBeam != null && ownedBeam.ActiveBeamCount > 0)
                {
                    float radians = (Time.realtimeSinceStartup - ownerAttackStarted) * 120 * Mathf.Deg2Rad;
                    ownedBeam.OwnerPlayer.SetRuntimeAimDirection(new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
                    // Execute the actual aiming routine once per frame while suppressing automatic future roots.
                    BeamUpdate.Invoke(ownedBeam, null);
                }
                ObserveRemoteBeam();
                if (Time.realtimeSinceStartup > deadline)
                    throw new InvalidOperationException("Timed out in role " + role);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(false);
            }
        }

        private void ObserveRemoteBeam()
        {
            if (!NetworkClient.active) return;
            var remote = NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned &&
                p.GetComponent<NetworkRunParticipant>() != null);
            if (remote == null) return;
            var adapter = remote.GetComponent<NetworkWeaponCombatAdapter>();
            if (ReferenceEquals(firstRemoteAdapter, null)) firstRemoteAdapter = adapter;
            AnimatedAttack attack = remote.GetComponentsInChildren<AnimatedAttack>()
                .FirstOrDefault(a => a.gameObject.activeInHierarchy && a.GetComponentInParent<PlayerBeamAttackBehaviour>() != null);
            if (attack != null)
            {
                Vector3 heading = attack.transform.localPosition;
                if (!sawRemoteHeading) { firstRemoteHeading = heading; sawRemoteHeading = true; }
                else if (Vector3.Angle(firstRemoteHeading, heading) > 10) remoteHeadingChanged = true;
            }
            if ((role == "host" || role == "client2") && adapter.ReplicaBeamSpawnCount >= 2 && adapter.ReplicaActiveBeamCount > 0)
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
            Require(database != null && database.TryGetWeaponData(3, out var beam), "Migrated beam is absent from the weapon database.");
            Require(BeamUpdate != null, "Native beam aiming routine is missing.");
            var weapons = Instantiate(database.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != 3) return definition;
                var copy = Instantiate(definition);
                var stats = copy.BaseStats;
                stats.damage = 5;
                stats.critRate = 0;
                stats.projectileCount = 1;
                stats.duration = 1.2f;
                stats.speed = 1f / 30;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weapons);
            manager.playerPrefab.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(3);
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
                if (role == "client") yield return DisconnectDuringBeamAndReconnect();
                while (!Has("stop")) yield return null;
                VerifyRemoteDisconnectCleanup();
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active)
                yield return null;
            yield return null;
            Require(FindObjectsByType<NetworkPlayerBootstrap>(FindObjectsSortMode.None).Length == 0, "Avatar survived scene shutdown.");
            Require(!FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Any(a => a.gameObject.activeInHierarchy), "Active beam survived scene shutdown.");
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
                Require(player.GetComponent<PlayerBuildRuntime>().InitialWeapon is PlayerBeamAttackBehaviour, "Server Build has the wrong weapon family.");
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
                Require(hits > 1 && damage == hits * 5, "Owner did not observe repeat physics hits of five damage.");
                var gateway = NetworkCombatWorld.Instance.Gateway;
                while (!gateway.Ledger.TryGetState(TargetId(player.netId), out var state) || state.Health != 1000 - damage ||
                       gateway.Attacks.ActiveCount(player.netId) != 0) yield return null;
                var adapter = player.GetComponent<NetworkWeaponCombatAdapter>();
                Require(adapter.AcceptedCooldownReportCount == 1 && adapter.RejectedAttackCount == 0, "Continuous beam hits must share one admitted root.");
                Require(adapter.RejectedPresentationCount == 0, "Server rejected a legal beam edge.");
                Debug.Log($"[BeamProcess] canonical player={player.netId} hits={hits} damage={damage} hp={1000 - damage} roots=0 admitted=1");
            }
            VerifyServerHasNoVisuals();
            NetworkIdentity returning = players.OrderBy(p => p.GetComponent<NetworkRunParticipant>().ParticipantId)
                .First(p => p.connectionToClient is not LocalConnectionToClient);
            uint oldAvatar = returning.netId;
            ulong participantId = returning.GetComponent<NetworkRunParticipant>().ParticipantId;
            double readyAt = returning.GetComponent<NetworkWeaponCombatAdapter>().CaptureCooldowns().Single().ReadyAt;
            while (NetworkTime.time < readyAt + 0.1) yield return null;
            Mark("cancel-beam");
            while (returning.GetComponent<NetworkWeaponCombatAdapter>().AcceptedCooldownReportCount < 2) yield return null;
            while (!Has("spectator-sees-second-" + (role == "host" ? "host" : "client2"))) yield return null;
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(oldAvatar) == 1, "Second beam must still be active when its spectator sees it.");
            VerifyServerHasNoVisuals();
            Mark("beam-admitted");
            while (!Has("disconnected-client") || NetworkServer.spawned.ContainsKey(oldAvatar)) yield return null;
            Require(!NetworkCombatWorld.Instance.Gateway.Attacks.RequiresAdmission(oldAvatar), "Disconnected avatar retained attack admission roots.");
            var retained = manager.Session.Participants.Single(p => p.Id == participantId);
            Require(retained.Checkpoint != null && retained.Checkpoint.WeaponCooldowns.Single().SequenceSeconds > 1.1f,
                "Disconnect during the beam lost its frozen duration before cooldown.");
            Mark("reconnect");
            while (!Has("resumed-client")) yield return null;
            Require(retained.AvatarId != 0 && retained.AvatarId != oldAvatar, "Reconnect reused the old Avatar.");
            PlayerBuildRuntime restored = NetworkServer.spawned[retained.AvatarId].GetComponent<PlayerBuildRuntime>();
            Require(restored.InitialWeapon is PlayerBeamAttackBehaviour && restored.InitialWeapon.ProjectileCountValue == 1,
                "Reconnect did not restore the original beam Build.");
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(retained.AvatarId) == 0,
                "Reconnect replayed an old beam root.");
            Debug.Log($"[BeamProcess] reconnect participant={participantId} oldAvatar={oldAvatar} newAvatar={retained.AvatarId} sequenceRetained=true");
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
            ownedBeam = build.InitialWeapon as PlayerBeamAttackBehaviour;
            Require(ownedBeam != null, "Owner did not receive the beam Build.");
            Mark("owner-ready-" + role);
            while (!Has("fire")) yield return null;
            var targetObject = new GameObject("Beam physics target");
            targetObject.layer = LayerMask.NameToLayer("EnemyHitbox");
            targetObject.transform.position = owner.transform.position;
            targetObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var collider = targetObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 30;
            var target = targetObject.AddComponent<BeamProcessTarget>();
            target.Initialize(TargetId(owner.netId));
            StartOwnedBeam();
            float observeUntil = Time.realtimeSinceStartup + ownedBeam.GetAttackSequenceDuration() + 1.5f;
            while (Time.realtimeSinceStartup < observeUntil) yield return null;
            Require(target.HitCount > 1 && target.TotalDamage == target.HitCount * 5,
                $"Physics must produce repeat hits of five damage; got {target.HitCount} hits, {target.TotalDamage} damage.");
            Require(target.Roots.Count == 1 && target.HitEvents.Count == target.HitCount,
                "A beam must retain one GAS root and create a distinct event for each overtime hit.");
            Require(target.SourcePlayers.SetEquals(new[] { owner.netId }), "Beam damage used another owner's runtime.");
            var adapter = owner.GetComponent<NetworkWeaponCombatAdapter>();
            Require(adapter.SentBeamAimBatchCount > 1, "Owner did not submit continuous aim samples.");
            var remote = NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned && p.GetComponent<NetworkRunParticipant>() != null);
            Require(remote != null, "Remote Avatar missing.");
            var replica = remote.GetComponent<NetworkWeaponCombatAdapter>();
            Require(replica.ReceivedBeamPresentationCount == 2 && replica.ReplicaActiveBeamCount == 0,
                $"Remote must receive spawn/end and release the beam; received={replica.ReceivedBeamPresentationCount}, active={replica.ReplicaActiveBeamCount}.");
            Require(replica.ReplicaBeamSpawnCount == 1 && replica.RejectedBeamPresentationCount == 0,
                $"Beam must actually replay; spawned={replica.ReplicaBeamSpawnCount}, rejected={replica.RejectedBeamPresentationCount}.");
            Require(replica.ReceivedBeamAimCount > 0 && sawRemoteHeading && remoteHeadingChanged,
                $"Remote must receive RPC aim and rotate its live beam; aims={replica.ReceivedBeamAimCount}, observed={sawRemoteHeading}, rotated={remoteHeadingChanged}.");
            if (!remote.isServer) Require(!remote.GetComponent<PlayerBuildRuntime>().IsBuildActive, "Remote executes another player's GAS Build.");
            Debug.Log($"[BeamProcess] owner={owner.netId} hits={target.HitCount} damage={target.TotalDamage} rootCount=1 remoteEdges=2 remoteAims={replica.ReceivedBeamAimCount} remoteRotated=true remoteActive=0 colliderDisabledHits={target.ColliderDisabledHitCount} firstHitSeconds={target.FirstHitSeconds:F3} lastHitSeconds={target.LastHitSeconds:F3}");
            File.WriteAllText(Path.Combine(directory, "owner-result-" + owner.netId), target.HitCount + "," + target.TotalDamage);
            Destroy(targetObject);
            Mark("verified-" + role);
        }

        private void StartOwnedBeam()
        {
            ownerAttackStarted = Time.realtimeSinceStartup;
            ownedBeam.OwnerPlayer.SetRuntimeAimDirection(Vector2.right);
            ownedBeam.Attack();
        }

        private IEnumerator DisconnectDuringBeamAndReconnect()
        {
            while (!Has("cancel-beam")) yield return null;
            NetworkIdentity oldPlayer = NetworkClient.localPlayer;
            uint oldAvatar = oldPlayer.netId;
            ushort oldEpoch = oldPlayer.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            ownedBeam = (PlayerBeamAttackBehaviour)oldPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;
            StartOwnedBeam();
            while (!Has("beam-admitted")) yield return null;
            Require(ownedBeam.ActiveBeamCount == 1 && ownedBeam.GetComponentsInChildren<AnimatedAttack>().Length == 1,
                "Fixture must disconnect with a live beam after its spectator receives the spawn.");
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(!FindObjectsByType<AnimatedAttack>(FindObjectsSortMode.None).Any(a => a.gameObject.activeInHierarchy),
                "Disconnect left an active owned or remote beam.");
            Mark("disconnected-client");
            while (!Has("reconnect")) yield return null;
            manager.StartClient();
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                   !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive) yield return null;
            NetworkIdentity restored = NetworkClient.localPlayer;
            Require(restored.netId != oldAvatar && restored.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch != oldEpoch,
                "Reconnect reused the old Avatar or attack epoch.");
            var beam = restored.GetComponent<PlayerBuildRuntime>().InitialWeapon as PlayerBeamAttackBehaviour;
            Require(beam != null && beam.ProjectileCountValue == 1 && beam.ActiveBeamCount == 0 &&
                    beam.GetCooldown() - beam.LastAttackElapsedTime > 10,
                "Reconnect reset the beam Build, replayed its visual or lost remaining cooldown.");
            Debug.Log($"[BeamProcess] resumed oldAvatar={oldAvatar} newAvatar={restored.netId} remaining={beam.GetCooldown() - beam.LastAttackElapsedTime:F3} oldBeamReplayed=false");
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
            Require(!ReferenceEquals(firstRemoteAdapter, null) && firstRemoteAdapter.ReplicaBeamSpawnCount == 2,
                "Spectator must have replayed the disconnecting player's second beam.");
            Require(firstRemoteAdapter.ReplicaActiveBeamCount == 0, "Disconnect left the old Avatar's beam replica active.");
        }

        private static uint TargetId(uint player) => 0x6ffe0000u + player;
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log("[BeamProcess] result=" + (passed ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(passed ? 0 : 1);
        }
    }

    public sealed class BeamProcessTarget : MonoBehaviour, IDamageable, INativeGasDamageable
    {
        private CombatantBehaviour combatant;
        private float initializedAt;
        public int HitCount { get; private set; }
        public int TotalDamage { get; private set; }
        public int ColliderDisabledHitCount { get; private set; }
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
            // This fixture uses one beam. Observe the authored collider window without
            // changing the source hitbox's timeoutAfterExit continuation semantics.
            PlayerAttackOvertimeHitBox box = hit.PresentationWeapon.GetComponentsInChildren<PlayerAttackOvertimeHitBox>()
                .FirstOrDefault(value => value.collider != null);
            if (box != null && !box.collider.enabled) ColliderDisabledHitCount++;
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
