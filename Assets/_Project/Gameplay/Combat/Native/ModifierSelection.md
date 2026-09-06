# Equipment modifier selection

Boot's `RuntimeDB.EquipmentDB` references `NativeGasEquipmentDB`, which contains
the eight canonical, already migrated numeric EquipmentData assets. No legacy
RuntimeDB initialization or new modifier-ID conversion is needed.

`NetworkPlayerBootstrap` binds `ModifierSelectionController` only after the
owned player's initial build starts. Each new initial weapon gets one round of
three uniformly sampled, distinct eligible cards at level index 0. The provider
checks native registrations, exact parameter types and weapon support without
instantiating modifiers. Dependencies and multi-slot offer eligibility are outside
this first pool; existing AddEquipment targeting semantics are unchanged.

The controller retains only the current offer and binding state. Selection calls
`PlayerBuildRuntime.AddEquipment(initialWeapon, card, levelIndex)`; the build
continues to own all runtime instances, handles, rollback and removal. A card may
contain multiple modifiers (the Knockback card also grants Speed).

## Input / future menu contract

- Read `Offers` immediately when binding a view; subscribe to `OffersChanged`
  and unsubscribe when the view unbinds/disables.
- Each offer includes `OfferId`, `Equipment`, `EquipmentId`, `LevelIndex` and
  read-only `Modifiers` (native stable ID, Parameters, application metadata).
- Reuse `Equipment.GetTitle()`, `Equipment.GetDescription((uint)LevelIndex)`
  and `Equipment.VisualDataReference` for presentation. Treat definition and
  parameter references as read-only; they are shared authored assets.
- Call `SelectOffer(offer.OfferId)` from a menu button. The ID belongs to this
  controller's offer lifetime; it is not a GAS Modifier ID or a persisted ID.
- `Select(index)` is the equivalent current-round index API. Both return
  `ModifierSelectionResult`: `Succeeded`, `EquipmentHandle`, `Error`.
- Successful application clears the round. Failures leave a still-valid round
  open. Expired offers and repeated choices fail without applying anything.
- Unbind invalidates offers; removal of already applied equipment remains the
  build owner's responsibility. Build replacement is also checked before input.

`DebugModifierSelectionInput` logs the cards and sends number-row 1/2/3 to
Select(0/1/2), only in a focused Editor or Development Build window. It can be removed when a
menu takes over; candidate generation does not depend on that adapter.

## Validation

Run `ModifierSelectionTests` and `DanteNativeGasRuntimeTests` in PlayMode.
The opt-in `ModifierSelectionProcessProbe` uses actual Boot and Gameplay scenes,
verifies owner isolation and reconnect, and submits native hit results through
the existing Mirror collector/gateway path. In an IncludeTestAssemblies build,
pass `--modifier-selection-role=host` / `client`; add
`--modifier-selection-keyboard` to verify real number-row input (Host 1, Client 3,
reconnected Client 2); click the fixture's `Begin key test` button before each
key check. `Tools/Run-ModifierSelectionProcessValidation.ps1` runs the automatic
pair; add `-Keyboard` for interactive verification. The fixture uses KCP port 7893 and disables enemy spawning
in memory to keep validation deterministic. It is absent from ordinary builds.

Validation on 2026-09-06: 19 selection/Dante PlayMode cases passed. The automatic
Host/Client process run (Logs/ModifierSelectionProcess/20260906-201453) passed,
including native network damage, reconnect and scene unload, with clean exits.
The real keyboard run (20260906-201130) passed all gameplay assertions for keys
1, 3 and 2. Its Client then crashed during native application shutdown; the stack
includes UnityPlayer / AppUINativePlugin window procedures, after scene unload
and the PASS marker. The runner correctly reports that nonzero process exit as
a failure. This native shutdown issue remains outside the selection migration.
