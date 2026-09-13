using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class OrdinaryKnockbackProcessProbe
    {
        private bool validateHitFlash;
        private readonly HashSet<ulong> flashIds = new HashSet<ulong>();
        private static readonly string[] FlashDotPhases =
            { "burn", "poison", "bleed", "burn-delayed", "poison-duplicate", "bleed-expired", "takeover" };

        private void RecordFlash(EnemyHitPresentation hit)
        {
            if (enemy == null || hit.TargetEntityId != enemy.netId) return;
            Require(flashIds.Add(hit.DamageEventId), "A predicted hit was flashed twice after its echo.");
            var renderer = Controller.GetComponentInChildren<SpriteRenderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Require(block.GetFloat("_HitEffectBlend") == 1 && block.GetColor("_HitEffectColor") == Color.white,
                "Flash notification did not set the real SpriteRenderer's white effect.");
            Debug.Log($"[EnemyHitFlashProcess] flash role={role} event={hit.DamageEventId} target={hit.TargetEntityId} version={hit.TargetStateVersion}");
        }

        private IEnumerator ServerFlashDots()
        {
            var world = NetworkCombatWorld.Instance;
            var gateway = world.Gateway;
            foreach (string phase in FlashDotPhases)
            {
                var expected = new List<ulong>();
                int directHits = 0;
                long rejectedDamageBefore = gateway.Metrics.ReceivedCombatResults - gateway.Metrics.AcceptedCombatResults;
                gateway.Ledger.TryGetState(enemy.netId, out var before);
                Action<CanonicalWorldBatch> observe = batch =>
                {
                    if (batch.EnemyHitPresentations != null)
                        expected.AddRange(batch.EnemyHitPresentations.Where(h => h.TargetEntityId == enemy.netId).Select(h => h.DamageEventId));
                };
                Action<CombatResult, CombatApplyResult, double> damage = (result, applied, time) =>
                {
                    if (result.TargetEntityId == enemy.netId && !ServerStatusDamageAdmissions.IsPeriodic(result)) directHits++;
                };
                world.ServerCanonicalBatchProduced += observe;
                gateway.CombatResultAccepted += damage;
                Mark("flash-fire-" + phase);
                while (!Has("flash-applied-" + phase)) yield return null;
                if (phase == "takeover")
                {
                    while (!gateway.Statuses.GetAllStates().Any(s => s.TargetEntityId == enemy.netId && !s.Removed)) yield return null;
                    Mark("flash-disconnect");
                    while (!Has("flash-disconnected")) yield return null;
                }
                else while (!Has("flash-attacker-done-" + phase)) yield return null;
                while (gateway.Statuses.GetAllStates().Any(s => s.TargetEntityId == enemy.netId && !s.Removed)) yield return null;
                yield return new WaitForSeconds(.6f);
                world.ServerCanonicalBatchProduced -= observe;
                gateway.CombatResultAccepted -= damage;
                gateway.Ledger.TryGetState(enemy.netId, out var after);
                int dotHits = expected.Count - directHits;
                Require(gateway.Metrics.ReceivedCombatResults - gateway.Metrics.AcceptedCombatResults == rejectedDamageBefore,
                    "DOT generated an extra or invalid predicted tick.");
                Require(dotHits == (phase == "takeover" ? 4 : 3) && expected.Distinct().Count() == expected.Count,
                    "Missing or duplicate accepted DOT edges.");
                Require(after.Health == before.Health - directHits * 12 - dotHits * 3, "DOT presentation changed canonical damage.");
                Mark("flash-expected-" + phase, JsonUtility.ToJson(new FlashObservation { ids = expected.ToArray(), health = after.Health }));
                if (phase == "takeover") Mark("flash-reconnect");
                while (!Has("flash-checked-" + phase + "-client") || !Has("flash-checked-" + phase + "-" + Second)) yield return null;
                Debug.Log($"[EnemyHitFlashProcess] phase={phase} direct={directHits} dot={dotHits} hp={after.Health} PASS");
                Mark("flash-complete-" + phase);
            }
        }

        private IEnumerator ClientFlashDots()
        {
            foreach (string phase in FlashDotPhases)
            {
                while (!Has("flash-fire-" + phase)) yield return null;
                flashIds.Clear();
                numberIds.Clear();
                var predicted = new HashSet<ulong>();
                var tickIds = new HashSet<string>();
                var combatant = Controller.GetComponent<CombatantBehaviour>();
                var world = NetworkCombatWorld.Instance;
                bool inject = phase.Contains("-");
                bool delivered = false;
                CanonicalStatusState? held = null;
                float confirmationDue = float.PositiveInfinity;
                Action<CanonicalWorldBatch> holdConfirmation = batch =>
                {
                    if (!inject || role != "client" || held.HasValue || batch.Statuses == null) return;
                    foreach (var state in batch.Statuses)
                        if (state.TargetEntityId == enemy.netId && !state.Removed)
                        {
                            held = state;
                            confirmationDue = Time.realtimeSinceStartup + (phase == "bleed-expired" ? 1.1f : .35f);
                            break;
                        }
                };
                Action<StatusTick, MonsterSupergroup.GAS.DamageInfo> localTick = (tick, applied) =>
                {
                    Require(applied.Value == 3, "DOT local damage changed.");
                    Require(tickIds.Add($"{tick.Instance.InstanceId.Value}:{tick.Instance.ApplicationRevision}:{tick.TickIndex}"),
                        "The same DOT application tick damaged locally twice.");
                };
                combatant.StatusDamageReceived += localTick;
                world.CanonicalBatchReceived += holdConfirmation;
                var collector = NetworkClient.localPlayer.GetComponent<MirrorNetworkCombatBridge>().Collector;
                Action<CombatEvent> predictedDamage = damage =>
                {
                    if (enemy == null || damage.Context.TargetEntityId != enemy.netId || damage.PredictedAppliedDamage.Value <= 0) return;
                    predicted.Add(damage.Context.EventId.Value);
                    Require(flashIds.Contains(damage.Context.EventId.Value), "Local DOT flash waited for server confirmation.");
                };
                collector.DamageResolved += predictedDamage;
                Mark("flash-view-ready-" + phase + "-" + role);
                while (!Has("flash-view-ready-" + phase + "-client") || !Has("flash-view-ready-" + phase + "-" + Second)) yield return null;
                if (role == "client")
                {
                    var weapon = (CirclingAttackBehaviour)NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;
                    var owner = NetworkClient.localPlayer;
                    bool applied = false;
                    Action<NativeGasHit, CombatResolution> apply = (hit, resolution) =>
                    {
                        if (applied) return;
                        applied = true;
                        var status = phase.StartsWith("burn") ? EnemyStatusID.Burn : phase.StartsWith("bleed") ? EnemyStatusID.Bleed : EnemyStatusID.Poison;
                        if (inject) world.Replica.UnregisterStatusController(enemy.netId, combatant.StatusController);
                        combatant.ApplyStatus(new StatusApplication(new StatusDefinition(status, StatusStackMode.Add, 20),
                            3, phase == "takeover" ? 4 : 3, phase == "takeover" ? 1f : .3f, 10,
                            owner.netId, sourcePlayerId: owner.netId, sourceEntityId: owner.netId,
                            targetEntityId: enemy.netId, executionAuthority: StatusExecutionAuthority.SourceClient,
                            sourceContext: resolution.DamageContext));
                        Mark("flash-applied-" + phase);
                    };
                    Controller.NativeHitKnockbackRequested += apply;
                    weapon.baseSpeed = 0;
                    KeepContact(weapon); weapon.Attack();
                    float until = Time.time + weapon.GetAttackSequenceDuration() + 2f;
                    while (Time.time < until && !(phase == "takeover" && Has("flash-disconnect")))
                    {
                        if (weapon.ActiveOrbCount > 0) KeepContact(weapon);
                        if (inject && !delivered && held.HasValue && Time.realtimeSinceStartup >= confirmationDue)
                        {
                            Require(tickIds.Count >= 1, "Confirmation was not delayed past the first local tick.");
                            if (phase == "bleed-expired") Require(tickIds.Count == 3 && !combatant.StatusController.Has(EnemyStatusID.Bleed),
                                "Post-expiry confirmation must arrive after all three local ticks have completed.");
                            var confirmation = held.Value.ToStatusInstance();
                            combatant.StatusController.UpsertCanonical(confirmation);
                            combatant.StatusController.UpsertCanonical(confirmation);
                            world.Replica.RegisterStatusController(enemy.netId, combatant.StatusController);
                            delivered = true;
                            Debug.Log($"[EnemyHitFlashProcess] injected={phase} localTicks={tickIds.Count} revision={confirmation.ApplicationRevision}");
                        }
                        yield return null;
                    }
                    Controller.NativeHitKnockbackRequested -= apply;
                    Require(applied, "DOT fixture must originate from a real admitted Native hit.");
                    Require(!inject || delivered, "Directed late/duplicate status confirmation did not run.");
                    if (phase == "takeover")
                    {
                        while (!Has("flash-disconnect")) yield return null;
                        manager.StopClient();
                        while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
                        Mark("flash-disconnected");
                        while (!Has("flash-reconnect")) yield return null;
                        flashIds.Clear();
                        numberIds.Clear();
                        manager.StartClient();
                        yield return AwaitOwner(); yield return AwaitEnemy();
                        yield return new WaitForSeconds(.5f);
                        Require(flashIds.Count == 0, "Reconnect snapshot replayed old flashes.");
                        if (validateDamageNumbers) Require(numberIds.Count == 0, "Reconnect snapshot replayed old numbers.");
                    }
                    else Mark("flash-attacker-done-" + phase);
                }
                else MovePlayer(Controller.hurtBox.GetPosition() + Vector2.left * 3);
                while (!Has("flash-expected-" + phase)) yield return null;
                var expected = JsonUtility.FromJson<FlashObservation>(Read("flash-expected-" + phase));
                collector.DamageResolved -= predictedDamage;
                if (combatant != null) combatant.StatusDamageReceived -= localTick;
                world.CanonicalBatchReceived -= holdConfirmation;
                if (!(phase == "takeover" && role == "client"))
                {
                    float deadline = Time.realtimeSinceStartup + 3;
                    while ((flashIds.Count < expected.ids.Length || Controller.CurrentHealth != expected.health) && Time.realtimeSinceStartup < deadline) yield return null;
                    var presented = new HashSet<ulong>(expected.ids);
                    Require(predicted.IsSubsetOf(presented), "Local prediction produced damage that the server did not confirm.");
                    Require(flashIds.SetEquals(presented), $"{phase} viewer {role}: expected {presented.Count} flashes, got {flashIds.Count}.");
                    if (validateDamageNumbers) Require(numberIds.SetEquals(presented), $"{phase} viewer {role}: expected {presented.Count} numbers, got {numberIds.Count}.");
                    if (phase != "takeover") Require(tickIds.Count == (role == "client" ? 3 : 0),
                        $"{phase} viewer {role}: expected three source ticks and no observer ticks, got {tickIds.Count}.");
                    Require(Controller.CurrentHealth == expected.health, "DOT health did not converge.");
                }
                Debug.Log($"[EnemyHitFlashProcess] phase={phase} viewer={role} flashes={flashIds.Count} accepted={expected.ids.Length} localPredictions={predicted.Count} PASS");
                if (validateDamageNumbers) Debug.Log($"[EnemyDamageNumbersProcess] phase={phase} viewer={role} numbers={numberIds.Count} accepted={expected.ids.Length} PASS");
                Mark("flash-checked-" + phase + "-" + role);
                while (!Has("flash-complete-" + phase)) yield return null;
            }
        }

        [Serializable] private sealed class FlashObservation { public ulong[] ids; public int health; }
    }
}
