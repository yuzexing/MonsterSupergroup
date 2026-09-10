using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Boot/transport, grants, admissions, real Animator colliders, GAS/Burn and replicas are production paths.
    [DefaultExecutionOrder(-31900)]
    public sealed class UltimateAttackProcessProbe : MonoBehaviour
    {
        private static readonly PropertyInfo PresentationOnly = typeof(BasePlayerAttack)
            .GetProperty("IsPresentationOnly", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo WaveSnapshot = typeof(BasePlayerAttack)
            .GetProperty("NativeAttackSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<uint, RemoteObservation> remoteObservations = new Dictionary<uint, RemoteObservation>();
        private string role, directory;
        private ushort port;
        private float deadline;
        private bool finished, driveRight;
        private BootGameplayNetworkManager manager;
        private NetworkPlayerUltimate disconnectingRemote;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--ultimate-role="));
            if (value == null || FindFirstObjectByType<UltimateAttackProcessProbe>() != null) return;
            var probe = new GameObject("Ultimate Process Validation").AddComponent<UltimateAttackProcessProbe>();
            probe.role = value.Split('=')[1];
            probe.directory = args.First(a => a.StartsWith("--ultimate-sync=")).Substring("--ultimate-sync=".Length);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--ultimate-port=")).Split('=')[1]);
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
                    // Ordinary weapons remain dormant; the separate Ultimate runtime keeps its real Update.
                    NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().SetWeaponExecutionEnabled(false);
                    if (driveRight) NetworkClient.localPlayer.GetComponent<PlayerMovement>().SetDirection(Vector2.right);
                }
                ObserveRemoteUltimate();
                if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Timed out in role " + role);
            }
            catch (Exception error) { Debug.LogException(error); Finish(false); }
        }

        private void ObserveRemoteUltimate()
        {
            if (!NetworkClient.active) return;
            foreach (NetworkIdentity remote in NetworkClient.spawned.Values.Where(p => p != null && !p.isOwned &&
                         p.GetComponent<NetworkRunParticipant>() != null))
            {
                var network = remote.GetComponent<NetworkPlayerUltimate>();
                if (!remoteObservations.TryGetValue(remote.netId, out RemoteObservation observation))
                    remoteObservations[remote.netId] = observation = new RemoteObservation { Network = network };
                foreach (DanteUltimateAttack attack in remote.GetComponentsInChildren<DanteUltimateAttack>())
                {
                    if (!attack.IsPresentationActive) continue;
                    Require(attack.NativeRuntime == null && attack.WeaponData == null && !attack.IsNativeActive &&
                            attack.OwnerPlayer == remote.GetComponent<PlayerMovement>() &&
                            attack.transform.IsChildOf(remote.transform) && attack.GetComponent<NetworkIdentity>() == null,
                        "Remote Ultimate acquired GAS or lost its player-scoped presentation parent.");
                    observation.Roots.Add(attack.ActiveUseId);
                    foreach (UltimateDamageAttack wave in attack.GetComponentsInChildren<UltimateDamageAttack>())
                    {
                        if (!wave.IsWavePlaying) continue;
                        Require((bool)PresentationOnly.GetValue(wave) && WaveSnapshot.GetValue(wave) == null &&
                                !wave.hitbox.collider.enabled && wave.transform.IsChildOf(attack.transform) &&
                                wave.GetComponent<NetworkIdentity>() == null,
                            "Remote wave acquired a snapshot, enabled collider or independent NetworkIdentity.");
                        observation.Waves.Add(wave.GetInstanceID());
                    }
                    if (!observation.HasPosition)
                    {
                        observation.HasPosition = true;
                        observation.PlayerPosition = remote.transform.position;
                        observation.RootPosition = attack.transform.position;
                    }
                    else if (Vector3.Distance(remote.transform.position, observation.PlayerPosition) > .1f &&
                        Vector3.Distance(attack.transform.position, observation.RootPosition) > .1f)
                        observation.FollowedPlayer = true;
                }
                if ((role == "host" || role == "client2") && network.ReplicaSpawnCount == 2 && network.HasRemotePresentation)
                {
                    disconnectingRemote = network;
                    Mark("spectator-sees-second");
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
            var configuration = manager.playerPrefab.GetComponent<NetworkPlayerUltimate>();
            Require(configuration != null && configuration.Definition != null && configuration.Definition.Id == 0 &&
                    configuration.Definition.BaseStats.damage == 100 && configuration.Definition.BaseStats.critRate == 0,
                "Validation requires the real Dante Ultimate definition with its original ID and damage.");
            var source = configuration.Definition.ultimateAttackWeaponBehaviour as DanteUltimateAttack;
            Require(source != null && source.burnStrength == .1f && source.burnDuration == 4f && source.burnRate == .5f &&
                    UltimateNativeDefinitionAdapter.EncodeAbilityId(configuration.Definition.Id) == 0x80000000u,
                "Dante's original intrinsic Burn or ability namespace changed.");
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out var error), error);
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
            Require(FindObjectsByType<UltimateDamageAttack>(FindObjectsSortMode.None).Length == 0 &&
                    FindObjectsByType<DanteUltimateAttack>(FindObjectsSortMode.None).Length == 0,
                "An active Ultimate survived scene shutdown.");
            Mark("stopped-" + role);
        }

        private NetworkIdentity[] ServerPlayers() => NetworkServer.spawned.Values
            .Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();

        private IEnumerator VerifyServer()
        {
            while (ServerPlayers().Length != 2 || ServerPlayers().Any(p => !p.GetComponent<PlayerBuildRuntime>().IsBuildActive)) yield return null;
            manager.BeginRun();
            while (!Has("owner-ready-client") || !Has(role == "host" ? "owner-ready-host" : "owner-ready-client2")) yield return null;
            NetworkIdentity[] players = ServerPlayers();
            foreach (NetworkIdentity player in players)
            {
                var ultimate = player.GetComponent<NetworkPlayerUltimate>();
                Require(!ultimate.CaptureServerState().HasCharge && ultimate.ServerGrantCharge() && !ultimate.ServerGrantCharge(),
                    "Grant must create exactly one held charge.");
                RegisterTarget(player.netId);
            }
            VerifyServerHasNoVisuals();
            Mark("fire");
            while (!Has("verified-client") || !Has(role == "host" ? "verified-host" : "verified-client2")) yield return null;
            foreach (NetworkIdentity player in players) yield return VerifyCanonicalCast(player, "first");
            NetworkIdentity returning = players.OrderBy(p => p.GetComponent<NetworkRunParticipant>().ParticipantId)
                .First(p => p.connectionToClient is not LocalConnectionToClient);
            uint oldAvatar = returning.netId;
            ulong participantId = returning.GetComponent<NetworkRunParticipant>().ParticipantId;
            var returningUltimate = returning.GetComponent<NetworkPlayerUltimate>();
            Require(returningUltimate.ServerGrantCharge(), "Second legitimate charge could not be granted.");
            Mark("second-use");
            while (returningUltimate.AcceptedUseCount < 2 || !Has("spectator-sees-second")) yield return null;
            PlayerUltimateSnapshot spent = returningUltimate.CaptureServerState();
            Require(!spent.HasCharge && spent.ActiveUntil > NetworkTime.time &&
                    NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(oldAvatar) == 1,
                "Disconnect fixture must contain one consumed, active admitted Ultimate.");
            Mark("second-admitted");
            while (!Has("disconnected-client") || NetworkServer.spawned.ContainsKey(oldAvatar)) yield return null;
            var retained = manager.Session.Participants.Single(p => p.Id == participantId);
            Require(retained.Checkpoint?.Ultimate.HasValue == true && !retained.Checkpoint.Ultimate.Value.HasCharge &&
                    retained.Checkpoint.Ultimate.Value.ActiveUntil == spent.ActiveUntil &&
                    retained.Checkpoint.Ultimate.Value.InvulnerableUntil == spent.InvulnerableUntil,
                "Disconnect refunded charge or discarded the absolute deadlines.");
            Require(!NetworkCombatWorld.Instance.Gateway.Attacks.RequiresAdmission(oldAvatar), "Disconnected avatar retained root admission.");
            double reconnectAt = NetworkTime.time + 1d;
            while (NetworkTime.time < reconnectAt) yield return null;
            Mark("reconnect");
            while (!Has("resumed-client")) yield return null;
            Require(retained.AvatarId != oldAvatar && retained.AvatarId != 0, "Reconnect reused the old avatar.");
            NetworkIdentity restored = NetworkServer.spawned[retained.AvatarId];
            var restoredUltimate = restored.GetComponent<NetworkPlayerUltimate>();
            PlayerUltimateSnapshot restoredState = restoredUltimate.CaptureServerState();
            Require(!restoredState.HasCharge && restoredState.ActiveUntil == spent.ActiveUntil &&
                    restoredState.InvulnerableUntil == spent.InvulnerableUntil &&
                    NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(restored.netId) == 0,
                "Reconnect restarted the spent cast, changed deadlines or replayed its root.");
            while (NetworkTime.time < spent.ActiveUntil + .1d) yield return null;
            RegisterTarget(restored.netId);
            Require(restoredUltimate.ServerGrantCharge(), "Reconnected runtime could not receive a fresh server charge.");
            Mark("refire");
            while (!Has("verified-resumed")) yield return null;
            yield return VerifyCanonicalCast(restored, "resumed");
            if (role == "host" || role == "server")
            {
                Mark("verify-spectator-cleanup");
                while (!Has("spectator-cleanup-verified")) yield return null;
            }
            yield return VerifyServerExecutionLifecycle(restored);
            VerifyServerHasNoVisuals();
            Debug.Log($"[UltimateProcess] reconnect participant={participantId} oldAvatar={oldAvatar} newAvatar={retained.AvatarId} spentDeadline={spent.ActiveUntil:F6} noReplay=true freshGrantVerified=true");
            Mark("stop");
            while (!Has("stopped-client") || (role == "server" && !Has("stopped-client2"))) yield return null;
        }

        private void RegisterTarget(uint player) => NetworkCombatWorld.Instance.RegisterEntity(TargetId(player), 1000,
            CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);

        private IEnumerator VerifyCanonicalCast(NetworkIdentity player, string label, int expectedAcceptedUses = 1)
        {
            string[] values = Read("cast-" + label + "-" + player.netId).Split(',');
            int direct = int.Parse(values[0]), ticks = int.Parse(values[1]), statusDamage = int.Parse(values[2]);
            Require(direct == 200 && ticks > 0 && statusDamage == ticks * 10, "Cast did not produce two original hits and legal intrinsic Burn ticks.");
            var gateway = NetworkCombatWorld.Instance.Gateway;
            while (!gateway.Ledger.TryGetState(TargetId(player.netId), out var state) || state.Health != 1000 - direct - statusDamage ||
                   gateway.Attacks.ActiveCount(player.netId) != 0 || gateway.Statuses.GetForTarget(TargetId(player.netId)).Count != 0) yield return null;
            var ultimate = player.GetComponent<NetworkPlayerUltimate>();
            Require(ultimate.AcceptedUseCount == expectedAcceptedUses && ultimate.RejectedUseCount == 0 && !ultimate.CaptureServerState().HasCharge,
                "A cast consumed twice, used a rejected admission or refunded its charge.");
            Require(player.GetComponent<PlayerBuildRuntime>().WeaponCount == 1,
                "Ultimate incorrectly occupied an ordinary weapon slot.");
            Debug.Log($"[UltimateProcess] canonical label={label} player={player.netId} direct={direct} burnTicks={ticks} burnDamage={statusDamage} hp={1000 - direct - statusDamage} activeRoot=0 activeStatus=0 consumed={expectedAcceptedUses}");
        }

        private IEnumerator VerifyServerExecutionLifecycle(NetworkIdentity owner)
        {
            var ultimate = owner.GetComponent<NetworkPlayerUltimate>();
            Require(owner.connectionToClient is not LocalConnectionToClient && ultimate.ServerGrantCharge(),
                "Server disable must exercise a separate Owner process with one fresh grant.");
            int usesBefore = ultimate.AcceptedUseCount;
            Mark("server-disable-use");
            while (!Has("server-disable-active-client") || !Has("server-disable-seen-remote") ||
                !Field<NetworkUltimatePresentationSpawn?>(ultimate, "serverPresentation").HasValue) yield return null;
            ulong root = ulong.Parse(Read("server-disable-active-client"));
            var gateway = NetworkCombatWorld.Instance.Gateway;
            PlayerUltimateSnapshot spent = ultimate.CaptureServerState();
            Require(ultimate.AcceptedUseCount == usesBefore + 1 && !spent.HasCharge && spent.ActiveUntil > NetworkTime.time &&
                gateway.Attacks.Contains(owner.netId, root, 0x80000000u), "Server cancellation fixture has no active consumed root.");
            ultimate.enabled = false; // This changes only the authority process; the Owner and spectator stay enabled.
            Require(!ultimate.enabled && gateway.Attacks.ActiveCount(owner.netId) == 0 &&
                Field<ulong>(ultimate, "serverRootId") == 0 &&
                !Field<NetworkUltimatePresentationSpawn?>(ultimate, "serverPresentation").HasValue,
                "Disabling only the server coordinator did not retire its root and presentation cache immediately.");
            RequireSameSpentState(ultimate.CaptureServerState(), spent, "Server disable changed the spent resource.");
            VerifyServerHasNoVisuals();
            Mark("server-disabled");
            while (!Has("server-disable-owner-cancelled") || !Has("server-disable-remote-ended")) yield return null;
            Require(!ultimate.enabled && !gateway.Attacks.Contains(owner.netId, root, 0x80000000u),
                "Owner cancellation depended on re-enabling the server or revived the retired root.");
            ultimate.enabled = true;
            Mark("server-enabled");
            while (!Has("server-disable-owner-resumed")) yield return null;
            RequireSameSpentState(ultimate.CaptureServerState(), spent, "Server enable restarted or refunded the cancelled cast.");
            while (NetworkTime.time < spent.ActiveUntil + .1d) yield return null;
            RegisterTarget(owner.netId);
            Require(ultimate.ServerGrantCharge(), "Server enable could not grant a fresh use after the retained deadline.");
            Mark("server-disable-refire");
            while (!Has("verified-server-reenabled")) yield return null;
            yield return VerifyCanonicalCast(owner, "server-reenabled", usesBefore + 2);
            while (!Has("server-disable-spectator-finished")) yield return null;
            Debug.Log($"[UltimateProcess] server-disable role={role} player={owner.netId} root={root} separateOwner=true immediateRetire=true ownerStopped=true remoteStopped=true deadline={spent.ActiveUntil:F6} freshCastVerified=true");
        }

        private IEnumerator VerifyOwnerServerExecutionLifecycle()
        {
            while (!Has("server-disable-use")) yield return null;
            var ultimate = NetworkClient.localPlayer.GetComponent<NetworkPlayerUltimate>();
            while (!ultimate.HasCharge) yield return null;
            Require(ultimate.RequestUse(), "Granted server-disable fixture use was refused.");
            while (!ultimate.OwnerAttack.IsNativeActive) yield return null;
            DanteUltimateAttack attack = ultimate.OwnerAttack;
            ulong root = attack.ActiveUseId;
            AttackSnapshot snapshot = Field<AttackSnapshot>(attack, "activeSnapshot");
            PlayerUltimateSnapshot spent = Field<PlayerUltimateRuntime>(ultimate, "ownerView").Capture();
            File.WriteAllText(Path.Combine(directory, "server-disable-active-client"), root.ToString());
            while (!Has("server-disabled") || attack.IsNativeActive || !Field<bool>(ultimate, "ownerServerExecutionSuspended")) yield return null;
            Require(ultimate.enabled && ReferenceEquals(ultimate.OwnerAttack, attack) && snapshot.IsDisposed &&
                Field<ulong>(ultimate, "pendingUseId") == 0 && !ultimate.RequestUse(),
                "Server-only disable did not stop the enabled Owner through its reliable cancellation.");
            RequireSameSpentState(Field<PlayerUltimateRuntime>(ultimate, "ownerView").Capture(), spent,
                "Owner cancellation changed authoritative charge or absolute deadlines.");
            Mark("server-disable-owner-cancelled");
            while (!Has("server-enabled") || Field<bool>(ultimate, "ownerServerExecutionSuspended")) yield return null;
            Require(!attack.IsNativeActive && !ultimate.HasCharge && !ultimate.RequestUse(),
                "Server resume replayed the cancelled native root or refunded charge.");
            RequireSameSpentState(Field<PlayerUltimateRuntime>(ultimate, "ownerView").Capture(), spent,
                "Owner resume reset the retained deadline.");
            Mark("server-disable-owner-resumed");
            while (!Has("server-disable-refire") || !ultimate.HasCharge) yield return null;
            yield return VerifyCast("server-reenabled");
            Mark("verified-server-reenabled");
        }

        private IEnumerator VerifySpectatorServerExecutionLifecycle()
        {
            while (!Has("server-disable-use")) yield return null;
            var remote = RemotePlayer().GetComponent<NetworkPlayerUltimate>();
            while (remote.ReplicaSpawnCount < 2 || !remote.HasRemotePresentation) yield return null;
            Require(remote.enabled && remote.ReplicaSpawnCount == 2,
                "The spectator must remain enabled and observe the cancellation fixture's actual second root.");
            Mark("server-disable-seen-remote");
            while (!Has("server-disabled") || remote.HasRemotePresentation) yield return null;
            // The Host's remote view and authoritative avatar share this component.
            // The dedicated run has a separate spectator process which must stay enabled.
            Require(remote.enabled == (role != "host"),
                "The spectator lifecycle does not match its Host/shared or independent client role.");
            Mark("server-disable-remote-ended");
            while (!Has("server-enabled")) yield return null;
            Require(!remote.HasRemotePresentation && remote.ReplicaSpawnCount == 2,
                "Server enable replayed its cancelled presentation cache.");
            while (!Has("verified-server-reenabled") || remote.HasRemotePresentation) yield return null;
            Require(remote.ReplicaSpawnCount == 3 && remoteObservations[remote.netId].Roots.Count == 3,
                "A fresh use after server enable did not replay exactly one independent presentation root.");
            Mark("server-disable-spectator-finished");
        }

        private static T Field<T>(object target, string name) => (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void RequireSameSpentState(PlayerUltimateSnapshot actual, PlayerUltimateSnapshot expected, string message) =>
            Require(actual.HasCharge == expected.HasCharge && actual.ActiveUntil == expected.ActiveUntil &&
                actual.InvulnerableUntil == expected.InvulnerableUntil, message);

        private IEnumerator WaitForOwner()
        {
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                   !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive ||
                   NetworkClient.localPlayer.GetComponent<NetworkPlayerUltimate>().OwnerAttack == null) yield return null;
        }

        private IEnumerator VerifyOwner()
        {
            yield return WaitForOwner();
            var ultimate = NetworkClient.localPlayer.GetComponent<NetworkPlayerUltimate>();
            Require(!ultimate.HasCharge && !ultimate.RequestUse(), "An uncharged player used Ultimate.");
            Mark("owner-ready-" + role);
            while (!Has("fire") || !ultimate.HasCharge) yield return null;
            yield return VerifyCast("first");
            NetworkIdentity remote = RemotePlayer();
            while (remote == null || !Has("source-complete-" + remote.netId)) { yield return null; remote = RemotePlayer(); }
            var remoteUltimate = remote.GetComponent<NetworkPlayerUltimate>();
            float deliveryDeadline = Time.realtimeSinceStartup + 5f;
            while ((!remoteObservations.TryGetValue(remote.netId, out var item) || item.Waves.Count < 2 ||
                    !item.FollowedPlayer || remoteUltimate.HasRemotePresentation) && Time.realtimeSinceStartup < deliveryDeadline) yield return null;
            RemoteObservation observed = remoteObservations[remote.netId];
            Require(remoteUltimate.ReplicaSpawnCount == 1 && !remoteUltimate.HasRemotePresentation &&
                    observed.Waves.Count == 2 && observed.Roots.Count == 1 && observed.FollowedPlayer,
                "Remote did not replay two owner-following waves and finish its single presentation root.");
            // Natural presentation expiry can precede the reliable terminal; do not require RPC to win that race.
            Debug.Log($"[UltimateProcess] remote player={remote.netId} roots=1 waves=2 followed=true gas=false damage=0 active=false effectiveEnds={remoteUltimate.ReplicaTerminationCount}");
            Mark("verified-" + role);
            if (role == "client") yield return DisconnectDuringUseAndReconnect();
            else
            {
                while (!Has("verify-spectator-cleanup")) yield return null;
                Require(!ReferenceEquals(disconnectingRemote, null) && !disconnectingRemote.HasRemotePresentation,
                    "Old disconnected avatar retained its Ultimate replica.");
                NetworkIdentity resumed = RemotePlayer();
                Require(resumed != null && resumed.GetComponent<NetworkPlayerUltimate>().ReplicaSpawnCount == 1 &&
                        !resumed.GetComponent<NetworkPlayerUltimate>().HasRemotePresentation &&
                        remoteObservations[resumed.netId].Waves.Count == 2,
                    "Fresh grant after reconnect did not complete through the new avatar's real remote presentation.");
                Mark("spectator-cleanup-verified");
                yield return VerifySpectatorServerExecutionLifecycle();
            }
        }

        private IEnumerator VerifyCast(string label)
        {
            NetworkIdentity owner = NetworkClient.localPlayer;
            var ultimate = owner.GetComponent<NetworkPlayerUltimate>();
            var build = owner.GetComponent<PlayerBuildRuntime>();
            var bridge = owner.GetComponent<MirrorNetworkCombatBridge>();
            DanteUltimateAttack attack = ultimate.OwnerAttack;
            var targetObject = new GameObject("Ultimate real wave target");
            targetObject.layer = LayerMask.NameToLayer("EnemyHitbox");
            targetObject.transform.position = owner.transform.position;
            targetObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var collider = targetObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 100f;
            var target = targetObject.AddComponent<UltimateProcessTarget>();
            target.Initialize(TargetId(owner.netId), bridge);
            var started = new List<byte>(); var ended = new List<byte>(); var completed = new List<ulong>();
            Action<byte> onStart = started.Add, onEnd = ended.Add;
            Action<ulong> onComplete = completed.Add;
            attack.WaveStarted += onStart; attack.WaveEnded += onEnd; attack.NativeAttackCompleted += onComplete;
            Vector3 firstOwnerPosition = owner.transform.position;
            Vector3 firstAttackPosition = attack.transform.position;
            bool followed = false;
            try
            {
                driveRight = true;
                Require(ultimate.RequestUse(), "A server-granted Ultimate request failed locally.");
                Require(!ultimate.RequestUse(), "Same-frame duplicate Ultimate request was accepted.");
                while (completed.Count == 0)
                {
                    if (attack.IsNativeActive)
                    {
                        Require(attack.transform.IsChildOf(owner.GetComponent<PlayerMovement>().AttacksParent) &&
                                attack.NativeRuntime != null && attack.NativeRuntime.CombatId == 0x80000000u,
                            "Native Ultimate lost its per-player parent or namespaced GAS runtime.");
                        if (Vector3.Distance(owner.transform.position, firstOwnerPosition) > .1f &&
                            Vector3.Distance(attack.transform.position, firstAttackPosition) > .1f) followed = true;
                    }
                    yield return null;
                }
            }
            finally
            {
                driveRight = false;
                if (owner != null) owner.GetComponent<PlayerMovement>().SetDirection(Vector2.zero);
                attack.WaveStarted -= onStart; attack.WaveEnded -= onEnd; attack.NativeAttackCompleted -= onComplete;
            }
            Require(started.SequenceEqual(new byte[] { 0, 1 }) && ended.SequenceEqual(new byte[] { 0, 1 }) &&
                    completed.Count == 1 && target.DirectHitCount == 2 && target.DirectDamage == 200 &&
                    target.WaveInstances.Count == 2 && target.DirectEvents.Count == 2 && target.Roots.SetEquals(completed) && followed,
                $"Native Animator did not deliver both real waves under one root: starts={started.Count} ends={ended.Count} hits={target.DirectHitCount} damage={target.DirectDamage} waves={target.WaveInstances.Count}.");
            Require(build.WeaponCount == 1 && !ultimate.HasCharge && !ultimate.RequestUse() && !attack.IsNativeActive,
                "Completed Ultimate consumed a weapon slot or regained charge.");
            while (target.Combatant.HasStatus(MonsterSupergroup.GAS.EnemyStatusID.Burn)) yield return null;
            // The real HighestPriority equal-priority refresh resets the tick schedule on the second wave.
            // Observe the actual tick count; neither time-scale acceleration nor an invented fixed total is used.
            Require(target.PredictedBurnApplications == 2 && target.BurnTickCount > 0 &&
                    target.StatusDamage == target.BurnTickCount * 10 && target.StatusEvents.Count == target.BurnTickCount &&
                    target.StatusRoots.SetEquals(completed), "Intrinsic Burn did not retain its original parameters and root lineage.");
            bridge.Flush();
            File.WriteAllText(Path.Combine(directory, "cast-" + label + "-" + owner.netId),
                target.DirectDamage + "," + target.BurnTickCount + "," + target.StatusDamage + "," + completed[0]);
            Debug.Log($"[UltimateProcess] source label={label} player={owner.netId} root={completed[0]} waves=2 direct={target.DirectDamage} burnTicks={target.BurnTickCount} burnDamage={target.StatusDamage} applications={target.PredictedBurnApplications} followed={followed} firstHit={target.FirstHitSeconds:F3} secondHit={target.LastHitSeconds:F3}");
            targetObject.SetActive(false);
            Destroy(targetObject);
            Mark("source-complete-" + owner.netId);
        }

        private IEnumerator DisconnectDuringUseAndReconnect()
        {
            while (!Has("second-use")) yield return null;
            NetworkIdentity old = NetworkClient.localPlayer;
            uint oldAvatar = old.netId;
            ushort oldEpoch = old.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            var ultimate = old.GetComponent<NetworkPlayerUltimate>();
            while (!ultimate.HasCharge) yield return null;
            Require(ultimate.RequestUse(), "Second granted use was refused.");
            while (!Has("second-admitted")) yield return null;
            Require(ultimate.OwnerAttack.IsNativeActive, "Expected to disconnect during a real active Ultimate.");
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(FindObjectsByType<DanteUltimateAttack>(FindObjectsSortMode.None).Length == 0 &&
                    FindObjectsByType<UltimateDamageAttack>(FindObjectsSortMode.None).Length == 0,
                "Disconnect left old native or remote Ultimate waves.");
            Mark("disconnected-client");
            while (!Has("reconnect")) yield return null;
            manager.StartClient();
            yield return WaitForOwner();
            NetworkIdentity restored = NetworkClient.localPlayer;
            ultimate = restored.GetComponent<NetworkPlayerUltimate>();
            Require(restored.netId != oldAvatar && restored.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch != oldEpoch &&
                    !ultimate.HasCharge && !ultimate.RequestUse() && !ultimate.OwnerAttack.IsNativeActive &&
                    ultimate.OwnerAttack.ActiveUseId == 0 && restored.GetComponent<PlayerBuildRuntime>().WeaponCount == 1,
                "Reconnect reset spent charge, reused epoch, occupied a weapon slot or replayed the old Ultimate.");
            Mark("resumed-client");
            while (!Has("refire") || !ultimate.HasCharge) yield return null;
            yield return VerifyCast("resumed");
            Mark("verified-resumed");
            yield return VerifyOwnerServerExecutionLifecycle();
        }

        private void VerifyServerHasNoVisuals()
        {
            if (role != "server") return;
            Require(!NetworkClient.active && FindObjectsByType<DanteUltimateAttack>(FindObjectsSortMode.None).Length == 0 &&
                    FindObjectsByType<UltimateDamageAttack>(FindObjectsSortMode.None).Length == 0 &&
                    FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length == 0,
                "Server-only peer created Ultimate presentation or particle systems.");
        }
        private static NetworkIdentity RemotePlayer() => NetworkClient.spawned.Values.FirstOrDefault(p => p != null && !p.isOwned &&
            p.GetComponent<NetworkRunParticipant>() != null);
        private static uint TargetId(uint player) => 0x6ff90000u + player;
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(directory, name));
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log("[UltimateProcess] result=" + (passed ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(passed ? 0 : 1);
        }
        private sealed class RemoteObservation
        {
            public NetworkPlayerUltimate Network;
            public readonly HashSet<ulong> Roots = new HashSet<ulong>();
            public readonly HashSet<int> Waves = new HashSet<int>();
            public Vector3 PlayerPosition, RootPosition;
            public bool HasPosition, FollowedPlayer;
        }
    }

    public sealed class UltimateProcessTarget : MonoBehaviour, IDamageable, INativeGasDamageable, ICombatEventSink
    {
        private static readonly FieldInfo HitEntries = typeof(BaseAttackHitBox).GetField("_hitEntries", BindingFlags.Instance | BindingFlags.NonPublic);
        private MirrorNetworkCombatBridge bridge;
        private NetworkCombatWorld world;
        private float initializedAt;
        public CombatantBehaviour Combatant { get; private set; }
        public int DirectHitCount { get; private set; }
        public int DirectDamage { get; private set; }
        public int BurnTickCount { get; private set; }
        public int StatusDamage { get; private set; }
        public int PredictedBurnApplications { get; private set; }
        public float FirstHitSeconds { get; private set; }
        public float LastHitSeconds { get; private set; }
        public readonly HashSet<int> WaveInstances = new HashSet<int>();
        public readonly HashSet<ulong> DirectEvents = new HashSet<ulong>();
        public readonly HashSet<ulong> StatusEvents = new HashSet<ulong>();
        public readonly HashSet<ulong> Roots = new HashSet<ulong>();
        public readonly HashSet<ulong> StatusRoots = new HashSet<ulong>();

        public void Initialize(uint id, MirrorNetworkCombatBridge ownerBridge)
        {
            initializedAt = Time.time;
            bridge = ownerBridge;
            world = NetworkCombatWorld.Instance;
            Combatant = gameObject.AddComponent<CombatantBehaviour>();
            Combatant.Initialize(1000);
            Combatant.ConfigureEntityId(id);
            Combatant.ConfigureStatusExecution(new StatusExecutionScope(false, NetworkServer.active, bridge.OwnerPlayerId, 0));
            Combatant.ConfigureStatusInstanceIds(new CombatEventStatusInstanceIdSource(bridge.EventIds));
            Combatant.ConfigureStatusCombatEvents(bridge.EventIds, this);
            bridge.ObserveStatus(Combatant.StatusController);
            Combatant.StatusController.Changed += ObserveStatus;
            Combatant.StatusDamageReceived += ObserveStatusTick;
            world.Replica.RegisterStatusController(id, Combatant.StatusController);
            world.Replica.EntityChanged += ApplyCanonical;
            gameObject.AddComponent<StatusUpdateDriver>().Configure(Combatant);
        }

        public bool ResolveNativeGasHit(NativeGasHit hit)
        {
            var source = hit.PresentationWeapon as DanteUltimateAttack;
            if (source == null || !source.IsNativeActive || hit.Runtime.CombatId != 0x80000000u)
                throw new InvalidOperationException("A non-native Ultimate attempted gameplay damage.");
            UltimateDamageAttack wave = source.GetComponentsInChildren<UltimateDamageAttack>().FirstOrDefault(item =>
                item.IsWavePlaying && !WaveInstances.Contains(item.GetInstanceID()) &&
                ((HashSet<int>)HitEntries.GetValue(item.hitbox)).Contains(GetID()));
            if (wave == null) throw new InvalidOperationException("A direct hit was not the first real physics hit from a distinct authored wave.");
            WaveInstances.Add(wave.GetInstanceID());
            var result = hit.Runtime.ResolveHitDetailed(hit.Attack, Combatant);
            if (result.HitContext.SourcePlayerId != bridge.OwnerPlayerId || result.ResolvedDamage.Value != 100)
                throw new InvalidOperationException("Ultimate hit used another Owner or changed original damage.");
            if (DirectHitCount == 0) FirstHitSeconds = Time.time - initializedAt;
            LastHitSeconds = Time.time - initializedAt;
            DirectHitCount++;
            DirectDamage += result.ResolvedDamage.Value;
            DirectEvents.Add(result.HitContext.EventId.Value);
            Roots.Add(result.HitContext.RootEventId.Value);
            return true;
        }

        private void ObserveStatus(StatusChange change)
        {
            if (change.Origin != StatusStateOrigin.Predicted || change.Kind == StatusChangeKind.Removed) return;
            StatusInstance status = change.Instance;
            if (status.DefinitionId != MonsterSupergroup.GAS.EnemyStatusID.Burn || status.SourcePlayerId != bridge.OwnerPlayerId ||
                status.TickDamage != 10 || status.TotalTicks != 8 || status.TickInterval != .5f ||
                status.SourceContext.AbilityId != 0x80000000u || status.SourceContext.BuildId != OnHitBurnModifier.ModifierIdValue)
                throw new InvalidOperationException("Ultimate intrinsic Burn did not use the existing stable modifier and original parameters.");
            PredictedBurnApplications++;
        }

        private void ObserveStatusTick(StatusTick tick, MonsterSupergroup.GAS.DamageInfo damage)
        {
            if (tick.StatusId != MonsterSupergroup.GAS.EnemyStatusID.Burn || damage.Value != 10 ||
                tick.Instance.SourcePlayerId != bridge.OwnerPlayerId)
                throw new InvalidOperationException("A Burn tick used another Owner or invalid damage.");
            BurnTickCount++;
            StatusDamage += damage.Value;
        }

        public void Publish(CombatEvent combatEvent)
        {
            if (combatEvent.Kind == CombatEventKind.DamageResolved)
            {
                StatusEvents.Add(combatEvent.Context.EventId.Value);
                StatusRoots.Add(combatEvent.Context.RootEventId.Value);
            }
            // This observer never fabricates an event; the real Combatant emitted it with the owner's IDs.
            bridge.Collector.Publish(combatEvent);
        }

        private void ApplyCanonical(CanonicalEntityState state)
        {
            if (Combatant != null && state.EntityId == Combatant.EntityId)
                Combatant.ApplyCanonicalHealth(state.Health, state.MaxHealth, state.StateVersion);
        }

        private void OnDestroy()
        {
            if (Combatant == null) return;
            Combatant.StatusController.Changed -= ObserveStatus;
            Combatant.StatusDamageReceived -= ObserveStatusTick;
            if (world != null)
            {
                world.Replica.UnregisterStatusController(Combatant.EntityId, Combatant.StatusController);
                world.Replica.EntityChanged -= ApplyCanonical;
            }
            bridge?.Collector?.StopObserving(Combatant.StatusController);
            Combatant.ClearStatusCombatEvents(this);
        }
        public int GetID() => (int)Combatant.EntityId;
        public Vector2 GetPosition() => transform.position;
        public bool IsActive() => Combatant.IsAlive;
        public void Damage(Vector2 position, WeaponBehaviour weapon, LegacyDamageType type) => throw new InvalidOperationException("Legacy damage was invoked.");
        public void Damage(int value, LegacyDamageType type) => throw new InvalidOperationException("Legacy damage was invoked.");
    }
}
