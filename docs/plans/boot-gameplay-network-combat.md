# Boot to Gameplay Multiplayer Combat Loop

## Goal

Starting from `Boot`, Host and Remote Client load `Gameplay` additively,
create exactly one `NetworkPlayer` per ready connection, spawn exactly one
`NetworkEnemySkeleton` per player, activate the owner-local Dante
`PlayerBuildRuntime`, and complete both canonical combat convergence loops.

1. Player -> Dante local projectile -> Enemy hurtbox -> `CombatResult` ->
   Server `CombatLedger` -> canonical Enemy HP.
2. Skeleton SimulationOwner melee -> local Player hitbox ->
   `PlayerCombatantBinding` -> `PlayerHealthReport` -> canonical Server Player
   HP.

## Required topology and authority

- `Boot` owns the Mirror manager and persistent combat/simulation worlds.
- `Gameplay` is additive content and owns player starts plus the product Enemy
  spawner; it does not own another manager or world.
- Mirror `NetworkIdentity` authority remains server-owned for Enemies.
- Gameplay `SimulationOwner` selects the one Client that runs each normal
  Enemy's movement and melee decisions.
- The attacking player's Client resolves Dante projectile hits immediately;
  the Server accepts `CombatResult` through the existing gateway and owns
  canonical Enemy HP/death.
- Each Player owner applies local incoming melee damage and reports final HP
  through the existing `PlayerHealthReport` path.
- Host and Remote Client each own an independent `PlayerBuildRuntime`.

## Non-goals

Do not expand this slice into Status ownership, Knockback/Pull, Boss simulation,
A*, network-spawned projectiles, `PlayerHand`, a parallel attack system, or
Legacy GAS damage/crit execution.

## Acceptance criteria

- Host and a late-joining Remote Client both enter additive `Gameplay`.
- One Player exists per connection and one Skeleton exists per Player.
- Each Skeleton targets and is simulated by its assigned Player owner.
- Both owner-local Dante builds damage their assigned Enemy and Server
  canonical Enemy HP converges.
- Skeleton melee damages each local Player and Server canonical Player HP
  converges through owner-final health reports.
- When the Remote Client disconnects, its Enemy remains, retains a cached
  snapshot, and changes to `ServerFallback` targeting the remaining Player.
- GAS EditMode, Gameplay PlayMode, NetworkCombat EditMode, and NetworkCombat
  Sandbox regressions pass.

