#if UNITY_EDITOR
using System.Collections;
using AstralShift.HellMaiden.Combat.Hand;
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
    public sealed class MusicCombatEffectsPlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture enemies;
        private GameObject[] bootRoots;
        private GameObject weaponGate;
        private int drops;
        private NetworkIdentity Owner => NetworkClient.localPlayer;

        [UnityTest]
        public IEnumerator SpeedRefreshesWithoutStackingAndExpiryPreservesPermanentModifiers()
        {
            yield return Start();
            // Exercise the independent modifier directly, without replicated deadline writes from music.
            Owner.GetComponent<NetworkPlayerMusic>().enabled = false;
            var player = Owner.GetComponent<PlayerMovement>();
            var stats = player.PlayerStats;
            var permanent = new TestMoveSpeed { Bonus = .1f };
            stats.AddModifier(permanent);
            float baseline = stats.currentStats.moveSpeed;
            float expected = baseline + stats.baseStats.moveSpeed * .25f;
            var speed = Owner.GetComponent<MusicSpeedModifier>();
            Assert.That(speed, Is.Not.Null);
            double original = NetworkTime.time + .3;
            speed.Apply(.25f, original);
            Assert.That(stats.currentStats.moveSpeed, Is.EqualTo(expected).Within(.001f));
            for (int i = 0; i < 5; i++) speed.Apply(.25f, original);
            Assert.That(stats.currentStats.moveSpeed, Is.EqualTo(expected).Within(.001f));
            yield return new WaitForSecondsRealtime(.15f);
            double refreshed = NetworkTime.time + .5;
            speed.Apply(.25f, refreshed);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(NetworkTime.time, Is.GreaterThan(original));
            Assert.That(stats.currentStats.moveSpeed, Is.EqualTo(expected).Within(.001f), "Refresh extends one modifier.");
            yield return EnemyDefinitionRuntimeFixture.Wait(() => speed.Remaining == 0, "speed deadline", 3);
            yield return null;
            Assert.That(stats.currentStats.moveSpeed, Is.EqualTo(baseline).Within(.001f));
            speed.Apply(.25f, NetworkTime.time + 5);
            speed.Clear();
            speed.Clear();
            Assert.That(stats.currentStats.moveSpeed, Is.EqualTo(baseline).Within(.001f));
            stats.RemoveModifier(permanent);
        }

        [UnityTest]
        public IEnumerator AuthoritativePushMovesTheActualSimulatorAndLightningSpawnsEachKillDropOnce()
        {
            yield return Start();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => enemies.PairReady(), "live normal enemies");
            var combat = NetworkCombatWorld.Instance;
            var simulations = NetworkEnemySimulationWorld.Instance;
            var agents = enemies.Agents();
            Vector2 origin = Owner.transform.position;
            for (int i = 0; i < agents.Length; i++)
                Assert.That(simulations.RepositionReferenceEnemy(agents[i], origin + new Vector2(2 + i, .5f)), Is.True);
            yield return new WaitForSecondsRealtime(.15f);
            var target = agents[0];
            float before = Vector2.Distance(target.transform.position, origin);
            var p = MusicParameters.Defaults;
            p.PushRadius = 6;
            uint source = Owner.GetComponent<MirrorNetworkCombatBridge>().SourceEntityId;
            var push = combat.ServerApplyMusicEffect(Owner.netId, source, combat.Gateway.NextServerEventId(), MusicEffect.Push, p);
            Assert.That(push.AffectedCount, Is.EqualTo(2));
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Vector2.Distance(target.transform.position, origin) > before + .5f,
                "music impulse moves the assigned simulator", 3);
            yield return EnemyDefinitionRuntimeFixture.Wait(() =>
                simulations.Registry.TryGetLatestSnapshot(target.netId, out var snapshot) &&
                snapshot.Runtime.LastHandledKnockbackId > 0 && Vector2.Distance(snapshot.Position, origin) > before + .3f,
                "music displacement reaches an authoritative simulator snapshot", 3);

            p.LightningRadius = 20;
            p.LightningTargets = 1;
            p.LightningDamage = 1;
            var lightning = combat.ServerApplyMusicEffect(Owner.netId, source, combat.Gateway.NextServerEventId(), MusicEffect.Lightning, p);
            Assert.That(lightning.AffectedCount, Is.EqualTo(1), "Random lightning chooses distinct targets up to its cap.");
            long killsBefore = combat.Gateway.Metrics.ConfirmedKills;
            drops = 0;
            PickupAudit.Recorded += ObserveDrop;
            p.FinaleRadius = 20;
            p.FinaleDamage = 10000;
            ulong eventId = combat.Gateway.NextServerEventId();
            var finale = combat.ServerApplyMusicEffect(Owner.netId, source, eventId, MusicEffect.Finale, p);
            Assert.That(finale.AffectedCount, Is.EqualTo(2));
            Assert.That(combat.Gateway.Metrics.ConfirmedKills, Is.EqualTo(killsBefore + 2));
            yield return EnemyDefinitionRuntimeFixture.Wait(() => drops >= 2, "canonical music kill pickup spawn", 5);
            int initialDrops = drops;
            Assert.That(combat.ServerApplyMusicEffect(Owner.netId, source, eventId, MusicEffect.Finale, p).AffectedCount, Is.Zero);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(drops, Is.EqualTo(initialDrops), "Duplicate music effect must not produce another pickup.");
            Assert.That(combat.Gateway.Metrics.ConfirmedKills, Is.EqualTo(killsBefore + 2));
        }

        private void ObserveDrop(string kind, string run, ulong id, string detail)
        { if (kind == "spawn") drops++; }

        private IEnumerator Start()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
            if (!Application.isBatchMode) UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");
            GameOptionsService.EnsureInitialized();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => GameLocalization.IsReady, "localization startup");
            yield return null;
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
            bootRoots = BootSceneFixtureObjects.Capture(boot);
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true);
            enemies = new EnemyDefinitionRuntimeFixture(manager);
            weaponGate = new GameObject("Music effects isolated-test weapon gate");
            weaponGate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "preparation");
            manager.SetOwnLoadout(6);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Members[0].WeaponId == 6, "loadout");
            manager.StartPreparedGame();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame && Owner != null, "gameplay");
            var settings = GluttonyParameters.Defaults;
            settings.PassiveEnabled = false;
            NetworkCombatWorld.Instance.ServerConfigureGluttony(settings, true);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => Owner.GetComponent<NetworkPlayerPrototypeAbilities>().OwnerReady, "owner baseline");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            PickupAudit.Recorded -= ObserveDrop;
            if (manager != null)
            {
                manager.LeavePreparationRoom();
                yield return EnemyDefinitionRuntimeFixture.Wait(() => !NetworkClient.active && !NetworkServer.active &&
                    !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "music effects cleanup");
            }
            enemies?.Dispose();
            if (weaponGate != null) Object.Destroy(weaponGate);
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }

        private sealed class TestMoveSpeed : PlayerPerkModifier
        {
            public float Bonus;
            public override void Apply(PlayerStats.PlayerStatsMultipliers multipliers) => multipliers.moveSpeedMultiplier += Bonus;
            public override bool TryStack(RuntimePerkModifier other) => false;
        }
    }
}
#endif
