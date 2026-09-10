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
    // Files coordinate processes only. Resources, roots, physics, damage and replicas use production paths.
    [DefaultExecutionOrder(-31900)]
    public sealed class DashAttackProcessProbe : MonoBehaviour
    {
        private static readonly PropertyInfo IsPresentationOnly = typeof(BasePlayerAttack)
            .GetProperty("IsPresentationOnly", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo NativeSnapshot = typeof(BasePlayerAttack)
            .GetProperty("NativeAttackSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SourceWeapon = typeof(BasePlayerAttack)
            .GetField("_behaviour", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TrailParticles = typeof(MultiParticlePlayerTrailAttack)
            .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<(ulong, int), PointObservation> observedRemotePoints =
            new Dictionary<(ulong, int), PointObservation>();
        private string role, directory;
        private ushort port;
        private float deadline;
        private bool finished, driveRight, remotePointStayedInWorld;
        private BootGameplayNetworkManager manager;
        private DashAttackBehaviour ownedDash;
        private NetworkWeaponCombatAdapter firstRemoteAdapter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--dash-role="));
            if (value == null || FindFirstObjectByType<DashAttackProcessProbe>() != null) return;
            var probe = new GameObject("Dash Process Validation").AddComponent<DashAttackProcessProbe>();
            probe.role = value.Split('=')[1];
            probe.directory = args.First(a => a.StartsWith("--dash-sync=")).Substring("--dash-sync=".Length);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--dash-port=")).Split('=')[1]);
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
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
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
                {
                    NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().SetWeaponExecutionEnabled(false);
                    if (driveRight) NetworkClient.localPlayer.GetComponent<PlayerMovement>().SetDirection(Vector2.right);
                }
                ObserveRemoteTrail();
                if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Timed out in role " + role);
            }
            catch (Exception error) { Debug.LogException(error); Finish(false); }
        }

        private void ObserveRemoteTrail()
        {
            if (!NetworkClient.active) return;
            NetworkIdentity remote = NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned &&
                p.GetComponent<NetworkRunParticipant>() != null);
            if (remote == null) return;
            NetworkWeaponCombatAdapter adapter = remote.GetComponent<NetworkWeaponCombatAdapter>();
            if (ReferenceEquals(firstRemoteAdapter, null)) firstRemoteAdapter = adapter;
            PlayerMovement remoteMovement = remote.GetComponent<PlayerMovement>();
            foreach (MultiParticlePlayerTrailAttack trail in FindObjectsByType<MultiParticlePlayerTrailAttack>(FindObjectsSortMode.None))
            {
                WeaponBehaviour source = SourceWeapon.GetValue(trail) as WeaponBehaviour;
                if (source == null || source.OwnerPlayer != remoteMovement) continue;
                Require((bool)IsPresentationOnly.GetValue(trail) && NativeSnapshot.GetValue(trail) == null,
                    "Remote trail acquired an executing GAS snapshot.");
                Require(trail.transform.parent == null && !trail.hitbox.enabled &&
                    !trail.GetComponent<EdgeCollider2D>().enabled && trail.GetComponent<NetworkIdentity>() == null,
                    "Remote world trail acquired gameplay collision or network-object ownership.");
                foreach (ParticleSystem particle in ReadParticles(trail))
                {
                    var key = (trail.PresentationSpawn.AttackEventId, particle.GetInstanceID());
                    if (!observedRemotePoints.TryGetValue(key, out PointObservation observed))
                        observedRemotePoints.Add(key, new PointObservation(particle.transform.position, remote.transform.position));
                    else
                    {
                        Require(Vector3.Distance(observed.Position, particle.transform.position) < 0.001f,
                            "A received world trail point drifted with its player's movement.");
                        if (Vector3.Distance(observed.PlayerPosition, remote.transform.position) > 0.05f)
                            remotePointStayedInWorld = true;
                    }
                }
            }
            if ((role == "host" || role == "client2") && adapter.ReplicaTrailSpawnCount >= 2 &&
                adapter.ReplicaActiveTrailCount == 1)
                Mark("spectator-sees-second-" + role);
        }

        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (NetworkGameplayEnemySpawner spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    { spawner.Configure(spawner.EnemyPrefab, 5); spawner.enabled = false; }
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Boot manager missing.");
            RuntimeDB database = FindFirstObjectByType<RuntimeDB>();
            Require(database != null && database.TryGetWeaponData(8, out _), "Migrated Dash ID 8 is absent from the weapon database.");
            var weapons = Instantiate(database.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != 8) return definition;
                var copy = Instantiate(definition);
                var stats = copy.BaseStats;
                Require(stats.damage == 9 && stats.duration == 1.25f && stats.speed == 1f && stats.projectileCount == 1,
                    "Validation requires the authored Dante Dash gameplay parameters.");
                stats.critRate = 0;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weapons);
            manager.playerPrefab.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(8);
            PlayerStats playerStats = manager.playerPrefab.GetComponent<PlayerMovement>().PlayerStats;
            var baseStats = Instantiate(playerStats.playerBaseStatsDatabase);
            Require(baseStats.values.maxDashCharges == 2, "The authored player must start with two Dash charges.");
            baseStats.values.dashCooldown = 30f;
            playerStats.playerBaseStatsDatabase = baseStats;
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out string error), error);
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
                if (role == "client") yield return DisconnectDuringDashAndReconnect();
                while (!Has("stop")) yield return null;
                VerifyRemoteDisconnectCleanup();
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active)
                yield return null;
            yield return null;
            Require(FindObjectsByType<NetworkPlayerBootstrap>(FindObjectsSortMode.None).Length == 0, "Avatar survived scene shutdown.");
            Require(FindObjectsByType<MultiParticlePlayerTrailAttack>(FindObjectsSortMode.None).Length == 0,
                "Active Dash trail survived scene shutdown.");
            Mark("stopped-" + role);
        }

        private NetworkIdentity[] ServerPlayers() => NetworkServer.spawned.Values
            .Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();

        private IEnumerator VerifyServer()
        {
            while (ServerPlayers().Length != 2 || ServerPlayers().Any(p => !p.GetComponent<PlayerBuildRuntime>().IsBuildActive))
                yield return null;
            NetworkIdentity[] players = ServerPlayers();
            manager.BeginRun();
            foreach (NetworkIdentity player in players)
            {
                Require(player.GetComponent<PlayerBuildRuntime>().InitialWeapon is DashAttackBehaviour, "Server Build has the wrong weapon family.");
                NetworkCombatWorld.Instance.Gateway.Ledger.RegisterEntity(TargetId(player.netId), 1000,
                    CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            }
            VerifyServerHasNoVisuals();
            while (!Has("owner-ready-client") || !Has(role == "host" ? "owner-ready-host" : "owner-ready-client2")) yield return null;
            Mark("fire");
            while (!Has("verified-client") || !Has(role == "host" ? "verified-host" : "verified-client2")) yield return null;
            foreach (NetworkIdentity player in players)
            {
                OwnerResult result = ReadOwnerResult(player.netId);
                Require(result.Hits >= 2 && result.Damage == result.Hits * 9 && result.Points > 1,
                    "Owner must report repeat physics hits of nine damage and actual trail samples.");
                var gateway = NetworkCombatWorld.Instance.Gateway;
                while (!gateway.Ledger.TryGetState(TargetId(player.netId), out var state) || state.Health != 1000 - result.Damage ||
                    gateway.Attacks.ActiveCount(player.netId) != 0) yield return null;
                var adapter = player.GetComponent<NetworkWeaponCombatAdapter>();
                var dash = player.GetComponent<NetworkPlayerDash>();
                Require(dash.AcceptedUseCount == 1 && dash.RejectedUseCount == 0 && adapter.RejectedAttackCount == 0,
                    "Initial attack must use one accepted Dash use and admitted weapon root.");
                Require(adapter.AcceptedCooldownReportCount == 0 && adapter.RejectedTrailPresentationCount == 0,
                    "Dash incorrectly used ordinary weapon cooldown admission or rejected legal trail messages.");
                PlayerDashSnapshot resource = dash.CaptureServerState();
                Require(resource.MaxCharges == 2 && resource.RechargeReadyAt.Length == 1,
                    "Dash weapon consumed additional resources or refunded its completed trail.");
                Debug.Log($"[DashProcess] canonical player={player.netId} hits={result.Hits} damage={result.Damage} hp={1000-result.Damage} points={result.Points} roots=0 acceptedDashUses=1 ordinaryCooldownReports=0");
            }
            VerifyServerHasNoVisuals();
            NetworkIdentity returning = players.OrderBy(p => p.GetComponent<NetworkRunParticipant>().ParticipantId)
                .First(p => p.connectionToClient is not LocalConnectionToClient);
            uint oldAvatar = returning.netId;
            ulong participantId = returning.GetComponent<NetworkRunParticipant>().ParticipantId;
            NetworkPlayerDash returningDash = returning.GetComponent<NetworkPlayerDash>();
            while (NetworkTime.time < returningDash.CaptureServerState().NextUseAt + 0.1d) yield return null;
            Mark("cancel-dash");
            while (returningDash.AcceptedUseCount < 2) yield return null;
            while (!Has("spectator-sees-second-" + (role == "host" ? "host" : "client2"))) yield return null;
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(oldAvatar) == 1,
                "Second Dash root must be active when the spectator sees its world trail.");
            PlayerDashSnapshot beforeDisconnect = returningDash.CaptureServerState();
            Require(beforeDisconnect.RechargeReadyAt.Length == 2 &&
                beforeDisconnect.RechargeReadyAt.All(readyAt => readyAt > NetworkTime.time + 10d),
                "Both consumed charges must have outstanding independent recharge deadlines.");
            VerifyServerHasNoVisuals();
            Mark("dash-admitted");
            while (!Has("disconnected-client") || NetworkServer.spawned.ContainsKey(oldAvatar)) yield return null;
            Require(!NetworkCombatWorld.Instance.Gateway.Attacks.RequiresAdmission(oldAvatar),
                "Disconnected avatar retained attack admission roots.");
            var retained = manager.Session.Participants.Single(p => p.Id == participantId);
            Require(retained.Checkpoint != null && retained.Checkpoint.Dash.HasValue,
                "Disconnect lost the canonical Dash checkpoint.");
            PlayerDashSnapshot checkpoint = retained.Checkpoint.Dash.Value;
            Require(checkpoint.MaxCharges == 2 && checkpoint.RechargeReadyAt.SequenceEqual(beforeDisconnect.RechargeReadyAt),
                "Disconnect changed or refunded consumed Dash deadlines.");
            Mark("reconnect");
            while (!Has("resumed-client")) yield return null;
            Require(retained.AvatarId != 0 && retained.AvatarId != oldAvatar, "Reconnect reused the old Avatar.");
            NetworkIdentity restored = NetworkServer.spawned[retained.AvatarId];
            PlayerBuildRuntime build = restored.GetComponent<PlayerBuildRuntime>();
            Require(build.InitialWeapon is DashAttackBehaviour && build.InitialWeapon.ID == 8 && build.InitialWeapon.DamageValue == 9,
                "Reconnect did not restore the original Dash Build.");
            PlayerDashSnapshot restoredState = restored.GetComponent<NetworkPlayerDash>().CaptureServerState();
            Require(restoredState.RechargeReadyAt.SequenceEqual(checkpoint.RechargeReadyAt), "New server Avatar lost Dash deadlines.");
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(retained.AvatarId) == 0, "Reconnect replayed an old Dash root.");
            Debug.Log($"[DashProcess] reconnect participant={participantId} oldAvatar={oldAvatar} newAvatar={retained.AvatarId} pendingRecharges=2 deadlinesRetained=true noRefund=true");
            Mark("stop");
            while (!Has("stopped-client") || (role == "server" && !Has("stopped-client2"))) yield return null;
        }


        private IEnumerator VerifyOwner()
        {
            while (!OwnerReady()) yield return null;
            NetworkIdentity owner = NetworkClient.localPlayer;
            PlayerBuildRuntime build = owner.GetComponent<PlayerBuildRuntime>();
            build.SetWeaponExecutionEnabled(false);
            ownedDash = build.InitialWeapon as DashAttackBehaviour;
            Require(ownedDash != null, "Owner did not receive the Dash Build.");
            PlayerMovement movement = owner.GetComponent<PlayerMovement>();
            NetworkPlayerDash dash = owner.GetComponent<NetworkPlayerDash>();
            Require(dash.OwnerRuntime.AvailableCharges == 2, "Fresh Owner has no two-charge Dash baseline.");
            Mark("owner-ready-" + role);
            while (!Has("fire")) yield return null;
            var targetObject = new GameObject("Dash physics target");
            targetObject.layer = LayerMask.NameToLayer("EnemyHitbox");
            targetObject.transform.position = owner.transform.position;
            targetObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var collider = targetObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 30f;
            var target = targetObject.AddComponent<DashProcessTarget>();
            target.Initialize(TargetId(owner.netId));
            var sourceSpawns = new List<TrailPresentationSpawn>();
            var sourcePoints = new List<TrailPresentationPoint>();
            var sourceEnds = new List<TrailPresentationTermination>();
            var samplingEnds = new List<TrailPresentationSamplingEnded>();
            bool motionEnded = false;
            int pointsAfterDashEnd = 0;
            Action<TrailPresentationPoint> onPoint = point =>
            {
                sourcePoints.Add(point);
                if (motionEnded) pointsAfterDashEnd++;
            };
            Action onDashEnd = () => motionEnded = true;
            Action<TrailPresentationSamplingEnded> onSamplingEnd = edge =>
            {
                samplingEnds.Add(edge);
                driveRight = false;
                movement.SetDirection(Vector2.zero);
            };
            ownedDash.PresentationSpawned += sourceSpawns.Add;
            ownedDash.PresentationPointAdded += onPoint;
            ownedDash.PresentationSamplingEnded += onSamplingEnd;
            ownedDash.PresentationTerminated += sourceEnds.Add;
            movement.OnDashEnd += onDashEnd;
            try
            {
                StartOwnedDash(movement);
                float startDeadline = Time.realtimeSinceStartup + 3f;
                while (sourceSpawns.Count == 0 && Time.realtimeSinceStartup < startDeadline) yield return null;
                Require(sourceSpawns.Count == 1, "Actual PlayerMovement.Dash input did not produce exactly one committed weapon root.");
                float endDeadline = Time.realtimeSinceStartup + 15f;
                while ((sourceEnds.Count < 1 || ownedDash.ActiveTrailCount > 0 || dash.PendingOwnerUseCount != 0) &&
                    Time.realtimeSinceStartup < endDeadline) yield return null;
            }
            finally
            {
                ownedDash.PresentationSpawned -= sourceSpawns.Add;
                ownedDash.PresentationPointAdded -= onPoint;
                ownedDash.PresentationSamplingEnded -= onSamplingEnd;
                ownedDash.PresentationTerminated -= sourceEnds.Add;
                movement.OnDashEnd -= onDashEnd;
                driveRight = false;
                if (movement != null) movement.SetDirection(Vector2.zero);
            }
            Require(target.HitCount >= 2 && target.TotalDamage == target.HitCount * 9,
                $"Actual trail physics must repeat nine-damage hits; hits={target.HitCount} damage={target.TotalDamage}.");
            Require(target.Roots.Count == 1 && target.HitEvents.Count == target.HitCount &&
                target.SourcePlayers.SetEquals(new[] { owner.netId }), "Dash damage lost root or per-hit event ownership.");
            Require(sourceSpawns.Count == 1 && sourceEnds.Count == 1 && samplingEnds.Count == 1 &&
                sourceEnds[0].AttackEventId == sourceSpawns[0].AttackEventId &&
                target.Roots.Contains(sourceSpawns[0].AttackEventId), "Source trail did not finish once under its original GAS root.");
            Require(sourcePoints.Count > 1 && sourcePoints.Select(point => point.PointIndex)
                .SequenceEqual(Enumerable.Range(0, sourcePoints.Count).Select(index => (uint)index)),
                "Source must sample ordered world points without fabricated catch-up points.");
            Require(motionEnded && pointsAfterDashEnd > 0, "Source 1.25-second trail must continue sampling after Dash motion ends.");
            Require(samplingEnds[0].SamplingElapsedSeconds >= 1.25f && dash.OwnerRuntime.AvailableCharges == 1 &&
                dash.PendingOwnerUseCount == 0, "Owner lost original sampling duration or acknowledged charge consumption.");
            WriteOwnerResult(owner.netId, new OwnerResult(target.HitCount, target.TotalDamage, sourcePoints.Count));
            NetworkIdentity remote = NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned &&
                p.GetComponent<NetworkRunParticipant>() != null);
            Require(remote != null, "Remote Avatar missing.");
            while (!Has("source-complete-" + remote.netId)) yield return null;
            OwnerResult remoteResult = ReadOwnerResult(remote.netId);
            var adapter = owner.GetComponent<NetworkWeaponCombatAdapter>();
            var replica = remote.GetComponent<NetworkWeaponCombatAdapter>();
            float deliveryDeadline = Time.realtimeSinceStartup + 5f;
            while ((adapter.SentTrailPresentationCount < sourcePoints.Count + 3 ||
                    replica.ReceivedTrailPresentationCount < remoteResult.Points + 3 || replica.ReplicaActiveTrailCount > 0) &&
                Time.realtimeSinceStartup < deliveryDeadline) yield return null;
            Require(adapter.SentTrailPresentationCount == sourcePoints.Count + 3,
                "Owner must send every actual point plus Spawn/SamplingEnded/Terminated exactly once.");
            Require(replica.ReceivedTrailPresentationCount == remoteResult.Points + 3 &&
                replica.ReplicaTrailPointCount == remoteResult.Points && replica.ReplicaTrailSpawnCount == 1 &&
                replica.ReplicaTrailSamplingEndCount == 1 && replica.ReplicaActiveTrailCount == 0 &&
                replica.RejectedTrailPresentationCount == 0,
                "Remote did not replay every source world point and clean its original root.");
            Require(observedRemotePoints.Count > 0 && remotePointStayedInWorld,
                "Actual remote particle transforms must stay in world space as their player moves.");
            if (!remote.isServer) Require(!remote.GetComponent<PlayerBuildRuntime>().IsBuildActive,
                "Remote executes another player's GAS Build.");
            Debug.Log($"[DashProcess] owner={owner.netId} hits={target.HitCount} damage={target.TotalDamage} roots=1 sourceEnds=1 points={sourcePoints.Count} postDashPoints={pointsAfterDashEnd} remotePoints={replica.ReplicaTrailPointCount} remoteEdges={replica.ReceivedTrailPresentationCount} remoteEffectiveEnds={replica.ReplicaTrailTerminationCount} worldStable=true remoteDamage=0 active=0 colliderDisabledHits={target.ColliderDisabledHitCount} firstHit={target.FirstHitSeconds:F3} lastHit={target.LastHitSeconds:F3}");
            Destroy(targetObject);
            Mark("verified-" + role);
        }

        private bool OwnerReady() => NetworkClient.localPlayer != null &&
            NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline &&
            NetworkClient.localPlayer.GetComponent<NetworkPlayerDash>().HasOwnerBaseline &&
            NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive;

        private void StartOwnedDash(PlayerMovement movement)
        {
            driveRight = true;
            movement.SetDirection(Vector2.right);
            movement.Dash();
        }

        private IEnumerator DisconnectDuringDashAndReconnect()
        {
            while (!Has("cancel-dash")) yield return null;
            NetworkIdentity oldPlayer = NetworkClient.localPlayer;
            uint oldAvatar = oldPlayer.netId;
            ushort oldEpoch = oldPlayer.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            NetworkPlayerDash dash = oldPlayer.GetComponent<NetworkPlayerDash>();
            while (NetworkTime.time < dash.OwnerRuntime.NextUseAt + 0.1d) yield return null;
            ownedDash = (DashAttackBehaviour)oldPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;
            StartOwnedDash(oldPlayer.GetComponent<PlayerMovement>());
            while (!Has("dash-admitted") || dash.PendingOwnerUseCount != 0) yield return null;
            Require(ownedDash.ActiveTrailCount == 1, "Disconnect must interrupt a live world trail observed by the spectator.");
            PlayerDashSnapshot beforeDisconnect = dash.OwnerRuntime.Capture(NetworkTime.time);
            Require(beforeDisconnect.RechargeReadyAt.Length == 2 && dash.OwnerRuntime.AvailableCharges == 0,
                "Owner must confirm both Dash charges are consumed before disconnect.");
            driveRight = false;
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(FindObjectsByType<MultiParticlePlayerTrailAttack>(FindObjectsSortMode.None).Length == 0,
                "Disconnect left an owned or remote world trail.");
            Mark("disconnected-client");
            while (!Has("reconnect")) yield return null;
            manager.StartClient();
            while (!OwnerReady()) yield return null;
            NetworkIdentity restored = NetworkClient.localPlayer;
            Require(restored.netId != oldAvatar && restored.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch != oldEpoch,
                "Reconnect reused the old Avatar or event epoch.");
            var restoredWeapon = restored.GetComponent<PlayerBuildRuntime>().InitialWeapon as DashAttackBehaviour;
            var restoredDash = restored.GetComponent<NetworkPlayerDash>();
            PlayerDashSnapshot afterReconnect = restoredDash.OwnerRuntime.Capture(NetworkTime.time);
            Require(restoredWeapon != null && restoredWeapon.ID == 8 && restoredWeapon.ActiveTrailCount == 0 &&
                restoredDash.OwnerRuntime.AvailableCharges == 0 && afterReconnect.MaxCharges == 2 &&
                afterReconnect.RechargeReadyAt.SequenceEqual(beforeDisconnect.RechargeReadyAt) &&
                afterReconnect.RechargeReadyAt.All(readyAt => readyAt > NetworkTime.time + 10d),
                "Reconnect replayed the old trail, refunded a charge, or replaced remaining recharge deadlines.");
            Require(restored.GetComponent<NetworkWeaponCombatAdapter>().SentTrailPresentationCount == 0,
                "Restored Build replayed an old Dash root.");
            Debug.Log($"[DashProcess] resumed oldAvatar={oldAvatar} newAvatar={restored.netId} charges=0 deadlines=2 remaining={string.Join(";", afterReconnect.RechargeReadyAt.Select(at => (at-NetworkTime.time).ToString("F3")))} oldTrailReplayed=false");
            Mark("resumed-client");
        }

        private void VerifyServerHasNoVisuals()
        {
            if (role != "server") return;
            Require(!NetworkClient.active, "Server-only process became a client.");
            Require(FindObjectsByType<MultiParticlePlayerTrailAttack>(FindObjectsSortMode.None).Length == 0,
                "Server-only process created trail attack visuals.");
            Require(FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length == 0,
                "Server-only process instantiated active particle presentation.");
        }

        private void VerifyRemoteDisconnectCleanup()
        {
            if (role != "host" && role != "client2") return;
            Require(!ReferenceEquals(firstRemoteAdapter, null) && firstRemoteAdapter.ReplicaTrailSpawnCount == 2,
                "Spectator never replayed the disconnecting player's second Dash trail.");
            Require(firstRemoteAdapter.ReplicaActiveTrailCount == 0, "Old Avatar retained a remote world trail.");
        }

        private void WriteOwnerResult(uint player, OwnerResult result)
        {
            File.WriteAllText(Path.Combine(directory, "owner-result-" + player), result.Hits + "," + result.Damage + "," + result.Points);
            Mark("source-complete-" + player);
        }
        private OwnerResult ReadOwnerResult(uint player)
        {
            string[] parts = File.ReadAllText(Path.Combine(directory, "owner-result-" + player)).Split(',');
            return new OwnerResult(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }
        internal static ParticleSystem[] ReadParticles(MultiParticlePlayerTrailAttack trail)
        {
            var result = new List<ParticleSystem>();
            foreach (object entry in (IEnumerable)TrailParticles.GetValue(trail))
            {
                var particle = (ParticleSystem)entry.GetType().GetField("Particle").GetValue(entry);
                if (particle != null) result.Add(particle);
            }
            return result.ToArray();
        }
        private readonly struct OwnerResult
        {
            public OwnerResult(int hits, int damage, int points) { Hits = hits; Damage = damage; Points = points; }
            public int Hits { get; }
            public int Damage { get; }
            public int Points { get; }
        }
        private sealed class PointObservation
        {
            public PointObservation(Vector3 position, Vector3 playerPosition) { Position = position; PlayerPosition = playerPosition; }
            public readonly Vector3 Position, PlayerPosition;
        }
        private static uint TargetId(uint player) => 0x6ffb0000u + player;
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log("[DashProcess] result=" + (passed ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(passed ? 0 : 1);
        }
    }

    public sealed class DashProcessTarget : MonoBehaviour, IDamageable, INativeGasDamageable
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
            MultiParticlePlayerTrailAttack trail = FindObjectsByType<MultiParticlePlayerTrailAttack>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.PresentationSpawn.AttackEventId == hit.Attack.Context.EventId.Value);
            if (trail == null) throw new InvalidOperationException("Native Dash hit lost its actual world trail.");
            if (!trail.GetComponent<EdgeCollider2D>().enabled) ColliderDisabledHitCount++;
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

