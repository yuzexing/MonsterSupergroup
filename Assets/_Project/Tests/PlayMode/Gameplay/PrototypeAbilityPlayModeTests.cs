#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
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
    public sealed class PrototypeAbilityPlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture enemies;
        private GameObject[] bootRoots;
        private GameObject weaponGate;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkPlayerPrototypeAbilities Abilities => Owner.GetComponent<NetworkPlayerPrototypeAbilities>();
        private NetworkPlayerGluttony Gluttony => Owner.GetComponent<NetworkPlayerGluttony>();
        private PlayerMovement Player => Owner.GetComponent<PlayerMovement>();
        private NetworkModifierSelection Upgrades => Owner.GetComponent<NetworkModifierSelection>();

        [UnityTest]
        public IEnumerator NormalBootDefaultsToGluttony_SelectionIsVersionedAndRejectsUnknownIds()
        {
            yield return Start();
            Assert.That(Abilities.PrototypeEnabled, Is.True);
            Assert.That(Abilities.SelectedAbility, Is.EqualTo(PrototypeAbilityId.Gluttony));
            Assert.That((int)PrototypeAbilityId.Gluttony, Is.EqualTo(1));
            Assert.That((int)PrototypeAbilityId.Music, Is.EqualTo(2));
            Assert.That((int)PrototypeAbilityId.Allure, Is.EqualTo(3));
            uint initial = Abilities.SelectionRevision;
            Assert.That(initial, Is.Not.Zero);
            Assert.That(Abilities.RequestSelect(PrototypeAbilityId.Gluttony), Is.False);
            Assert.That(Abilities.RequestSelect((PrototypeAbilityId)0), Is.False);
            Assert.That(Abilities.RequestSelect((PrototypeAbilityId)4), Is.False);
            Assert.That(Abilities.SelectionRevision, Is.EqualTo(initial));

            yield return Select(PrototypeAbilityId.Music);
            Assert.That(Abilities.SelectionRevision, Is.GreaterThan(initial));
            uint musicRevision = Abilities.SelectionRevision;
            Assert.That(Abilities.RequestSelect(PrototypeAbilityId.Music), Is.False);
            Assert.That(Abilities.SelectionRevision, Is.EqualTo(musicRevision));
            Assert.That(Owner.GetComponent<NetworkPlayerMusic>(), Is.Not.Null,
                "Music is now registered; its own tests exercise performance admission.");
            yield return Select(PrototypeAbilityId.Allure);
            Assert.That(Owner.GetComponent<NetworkPlayerAllure>(), Is.Not.Null,
                "Allure is now registered; its tests exercise target admission and cooldowns.");
            yield return Select(PrototypeAbilityId.Gluttony);
            Assert.That(Abilities.ServerCanBegin(PrototypeAbilityId.Gluttony, initial), Is.False,
                "Returning to the same ability must not make an old request valid again.");
            Assert.That(Abilities.ServerCanBegin(PrototypeAbilityId.Gluttony, Abilities.SelectionRevision), Is.True);
        }

        [UnityTest]
        public IEnumerator LeavingGluttonyStopsUnmarkedPassiveAndNewMarks_WithoutDisablingComponents()
        {
            yield return Start();
            yield return Select(PrototypeAbilityId.Music);
            var settings = Gluttony.Parameters;
            settings.PassiveEnabled = true;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings, false);
            yield return PrepareEnemies();
            var target = enemies.Agents().First();
            Assert.That(NetworkEnemySimulationWorld.Instance.RepositionReferenceEnemy(target,
                (Vector2)Owner.transform.position + Vector2.right * .5f), Is.True);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(Gluttony.enabled, Is.True, "Switching cannot disable the module or reset its runtime.");
            Assert.That(Gluttony.GetComponent<GluttonyPrototypeView>().enabled, Is.True);
            Assert.That(Gluttony.RequestMark(Vector2.right), Is.False);
            Assert.That(Gluttony.RequestDevour(target.netId, false), Is.False);
            Assert.That(Gluttony.State.PassiveKills, Is.Zero);
            Assert.That(Gluttony.State.PassiveReadyAt, Is.Zero);
            Assert.That(NetworkCombatWorld.Instance.Gateway.Ledger.IsAlive(target.netId), Is.True);

            yield return Select(PrototypeAbilityId.Gluttony);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Gluttony.State.PassiveKills == 1,
                "passive resumes only after selecting Gluttony", 5);
        }

        [UnityTest]
        public IEnumerator ExistingMarksContinueCollectingAfterSwitch_WithoutStartingPassiveCooldown()
        {
            yield return Start();
            yield return PrepareEnemies();
            Assert.That(Gluttony.RequestMark(new Vector2(5, 3)), Is.True);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Gluttony.State.Marked == 2, "two marks");
            ulong cast = Gluttony.State.CastId;
            double cooldown = Gluttony.State.ActiveReadyAt;
            yield return Select(PrototypeAbilityId.Music);
            Assert.That(Gluttony.RemainingMarks, Is.EqualTo(2));
            yield return ApproachUntil(() => Gluttony.State.TotalCollected == 2, () =>
            {
                var target = enemies.Agents().Where(a => Gluttony.HasMark(a.netId))
                    .OrderBy(a => Vector2.Distance(a.transform.position, Owner.transform.position)).FirstOrDefault();
                return target != null ? (Vector2)target.transform.position : (Vector2)Owner.transform.position;
            });
            Assert.That(Gluttony.State.CastId, Is.EqualTo(cast));
            Assert.That(Gluttony.State.ActiveReadyAt, Is.EqualTo(cooldown));
            Assert.That(Gluttony.State.PassiveReadyAt, Is.Zero);
            Assert.That(Gluttony.State.PassiveKills, Is.Zero);
            Assert.That(Gluttony.RemainingMarks, Is.Zero);
            Assert.That(NetworkCombatWorld.Instance.Gateway.Metrics.ConfirmedKills, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SwitchingAwayAndBackPreservesBothCooldownDeadlines()
        {
            yield return Start();
            yield return PrepareEnemies();
            var settings = Gluttony.Parameters;
            settings.PassiveEnabled = true;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings, true);
            var target = enemies.Agents().OrderBy(a => Vector2.Distance(a.transform.position, Owner.transform.position)).First();
            yield return ApproachUntil(() => Gluttony.State.PassiveKills == 1,
                () => target != null ? (Vector2)target.transform.position : (Vector2)Owner.transform.position);
            double passive = Gluttony.State.PassiveReadyAt;
            var remaining = enemies.Agents().First(a => NetworkCombatWorld.Instance.Gateway.Ledger.IsAlive(a.netId));
            Assert.That(Gluttony.RequestMark((Vector2)remaining.transform.position - (Vector2)Owner.transform.position), Is.True);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Gluttony.State.Marked == 1, "remaining enemy marked");
            double active = Gluttony.State.ActiveReadyAt;
            double beforeSwitch = NetworkTime.time;
            yield return Select(PrototypeAbilityId.Music);
            yield return Select(PrototypeAbilityId.Allure);
            yield return new WaitForSecondsRealtime(.25f);
            // The second pickup may still be travelling after the mark has already been consumed.
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Upgrades.Level >= 2, "both kill pickups credited");
            yield return FinishUpgrades();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.OwnerReady, "collected XP selection released");
            yield return Select(PrototypeAbilityId.Gluttony);
            Assert.That(Gluttony.State.PassiveReadyAt, Is.EqualTo(passive));
            Assert.That(Gluttony.State.ActiveReadyAt, Is.EqualTo(active));
            Assert.That(active - NetworkTime.time, Is.LessThan(active - beforeSwitch), "Cooldown continues in the background.");
            Assert.That(Gluttony.RequestMark(Vector2.right), Is.False, "Switching back cannot refund the active cooldown.");
        }

        [UnityTest]
        public IEnumerator MenuAndUpgradeSelectionRejectSwitchingAndNewCasts()
        {
            yield return Start();
            uint revision = Abilities.SelectionRevision;
            Player.SetMenuInputBlocked(true);
            Assert.That(Abilities.RequestSelect(PrototypeAbilityId.Music), Is.False);
            Assert.That(Gluttony.RequestMark(Vector2.right), Is.False);
            Assert.That(Abilities.SelectionRevision, Is.EqualTo(revision));
            Player.SetMenuInputBlocked(false);
            yield return EnemyDefinitionRuntimeFixture.Wait(
                () => Owner.GetComponent<ModifierSelectionController>().IsPresentationReady, "upgrade presentation ready");
            Upgrades.ServerGrantExperience(Upgrades.ExperiencePerLevel);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Upgrades.IsSelecting, "real upgrade selection");
            Assert.That(Abilities.RequestSelect(PrototypeAbilityId.Music), Is.False);
            Assert.That(Gluttony.RequestMark(Vector2.right), Is.False);
            Assert.That(Abilities.ServerCanBegin(PrototypeAbilityId.Gluttony, revision), Is.False);
            Assert.That(Abilities.SelectionRevision, Is.EqualTo(revision));
            yield return FinishUpgrades();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.OwnerReady, "selection gate released");
            yield return Select(PrototypeAbilityId.Music);
        }

        [UnityTest]
        public IEnumerator DisablingPrototypeCancelsMarks_ButDoesNotRefundCooldownOrDisableModule()
        {
            yield return Start();
            yield return PrepareEnemies();
            Assert.That(Gluttony.RequestMark(new Vector2(5, 3)), Is.True);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Gluttony.RemainingMarks == 2, "marks before disable");
            uint target = Gluttony.MarkedTargets.First();
            double deadline = Gluttony.State.ActiveReadyAt;
            Abilities.ServerSetEnabled(false);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !Abilities.PrototypeEnabled && Gluttony.RemainingMarks == 0,
                "disabled prototype cancels live marks");
            Assert.That(Abilities.OwnerReady, Is.False);
            Assert.That(Abilities.RequestSelect(PrototypeAbilityId.Music), Is.False);
            Assert.That(Gluttony.RequestMark(Vector2.right), Is.False);
            Assert.That(Gluttony.RequestDevour(target, true), Is.False);
            Assert.That(Gluttony.enabled, Is.True);
            Assert.That(Gluttony.State.Cancelled, Is.EqualTo(2));
            Abilities.ServerSetEnabled(true);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.OwnerReady, "prototype reenabled");
            Assert.That(Gluttony.RemainingMarks, Is.Zero);
            Assert.That(Gluttony.State.ActiveReadyAt, Is.EqualTo(deadline));
            Assert.That(Gluttony.RequestMark(Vector2.right), Is.False);
        }

        [UnityTest]
        public IEnumerator DeathRejectsAbilityInputAndCancelsExistingMarks()
        {
            yield return Start();
            yield return PrepareEnemies();
            Assert.That(Gluttony.RequestMark(new Vector2(5, 3)), Is.True);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Gluttony.RemainingMarks == 2, "marks before death");
            uint revision = Abilities.SelectionRevision;
            Assert.That(Player.CombatantBinding.ApplyDamage(int.MaxValue), Is.GreaterThan(0));
            Assert.That(Abilities.RequestSelect(PrototypeAbilityId.Music), Is.False);
            Assert.That(Gluttony.RequestMark(Vector2.right), Is.False);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Gluttony.RemainingMarks == 0, "death clears live marks");
            Assert.That(Abilities.OwnerReady, Is.False);
            Assert.That(Abilities.ServerCanBegin(PrototypeAbilityId.Gluttony, revision), Is.False);
            Assert.That(Gluttony.State.Cancelled, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator NewSessionRestoresDefaultsAfterHostDisabledThePreviousRun()
        {
            yield return Start();
            var world = NetworkCombatWorld.Instance;
            yield return Select(PrototypeAbilityId.Music);
            world.ServerConfigurePrototypesEnabled(false);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !Abilities.PrototypeEnabled, "previous run disabled");
            manager.LeavePreparationRoom();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !NetworkClient.active && !NetworkServer.active &&
                !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "previous run stopped");
            Assert.That(NetworkCombatWorld.Instance, Is.SameAs(world), "Exercise the persistent Boot world, not a fresh scene instance.");

            yield return EnterGameplay();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.OwnerReady && Gluttony.OwnerReady,
                "new run defaults ready");
            Assert.That(world.PrototypesEnabled, Is.True);
            Assert.That(Abilities.PrototypeEnabled, Is.True);
            Assert.That(Abilities.SelectedAbility, Is.EqualTo(PrototypeAbilityId.Gluttony));
            Assert.That(Gluttony.Parameters.PassiveEnabled, Is.True, "Previous test-session tuning must not leak into the new run.");
            Assert.That(Gluttony.State.ActiveReadyAt, Is.Zero);
            Assert.That(Gluttony.RemainingMarks, Is.Zero);
        }

        private IEnumerator Start()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
            if (!Application.isBatchMode) UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");
            GameOptionsService.EnsureInitialized();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => GameLocalization.IsReady, "localization startup");
            // Let initial dynamic-font imports finish before Boot loads Rewired and gameplay assets.
            yield return null;
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
            bootRoots = BootSceneFixtureObjects.Capture(boot);
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true);
            enemies = new EnemyDefinitionRuntimeFixture(manager);
            weaponGate = new GameObject("Prototype ability isolated-test weapon gate");
            weaponGate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            yield return EnterGameplay();
            Assert.That(Abilities, Is.Not.Null, "Production NetworkPlayer must include the unified ability selector.");
            Assert.That(Abilities.PrototypeEnabled, Is.True, "Boot must enable prototypes without a test-only override.");
            Assert.That(Gluttony.Parameters.Enabled, Is.True, "The production Gluttony configuration must start enabled.");
            // Freeze autonomous consumption before waiting for placement; individual tests enable it explicitly.
            var settings = GluttonyParameters.Defaults;
            settings.PassiveEnabled = false;
            settings.PassiveCooldown = 10;
            settings.ActiveCooldown = 20;
            settings.Length = 20;
            settings.Width = 15;
            settings.MarkDuration = 30;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings, true);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.OwnerReady && Gluttony.OwnerReady, "owner ability baselines");
        }

        private IEnumerator EnterGameplay()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "preparation");
            manager.SetOwnLoadout(6);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Members[0].WeaponId == 6, "weapon loadout");
            manager.StartPreparedGame();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame && Owner != null, "gameplay");
        }

        private IEnumerator PrepareEnemies() => EnemyDefinitionRuntimeFixture.Wait(
            () => enemies.PairReady() && enemies.PlaceInView(), "isolated normal enemies in view");

        private IEnumerator Select(PrototypeAbilityId ability)
        {
            Assert.That(Abilities.RequestSelect(ability), Is.True, "Switch request to " + ability);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.SelectedAbility == ability && !Abilities.SelectionPending,
                "acknowledged selection " + ability);
        }

        private IEnumerator ApproachUntil(Func<bool> condition, Func<Vector2> target)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 12;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                ChooseUpgrade();
                Player.SetDirection((target() - (Vector2)Player.transform.position).normalized);
                yield return null;
            }
            Player.SetDirection(Vector2.zero);
            Assert.That(condition(), Is.True, "Timed out approaching a real enemy.");
        }

        private void ChooseUpgrade()
        {
            if (Upgrades.IsSelecting && Upgrades.PendingEventId != 0 && Upgrades.ServerOffers.Count > 0)
                Upgrades.ServerSelect(Owner.connectionToClient, Upgrades.PendingEventId, 0, out _);
        }

        private IEnumerator FinishUpgrades()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while ((Upgrades.IsSelecting || Upgrades.PendingUpgradeCount > 0) && Time.realtimeSinceStartupAsDouble < deadline)
            {
                ChooseUpgrade();
                yield return null;
            }
            Assert.That(Upgrades.IsSelecting, Is.False);
            Assert.That(Upgrades.PendingUpgradeCount, Is.Zero);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (manager != null)
            {
                manager.LeavePreparationRoom();
                yield return EnemyDefinitionRuntimeFixture.Wait(() => !NetworkClient.active && !NetworkServer.active &&
                    !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "prototype test cleanup");
            }
            enemies?.Dispose();
            if (weaponGate != null) UnityEngine.Object.Destroy(weaponGate);
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
#endif
