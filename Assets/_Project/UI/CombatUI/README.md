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

## Player Debug acceptance panel

`NetworkPlayerDebugPanel` is attached once to the production root, alongside the
existing HUD and Enemy Debug panel. It is enabled in **all graphical builds**,
including non-Development players. F2 or the header folds the panel. Its bounds
are bottom-left, at most 520 pixels wide and 60% of screen height, with scrolling.
Clients show their owned avatar and initially expand its details. Host shows the
whole run roster, sorted by stable ParticipantId; selecting a row shows one set
of details below all summaries. While the local card menu is open, only summaries
are displayed and the selected participant/expanded preference is retained.

- Host reads `RunSession.Participants`, the server's spawned avatars, Ledger HP,
  server Status registry, and each server Build/progression/skill runtime. Player
  HP is explicitly labeled as an **OwnerFinal report received by the server**.
  A graphical Host always uses this server lane, even for its own avatar.
- Clients read their owned runtime and the canonical replica separately. Effective
  status stacks can differ from canonical stacks, including an effect predicted
  down to zero. Missing baselines say unavailable instead of displaying zero.
- Rows contain HP/MaxHP, Alive/Downed, XP/level, pending rewards, selection stage
  and lock, status instances/timing/source/DOT progress, all four weapon slots,
  equipment levels (one-based), Perk rarity, Ultimate charge/active/protection
  deadlines, Dash charges/deadlines, current character and GAS weapon stats,
  avatar/connection/build versions and existing server attack rejections.
  Weapon runtime stats are not a prediction of final target-dependent damage.
- `PlayerProgressionDebugState` is an Owner-only reliable changed-value TargetRpc.
  It starts after the existing Owner readiness request and also reports queues
  that have no legal offer. Receiving it changes only the display cache. Folding
  or disabling the panel does not change this existing component's notification.
- The reader never advances GAS, starts attacks, initializes Builds/databases,
  or calls the mutating checkpoint APIs. `PlayerDashRuntime.ReadSnapshot` copies
  raw deadlines; the formatter computes remaining charges on that copy.
  `TryReadDebugCooldown` applies current cooldown modifiers to a value copy and
  neither refreshes server records nor binds weapons. Ultimate reads a value copy.
- Disconnected roster entries use only their retained checkpoint and explicitly
  freeze countdowns at CapturedAt. Unsaved character/weapon stats and live input
  state are unavailable. Reconnect reuses ParticipantId with a new AvatarId.
  Scene unload, disconnect, world/session replacement and disable clear UI caches.
  There is no static player registry or additional gameplay subscription.

Tests: `PlayerDebugSnapshotTests`, `NetworkPlayerDebugPanelTests`,
`PlayerWeaponCooldownRuntimeTests`, and `GameplayHealthHUDLoadingTests` cover
read-only behavior, canonical versus effective data, missing baselines, retained
queues without offers, offline samples, viewport bounds and production UI lifetime.

For actual Host + two client acceptance, build with
`-executeMethod MonsterSupergroup.Gameplay.Tests.PlayerDebugValidationBuild.Build`
and run `Tools/Run-PlayerDebugProcessValidation.ps1`. This uses the actual Boot,
Gameplay, player and UI assets, without regenerating scenes. The opt-in test probe
exercises distinct HP values, three queued upgrades, selection, Build changes,
Ultimate use, Dash, a downed player's disconnect/reconnect and UI cleanup. It saves
per-role text snapshots and frame captures under `Logs/PlayerDebug/Process-*`.
Test assemblies are included only in these validation builds.

The default output is `Builds/PlayerDebugDevelopment/PlayerDebug.exe`. Set the
build process environment variable `PLAYER_DEBUG_RELEASE=1` for the same probe in
`Builds/PlayerDebugRelease/PlayerDebug.exe` with Development disabled. Pass that
executable to the script with `-Executable`; use `-Width 1920 -Height 1080` for the
larger viewport (default 1280x720). KCP is enabled in these local validation builds.

Player Debug summaries retain the short player label (for example `P1`) and also
show explicit `ParticipantId` and `Avatar netId` fields. Offline rows label the
previous Avatar separately. Reconnect preserves ParticipantId and replaces netId.
Downed health blocks normal movement and directional input even when a newly
restored avatar's local movement FSM is still `Moving`; loading health does not
replay damage or death callbacks.

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
