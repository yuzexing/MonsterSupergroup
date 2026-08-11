# GAS Unity Vertical Slice

Open `Assets/_Project/Scenes/Development/GASVerticalSlice.unity` and enter Play Mode.
The development overlay can attack or reset the enemy; automatic attacks are enabled by default.

Runtime flow:

1. `WeaponRuntimeBehaviour` converts the assigned authoring sets through `ModifierSetRuntimeLoader`.
2. `VerticalSliceCombatController` schedules attacks from the resolved weapon speed.
3. `CombatPipeline` applies direct damage and OnHit effects to `CombatantBehaviour`.
4. `StatusUpdateDriver` advances the combatant's pure C# `StatusController` with explicit delta time.
5. `CombatDebugPresenter` reads gameplay state without introducing UI dependencies into the gameplay assembly.

Regenerate the development assets and scene with:

`Tools/MonsterSupergroup/Gameplay/Rebuild GAS Vertical Slice`

The scene is intentionally excluded from product Build Settings. Core GAS tests remain independent of scenes,
GameObjects, UI, FMOD and Rewired.
