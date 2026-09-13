#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Linq;
using Mirror;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.NetworkCombat;
using MonsterSupergroup.GAS;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameplayMenuUiScenario
    {
        public static IEnumerator Run(BootGameplayNetworkManager manager, Func<string, IEnumerator> shot, bool testCards)
        {
            var menu = UnityEngine.Object.FindFirstObjectByType<NetworkGameplayMenuController>();
            Check(menu != null, "Combat prefab has no ESC menu");
            var avatar = NetworkClient.localPlayer;
            var movement = avatar.GetComponent<PlayerMovement>();
            var combatant = avatar.GetComponent<CombatantBehaviour>();
            var build = avatar.GetComponent<PlayerBuildRuntime>();
            var selection = avatar.GetComponent<NetworkModifierSelection>();
            float scale = Time.timeScale; bool invulnerable = combatant.IsInvulnerable;
            Click("CombatMenu.Open"); yield return null;
            var characterScroll = GameObject.Find("Character statistics").GetComponent<ScrollRect>();
            Check(IsAtTop(characterScroll), "Initial character statistics were scrolled away: " + characterScroll.content.anchoredPosition);
            Check(menu.IsOpen && movement.IsMenuInputBlocked && GameplayMenuInput.IsOpen, "Menu did not acquire input");
            Check(EventSystem.current.currentSelectedGameObject.name == "CombatMenu.Continue", "Continue was not focused");
            var navigation = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down, moveVector = Vector2.down };
            ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, navigation, ExecuteEvents.moveHandler);
            Check(EventSystem.current.currentSelectedGameObject.name == "CombatMenu.Options", "Arrow navigation failed");
            ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Check(Time.timeScale == scale && combatant.IsInvulnerable == invulnerable && !movement.IsUpgradeSelectionLocked, "Menu changed combat rules");
            Check(GameOptionsPanel.IsOpen, "Options did not open from keyboard submit");
            Click("Options.Back"); yield return null;
            Check(menu.IsOpen && movement.IsMenuInputBlocked && !GameOptionsPanel.IsOpen, "Options did not return to the combat menu");
            Click("CombatMenu.Options"); yield return null;
            Check(GameOptionsPanel.IsOpen, "Options did not open from the combat button");
            Click("Options.Back"); yield return null;
            Click("CombatMenu.Feedback");
            var dash = movement.OwnerDashRuntime.ReadSnapshot();
            var buildState = JsonUtility.ToJson(build.CaptureState());
            float elapsed = build.InitialWeapon.LastAttackElapsedTime;
            for (int i = 0; i < 50; i++)
            {
                var snapshot = GameplayMenuSnapshotReader.Read(avatar, PreparationMenuCatalog.Load(), NetworkTime.time + i * 10);
                Check(snapshot.Health == combatant.CurrentHealth && snapshot.MaxHealth == combatant.MaxHealth, "HP differs from HUD runtime");
                Check(snapshot.Weapons[build.CaptureState().InitialWeaponSlot].Id == build.InitialWeaponId, "Wrong weapon");
                Check(snapshot.Weapons.Count(w => w.State == MenuDataState.Empty) == 3, "Empty slots are not distinguished");
            }
            Check(movement.OwnerDashRuntime.ReadSnapshot().RechargeReadyAt.SequenceEqual(dash.RechargeReadyAt), "Display advanced dash queue");
            Check(build.InitialWeapon.LastAttackElapsedTime == elapsed && JsonUtility.ToJson(build.CaptureState()) == buildState, "Display mutated weapon or Build");
            int uses = avatar.GetComponent<NetworkPlayerDash>().AcceptedUseCount;
            var position = movement.body.position;
            var aim = movement.attackDirection;
            for (int i = 0; i < 5; i++)
            {
                movement.SetDirection(Vector2.right); movement.SetAimDirection(Vector2.left); movement.Dash(); movement.UltimateAction(); movement.Interact();
                Check(!avatar.GetComponent<NetworkPlayerUltimate>().RequestUse(), "Menu allowed a new ultimate");
                yield return new WaitForFixedUpdate();
            }
            Check(Vector2.Distance(position, movement.body.position) < .01f && avatar.GetComponent<NetworkPlayerDash>().AcceptedUseCount == uses, "Menu input moved/dashed");
            Check(Time.timeScale == scale && build.InitialWeapon.enabled, "Combat execution was disabled");
            if (!invulnerable)
            {
                int health = combatant.CurrentHealth;
                combatant.ReceiveDamage(new DamageInfo(1, 7, false));
                Check(combatant.CurrentHealth < health, "Menu prevented ordinary damage");
                yield return new WaitForSecondsRealtime(.25f);
                Check(menu.Snapshot.Health == combatant.CurrentHealth, "Menu HP did not refresh after damage");
            }
            if (shot != null) yield return shot("combat-character");
            Click("CombatMenu.SpellTab"); Click("CombatMenu.Slot1");
            yield return null;
            var weaponScroll = GameObject.Find("Selected weapon").GetComponent<ScrollRect>();
            Check(IsAtTop(weaponScroll), "Initial spell statistics were scrolled away");
            // Font metrics can make this short sample fit without scrolling. Use a smaller test viewport
            // to exercise real scrolling instead of assuming the old OS font's preferred height.
            var originalViewportSize = weaponScroll.viewport.sizeDelta;
            weaponScroll.viewport.sizeDelta = new Vector2(originalViewportSize.x, 120);
            Canvas.ForceUpdateCanvases();
            weaponScroll.verticalNormalizedPosition = .4f;
            var beforeFocus = EventSystem.current.currentSelectedGameObject;
            yield return new WaitForSecondsRealtime(.25f);
            Check(EventSystem.current.currentSelectedGameObject == beforeFocus, "Statistics refresh stole keyboard focus");
            Check(Mathf.Abs(weaponScroll.verticalNormalizedPosition - .4f) < .01f, "Statistics refresh reset scroll");
            weaponScroll.verticalNormalizedPosition = 1;
            weaponScroll.viewport.sizeDelta = originalViewportSize;
            Canvas.ForceUpdateCanvases();
            if (shot != null) yield return shot("combat-spells");
            menu.RequestExit(); yield return null;
            Check(EventSystem.current.currentSelectedGameObject.name == "CombatMenu.CancelExit", "Confirm must default to Cancel");
            if (shot != null) yield return shot("combat-confirm");
            menu.HandleEscape(); yield return null;
            Check(menu.IsOpen && !menu.IsConfirming && NetworkClient.active, "Escape in confirmation left the run");
            menu.HandleEscape(); yield return null;
            Check(!menu.IsOpen && !movement.IsMenuInputBlocked, "Escape did not release input");
            menu.HandleEscape(); menu.HandleEscape(); yield return null;
            Check(menu.IsOpen && menu.ShowingSpells, "Repeated Escape toggled twice or lost page");
            menu.CloseMenu(); yield return null;

            if (testCards)
            {
                var cards = UnityEngine.Object.FindFirstObjectByType<CardPickMenu>();
                selection.ServerQueueUpgrades(1);
                yield return Wait(() => cards.IsOpen && cards.BoundSelection.Offers.Count > 0, "equipment offer");
                var offers = cards.BoundSelection.Offers.Select(o => o.OfferId).ToArray();
                menu.OpenMenu(); yield return null;
                Check(cards.IsOpen && cards.IsPresentationSuppressed, "Suppression discarded card binding");
                string originalLocale = GameLocalization.Language;
                var phase = cards.BoundSelection.Stage;
                var currentBuild = JsonUtility.ToJson(build.CaptureState());
                var selectedFocus = EventSystem.current.currentSelectedGameObject;
                GameOptionsService.Instance.SetLanguage(originalLocale == "en" ? "zh-CN" : "en");
                yield return Wait(() => GameLocalization.Language != originalLocale, "locale during card suppression");
                Check(menu.IsOpen && cards.IsPresentationSuppressed && cards.BoundSelection.Stage == phase &&
                    cards.BoundSelection.Offers.Select(o => o.OfferId).SequenceEqual(offers), "Locale changed the active card choice");
                Check(JsonUtility.ToJson(build.CaptureState()) == currentBuild && EventSystem.current.currentSelectedGameObject == selectedFocus,
                    "Locale changed build or menu focus");
                GameOptionsService.Instance.SetLanguage(originalLocale);
                yield return Wait(() => GameLocalization.Language == originalLocale, "restore card locale");
                foreach (var button in cards.GetComponentsInChildren<Button>()) button.onClick.Invoke();
                Check(cards.BoundSelection.Offers.Select(o => o.OfferId).SequenceEqual(offers), "Hidden card click submitted an offer");
                int previousLevel = selection.Level;
                selection.ServerGrantExperience(selection.ExperiencePerLevel);
                yield return null;
                Check(selection.Level > previousLevel, "XP could not advance while menu was open");
                if (shot != null) yield return shot("combat-over-cards");
                menu.CloseMenu(); yield return null;
                Check(!cards.IsPresentationSuppressed && cards.IsOpen && movement.IsUpgradeSelectionLocked, "Selection lock was lost");
                Check(cards.BoundSelection.SelectOffer(offers[0]).Succeeded, "Equipment selection failed");
                yield return Wait(() => cards.BoundSelection.Stage == UpgradeSelectionStage.EquipmentTarget, "equipment target");
                var targetIds = cards.BoundSelection.Offers.Select(o => o.OfferId).ToArray();
                menu.OpenMenu(); yield return null; menu.CloseMenu(); yield return null;
                Check(cards.BoundSelection.Stage == UpgradeSelectionStage.EquipmentTarget && cards.BoundSelection.Offers.Select(o => o.OfferId).SequenceEqual(targetIds), "Equipment target was reset");
                if (shot != null) yield return shot("combat-card-restored");
                int safety = 12;
                while (cards.IsOpen && safety-- > 0)
                {
                    if (!cards.BoundSelection.IsRequestPending && cards.BoundSelection.Offers.Count > 0)
                        cards.BoundSelection.SelectOffer(cards.BoundSelection.Offers[0].OfferId);
                    yield return new WaitForSecondsRealtime(.1f);
                }
                Check(!cards.IsOpen, "Queued choices did not drain after restoring presentation");
                menu.OpenMenu(); yield return new WaitForSecondsRealtime(.25f);
                Check(menu.Snapshot.Weapons.Any(w => w.Equipment.Any(e => e.Level >= 1)), "Equipment statistics missing");
                if (shot != null) yield return shot("combat-equipment");
                menu.CloseMenu();
            }
            menu.enabled = false; yield return null;
            Check(!movement.IsMenuInputBlocked && !GameplayMenuInput.IsOpen, "Disabled menu retained lock");
            menu.enabled = true; yield return null;
            Debug.Log("[CombatMenuValidation] owner-input, readonly-stats, focus, confirmation, reopen" + (testCards ? ", cards-and-equipment" : "") + " PASS");
        }
        private static bool IsAtTop(ScrollRect scroll) => scroll.content.rect.height <= scroll.viewport.rect.height + .01f
            ? Mathf.Abs(scroll.content.anchoredPosition.y) < .1f : scroll.verticalNormalizedPosition > .99f;
        public static void Click(string name)
        {
            var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name);
            Check(button != null && button.IsInteractable(), "Missing usable button " + name); button.onClick.Invoke();
        }
        public static IEnumerator Wait(Func<bool> condition, string stage)
        {
            float until = Time.realtimeSinceStartup + 35;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Check(condition(), "Timed out: " + stage);
        }
        public static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    }
}
#endif
