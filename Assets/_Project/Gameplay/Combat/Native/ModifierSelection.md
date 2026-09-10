# Server-authoritative upgrade selection

> M4 extends the formal flow to weapons, two-stage Equipment targets and Perks.
> The current configuration, authority/checkpoint contract and validation steps are in
> [M4 upgrade selection](../../../../../docs/plans/m4-upgrade-selection.md).
> The Equipment-only description below records the earlier implementation and its original validation.

The Gameplay `CombatUI.prefab` has a local `CardPickMenu` with up to three choices.
It uses the existing Equipment definitions and the existing native GAS Build. No source artwork,
card definitions, runtime modifier mapping, global menu controller or pause flow was imported.

## Reference and repository audit

The reference was `F:/DecomplieLatest/HellMaiden/ExportedProject/Assets/GameObject/CombatUI.prefab`,
its `Card Pick Menu` object and the related `Leveler`, `CombatUIManager`,
`UICardPickMenuView`, `CardPickMenuController`, `UIEquipmentCardViewHandler`,
`PlayerHandSlotView` and card pool flow. The old sequence is:

`Leveler queue → ProcessLevelUp → GameEvents.ShowOfferingsScreen → CombatUIManager`
`→ UICardPickMenuView.Open → CardPool.GetCardsDrop → card visuals/input handler`
`→ PickCard / Equip → PlayerHandSlotView.AddEquipment → Close → Leveler.EvalLevelUp`.

Views and input handlers are presentation. Pools, eligibility, levels and equipment
application are gameplay. Its controller globally pauses; that behavior is not reused.

In MonsterSupergroup, `PlayerBuildRuntime` is the active native Build. `PlayerHand`
is a legacy singleton and native `PlayerHandSlot` mutation explicitly directs callers
to the owning Build. `PlayerLoader` belongs to the old scene-loading path. There is
no `PlayerHandBehaviour` class in this repository. NetworkPlayer is initialized through
`NetworkPlayerBootstrap`, not the legacy hand/loader. The old `Leveler` is not attached
and its XP curve is not configured for the network player.

Boot already references `NativeGasEquipmentDB` (eight converted cards) and
`NativeGasWeaponDB` (Dante, already owned). Therefore this configured pool has
Equipment additions/upgrades; it has no unowned configured weapon candidate.
`LegacyEquipmentModifierConverter` remains the only legacy ID conversion, in the
Editor. No runtime conversion and no complete `RuntimeDB.Init()` call were added.

## Authoritative flow

`Server confirmed enemy kill → existing enemy.stats.XP → NetworkModifierSelection`
`→ per-player XP / level / pending count → EquipmentModifierOfferProvider.Generate(build)`
`→ TargetRpc with event identity + one to three option/card IDs, levels and target slots`
`→ Owner resolves local EquipmentDB → ModifierSelectionController → CardPickMenu`
`→ Select(index) / SelectOffer(optionId) → Command(eventId, index)`
`→ owner / event / index / current Build eligibility validation`
`→ PlayerBuildRuntime.AddEquipment or UpgradeEquipment`
`→ owner-targeted Build state → menu close or next queued offer`.

The scene is a minimal smoke-test scene with one 2-XP enemy per player. Its
NetworkPlayer threshold is **2 XP per level**, serialized as `experiencePerLevel`;
this makes the production kill-to-upgrade entry reachable. A full progression curve
and shared/collectible XP rules are not migrated. Authoritative gameplay can also call
`ServerGrantExperience(amount)` or `ServerQueueUpgrades(count)`. No client XP command exists.

Pending candidates, event sequence and pending count are instance fields on each
player's `NetworkModifierSelection`. Event IDs contain the player netId and sequence;
option IDs are also scoped to that player and round. There is no static pending offer.
Only that player's connection receives candidate definitions by ID and Build results.
Mirror Commands enforce authority, and the apply method additionally verifies the
connection's player identity, pending event, index and current eligibility. A consumed
round is cleared before acknowledgement or advancing the queue. Invalid, stale,
duplicate, cross-player and no-longer-eligible requests cannot add equipment.

Both Host and remote players use the same Command and server apply method. The server
has one existing `PlayerBuildRuntime` per player. Remote server Builds have automatic
weapon execution disabled but retain their native modifier/runtime state. The remote
Owner reconciles the server's ordered slot/card/level state through the same Build APIs.
The Host shares its server Build and skips replica application, avoiding double adds.
Other observers do not instantiate an execution Build or receive the player's options.

## Candidate and Build rules

The provider samples up to three distinct valid cards uniformly. It checks the configured
DB, duplicate card IDs, native registrations, exact parameter types, weapon support,
initial-weapon slot, capacity and the next authored equipment level. Owned cards offer
the next level; max-level cards do not recur. Dependencies and multi-slot definitions
are excluded from this initial configured pool rather than bypassing their rules.
The existing eight-card pool has no such entries. Candidate generation creates no
runtime modifiers. A combination card keeps every authored modifier.

An upgrade replaces the previous absolute level (damage bonus 0.3 → 0.5 → 1.0), rather
than stacking complete levels. The Build stages the replacement, rolls back failed
creation, consumes the previous handle, and preserves capacity and application order.
The native factory, generated registry, modifier instances and CombatPipeline remain
unchanged.

The confirmed finite-pool rule is: show three options when available, or all remaining
valid options when only one or two remain. Unused menu buttons are hidden and cannot
submit a selection. No duplicate, invalid option or fallback reward is invented.
If no valid choice remains, retain the pending count, keep the player unlocked and
log a diagnostic once. Eligibility is retried while waiting; a later Build/data change
can make the round available. With a fully exhausted pool, progression can remain
pending indefinitely. This is the approved exception to the original exactly-three rule.

## Presentation and temporary input API

`ModifierSelectionController` contains presentation state and input intent only.
`Bind(build)` does not generate or apply offers. The network adapter supplies one to
three definitions and the submission callback with `ReceiveOffers`.

A view subscribes to `OffersChanged` and then calls `NotifyPresentationReady()` once
it is bound and can accept input. This sets `IsPresentationReady` and raises
`PresentationReady`; the network adapter reports readiness to the server. Before the
actual menu binds, earned upgrades remain pending and the player stays unlocked.
When unbinding or disabling, the view calls `CancelOffer()`, which clears readiness
and raises `CancelRequested`, then removes its subscription. Cancellation closes the
current offer and releases its restrictions without applying or removing Equipment.
The default `CardPickMenu` performs these calls in `Bind` / `Unbind`; a replacement
view should use the same contract.

Views read `Offers`, subscribe to `OffersChanged`, call `Select(index)` or
`SelectOffer(offer.OfferId)`, and unsubscribe on disable/unbind. `IsRequestPending`
disables buttons until an authoritative response. `ModifierSelectionResult.Succeeded`
means the request was submitted; its equipment handle is not a remote application
receipt. The server acknowledgement updates the Build and clears/advances the offers.
Rejection retains a valid menu and allows retry. `LastError` exposes rejection text.

Names use the existing `Equipment.GetTitle()` and fall back to `Equipment.Title`
when localization returns blank text. Description and visual references remain
available on the shared definition for future presentation. The
minimal menu uses only existing TMP font/material, solid Images and three Buttons.
`LocalPlayerUIBinder` resolves the local owned player and binds the menu independently
of the HP HUD. No menu GameObjects or visuals are networked. The existing development
keyboard adapter calls the same interface; removing it does not affect offers or UI.

## Selection restrictions and lifecycle

`PlayerMovement.SetUpgradeSelectionLocked` stops local input, physics movement, dash,
ultimate and interaction. Weapons check the same flag before starting attacks. Its
Rigidbody constraints and independent immunity state are restored on exit.
`CombatantBehaviour` blocks direct/status damage while selection immunity is active.
Other immunity sources are preserved.

The server ledger separately tracks the selecting flag, rejects that player's new
combat/status submissions, protects owner health reports, and sends canonical health
corrections. Rejected selection-time events cannot be replayed after unlock. The
projectile presentation adapter rejects new spawn edges while allowing termination.
`NetworkPlayerTransformReliable` retains Mirror's protocol but discards incoming and
buffered owner positions while locked. Existing statuses, enemies and other players
continue; no global pause or timeScale changes were introduced. In-flight hit reports
arriving from the selecting player are also rejected during the restriction.

Accepted selection, interruption, lost ownership, disconnect, Build replacement,
component disable and scene destruction clear pending UI/lock state appropriately.
Re-enabling the network adapter or presentation facade requests current Owner state
with the actual `IsPresentationReady` value. It cannot reopen an offer while the menu
is still unavailable. Upgrades earned during that interval remain pending; after the
view binds and signals readiness, the server resumes offering them. Consecutive earned
levels use one pending count and one visible menu at a time.

The HP subtree is retained. Regression testing exposed an existing enabled animation
flag with no animation component/clips; the prefab now uses its existing color-blink
path. Its minimum fill is zero so death displays an empty bar. HP tests separately
verify immediate state/subscriptions and animated top/bottom convergence.

## Validation and follow-up

Current evidence and acceptance audit are recorded in `UpgradeSelectionValidation.md`.
Build the opt-in two-process fixture with
`MonsterSupergroup.Gameplay.Tests.ModifierSelectionValidationBuild.Build`; it includes
real Boot/Gameplay scenes and the existing Mirror KCP development backend. The default
output is under the Editor project used for the build (the test clone in this session).
Pass that executable to `Tools/Run-ModifierSelectionProcessValidation.ps1`.

The automatic fixture invokes real menu Buttons and authoritative RPCs. `-Keyboard`
opens two windows and waits for the fixture's `Begin input test` button before each
manual choice. The fixture is present only in IncludeTestAssemblies builds. Its
initial waiting-player protection and high-health background enemies are test-only;
a separate production enemy is killed through native GAS to test confirmed-kill XP.

Steamworks.NET/FizzySteamworks backend selection and combat transport were left intact.
A final two-account Steam lobby test still requires two signed-in Steam clients; KCP
uses the same NetworkBehaviours, Commands, TargetRpcs and authority checks.
