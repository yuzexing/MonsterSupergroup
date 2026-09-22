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
    public sealed class MusicPrototypePlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture enemies;
        private GameObject[] bootRoots;
        private GameObject weaponGate;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkPlayerPrototypeAbilities Abilities => Owner.GetComponent<NetworkPlayerPrototypeAbilities>();
        private NetworkPlayerGluttony Gluttony => Owner.GetComponent<NetworkPlayerGluttony>();
        private NetworkPlayerMusic Music => Owner.GetComponent<NetworkPlayerMusic>();
        private PlayerMovement Player => Owner.GetComponent<PlayerMovement>();
        private NetworkModifierSelection Upgrades => Owner.GetComponent<NetworkModifierSelection>();

        [UnityTest]
        public IEnumerator FullPerformanceSurvivesSwitchAndDuplicateRequests_WithoutCooldownReset()
        {
            yield return Start(); yield return BeginMusic();
            Assert.That(Upgrades.OffersDeferred);
            ulong cast = Music.State.CastId;
            double previousX = Player.transform.position.x;
            Player.SetDirection(Vector2.right);
            for (int i = 0; i < 10; i++)
            {
                int beat = i;
                yield return EnemyDefinitionRuntimeFixture.Wait(() => Music.LocalElapsed >= Music.Parameters.BeatTime(beat), "DSP beat");
                double elapsed = Music.LocalElapsed;
                Assert.That(Music.RequestBeat(), Is.True,
                    $"first input at beat {i}, elapsed={elapsed:0.000}, ready={Abilities.OwnerReady}, judged={Music.State.JudgedMask}");
                Assert.That(Music.RequestBeat(), Is.False, "repeated local input");
                // Exercise Mirror command transport and server dedup, beyond the local mask.
                typeof(NetworkPlayerMusic).GetMethod("CmdBeat", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(Music, new object[] { cast, i, elapsed, null });
                if (i == 2) yield return Select(PrototypeAbilityId.Allure);
            }
            Player.SetDirection(Vector2.zero);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !Music.State.Active, "music settlement");
            Assert.That(Music.State.Hits, Is.EqualTo(10)); Assert.That(Music.State.Judged, Is.EqualTo(10));
            Assert.That(Upgrades.OffersDeferred, Is.False);
            Assert.That(Player.transform.position.x, Is.GreaterThan(previousX + .5f), "performance allows movement");
            double cooldown = Music.State.CooldownReadyAt;
            yield return FinishUpgrades();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.OwnerReady, "post music ready");
            yield return Select(PrototypeAbilityId.Music);
            Assert.That(Music.State.CooldownReadyAt, Is.EqualTo(cooldown));
            Assert.That(Music.RequestStart(), Is.False);
        }

        [UnityTest]
        public IEnumerator OmittedBeatsAndPartialMistakesContinueToEnd()
        {
            yield return Start(); yield return BeginMusic();
            // First beat deliberately early, then consume repeated input within the same slot.
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Music.LocalElapsed >= .8, "early first input");
            Assert.That(Music.RequestBeat(), Is.True);
            Assert.That(Music.RequestBeat(), Is.False);
            for (int i = 2; i < 10; i += 2)
            {
                int beat = i;
                yield return EnemyDefinitionRuntimeFixture.Wait(() => Music.LocalElapsed >= Music.Parameters.BeatTime(beat), "even beat");
                Assert.That(Music.RequestBeat());
            }
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !Music.State.Active, "remaining omissions settle", 8);
            Assert.That(Music.State.Hits, Is.EqualTo(4));
            Assert.That(Music.State.Judged, Is.EqualTo(10));
            Assert.That(Music.State.Cancelled, Is.False);
            Assert.That(Upgrades.OffersDeferred, Is.False);
        }

        [UnityTest]
        public IEnumerator AllMissesResumeMultipleQueuedUpgradesAutomatically()
        {
            yield return Start(); yield return BeginMusic();
            Upgrades.ServerGrantExperience(Upgrades.ExperienceRequiredAtLevel(1) + Upgrades.ExperienceRequiredAtLevel(2));
            Assert.That(Upgrades.Level, Is.EqualTo(3)); Assert.That(Upgrades.PendingUpgradeCount, Is.EqualTo(2));
            Assert.That(Upgrades.IsSelecting, Is.False); Assert.That(Player.IsUpgradeSelectionLocked, Is.False);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !Music.State.Active, "ten omissions", 10);
            Assert.That(Music.State.Hits, Is.Zero); Assert.That(Music.State.Judged, Is.EqualTo(10));
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Upgrades.IsSelecting, "auto-open queued upgrade");
            yield return FinishUpgrades(); Assert.That(Upgrades.Level, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator DisableInterruptsAndReleasesUpgradesWhilePreservingCooldown()
        {
            yield return Start(); yield return BeginMusic();
            Upgrades.ServerGrantExperience(Upgrades.ExperiencePerLevel);
            Assert.That(Upgrades.OffersDeferred); Assert.That(Upgrades.IsSelecting, Is.False);
            Abilities.ServerSetEnabled(false);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !Music.State.Active && !Music.IsLocalPerforming, "cancel music");
            Assert.That(Music.State.Cancelled); Assert.That(Music.SpeedRemaining, Is.Zero);
            Assert.That(Upgrades.OffersDeferred, Is.False);
            Assert.That(Upgrades.Level, Is.EqualTo(2));
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Upgrades.IsSelecting, "cancel auto resumes offer");
            double cooldown = Music.State.CooldownReadyAt;
            yield return FinishUpgrades(); Abilities.ServerSetEnabled(true);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.OwnerReady, "reenabled");
            Assert.That(Music.RequestStart(), Is.False);
            Assert.That(Music.State.CooldownReadyAt, Is.EqualTo(cooldown));
        }

        [UnityTest]
        public IEnumerator SwitchingBackDuringPerformanceCannotStartSecondCast()
        {
            yield return Start(); yield return BeginMusic(); ulong cast = Music.State.CastId;
            yield return Select(PrototypeAbilityId.Gluttony); yield return Select(PrototypeAbilityId.Music);
            Assert.That(Music.RequestStart(), Is.False); Assert.That(Music.State.CastId, Is.EqualTo(cast));
            Assert.That(Music.State.Active); Assert.That(Upgrades.OffersDeferred);
        }

        private IEnumerator BeginMusic()
        {
            // First-spawn asset initialization can stall a frame; start the timing test after the battle is ready.
            yield return EnemyDefinitionRuntimeFixture.Wait(() => enemies.PairReady(), "music battle warmup");
            yield return Select(PrototypeAbilityId.Music);
            Assert.That(Music.RequestStart(), Is.True);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Music.IsLocalPerforming && Music.State.Scheduled, "scheduled music");
        }

        [UnityTest]
        public IEnumerator DisabledUpgradeComponentInterruptsMusicAndKeepsEarnedRewards()
        {
            yield return Start(); yield return BeginMusic();
            Upgrades.ServerGrantExperience(Upgrades.ExperiencePerLevel);
            Upgrades.enabled = false;
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !Music.State.Active, "missing deferral cancels music");
            Assert.That(Music.State.Cancelled); Assert.That(Upgrades.PendingUpgradeCount, Is.EqualTo(1));
            Assert.That(Upgrades.OffersDeferred, Is.False); Assert.That(Music.SpeedRemaining, Is.Zero);
            Upgrades.enabled = true;
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Upgrades.IsSelecting, "retained reward reopens");
            yield return FinishUpgrades();
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

        private IEnumerator Select(PrototypeAbilityId ability)
        {
            Assert.That(Abilities.RequestSelect(ability), Is.True, "Switch request to " + ability);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Abilities.SelectedAbility == ability && !Abilities.SelectionPending,
                "acknowledged selection " + ability);
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
