using System.Collections;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class GameplayExperiencePlayModeTests
    {
        private NetworkExperienceGem HealthDrop() => (NetworkExperienceGem)typeof(NetworkExperienceWorld)
            .GetMethod("SpawnPickup", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(World,
                new object[] { PickupEffect.RestoreHealth, 200f, (Vector2)Owner.transform.position, Owner.gameObject.scene });

        [UnityTest]
        public IEnumerator XpIdleCacheOverflowNeverDeletesGroundReward_ReuseDoesNotKeepOldFlight()
        {
            yield return StartHost();
            var rules = (GameplayPickupRules)typeof(NetworkExperienceWorld).GetField("pickupRules", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(World);
            var asset = Object.Instantiate(rules.experienceItem);
            try
            {
                asset.idleCapacity = 1;
                typeof(NetworkExperienceWorld).GetField("xpDefinition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(World, asset.Capture());
                var spawn = typeof(NetworkExperienceWorld).GetMethod("SpawnPickup", BindingFlags.Instance | BindingFlags.NonPublic);
                var items = new NetworkExperienceGem[5];
                for (int i = 0; i < items.Length; i++) items[i] = (NetworkExperienceGem)spawn.Invoke(World,
                    new object[] { PickupEffect.Experience, .01f, (Vector2)Owner.transform.position, Owner.gameObject.scene });
                Assert.That(World.UnclaimedCount, Is.EqualTo(5), "Idle capacity must not cap ground XP.");
                float before = Progression.Experience;
                foreach (var item in items) Assert.That(Collect(item, out _), Is.True);
                Assert.That(Progression.Experience - before, Is.EqualTo(.1f).Within(.00001));
                yield return new WaitForSecondsRealtime(.5f);
                var reused = (NetworkExperienceGem)spawn.Invoke(World,
                    new object[] { PickupEffect.Experience, .01f, (Vector2)Owner.transform.position, Owner.gameObject.scene });
                Assert.That(World.EntityPoolHits, Is.GreaterThan(0));
                Assert.That(reused.Visual.gameObject.activeInHierarchy, Is.True);
                Assert.That(Object.FindObjectsByType<ExperienceCollectionFlight>(FindObjectsSortMode.None).Length, Is.Zero);
                Assert.That(World.UnclaimedCount, Is.EqualTo(1));
            }
            finally { Object.Destroy(asset); }
        }

        [UnityTest]
        public IEnumerator HealthPickup_StaleReceiptRetriesWithoutHealingTwice_AdjacentDamageCommits()
        {
            yield return StartHost();
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 200);
            bool changed = false;
            void InjectVersion(string kind, string run, ulong drop, string detail)
            {
                if (kind != "owner-result" || changed) return;
                changed = true;
                // Real gate changes can race an owner health report. Deliberately delay their snapshot.
                NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(Owner.netId, true);
            }
            PickupAudit.Recorded += InjectVersion;
            try
            {
                var bottle = HealthDrop(); Assert.That(Collect(bottle, out _), Is.True);
                yield return WaitFor(() => World.HealthCount == 0, "stale receipt retry");
                Assert.That(changed, Is.True);
                var binding = Owner.GetComponent<AstralShift.HellMaiden.Player.PlayerMovement>().CombatantBinding;
                Assert.That(binding.CurrentHealth, Is.EqualTo(400), "Retry is a receipt, never a second RestoreHealth.");
                GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 400);
                yield return null; yield return null;
                Assert.That(binding.ApplyDamage(30), Is.EqualTo(30), "Protection correction must reach the owner before this damage.");
                Owner.GetComponent<MirrorNetworkCombatBridge>().Flush();
                yield return WaitFor(() => NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(Owner.netId, out var s) && s.Health == 370, "adjacent damage");
            }
            finally { PickupAudit.Recorded -= InjectVersion; }
        }

        [UnityTest]
        public IEnumerator HealthPickup_LethalDamageBeforeReceiptDoesNotResurrectOrReturnConsumedBottle()
        {
            yield return StartHost();
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 200);
            yield return null; yield return null;
            bool damaged = false, committed = false;
            void Race(string kind, string run, ulong drop, string detail)
            {
                if (kind == "health-committed") committed = true;
                if (kind != "owner-result" || damaged) return;
                damaged = true;
                // Make the original heal report stale, then a newer legitimate lethal report.
                NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(Owner.netId, true);
                NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(Owner.netId, false);
                var binding = Owner.GetComponent<AstralShift.HellMaiden.Player.PlayerMovement>().CombatantBinding;
                Assert.That(binding.ApplyDamage(999), Is.EqualTo(400));
            }
            PickupAudit.Recorded += Race;
            try
            {
                Assert.That(Collect(HealthDrop(), out _), Is.True);
                yield return WaitFor(() => damaged, "lethal damage after healing");
                yield return null; yield return null;
                Assert.That(committed, Is.True, "A bottle already applied before the lethal hit must consume exactly once.");
                Assert.That(Owner.GetComponent<CombatantBehaviour>().CurrentHealth, Is.Zero, "Receipt rejection must not revive the owner.");
                Assert.That(NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(Owner.netId, out var state) && !state.Alive, Is.True);
            }
            finally { PickupAudit.Recorded -= Race; }
        }

        [UnityTest]
        public IEnumerator HealthPickup_ArrivesBeforeHealing_ClampsAndReusesSameNetworkObject()
        {
            yield return StartHost();
            var owner = Owner.GetComponent<CombatantBehaviour>();
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 200);
            var bottle = HealthDrop(); var firstId = bottle.DropId;
            Assert.That(Collect(bottle, out var reason), Is.True, reason);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(owner.CurrentHealth, Is.EqualTo(200), "No health before the .8 second flight ends.");
            yield return WaitFor(() => World.HealthCount == 0, "health receipt commit");
            Assert.That(owner.CurrentHealth, Is.EqualTo(400));
            Assert.That(NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(Owner.netId, out var canonical), Is.True);
            Assert.That(canonical.Health, Is.EqualTo(400));
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 450);
            var second = HealthDrop();
            Assert.That(second, Is.SameAs(bottle), "Actual pooled identity, not a newly spawned replacement.");
            Assert.That(second.DropId, Is.GreaterThan(firstId));
            Assert.That(Collect(second, out reason), Is.True, reason);
            yield return WaitFor(() => World.HealthCount == 0, "clamped health receipt");
            Assert.That(owner.CurrentHealth, Is.EqualTo(500));
            Assert.That(World.EntityPoolHits, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator HealthPickup_FullBusyDeathAndFourSlotLimitPreserveUnconsumedBottle()
        {
            yield return StartHost();
            var bottle = HealthDrop();
            Assert.That(Collect(bottle, out var reason), Is.False); Assert.That(reason, Is.EqualTo("full-or-unready"));
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 200);
            Assert.That(Collect(bottle, out _), Is.True);
            for (int i = 0; i < 3; i++) Assert.That(HealthDrop(), Is.Not.Null);
            Assert.That(HealthDrop(), Is.Null, "In-flight bottles still occupy the four slots.");
            NetworkCombatWorld.Instance.Gateway.Ledger.SetPlayerUpgradeSelectionState(Owner.netId, true);
            yield return new WaitForSecondsRealtime(.9f);
            Assert.That(bottle.FlightElapsed, Is.LessThan(.3f));
            Assert.That(World.HealthCount, Is.EqualTo(4));
            NetworkCombatWorld.Instance.Gateway.Ledger.SetPlayerUpgradeSelectionState(Owner.netId, false);
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 500);
            yield return null;
            Assert.That(bottle.Claimed, Is.False); Assert.That(World.HealthCount, Is.EqualTo(4));
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 200);
            Assert.That(Collect(bottle, out _), Is.True);
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 0);
            yield return null;
            Assert.That(bottle.Claimed, Is.False); Assert.That(Owner.GetComponent<CombatantBehaviour>().CurrentHealth, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HealthPickup_PauseAndOldReceiptCannotSkipFlightOrDuplicateHealing()
        {
            yield return StartHost();
            typeof(NetworkEnemySimulationWorld).GetMethod("BeginReferenceClock", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(NetworkEnemySimulationWorld.Instance, null);
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 200);
            var bottle = HealthDrop(); Assert.That(Collect(bottle, out _), Is.True);
            Time.timeScale = 0;
            try
            {
                yield return new WaitForSecondsRealtime(.9f);
                Assert.That(bottle.FlightElapsed, Is.LessThan(.1f));
                Assert.That(Owner.GetComponent<CombatantBehaviour>().CurrentHealth, Is.EqualTo(200));
            }
            finally { Time.timeScale = 1; }
            uint version = bottle.ClaimVersion; ulong id = bottle.DropId;
            Assert.That(World.RejectHealthGrant(Owner.connectionToClient, "old-run", id, version), Is.False);
            Assert.That(World.RejectHealthGrant(Owner.connectionToClient, World.RunId, id, version + 1), Is.False);
            yield return WaitFor(() => World.HealthCount == 0, "unpause pickup");
            Assert.That(Owner.GetComponent<NetworkCombatantAdapter>().RestorePickupHealth(World.RunId, id, version, 200), Is.EqualTo(-1));
            Assert.That(Owner.GetComponent<CombatantBehaviour>().CurrentHealth, Is.EqualTo(400));
        }
    }
}
