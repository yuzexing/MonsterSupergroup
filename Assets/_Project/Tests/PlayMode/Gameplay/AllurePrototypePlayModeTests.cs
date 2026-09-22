#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class AllurePrototypePlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture enemies;
        private GameObject[] roots;
        private GameObject gate;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkPlayerPrototypeAbilities Abilities => Owner.GetComponent<NetworkPlayerPrototypeAbilities>();
        private NetworkPlayerAllure Allure => Owner.GetComponent<NetworkPlayerAllure>();
        private NetworkEnemySimulationWorld World => NetworkEnemySimulationWorld.Instance;

        [UnitySetUp]
        public IEnumerator Start()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
            if (!Application.isBatchMode) UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");
            GameOptionsService.EnsureInitialized();
            yield return Wait(() => GameLocalization.IsReady, "localization startup");
            yield return null;
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
            roots = BootSceneFixtureObjects.Capture(boot);
            manager = UnityEngine.Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            manager.ConfigurePreparationFlow(false);
            enemies = new EnemyDefinitionRuntimeFixture(manager, resetOnReposition: false);
            gate = new GameObject("Allure test automatic attack gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7999, false, out string error), Is.True, error);
            manager.StartHost();
            var gluttony = GluttonyParameters.Defaults; gluttony.PassiveEnabled = false;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(gluttony, true);
            yield return Wait(() => Owner != null && Abilities.OwnerReady && manager.CanBeginRun(out _), "owner and run ready");
            Owner.GetComponent<PlayerMovement>().SetDirection(Vector2.zero);
            manager.BeginRun();
            yield return Wait(() => enemies.PairReady() && enemies.PlaceInView(), "ordinary enemies ready");
            yield return Wait(() => World.TryGetPlayerView(Owner.netId, out _) &&
                enemies.Agents().All(a => World.IsInPlayerView(Owner.netId, a)), "real owner camera report");
            yield return Select(PrototypeAbilityId.Allure);
        }

        [UnityTest]
        public IEnumerator SoloThrowAndTakeExplainRejectionWithoutChargingCooldown()
        {
            uint revision = Allure.State.Revision;
            yield return Cast(AllureAction.Throw, false);
            Assert.That(Allure.LastResult, Is.Not.Empty);
            Assert.That(Allure.CooldownRemaining(AllureAction.Throw), Is.Zero);
            yield return Cast(AllureAction.Take, false);
            Assert.That(Allure.LastResult, Is.Not.Empty);
            Assert.That(Allure.CooldownRemaining(AllureAction.Take), Is.Zero);
            Assert.That(Allure.State.Revision, Is.EqualTo(revision));
            yield return Cast(AllureAction.Decoy, true);
            Assert.That(Allure.State.LastAffectedCount, Is.EqualTo(2), "solo F remains available");
        }

        [UnityTest]
        public IEnumerator DecoySelectsNearestWithinCapWithoutRenewingSimulationEpoch()
        {
            var parameters = AllureParameters.Defaults; parameters.MaximumTargets = 1;
            NetworkCombatWorld.Instance.ServerConfigureAllure(parameters, true);
            var agents = enemies.Agents().OrderBy(a => a.netId).ToArray();
            for (int i = 0; i < agents.Length; i++)
                Assert.That(World.RepositionReferenceEnemy(agents[i], (Vector2)Owner.transform.position + new Vector2(3 + i * 4, 1)), Is.True);
            yield return Wait(() => agents.All(a => World.TryReadHandoff(a.netId, out var h) && !h.AwaitingFirstSnapshot), "fixture placement acknowledged");
            uint[] epochs = agents.Select(a => a.Assignment.Epoch).ToArray();
            yield return Cast(AllureAction.Decoy, true);
            Assert.That(Allure.State.LastAffectedCount, Is.EqualTo(1));
            Assert.That(agents[0].HasAllureDecoy, Is.True);
            Assert.That(agents[1].HasAllureDecoy, Is.False);
            for (int i = 0; i < agents.Length; i++) Assert.That(agents[i].Assignment.Epoch, Is.EqualTo(epochs[i]));
        }

        [UnityTest]
        public IEnumerator SwitchingKeepsDecoyAndAllIndependentCooldownDeadlines()
        {
            yield return Cast(AllureAction.Decoy, true);
            var state = Allure.State;
            Assert.That(Allure.CooldownRemaining(AllureAction.Decoy), Is.GreaterThan(19));
            Assert.That(Allure.CooldownRemaining(AllureAction.Throw), Is.Zero);
            Assert.That(Allure.CooldownRemaining(AllureAction.Take), Is.Zero);
            yield return Select(PrototypeAbilityId.Music);
            Assert.That(enemies.Agents().All(a => a.HasAllureDecoy), Is.True);
            Assert.That(Allure.DecoyRemaining, Is.GreaterThan(0));
            Assert.That(Allure.RequestAction(AllureAction.Decoy), Is.False, "unselected module cannot start another cast");
            yield return Select(PrototypeAbilityId.Allure);
            Assert.That(Allure.State.DecoyReadyAt, Is.EqualTo(state.DecoyReadyAt));
            Assert.That(Allure.State.DecoyCastId, Is.EqualTo(state.DecoyCastId));
            Assert.That(Allure.RequestAction(AllureAction.Decoy), Is.False, "switch does not refund cooldown");
        }

        [UnityTest]
        public IEnumerator ExpiryRestoresPlayerTrackingWithoutRefundingCooldown()
        {
            var parameters = AllureParameters.Defaults; parameters.DecoyDuration = .6f;
            NetworkCombatWorld.Instance.ServerConfigureAllure(parameters, true);
            yield return Cast(AllureAction.Decoy, true);
            double ready = Allure.State.DecoyReadyAt;
            yield return Wait(() => Allure.DecoyRemaining <= 0 && enemies.Agents().All(a => !a.HasAllureDecoy), "decoy expires");
            Assert.That(enemies.Agents().All(a => a.TargetState.AggroPlayerId == Owner.netId), Is.True);
            Assert.That(Allure.State.DecoyReadyAt, Is.EqualTo(ready));
            Assert.That(Allure.CooldownRemaining(AllureAction.Decoy), Is.GreaterThan(15));
        }

        [UnityTest]
        public IEnumerator DisablingPrototypeClearsTemporaryTargetButKeepsCooldown()
        {
            yield return Cast(AllureAction.Decoy, true);
            double ready = Allure.State.DecoyReadyAt;
            Abilities.ServerSetEnabled(false);
            yield return Wait(() => Allure.DecoyRemaining <= 0 && enemies.Agents().All(a => !a.HasAllureDecoy), "prototype shutdown cleanup");
            Assert.That(Allure.State.DecoyReadyAt, Is.EqualTo(ready));
            Abilities.ServerSetEnabled(true);
            yield return Wait(() => Abilities.OwnerReady, "prototype enabled");
            Assert.That(Allure.RequestAction(AllureAction.Decoy), Is.False);
        }

        [UnityTest]
        public IEnumerator OwnerDeathClearsDecoyAndLeavesNoEnemyTrackingIt()
        {
            yield return Cast(AllureAction.Decoy, true);
            Owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return Wait(() => Allure.DecoyRemaining <= 0 && enemies.Agents().All(a => !a.HasAllureDecoy), "death cleanup");
            Assert.That(Allure.RequestAction(AllureAction.Decoy), Is.False);
        }

        [UnityTest]
        public IEnumerator NoVisibleTargetsDoesNotConsumeDecoyCooldown()
        {
            foreach (var enemy in enemies.Agents()) NetworkServer.Destroy(enemy.gameObject);
            yield return null;
            yield return Cast(AllureAction.Decoy, false);
            Assert.That(Allure.State.DecoyCastId, Is.Zero);
            Assert.That(Allure.CooldownRemaining(AllureAction.Decoy), Is.Zero);
            Assert.That(Allure.LastResult, Is.Not.Empty);
        }

        [UnityTest]
        public IEnumerator ReplayedNetworkRequestCannotRenewDecoyOrChargeCooldownAgain()
        {
            yield return Cast(AllureAction.Decoy, true);
            var accepted = Allure.State;
            uint targetRevision = enemies.Agents()[0].TargetState.Revision;
            SendCommand(Allure.LastRequestId, Abilities.SelectionRevision, AllureAction.Decoy);
            yield return Wait(() => !Allure.LastRequestAccepted, "duplicate command rejected by server");
            Assert.That(Allure.State.Revision, Is.EqualTo(accepted.Revision));
            Assert.That(Allure.State.DecoyReadyAt, Is.EqualTo(accepted.DecoyReadyAt));
            Assert.That(Allure.State.DecoyExpiresAt, Is.EqualTo(accepted.DecoyExpiresAt));
            Assert.That(enemies.Agents()[0].TargetState.Revision, Is.EqualTo(targetRevision));
        }

        [UnityTest]
        public IEnumerator PreviousSelectionRevisionCannotStartNewDecoyAfterSwitchingBack()
        {
            uint oldRevision = Abilities.SelectionRevision;
            yield return Select(PrototypeAbilityId.Music);
            yield return Select(PrototypeAbilityId.Allure);
            ulong id = Owner.GetComponent<MirrorNetworkCombatBridge>().EventIds.Next().Value;
            SendCommand(id, oldRevision, AllureAction.Decoy);
            yield return Wait(() => Allure.LastResolvedRequestId == id, "stale selection command receipt");
            Assert.That(Allure.LastRequestAccepted, Is.False);
            Assert.That(Allure.CooldownRemaining(AllureAction.Decoy), Is.Zero);
            Assert.That(enemies.Agents().All(a => !a.HasAllureDecoy), Is.True);
            yield return Cast(AllureAction.Decoy, true);
        }

        private void SendCommand(ulong id, uint revision, AllureAction action) =>
            typeof(NetworkPlayerAllure).GetMethod("CmdCast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(Allure, new object[] { id, revision, action, null });

        [UnityTest]
        public IEnumerator TargetingCoreChangesDecoyDuringWarningWithoutRestartingActionOrEpoch()
        {
            // The action-capable skeleton isolates target binding from Allure's ordinary-enemy filter.
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Content/NetworkCombat/NetworkEnemySkeleton.prefab");
            var instance = UnityEngine.Object.Instantiate(prefab, Owner.transform.position + Vector3.right * 8, Quaternion.identity);
            var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureInitialServerTarget(Owner.netId);
            NetworkServer.Spawn(instance);
            yield return Wait(() => agent.ProductEnemyInitialized && agent.Authority.RunsCombatDecisions &&
                World.TryReadHandoff(agent.netId, out var handoff) && !handoff.AwaitingFirstSnapshot, "warning fixture ready");
            var enemy = instance.GetComponent<EnemyController>();
            double now = EnemySimulationClock.CombatNow;
            var action = new EnemyActionState { ActionId = 77001, Phase = EnemyAttackPresentationPhase.Warning,
                WarningStartedAt = now, WarningUntil = now + 5, ActiveUntil = now + 6,
                RecoveryUntil = now + 7, NextAttackAt = now + 8, Facing = Vector2.left,
                TargetPosition = Owner.transform.position };
            enemy.SuspendSimulationExecution();
            enemy.RestoreSimulationAction(action, now);
            uint epoch = agent.Assignment.Epoch;
            int roleChanges = 0;
            agent.Authority.RoleChanged += (_, _) => roleChanges++;
            ulong cast = NetworkCombatWorld.Instance.Gateway.NextServerEventId();
            Assert.That(World.ServerApplyAllureDecoy(agent.netId, Owner.netId, cast,
                (Vector2)Owner.transform.position + Vector2.up * 2, NetworkTime.time + 5), Is.True);
            Assert.That(agent.ActualTarget, Is.Not.Null);
            Assert.That(enemy.Target, Is.SameAs(agent.ActualTarget));
            Assert.That(enemy.attackScript.Target, Is.SameAs(agent.ActualTarget), "movement and attack must resolve one target");
            yield return null;
            var captured = enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow);
            Assert.That(captured.ActionId, Is.EqualTo(action.ActionId));
            Assert.That(captured.WarningUntil, Is.EqualTo(action.WarningUntil));
            Assert.That(captured.NextAttackAt, Is.EqualTo(action.NextAttackAt));
            Assert.That(agent.Assignment.Epoch, Is.EqualTo(epoch));
            Assert.That(roleChanges, Is.Zero);
            World.ServerClearAllureDecoy(Owner.netId, cast);
            Assert.That(agent.HasAllureDecoy, Is.False);
            var playerTarget = Owner.GetComponent<PlayerMovement>().EnemyAttackTarget;
            Assert.That(enemy.Target, Is.SameAs(playerTarget != null ? playerTarget : Owner.transform));
            Assert.That(enemy.attackScript.Target, Is.SameAs(enemy.Target));
            Assert.That(enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow).ActionId, Is.EqualTo(action.ActionId));
            Assert.That(agent.Assignment.Epoch, Is.EqualTo(epoch));
            Assert.That(roleChanges, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ThreePlayerLaunchRosterRejectsThrowAndTakeAfterDownOrDisconnect()
        {
            // This is a server admission policy test, not a three-process network simulation.
            // Replace only the session data during synchronous calls and restore it before yielding.
            var original = manager.Session;
            Assert.That(original.TryGetConnection(Owner.connectionToClient.connectionId, out var host), Is.True);
            var roster = new RunSession();
            Assert.That(roster.TryConnect("allure-policy-host", host.ConnectionId, out _, out _), Is.True);
            roster.AttachAvatar(host.ConnectionId, Owner.netId, host.ConnectionEpoch);
            Assert.That(roster.TryConnect("allure-policy-second", 100000001, out _, out _), Is.True);
            Assert.That(roster.TryConnect("allure-policy-third", 100000002, out var third, out _), Is.True);
            roster.BeginRun();
            var sessionProperty = typeof(BootGameplayNetworkManager).GetProperty(nameof(BootGameplayNetworkManager.Session));
            try
            {
                sessionProperty.SetValue(manager, roster);
                for (int phase = 0; phase < 3; phase++)
                {
                    if (phase == 1)
                        typeof(RunParticipant).GetProperty(nameof(RunParticipant.LifeState)).SetValue(third, RunPlayerLifeState.Downed);
                    if (phase == 2)
                        roster.Disconnect(100000002, new PlayerRuntimeCheckpoint { LifeState = RunPlayerLifeState.Downed });
                    Assert.That(roster.Participants.Count, Is.EqualTo(3));
                    foreach (var action in new[] { AllureAction.Throw, AllureAction.Take })
                    {
                        var result = NetworkCombatWorld.Instance.ServerCastAllure(Owner.netId, action,
                            NetworkCombatWorld.Instance.Gateway.NextServerEventId(), AllureParameters.Defaults);
                        Assert.That(result.Accepted, Is.False);
                        Assert.That(result.Reason, Does.Contain("two players"),
                            $"Roster phase {phase} must reject the unsupported party size before choosing from survivors.");
                        TestContext.WriteLine($"three-player roster phase={phase}, action={action}, rejection={result.Reason}");
                    }
                }
            }
            finally { sessionProperty.SetValue(manager, original); }
            Assert.That(Allure.CooldownRemaining(AllureAction.Throw), Is.Zero);
            Assert.That(Allure.CooldownRemaining(AllureAction.Take), Is.Zero);
            yield break;
        }

        private IEnumerator Cast(AllureAction action, bool accepted)
        {
            ulong before = Allure.LastRequestId;
            Assert.That(Allure.RequestAction(action), Is.True, "owner sends " + action);
            ulong request = Allure.LastRequestId;
            Assert.That(request, Is.Not.EqualTo(before));
            yield return Wait(() => Allure.LastResolvedRequestId == request, "server receipt for " + action);
            Assert.That(Allure.LastRequestAccepted, Is.EqualTo(accepted), Allure.LastResult);
        }

        private IEnumerator Select(PrototypeAbilityId ability)
        {
            Assert.That(Abilities.RequestSelect(ability), Is.True);
            yield return Wait(() => Abilities.SelectedAbility == ability && !Abilities.SelectionPending, "selection " + ability);
        }

        private static IEnumerator Wait(Func<bool> predicate, string phase) => EnemyDefinitionRuntimeFixture.Wait(predicate, phase, 30);

        [UnityTearDown]
        public IEnumerator Stop()
        {
            if (manager != null && NetworkServer.active) manager.StopHost();
            if (manager != null) yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Gameplay cleanup");
            enemies?.Dispose();
            if (gate != null) UnityEngine.Object.Destroy(gate);
            BootSceneFixtureObjects.Destroy(roots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
#endif
