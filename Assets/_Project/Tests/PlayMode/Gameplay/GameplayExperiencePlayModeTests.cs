using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class GameplayExperiencePlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] roots;
        private GameObject fixture;
        private string originalLanguage;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkModifierSelection Progression => Owner.GetComponent<NetworkModifierSelection>();
        private NetworkExperienceWorld World => NetworkExperienceWorld.Current;
        private IEnumerator StartHost()
        {
            PickupAudit.Recorded -= LogPickup;
            PickupAudit.Recorded += LogPickup;
            const string boot = "Assets/_Project/Scenes/Boot.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(boot, LoadSceneMode.Single);
#endif
            roots = BootSceneFixtureObjects.Capture(boot);
            originalLanguage = MonsterSupergroup.Gameplay.Options.GameLocalization.Language;
            MonsterSupergroup.Gameplay.Options.GameLocalization.Select("en");
            yield return WaitFor(() => MonsterSupergroup.Gameplay.Options.GameLocalization.Language == "en", "Fixed English HUD fixture locale");
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            fixture = new GameObject("M6 runtime fixture"); fixture.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7972, false, out string error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Progression.HasOwnerBaseline && manager.CanBeginRun(out _), "Boot XP ready");
            Owner.GetComponent<NetworkExperienceCollector>().enabled = false;
            NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(Owner.netId, true);
            Owner.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
        }
        // Boundary fixture invokes the frozen-kill callback; real weapon coverage is separate below.
        private NetworkExperienceGem Drop(Vector2 position, Action<NetworkEnemySimulationAgent> beforeKill = null)
        {
            var spawner = Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            var enemy = Object.Instantiate(spawner.EnemyPrefab, position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(enemy, Owner.gameObject.scene);
            enemy.GetComponent<NetworkEnemySimulationAgent>().ConfigureInitialServerTarget(Owner.netId);
            NetworkServer.Spawn(enemy);
            beforeKill?.Invoke(enemy.GetComponent<NetworkEnemySimulationAgent>());
            typeof(NetworkExperienceWorld).GetMethod("OnConfirmedKill", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(World, new object[] { new ConfirmedKill { TargetEntityId = enemy.GetComponent<NetworkIdentity>().netId,
                    TargetStateVersion = 2, KillerPlayerId = Owner.netId, CauseEventId = 999 } });
            NetworkServer.Destroy(enemy);
            return World.Unclaimed.Last();
        }
        private bool Collect(NetworkExperienceGem gem, out string reason) =>
            World.TryCollect(Owner.connectionToClient, Owner, World.RunId, gem.DropId, out reason);
        [UnityTest]
        public IEnumerator DropPositionUsesCurrentAcceptedSnapshot_ServerPosition_OrSpawnFallback()
        {
            yield return StartHost();
            var first = Drop(new Vector2(8, 8), agent =>
            {
                agent.transform.position = new Vector2(20, 20);
                Assert.That(NetworkEnemySimulationWorld.Instance.Registry.TryAcceptClientSnapshot(Owner.netId,
                    new EnemySimulationSnapshot { EnemyEntityId = agent.netId, AssignmentEpoch = agent.Assignment.Epoch,
                        Sequence = 10, SampleNetworkTime = NetworkTime.time + 1, Position = new Vector2(11, 12) }),
                    Is.EqualTo(EnemySnapshotRejectionReason.None));
            });
            Assert.That((Vector2)first.transform.position, Is.EqualTo(new Vector2(11, 12)));
            var second = Drop(new Vector2(8, 8), agent =>
            {
                agent.SetServerAssignment(NetworkEnemySimulationWorld.Instance.Registry.AssignServerFallback(agent.netId, Owner.netId));
                agent.transform.position = new Vector2(21, 22);
            });
            Assert.That((Vector2)second.transform.position, Is.EqualTo(new Vector2(21, 22)));
            var third = Drop(new Vector2(8, 8), agent => agent.transform.position = new Vector2(25, 25));
            Assert.That((Vector2)third.transform.position, Is.EqualTo(new Vector2(8, 8)));
        }
        [UnityTest]
        public IEnumerator DisabledXpWorldBlocksStartAndGrants_AndClearsUnclaimedObjects()
        {
            yield return StartHost(); Drop(Owner.transform.position);
            World.enabled = false;
            Assert.That(manager.TryBeginRun(out string reason), Is.False); Assert.That(reason, Is.Not.Empty);
            Assert.That(Progression.TryGrantExperience(19), Is.False);
            Assert.That(Progression.Level, Is.EqualTo(1)); Assert.That(World.UnclaimedCount, Is.Zero);
            Assert.That(NetworkExperienceGem.ClientGems, Is.Empty);
        }
        [UnityTest]
        public IEnumerator PickupCommitsOnce_AppliesMultiplierOnce_AndHudShowsOwnerBaseline()
        {
            yield return StartHost();
            var gem = Drop(Owner.transform.position); ulong id = gem.DropId;
            Assert.That(Progression.Experience, Is.Zero, "Kill must not grant XP.");
            Assert.That(gem.RawExperience, Is.EqualTo(2));
            var visibleOrb = gem.GetComponentsInChildren<SpriteRenderer>().First(s => s.gameObject.name == "orb_0");
            Assert.That(Vector2.Distance(gem.transform.position, visibleOrb.bounds.center), Is.LessThan(2),
                "The visible orb must be within its pickup area; exported scene coordinates cannot offset it.");
            Assert.That(Collect(gem, out string reason), Is.True, reason);
            Assert.That(Progression.Experience, Is.EqualTo(4));
            Assert.That(World.TryCollect(Owner.connectionToClient, Owner, World.RunId, id, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("unavailable")); Assert.That(Progression.Experience, Is.EqualTo(4));
            yield return null;
            Assert.That(Object.FindFirstObjectByType<PlayerExperienceHUD>().Content, Does.Contain("XP 4 / 19"));
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Object.FindObjectsByType<ExperienceCollectionFlight>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(World.UnclaimedCount, Is.Zero);
        }
        [UnityTest]
        public IEnumerator HostPickupPreservesOneFlightAfterGemDespawn_AndMovesBackThenToWinner()
        {
            yield return StartHost();
            Vector3 origin = Owner.transform.position + Vector3.right;
            var gem = Drop(origin);
            uint gemId = gem.netId;
            Assert.That(Collect(gem, out string reason), Is.True, reason);
            Assert.That(NetworkClient.spawned.ContainsKey(gemId), Is.False, "Claimed gem must despawn immediately.");
            yield return null;
            var flights = Object.FindObjectsByType<ExperienceCollectionFlight>(FindObjectsSortMode.None);
            Assert.That(flights, Has.Length.EqualTo(1), "Host must retain a visible flight after its shared network gem is destroyed.");
            var flight = flights.Single();
            float maxX = origin.x, minReturnX = float.PositiveInfinity;
            bool visible = false;
            while (flight != null && flight.isActiveAndEnabled)
            {
                Assert.That(Object.FindObjectsByType<ExperienceCollectionFlight>(FindObjectsSortMode.None), Has.Length.EqualTo(1),
                    "Host's local and RPC paths must not create two flights.");
                maxX = Mathf.Max(maxX, flight.transform.position.x);
                if (maxX > origin.x + .1f) minReturnX = Mathf.Min(minReturnX, flight.transform.position.x);
                visible |= flight.GetComponentsInChildren<SpriteRenderer>().Any(s => s.enabled) ||
                    flight.GetComponentsInChildren<ParticleSystem>().Any(p => p.isPlaying);
                yield return null;
            }
            Assert.That(visible, Is.True, "A transform without visible renderers is not an animation.");
            Assert.That(maxX, Is.GreaterThan(origin.x + .1f), "Back away from the winner first.");
            Assert.That(minReturnX, Is.LessThan(origin.x - .3f), "Then fly toward the winner.");
            Assert.That(Progression.Experience, Is.EqualTo(4), "Presentation must not grant a second award.");
        }
        [UnityTest]
        public IEnumerator HostPresentationDeduplicatesLocalAndQueuedRpc_AndDisconnectCleansFlight()
        {
            yield return StartHost();
            var gem = Drop(Owner.transform.position + Vector3.right);
            // Keep the identity alive so the queued Host RPCs also reach the presentation consumer.
            gem.ServerPresentCollection(Owner.netId);
            gem.ServerPresentCollection(Owner.netId);
            yield return null;
            Assert.That(Object.FindObjectsByType<ExperienceCollectionFlight>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Progression.Experience, Is.Zero, "Playing or replaying a visual cannot award XP.");
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "disconnect during flight");
            Assert.That(Object.FindObjectsByType<ExperienceCollectionFlight>(FindObjectsSortMode.None), Is.Empty);
        }
        [UnityTest]
        public IEnumerator InvalidClaimsKeepTheGem_SelectionAndDeathBlockPickup()
        {
            yield return StartHost();
            var gem = Drop((Vector2)Owner.transform.position + Vector2.right * 8);
            Assert.That(Collect(gem, out string reason), Is.False); Assert.That(reason, Is.EqualTo("distance-or-stats"));
            Assert.That(World.TryCollect(Owner.connectionToClient, Owner, "previous-run", gem.DropId, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("old-run"));
            Assert.That(World.TryCollect(null, Owner, World.RunId, gem.DropId, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("ownership"));
            gem.transform.position = Owner.transform.position;
            Progression.ServerGrantExperience(Progression.ExperiencePerLevel);
            yield return WaitFor(() => Progression.IsSelecting, "selection lock");
            Assert.That(Collect(gem, out reason), Is.False); Assert.That(reason, Is.EqualTo("selecting-or-unready"));
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 0);
            Assert.That(Collect(gem, out reason), Is.False); Assert.That(reason, Is.EqualTo("dead"));
            Assert.That(World.UnclaimedCount, Is.EqualTo(1)); Assert.That(gem.Claimed, Is.False);
        }
        [UnityTest]
        public IEnumerator FailedGrantDoesNotConsume_OrdinaryDestroyDoesNotDrop_StopStartsFresh()
        {
            yield return StartHost();
            var gem = Drop(Owner.transform.position);
            var field = typeof(NetworkModifierSelection).GetField("selectionRules", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = field.GetValue(Progression); field.SetValue(Progression, null);
            Assert.That(Collect(gem, out string reason), Is.False); Assert.That(reason, Is.EqualTo("grant-rejected"));
            field.SetValue(Progression, original);
            Assert.That(gem.Claimed, Is.False); Assert.That(Progression.Experience, Is.Zero);
            var enemy = Object.Instantiate(Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>().EnemyPrefab);
            NetworkServer.Spawn(enemy); NetworkServer.Destroy(enemy);
            Assert.That(World.UnclaimedCount, Is.EqualTo(1));
            string old = World.RunId; manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Stop");
            Assert.That(NetworkExperienceGem.ClientGems, Is.Empty);
            Assert.That(Object.FindObjectsByType<NetworkExperienceGem>(FindObjectsSortMode.None), Is.Empty);
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Progression.HasOwnerBaseline && manager.CanBeginRun(out _), "new run");
            Assert.That(World.RunId, Is.Not.EqualTo(old)); Assert.That(World.UnclaimedCount, Is.Zero);
            Assert.That(Progression.Level, Is.EqualTo(1)); Assert.That(Progression.ExperiencePerLevel, Is.EqualTo(19));
        }
        [UnityTest]
        public IEnumerator BulkExperienceRetainsEveryRewardAndRemainder_ThenF5AddsExactlyOne()
        {
            yield return StartHost();
            float amount = Enumerable.Range(1, 20).Sum(Progression.ExperienceRequiredAtLevel) + .5f;
            Assert.That(Progression.TryGrantExperience(amount), Is.True);
            Assert.That(Progression.Level, Is.EqualTo(21)); Assert.That(Progression.Experience, Is.EqualTo(.5f));
            Assert.That(Progression.CaptureProgression().Rewards.Select(r => r.EarnedLevel), Is.EqualTo(Enumerable.Range(2, 20)));
            Progression.RequestDebugLevelUp(); yield return WaitFor(() => Progression.Level == 22, "F5");
            Assert.That(Progression.Experience, Is.EqualTo(.5f)); Assert.That(Progression.PendingUpgradeCount, Is.EqualTo(21));
        }
        [UnityTest]
        public IEnumerator RealCirclingPhysicsKillProducesGem_OnlyAutomaticPickupGrantsXP()
        {
            yield return StartHost(); manager.BeginRun();
            yield return WaitFor(() => Object.FindFirstObjectByType<NetworkEnemySimulationAgent>() != null, "wave enemy");
            var agent = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsNavigation, "enemy simulator");
            var enemy = agent.GetComponent<EnemyController>();
            var weapon = (CirclingAttackBehaviour)Owner.GetComponent<PlayerBuildRuntime>().InitialWeapon;
            weapon.baseSpeed = 0;
            var player = Owner.GetComponent<PlayerMovement>();
            float until = Time.realtimeSinceStartup + 24, nextAttack = 0;
            while (agent != null && Time.realtimeSinceStartup < until)
            {
                enemy.Movement.StopMovement(); enemy.rigidBody.gravityScale = 0;
                Vector2 offset = weapon.transform.TransformPoint(Vector3.right * (weapon.baseRadius * weapon.SizeValue)) - player.transform.position;
                var activeOrb = weapon.GetComponentsInChildren<AnimatedAttack>().FirstOrDefault(a => a.hitbox != null && a.hitbox.collider.enabled);
                if (activeOrb != null) offset = activeOrb.hitbox.collider.bounds.center - player.transform.position;
                Vector2 position = (Vector2)enemy.hurtBox.GetBounds().center - offset;
                player.SetDirection(Vector2.zero); player.body.position = position; player.transform.position = position;
                Physics2D.SyncTransforms();
                if (Time.time >= nextAttack)
                {
                    weapon.Attack(); nextAttack = Time.time + weapon.GetAttackSequenceDuration() + weapon.GetCooldown() + .2f;
                }
                yield return new WaitForFixedUpdate();
            }
            if (agent != null && NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(agent.netId, out var state))
                Debug.Log($"[M6] kill fixture local={enemy.CurrentHealth} canonical={state.Health} requested={agent.RequestedOrdinaryKnockbackCount} activeOrbs={weapon.ActiveOrbCount} rejectedRoot={NetworkCombatWorld.Instance.Gateway.Metrics.GetRejected(CombatRejectionReason.InvalidAttackRoot)}");
            Assert.That(agent == null, Is.True, "Native weapon did not reach canonical death.");
            yield return WaitFor(() => World.UnclaimedCount > 0, "real kill drop");
            Assert.That(Progression.Experience, Is.Zero, "No direct kill XP.");
            var gem = World.Unclaimed.First();
            player.body.position = gem.transform.position; player.transform.position = gem.transform.position;
            Owner.GetComponent<NetworkExperienceCollector>().enabled = true;
            float expectedExperience = gem.RawExperience * player.PlayerStats.currentStats.xpModifier;
            yield return WaitFor(() => Mathf.Abs(Progression.Experience - expectedExperience) < .001f, "automatic Owner pickup");
            Debug.Log("[M6] real Circling -> canonical kill -> network gem -> automatic pickup passed");
        }
        private static IEnumerator WaitFor(Func<bool> predicate, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, reason);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PickupAudit.Recorded -= LogPickup;
            if (originalLanguage != null) MonsterSupergroup.Gameplay.Options.GameLocalization.Select(originalLanguage);
            if (manager != null && NetworkServer.active)
            { manager.StopHost(); yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "teardown"); }
            BootSceneFixtureObjects.Destroy(roots);
            if (fixture != null) Object.Destroy(fixture);
            yield return null; NetworkManager.ResetStatics();
        }
        private static void LogPickup(string kind, string run, ulong drop, string detail) => Debug.Log($"[PickupTest] {kind} drop={drop} {detail}");
    }
}
