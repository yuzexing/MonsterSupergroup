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
    // Only coordination uses files. Maturity, AI, original clips/physics, GAS and views use product code.
    [DefaultExecutionOrder(-31900)]
    public sealed class SummonAttackProcessProbe : MonoBehaviour
    {
        private const uint WeaponId = 402;
        // Allow first-process startup without shortening or accelerating any attack animation.
        private const float FixtureCocoonSeconds = 20f;
        private readonly Dictionary<uint, RemoteObservation> remoteObservations = new Dictionary<uint, RemoteObservation>();
        private string role, directory;
        private ushort port;
        private float deadline;
        private bool finished;
        private BootGameplayNetworkManager manager;
        private NetworkWeaponCombatAdapter disconnectedRemoteAdapter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--summon-role="));
            if (value == null || FindFirstObjectByType<SummonAttackProcessProbe>() != null) return;
            var probe = new GameObject("Summon Process Validation").AddComponent<SummonAttackProcessProbe>();
            probe.role = value.Split('=')[1];
            probe.directory = args.First(a => a.StartsWith("--summon-sync=")).Substring("--summon-sync=".Length);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--summon-port=")).Split('=')[1]);
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
                ObserveRemotePets();
                if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Timed out in role " + role);
            }
            catch (Exception error) { Debug.LogException(error); Finish(false); }
        }

        private void ObserveRemotePets()
        {
            if (!NetworkClient.active) return;
            foreach (NetworkIdentity remote in NetworkClient.spawned.Values.Where(p => p != null && !p.isOwned &&
                         p.GetComponent<NetworkRunParticipant>() != null))
            {
                var adapter = remote.GetComponent<NetworkWeaponCombatAdapter>();
                if (!remoteObservations.TryGetValue(remote.netId, out RemoteObservation observation) ||
                    !ReferenceEquals(observation.Adapter, adapter))
                    remoteObservations[remote.netId] = observation = new RemoteObservation { Adapter = adapter };
                foreach (SummonAttackBehaviour emitter in remote.GetComponentsInChildren<SummonAttackBehaviour>())
                {
                    // On a Host the remote avatar also owns a dormant authoritative Build.
                    // Only the separate presentation emitter is allowed to own a public pet.
                    if (emitter.ActiveSummon == null) continue;
                    SummonAIBehaviour pet = emitter.ActiveSummon;
                    Require(pet.IsPresentation && !emitter.HasSimulationBinding && emitter.NativeRuntime == null &&
                            emitter.CurrentSnapshot == null && !emitter.enabled,
                        "Remote pet acquired a native runtime, AI clock or attack snapshot.");
                    Require(pet.transform.parent == null && !pet.GetComponent<Rigidbody2D>().simulated &&
                            pet.GetComponent<NetworkIdentity>() == null &&
                            pet.GetComponentsInChildren<BaseAttackHitBox>(true).All(box => !box.enabled),
                        "Remote pet retained gameplay physics or an independent NetworkIdentity.");
                    observation.Phases.Add(pet.Phase);
                    observation.PetIds.Add(emitter.PetId);
                    SummonPose pose = pet.Mover.CapturePose();
                    if (observation.HasPose && (Vector3.Distance(observation.FirstPose.Position, pose.Position) > .02f ||
                        Vector3.Distance(observation.FirstPose.RotationPivotEuler, pose.RotationPivotEuler) > .5f ||
                        Vector3.Distance(observation.FirstPose.IsoLocalPosition, pose.IsoLocalPosition) > .02f))
                        observation.PoseChanged = true;
                    if (!observation.HasPose) { observation.FirstPose = pose; observation.HasPose = true; }
                    if ((role == "host" || role == "client2") && pet.Phase == SummonPhase.Cocoon &&
                        !Has("cocoon-removed"))
                    {
                        disconnectedRemoteAdapter = adapter;
                        Mark("spectator-sees-cocoon");
                    }
                }
            }
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
            Require(database != null && database.TryGetWeaponData(WeaponId, out _), "Migrated Ovid ID 402 is absent from the WeaponDB.");
            var staging = new GameObject("Summon process fixture definitions");
            staging.transform.SetParent(transform, false);
            staging.SetActive(false);
            var weapons = Instantiate(database.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != WeaponId) return definition;
                var copy = Instantiate(definition);
                Require(copy.BaseStats.damage == 24 && copy.BaseStats.duration == 1f &&
                        copy.BaseStats.speed == .2f && copy.BaseStats.projectileCount == 1 && copy.BaseStats.critRate == 0,
                    "Validation requires authored Ovid damage/duration/speed/count/crit.");
                var source = copy.WeaponPrefab as OvidSummonAttackBehaviour;
                Require(source != null && source.InitialMaturityDelay == 3f, "Gameplay Ovid must use its three-second cocoon.");
                var emitter = Instantiate(source, staging.transform);
                typeof(OvidSummonAttackBehaviour).GetField("cacoonStateTime", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(emitter, FixtureCocoonSeconds);
                copy.WeaponPrefab = emitter;
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weapons);
            manager.playerPrefab.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(WeaponId);
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out var error), error);
            Debug.Log($"[SummonProcess] fixtureCocoon={FixtureCocoonSeconds} gameplayCocoon=3 sourceAttackUnchanged=true timeScale={Time.timeScale}");
            if (role == "host") manager.StartHost();
            else if (role == "server") manager.StartServer();
            else manager.StartClient();
            if (role != "server") StartCoroutine(Guard(VerifyOwner()));
            if (role == "host" || role == "server")
            {
                Mark("listening");
                yield return VerifyServer();
                if (role == "host") manager.StopHost(); else manager.StopServer();
            }
            else
            {
                while (!Has("stop")) yield return null;
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active)
                yield return null;
            yield return null;
            Require(FindObjectsByType<NetworkPlayerBootstrap>(FindObjectsSortMode.None).Length == 0, "Avatar survived scene shutdown.");
            Require(FindObjectsByType<SummonAIBehaviour>(FindObjectsSortMode.None).Length == 0, "World pet survived scene shutdown.");
            Mark("stopped-" + role);
        }

        private NetworkIdentity[] ServerPlayers() => NetworkServer.spawned.Values
            .Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();

        private IEnumerator VerifyServer()
        {
            while (ServerPlayers().Length != 2 || ServerPlayers().Any(p => !p.GetComponent<PlayerBuildRuntime>().IsBuildActive)) yield return null;
            manager.BeginRun();
            while (!Has("owner-ready-client") || !Has(role == "host" ? "owner-ready-host" : "owner-ready-client2") ||
                   !Has("spectator-sees-cocoon")) yield return null;
            NetworkIdentity returning = ServerPlayers().OrderBy(p => p.GetComponent<NetworkRunParticipant>().ParticipantId)
                .First(p => p.connectionToClient is not LocalConnectionToClient);
            uint oldAvatar = returning.netId;
            ulong participantId = returning.GetComponent<NetworkRunParticipant>().ParticipantId;
            PlayerSummonMaturitySnapshot initial = returning.GetComponent<NetworkWeaponCombatAdapter>().CaptureSummonMaturities().Single();
            Require(initial.WeaponId == WeaponId && initial.MaturityAt - NetworkTime.time > 3d,
                "Fixture startup consumed the cocoon before the intended disconnect.");
            Require(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(oldAvatar) == 0, "Cocoon unexpectedly admitted an attack.");
            VerifyServerHasNoPets();
            Mark("disconnect-cocoon");
            while (!Has("disconnected-cocoon") || NetworkServer.spawned.ContainsKey(oldAvatar)) yield return null;
            var retained = manager.Session.Participants.Single(p => p.Id == participantId);
            Require(retained.Checkpoint != null && retained.Checkpoint.SummonMaturities.Length == 1 &&
                    Math.Abs(retained.Checkpoint.SummonMaturities[0].MaturityAt - initial.MaturityAt) < .0001d,
                "Disconnect lost the absolute maturity deadline.");
            Require(!NetworkCombatWorld.Instance.Gateway.Attacks.RequiresAdmission(oldAvatar), "Old avatar retained attack admission.");
            Mark("cocoon-removed");
            double reconnectAt = NetworkTime.time + 2d;
            while (NetworkTime.time < reconnectAt) yield return null;
            Mark("reconnect-cocoon");
            while (!Has("resumed-cocoon")) yield return null;
            Require(retained.AvatarId != 0 && retained.AvatarId != oldAvatar, "Cocoon reconnect reused the old avatar.");
            var restoredAdapter = NetworkServer.spawned[retained.AvatarId].GetComponent<NetworkWeaponCombatAdapter>();
            Require(Math.Abs(restoredAdapter.CaptureSummonMaturities().Single().MaturityAt - initial.MaturityAt) < .0001d &&
                    restoredAdapter.AcceptedCooldownReportCount == 0,
                "Reconnect restarted cocoon time or admitted an attack before maturity.");
            NetworkIdentity[] players = ServerPlayers();
            foreach (NetworkIdentity player in players)
                NetworkCombatWorld.Instance.Gateway.Ledger.RegisterEntity(TargetId(player.netId), 1000,
                    CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            Mark("fire");
            while (!Has("verified-client") || !Has(role == "host" ? "verified-host" : "verified-client2")) yield return null;
            foreach (NetworkIdentity player in players)
            {
                string[] result = Read("owner-result-" + player.netId).Split(',');
                int hits = int.Parse(result[0]), damage = int.Parse(result[1]);
                Require(hits >= 1 && damage == hits * 24, "Owner did not observe authentic twenty-four-damage physics hits.");
                var gateway = NetworkCombatWorld.Instance.Gateway;
                while (!gateway.Ledger.TryGetState(TargetId(player.netId), out var state) || state.Health != 1000 - damage ||
                       gateway.Attacks.ActiveCount(player.netId) != 0) yield return null;
                var adapter = player.GetComponent<NetworkWeaponCombatAdapter>();
                Require(adapter.AcceptedCooldownReportCount == 1 && adapter.RejectedAttackCount == 0 &&
                        adapter.RejectedSummonPresentationCount == 0,
                    "A legal AI attack was rejected or admitted more than once.");
                Debug.Log($"[SummonProcess] canonical player={player.netId} hits={hits} damage={damage} hp={1000 - damage} admitted=1 activeRoots=0");
            }
            VerifyServerHasNoPets();
            // The same client now reconnects as an observer of the other player's persistent public pet.
            uint secondOldAvatar = retained.AvatarId;
            Mark("disconnect-spectator");
            while (!Has("disconnected-spectator") || NetworkServer.spawned.ContainsKey(secondOldAvatar)) yield return null;
            Require(!NetworkCombatWorld.Instance.Gateway.Attacks.RequiresAdmission(secondOldAvatar), "Second old avatar retained roots.");
            Mark("reconnect-spectator");
            while (!Has("resumed-spectator")) yield return null;
            Require(retained.AvatarId != secondOldAvatar &&
                    NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(retained.AvatarId) == 0,
                "Spectator reconnect replayed an old attack root.");
            VerifyServerHasNoPets();
            Debug.Log($"[SummonProcess] reconnect participant={participantId} maturityAt={initial.MaturityAt:F6} offlineClockContinued=true publicPetBaseline=true");
            Mark("stop");
            while (!Has("stopped-client") || (role == "server" && !Has("stopped-client2"))) yield return null;
        }

        private IEnumerator WaitForOwner()
        {
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                   !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive ||
                   NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon is not SummonAttackBehaviour summon ||
                   !summon.HasSimulationBinding || summon.ActiveSummon == null) yield return null;
        }

        private SummonAttackBehaviour OwnedWeapon => (SummonAttackBehaviour)NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;

        private IEnumerator VerifyOwner()
        {
            yield return WaitForOwner();
            Require(OwnedWeapon.ActiveSummon.Phase == SummonPhase.Cocoon && OwnedWeapon.CurrentSnapshot == null,
                "Initial pet did not begin in its native cocoon without an attack root.");
            Mark("owner-ready-" + role);
            if (role == "client") yield return DisconnectCocoonAndReconnect();
            while (!Has("fire")) yield return null;
            NetworkIdentity owner = NetworkClient.localPlayer;
            PlayerBuildRuntime build = owner.GetComponent<PlayerBuildRuntime>();
            SummonAttackBehaviour summon = OwnedWeapon;
            // A stationary query target six units away is inside the authored five-to-ten attack annulus.
            // Its broad trigger receives the real animated laser even during the source sweep.
            var targetObject = new GameObject("Summon physics target");
            targetObject.layer = LayerMask.NameToLayer("EnemyHitbox");
            targetObject.transform.position = summon.ActiveSummon.transform.position + Vector3.right * 6f;
            targetObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var collider = targetObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 30f;
            var target = targetObject.AddComponent<SummonProcessTarget>();
            target.Initialize(TargetId(owner.netId));
            // This query fixture is never spawned, but Mirror still requires an identity
            // when the agent's real health-change callback reads its zero netId.
            targetObject.SetActive(false);
            targetObject.AddComponent<NetworkIdentity>();
            targetObject.AddComponent<NetworkEnemySimulationAgent>().enabled = false;
            targetObject.SetActive(true);
            var phases = new List<SummonPresentationState> { summon.CapturePresentationState() };
            var roots = new HashSet<ulong>();
            var completed = new HashSet<ulong>();
            Action<SummonPresentationState> phaseHandler = phases.Add;
            Action<int, uint, MonsterSupergroup.GAS.CombatEventId> rootHandler = (slot, weapon, id) => roots.Add(id.Value);
            Action<int, uint, MonsterSupergroup.GAS.CombatEventId> completeHandler = (slot, weapon, id) => completed.Add(id.Value);
            summon.PresentationStateChanged += phaseHandler;
            build.NativeAttackStarted += rootHandler;
            build.NativeAttackCompleted += completeHandler;
            try
            {
                // No Attack(), manual ticks or fabricated hits: the enabled emitter drives the original FSM.
                while (completed.Count == 0) yield return null;
            }
            finally
            {
                summon.PresentationStateChanged -= phaseHandler;
                build.NativeAttackStarted -= rootHandler;
                build.NativeAttackCompleted -= completeHandler;
            }
            Require(target.HitCount >= 1 && target.TotalDamage == target.HitCount * 24,
                $"Authored laser produced {target.HitCount} hits and {target.TotalDamage} damage instead of twenty-four per hit.");
            Require(roots.Count == 1 && roots.SetEquals(completed) && roots.SetEquals(target.Roots) &&
                    target.HitEvents.Count == target.HitCount && target.SourcePlayers.SetEquals(new[] { owner.netId }),
                "Summon hits lost their one frozen root, unique hit events or Owner identity.");
            Require(summon.CurrentSnapshot == null && summon.ActiveSummon.Phase == SummonPhase.Positioning &&
                    phases.Last().AttackEventId == 0 &&
                    phases.Where(p => p.Phase >= SummonPhase.AttackEnter).All(p => roots.Contains(p.AttackEventId)),
                "Enter/Main/Exit did not finish under one root before returning the pet to Positioning.");
            foreach (SummonPhase phase in Enum.GetValues(typeof(SummonPhase)))
                Require(phases.Any(state => state.Phase == phase), "Native source skipped phase " + phase);
            ulong petId = summon.PetId;
            uint phaseSequence = phases.Last().PhaseSequence;
            targetObject.SetActive(false);
            Destroy(targetObject);
            File.WriteAllText(Path.Combine(directory, "owner-result-" + owner.netId),
                target.HitCount + "," + target.TotalDamage + "," + petId + "," + phaseSequence);
            Mark("source-complete-" + owner.netId);
            NetworkIdentity remote = RemotePlayer();
            while (remote == null || !Has("source-complete-" + remote.netId)) { yield return null; remote = RemotePlayer(); }
            var adapter = remote.GetComponent<NetworkWeaponCombatAdapter>();
            float deliveryDeadline = Time.realtimeSinceStartup + 5f;
            while ((!remoteObservations.TryGetValue(remote.netId, out var item) || item.Phases.Count < 6 ||
                    !item.PoseChanged || adapter.ReplicaSummonPoseCount < 2) && Time.realtimeSinceStartup < deliveryDeadline)
                yield return null;
            RemoteObservation observed = remoteObservations[remote.netId];
            Require(observed.Phases.Count == 6 && observed.PoseChanged && adapter.ReplicaSummonPoseCount >= 2 &&
                    adapter.ReplicaActiveSummonCount == 1 && adapter.RejectedSummonPresentationCount == 0,
                $"Remote pet did not replay all real phases/poses: phases={observed.Phases.Count}, poses={adapter.ReplicaSummonPoseCount}, changed={observed.PoseChanged}, active={adapter.ReplicaActiveSummonCount}, rejected={adapter.RejectedSummonPresentationCount}, droppedPoses={adapter.DroppedSummonPoseCount}.");
            if (!remote.isServer) Require(!remote.GetComponent<PlayerBuildRuntime>().IsBuildActive, "Remote executes another player's native Build.");
            if (role == "host" || role == "client2")
                Require(!ReferenceEquals(disconnectedRemoteAdapter, null) && disconnectedRemoteAdapter.ReplicaActiveSummonCount == 0,
                    "First disconnected avatar retained its world pet.");
            Debug.Log($"[SummonProcess] owner={owner.netId} pet={petId} hits={target.HitCount} damage={target.TotalDamage} sourcePhases={phases.Count} nativeRoots=1 completedRoots=1 remotePhases={observed.Phases.Count} remotePoses={adapter.ReplicaSummonPoseCount} remoteAI=false remoteDamage=0 colliderDisabledHits={target.ColliderDisabledHitCount} firstHit={target.FirstHitSeconds:F3} lastHit={target.LastHitSeconds:F3}");
            Mark("verified-" + role);
            if (role == "client") yield return DisconnectSpectatorAndReconnect();
        }

        private IEnumerator DisconnectCocoonAndReconnect()
        {
            while (!Has("disconnect-cocoon")) yield return null;
            NetworkIdentity previous = NetworkClient.localPlayer;
            uint avatar = previous.netId;
            ushort epoch = previous.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            SummonAttackBehaviour weapon = OwnedWeapon;
            ulong pet = weapon.PetId;
            double maturity = weapon.MaturityAt;
            double remainingBefore = maturity - NetworkTime.time;
            Require(weapon.ActiveSummon.Phase == SummonPhase.Cocoon && weapon.CurrentSnapshot == null, "Expected to disconnect during cocoon.");
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(FindObjectsByType<SummonAIBehaviour>(FindObjectsSortMode.None).Length == 0, "Disconnect left an owned or remote world pet.");
            Mark("disconnected-cocoon");
            while (!Has("reconnect-cocoon")) yield return null;
            manager.StartClient();
            yield return WaitForOwner();
            NetworkIdentity restored = NetworkClient.localPlayer;
            weapon = OwnedWeapon;
            Require(restored.netId != avatar && restored.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch != epoch &&
                    weapon.PetId != pet && Math.Abs(weapon.MaturityAt - maturity) < .0001d &&
                    maturity - NetworkTime.time < remainingBefore - 1.5d && weapon.CurrentSnapshot == null,
                "Reconnect reused identity, reset the cocoon deadline or replayed an old root.");
            SummonPhase expected = weapon.GetMaturityPhase(out _);
            Require(weapon.ActiveSummon.Phase == expected, "Restored pet did not seek its current absolute maturity phase.");
            Debug.Log($"[SummonProcess] cocoonResume oldAvatar={avatar} newAvatar={restored.netId} oldPet={pet} newPet={weapon.PetId} remainingBefore={remainingBefore:F3} remainingAfter={maturity - NetworkTime.time:F3} maturityAt={maturity:F6}");
            Mark("resumed-cocoon");
        }

        private IEnumerator DisconnectSpectatorAndReconnect()
        {
            while (!Has("disconnect-spectator")) yield return null;
            NetworkIdentity previous = NetworkClient.localPlayer;
            uint avatar = previous.netId;
            ushort epoch = previous.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            NetworkIdentity remote = RemotePlayer();
            uint remoteAvatar = remote.netId;
            string[] original = Read("owner-result-" + remoteAvatar).Split(',');
            ulong remotePetId = ulong.Parse(original[2]);
            uint remotePhaseSequence = uint.Parse(original[3]);
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(FindObjectsByType<SummonAIBehaviour>(FindObjectsSortMode.None).Length == 0, "Spectator disconnect leaked world pets.");
            Mark("disconnected-spectator");
            while (!Has("reconnect-spectator")) yield return null;
            manager.StartClient();
            yield return WaitForOwner();
            Require(NetworkClient.localPlayer.netId != avatar &&
                    NetworkClient.localPlayer.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch != epoch &&
                    OwnedWeapon.CurrentSnapshot == null && OwnedWeapon.ActiveSummon.Phase == SummonPhase.Positioning,
                "Second reconnect reset mature state or replayed an attack.");
            SummonAttackBehaviour replica = null;
            while (replica == null)
            {
                remote = RemotePlayer();
                if (remote != null && remote.netId == remoteAvatar)
                    replica = remote.GetComponentsInChildren<SummonAttackBehaviour>().FirstOrDefault(w => w.ActiveSummon != null);
                yield return null;
            }
            var adapter = remote.GetComponent<NetworkWeaponCombatAdapter>();
            while (adapter.ReplicaSummonPoseCount == 0) yield return null;
            // CapturePresentationState is the native sender API and reads GAS stats. Inspect
            // the received history instead; a replica must never acquire a runtime to do this.
            var history = (SummonPresentationHistory)typeof(NetworkWeaponCombatAdapter)
                .GetField("clientSummonHistory", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
            SummonPresentationState current = history.CaptureStates().Single(state => state.State.PetId == remotePetId).State;
            Require(replica.PetId == remotePetId && current.PhaseSequence >= remotePhaseSequence &&
                    current.Phase == SummonPhase.Positioning && current.AttackEventId == 0 &&
                    replica.ActiveSummon.IsPresentation && replica.NativeRuntime == null &&
                    adapter.ReceivedSummonStateCount == 1 && adapter.RejectedSummonPresentationCount == 0,
                "Reconnect spectator did not receive only the current persistent public pet baseline.");
            Debug.Log($"[SummonProcess] spectatorResume observedAvatar={remoteAvatar} pet={remotePetId} phase={current.Phase} stateEdges={adapter.ReceivedSummonStateCount} poseUpdates={adapter.ReplicaSummonPoseCount} oldAttackReplayed=false");
            Mark("resumed-spectator");
        }

        private static NetworkIdentity RemotePlayer() => NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned &&
            p.GetComponent<NetworkRunParticipant>() != null);

        private void VerifyServerHasNoPets()
        {
            if (role != "server") return;
            Require(!NetworkClient.active, "Server-only peer became a client.");
            Require(FindObjectsByType<SummonAIBehaviour>(FindObjectsSortMode.None).Length == 0 &&
                    FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length == 0,
                "Server-only peer created a pet AI or visual particle system.");
        }

        private static uint TargetId(uint player) => 0x6ffa0000u + player;
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(directory, name));
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log("[SummonProcess] result=" + (passed ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(passed ? 0 : 1);
        }

        private sealed class RemoteObservation
        {
            public NetworkWeaponCombatAdapter Adapter;
            public readonly HashSet<SummonPhase> Phases = new HashSet<SummonPhase>();
            public readonly HashSet<ulong> PetIds = new HashSet<ulong>();
            public SummonPose FirstPose;
            public bool HasPose, PoseChanged;
        }
    }

    public sealed class SummonProcessTarget : MonoBehaviour, IDamageable, INativeGasDamageable
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
            gameObject.AddComponent<CombatTeamBehaviour>().Configure(CombatTeam.Enemy, combatant);
        }
        public bool ResolveNativeGasHit(NativeGasHit hit)
        {
            float elapsed = Time.time - initializedAt;
            if (HitCount == 0) FirstHitSeconds = elapsed;
            LastHitSeconds = elapsed;
            var weapon = hit.PresentationWeapon as SummonAttackBehaviour;
            if (weapon == null || weapon.ActiveSummon == null || weapon.ActiveSummon.IsPresentation)
                throw new InvalidOperationException("A Summon hit did not originate from the native pet.");
            var box = ((OvidSummonAttackModule)weapon.ActiveSummon.AttackModule).HitBox;
            if (box.collider != null && !box.collider.enabled) ColliderDisabledHitCount++;
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
