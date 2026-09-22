using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PrototypeAbilityInputTests
    {
        private GameObject playerObject;
        private PlayerMovement player;

        [SetUp]
        public void SetUp()
        {
            playerObject = new GameObject("Prototype input owner");
            playerObject.SetActive(false);
            player = playerObject.AddComponent<PlayerMovement>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(playerObject);

        [Test]
        public void InputIsBoundToEachPlayerAndOldTeardownPreservesNewBinding()
        {
            int firstCalls = 0, secondCalls = 0, actionCalls = 0;
            Func<PrototypeAbilityId, bool> first = _ => { firstCalls++; return true; };
            Func<PrototypeAbilityId, bool> second = _ => { secondCalls++; return true; };
            Func<PrototypeAbilityAction, bool> firstAction = _ => false;
            Func<PrototypeAbilityAction, bool> secondAction = _ => { actionCalls++; return true; };
            player.BindPrototypeInput(first, firstAction);
            player.BindPrototypeInput(second, secondAction);
            player.UnbindPrototypeInput(first, firstAction);
            Assert.That(player.SelectPrototypeAbility(PrototypeAbilityId.Music), Is.True);
            Assert.That(player.PrototypeAction(PrototypeAbilityAction.Secondary), Is.True);
            Assert.That(firstCalls, Is.Zero);
            Assert.That(secondCalls, Is.EqualTo(1));
            Assert.That(actionCalls, Is.EqualTo(1));
            player.UnbindPrototypeInput(second, secondAction);
            Assert.That(player.SelectPrototypeAbility(PrototypeAbilityId.Allure), Is.False);
            Assert.That(player.PrototypeAction(PrototypeAbilityAction.Secondary), Is.False);
        }

        [Test]
        public void PrimaryDispatcherNeverFallsThroughToGluttonyOnRejection()
        {
            int legacyCalls = 0, dispatchedCalls = 0;
            player.BindGluttonyInput(() => { legacyCalls++; return true; });
            player.BindPrototypeInput(_ => true, action =>
            {
                Assert.That(action, Is.EqualTo(PrototypeAbilityAction.Primary));
                dispatchedCalls++;
                return false;
            });
            player.GluttonyAction();
            Assert.That(dispatchedCalls, Is.EqualTo(1));
            Assert.That(legacyCalls, Is.Zero, "Music/Allure rejection must not invoke Gluttony.");
        }

        [Test]
        public void LegacyPrimaryBindingRemainsUsableWithoutSelector()
        {
            int calls = 0;
            player.BindGluttonyInput(() => { calls++; return true; });
            player.GluttonyAction();
            Assert.That(player.PrototypeAction(PrototypeAbilityAction.Secondary), Is.False);
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase("menu")]
        [TestCase("upgrade")]
        [TestCase("loading")]
        [TestCase("remote")]
        public void BlockedOwnerCannotSwitchOrUsePrototype(string reason)
        {
            int calls = 0;
            player.BindPrototypeInput(_ => { calls++; return true; }, _ => { calls++; return true; });
            switch (reason)
            {
                case "menu": player.SetMenuInputBlocked(true); break;
                case "upgrade": player.SetUpgradeSelectionLocked(true); break;
                case "loading": player.SetRunLoadingLocked(true); break;
                case "remote": typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.UsesNetworkLifecycle))
                    .SetValue(player, true); break;
            }
            Assert.That(player.SelectPrototypeAbility(PrototypeAbilityId.Music), Is.False);
            Assert.That(player.PrototypeAction(PrototypeAbilityAction.Primary), Is.False);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ClosingUpgradeBlocksPrototypeForTheRestOfTheFrameOnlyForThatOwner()
        {
            var selection = playerObject.AddComponent<ModifierSelectionController>();
            // Seed a visible offer without constructing a weapon build; this test exercises close/input arbitration.
            typeof(ModifierSelectionController).GetProperty(nameof(ModifierSelectionController.Offers))
                .SetValue(selection, new ModifierOffer[1]);
            player.BindPrototypeInput(_ => true, _ => true);
            Assert.That(player.SelectPrototypeAbility(PrototypeAbilityId.Music), Is.False);
            selection.ClearOffers();
            Assert.That(selection.Offers, Is.Empty);
            Assert.That(selection.BlocksPrototypeInputThisFrame, Is.True);
            Assert.That(selection.TryConsumeNumberKey(1), Is.True);
            Assert.That(player.SelectPrototypeAbility(PrototypeAbilityId.Music), Is.False);
            Assert.That(player.PrototypeAction(PrototypeAbilityAction.Primary), Is.False);

            var otherObject = new GameObject("Other prototype input owner");
            otherObject.SetActive(false);
            try
            {
                var other = otherObject.AddComponent<PlayerMovement>();
                other.BindPrototypeInput(_ => true, _ => true);
                Assert.That(other.SelectPrototypeAbility(PrototypeAbilityId.Music), Is.True);
            }
            finally { Object.DestroyImmediate(otherObject); }
        }

        [Test]
        public void InvalidOrPendingUpgradeNumberIsConsumedInsteadOfSelectingAbility()
        {
            var selection = playerObject.AddComponent<ModifierSelectionController>();
            typeof(ModifierSelectionController).GetProperty(nameof(ModifierSelectionController.Offers))
                .SetValue(selection, new ModifierOffer[1]);
            Assert.That(selection.TryConsumeNumberKey(2), Is.True);
            selection.ClearOffers();
            Assert.That(selection.TryConsumeNumberKey(0), Is.True,
                "A second input delegate in the same frame must not fall through after the menu closes.");
        }

        [Test]
        public void EmptySelectionDoesNotConsumeNumberKey()
        {
            var selection = playerObject.AddComponent<ModifierSelectionController>();
            selection.ClearOffers();
            Assert.That(selection.TryConsumeNumberKey(1), Is.False);
        }

        [TestCase(63, "PrototypeSelectGluttony", 49)]
        [TestCase(64, "PrototypeSelectMusic", 50)]
        [TestCase(65, "PrototypeSelectAllure", 51)]
        [TestCase(66, "PrototypeSecondary", 116)]
        [TestCase(67, "PrototypeDecoy", 102)]
        [TestCase(68, "PrototypeRhythm", 32)]
        [TestCase(69, "UpgradeSelectFourth", 52)]
        [TestCase(5, "Button3", 114)]
        [TestCase(4, "Button2", 113)]
        [TestCase(14, "R_Trigger", 304)]
        [TestCase(0, "Button1", 101)]
        public void BootRewiredActionAndPhysicalKeyRemainConnected(int actionId, string name, int keyCode)
        {
            string scene = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Scenes/Boot.unity"));
            string actions = Regex.Match(scene, @"    actions:\r?\n(?<value>[\s\S]*?)    actionCategories:").Groups["value"].Value;
            Assert.That(Regex.Matches(actions, $@"    - _id: {actionId}\r?\n      _name: {name}\r?\n").Count,
                Is.EqualTo(1), "Action must exist once with its stable ID.");
            string keyboard = Regex.Match(scene, @"    keyboardMaps:\r?\n(?<value>[\s\S]*?)    mouseMaps:").Groups["value"].Value;
            var mappings = Regex.Matches(keyboard, @"      - _actionCategoryId:[\s\S]*?(?=      - _actionCategoryId:|\z)");
            int matched = 0;
            foreach (Match map in mappings)
                if (Regex.IsMatch(map.Value, $@"        _actionId: {actionId}\r?\n") &&
                    Regex.IsMatch(map.Value, $@"        _keyboardKeyCode: {keyCode}\r?\n")) matched++;
            Assert.That(matched, Is.EqualTo(1), "Expected physical key must dispatch the intended action once.");
            Assert.That((int)typeof(RewiredConsts.Action).GetField(name, BindingFlags.Public | BindingFlags.Static)
                .GetRawConstantValue(), Is.EqualTo(actionId));
        }
    }
}
