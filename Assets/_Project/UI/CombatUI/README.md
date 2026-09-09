# Owned-player HP HUD

`CombatUI.prefab` retains the migrated HellMaiden health sprites and text layout.
Its root component is `GameplayUIRoot`; it contains no `CombatUIManager` or networked UI.
The original `OverflowBar`, top/bottom fills, blink layer, materials and animation
references remain presentation components. Their cached values describe display
transitions, not gameplay truth.

## Binding and lifetime

```text
NetworkClient.localPlayer (active client, isOwned)
  -> CombatantBehaviour
  -> LocalPlayerUIBinder
  -> CombatHUDController
  -> PlayerHealthHUD
```

- Gameplay's `UILoader` uses `GameplayUILoader`, a MonoBehaviour whose `Start`
  creates the configured prefab once and calls `GameplayUIRoot.Initialize()`.
  The instance is parented to the loader and therefore belongs to Gameplay even
  before Mirror makes that additive scene active. It is destroyed with the scene.
  The old `SceneUILoader` is not on this object: its `LoadAsync()` had no caller
  in the Mirror scene flow, and assigning its prefab did not instantiate any UI.
- `LocalPlayerUIBinder` lives on the root in the NetworkCombat assembly. It checks
  the local-player/runtime reference in `LateUpdate`, without scene-wide searches
  or health polling. Either UI/player creation order, player replacement, authority
  loss, disconnect, disabled players and rebuilt Combatants are handled there.
  OnEnable hides/unbinds only; the first bind waits for LateUpdate so all child
  `StatusBar.Awake` calls finish before applying the real health snapshot.
- `CombatHUDController` organizes modules and implements Bind/Unbind/Show/Hide.
  Hide changes only CanvasGroup visibility; it keeps data subscriptions active.
  Disable/Unbind clears the binding. An enabled binder rebinds the current player
  on its next update, so disable the binder when intentionally detaching the UI.
- `PlayerHealthHUD` reads the current snapshot when bound and subscribes directly
  to `CombatantBehaviour.HealthChanged(current, maximum)`. Damage, healing,
  maximum-health changes, initialization and canonical correction all use this
  event. The first snapshot uses `OverflowBar.SetValueImmediate`; subsequent events
  use `SetMaxValue` and `StatusChange`. Both top/bottom fills update, while the
  original animation options remain available. Numbers use runtime integers.
  Unbind/disable cancels fill, blink and overflow transitions via `ClearPresentation`.
- Rebinding/unbinding unsubscribes the previous instance. Disabling the module
  unsubscribes; enabling it reads a fresh snapshot. Destroying the UI cleans up
  subscriptions even when the player remains alive. With no owned player the HUD
  is hidden, its fill is zero, and both numeric fields are empty. This is a runtime
  state only: the saved prefab has alpha 1, full fill and `100 / 100` for editing.

## Enemy Debug list (M1)

`NetworkEnemyDebugPanel` is attached once to the same production UI root. Editor and
Development clients show it in the upper right; F3/the header toggles it. It reads
Mirror's spawned enemy agents and the existing canonical replica at 5 Hz, labels
local predicted HP separately, and displays canonical GAS stack counts and simulation
assignment/role/epoch. Missing data is explicitly unavailable. A destroyed enemy's
canonical death snapshot is retained for two seconds without retaining its GameObject.
Selection temporarily hides the list body, preserving the user's expanded preference;
the panel never changes selection, movement locks, health, Build or network state.
Disable, disconnect, world replacement and scene unload release subscriptions and caches.
Dedicated servers and non-Development players do not run the panel.

M1 acceptance evidence and manual steps: `docs/plans/boot-gameplay-network-combat.md`.

## Next migrations

Add XP/Level, Dash and Ultimate as separate presentation modules under the HUD,
bound to their respective gameplay runtimes. Extend the controller/binder only
when those runtime contracts are needed. Minimap takes World/Session data.
Card/Perk/Stats/EndScreen menus belong alongside the HUD under GameplayUIRoot.
Do not introduce GameEvents, GameDirector.Player or a UI singleton into this path.

## Verification

Run PlayMode fixtures `PlayerHealthHUDTests` and `GameplayHealthHUDLoadingTests`.
They cover the production prefab, canonical MaxHP-only changes, death/reset,
subscription and animation cleanup, Mirror Host player replacement, and loading
the actual Gameplay scene without manually creating UI. The scene test also
checks additive scene ownership, single creation and unload cleanup.

For a repeatable two-process test, build with
`-executeMethod MonsterSupergroup.Gameplay.Tests.HealthHUDValidationBuild.Build`
and run `Tools/Run-HealthHUDProcessValidation.ps1`. The build uses the existing
Boot/Gameplay scenes without regenerating them, enables KCP, and includes test
assemblies. The opt-in `HealthHUDProcessProbe` checks Host `71/151`, Client
`129/263`, then Client reconnect with a new player and `83/233`; it verifies
the binding, numbers, both fills, visibility, instance count and scene cleanup.
Enemy spawning is disabled in memory for this HP presentation test so unrelated
combat cannot change the expected values. Normal builds omit the test assembly.
