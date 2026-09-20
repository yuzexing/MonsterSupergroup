using System.Collections;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class GameplayExperienceProcessProbe
    {
        private bool pickups;
        private bool disconnectAfterHeal, healedOffline;
        private void RecordPickup(string kind, string run, ulong drop, string detail)
        {
            Debug.Log($"[PickupProcess] {kind} run={run} drop={drop} {detail}");
            if (role != "client" || !disconnectAfterHeal || kind != "owner-result" || !detail.Contains("restored=200;")) return;
            disconnectAfterHeal = false;
            // Cut the actual connection between RestoreHealth and the following bridge Flush.
            service.Stop();
            healedOffline = true;
        }
        private NetworkExperienceGem SpawnHealth() => (NetworkExperienceGem)typeof(NetworkExperienceWorld)
            .GetMethod("SpawnPickup", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(World,
                new object[] { PickupEffect.RestoreHealth, 200f, Vector2.zero, Players()[0].gameObject.scene });
        private void Health(uint id, int health)
        {
            var world = NetworkCombatWorld.Instance;
            var saved = world.Gateway.Ledger.CaptureEntityState(id);
            var state = saved.State; state.Health = health; state.Alive = health > 0; state.StateVersion++;
            // Keep this fixture's protection in the same published snapshot. Toggling it back on next
            // frame silently advanced the ledger version and rejected an otherwise valid owner receipt.
            world.RestorePlayerState(id, new PlayerRuntimeCheckpoint { PreviousAvatarId = id,
                Health = new ServerEntityCheckpoint(state, saved.AbsoluteInvulnerable) });
            Debug.Log($"[PickupProcess] injected-health id={id} health={health} version={state.StateVersion} protected={saved.AbsoluteInvulnerable}");
        }
        private int Health(uint id) => NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(id, out var state) ? state.Health : -1;
        private IEnumerator PickupServerScenario()
        {
            StartRole(role); Mark("listening");
            yield return Wait(() => Has("p-ready-client") && Has("p-ready-" + Other), "pickup party");
            foreach (var p in Players()) Health(p.netId, 200);
            yield return new WaitForSecondsRealtime(.3f);
            var bottle = SpawnHealth(); ulong firstDrop = bottle.DropId;
            Mark("p-race", firstDrop.ToString());
            yield return Wait(() => World.HealthCount == 0, "shared bottle receipt");
            Require(Players().Sum(p => Health(p.netId)) == 600, "Competition healed more or less than one player.");
            Require(World.PendingHealthCount == 0, "Committed bottle retained authorization.");
            Mark("p-race-done");
            yield return Wait(() => Has("p-raced-client") && Has("p-raced-" + Other), "race acknowledgments");
            uint client = uint.Parse(Read("p-ready-client"));
            Health(client, 200);
            bottle = SpawnHealth();
            Require(bottle.DropId != firstDrop && World.EntityPoolHits > 0, "Network pool not reused.");
            Mark("p-disconnect-drop", bottle.DropId.ToString());
            yield return Wait(() => World.PendingHealthCount == 1, "client reserved");
            Mark("p-disconnect-now");
            yield return Wait(() => Has("p-disconnected") && World.PendingHealthCount == 0, "disconnect releases flight");
            Require(World.HealthCount == 1 && !bottle.Claimed, "Uncommitted bottle was lost.");
            Mark("p-resume");
            yield return Wait(() => Has("p-rejoined"), "client baseline restored");
            client = uint.Parse(Read("p-rejoined"));
            Require(Health(client) == 200, "Disconnect before receipt must not save healing.");
            Mark("p-recollect", bottle.DropId.ToString());
            yield return Wait(() => Has("p-healed-offline") && World.PendingHealthCount == 0, "disconnect after local heal before receipt");
            Require(World.HealthCount == 1 && !bottle.Claimed, "Unsubmitted healing consumed the bottle.");
            Mark("p-healed-resume");
            yield return Wait(() => Has("p-healed-rejoined"), "unsubmitted healing baseline");
            client = uint.Parse(Read("p-healed-rejoined"));
            Require(Health(client) == 200, "Unsubmitted local healing survived reconnect.");
            Mark("p-healed-recollect");
            yield return Wait(() => World.HealthCount == 0, "reconnected claim");
            Require(Health(client) == 400, "Reconnected player did not receive exactly 200.");
            Mark("p-commit-done");
            yield return Wait(() => Has("p-committed-disconnected"), "disconnect after commit");
            Mark("p-commit-resume");
            yield return Wait(() => Has("p-committed-rejoined"), "committed baseline");
            client = uint.Parse(Read("p-committed-rejoined"));
            Require(Health(client) == 400 && World.HealthCount == 0, "Committed health or consumed bottle replayed on reconnect.");
            Debug.Log($"[PickupProcess] pool created={World.EntitiesCreated} hits={World.EntityPoolHits}; receipts and reconnect passed");
            Mark("p-finish");
            yield return Wait(() => Has("p-finished-client") && Has("p-finished-" + Other), "pickup clients finished");
            service.Stop(); yield return Wait(CanRestart, "pickup stop");
            Require(FindObjectsByType<NetworkExperienceGem>(FindObjectsSortMode.None).Length == 0, "Stop leaked pickup entities.");
            StartRole(role); yield return Wait(() => World != null && World.CanGrant(out _), "fresh pickup world");
            Require(World.UnclaimedCount == 0 && World.PendingHealthCount == 0, "Restart retained claims.");
            service.Stop(); yield return Wait(CanRestart, "final pickup stop");
        }
        private IEnumerator PickupClientScenario()
        {
            yield return Wait(() => Has("listening"), "pickup listening");
            if (role != "host") StartRole("client");
            yield return Wait(OwnerReady, "pickup owner baseline");
            holdPosition = Vector2.zero;
            Mark("p-ready-" + role, Owner.netId.ToString());
            yield return Wait(() => Has("p-race"), "pickup race");
            ulong id = ulong.Parse(Read("p-race"));
            yield return Wait(() => NetworkExperienceGem.ClientGems.Any(x => x.DropId == id), "bottle spawn");
            yield return new WaitForSecondsRealtime(.25f);
            Owner.GetComponent<NetworkExperienceCollector>().enabled = true;
            Owner.GetComponent<NetworkExperienceCollector>().RequestCollection(World.RunId, id);
            yield return Wait(() => Has("p-race-done"), "race result");
            Mark("p-raced-" + role);
            if (role == "client")
            {
                yield return Wait(() => Has("p-disconnect-drop"), "disconnect bottle");
                id = ulong.Parse(Read("p-disconnect-drop"));
                yield return Wait(() => NetworkExperienceGem.ClientGems.Any(x => x.DropId == id), "disconnect spawn");
                Owner.GetComponent<NetworkExperienceCollector>().enabled = true;
                Owner.GetComponent<NetworkExperienceCollector>().RequestCollection(World.RunId, id);
                yield return Wait(() => Has("p-disconnect-now"), "disconnect before arrival");
                service.Stop(); yield return Wait(CanRestart, "disconnect cleanup"); Mark("p-disconnected");
                yield return Wait(() => Has("p-resume"), "resume"); StartRole("client"); yield return Wait(OwnerReady, "restored owner");
                Mark("p-rejoined", Owner.netId.ToString());
                yield return Wait(() => Has("p-recollect"), "recollect");
                disconnectAfterHeal = true;
                Owner.GetComponent<NetworkExperienceCollector>().enabled = true;
                Owner.GetComponent<NetworkExperienceCollector>().RequestCollection(World.RunId, id);
                yield return Wait(() => healedOffline && CanRestart(), "unsubmitted heal disconnect cleanup");
                Mark("p-healed-offline");
                yield return Wait(() => Has("p-healed-resume"), "unsubmitted heal resume");
                StartRole("client"); yield return Wait(OwnerReady, "unsubmitted heal baseline");
                Require(Owner.GetComponent<CombatantBehaviour>().CurrentHealth == 200, "Owner kept unsubmitted healing.");
                Mark("p-healed-rejoined", Owner.netId.ToString());
                yield return Wait(() => Has("p-healed-recollect"), "final recollect");
                Owner.GetComponent<NetworkExperienceCollector>().enabled = true;
                Owner.GetComponent<NetworkExperienceCollector>().RequestCollection(World.RunId, id);
                yield return Wait(() => Has("p-commit-done"), "committed health");
                service.Stop(); yield return Wait(CanRestart, "committed disconnect"); Mark("p-committed-disconnected");
                yield return Wait(() => Has("p-commit-resume"), "committed resume");
                StartRole("client"); yield return Wait(OwnerReady, "committed baseline");
                Require(Owner.GetComponent<CombatantBehaviour>().CurrentHealth == 400, "Owner restored wrong committed health.");
                Mark("p-committed-rejoined", Owner.netId.ToString());
            }
            yield return Wait(() => Has("p-finish"), "pickup finish");
            Mark("p-finished-" + role);
            if (role != "host") { service.Stop(); yield return Wait(CanRestart, "client final stop"); }
        }
    }
}
