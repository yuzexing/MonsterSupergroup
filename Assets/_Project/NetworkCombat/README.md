# Network Combat Integration

This folder adapts the existing `_Project/GAS` and Gameplay Combat runtime to
Mirror. It is not a second GAS implementation.

## Dependency boundary

- `MonsterSupergroup.GAS.Core` and `MonsterSupergroup.Gameplay.Combat.Runtime`
  do not reference Mirror.
- `MonsterSupergroup.NetworkCombat` owns Commands, RPCs, canonical ledgers,
  status replication and reconciliation.
- Projectiles remain local pooled GameObjects. They are never NetworkSpawned;
  Spawn/Termination presentation edges are replicated so observers can replay
  the same trajectory without running hit or damage logic.

## Authority

| State or operation | Executor/final authority |
| --- | --- |
| Player movement, attack, projectile, hit, crit and build trigger | Owner client |
| Enemy predicted HP and PredictedLethalHit | Each attacking owner client |
| Enemy canonical HP, Alive/Dead and ConfirmedKill | Server CombatLedger |
| Player HP | Owner-final report, stored by Server CombatLedger |
| Gameplay status Add/Remove/Stack/Duration/Version | Server StatusRegistry |
| SourceClient status gameplay and DOT | Source owner client |
| Stun/shared AI control | Server |
| Normal/Elite Enemy AI, movement and attack progression | Assigned SimulationOwner client |
| Boss simulation | Server |
| Enemy target assignment, spawn and canonical network destroy | Server |

The server accepts the resolved `Damage` and tags submitted by the owner. It
does not rerun attack stats, crit, projectile collision or player builds.

## Runtime flow

1. `PlayerBuildRuntime -> WeaponRuntimeBehaviour -> HellMaiden WeaponBehaviour`
   simulates immediately on the owner from the Native GAS weapon database.
2. `CombatPipeline` emits a raw `DamageResolved` plus local predicted damage and
   `PredictedLethalHit` when appropriate.
   OnHit/PredictedLethal modifiers use the player-build `CombatTriggerGuard`
   (depth 32, 256 triggers per root by default, self-trigger/once/cooldown rules).
3. `ClientCombatCollector` batches damage, status mutations and owner-final
   player health reports.
4. `MirrorNetworkCombatBridge` sends reliable Commands every 50 ms without
   blocking local combat.
5. `ServerCombatGateway` performs lightweight identity, target, numeric,
   authority and idempotency checks.
6. `CombatLedger` and `ServerStatusRegistry` update canonical shared facts.
7. `NetworkCombatWorld` broadcasts canonical batches. Clients reconcile future
   HP/status state without rolling back completed local build chains.

Source-client DOT uses the same path: `StatusController -> CombatantBehaviour ->
DamageResolved -> ClientCombatCollector -> ServerCombatGateway`. On source
disconnect, the server takes over only the remaining DOT ticks; it does not
rerun the source player's build logic.

## Validation sandbox

Open `Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity` and use the
Mirror HUD to start a Host or Client. The scene intentionally is not added to
the production Build Profile. It contains:

- KCP wrapped by Mirror `LatencySimulation` (100 ms each direction, 50 ms
  jitter, 5% unreliable loss);
- one `NetworkCombatWorld` and server spawner;
- the existing player gameplay stack adapted as `NetworkPlayer.prefab`;
- 120 server-spawned `NetworkEnemy.prefab` instances;
- local-only pooled projectiles.

`NetworkPlayer.prefab`, `NetworkEnemy.prefab`, `NetworkEnemyBase.prefab` and
`NetworkEnemySkeleton.prefab` are the canonical assets. Use `Monster
Supergroup/Network Combat/Build Validation Sandbox` to repair those prefabs in
place and regenerate only the validation scene; no LocalCombat source prefab is
copied over a production network prefab.

## Production scene hookup

Place exactly one `NetworkCombatWorld` scene object before spawning combatants.
Assign `NetworkPlayer.prefab` to the Mirror NetworkManager player prefab and
register `NetworkEnemy.prefab` as a spawn prefab. Replace the sandbox spawner
with the production server wave/spawn system; keep the gateway, adapters and
authority rules unchanged.
